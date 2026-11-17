using System.Globalization;
using Einzel.Analysis;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;
using static Einzel.Library.Tests.PaulTrapHoles;

namespace Einzel.Library.Tests;

/// <summary>
/// The Paul trap's resonance band, named while it existed and gone now that it does not.
/// </summary>
/// <remarks>
/// <para>
/// <b>While this template's electrodes were flat annuli</b> the trap lost its ion in a narrow
/// band at 605-614 V, sixty volts inside what the Mathieu chart calls stable. Every control
/// said it was real - identical at twice the mesh and twice the hold, absent at a quarter of
/// the hold and absent at a third of the launch offset - but a scan over amplitude can only
/// say <em>that</em> there is a band. A nonlinear resonance is <strong>defined</strong> by a
/// frequency condition, <c>n_z beta_z + n_r beta_r = 2</c> for a multipole of order
/// <c>n_z + n_r</c>, so naming one means measuring the frequencies.
/// </para>
/// <para>
/// <b>The answer was the octupole.</b> At the band center the measured exponents were
/// beta_z = 0.6769 and beta_r = 0.3225, and <c>2 beta_z + 2 beta_r = 1.9989</c> - an
/// order-four condition met to 0.055 percent, against 0.22 and 0.10 at the amplitudes
/// either side. Order four is an octupole, which is precisely the leading unwanted multipole
/// this geometry's symmetry permits: the trap is symmetric about its own center plane and
/// about the axis, so every odd order vanishes. The identification was predicted before it
/// was fitted.
/// </para>
/// <para>
/// <b>The electrodes are hyperboloids now, and the band is gone</b> - which is a stronger
/// confirmation of that identification than the fit was. An octupole is what a flat annulus
/// buys, so an account blaming the octupole predicts that giving the trap its real surfaces
/// removes the band; scanned at four volts from 560 to 730, every amplitude below the
/// ejection edge holds its ion. And the frequency condition goes with it: at the old band
/// center the best even condition now misses 2 by 0.22, where it was met to 0.0011. Two
/// independent signatures, both gone together.
/// </para>
/// <para>
/// What remains of the octupole is measurable and small: the curvature ratio in
/// <c>PaulTrapStudy</c> departs from -2 as the square of the sampling radius, which is an
/// order-four multipole, at 0.06 percent where the flat annuli gave 1.3. That is the
/// truncation of the hyperboloids, and a real trap has it too.
/// </para>
/// </remarks>
public sealed class PaulTrapResonanceStudy(ITestOutputHelper output)
{
    /// <summary>Where the flat-annulus trap lost its ion, in volts.</summary>
    /// <remarks>
    /// Kept as a coordinate rather than as a phenomenon: the hyperboloid trap holds its
    /// ion here, and this is the amplitude the tests below go and look at.
    /// </remarks>
    private const double FormerBandCenter = 610.0;

