using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Fields.Magnetic;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// The v x B term against closed forms the engine has no part in: cyclotron motion, the
/// E x B drift, a Wien filter and a hard-edge 180 degree sector.
/// </summary>
/// <remarks>
/// Every expectation is arithmetic written here from the textbook solution of
/// <c>m dv/dt = q (E + v x B)</c> in uniform fields. Each test fails for a specific
/// breakage: the sense tests if the cross product is written the wrong way round, all of
/// them if B is ignored, the cyclotron and sector tests if the field-free drift shortcut is
/// taken inside a magnet.
/// </remarks>
public sealed class MagneticFieldTests(ITestOutputHelper output)
{
    private const double FluxDensity = 0.5;

    private static IonSpecies Light => IonSpecies.FromMassToCharge(100.0, 1);

    private static double Speed(IonSpecies species, double volts) =>
        species.SpeedAfterAcceleration(Quantity.From(volts, "V")).SiValue;

    private static double Omega(IonSpecies species, double b) =>
        Math.Abs(species.ChargeSi) * b / species.MassSi;

    private static MagnetizedField Magnet(Vec3 b, IElectrostaticField? electric = null) =>
        MagnetizedField.Around(electric ?? FieldFreeSpace.Instance, new UniformMagneticField(b));

    /// <summary>Quarter turn: launched along +x, v_x first reaches zero at (r, -r).</summary>
    private static TrajectoryResult QuarterTurn(IonSpecies species, double volts, Vec3 b, Vec3? extra = null)
    {
        var v = Speed(species, volts);
        var launch = new PhaseState(Vec3.Zero, new Vec3(v, 0.0, 0.0) + (extra ?? Vec3.Zero));
        var settings = new IntegrationSettings { MaximumFlightTime = 1.0, RelativeTolerance = 1e-12 };

        // The stop function is the x velocity: positive at launch, negative after a quarter
        // turn whichever way the ion goes round.
        TrajectoryStopFunction stop = (in PhaseState s) => s.Velocity.X;

        return TrajectoryIntegrator.Integrate(launch, species, Magnet(b), settings, stop);
    }

    [Theory]
    [InlineData(1000.0)]
    [InlineData(4000.0)]
    public void ACyclotronQuarterTurnHasTheClosedFormRadiusAndPeriod(double volts)
    {
        var species = Light;
        var v = Speed(species, volts);
        var omega = Omega(species, FluxDensity);
        var radius = v / omega;

        var result = QuarterTurn(species, volts, new Vec3(0.0, 0.0, FluxDensity));

        output.WriteLine(
            $"{volts} V: r = {result.FinalState.Position.X * 1e3:F9} mm against {radius * 1e3:F9}; "
            + $"t = {result.FlightTimeSeconds * 1e9:F6} ns against {Math.PI / (2.0 * omega) * 1e9:F6}; "
            + $"{result.AcceptedSteps} steps");

        Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);

        // r = m v / (|q| B), and the ion is at (r, -r) after a quarter turn.
        Assert.Equal(radius, result.FinalState.Position.X, radius * 1e-9);
        Assert.Equal(-radius, result.FinalState.Position.Y, radius * 1e-9);

        // T/4 = pi m / (2 |q| B): independent of the energy, which is what the two
        // InlineData rows say together.
        var quarter = Math.PI / (2.0 * omega);
        Assert.Equal(quarter, result.FlightTimeSeconds, quarter * 1e-9);

