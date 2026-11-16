using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Fields.Magnetic;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// A magnet's edges and its reach: how a bounded magnet composes with an electric
/// discontinuity, where its step caps apply, and what an ion entering one at speed meets.
/// </summary>
public sealed class MagneticEdgeTests(ITestOutputHelper output)
{
    private static IdealSingleStageReflectron Mirror() => IdealSingleStageReflectron.AtFirstOrderFocus(
        IonSpecies.FromMassToCharge(500.0, 1), Quantity.From(4.0, "kV"), Quantity.From(50.0, "mm"));

    private static FieldRegion Box(double minX, double maxX, double half = 0.05) =>
        new(minX, maxX, -half, half, -half, half);

    private static MagnetizedField Bounded(IElectrostaticField electric, Vec3 b, FieldRegion region) =>
        MagnetizedField.Around(electric, new BoundedMagneticField(new UniformMagneticField(b), region));

    [Fact]
    public void AMagnetAndAMirrorComposeTheirSignsAsAProduct()
    {
        // The mirror plane is x = 0 (negative outside, positive inside); the magnet's box runs
        // from x = -6 mm to +0.7 mm, so its exit face lies inside the mirror. An ion on its way
        // back out of the mirror at a depth of 0.5 mm is 0.2 mm from the magnet's exit face,
        // and 0.3 mm in front of the plane it is 1.0 mm from the magnet's faces: it has crossed
        // the mirror plane and nothing else, so the composed sign must flip. Taking the sign of
        // whichever surface is nearer gives -0.2 mm then -0.3 mm, the same sign, and the
        // landing on the mirror - the one that buys the reflectron its 1e-16 - is skipped.
        var field = Bounded(Mirror().Field, new Vec3(1e-3, 0.0, 0.0), Box(-0.006, 0.0007));

        var inside = field.SignedDistanceToDiscontinuity(new Vec3(0.0005, 0.0, 0.0));
        var outside = field.SignedDistanceToDiscontinuity(new Vec3(-0.0003, 0.0, 0.0));

        output.WriteLine($"depth 0.5 mm: {inside * 1e3:F4} mm; 0.3 mm in front: {outside * 1e3:F4} mm");

        Assert.NotEqual(Math.Sign(inside), Math.Sign(outside));
        Assert.Equal(0.0002, Math.Abs(inside), 1e-15);
        Assert.Equal(0.0003, Math.Abs(outside), 1e-15);
    }

    [Fact]
    public void ThePathFromOneSurfaceToAnotherCrossesNothingAndTheSignHolds()
    {
        // Inside the magnet's box and in front of the mirror plane, moving from where the box
        // face is nearer to where the plane is nearer: nothing is crossed, so the sign must
        // not change. Under the nearer-surface rule it flips half way, and a bisection is
        // spent hunting a surface that is not there.
        var field = Bounded(Mirror().Field, new Vec3(1e-3, 0.0, 0.0), Box(-0.010, 0.020));

        var signs = Enumerable.Range(1, 99)
            .Select(i => Math.Sign(field.SignedDistanceToDiscontinuity(new Vec3(-0.0001 * i, 0.0, 0.0))))
            .Distinct()
            .ToArray();

        Assert.Single(signs);
    }

    [Fact]
    public void TwoMagnetsComposeTheirSignsAsAProduct()
    {
        // Two overlapping boxes: crossing the first one's exit face while deep inside the
        // second must flip the sign whichever face is nearer.
        var magnets = new SuperposedMagneticField(
        [
            new BoundedMagneticField(new UniformMagneticField(new Vec3(0.0, 0.0, 0.1)), Box(0.0, 0.010)),
            new BoundedMagneticField(new UniformMagneticField(new Vec3(0.0, 0.0, 0.1)), Box(0.0095, 0.100)),
        ]);

        var before = magnets.SignedDistanceToMagneticDiscontinuity(new Vec3(0.0099, 0.0, 0.0));
        var after = magnets.SignedDistanceToMagneticDiscontinuity(new Vec3(0.0101, 0.0, 0.0));

        Assert.NotEqual(Math.Sign(before), Math.Sign(after));
    }

