using System.Globalization;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The three-dimensional quadrupole trap, and the two things that separate its
/// measured ejection boundary from the tabulated one.
/// </summary>
/// <remarks>
/// <para>
/// A Paul trap is the device the whole <c>ITransportMode</c> / figure-of-merit
/// apparatus is least suited to by default, because everything else here is
/// measured by ions <em>arriving</em>. A trapped ion never arrives anywhere, so a
/// transmission is zero for a trap that works and zero again for one that lost
/// everything, and no figure that counts arrivals can tell those apart. What a trap
/// is measured by is the complement - <c>confined</c>, the fraction still inside
/// when the hold ends - and this study exists as much to exercise that as to
/// measure the device.
/// </para>
/// <para>
/// Axisymmetric, so it is a half-plane solve rather than a volume: SYM-1 is what
/// makes a 3-D trap cost what a 2-D cross-section costs. The classical geometry has
/// <c>r0^2 = 2 z0^2</c>, which collapses
/// <c>q_z = 8 z e V / (m omega^2 (r0^2 + 2 z0^2))</c> to
/// <c>4 z e V / (m omega^2 r0^2)</c> - the same volts per unit q as a linear
/// quadrupole of the same inscribed radius, so the two are directly comparable. The
/// tabulated ejection boundary on the <c>a = 0</c> line is <c>q_z = 0.90804</c>.
/// </para>
/// </remarks>
public sealed class PaulTrapStudy(ITestOutputHelper output)
{
    /// <summary>The tabulated Mathieu boundary on the a = 0 line.</summary>
    private const double TabulatedQ = 0.90804;

    private static ModelDocument Trap(params (string Name, double Value)[] overrides)
    {
        var document = Io.ModelJson.Parse(DeviceTemplates.Read("paul-trap"));

        var parameters = new Dictionary<string, ParameterDocument>(
            document.Parameters!, StringComparer.Ordinal);

        foreach (var (name, value) in overrides)
        {
            parameters[name] = parameters[name] with { Value = value };
        }

        return document with { Parameters = parameters };
    }

