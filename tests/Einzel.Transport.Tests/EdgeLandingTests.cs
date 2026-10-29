using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// <see cref="IntegrationSettings.LandOnEdges"/>: a step lands on every waveform edge, samples
/// the near side at its end and restarts on the far side, an edge closer than the minimum step
/// is taken in place, and the default flies as it always did.
/// </summary>
/// <remarks>
/// The reference is the exact transfer-matrix solution of the Meissner equation for an ideal
/// quadrupole driven by a rectangular wave: piecewise-constant forces have closed-form
/// solutions between edges, so the product of the per-segment matrices is the trajectory to
/// the precision it is evaluated at (mpmath, 50 digits; the project's
/// <c>21_edge_events/reference/test_values.json</c>). Errors are relative, position against
/// <c>max(|ref|, x0)</c> and velocity against <c>max(|ref|, omega x0)</c>.
/// </remarks>
public sealed class EdgeLandingTests(ITestOutputHelper output)
{
    private const double X0 = 1e-3;

    private static readonly IonSpecies Ion = IonSpecies.Create(Quantity.From(100.0, "u"), Quantity.From(1.0, "e"));

    /// <summary>
    /// The Meissner rows (test A) at rtol 1e-11. The bound per row is ten times what this
    /// build measured under <c>land</c> and never looser than 1e-8. <c>land</c> must also
    /// reject at most a tenth of what <c>cap</c> rejects on the same row, and at 100 periods
    /// leave at most a tenth of its error.
    /// </summary>
    /// <param name="duty">The duty cycle.</param>
    /// <param name="q">The Mathieu q.</param>
    /// <param name="periods">The flight, in drive periods.</param>
    /// <param name="bound">The relative error <c>land</c> must stay under.</param>
    /// <param name="x">Reference x, in metres.</param>
    /// <param name="y">Reference y, in metres.</param>
    /// <param name="vx">Reference vx, in metres per second.</param>
    /// <param name="vy">Reference vy, in metres per second.</param>
    [Theory]
    [InlineData(0.5, 0.5, 1, 1e-11, -0.002301298902307295, 0.001150649451153647, -7882.816675097764, -3941.408337548882)]
    [InlineData(0.5, 0.5, 10, 1e-10, -0.001000000000000002, -0.000499999999999999, -6.736441151140501e-12, -3.368220575570251e-12)]
    [InlineData(0.5, 0.5, 100, 1e-9, 0.00100000000000002, 0.0004999999999999903, 6.736441151140502e-11, 3.368220575570251e-11)]
    [InlineData(0.5, 0.45, 100, 1e-9, 0.001078396211768642, 0.0004601172555219732, 241.8648670830692, 120.9324335415346)]
    [InlineData(0.5, 0.7, 20, 2e-10, 0.007879402164680508, -0.003492942359022126, 37183.20622775961, 18591.60311387981)]
    [InlineData(0.3, 0.4, 5, 2e-10, -0.1971502428833924, -0.0006528493183553122, -529063.2241645288, 2555.972081194869)]
    public void LandingMeetsTheExactMeissnerSolution(
        double duty, double q, int periods, double bound, double x, double y, double vx, double vy)
    {
        var field = Quadrupole(q, new RfWaveform.Rectangular(duty));
        var land = Fly(field, periods, 1e-11, land: true);
        var cap = Fly(field, periods, 1e-11, land: false);
        var omegaX0 = Math.PI * 1e6 * Math.Sqrt(2.0 * q) * X0;
        var landError = Error(land, x, y, vx, vy, omegaX0);
        var capError = Error(cap, x, y, vx, vy, omegaX0);

        output.WriteLine(
            $"d {duty} q {q} N {periods}: land {landError:E2} ({land.AcceptedSteps} steps, {land.RejectedSteps} rejected), "
            + $"cap {capError:E2} ({cap.AcceptedSteps} steps, {cap.RejectedSteps} rejected)");

        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, land.Outcome);
        Assert.True(landError < bound, $"land error {landError:E2} over {bound:E0}");
        Assert.True(10 * land.RejectedSteps <= cap.RejectedSteps, $"land rejected {land.RejectedSteps}, cap {cap.RejectedSteps}");

