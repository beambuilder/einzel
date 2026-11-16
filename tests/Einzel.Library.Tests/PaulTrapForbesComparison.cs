using System.Globalization;
using Einzel.Analysis;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The single-ion, collision-free comparison of Forbes et al. (1999), run on the
/// <c>paul-trap</c> template.
/// </summary>
/// <remarks>
/// <para>
/// Forbes, Sharifi, Croley, Lausevic and March, "Simulation of ion trajectories in a
/// quadrupole ion trap: a comparison of three simulation programs", J. Mass Spectrom. 34,
/// 1219-1239 (1999), flew one ion through the ideal trap with three independent codes -
/// ITSIM and ISIS evaluating the ideal quadrupole directly, SIMION refining a potential
/// array - and tabulated what each got (their tables 1 and 2). The conditions: r0 = 10 mm,
/// z0 = 7.071 mm, 1.1 MHz, phase zero, q_z = 0.400, a = 0, no gas, m/z 100, starting at
/// 0.5 mm on every axis with velocity (0, 200, -100) m/s in (x, y, z), z the axis of
/// rotation, for 25 microseconds. Their equation for the ring potential carries the drive
/// as <c>U - V cos(Omega t + phi)</c>; the template drives the ring at <c>+V cos(Omega t)</c>,
/// so the comparison shifts the field by half a drive period to match.
/// </para>
/// <para>
/// The three codes agreed on the axial secular frequency to a tenth of a kilohertz (161.1,
/// 161.1, 161.0 kHz) and the paper states the theoretical value as 160.91142 kHz while
/// describing the codes as within 0.01 percent of it, which they are not: 161.1 is 0.12
/// percent above 160.911. The difference is the paper's, recorded here rather than resolved.
/// </para>
/// </remarks>
public sealed class PaulTrapForbesComparison(ITestOutputHelper output)
{
    private const double R0Mm = 10.0;
    private const double DriveMHz = 1.1;
    private const double Qz = 0.400;

    private static CompiledModel Model(params (string Name, double Value)[] extra)
    {
        // q_z = 4 e V / (m Omega^2 r0^2) for the ideal r0^2 = 2 z0^2 trap.
        var mass = 100.0 * 1.66053906660e-27;
        var omega = 2.0 * Math.PI * DriveMHz * 1e6;
        var volts = Qz * mass * omega * omega * R0Mm * 1e-3 * R0Mm * 1e-3 / (4.0 * 1.602176634e-19);

        (string, double)[] scaled =
        [
            ("inscribedRadius", R0Mm), ("driveFrequency", DriveMHz), ("rfAmplitude", volts),

            // The shipped trap scaled from r0 = 4 mm to 10 mm whole: gap, ring and endcap
            // thickness by 2.5 as well, so the truncation is the shipped one rather than a
            // ring 4 mm deep on a 10 mm trap.
            ("electrodeGap", 1.5), ("ringThickness", 10.0), ("endcapThickness", 3.75),
        ];

        var document = PaulTrapHoles.Trap(scaled.Concat(extra).ToArray());

        return PaulTrapHoles.Compile(document with
        {
            Ion = document.Ion! with { MassToCharge = document.Ion!.MassToCharge! with { Value = 100.0 } },
        });
    }

    private sealed record Run(
        double ZMin, double ZMax, double RMin, double RMax, double EkMinEv, double EkMaxEv,
        double AxialKHz, double RadialKHz, double Volts, double R0EffMm);

    /// <summary>
    /// Flies the paper's ion once: the extremes from its first 25 microseconds, which is what
    /// table 2 tabulates at 10 ns output, and - when asked - the secular lines from the whole
    /// millisecond, as the paper's figure 13 used. One integration, not two: the first 25
    /// microseconds of the long flight are the short one.
    /// </summary>
    private static Run Fly(CompiledModel model, double phaseShiftPeriods, bool lines)
    {
        var drive = model.Parameters["driveFrequency"].SiValue;
        var field = (ITimeVaryingField)FieldAssembly.Build(model);
        ITimeVaryingField shifted = phaseShiftPeriods == 0.0
            ? field
            : new TimeShiftedField(field, phaseShiftPeriods / drive);

        var species = IonSpecies.FromModel(model);

        // Paper (x, y, z) with z axial; here x is axial: (z, x, y).
        var launch = new PhaseState(
            new Vec3(0.5e-3, 0.5e-3, 0.5e-3),
            new Vec3(-100.0, 0.0, 200.0));

        var point = model.DetectorPoint;
        var normal = model.DetectorNormal;

        TrajectoryStopFunction detector =
            (in PhaseState state) => Vec3.Dot(state.Position - point, normal);

        const double Window = 25e-6;
        var record = new TrajectoryRecorder(10e-9);

        var result = TrajectoryIntegrator.Integrate(
            launch, species, shifted,
            new IntegrationSettings { RelativeTolerance = 1e-10, MaximumFlightTime = lines ? 1e-3 : Window },
            detector, record);

        // The whole flight is held: an ion that left would make both the extremes and the
        // spectrum describe something else.
        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, result.Outcome);