    /// <summary>Flies one ion in the trap and records its path.</summary>
    private static (IReadOnlyList<TrajectorySample> Samples, TrajectoryOutcome Outcome, double DriveHz)
        Fly(double volts, double offsetMm, int cycles)
    {
        var model = Compile(Trap(
            ("rfAmplitude", volts), ("launchOffset", offsetMm), ("cycles", cycles)));

        var field = FieldAssembly.Build(model);
        var species = IonSpecies.FromModel(model);
        var drive = model.Parameters["driveFrequency"].SiValue;

        var launch = new PhaseState(
            model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

        var point = model.DetectorPoint;
        var normal = model.DetectorNormal;

        TrajectoryStopFunction detector =
            (in PhaseState state) => Vec3.Dot(state.Position - point, normal);

        // Sixteen samples per RF cycle, the cadence the ideal-quadrupole check used.
        var recorder = new TrajectoryRecorder(1.0 / (16.0 * drive));

        var result = TrajectoryIntegrator.Integrate(
            launch,
            species,
            field,
            new IntegrationSettings
            {
                RelativeTolerance = 1e-9,
                MaximumFlightTime = model.MaximumFlightTimeSi,
            },
            detector,
            recorder);

        return (recorder.Samples, result.Outcome, drive);
    }

    /// <summary>The amplitude at which an ion launched this far out stops being held.</summary>
    /// <remarks>
    /// Bisected rather than scanned, and returned as the bracket rather than its midpoint:
    /// what a bisection produces is an interval known to contain the crossing, and the
    /// midpoint is a convention. Two hundred cycles, which this geometry's edge is
    /// converged in.
    /// </remarks>
    private static (double Held, double Lost) EjectionEdge(double offsetMm)
    {
        double held = 600.0, lost = 900.0;

        for (var k = 0; k < 9; k++)
        {
            var mid = 0.5 * (held + lost);

            if (Fly(mid, offsetMm, 200).Outcome == TrajectoryOutcome.MaximumFlightTimeReached)
            {
                held = mid;
            }
            else
            {
                lost = mid;
            }
        }

        return (held, lost);
    }

    /// <summary>β from a measured line: the line is β Ω / 2.</summary>
    private static double BetaFrom(double lineHz, double driveHz) => 2.0 * lineHz / driveHz;

    [Fact]
    public void BothSecularFrequenciesAreMeasurable()
    {
        // Where the flat-annulus trap's band used to be, minus twelve volts - an
        // amplitude at which the ion is confined for the whole hold either way, so the
        // record is long and clean. Both axes at once: a three-dimensional trap has two
        // independent secular motions and a resonance condition couples them, so one of
        // them alone cannot answer the question.
        var (samples, outcome, drive) = Fly(FormerBandCenter - 12.0, 0.3, 200);

        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, outcome);

        output.WriteLine($"{samples.Count} samples, drive {drive / 1e3:F1} kHz");
        output.WriteLine("axis          line (kHz)      beta      power");

        var betas = new double[2];

        for (var axis = 0; axis < 2; axis++)
        {
            var spectrum = SecularSpectrum.From(samples, axis, 0.02 * drive, 0.90 * drive, 6000);
            var peak = spectrum.Peak();

            Assert.NotNull(peak);

            var (value, _, _, _) = peak;
            var line = value.In("Hz");

            betas[axis] = BetaFrom(line, drive);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{(axis == 0 ? "axial (z)" : "radial (r)"),-12}{line / 1e3,10:F2}  {betas[axis],10:F4}  "
                + $"{spectrum.Lines.Max(l => l.Power),9:F4}"));
        }

        // The signature of a three-dimensional quadrupole trap, and a check that
        // does not depend on any measured absolute: on the a = 0 line q_r is exactly
        // half q_z, so beta_r is smaller than beta_z and by roughly a factor of two
        // in the low-q limit. Whatever the effective radius is, it scales both.
        output.WriteLine($"beta_z / beta_r  {betas[0] / betas[1]:F4}");

        Assert.True(betas[0] > betas[1], "the axial motion should be the faster one");
        Assert.InRange(betas[0] / betas[1], 1.5, 3.0);
    }

    /// <summary>The effective radius read off the field's curvature at the trap center.</summary>
    /// <remarks>
    /// Measured rather than carried as a constant, which is what this test was doing and
    /// what went stale: it held 3.8195 mm, the flat-annulus geometry's, and against the
    /// hyperboloids that over-predicts every secular line by ten percent. A constant
    /// describing a geometry is a constant that stops being true when the geometry is
    /// rebuilt, and nothing tells you.
    /// </remarks>
    private static double EffectiveRadius(CompiledModel model)
    {
        var field = (ITimeVaryingField)FieldAssembly.Build(model);
        var volts = model.Parameters["rfAmplitude"].SiValue;

        const double Delta = 0.4e-3;

        var axial =
            (field.ElectricFieldAt(new Vec3(Delta, 0.0, 0.0), 0.0).X
                - field.ElectricFieldAt(new Vec3(-Delta, 0.0, 0.0), 0.0).X)
            / (2.0 * Delta);

        return Math.Sqrt(2.0 * volts / axial);
    }

    /// <summary>The secular line of a flown ion agrees with Mathieu at the solved radius.</summary>
    /// <remarks>
    /// <para>
    /// Two entirely different routes to the same number. The field gives an effective
    /// radius as a curvature measured at the center with no ion involved; here it is read
    /// off a <em>trajectory</em> - fly an ion for two hundred RF cycles, take the
    /// periodogram, and compare the secular line to Mathieu's closed form at q scaled by
    /// (r0/r0_eff) squared. Nothing is shared between them but the solved field.
    /// </para>
    /// <para>
    /// <b>On the hyperboloids the scale factor is 1.0009 and the test is sharper for it.</b>
    /// While the electrodes were flat annuli the factor was 1.0968, so most of what the
    /// agreement demonstrated was that one number had been transcribed into two places. Now
    /// the correction is negligible and what is left is Mathieu against a flown ion.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheEffectiveRadiusIsConfirmedByTheSecularFrequency()
    {
        var model = Compile(Trap());
        var declared = model.Parameters["inscribedRadius"].SiValue;
        var effectiveRadius = EffectiveRadius(model);

        var factor = Math.Pow(declared / effectiveRadius, 2.0);
        var perVolt = PerVolt(model);

        output.WriteLine(
            $"effective radius {effectiveRadius * 1e3:F4} mm against {declared * 1e3:F4} "
            + $"declared, factor {factor:F5}");

        output.WriteLine(" volts    q_nom    q_eff     beta   predicted    measured    ratio");

        var worstLow = 0.0;
        var shifts = new List<(double Q, double Ratio)>();

        foreach (var volts in new[] { 200.0, 300.0, 400.0, 600.0 })
        {
            var (samples, _, drive) = Fly(volts, 0.3, 200);
            var measured = Line(samples, 0, drive);

            var nominal = volts * perVolt;
            var effective = nominal * factor;
            var predicted = Beta(0.0, effective) * drive / 2.0;

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{volts,6:F0}  {nominal,7:F5}  {effective,7:F5}  {Beta(0.0, effective),7:F5}  "
                + $"{predicted / 1e3,9:F3}  {measured / 1e3,10:F3}  {measured / predicted,7:F4}"));

            shifts.Add((nominal, measured / predicted));

            worstLow = Math.Max(worstLow, Math.Abs((measured / predicted) - 1.0));
        }

        output.WriteLine($"worst departure across the range: {100.0 * worstLow:F3} percent");

        // A tenth of a percent, where the flat trap needed half a percent below q = 0.5
        // and drifted three percent above it. A hyperboloid trap IS the Mathieu problem,
        // so the residual here is the truncation and the mesh rather than the geometry.
        Assert.True(
            worstLow < 0.002,
            $"the field's curvature and a flown ion's secular line should agree to two "
            + $"parts in a thousand, and the worst is {100.0 * worstLow:F3} percent");

        // The residual is reported and deliberately not attributed. It moves monotonically
        // with q - from -5.7e-4 at q = 0.24 to +7.6e-4 at q = 0.73 - but it crosses zero on
        // the way, so it is not the one-sided pull an anharmonicity gives. At a few parts in
        // ten thousand it is the size of the mesh, the periodogram's own resolution and the
        // continued fraction's truncation together, and separating those is a study rather
        // than an assertion.
        output.WriteLine(
            "departure from 1: " + string.Join(
                ", ",
                shifts.Select(s => string.Create(
                    CultureInfo.InvariantCulture, $"q {s.Q:F3} -> {s.Ratio - 1.0:E2}"))));
    }

    /// <summary>The published Mathieu boundary on the a = 0 line, where beta reaches one.</summary>
    /// <remarks>
    /// Taken from the literature rather than from the continued fraction above,
    /// deliberately. That expansion has a near-singularity exactly at beta = 1 - the
    /// n = 1 denominator (beta - 2)^2 goes to one there - and it puts the crossing at
    /// q = 0.9117, four parts in a thousand off the tabulated value. It is accurate
    /// where it is used below, at beta from 0.3 to 0.8, and not at the endpoint.
    /// </remarks>
    private const double TabulatedBoundaryQ = 0.90804;

    /// <summary>q per volt for this ion in this geometry, from the closed form.</summary>
    private static double PerVolt(CompiledModel model)
    {
        var species = IonSpecies.FromModel(model);
        var radius = model.Parameters["inscribedRadius"].SiValue;
        var omega = 2.0 * Math.PI * model.Parameters["driveFrequency"].SiValue;

        return 4.0 * species.ChargeSi / (species.MassSi * omega * omega * radius * radius);
    }

    /// <summary>The measured axial exponent at one drive amplitude.</summary>
    private static double MeasuredBeta(double volts, double offsetMm)
    {
        var (samples, _, drive) = Fly(volts, offsetMm, 200);

        return BetaFrom(Line(samples, 0, drive), drive);
    }

    [Fact]
    public void BetaDoesNotDependOnHowFarOffCenterTheIonStarted()
    {
        // The premise of the calibration below. Mathieu's equation is linear, so its
        // characteristic exponent is a property of the field and the working point and not
        // of the orbit - if what is measured here moved with the launch amplitude, it would
        // not be beta and the calibration would be of something else.
        //
        // THE CONTROL HAD TO MOVE OUT, and that is the finding. On flat annuli a 0.20 mm
        // launch already shifted beta measurably against 0.05 mm, and the test read that
        // shift as the anharmonicity arriving. On hyperboloids those two agree to below a
        // part in a thousand at every amplitude tried, so the old control no longer
        // discriminates: it asserted that a deliberately imperfect trap was imperfect.
        // What still does is going far enough out that the TRUNCATION of the hyperboloids
        // bites, which is a millimeter and a half rather than a fifth of one.
        output.WriteLine("   volts    offset/mm        beta      vs 0.05 mm");

        var small = new List<double>();
        var far = new List<double>();

        foreach (var volts in new[] { 300.0, 600.0 })
        {
            var reference = MeasuredBeta(volts, 0.05);

            foreach (var offset in new[] { 0.20, 0.80, 1.60 })
            {
                var beta = MeasuredBeta(volts, offset);
                var shift = Math.Abs(beta - reference) / reference;

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{volts,8:F0}   {offset,10:F2}   {beta,9:F5}   {shift,13:E2}"));

                if (offset <= 0.20)
                {
                    small.Add(shift);
                }
                else if (offset >= 1.60)
                {
                    far.Add(shift);
                }
            }
        }

        output.WriteLine(
            $"near the center the worst shift is {small.Max():E2}; "
            + $"at 1.6 mm the smallest is {far.Min():E2}");

        // A fourfold change in launch amplitude near the center leaves beta where it was.
        Assert.True(
            small.Max() < 1.0e-3,
            $"beta moved {small.Max():E2} between a 0.05 mm and a 0.20 mm launch, so what "
            + "is being measured is not a characteristic exponent");

        // And a thirtyfold one does not, at both amplitudes. That is the half that says the
        // field has an anharmonicity at all rather than a spectrum too blunt to see one -
        // and it is the same truncation octupole the curvature measurement finds.
        Assert.True(
            far.Min() > 5.0 * small.Max(),
            $"a 1.6 mm launch shifted beta by only {far.Min():E2} against {small.Max():E2} "
            + "near the center, so nothing here would notice an anharmonicity");
    }

    [Fact]
    public void TheLinearBoundaryIsMeasurableWithoutTheIonReachingAnElectrode()
    {
        // The measurement the ejection scan structurally cannot make. A stability
        // boundary found by asking "did the ion reach an electrode" requires the ion
        // to travel from wherever it started all the way to z0, through the whole
        // anharmonic region - so it is never a small-amplitude measurement, whatever
        // it was launched at, and it comes out amplitude-dependent.
        //
        // beta needs no journey. It is read off the spectrum of an ion that stays
        // small, so the linear boundary can be located by calibrating beta(V) against
        // Mathieu and asking where the calibration puts beta = 1.
        var model = Compile(Trap());
        var perVolt = PerVolt(model);
        var declared = model.Parameters["inscribedRadius"].SiValue * 1e3;

        var points = new List<(double Volts, double Beta)>();

        foreach (var volts in new[] { 300.0, 390.0, 480.0, 570.0 })
        {
            points.Add((volts, MeasuredBeta(volts, 0.10)));
        }

        // One free number fitted across the whole curve rather than read at one
        // point: the scale s with beta_measured(V) = beta_Mathieu(q_nominal(V) * s).
        // Golden section on the summed squared relative misfit.
        double Misfit(double scale)
        {
            var total = 0.0;

            foreach (var (volts, measured) in points)
            {
                var predicted = Beta(0.0, volts * perVolt * scale);

                total += Math.Pow((measured / predicted) - 1.0, 2.0);
            }

            return total;
        }

        double lo = 1.0, hi = 1.25;

        for (var k = 0; k < 80; k++)
        {
            var a = lo + ((hi - lo) / 3.0);
            var b = hi - ((hi - lo) / 3.0);

            if (Misfit(a) < Misfit(b))
            {
                hi = b;
            }
            else
            {
                lo = a;
            }
        }

        var scale = 0.5 * (lo + hi);
        var effective = declared / Math.Sqrt(scale);

        output.WriteLine("   volts   q_nom   beta_meas  beta_pred    ratio");

        var worst = 0.0;

        foreach (var (volts, measured) in points)
        {
            var predicted = Beta(0.0, volts * perVolt * scale);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{volts,8:F0}  {volts * perVolt,6:F4}   {measured,8:F5}   {predicted,8:F5}  "
                + $"{measured / predicted,7:F4}"));

            worst = Math.Max(worst, Math.Abs((measured / predicted) - 1.0));
        }

        var boundaryVolts = TabulatedBoundaryQ / scale / perVolt;

        output.WriteLine($"fitted scale        {scale:F5}");
        output.WriteLine($"effective radius    {effective:F4} mm against {declared:F4} declared");
        output.WriteLine($"worst residual      {worst:E2}");
        output.WriteLine($"beta = 1 at         {boundaryVolts:F1} V, q_nominal {TabulatedBoundaryQ / scale:F5}");

        // The curve is one ideal quadrupole across the whole range, which is what
        // makes the endpoint worth extrapolating to at all.
        Assert.True(worst < 0.01, $"the fit leaves {worst:E2}, so this is not one quadrupole");

        // And the radius it implies is the one the FIELD gives, measured with no ion
        // involved at all. Two routes sharing nothing but the solved field; the number is
        // taken from the field here rather than written down, because a constant naming a
        // geometry is what went stale when the electrodes were rebuilt.
        Assert.Equal(EffectiveRadius(Compile(Trap())) * 1e3, effective, 2);

        // MEASURED RATHER THAN QUOTED, which is the second thing this test had gone stale
        // about: it printed a sentence naming the ejection edges of the flat-annulus trap
        // and asserted a range around them. The edges are bisected here.
        output.WriteLine("  launch/mm    held to     lost by      q_nom     of beta = 1");

        var edges = new List<(double Offset, double Held)>();

        foreach (var offset in new[] { 0.05, 0.10, 0.30, 0.60 })
        {
            var (held, lost) = EjectionEdge(offset);

            edges.Add((offset, held));

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {offset,9:F2}  {held,9:F1} V  {lost,9:F1} V  {held * perVolt,9:F5}  "
                + $"{held / boundaryVolts,12:F4}"));
        }

        // AN EJECTION EDGE IS NOT A LINEAR BOUNDARY, and the amplitude dependence is the
        // proof: Mathieu's equation is linear, so a trajectory scaled by a constant is
        // another trajectory and an ideal boundary cannot move with the launch. What is
        // measured here requires the ion to cross the whole anharmonic region to reach an
        // electrode, so it is never a small-amplitude measurement whatever it was launched
        // at. It rises as the launch shrinks.
        for (var k = 1; k < edges.Count; k++)
        {
            Assert.True(
                edges[k].Held < edges[k - 1].Held,
                $"a {edges[k].Offset:F2} mm launch ejected at {edges[k].Held:F1} V against "
                + $"{edges[k - 1].Held:F1} V at {edges[k - 1].Offset:F2} mm");
        }

        // And it converges on the linear boundary from below as the launch shrinks, which
        // is what the flat annuli obscured - their small-launch edge sat at q_z = 0.85
        // against a beta-derived 0.8254, on the wrong side and twice as far away. Here the
        // smallest launch ejects within a percent of beta = 1, and below it.
        var closest = edges[0].Held / boundaryVolts;

        output.WriteLine(
            $"the smallest launch ejects at {closest:F4} of the beta = 1 amplitude");

        Assert.InRange(closest, 0.99, 1.0);
    }

    /// <summary>The octupole band went with the flat electrodes.</summary>
    /// <remarks>
    /// <para>
    /// <b>The prediction this makes was made before the geometry changed.</b> The band at
    /// 605-614 V was identified as an order-four resonance, and order four is what a flat
    /// annulus buys - so giving the trap the hyperboloids it actually has had to remove it.
    /// Both signatures are checked, because either alone is much weaker: a scan that found
    /// no loss might have a hold too short to grow one, and a frequency condition that is no
    /// longer met says nothing about whether an ion survives.
    /// </para>
    /// <para>
    /// The scan runs to just below the measured ejection edge, since above it the ion is
    /// lost for the ordinary reason and a loss there is not a band.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOctupoleBandWentWithTheFlatElectrodes()
    {
        // Below the 724 V edge this geometry has at a 0.3 mm launch. Four volts is finer
        // than the nine-volt band it is looking for.
        var lost = new List<double>();

        for (var volts = 560.0; volts <= 716.0; volts += 4.0)
        {
            if (Fly(volts, 0.3, 200).Outcome != TrajectoryOutcome.MaximumFlightTimeReached)
            {
                lost.Add(volts);
            }
        }

        output.WriteLine(
            lost.Count == 0
                ? "  no loss at any of 40 amplitudes from 560 to 716 V"
                : "  lost at " + string.Join(
                    ", ",
                    lost.Select(v => v.ToString("F0", CultureInfo.InvariantCulture))) + " V");

        Assert.Empty(lost);

        // And the frequency condition that named it. Every condition a multipole up to
        // order six could impose is tried and the smallest departure from 2 reported; only
        // even orders, since the trap is symmetric about its center plane and about the
        // axis so the odd multipoles vanish by symmetry.
        output.WriteLine("  volts     beta_z    beta_r    best condition            n_z b_z + n_r b_r");

        var atFormerBand = double.NaN;

        foreach (var volts in new[] { 560.0, FormerBandCenter, 660.0 })
        {
            var (samples, _, drive) = Fly(volts, 0.3, 200);

            var betaZ = BetaFrom(Line(samples, 0, drive), drive);
            var betaR = BetaFrom(Line(samples, 1, drive), drive);

            (int Z, int R, double Sum, double Miss) best = (0, 0, 0.0, double.MaxValue);

            for (var order = 2; order <= 6; order += 2)
            {
                for (var nz = 0; nz <= order; nz++)
                {
                    var nr = order - nz;

                    // n_r must be even: the radial coordinate enters the potential through
                    // r squared, so an odd power of the radial motion has no term to
                    // couple to.
                    if (nr % 2 != 0)
                    {
                        continue;
                    }

                    var sum = (nz * betaZ) + (nr * betaR);
                    var miss = Math.Abs(sum - 2.0);

                    if (miss < best.Miss)
                    {
                        best = (nz, nr, sum, miss);
                    }
                }
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{volts,7:F0}  {betaZ,9:F4} {betaR,9:F4}    "
                + $"{best.Z} beta_z + {best.R} beta_r        {best.Sum,10:F4}  (miss {best.Miss:F4})"));

            if (volts == FormerBandCenter)
            {
                atFormerBand = best.Miss;
            }
        }

        output.WriteLine(
            $"at {FormerBandCenter:F0} V the nearest even condition now misses 2 by "
            + $"{atFormerBand:F4}, against 0.0011 on flat annuli");

        // Nine candidate conditions are searched and one is always nearest, so what is
        // asserted is that the nearest is nowhere near - a hundred times further off than
        // the flat trap's, which is the difference between a resonance and a coincidence.
        Assert.True(
            atFormerBand > 0.1,
            $"the old band center still sits {atFormerBand:F4} from a resonance condition, "
            + "close enough that the band's absence needs another explanation");
    }

    private static double Line(IReadOnlyList<TrajectorySample> samples, int axis, double drive)
    {
        var spectrum = SecularSpectrum.From(samples, axis, 0.02 * drive, 0.90 * drive, 6000);
        var peak = spectrum.Peak();

        Assert.NotNull(peak);

        var (value, _, _, _) = peak;

        return value.In("Hz");
    }
}