        if (periods == 100)
        {
            Assert.True(10 * landError <= capError, $"land {landError:E2} against cap {capError:E2}");
        }
    }

    /// <summary>
    /// The default is the integrator it was: a <c>cap</c> flight's final state is, to the bit,
    /// the one recorded at <c>17352b1</c> (before landing existed) for the Meissner row
    /// d = 0.5, q = 0.5, ten periods, rtol 1e-11, with the same step counts. So the
    /// end-sample argument's default reproduces <c>t + h</c> and nothing on the default path
    /// moved.
    /// </summary>
    [Fact]
    public void TheDefaultReproducesTheRecordedFlightToTheBit()
    {
        var result = Fly(Quadrupole(0.5, new RfWaveform.Rectangular(0.5)), 10, 1e-11, land: false);

        Assert.Equal(-0.0009999999839435216, result.FinalState.Position.X);
        Assert.Equal(-0.0005000000083226206, result.FinalState.Position.Y);
        Assert.Equal(3.2050050134735386e-05, result.FinalState.Velocity.X);
        Assert.Equal(3.9985823216071026e-05, result.FinalState.Velocity.Y);
        Assert.Equal(1904, result.AcceptedSteps);
        Assert.Equal(704, result.RejectedSteps);
    }

    /// <summary>
    /// An explicit end sample at <c>t + h</c> is the default, bitwise: the argument only
    /// moves the instant the two <c>c = 1</c> stages sample.
    /// </summary>
    [Fact]
    public void AnEndSampleAtTheStepsEndIsTheDefault()
    {
        var field = Quadrupole(0.5, new RfWaveform.Rectangular(0.5));
        var state = new PhaseState(new Vec3(1e-3, 5e-4, 0.0), new Vec3(30.0, -20.0, 0.0));
        const double T = 1.234e-7;
        const double H = 3.1e-9;
        var k1 = DormandPrince54.Derivative(in state, field, Ion.ChargeToMassSi, T);

        DormandPrince54.Step(in state, in k1, H, field, Ion.ChargeToMassSi, out var plain, out var ep, out var ev, out var kp, T);
        DormandPrince54.Step(in state, in k1, H, field, Ion.ChargeToMassSi, out var explicitly, out var ee, out var ve, out var ke, T, T + H);

        Assert.Equal(plain, explicitly);
        Assert.Equal(ep, ee);
        Assert.Equal(ev, ve);
        Assert.Equal(kp, ke);
    }

    /// <summary>
    /// A landing costs exactly one field evaluation, the derivative on the far side, and
    /// nothing else: over ten periods (twenty edges, the last at the flight time) the count
    /// is seven per accepted step (six stages and the energy check), six per rejected one,
    /// the first derivative, and one per edge. A landing whose time were left to the
    /// compensated sum, an ulp short of the edge, would be met again an ulp later and taken
    /// in place, at one more.
    /// </summary>
    [Fact]
    public void EachLandingCostsOneEvaluation()
    {
        var result = Fly(Quadrupole(0.5, new RfWaveform.Rectangular(0.5)), 10, 1e-11, land: true);

        Assert.Equal((7L * result.AcceptedSteps) + (6L * result.RejectedSteps) + 1 + 20, result.FieldEvaluations);
    }

    /// <summary>
    /// A drive without edges flies identically under either rule: a sinusoid declares no
    /// breakpoint and an ideal quadrupole no stage, so no landing instant ever occurs.
    /// </summary>
    [Fact]
    public void ASinusoidFliesTheSameUnderEitherRule()
    {
        var field = Quadrupole(0.5, new RfWaveform.Sinusoid());
        var land = Fly(field, 20, 1e-10, land: true);
        var cap = Fly(field, 20, 1e-10, land: false);

        Assert.Equal(cap.FinalState, land.FinalState);
        Assert.Equal(cap.AcceptedSteps, land.AcceptedSteps);
        Assert.Equal(cap.RejectedSteps, land.RejectedSteps);
        Assert.Equal(cap.FieldEvaluations, land.FieldEvaluations);
    }

    /// <summary>
    /// The landing step samples the near side, the next one starts on the far side: around
    /// the first edge <c>t*</c> the field is sampled at <c>BitDecrement(t*)</c> by the landing
    /// step's two end stages and then at <c>t*</c> exactly, and no sample before that one
    /// crosses the edge.
    /// </summary>
    [Fact]
    public void ALandingSamplesTheNearSideAndRestartsOnTheFarSide()
    {
        var recorder = new Recorder(Quadrupole(0.5, new RfWaveform.Rectangular(0.3)), twin: false);
        var edge = recorder.NextEdgeAfter(0.0);

        var result = TrajectoryIntegrator.Integrate(
            Start, Ion, recorder, new IntegrationSettings { RelativeTolerance = 1e-11, MaximumFlightTime = 2.0 * edge, LandOnEdges = true });

        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, result.Outcome);
        var first = recorder.Times.IndexOf(edge);
        Assert.True(first >= 2, "the edge was never sampled");
        Assert.Equal(Math.BitDecrement(edge), recorder.Times[first - 1]);
        Assert.Equal(Math.BitDecrement(edge), recorder.Times[first - 2]);
        Assert.All(recorder.Times.Take(first), t => Assert.True(t < edge, $"sampled {t:R} past the edge {edge:R}"));
    }

    /// <summary>
    /// 10^5 consecutive edges (q = 0.5, 50 ms at 1 MHz) at rtol 1e-9, at duty 0.5 and 0.3:
    /// the flight reaches its time exactly, without underflow, still bounded.
    /// </summary>
    /// <param name="duty">The duty cycle.</param>
    [Theory]
    [InlineData(0.5)]
    [InlineData(0.3)]
    public void TenToTheFiveEdgesLandWithoutUnderflow(double duty)
    {
        // On the y axis at duty 0.3, where x is unstable at q = 0.5 and y is not; x stays 0.
        var start = duty == 0.5 ? Start : new PhaseState(new Vec3(0.0, 5e-4, 0.0), new Vec3(0.0, 0.0, 0.0));
        var result = Chain(new Recorder(Quadrupole(0.5, new RfWaveform.Rectangular(duty)), twin: false, record: false), start);

        output.WriteLine($"d {duty}: {result.AcceptedSteps} steps, {result.RejectedSteps} rejected");
        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, result.Outcome);
        Assert.Equal(ChainSeconds, result.FlightTimeSeconds);
        Assert.True(result.FinalState.Position.Length < 1e-2, $"{result.FinalState.Position.Length:E3} m");
    }

    /// <summary>
    /// Two edges one ulp apart, 10^5 times (R4): the second of each pair is taken in place,
    /// as neither a step nor a rejection. Measured: 100,001 in-place landings and the same
    /// 2.95 million steps the flight takes without the twins. Without the in-place rule for
    /// an instant one ulp ahead (above about 8 ms an ulp exceeds the minimum step) each twin
    /// is a one-ulp step the controller then regrows from, 4.11 million steps in all.
    /// </summary>
    [Fact]
    public void EdgesOneUlpApartAreTakenInPlace()
    {
        var twins = new Recorder(Quadrupole(0.5, new RfWaveform.Rectangular(0.5)), twin: true, record: false);
        var paired = Chain(twins, Start);
        var single = Chain(Quadrupole(0.5, new RfWaveform.Rectangular(0.5)), Start);

        output.WriteLine(
            $"twins: {paired.AcceptedSteps} steps, {paired.RejectedSteps} rejected, {twins.TwinSamples} in place; "
            + $"single: {single.AcceptedSteps} steps, {single.RejectedSteps} rejected; "
            + $"apart by {(paired.FinalState.Position - single.FinalState.Position).Length:E3} m");

        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, paired.Outcome);
        Assert.Equal(ChainSeconds, paired.FlightTimeSeconds);
        Assert.InRange(twins.TwinSamples, 99_990, 100_001);
        Assert.InRange(paired.AcceptedSteps, 0.99 * single.AcceptedSteps, 1.01 * single.AcceptedSteps);
        Assert.InRange(paired.RejectedSteps, 0, 1.01 * single.RejectedSteps);
        // Measured 4.1e-9 m on 1 mm: the twins shift each restart by an ulp and nothing more.
        Assert.True((paired.FinalState.Position - single.FinalState.Position).Length < 5e-8);
    }

    /// <summary>
    /// A collision due before the edge cuts the step short of it, and that step is then not a
    /// landing: in a gas, through a drive too weak to bend the path (q = 1e-20), <c>land</c>
    /// and <c>cap</c> fly the same straight segments between the same collisions and end in
    /// the same place, to round-off. A step still treated as a landing would jump the clock
    /// to the edge without moving the ion.
    /// </summary>
    [Fact]
    public void ACollisionBeforeAnEdgeIsNotALanding()
    {
        var gas = new Collisions.BackgroundGas
        {
            Model = Collisions.CollisionModel.HardSphere,
            PressureSi = 1e4,
            TemperatureK = 300.0,
            MassSi = 28.0134 * 1.66053906892e-27,
            PolarizabilitySi = 1.74e-30,
            CrossSectionSi = 250e-20,
        };
        var field = Quadrupole(1e-20, new RfWaveform.Rectangular(0.3));
        var start = new PhaseState(Vec3.Zero, new Vec3(0.0, 0.0, 1000.0));
        TrajectoryResult Gas(bool land) => TrajectoryIntegrator.Integrate(
            start,
            Ion,
            field,
            new IntegrationSettings { RelativeTolerance = 1e-10, MaximumFlightTime = 5e-5, LandOnEdges = land },
            collisions: new Collisions.CollisionSampler(gas, Ion.MassSi, Ion.ChargeSi, 7));

        var land = Gas(land: true);
        var cap = Gas(land: false);
        var apart = (land.FinalState.Position - cap.FinalState.Position).Length;
        output.WriteLine($"land {land.FinalState.Position.Length:E6} m, cap {cap.FinalState.Position.Length:E6} m, apart {apart:E3} m");

        Assert.Equal(cap.FlightTimeSeconds, land.FlightTimeSeconds);
        Assert.True(apart < 1e-12 * cap.FinalState.Position.Length + 1e-15, $"apart by {apart:E3} m");
    }

    private const double ChainSeconds = 0.05;

    private static TrajectoryResult Chain(ITimeVaryingField field, PhaseState start) =>
        TrajectoryIntegrator.Integrate(
            start, Ion, field, new IntegrationSettings { RelativeTolerance = 1e-9, MaximumFlightTime = ChainSeconds, LandOnEdges = true });

    private static PhaseState Start => new(new Vec3(1e-3, 5e-4, 0.0), new Vec3(0.0, 0.0, 0.0));

    private static IdealQuadrupoleRf Quadrupole(double q, RfWaveform waveform) =>
        IdealQuadrupoleRf.FromMathieu(
            0.0, q, Quantity.From(100.0, "u"), Quantity.From(1.0, "e"), Quantity.From(1.0, "MHz"), Quantity.From(4.0, "mm"), waveform);

    private static TrajectoryResult Fly(IdealQuadrupoleRf field, int periods, double tolerance, bool land) =>
        TrajectoryIntegrator.Integrate(
            Start,
            Ion,
            field,
            new IntegrationSettings
            {
                RelativeTolerance = tolerance,
                MaximumFlightTime = periods * field.ShortestPeriodSeconds,
                LandOnEdges = land,
            });

    private static double Error(TrajectoryResult result, double x, double y, double vx, double vy, double omegaX0)
    {
        var s = result.FinalState;
        return new[]
        {
            Math.Abs(s.Position.X - x) / Math.Max(Math.Abs(x), X0),
            Math.Abs(s.Position.Y - y) / Math.Max(Math.Abs(y), X0),
            Math.Abs(s.Velocity.X - vx) / Math.Max(Math.Abs(vx), omegaX0),
            Math.Abs(s.Velocity.Y - vy) / Math.Max(Math.Abs(vy), omegaX0),
        }.Max();
    }

    /// <summary>
    /// Records every instant the field is sampled at and, as <c>twin</c>, reports a second
    /// edge one ulp after each of the inner field's: the "already there" case two taps whose
    /// flips differ by an ulp produce, without depending on which phases round to adjacent
    /// instants.
    /// </summary>
    private sealed class Recorder(ITimeVaryingField inner, bool twin, bool record = true) : ITimeVaryingField
    {
        public List<double> Times { get; } = [];

        public int TwinSamples { get; private set; }

        private double _lastTwin = double.NaN;

        public double ShortestPeriodSeconds => inner.ShortestPeriodSeconds;

        public double ResolutionLength => inner.ResolutionLength;

        public double NextSwitchAfter(double timeSeconds) => inner.NextSwitchAfter(timeSeconds);

        public double NextEdgeAfter(double timeSeconds)
        {
            var next = inner.NextEdgeAfter(timeSeconds);

            if (twin && inner.NextEdgeAfter(Math.BitDecrement(timeSeconds)) == timeSeconds)
            {
                _lastTwin = Math.BitIncrement(timeSeconds);
                return _lastTwin;
            }

            return next;
        }

        public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds)
        {
            if (record)
            {
                Times.Add(timeSeconds);
            }

            if (timeSeconds == _lastTwin)
            {
                TwinSamples++;
                _lastTwin = double.NaN;
            }

            return inner.ElectricFieldAt(in position, timeSeconds);
        }

        public double PotentialAt(in Vec3 position, double timeSeconds) => inner.PotentialAt(in position, timeSeconds);

        public Vec3 ElectricFieldAt(in Vec3 position) => inner.ElectricFieldAt(in position);

        public double PotentialAt(in Vec3 position) => inner.PotentialAt(in position);
    }
}