        double zMin = double.MaxValue, zMax = double.MinValue, rMin = double.MaxValue, rMax = 0.0;
        double ekMin = double.MaxValue, ekMax = 0.0;

        foreach (var s in record.Samples.Where(s => s.TimeSeconds <= Window * (1.0 + 1e-12)))
        {
            var p = s.Position;
            var v = s.Velocity;
            var r = Math.Sqrt((p.Y * p.Y) + (p.Z * p.Z));
            var ek = 0.5 * species.MassSi * Vec3.Dot(v, v) / 1.602176634e-19;

            zMin = Math.Min(zMin, p.X);
            zMax = Math.Max(zMax, p.X);
            rMin = Math.Min(rMin, r);
            rMax = Math.Max(rMax, r);
            ekMin = Math.Min(ekMin, ek);
            ekMax = Math.Max(ekMax, ek);
        }

        double axial = double.NaN, radial = double.NaN;

        if (lines)
        {
            // Every tenth sample, 100 ns apart: the line search needs the millisecond, not the
            // 10 ns cadence the extremes are read at.
            var thinned = new List<TrajectorySample>();
            var next = 0.0;

            foreach (var s in record.Samples)
            {
                if (s.TimeSeconds >= next)
                {
                    thinned.Add(s);
                    next = s.TimeSeconds + (100e-9 * (1.0 - 1e-6));
                }
            }

            double LineKHz(int axis)
            {
                var spectrum = SecularSpectrum.From(thinned, axis, 0.02 * drive, 0.45 * drive, 20000);
                var peak = spectrum.Peak();

                Assert.NotNull(peak);

                var (value, _, _, _) = peak;

                return value.In("Hz") / 1e3;
            }

            axial = LineKHz(0);
            radial = LineKHz(2);
        }

        var volts = model.Parameters["rfAmplitude"].SiValue;