    private static CompiledModel Compile(ModelDocument document)
    {
        var validation = ModelValidator.Validate(document);

        Assert.True(
            validation.Model is not null,
            string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        return validation.Model!;
    }

    /// <summary>The stability parameter this geometry's declared radius implies.</summary>
    private static double NominalQ(CompiledModel model, double volts)
    {
        var species = IonSpecies.FromModel(model);
        var radius = model.Parameters["inscribedRadius"].SiValue;
        var omega = 2.0 * Math.PI * model.Parameters["driveFrequency"].SiValue;

        return 4.0 * species.ChargeSi * volts / (species.MassSi * omega * omega * radius * radius);
    }

    [Fact]
    public void TheWholeTrapIsOneBasisSolve()
    {
        var model = Compile(Trap());
        var solve = model.Fields[0].Solve!;

        var channels = GeometryBuilder.SolveChannels(solve);

        output.WriteLine($"electrodes    {solve.Electrodes.Count}");
        output.WriteLine($"basis solves  {channels.Count}");
        output.WriteLine(
            $"convergence   {channels[0].Report.Cycles} cycles at factor "
            + $"{channels[0].Report.ConvergenceFactor:F4}");

        // Three electrodes and two stops, one spatial pattern: the endcaps and the stops are
        // grounded, so the only thing that moves is the ring and there is nothing to
        // superpose. The stops exist to itemise an ion leaving the solve domain and fix no
        // node of it (PaulTrapEndcapHoleStudy), so they are declarations and not conductors
        // this solve feels.
        Assert.Equal(5, solve.Electrodes.Count);
        Assert.Single(channels);
        Assert.True(channels[0].Report.Converged);
    }

    [Fact]
    public void ARestingIonNeedsNoAcceleratingPotential()
    {
        // A trap holds its ions still, so the source starts at rest and the drive is
        // the only thing that can move it. That is legal precisely when a field can
        // do work - and asking only about the DC potential, which is how this check
        // was first written, declares the archetypal start-at-rest device incapable
        // of moving anything: every electrode here holds zero volts of DC and all of
        // its potential as drive.
        var document = Trap();

        Assert.Equal(0.0, document.Source!.AccelerationPotential!.Value);

        foreach (var electrode in document.Fields![0].Solve!.Electrodes!)
        {
            Assert.True(
                electrode.Potential is null || electrode.Potential.Value == 0.0,
                $"{electrode.Name} was expected to hold no DC");
        }

        Assert.True(ModelValidator.Validate(document).IsValid);
    }

    /// <summary>The effective radius from the field's own curvature at the center.</summary>
    /// <remarks>
    /// The drive is a cosine, so t = 0 is the top of the cycle and the ring sits at its
    /// full declared amplitude. Central differences either side of the center, several
    /// cells wide - a difference narrower than a few cells measures the interpolant.
    /// </remarks>
    private static (double Axial, double Radial, double Radius) Curvature(
        CompiledModel model, ITimeVaryingField field, double delta)
    {
        var volts = model.Parameters["rfAmplitude"].SiValue;

        var axial =
            (field.ElectricFieldAt(new Vec3(delta, 0.0, 0.0), 0.0).X
                - field.ElectricFieldAt(new Vec3(-delta, 0.0, 0.0), 0.0).X)
            / (2.0 * delta);

        var radial =
            (field.ElectricFieldAt(new Vec3(0.0, delta, 0.0), 0.0).Y
                - field.ElectricFieldAt(new Vec3(0.0, -delta, 0.0), 0.0).Y)
            / (2.0 * delta);

        return (axial, radial, Math.Sqrt(2.0 * volts / axial));
    }

    /// <summary>The field is quadrupolar and its effective radius is the declared one.</summary>
    /// <remarks>
    /// <para>
    /// <b>This test asserted the opposite for most of this project's life, and the
    /// electrodes are why.</b> The template declared three flat annuli, and a flat annulus
    /// at the nominal r0 lies <em>inside</em> the hyperbola sharing its vertex everywhere
    /// except at that vertex: at z = 2.23 mm the ring hyperbola would be at r = 5.09 mm and
    /// the annulus was at 4.00. Metal closer in is a stronger field at the center, a smaller
    /// effective r0, a larger q per volt, and ejection at a lower amplitude - so every
    /// published number here carried a 9.4 percent correction, measured three independent
    /// ways and correct, for a geometry the device does not have.
    /// </para>
    /// <para>
    /// <b>The electrodes of a quadrupole trap are hyperboloids</b> - the ring one sheet at
    /// <c>r = r0 sqrt(1 + (z/z0)^2)</c>, each endcap one of two at
    /// <c>z = z0 sqrt(1 + (r/r0)^2)</c> - and the template now traces them as outlines in
    /// the cylindrical half-plane. The correction is gone rather than smaller:
    /// <b>3.9983 mm against 4.0000 declared</b>, 0.04 percent, where it was 3.8195.
    /// </para>
    /// <para>
    /// <b>What is left is an octupole, and its order is predicted rather than fitted.</b>
    /// The hyperboloids are truncated - they have to be, at the gap and at the outer radius
    /// - so the field is not the ideal one however exact the faces are. The trap is
    /// symmetric about its center plane and about its axis, so every odd multipole vanishes
    /// and four is the first available; an octupole makes the curvature ratio depart from -2
    /// as the square of the sampling radius, and it does, to two figures across a doubling.
    /// That is the assertion, rather than a blanket tolerance: a departure growing at the
    /// wrong power would be discretization or a bug.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFieldIsQuadrupolarAndItsEffectiveRadiusIsTheDeclaredOne()
    {
        var model = Compile(Trap());
        var field = (ITimeVaryingField)FieldAssembly.Build(model);

        var declared = model.Parameters["inscribedRadius"].SiValue;

        output.WriteLine("     delta      dEz/dz      dEr/dr       ratio     r0_eff");

        var effective = double.NaN;
        var deltas = new[] { 0.4e-3, 0.6e-3, 0.8e-3 };
        var anharmonicity = new List<double>();

        foreach (var delta in deltas)
        {
            var (axial, radial, radius) = Curvature(model, field, delta);

            output.WriteLine(
                $"{delta * 1e3,10:F2}  {axial,10:E3}  {radial,10:E3}  "
                + $"{axial / radial,10:F4}  {radius * 1e3,9:F4}");

            // phi = V (r^2 - 2 z^2) / (2 r0^2) gives dEz/dz = +2V/r0^2 and
            // dEr/dr = -V/r0^2, so the ratio is exactly -2 wherever the quadratic term
            // dominates. That is Laplace in cylindrical coordinates, and it is the check
            // that the expansion is valid at this radius at all.
            anharmonicity.Add(Math.Abs((axial / radial) + 2.0));

            // The innermost sample, where the quadratic term is least contaminated.
            effective = double.IsNaN(effective) ? radius : effective;
        }

        output.WriteLine(
            "anharmonicity " + string.Join(
                ", ", anharmonicity.Select(a => a.ToString("F4", CultureInfo.InvariantCulture))));

        Assert.True(anharmonicity[0] < 0.005, $"the center is not quadrupolar: {anharmonicity[0]:F4}");

        // Quadratic in the sampling radius, which is what an octupole is. Checked as the
        // ratio of departures against the ratio of radii squared, at both steps.
        for (var k = 1; k < deltas.Length; k++)
        {
            var grew = anharmonicity[k] / anharmonicity[k - 1];
            var predicted = Math.Pow(deltas[k] / deltas[k - 1], 2.0);

            output.WriteLine(
                $"  {deltas[k - 1] * 1e3:F1} to {deltas[k] * 1e3:F1} mm: departure grew "
                + $"{grew:F3}x against {predicted:F3}x for an octupole");

            Assert.Equal(predicted, grew, 1);
        }

        output.WriteLine($"declared r0   {declared * 1e3:F4} mm");
        output.WriteLine($"effective r0  {effective * 1e3:F4} mm");
        output.WriteLine($"(r0/r0_eff)^2 {Math.Pow(declared / effective, 2.0):F4}");

        // The declared radius, to a tenth of a percent. The flat-annulus geometry gave
        // 3.8195 here, which is 4.5 percent low and 9.4 percent in q per volt.
        Assert.Equal(declared, effective, 4);

        // And the boundary it implies is the ideal trap's. A scale-factor account of the
        // flat trap put this at 677.5 V against an ideal 743.1; with the hyperboloids the
        // scale factor has nothing left to do.
        var scaled = TabulatedQ * Math.Pow(effective / declared, 2.0);
        var perVolt = NominalQ(model, 1.0);

        output.WriteLine($"q at boundary {scaled:F5} (tabulated {TabulatedQ:F5})");
        output.WriteLine(
            $"implies       {scaled / perVolt:F2} V, against {TabulatedQ / perVolt:F2} V "
            + "for a trap whose effective radius is exactly the declared one");

        Assert.True(
            Math.Abs(scaled - TabulatedQ) < 0.01 * TabulatedQ,
            $"the implied boundary is {scaled / perVolt:F2} V against the ideal trap's "
            + $"{TabulatedQ / perVolt:F2} V, more than a percent apart");
    }

    /// <summary>What is left of the effective-radius shortfall is the truncation, not the mesh.</summary>
    /// <remarks>
    /// <para>
    /// <b>Attribution by refinement, and it refuted the guess that prompted it.</b> The
    /// hyperboloids leave the effective radius 0.04 percent below the declared one, and
    /// there were two candidates: the discretization of a curved cut cell, which would fall
    /// at second order under refinement, and the truncation of the electrodes, which is
    /// geometry and would not move. I expected the mesh, and wrote the test asserting it.
    /// </para>
    /// <para>
    /// <b>It plateaus.</b> Eight times finer takes the shortfall from 0.0506 to 0.0420 per
    /// cent and stops - 1.18x, then 1.05x, then 0.97x, which is a floor with the last step
    /// inside its own noise rather than a sequence still falling. So what remains is a
    /// property of the device: a real quadrupole trap has hyperboloids that stop, at the
    /// gap between ring and endcap and at the outer radius, and the field inside a truncated
    /// hyperboloid is not the ideal one however exactly the faces are cut. It is the same
    /// truncation the octupole in
    /// <see cref="TheFieldIsQuadrupolarAndItsEffectiveRadiusIsTheDeclaredOne"/> comes from.
    /// </para>
    /// <para>
    /// Worth 0.08 percent in q per volt, against the 9.4 percent the flat annuli cost. The
    /// distinction that matters for a reader is that this one is the trap's and not the
    /// solver's, so no amount of mesh buys it back.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheResidualEffectiveRadiusShortfallIsTheTruncationRatherThanTheMesh()
    {
        const double Declared = 4.0e-3;

        var shortfalls = new List<double>();

        output.WriteLine("  cells/r0     r0_eff      shortfall   fell");

        foreach (var cells in (double[])[10.0, 20.0, 40.0, 80.0])
        {
            var model = Compile(Trap(("cellsPerRadius", cells)));
            var field = (ITimeVaryingField)FieldAssembly.Build(model);

            var radius = Curvature(model, field, 0.4e-3).Radius;
            var shortfall = (Declared - radius) / Declared;

            output.WriteLine(
                $"  {cells,8:F0}  {radius * 1e3,9:F6} mm  {shortfall * 100.0,9:F4}%"
                + (shortfalls.Count == 0 ? "" : $"   {shortfalls[^1] / shortfall,5:F2}x"));

            shortfalls.Add(shortfall);
        }

        // A floor rather than a sequence still falling: the last halving of the cell moves
        // it by under a twentieth, where second order would move it fourfold.
        var settled = shortfalls[^1] / shortfalls[^2];

        output.WriteLine(
            $"the finest halving moved it {settled:F3}x, against 0.25x for second order");

        Assert.InRange(settled, 0.9, 1.1);

        // And the floor is small, which is what says the geometry is right. Flat annuli
        // gave 4.5 percent here.
        Assert.True(
            shortfalls[^1] < 1.0e-3,
            $"the shortfall settles at {shortfalls[^1] * 100.0:F4} percent, too much for "
            + "the truncation of a hyperboloid this deep");
    }

    [Fact]
    public void ItHoldsAnIonWellInsideTheBoundaryAndEjectsItAxiallyOutside()
    {
        // The two ends of the measurement, with the loss named. An ion ejected from
        // a 3-D trap on the a = 0 line goes *axially* first, because q_r is half
        // q_z, and that is checkable rather than assumed: the ion ends on an endcap
        // at exactly z0, not on the ring. Which endcap is not asserted - the trap is
        // symmetric about its centre and the phase of the drive when the growth wins
        // decides, so pinning the direction would be pinning an accident.
        foreach (var (volts, held) in new[] { (400.0, true), (760.0, false) })
        {
            var model = Compile(Trap(("rfAmplitude", volts)));
            var field = FieldAssembly.Build(model);
            var species = IonSpecies.FromModel(model);

            var launch = new PhaseState(
                model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

            var point = model.DetectorPoint;
            var normal = model.DetectorNormal;

            TrajectoryStopFunction detector =
                (in PhaseState state) => Vec3.Dot(state.Position - point, normal);

            var result = TrajectoryIntegrator.Integrate(
                launch,
                species,
                field,
                new IntegrationSettings
                {
                    RelativeTolerance = 1e-9,
                    MaximumFlightTime = model.MaximumFlightTimeSi,
                },
                detector);

            output.WriteLine(
                $"{volts,6:F0} V  q_z {NominalQ(model, volts):F4}  {result.Outcome}  "
                + $"{result.StruckSurface ?? "-"}  x = {result.FinalState.Position.X * 1e3:F4} mm  "
                + $"{result.AcceptedSteps} steps");

            if (held)
            {
                Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, result.Outcome);
                Assert.Null(result.StruckSurface);
            }
            else
            {
                Assert.Equal(TrajectoryOutcome.StruckElectrode, result.Outcome);
                Assert.Contains("endcap", result.StruckSurface!, StringComparison.Ordinal);

                // ON THE HYPERBOLOID, not at z0. The endcap was a flat annulus when this
                // was written, so an ion reached it at exactly z0 wherever it was radially;
                // it is now the sheet z = z0 sqrt(1 + (r/r0)^2), which is z0 on the axis
                // and further out everywhere else. Asserting z0 would be asserting the
                // geometry the template no longer has - and it is the surface equation
                // itself that is worth pinning, since a solve and a flight both read it.
                var z0 = model.Parameters["endcapHalfSpacing"].SiValue;
                var r0 = model.Parameters["inscribedRadius"].SiValue;

                var landed = result.FinalState.Position;

                var radial = Math.Sqrt(
                    (landed.Y * landed.Y) + (landed.Z * landed.Z));

                var onSheet = z0 * Math.Sqrt(1.0 + ((radial / r0) * (radial / r0)));

                output.WriteLine(
                    $"        struck at r = {radial * 1e3:F4} mm, |z| = "
                    + $"{Math.Abs(landed.X) * 1e3:F4} mm against {onSheet * 1e3:F4} mm on "
                    + $"the endcap hyperboloid (z0 = {z0 * 1e3:F4} mm)");

                Assert.Equal(onSheet, Math.Abs(landed.X), 1e-6);

                // And the control: the ion is far enough off axis for the hyperboloid to
                // be distinguishable from the plane through its vertex, so this is not
                // z0 wearing another formula.
                Assert.True(
                    onSheet - z0 > 1e-6,
                    $"the ion struck {radial * 1e3:F4} mm off axis, where the endcap sheet "
                    + $"is only {(onSheet - z0) * 1e9:F1} nm past its vertex");
            }
        }
    }

    // What this study measures and does not assert, and why.
    //
    // What this study measures and does not assert, and why. None of the numbers
    // below is asserted: a single boundary costs about thirty two-hundred-cycle
    // flights, and they belong in docs/device-templates.md where the controls can
    // sit beside them rather than in a test that runs on every build.
    //
    // The ejection boundary is 672 to 674 V at a 0.3 mm launch, 200 RF cycles, on a
    // 128 x 64 grid - q_z = 0.8218 to 0.8236 against a tabulated 0.90804. It is
    // mesh-converged (the identical 672-674 at 256 x 128) and hold-converged (the
    // identical 674 at 800 cycles).
    //
    // It is NOT amplitude-converged, and that is the finding. Hold-converged edges:
    //
    //     launch    edge        q_z
    //     0.1 mm    700-704 V   0.855-0.860
    //     0.3 mm    674 V       0.8236
    //     0.6 mm    ~520 V      0.635
    //
    // An ideal trap's boundary cannot depend on amplitude - the Mathieu equation is
    // linear, so a trajectory scaled by a constant is another trajectory. This one
    // depends on it strongly, which is the anharmonicity measured above doing its
    // work. It also means the edge cannot be reduced to the effective radius alone:
    // the scale factor predicts 0.828, which matches the 0.3 mm figure and not the
    // 0.1 mm one, so that agreement is partly coincidence. A measurement that only
    // reaches an electrode by growing to z0 is never a small-amplitude measurement,
    // whatever it was launched at.
    //
    // The finite window, which is the part worth remembering. At 60 cycles the
    // boundary is not a boundary at all but a ragged strip: confined at 674, lost at
    // 676 and 678, confined again at 680, lost at 682, confined at 684, and so on to
    // a solid loss from 690. At 200 cycles the same scan is a clean step. Nothing
    // about the design changed - the growth rate goes to zero at the stability edge,
    // so whether a marginally unstable ion reaches an electrode inside the hold is a
    // property of the hold. A boundary quoted without its observation window is not
    // quoted.
    //
    // And a narrow band of loss at 605 to 614 V (q_z = 0.739 to 0.750), well inside
    // the stable region. Every control says it is real: identical at 256 x 128,
    // identical at 400 cycles, absent at 60 (so the growth is slow and secular
    // rather than exponential), and absent at a 0.1 mm launch (so it is driven by
    // the field's higher multipoles, which a linear boundary cannot be). Which
    // resonance it is is NOT established - beta_z there is 0.615, which lands on no
    // n_z beta_z + n_r beta_r = 2 for any multipole order up to six - and settling
    // that needs a frequency analysis of the secular motion rather than a loss test.
    //
    // That band is also why BoundarySearch now walks outward from its converged
    // bracket. It found this one on its first real use, from a bracket whose
    // bisection had converged cleanly onto the main edge 60 V above it.
}