    [Theory]
    [InlineData(-0.006, 0.060)]
    public void AForceFreeMagnetAcrossTheMirrorPlaneLeavesTheClosedFormFlightTime(double entryFace, double exitFace)
    {
        // B along the axis of a one-dimensional reflectron exerts nothing (v x B = 0 exactly),
        // so the flight is the closed form - provided the landing on the mirror plane still
        // happens inside a magnet whose sign now multiplies the mirror's. The box straddles
        // the plane; the ion enters it, turns inside it and leaves it.
        //
        // Not asserted, and measured instead: a magnet face within a step of the mirror plane
        // (an entry face 0.2 mm in front of it, 5.0e-10; an exit face 0.7 mm behind it,
        // 1.1e-10). A step then crosses the plane AND the face, the product sees two crossings
        // as none, and the flight falls back to what the error controller makes of the jump -
        // the one-surface-per-step limit SuperposedField documents, met with a magnet's face
        // as the second surface. The nearer-surface rule this replaced did no better there
        // (1.4e-10 and 2.0e-10 at exit faces of 1.5 and 3 mm). Nor asserted: an entry face
        // 20 mm out, which gives 2.0e-13 for an electric reason. The analytic drift stops at the
        // magnet's entrance instead of at the mirror plane, so the plane is reached by a
        // Runge-Kutta step whose stages straddle the jump in E, and the error controller - not
        // a landing that starts on the plane - sets how well. That is the electric family the
        // Stage 1 findings record, and it is present for any electric jump approached by a step.
        //
        // With the nearer-surface rule this case is 1.7e-10 out.
        //
        // The turning-point cap is off on both sides, as in the reflectron's own precision
        // test, because with it on the reference itself is 1.3e-10 out and the landing's
        // effect would be hidden under it.
        var mirror = Mirror();
        var field = Bounded(mirror.Field, new Vec3(1e-3, 0.0, 0.0), Box(entryFace, exitFace));
        var settings = mirror.Settings() with { TurningPointStepFactor = 0.0 };

        var result = TrajectoryIntegrator.Integrate(
            mirror.LaunchState(), mirror.Species, field, settings, mirror.DetectorPlane());
        var plain = TrajectoryIntegrator.Integrate(
            mirror.LaunchState(), mirror.Species, mirror.Field, settings, mirror.DetectorPlane());

        var exact = mirror.ExactFlightTime();
        var error = Math.Abs(result.FlightTimeSeconds - exact) / exact;
        var plainError = Math.Abs(plain.FlightTimeSeconds - exact) / exact;

        output.WriteLine(
            $"faces {entryFace * 1e3} to {exitFace * 1e3} mm: relative error {error:E2} against {plainError:E2} with no magnet; "
            + $"{result.AcceptedSteps} steps, {result.FieldEvaluations} evaluations");

        Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);
        Assert.True(plainError < 1e-14, $"reference relative error {plainError:E3}");
        Assert.True(error < 1e-14, $"relative error {error:E3}");
    }

    [Fact]
    public void AMagnetWithZeroFluxDensityFliesBitForBitWithDefaultSettings()
    {
        var mirror = Mirror();
        var settings = mirror.Settings();

        var plain = TrajectoryIntegrator.Integrate(
            mirror.LaunchState(), mirror.Species, mirror.Field, settings, mirror.DetectorPlane());

        foreach (var field in new IElectrostaticField[]
        {
            MagnetizedField.Around(mirror.Field, new UniformMagneticField(Vec3.Zero)),
            Bounded(mirror.Field, Vec3.Zero, Box(-0.006, 0.010)),
        })
        {
            var magnetised = TrajectoryIntegrator.Integrate(
                mirror.LaunchState(), mirror.Species, field, settings, mirror.DetectorPlane());

            AssertSameFlight(plain, magnetised);
        }
    }

    [Fact]
    public void AMagnetTheIonNeverEntersFliesBitForBitWithDefaultSettings()
    {
        // Beside the flight path, and behind the launch plane: the ion never enters either.
        var mirror = Mirror();
        var settings = mirror.Settings();
        var b = new Vec3(0.0, 0.0, 0.5);

        var plain = TrajectoryIntegrator.Integrate(
            mirror.LaunchState(), mirror.Species, mirror.Field, settings, mirror.DetectorPlane());

        foreach (var region in new[]
        {
            new FieldRegion(-0.05, 0.05, 0.010, 0.020, -0.01, 0.01),
            new FieldRegion(-0.5, -0.2, -0.01, 0.01, -0.01, 0.01),
        })
        {
            var magnetised = TrajectoryIntegrator.Integrate(
                mirror.LaunchState(), mirror.Species, Bounded(mirror.Field, b, region), settings, mirror.DetectorPlane());

            AssertSameFlight(plain, magnetised);
        }
    }

    /// <summary>A metre of uniform accelerating field along x: no analytic drift, every step integrated.</summary>
    private static TrajectoryResult Beamline(IElectrostaticField? withMagnet, out IonSpecies species)
    {
        species = IonSpecies.FromMassToCharge(50.0, 1);
        var electric = UniformField.Create(Quantity.Si(10.0, Dimension.ElectricField), Vec3.UnitX);
        var launch = new PhaseState(Vec3.Zero, new Vec3(2.0e4, 0.0, 0.0));
        TrajectoryStopFunction end = (in PhaseState s) => 1.0 - s.Position.X;
        var settings = new IntegrationSettings { MaximumFlightTime = 1.0, RelativeTolerance = 1e-9 };

        return TrajectoryIntegrator.Integrate(launch, species, withMagnet ?? electric, settings, end);
    }

    private static IElectrostaticField BeamlineField =>
        UniformField.Create(Quantity.Si(10.0, Dimension.ElectricField), Vec3.UnitX);

    [Fact]
    public void ASmallMagnetDoesNotCapTheStepsOfTheBeamlineAroundIt()
    {
        // A strong magnet beside the beamline, and a short one on it whose B lies along the
        // velocity and so exerts nothing. A cyclotron cap taken once from the magnet's |B| and
        // applied to every step costs hundreds of steps over the metre (a twentieth of a 3.3 us
        // period, on a 50 us flight); taken from B where the ion is, it costs nothing beside
        // the beamline and only the magnet's own few steps on it.
        var plain = Beamline(null, out _);

        var beside = Beamline(
            Bounded(BeamlineField, new Vec3(0.0, 0.0, 1.0), new FieldRegion(0.4, 0.6, 0.010, 0.020, -0.01, 0.01)),
            out _);
        var through = Beamline(
            Bounded(BeamlineField, new Vec3(1.0, 0.0, 0.0), Box(0.100, 0.102, 0.01)), out _);

        output.WriteLine(
            $"{plain.AcceptedSteps} steps with no magnet, {beside.AcceptedSteps} beside one, "
            + $"{through.AcceptedSteps} through a 2 mm one");

        Assert.Equal(plain.AcceptedSteps, beside.AcceptedSteps);
        Assert.Equal(plain.FlightTimeSeconds, beside.FlightTimeSeconds);
        Assert.True(
            through.AcceptedSteps <= plain.AcceptedSteps + 30,
            $"{through.AcceptedSteps} steps through the magnet against {plain.AcceptedSteps} without it");
    }

    [Fact]
    public void AStepFromOutsideDoesNotCarryTheIonCleanThroughAThinMagnet()
    {
        // The uniform field lets the controller grow a step to most of the metre, and a 2 mm
        // magnet in the middle is then crossed in and out within one step. B along z turns the
        // ion: dv_y/dt = -omega v_x, so the transverse kick is exactly -omega times the 2 mm
        // crossed, whatever E along x does to v_x meanwhile.
        var result = Beamline(
            Bounded(BeamlineField, new Vec3(0.0, 0.0, 0.5), Box(0.500, 0.502, 0.01)), out var species);

        var kick = -species.ChargeSi * 0.5 / species.MassSi * 0.002;

        output.WriteLine(
            $"v_y {result.FinalState.Velocity.Y:F6} m/s against {kick:F6}; {result.AcceptedSteps} steps");

        Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);
        Assert.Equal(kick, result.FinalState.Velocity.Y, Math.Abs(kick) * 1e-6);
    }

    [Fact]
    public void TwoDisjointMagnetsCapEachAtItsOwnPeriod()
    {
        // Two equal magnets far apart: inside either, the cap is one twentieth of that
        // magnet's period, not of the period of their summed field.
        var species = IonSpecies.FromMassToCharge(100.0, 1);
        var b = new Vec3(0.0, 0.0, 0.5);
        var magnets = new SuperposedMagneticField(
        [
            new BoundedMagneticField(new UniformMagneticField(b), Box(-1.0, 1.0, 1.0)),
            new BoundedMagneticField(new UniformMagneticField(b), new FieldRegion(10.0, 11.0, -1.0, 1.0, -1.0, 1.0)),
        ]);
        var one = MagnetizedField.Around(FieldFreeSpace.Instance, new UniformMagneticField(b));
        var two = MagnetizedField.Around(FieldFreeSpace.Instance, magnets);

        var v = species.SpeedAfterAcceleration(Quantity.From(1000.0, "V")).SiValue;
        var period = 2.0 * Math.PI * species.MassSi / (species.ChargeSi * b.Length);
        var settings = new IntegrationSettings
        {
            MaximumFlightTime = 5.0 * period,
            RelativeTolerance = 1e-2,
            AbsolutePositionTolerance = 1e-2,
            AbsoluteVelocityTolerance = 1e3,
        };
        var launch = new PhaseState(Vec3.Zero, new Vec3(v, 0.0, 0.0));

        var single = TrajectoryIntegrator.Integrate(launch, species, one, settings);
        var pair = TrajectoryIntegrator.Integrate(launch, species, two, settings);

        output.WriteLine($"{single.AcceptedSteps} steps in one magnet, {pair.AcceptedSteps} inside one of two");

        Assert.Equal(single.AcceptedSteps, pair.AcceptedSteps);
    }

    [Theory]
    [InlineData(5.48579909e-4, -1, 1.0)]
    [InlineData(5.48579909e-4, -1, 0.1)]
    [InlineData(100.0, 1, 0.5)]
    public void AnIonEnteringAHardEdgeMagnetFromADriftTurnsWithoutUnderflow(
        double massToCharge, int charge, double tesla)
    {
        // Field-free outside, so the approach is one analytic drift to the face; inside, a
        // semicircle back out. If the drift leaves the ion a rounding outside the box, the next
        // step's first stage sees B = 0 and its others full B, and for an electron at a tesla
        // the step that makes that error acceptable is below MinimumStep.
        var species = IonSpecies.FromMassToCharge(massToCharge, charge);
        var b = new Vec3(0.0, 0.0, tesla);
        var field = Bounded(FieldFreeSpace.Instance, b, new FieldRegion(0.0, 1.0, -1.0, 1.0, -1.0, 1.0));
        var v = species.SpeedAfterAcceleration(Quantity.From(1000.0, "V")).SiValue;
        var omega = Math.Abs(species.ChargeSi) * tesla / species.MassSi;

        // Launched from a position that is not a round number, so the drift's endpoint is
        // rounded and may land either side of the face.
        var start = -0.0123456789;
        var launch = new PhaseState(new Vec3(start, 0.0, 0.0), new Vec3(v, 0.0, 0.0));
        TrajectoryStopFunction detector = (in PhaseState s) => s.Position.X + 0.005;

        var result = TrajectoryIntegrator.Integrate(
            launch, species, field, new IntegrationSettings { MaximumFlightTime = 1.0 }, detector);

        var exact = ((-start + 0.005) / v) + (Math.PI / omega);
        var radius = v / omega;
        var side = Math.Sign(charge);

        output.WriteLine(
            $"m/z {massToCharge} at {tesla} T: {result.Outcome}, {result.AcceptedSteps} steps, "
            + $"{result.RejectedSteps} rejected, t = {result.FlightTimeSeconds:E12} against {exact:E12}, "
            + $"y = {result.FinalState.Position.Y:E9} against {-2.0 * radius * side:E9}");

        Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);
        Assert.Equal(exact, result.FlightTimeSeconds, exact * 1e-9);
        Assert.Equal(-2.0 * radius * side, result.FinalState.Position.Y, radius * 1e-8);
    }

    private static void AssertSameFlight(TrajectoryResult expected, TrajectoryResult actual)
    {
        Assert.Equal(expected.Outcome, actual.Outcome);
        Assert.Equal(expected.FlightTimeSeconds, actual.FlightTimeSeconds);
        Assert.Equal(expected.FinalState.Position, actual.FinalState.Position);
        Assert.Equal(expected.FinalState.Velocity, actual.FinalState.Velocity);
        Assert.Equal(expected.AcceptedSteps, actual.AcceptedSteps);
        Assert.Equal(expected.RejectedSteps, actual.RejectedSteps);
        Assert.Equal(expected.AnalyticDriftDistance, actual.AnalyticDriftDistance);
        Assert.Equal(expected.MaximumRelativeEnergyDrift, actual.MaximumRelativeEnergyDrift);
    }
}