        return new Run(
            zMin * 1e3, zMax * 1e3, rMin * 1e3, rMax * 1e3, ekMin, ekMax,
            axial, radial, volts, PaulTrapHoles.EffectiveRadius(field, volts, 0.8e-3) * 1e3);
    }

    /// <summary>
    /// The paper's conditions in the ideal quadrupole field itself, integrated here by fixed-step
    /// fourth-order Runge-Kutta: the reference all three codes were meant to reproduce.
    /// </summary>
    /// <remarks>
    /// <c>phi = Phi0(t) (r^2 - 2 z^2) / (2 r0^2)</c> plus a constant, with the ring at
    /// <c>Phi0(t) = sign V cos(Omega t)</c>, so <c>z'' = 2 e Phi0 z / (m r0^2)</c> and
    /// <c>x'' = -e Phi0 x / (m r0^2)</c>. Written here, not shipped: it is the closed form the
    /// engine is compared against.
    /// </remarks>
    private static (double ZMin, double ZMax, double RMax, double EkMaxEv) Ideal(double sign, double volts)
    {
        var mass = 100.0 * 1.66053906660e-27;
        const double Charge = 1.602176634e-19;
        var r0 = R0Mm * 1e-3;
        var omega = 2.0 * Math.PI * DriveMHz * 1e6;

        double[] Derivative(double t, double[] s)
        {
            var phi0 = sign * volts * Math.Cos(omega * t);
            var k = Charge * phi0 / (mass * r0 * r0);

            // s = z, x, y, vz, vx, vy
            return [s[3], s[4], s[5], 2.0 * k * s[0], -k * s[1], -k * s[2]];
        }

        double[] state = [0.5e-3, 0.5e-3, 0.5e-3, -100.0, 0.0, 200.0];
        const double Dt = 0.5e-9;
        double zMin = double.MaxValue, zMax = double.MinValue, rMax = 0.0, ekMax = 0.0;

        // The starting state is a state of the flight, and the extremes are over all of it.
        void Sample()
        {
            zMin = Math.Min(zMin, state[0]);
            zMax = Math.Max(zMax, state[0]);
            rMax = Math.Max(rMax, Math.Sqrt((state[1] * state[1]) + (state[2] * state[2])));
            ekMax = Math.Max(
                ekMax,
                0.5 * mass * ((state[3] * state[3]) + (state[4] * state[4]) + (state[5] * state[5])) / Charge);
        }

        Sample();

        for (var n = 0; n < 50000; n++)
        {
            var t = n * Dt;
            var k1 = Derivative(t, state);
            var k2 = Derivative(t + (Dt / 2), state.Select((v, i) => v + (Dt / 2 * k1[i])).ToArray());
            var k3 = Derivative(t + (Dt / 2), state.Select((v, i) => v + (Dt / 2 * k2[i])).ToArray());
            var k4 = Derivative(t + Dt, state.Select((v, i) => v + (Dt * k3[i])).ToArray());

            state = state.Select((v, i) => v + (Dt / 6 * (k1[i] + (2 * k2[i]) + (2 * k3[i]) + k4[i]))).ToArray();

            Sample();
        }

        return (zMin * 1e3, zMax * 1e3, rMax * 1e3, ekMax);
    }

    /// <summary>
    /// Forbes et al. table 2 against this engine at their conditions, against the ideal field
    /// itself, and what holes and a stretch would have changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The secular frequencies reproduce; the tabulated excursions do not, and neither does
    /// the ideal field.</b> Integrating the ideal quadrupole directly from table 1's starting
    /// point gives an axial excursion of about half a millimetre and a peak kinetic energy of
    /// about half an electronvolt, where all three codes report about a millimetre and 1.5 eV.
    /// The engine agrees with the ideal field and not with the table. Something in table 1 is
    /// not what the codes were given - the starting point, the velocity units, or the phase -
    /// and the extracted text cannot settle which; it is recorded as a disagreement with both
    /// sides stated, in <c>docs/literature-targets.md</c>.
    /// </para>
    /// <para>
    /// Asserted: the secular lines against Mathieu at the q the solved field's own curvature
    /// implies, to 0.1 kHz (measured within 0.035); the excursions against the ideal field to
    /// a micrometre for either sign of drive (measured 0.26 and 0.36 um); the holed, stretched
    /// trap's lines against Mathieu at its own effective radius, and 12 percent below the ideal
    /// trap's; and that the engine is not inside the codes' tabulated excursion spread, so the
    /// disagreement cannot silently vanish.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSingleIonComparison()
    {
        var betaZ = PaulTrapHoles.Beta(0.0, Qz);
        var betaR = PaulTrapHoles.Beta(0.0, Qz / 2.0);
        var fz = betaZ * DriveMHz * 1e3 / 2.0;
        var fr = betaR * DriveMHz * 1e3 / 2.0;

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Mathieu at q 0.400: beta_z {betaZ:F6} -> {fz:F4} kHz, beta_r {betaR:F6} -> {fr:F4} kHz"));

        var ideal = Model();
        var matched = Fly(ideal, 0.5, lines: true);
        var unmatched = Fly(ideal, 0.0, lines: false);
        var real = Fly(Model(
            ("endcapEntranceHoleFraction", PaulTrapHoles.HoleFraction),
            ("endcapExitHoleFraction", PaulTrapHoles.HoleFraction),
            ("endcapStretchFraction", PaulTrapHoles.Stretch)), 0.5, lines: true);

        var reference = Ideal(-1.0, matched.Volts);
        var referencePlus = Ideal(+1.0, matched.Volts);

        output.WriteLine("                     zmin      zmax      rmin     rmax    Ek min   Ek max   f_z(kHz)  f_r(kHz)  r0_eff");
        output.WriteLine("SIMION             -1.0631    1.0421   0.1791   1.1477   0.0050   1.6089   161.1     78.7");
        output.WriteLine("ITSIM              -1.0206    1.0264   0.1786   1.1475   0.0054   1.5251   161.1     78.4");
        output.WriteLine("ISIS               -1.0100    1.0000   0.1740   1.1390   0.0050   1.5298   161.0     78.4");
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"ideal field, -V cos {reference.ZMin,8:F4}  {reference.ZMax,8:F4}     -     {reference.RMax,7:F4}     -     {reference.EkMaxEv,7:F4}  {fz,8:F2}  {fr,8:F2}"));
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"ideal field, +V cos {referencePlus.ZMin,8:F4}  {referencePlus.ZMax,8:F4}     -     {referencePlus.RMax,7:F4}     -     {referencePlus.EkMaxEv,7:F4}"));

        foreach (var (label, run) in new[]
        {
            ("Einzel, -V cos     ", matched), ("Einzel, +V cos     ", unmatched), ("holes+stretch      ", real),
        })
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{label} {run.ZMin,8:F4}  {run.ZMax,8:F4}  {run.RMin,7:F4}  {run.RMax,7:F4}  "
                + $"{run.EkMinEv,7:F4}  {run.EkMaxEv,7:F4}  {run.AxialKHz,8:F2}  {run.RadialKHz,8:F2}  {run.R0EffMm:F4}"));
        }

        var qEff = Qz * Math.Pow(R0Mm / matched.R0EffMm, 2.0);
        var fzEff = PaulTrapHoles.Beta(0.0, qEff) * DriveMHz * 1e3 / 2.0;
        var frEff = PaulTrapHoles.Beta(0.0, qEff / 2.0) * DriveMHz * 1e3 / 2.0;

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"ring amplitude {matched.Volts:F3} V; q at the solved r0_eff {qEff:F5}: f_z {fzEff:F3} kHz, f_r {frEff:F3} kHz"));

        var qReal = Qz * Math.Pow(R0Mm / real.R0EffMm, 2.0);
        var fzReal = PaulTrapHoles.Beta(0.0, qReal) * DriveMHz * 1e3 / 2.0;
        var frReal = PaulTrapHoles.Beta(0.0, qReal / 2.0) * DriveMHz * 1e3 / 2.0;

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"holes+stretch: q at r0_eff {qReal:F5}: f_z {fzReal:F3} kHz, f_r {frReal:F3} kHz; f_z / ideal {real.AxialKHz / matched.AxialKHz:F4}"));
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"worst excursion difference from the ideal field: -V cos {Worst(matched, reference) * 1e3:F3} um, +V cos {Worst(unmatched, referencePlus) * 1e3:F3} um"));

        // The lines: Mathieu at the solved field's own q. Measured 0.025 and 0.035 kHz apart;
        // a 1 ms record's own resolution is 1 kHz, and the peak is located well inside it.
        Assert.Equal(fzEff, matched.AxialKHz, 0.1);
        Assert.Equal(frEff, matched.RadialKHz, 0.1);

        // And against the three programs' own published lines (Forbes 1999, table 2), which is
        // the cross-code check: SIMION and ITSIM give 161.1 kHz axially and ISIS 161.0; radially
        // SIMION gives 78.7 and the other two 78.4. The paper quotes four figures, so the band is
        // the spread of the three plus one unit in their last place either side. Asserted on the
        // published values rather than on Mathieu, because agreeing with a formula this engine
        // also evaluates is not agreeing with another code.
        Assert.InRange(matched.AxialKHz, 161.0 - 0.1, 161.1 + 0.1);
        Assert.InRange(matched.RadialKHz, 78.4 - 0.1, 78.7 + 0.1);

        // The excursions: the ideal field, to under a micrometre either way the drive is signed.
        Assert.True(Worst(matched, reference) < 1e-3, "the -V cos run departs from the ideal field");
        Assert.True(Worst(unmatched, referencePlus) < 1e-3, "the +V cos run departs from the ideal field");

        // And the disagreement with table 2 stays visible.
        Assert.True(matched.ZMax < 0.9, "the axial excursion now matches table 2; revisit the docs");

        // What a real trap would have shown: its lines are Mathieu's at its own effective radius,
        // and they are well below the ideal trap's at the same volts.
        Assert.Equal(fzReal, real.AxialKHz, 0.1);
        Assert.Equal(frReal, real.RadialKHz, 0.1);
        Assert.InRange(real.AxialKHz / matched.AxialKHz, 0.86, 0.90);
    }

    /// <summary>The worst disagreement in mm between a run's extremes and the ideal field's.</summary>
    private static double Worst(Run run, (double ZMin, double ZMax, double RMax, double EkMaxEv) ideal) =>
        new[] { run.ZMin - ideal.ZMin, run.ZMax - ideal.ZMax, run.RMax - ideal.RMax }.Max(Math.Abs);
}
