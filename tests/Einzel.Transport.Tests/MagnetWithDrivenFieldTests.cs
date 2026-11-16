using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Fields.Magnetic;
using Einzel.Transport.Collisions;
using Einzel.Transport.Integration;

namespace Einzel.Transport.Tests;

/// <summary>
/// A magnet beside a driven field keeps what the driven field does on its own: its waveform
/// edges are landed on, and an ion born late sees the drive as it stands at its birth and
/// the magnet as it always is.
/// </summary>
/// <remarks>
/// Both properties are carried by forwarding through <see cref="DrivenMagnetizedField"/>,
/// and both fail silently without it: <see cref="ITimeVaryingField.NextEdgeAfter"/> has a
/// default that answers "no edge", and a time shift around the whole field would drop B.
/// </remarks>
public sealed class MagnetWithDrivenFieldTests
{
    private const double PeriodSeconds = 1e-6;

    private static readonly IonSpecies Ion = IonSpecies.Create(Quantity.From(100.0, "u"), Quantity.From(1.0, "e"));

    private static PhaseState Start => new(new Vec3(1e-3, 5e-4, 0.0), new Vec3(2e3, 0.0, 0.0));

    /// <summary>
    /// A rectangular quadrupole drive inside a uniform B: the wrapper reports the drive's
    /// edges, every edge of the flight is met by end stages one ulp before it and one
    /// derivative at it, and landing rejects at most a tenth of the steps capping does.
    /// </summary>
    [Fact]
    public void AStepLandsOnAWaveformEdgeWithAMagnetPresent()
    {
        var drive = Quadrupole(new RfWaveform.Rectangular(0.5));
        var recorder = new Recording(drive);
        var field = MagnetizedField.Around(recorder, new UniformMagneticField(new Vec3(0.0, 0.0, 0.02)));

        var driven = Assert.IsType<DrivenMagnetizedField>(field);
        Assert.Equal(drive.NextEdgeAfter(0.0), driven.NextEdgeAfter(0.0));
        Assert.Equal(drive.NextEdgeAfter(0.7e-6), driven.NextEdgeAfter(0.7e-6));

        var land = Fly(field, land: true);

        // 3.25 periods: six edges, and the flight does not end on one.
        var edge = 0.0;

        for (var k = 0; k < 6; k++)
        {
            edge = drive.NextEdgeAfter(edge);
            var before = Math.BitDecrement(edge);

            // Stages 6 and 7 read the near side at each attempt at the landing step.
            var near = recorder.Times.Count(t => t == before);
            Assert.True(near >= 2, $"edge {k}: {near} samples one ulp before it");

            // The far side is sampled once, by the integrator after it has accepted the step.
            // This is the sampling contract, not a flown number: the derivative upstream
            // recomputes at the result for a magnet is discarded on a landing, and must not
            // put a second sample at the edge.
            Assert.Equal(1, recorder.Times.Count(t => t == edge));
        }

        var cap = Fly(field, land: false);

        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, land.Outcome);
        Assert.True(cap.RejectedSteps > 0, "capping rejected nothing, so the comparison says nothing");
        Assert.True(
            10 * land.RejectedSteps <= cap.RejectedSteps,
            $"land rejected {land.RejectedSteps}, cap {cap.RejectedSteps}");
    }

    /// <summary>
    /// A row born 0.3 periods late in a sinusoidal field beside a magnet ends where the same
    /// ion ends in a field written out by hand with the clock moved and B kept, to the bit;
    /// not where it ends born at zero, and not where it ends with the magnet dropped.
    /// </summary>
    [Fact]
    public void ABirthTimeShiftsTheDriveAndKeepsTheMagnet()
    {
        const double birth = 0.3 * PeriodSeconds;

        var drive = OscillatingUniformField.Create(new Vec3(2e4, 0.0, 0.0), Quantity.From(1.0, "MHz"));
        var magnet = new UniformMagneticField(new Vec3(0.0, 0.0, 0.05));
        var field = MagnetizedField.Around(drive, magnet);
        var settings = new IntegrationSettings { RelativeTolerance = 1e-10, MaximumFlightTime = 3.25 * PeriodSeconds };

        var late = FlyRow(field, birth, settings);
        var reference = TrajectoryIntegrator.Integrate(Start, Ion, new Delayed(drive, magnet, birth), settings);

        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, late.Outcome);
        Assert.Equal(reference.FinalState, late.FinalState);
        Assert.Equal(reference.AcceptedSteps, late.AcceptedSteps);

        // The controls: the shift and the magnet each move the ion by far more than rounding.
        var atZero = FlyRow(field, 0.0, settings);
        var noMagnet = FlyRow(drive, birth, settings);

        Assert.True((late.FinalState.Position - atZero.FinalState.Position).Length > 1e-4);
        Assert.True((late.FinalState.Position - noMagnet.FinalState.Position).Length > 1e-4);
    }

    /// <summary>
    /// The two together, with a hard-edged magnet: a rectangular drive, a magnet in a box
    /// and a birth time. The shifted field reports the edges the shifted drive reports on
    /// its own, B and the magnet's faces where the unshifted field has them, and a row born
    /// late lands on the edges it meets.
    /// </summary>
    [Fact]
    public void ARowBornLateLandsOnEdgesBesideAHardEdgedMagnet()
    {
        const double birth = 0.3 * PeriodSeconds;

        var drive = Quadrupole(new RfWaveform.Rectangular(0.5));
        var flux = new Vec3(0.0, 0.0, 0.02);
        var magnet = new BoundedMagneticField(
            new UniformMagneticField(flux), new FieldRegion(-0.002, 0.002, -0.05, 0.05, -0.05, 0.05));
        var field = Assert.IsType<DrivenMagnetizedField>(MagnetizedField.Around(drive, magnet));
        var shifted = TimeShiftedField.Shift(field, birth);
        var alone = new TimeShiftedField(drive, birth);

        // The drive's clock is moved: the first edge is 0.2 periods away, not 0.5.
        Assert.Equal(alone.NextEdgeAfter(0.0), shifted.NextEdgeAfter(0.0));
        Assert.Equal(alone.NextEdgeAfter(0.45e-6), shifted.NextEdgeAfter(0.45e-6));
        Assert.InRange(shifted.NextEdgeAfter(0.0), 0.19e-6, 0.21e-6);

        // The magnet is not: B and its faces are where they were.
        var inside = new Vec3(0.001, 0.0, 0.0);
        var outside = new Vec3(0.003, 0.0, 0.0);
        var towards = new Vec3(-1.0, 0.0, 0.0);
        var magnetic = Assert.IsAssignableFrom<IMagneticField>(shifted);

        Assert.Equal(flux, magnetic.MagneticFieldAt(in inside));
        Assert.Equal(0.0, magnetic.MagneticFieldAt(in outside).Length);
        Assert.True(double.IsFinite(field.SignedDistanceToDiscontinuity(in inside)));
        Assert.Equal(field.SignedDistanceToDiscontinuity(in inside), shifted.SignedDistanceToDiscontinuity(in inside));
        Assert.Equal(field.SignedDistanceToDiscontinuity(in outside), shifted.SignedDistanceToDiscontinuity(in outside));
        Assert.Equal(field.MagnetFreeRunLength(in outside, in towards), magnetic.MagnetFreeRunLength(in outside, in towards));
        Assert.Equal(1e-3, magnetic.MagnetFreeRunLength(in outside, in towards), 12);

        // And flown: the born-late row rejects at most a tenth of the steps it rejects capped.
        IntegrationSettings Settings(bool land) => new()
        {
            RelativeTolerance = 1e-11, MaximumFlightTime = 3.25 * PeriodSeconds, LandOnEdges = land,
        };

        var land = FlyRow(field, birth, Settings(true));
        var cap = FlyRow(field, birth, Settings(false));

        Assert.True(cap.RejectedSteps > 0, "capping rejected nothing, so the comparison says nothing");
        Assert.True(
            10 * land.RejectedSteps <= cap.RejectedSteps,
            $"land rejected {land.RejectedSteps}, cap {cap.RejectedSteps}");
    }

    private static TrajectoryResult FlyRow(IElectrostaticField field, double birth, IntegrationSettings settings)
    {
        TrajectoryStopFunction never = (in PhaseState _) => 1.0;
        var flight = Assert.Single(IonListFlight.Fly(
            [new IonRow(1, Ion, Start, birth, "row")], field, settings, never, BackgroundGas.Vacuum, 1, 1));

        Assert.Null(flight.Failure);
        return flight.Result!;
    }

    private static IdealQuadrupoleRf Quadrupole(RfWaveform waveform) =>
        IdealQuadrupoleRf.FromMathieu(
            0.0, 0.5, Quantity.From(100.0, "u"), Quantity.From(1.0, "e"), Quantity.From(1.0, "MHz"), Quantity.From(4.0, "mm"), waveform);

    private static TrajectoryResult Fly(IElectrostaticField field, bool land) =>
        TrajectoryIntegrator.Integrate(
            Start,
            Ion,
            field,
            new IntegrationSettings
            {
                RelativeTolerance = 1e-11,
                MaximumFlightTime = 3.25 * PeriodSeconds,
                LandOnEdges = land,
            });

    /// <summary>Forwards everything to the inner field and records each instant E is asked for.</summary>
    private sealed class Recording(ITimeVaryingField inner) : ITimeVaryingField
    {
        public List<double> Times { get; } = [];

        public double ShortestPeriodSeconds => inner.ShortestPeriodSeconds;

        public double ResolutionLength => inner.ResolutionLength;

        public double OscillatingResolutionLength => inner.OscillatingResolutionLength;

        public double NextSwitchAfter(double timeSeconds) => inner.NextSwitchAfter(timeSeconds);

        public double NextEdgeAfter(double timeSeconds) => inner.NextEdgeAfter(timeSeconds);

        public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds)
        {
            Times.Add(timeSeconds);
            return inner.ElectricFieldAt(in position, timeSeconds);
        }

        public double PotentialAt(in Vec3 position, double timeSeconds) => inner.PotentialAt(in position, timeSeconds);

        public Vec3 ElectricFieldAt(in Vec3 position) => inner.ElectricFieldAt(in position);

        public double PotentialAt(in Vec3 position) => inner.PotentialAt(in position);

        public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) =>
            inner.FieldFreeRunLength(in position, in direction);

        public double SignedDistanceToDiscontinuity(in Vec3 position) =>
            inner.SignedDistanceToDiscontinuity(in position);
    }

    /// <summary>
    /// The reference for a birth time, written without <see cref="TimeShiftedField"/> or
    /// <see cref="MagnetizedField"/>: the drive read at <c>t + offset</c>, B as it is.
    /// </summary>
    private sealed class Delayed(ITimeVaryingField drive, IMagneticField magnet, double offsetSeconds)
        : ITimeVaryingField, IMagneticField
    {
        public double ShortestPeriodSeconds => drive.ShortestPeriodSeconds;

        public double MonochromaticPeriodSeconds => drive.MonochromaticPeriodSeconds;

        public double ResolutionLength => drive.ResolutionLength;

        public double OscillatingResolutionLength => drive.OscillatingResolutionLength;

        public double MaximumFluxDensity => magnet.MaximumFluxDensity;

        public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds) =>
            drive.ElectricFieldAt(in position, timeSeconds + offsetSeconds);

        public double PotentialAt(in Vec3 position, double timeSeconds) =>
            drive.PotentialAt(in position, timeSeconds + offsetSeconds);

        public Vec3 ElectricFieldAt(in Vec3 position) => drive.ElectricFieldAt(in position, offsetSeconds);

        public double PotentialAt(in Vec3 position) => drive.PotentialAt(in position, offsetSeconds);

        public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) =>
            Math.Min(
                drive.FieldFreeRunLength(in position, in direction),
                magnet.MagnetFreeRunLength(in position, in direction));

        public Vec3 MagneticFieldAt(in Vec3 position) => magnet.MagneticFieldAt(in position);

        public double MagnetFreeRunLength(in Vec3 position, in Vec3 direction) =>
            magnet.MagnetFreeRunLength(in position, in direction);
    }
}