        // Never a drift: the shortcut assumes a straight line, which an ion in B is not on.
        Assert.Equal(0.0, result.AnalyticDriftDistance);
    }

    [Fact]
    public void APositiveIonTurnsClockwiseSeenFromBAndTheSenseReversesWithBOrCharge()
    {
        var b = new Vec3(0.0, 0.0, FluxDensity);

        // F = q v x B: +x cross +z is -y, so a cation going +x is pushed to -y.
        var cation = QuarterTurn(Light, 1000.0, b);
        var reversed = QuarterTurn(Light, 1000.0, -b);
        var anion = QuarterTurn(IonSpecies.FromMassToCharge(100.0, -1), 1000.0, b);

        Assert.True(cation.FinalState.Position.Y < 0.0, $"cation y {cation.FinalState.Position.Y}");
        Assert.True(reversed.FinalState.Position.Y > 0.0, $"reversed-B y {reversed.FinalState.Position.Y}");
        Assert.True(anion.FinalState.Position.Y > 0.0, $"anion y {anion.FinalState.Position.Y}");

        // And by the same amount, so the mirror image is exact rather than merely opposite.
        Assert.Equal(-cation.FinalState.Position.Y, reversed.FinalState.Position.Y, 1e-15);
    }

    [Fact]
    public void TheVelocityAlongBIsUntouched()
    {
        var species = Light;
        var along = 12345.0;
        var result = QuarterTurn(species, 1000.0, new Vec3(0.0, 0.0, FluxDensity), new Vec3(0.0, 0.0, along));

        Assert.Equal(along, result.FinalState.Velocity.Z, 1e-9 * along);
        Assert.Equal(along * result.FlightTimeSeconds, result.FinalState.Position.Z, 1e-12);
    }

    [Fact]
    public void KineticEnergyIsConservedOverAHundredOrbitsAndTheOrbitCloses()
    {
        // B does no work, so speed is a constant of the motion and the energy diagnostic -
        // total energy, here entirely kinetic - stays meaningful in a magnet.
        var species = Light;
        var v = Speed(species, 1000.0);
        var omega = Omega(species, FluxDensity);
        var period = 2.0 * Math.PI / omega;
        var radius = v / omega;
        const int Orbits = 100;

        var launch = new PhaseState(Vec3.Zero, new Vec3(v, 0.0, 0.0));
        var settings = new IntegrationSettings { MaximumFlightTime = Orbits * period, RelativeTolerance = 1e-11 };

        var result = TrajectoryIntegrator.Integrate(
            launch, species, Magnet(new Vec3(0.0, 0.0, FluxDensity)), settings);

        var speedError = Math.Abs(result.FinalState.Speed - v) / v;
        var closure = result.FinalState.Position.Length / radius;

        output.WriteLine(
            $"{Orbits} orbits: energy drift {result.MaximumRelativeEnergyDrift:E3}, speed error "
            + $"{speedError:E3}, closure {closure:E3} of a radius, {result.AcceptedSteps} steps "
            + $"({(double)result.AcceptedSteps / Orbits:F1} per orbit)");

        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, result.Outcome);
        Assert.True(result.MaximumRelativeEnergyDrift < 1e-8, $"energy drift {result.MaximumRelativeEnergyDrift:E3}");
        Assert.True(speedError < 1e-8, $"speed error {speedError:E3}");

        // After a whole number of periods the ion is back where it started: a period
        // independent of energy and a radius m v / qB, compounded a hundred times.
        Assert.True(closure < 1e-6, $"after {Orbits} orbits the ion is {closure:E3} of a radius from its start");
    }

    [Fact]
    public void ALooseToleranceStillTakesTwentyStepsATurn()
    {
        // The cyclotron cap: a step may span at most a twentieth of a turn whatever the
        // tolerance lets the controller grow to.
        var species = Light;
        var v = Speed(species, 1000.0);
        var period = 2.0 * Math.PI / Omega(species, FluxDensity);

        var launch = new PhaseState(Vec3.Zero, new Vec3(v, 0.0, 0.0));
        var settings = new IntegrationSettings
        {
            MaximumFlightTime = 10.0 * period,
            RelativeTolerance = 1e-2,
            AbsolutePositionTolerance = 1e-2,
            AbsoluteVelocityTolerance = 1e3,
        };

        var result = TrajectoryIntegrator.Integrate(
            launch, species, Magnet(new Vec3(0.0, 0.0, FluxDensity)), settings);

        output.WriteLine($"{result.AcceptedSteps} steps over 10 turns at a 1e-2 tolerance");
        Assert.True(result.AcceptedSteps >= 200, $"{result.AcceptedSteps} steps over 10 turns");
    }

    [Theory]
    [InlineData(100.0, 1)]
    [InlineData(300.0, -1)]
    public void ExBDriftIsEOverBWhateverTheMassOrSignOfCharge(double massToCharge, int charge)
    {
        // Released from rest in crossed E (along y) and B (along z) an ion traces a cycloid
        // and, after every whole cyclotron period, is back at rest a distance (E x B / B^2) T
        // further on - for either sign of charge, which only reverses the sense of the loops.
        var species = IonSpecies.FromMassToCharge(massToCharge, charge);
        var e = 2000.0;
        var b = new Vec3(0.0, 0.0, FluxDensity);
        var electric = UniformField.Create(Quantity.Si(e, Dimension.ElectricField), Vec3.UnitY);
        var drift = Vec3.Cross(new Vec3(0.0, e, 0.0), b) * (1.0 / b.LengthSquared);

        var period = 2.0 * Math.PI / Omega(species, FluxDensity);
        const int Periods = 7;

        var settings = new IntegrationSettings { MaximumFlightTime = Periods * period, RelativeTolerance = 1e-12 };
        var result = TrajectoryIntegrator.Integrate(
            new PhaseState(Vec3.Zero, Vec3.Zero), species, Magnet(b, electric), settings);

        var measured = result.FinalState.Position * (1.0 / result.FlightTimeSeconds);

        output.WriteLine(
            $"m/z {massToCharge}, z = {charge}: drift ({measured.X:F6}, {measured.Y:E2}) m/s against "
            + $"({drift.X:F6}, 0); speed at the end {result.FinalState.Speed:E3} m/s");

        Assert.Equal(drift.X, measured.X, Math.Abs(drift.X) * 1e-8);
        Assert.True(Math.Abs(measured.Y) < Math.Abs(drift.X) * 1e-8, $"drift along E {measured.Y:E3}");
        Assert.True(result.FinalState.Speed < drift.Length * 1e-6, $"not at rest: {result.FinalState.Speed:E3}");
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.2)]
    [InlineData(0.8)]
    public void AWienFilterPassesEOverBStraightAndDeflectsOthersByTheClosedForm(double speedRatio)
    {
        // E along +y, B along +z: the electric push q E y and the magnetic push
        // -q v B y cancel at v = E / B. Any other speed gyrates about that drift:
        // x = v_d t + (v0 - v_d)/w sin(wt), y = (v0 - v_d)/w (cos(wt) - 1).
        var species = Light;
        var e = 50000.0;
        var vd = e / FluxDensity;
        var v0 = speedRatio * vd;
        var omega = Omega(species, FluxDensity);
        const double Length = 0.05;

        var field = Magnet(
            new Vec3(0.0, 0.0, FluxDensity),
            UniformField.Create(Quantity.Si(e, Dimension.ElectricField), Vec3.UnitY));

        TrajectoryStopFunction detector = (in PhaseState s) => Length - s.Position.X;
        var settings = new IntegrationSettings { MaximumFlightTime = 1.0, RelativeTolerance = 1e-12 };

        var result = TrajectoryIntegrator.Integrate(
            new PhaseState(Vec3.Zero, new Vec3(v0, 0.0, 0.0)), species, field, settings, detector);

        // Newton on x(t) = L, monotone because v0 > 0 and |v0 - v_d| < v_d.
        var a = (v0 - vd) / omega;
        var t = Length / vd;

        for (var i = 0; i < 60; i++)
        {
            t -= ((vd * t) + (a * Math.Sin(omega * t)) - Length) / (vd + (a * omega * Math.Cos(omega * t)));
        }

        var y = a * (Math.Cos(omega * t) - 1.0);

        output.WriteLine(
            $"v0 = {speedRatio} v_d: y = {result.FinalState.Position.Y * 1e3:F9} mm against {y * 1e3:F9}, "
            + $"t = {result.FlightTimeSeconds * 1e6:F9} us against {t * 1e6:F9}");

        Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);
        Assert.Equal(t, result.FlightTimeSeconds, t * 1e-9);

        if (speedRatio == 1.0)
        {
            Assert.True(Math.Abs(result.FinalState.Position.Y) < 1e-12, $"y = {result.FinalState.Position.Y:E3}");
        }
        else
        {
            Assert.Equal(y, result.FinalState.Position.Y, Math.Abs(y) * 1e-7);

            // Faster than v_d: the magnetic push wins and the ion goes to -y. Slower: +y.
            Assert.Equal(speedRatio > 1.0 ? -1 : 1, Math.Sign(result.FinalState.Position.Y));
        }
    }

    /// <summary>A hard-edge sector filling x &gt; 0: B along +z.</summary>
    private static TrajectoryResult Sector(IonSpecies species, double volts)
    {
        var region = new FieldRegion(0.0, 1.0, -1.0, 1.0, -1.0, 1.0);
        var magnet = new BoundedMagneticField(new UniformMagneticField(new Vec3(0.0, 0.0, FluxDensity)), region);
        var field = MagnetizedField.Around(FieldFreeSpace.Instance, magnet);

        // Launched 10 mm before the edge going +x; the detector is the plane x = -5 mm, met
        // on the way back after the semicircle (the stop arms once the ion passes it going out).
        var v = Speed(species, volts);
        var launch = new PhaseState(new Vec3(-0.010, 0.0, 0.0), new Vec3(v, 0.0, 0.0));
        TrajectoryStopFunction detector = (in PhaseState s) => s.Position.X + 0.005;

        return TrajectoryIntegrator.Integrate(
            launch, species, field, new IntegrationSettings { MaximumFlightTime = 1.0, RelativeTolerance = 1e-12 },
            detector);
    }

    [Fact]
    public void A180DegreeSectorReturnsTheIonAtTwoRadiiAfterHalfAPeriod()
    {
        var species = Light;
        var v = Speed(species, 1000.0);
        var omega = Omega(species, FluxDensity);
        var radius = v / omega;

        var result = Sector(species, 1000.0);

        // 15 mm of straight flight at v, and half a cyclotron period in the magnet.
        var exact = (0.015 / v) + (Math.PI / omega);

        output.WriteLine(
            $"exit y = {result.FinalState.Position.Y * 1e3:F9} mm against {-2.0 * radius * 1e3:F9} "
            + $"(relative {Math.Abs((result.FinalState.Position.Y / (-2.0 * radius)) - 1.0):E2}, exit angle {result.FinalState.Velocity.Y / v:E2} rad); "
            + $"t = {result.FlightTimeSeconds * 1e6:F9} us against {exact * 1e6:F9}; analytic drift "
            + $"{result.AnalyticDriftDistance * 1e3:F6} mm, {result.AcceptedSteps} steps, {result.RejectedSteps} rejected, {result.FieldEvaluations} evaluations");

        Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);
        Assert.Equal(-2.0 * radius, result.FinalState.Position.Y, radius * 1e-9);
        Assert.Equal(-v, result.FinalState.Velocity.X, v * 1e-9);
        Assert.Equal(exact, result.FlightTimeSeconds, exact * 1e-10);

        // Outside the magnet the flight is straight and is taken as an exact drift: the 10 mm
        // in and the 5 mm out. Inside, never.
        Assert.Equal(0.015, result.AnalyticDriftDistance, 1e-9);
    }

    [Fact]
    public void ASectorDispersesMassAsTheSquareRootOfTheMass()
    {
        // At one accelerating potential r = sqrt(2 m V / q) / B, so two masses land 2r apart
        // in the ratio sqrt(m1/m2): the mass spectrograph.
        var light = Sector(IonSpecies.FromMassToCharge(100.0, 1), 1000.0);
        var heavy = Sector(IonSpecies.FromMassToCharge(121.0, 1), 1000.0);

        var ratio = heavy.FinalState.Position.Y / light.FinalState.Position.Y;
        output.WriteLine($"exit ratio {ratio:F12} against {Math.Sqrt(121.0 / 100.0):F12}");

        Assert.Equal(1.1, ratio, 1e-9);
    }

    [Fact]
    public void AZeroBAddsNothingToAFlightBitForBit()
    {
        // The magnetic path with B = 0 must be the electric path exactly: the cross product
        // adds zeros, no cyclotron cap applies, and the drift bound is +infinity. The
        // turning-point cap is the one thing a magnetised field changes regardless of B, so it
        // is off on both sides here. The half-space mirror has a discontinuity to land on and
        // a field-free drift to take, so both of those paths are compared too.
        var mirror = IdealSingleStageReflectron.AtFirstOrderFocus(
            IonSpecies.FromMassToCharge(500.0, 1), Quantity.From(4.0, "kV"), Quantity.From(50.0, "mm"));

        var settings = new IntegrationSettings
        {
            MaximumFlightTime = 1e-3,
            TurningPointStepFactor = 0.0,
            RelativeTolerance = 1e-11,
        };

        var plain = TrajectoryIntegrator.Integrate(
            mirror.LaunchState(), mirror.Species, mirror.Field, settings, mirror.DetectorPlane());

        var magnetised = TrajectoryIntegrator.Integrate(
            mirror.LaunchState(), mirror.Species, Magnet(Vec3.Zero, mirror.Field), settings, mirror.DetectorPlane());

        Assert.Equal(plain.FlightTimeSeconds, magnetised.FlightTimeSeconds);
        Assert.Equal(plain.FinalState.Position, magnetised.FinalState.Position);
        Assert.Equal(plain.FinalState.Velocity, magnetised.FinalState.Velocity);
        Assert.Equal(plain.AcceptedSteps, magnetised.AcceptedSteps);
        Assert.Equal(plain.AnalyticDriftDistance, magnetised.AnalyticDriftDistance);
    }

    [Fact]
    public void ThePacketIntegratorRefusesAMagneticField()
    {
        var field = Magnet(new Vec3(0.0, 0.0, FluxDensity));
        var launch = new[] { new PhaseState(Vec3.Zero, Vec3.UnitX), new PhaseState(Vec3.UnitY, Vec3.UnitX) };

        var refused = Assert.Throws<Core.Errors.EinzelException>(() => Interaction.PacketIntegrator.Fly(
            launch, Light, field, null, new IntegrationSettings { MaximumFlightTime = 1e-6 },
            (in PhaseState s) => 1.0));

        Assert.Contains("space-charge packet integrator", refused.Error.Constraint, StringComparison.Ordinal);
    }
}
