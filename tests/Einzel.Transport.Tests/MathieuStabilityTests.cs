using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// The Mathieu stability boundary, recovered by integration.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 19 calls this "the best single test that the RF path is correct",
/// and it earns the description. An ideal quadrupole turns Newton's equation into
/// the Mathieu equation, whose stable and unstable regions are known exactly and
/// have been tabulated since the nineteenth century. Nothing about the answer
/// comes from this code, and almost nothing about the code fails to show up in it:
/// a stage evaluated at the wrong instant, a step that skips a phase, a sign error
/// in the field, or a factor of two in the a-q mapping all move the boundary.
/// </para>
/// <para>
/// The number being recovered is <b>q = 0.90804</b> at a = 0, the first boundary
/// of the first stability region. Below it an ion oscillates about the axis
/// forever; above it the amplitude grows without bound and the ion is lost.
/// </para>
/// </remarks>
public sealed class MathieuStabilityTests(ITestOutputHelper output)
{
    /// <summary>The tabulated first stability boundary at a = 0.</summary>
    private const double BoundaryAtZeroA = 0.90804;

    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    /// <summary>
    /// Whether an ion launched slightly off axis stays bounded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stability is an asymptotic property, so it is tested by flying for many
    /// cycles and asking whether the excursion ever exceeds the aperture. Near the
    /// boundary the growth is slow, which is exactly why the cycle count sets how
    /// finely the boundary can be located - not the integrator tolerance.
    /// </para>
    /// <para>
    /// Launched off axis in both x and y, because the two are unstable in
    /// different places and an ion started on one axis would be blind to the
    /// other's boundary.
    /// </para>
    /// </remarks>
    private static bool IsStable(double a, double q, int cycles, out double excursion)
    {
        var species = Peptide;
        var radius = Quantity.From(4.0, "mm");
        var frequency = Quantity.From(1.0, "MHz");

        var field = IdealQuadrupoleRf.FromMathieu(
            a, q, species.Mass(), species.Charge(), frequency, radius);

        var start = new PhaseState(
            new Vec3(0.1e-3, 0.1e-3, 0.0),
            new Vec3(0.0, 0.0, 0.0));

        var flight = cycles * field.ShortestPeriodSeconds;
        var aperture = radius.In("m");

        // The flight ends when the ion leaves the aperture; an ion that survives
        // to the time limit stayed in.
        TrajectoryStopFunction inside = (in PhaseState s) =>
            aperture - Math.Max(Math.Abs(s.Position.X), Math.Abs(s.Position.Y));

        var recorder = new TrajectoryRecorder(field.ShortestPeriodSeconds / 4.0);

        var result = TrajectoryIntegrator.Integrate(
            start,
            species,
            field,
            new IntegrationSettings { RelativeTolerance = 1e-9, MaximumFlightTime = flight },
            inside,
            recorder);

        excursion = 0.0;

        foreach (var sample in recorder.Samples)
        {
            excursion = Math.Max(
                excursion, Math.Max(Math.Abs(sample.Position.X), Math.Abs(sample.Position.Y)));
        }

        return result.Outcome == TrajectoryOutcome.MaximumFlightTimeReached;
    }

    [Theory]
    [InlineData(0.4, true)]
    [InlineData(0.7, true)]
    [InlineData(0.85, true)]
    [InlineData(0.95, false)]
    [InlineData(1.2, false)]
    [InlineData(2.0, false)]
    public void StabilityAtZeroDcFallsWhereMathieuSaysItDoes(double q, bool expected)
    {
        var stable = IsStable(0.0, q, cycles: 200, out var excursion);

        output.WriteLine(
            $"q = {q:F3}: {(stable ? "stable" : "lost")}, excursion {excursion * 1e3:F3} mm "
            + $"(boundary is {BoundaryAtZeroA})");

        Assert.Equal(expected, stable);
    }

    [Fact]
    public void TheBoundaryIsRecoveredToTheClassBBudget()
    {
        // ACC-6 asks for a boundary resolved to one part in five hundred of the
        // scan. Bisection on stability over a scan of 0.4 to 1.4 makes that
        // 0.002 in q, which is well inside what distinguishes the tabulated
        // 0.90804 from anything else.
        const double Low = 0.4;
        const double High = 1.4;
        var tolerance = (High - Low) / 500.0;

        var low = Low;
        var high = High;

        Assert.True(IsStable(0.0, low, 200, out _), "the scan should start inside the stable region");
        Assert.False(IsStable(0.0, high, 200, out _), "the scan should end outside it");

        var probes = 0;

        while (high - low > tolerance)
        {
            var mid = 0.5 * (low + high);

            if (IsStable(0.0, mid, 200, out _))
            {
                low = mid;
            }
            else
            {
                high = mid;
            }

            probes++;
        }

        var boundary = 0.5 * (low + high);
        var error = Math.Abs(boundary - BoundaryAtZeroA);

        output.WriteLine($"scan {Low} to {High}, ACC-6 resolution {tolerance:F4} in q");
        output.WriteLine($"boundary found at q = {boundary:F5} in {probes} probes");
        output.WriteLine($"tabulated        q = {BoundaryAtZeroA}");
        output.WriteLine($"difference       {error:F5} ({error / tolerance:F2} of the ACC-6 resolution)");

        // Within the resolution the scan can distinguish. Asking for better would
        // be asking the bisection to resolve a boundary it did not sample.
        Assert.True(
            error <= tolerance,
            $"the boundary came out at {boundary:F5} against a tabulated {BoundaryAtZeroA}");
    }

    [Fact]
    public void ADcComponentNarrowsTheStableRange()
    {
        // The shape of the diagram, not just one point of it. Adding DC lifts the
        // working point toward the apex at a = 0.237, so the stable range in q
        // shrinks from both sides - which is the whole principle of a mass filter:
        // hold a and q on the ratio that leaves one mass inside the tip.
        output.WriteLine("    a    stable q from    to");

        foreach (var a in new[] { 0.0, 0.1, 0.2 })
        {
            var first = double.NaN;
            var last = double.NaN;

            for (var q = 0.05; q <= 1.0; q += 0.01)
            {
                if (IsStable(a, q, 120, out _))
                {
                    if (double.IsNaN(first))
                    {
                        first = q;
                    }

                    last = q;
                }
            }

            output.WriteLine($"{a,5:F2} {first,15:F3} {last,7:F3}");

            if (a == 0.0)
            {
                // With no DC every small q is stable: there is nothing to defocus
                // the ion on average.
                Assert.True(first < 0.1, $"a = 0 should be stable at small q; first stable was {first:F3}");
            }
            else
            {
                // With DC the x or y motion is defocused until the RF is strong
                // enough to hold it, so small q is lost.
                Assert.True(first > 0.1, $"a = {a} should be unstable at small q; first stable was {first:F3}");
            }

            Assert.True(last < BoundaryAtZeroA + 0.05, "the upper edge should not exceed the a = 0 boundary");
        }
    }
}

/// <summary>
/// What the RF path costs and what it gives up.
/// </summary>
public sealed class RfIntegrationTests(ITestOutputHelper output)
{
    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    private static (IdealQuadrupoleRf Field, PhaseState Start) Setup(double q)
    {
        var species = Peptide;

        var field = IdealQuadrupoleRf.FromMathieu(
            0.0, q, species.Mass(), species.Charge(),
            Quantity.From(1.0, "MHz"), Quantity.From(4.0, "mm"));

        return (field, new PhaseState(new Vec3(0.2e-3, 0.0, 0.0), new Vec3(0.0, 0.0, 0.0)));
    }

    [Fact]
    public void EnergyDriftIsNotReportedForADrivenField()
    {
        // A driven field does work on the ion deliberately and continuously, so
        // there is no conserved quantity to check against. Reporting the change
        // against the t = 0 potential would give a figure that looks like a
        // diagnostic, moves when the physics moves, and means nothing.
        var (field, start) = Setup(0.4);

        var result = TrajectoryIntegrator.Integrate(
            start,
            Peptide,
            field,
            new IntegrationSettings
            {
                RelativeTolerance = 1e-9,
                MaximumFlightTime = 20.0 * field.ShortestPeriodSeconds,
            });

        output.WriteLine($"energy drift reported as {result.MaximumRelativeEnergyDrift}");

        Assert.True(
            double.IsNaN(result.MaximumRelativeEnergyDrift),
            "a driven field has no energy invariant, so a number here would be a false diagnostic");
    }

    [Fact]
    public void TheStepIsCappedByTheDrivePeriod()
    {
        // The same lesson a gridded field taught, in a different variable. An
        // embedded error estimate compares two solutions of the problem it was
        // given; if every stage of a step lands on the same phase of the cycle,
        // both agree and the step is accepted as accurate. It was accurate for the
        // field it was shown, and it was not shown the field.
        var (field, start) = Setup(0.4);
        const int Cycles = 40;

        var result = TrajectoryIntegrator.Integrate(
            start,
            Peptide,
            field,
            new IntegrationSettings
            {
                RelativeTolerance = 1e-9,
                MaximumFlightTime = Cycles * field.ShortestPeriodSeconds,
            });

        var perCycle = (double)result.AcceptedSteps / Cycles;
        output.WriteLine($"{result.AcceptedSteps} steps over {Cycles} cycles: {perCycle:F1} per cycle");

        // At least twenty, which is the cap. The controller may ask for more and
        // is never allowed fewer.
        Assert.True(perCycle >= 20.0, $"only {perCycle:F1} steps per RF cycle; the drive was not resolved");
    }

    [Fact]
    public void AStaticFieldIsUnchangedToTheLastBit()
    {
        // Threading time through every Dormand-Prince stage must cost a static
        // field nothing. Time enters as an added zero and a type test that always
        // fails the same way, so a reflectron integrated today has to agree with
        // one integrated before RF existed - exactly, not nearly.
        var species = Peptide;
        var field = UniformField.Create(new Vec3(-1000.0, 0.0, 0.0));
        var speed = species.SpeedAfterAcceleration(Quantity.From(2.0, "kV")).In("m/s");
        var start = new PhaseState(new Vec3(0.0, 0.0, 0.0), new Vec3(speed, 0.0, 0.0));

        TrajectoryStopFunction detector = (in PhaseState s) => 0.05 - s.Position.X;

        var settings = new IntegrationSettings { RelativeTolerance = 1e-12, MaximumFlightTime = 1e-3 };
        var result = TrajectoryIntegrator.Integrate(start, species, field, settings, detector);

        // A uniform decelerating field with a known closed form: x = vt - at^2/2.
        var acceleration = Math.Abs(species.ChargeSi) * 1000.0 / species.MassSi;
        var exact = (speed - Math.Sqrt((speed * speed) - (2.0 * acceleration * 0.05))) / acceleration;

        var error = Math.Abs(result.FlightTimeSeconds - exact) / exact;
        output.WriteLine($"{result.FlightTimeSeconds * 1e6:F9} us against {exact * 1e6:F9}, off by {error:E3}");

        Assert.True(error < 1e-12, $"the static path moved by {error:E3} when time was threaded through it");

        // And it still has an energy diagnostic, because it still has an invariant.
        Assert.False(double.IsNaN(result.MaximumRelativeEnergyDrift));
    }
}

/// <summary>
/// A rectangular drive, against Schrader, Anderson and Russell (JASMS 2024).
/// </summary>
/// <remarks>
/// <para>
/// "Increasing Isolation Efficiency Using a Segmented Quadrupole Mass Filter
/// Operated with Rectangular Waveforms", J. Am. Soc. Mass Spectrom. 35 (2024)
/// 1237-1244. A switching drive rather than a resonant one, which changes the
/// equation of motion from Mathieu's to Meissner's and moves the stability
/// boundaries with it.
/// </para>
/// <para>
/// Two numbers from the paper are checkable here and neither comes from this code.
/// The low-mass cut-off moves from q = 0.908 to <b>q = 0.712</b>. And an
/// asymmetric duty cycle supplies its own a: at 61.15/38.85 and q = 0.5897 the
/// paper quotes a = -0.2640, which is the waveform's mean doing the job a DC
/// supply would.
/// </para>
/// <para>
/// The authors simulated this in SIMION 8.1. That is the project thesis restated
/// as a fact for the second time in these targets - the Ion Processor was
/// simulated in an in-house package nobody outside the group can run, and this one
/// in software behind a licence.
/// </para>
/// </remarks>
public sealed class RectangularWaveformTests(ITestOutputHelper output)
{
    /// <summary>The square-wave low-mass cut-off the paper quotes.</summary>
    private const double SquareBoundary = 0.712;

    /// <summary>
    /// Where the 4 mm aperture criterion of <see cref="IsStable"/> cuts off from a
    /// 0.1 mm start over 200 cycles, run on the exact piecewise motion (the
    /// project's exact Meissner reference). Not the stability boundary, which is
    /// 0.71249366.
    /// </summary>
    private const double ApertureCutOff = 0.71113;

    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    private static bool IsStable(double a, double q, RfWaveform waveform, int cycles)
    {
        var species = Peptide;
        var radius = Quantity.From(4.0, "mm");

        var field = IdealQuadrupoleRf.FromMathieu(
            a, q, species.Mass(), species.Charge(), Quantity.From(1.0, "MHz"), radius, waveform);

        var start = new PhaseState(new Vec3(0.1e-3, 0.1e-3, 0.0), new Vec3(0.0, 0.0, 0.0));
        var aperture = radius.In("m");

        TrajectoryStopFunction inside = (in PhaseState s) =>
            aperture - Math.Max(Math.Abs(s.Position.X), Math.Abs(s.Position.Y));

        var result = TrajectoryIntegrator.Integrate(
            start,
            species,
            field,
            new IntegrationSettings
            {
                RelativeTolerance = 1e-9,
                MaximumFlightTime = cycles * field.ShortestPeriodSeconds,
            },
            inside);

        return result.Outcome == TrajectoryOutcome.MaximumFlightTimeReached;
    }

    [Fact]
    public void ASquareWaveMovesTheCutOffToSevenOneTwo()
    {
        // The headline check. A switching drive is not a cosmetic change to a
        // sinusoidal one: the boundary moves by more than twenty per cent, and an
        // implementation that treated the waveform as a detail would land on 0.908
        // and look fine.
        const double Low = 0.4;
        const double High = 1.0;

        // 0.6 / 2000 = 0.0003: two halvings finer than ACC-6 asks, so that the
        // second assertion below can tell the aperture criterion's 0.71113 from
        // the boundary 0.71249 (0.00136 apart).
        var tolerance = (High - Low) / 2000.0;

        var waveform = new RfWaveform.Rectangular(0.5);
        var low = Low;
        var high = High;

        Assert.True(IsStable(0.0, low, waveform, 200), "the scan should start inside the stable region");
        Assert.False(IsStable(0.0, high, waveform, 200), "the scan should end outside it");

        while (high - low > tolerance)
        {
            var mid = 0.5 * (low + high);

            if (IsStable(0.0, mid, waveform, 200))
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        var boundary = 0.5 * (low + high);

        output.WriteLine($"square wave cut-off  q = {boundary:F5}");
        output.WriteLine($"paper quotes         q = {SquareBoundary}");
        output.WriteLine($"sinusoid, for scale  q = 0.90804");
        output.WriteLine($"difference           {Math.Abs(boundary - SquareBoundary):F5}");

        // The paper quotes three figures, so three figures is what can be checked.
        Assert.True(
            Math.Abs(boundary - SquareBoundary) < 0.005,
            $"square-wave cut-off came out at {boundary:F5} against a published {SquareBoundary}");

        // What this bisection measures, stated so the number is not mistaken for
        // the Meissner boundary. IsStable calls an ion lost when its excursion
        // exceeds the 4 mm aperture from a 0.1 mm start within 200 cycles. Close to
        // the boundary a *stable* ion's beat envelope grows without limit (the
        // exact motion reaches 4.03 mm at q = 0.711 and 7.0 mm at 0.712), so the
        // 40x aperture-to-start ratio fails stable ions above q = 0.7109, and the
        // same bisection on the exact motion returns 0.71113. The boundary proper,
        // |tr M| = 2, is q = 0.71249366 and is measured by RectangularBoundaryTests;
        // the published 0.712 is its three-figure rounding. The 0.001 below is three
        // bisection cells (0.0003 each) and excludes the boundary, 0.00136 away: a
        // 400 mm aperture, or a 1 um start, moves this cut-off toward 0.7125 and
        // fails it.
        Assert.True(
            Math.Abs(boundary - ApertureCutOff) < 0.001,
            $"aperture-criterion cut-off came out at {boundary:F5} against the exact-motion value {ApertureCutOff}");
    }

    [Fact]
    public void AnAsymmetricDutyCycleSuppliesItsOwnDc()
    {
        // The trick that makes a digital filter work without a DC supply. The
        // paper's prefilter runs at 61.15/38.85 and q = 0.5897, and quotes
        // a = -0.2640; the mean of the waveform is 2d - 1, which enters the
        // equation of motion exactly where a DC offset would.
        var species = Peptide;
        const double Duty = 0.6115;
        const double Q = 0.5897;

        var field = IdealQuadrupoleRf.FromMathieu(
            0.0, Q, species.Mass(), species.Charge(),
            Quantity.From(1.0, "MHz"), Quantity.From(4.0, "mm"),
            new RfWaveform.Rectangular(Duty));

        var a = field.MathieuA(species.Mass(), species.Charge());
        var q = field.MathieuQ(species.Mass(), species.Charge());

        output.WriteLine($"duty {Duty:P2} at q = {q:F4} gives a = {a:F4}");
        output.WriteLine($"paper quotes a = -0.2640 for the same working point (sign by pair)");

        Assert.Equal(Q, q, 1e-9);

        // a = 2q(2d - 1), which for this duty and q is 0.263.
        Assert.Equal(2.0 * Q * ((2.0 * Duty) - 1.0), a, 1e-9);
        Assert.Equal(0.2640, Math.Abs(a), 0.002);
    }

    [Fact]
    public void ABalancedSquareWaveHasNoMeanAndAnOffsetOneDoes()
    {
        // The mechanism, isolated from any trajectory. If the mean were wrong the
        // stability results would be wrong by a shifted working point and would
        // still look self-consistent.
        Assert.Equal(0.0, new RfWaveform.Sinusoid().Mean);
        Assert.Equal(0.0, new RfWaveform.Rectangular(0.5).Mean, 1e-15);
        Assert.Equal(0.223, new RfWaveform.Rectangular(0.6115).Mean, 1e-3);

        // And the shape itself: high for the duty fraction, low for the rest.
        var wave = new RfWaveform.Rectangular(0.6115);

        Assert.Equal(1.0, wave.At(0.0));
        Assert.Equal(1.0, wave.At(0.61));
        Assert.Equal(-1.0, wave.At(0.62));
        Assert.Equal(-1.0, wave.At(0.99));

        // Phase arrives from an accumulated flight time and will not be tidy.
        Assert.Equal(wave.At(0.3), wave.At(7.3), 1e-15);
    }
}

/// <summary>
/// The rectangular drive's exact stability boundaries, by numerical monodromy.
/// </summary>
/// <remarks>
/// <para>
/// With a rectangular drive the equation of motion is Meissner's: on each part of
/// the period the field is constant, so the one-period transfer matrix M of each
/// transverse axis is a product of two closed-form 2x2 matrices and the motion is
/// bounded iff |tr M| &lt; 2. The boundaries are therefore known to any precision,
/// unlike an aperture test, which measures where a beat envelope exceeds a
/// chosen radius (see <see cref="RectangularWaveformTests"/>).
/// </para>
/// <para>
/// Here M is measured rather than computed: two flights of exactly one period,
/// from (x, y) = (1 mm, 1 mm) at rest and from the origin at (1 km/s, 1 km/s),
/// give the two columns of M for both axes at once (the axes are decoupled), and
/// tr M = x(T)/x0 + vx(T)/vx0. The integrator lands on both edges of the period
/// (<c>LandOnEdges</c>), so what is tested is the field, the a-q mapping, the duty
/// cycle's edge instants and the integrator together, at the 1e-6 level, against
/// numbers that come from nowhere in this code.
/// </para>
/// <para>
/// Conventions: <c>a</c> is the DC part (what <c>FromMathieu</c> takes); the
/// waveform mean 2d - 1 sits inside the drive, so the reported <c>MathieuA</c>
/// is a + 2q(2d - 1). Ding and Kumashiro (Rapid Commun. Mass Spectrom. 20 (2006)
/// 3-8, doi 10.1002/rcm.2253) write the same boundary with a_Z = a + 2q(2d - 1) and
/// |q_Z| = 4d(1 - d) q, and quote 0.7125 for the square wave, the four-figure
/// rounding of 0.71249366.
/// </para>
/// </remarks>
public sealed class RectangularBoundaryTests(ITestOutputHelper output)
{
    /// <summary>
    /// How far on each side of an edge the sign test probes. Each row names the
    /// axis whose stable interval ends at the edge, and every such per-axis
    /// interval in the rows is at least 0.14 wide (the narrowest is the x interval
    /// (0, 0.1395) at d = 0.3, a = 0.1), so a probe 0.01 inside stays inside that
    /// axis's own interval even where the common band of both axes is narrower
    /// (0.0144 at d = 0.61). The probe outside lands in the neighbouring unstable
    /// interval, which is at least 0.1 wide in every row.
    /// </summary>
    private const double Probe = 0.01;

    /// <summary>
    /// The bound every edge row must meet. The measured residual under land at
    /// rtol 1e-11 is 2e-11 to 1.4e-9 (1e-8 under cap), most of it the 1e-10
    /// rounding of the reference edges times the slope of tr M.
    /// </summary>
    private const double TraceTolerance = 1e-6;

    private static Quantity Mass => Quantity.From(100.0, "u");

    private static Quantity Charge => Quantity.From(1.0, "e");

    /// <summary>
    /// The traces of the one-period transfer matrices of the x and y axes, with the
    /// flight time the integrator reports beside the period it was asked for. Both
    /// flights are checked to have ended at the period.
    /// </summary>
    private static (double TraceX, double TraceY, double EndTime, double Period) Monodromy(
        double a, double q, double duty)
    {
        var species = IonSpecies.Create(Mass, Charge);
        var field = IdealQuadrupoleRf.FromMathieu(
            a, q, Mass, Charge, Quantity.From(1.0, "MHz"), Quantity.From(4.0, "mm"),
            new RfWaveform.Rectangular(duty));

        var period = field.ShortestPeriodSeconds;
        var settings = new IntegrationSettings
        {
            RelativeTolerance = 1e-11,
            MaximumFlightTime = period,
            LandOnEdges = true,
        };

        const double X0 = 1.0e-3;
        const double V0 = 1.0e3;

        // Column one: unit displacement at rest. Column two: at the origin with
        // unit velocity. x and y are launched together because they do not couple.
        var fromRest = TrajectoryIntegrator.Integrate(
            new PhaseState(new Vec3(X0, X0, 0.0), Vec3.Zero), species, field, settings);
        var fromMotion = TrajectoryIntegrator.Integrate(
            new PhaseState(Vec3.Zero, new Vec3(V0, V0, 0.0)), species, field, settings);

        AssertEndedAtThePeriod(fromRest, period);
        AssertEndedAtThePeriod(fromMotion, period);

        var traceX = (fromRest.FinalState.Position.X / X0) + (fromMotion.FinalState.Velocity.X / V0);
        var traceY = (fromRest.FinalState.Position.Y / X0) + (fromMotion.FinalState.Velocity.Y / V0);

        return (traceX, traceY, fromRest.FlightTimeSeconds, period);
    }

    /// <summary>
    /// The flight ended at one period, to the ulp. A trace taken anywhere else
    /// would not be tr M.
    /// </summary>
    /// <remarks>
    /// The last step is cut to the time remaining, and the compensated sum can leave
    /// the time one ulp (2e-22 s) short of T with a remainder below <c>MinimumStep</c>;
    /// the integrator then takes the end in place, as it takes a landing closer than
    /// the minimum step (<c>IntegratorBehaviourTests.AFlightEndingAnUlpShortOfItsTimeReachesIt</c>
    /// pins the one flight of the sixty-three here that does this: d = 0.3, a = 0.1,
    /// q = 0.1056, from rest). Every flight therefore ends at T exactly and reports
    /// <c>MaximumFlightTimeReached</c>.
    /// </remarks>
    private static void AssertEndedAtThePeriod(TrajectoryResult result, double period)
    {
        Assert.True(
            result.Outcome == TrajectoryOutcome.MaximumFlightTimeReached,
            $"the flight ended {result.Outcome} at {result.FlightTimeSeconds:R} s");
        Assert.True(
            result.FlightTimeSeconds == period,
            $"the flight ended at {result.FlightTimeSeconds:R} s, not at the period {period:R} s");
    }

    /// <summary>
    /// Every non-zero edge of the first stability intervals of either axis, from the
    /// project's exact Meissner reference: bisection on |tr M| = 2 of the closed-form
    /// transfer matrices, mpmath at 40 digits for d = 0.3 and 0.5 and float64 to 1e-13
    /// for the other duties, all rounded to 1e-10. Ding 2006's boundary expression was
    /// checked at the thirteen d = 0.3 and 0.5 edges and agrees to 9.4e-10, the
    /// rounding. An edge belongs to one axis; the
    /// last column says whether that axis is stable below the edge (its interval
    /// ends there) or above it (its interval begins there).
    /// </summary>
    public static TheoryData<double, double, double, char, bool> Edges => new()
    {
        // duty, a (DC), edge q, axis, stable below the edge
        // d = 0.5, a = 0: both axes (0, 0.71249366); 2 cos(th) cosh(th) = -2 at th = 1.8751040687.
        { 0.5, 0.0, 0.7124936574, 'x', true },
        { 0.5, 0.0, 0.7124936574, 'y', true },
        // d = 0.5, a = 0.1: x (0, 0.64601913), y (0.35276015, 0.77822809).
        { 0.5, 0.1, 0.6460191281, 'x', true },
        { 0.5, 0.1, 0.3527601455, 'y', false },
        { 0.5, 0.1, 0.7782280924, 'y', true },
        // d = 0.3, a = 0: x (1.28702976, 1.47139258), y (0, 0.54142265); no common band.
        { 0.3, 0.0, 1.2870297574, 'x', false },
        { 0.3, 0.0, 1.4713925787, 'x', true },
        { 0.3, 0.0, 0.5414226520, 'y', true },
        // d = 0.3, a = 0.1: x (0, 0.13953492) and (1.12994057, 1.37352026), y (0.11556813, 0.59508159).
        { 0.3, 0.1, 0.1395349181, 'x', true },
        { 0.3, 0.1, 1.1299405735, 'x', false },
        { 0.3, 0.1, 1.3735202561, 'x', true },
        { 0.3, 0.1, 0.1155681312, 'y', false },
        { 0.3, 0.1, 0.5950815940, 'y', true },
        // a = 0 at other duties: the first common band is (y's +2 root, x's -2 root)
        // for d > 0.5 and its mirror for d < 0.5. d = 0.4 (0.51827540, 0.60021228),
        // d = 0.55 (0.24698008, 0.64727476), d = 0.6 (0.51827540, 0.60021228),
        // d = 0.61 (0.57805444, 0.59247539); the band closes at d = 0.6120990.
        { 0.4, 0.0, 0.5182753992, 'x', false },
        { 0.4, 0.0, 0.6002122842, 'y', true },
        { 0.55, 0.0, 0.2469800827, 'y', false },
        { 0.55, 0.0, 0.6472747567, 'x', true },
        { 0.6, 0.0, 0.5182753992, 'y', false },
        { 0.6, 0.0, 0.6002122842, 'x', true },
        { 0.61, 0.0, 0.5780544405, 'y', false },
        { 0.61, 0.0, 0.5924753870, 'x', true },
    };

    [Theory]
    [MemberData(nameof(Edges))]
    public void TheMonodromyTraceIsTwoAtEveryClosedFormEdge(
        double duty, double a, double edge, char axis, bool stableBelow)
    {
        static double Pick(char axis, (double TraceX, double TraceY, double EndTime, double Period) m)
            => axis == 'x' ? m.TraceX : m.TraceY;

        var at = Monodromy(a, edge, duty);
        var below = Monodromy(a, edge - Probe, duty);
        var above = Monodromy(a, edge + Probe, duty);

        var trace = Pick(axis, at);
        var traceBelow = Pick(axis, below);
        var traceAbove = Pick(axis, above);
        var residual = Math.Abs(Math.Abs(trace) - 2.0);

        output.WriteLine($"d = {duty}, a = {a}, {axis} edge q = {edge:F10}");
        output.WriteLine($"tr M({axis}) = {trace:F10} at the edge, ||tr| - 2| = {residual:E2}");
        output.WriteLine($"tr M({axis}) = {traceBelow:F6} at q - {Probe}, {traceAbove:F6} at q + {Probe}");
        output.WriteLine($"flight ended at {at.EndTime:R} s against the period {at.Period:R} s");

        Assert.True(residual < TraceTolerance, $"||tr M| - 2| = {residual:E3} at the closed-form edge q = {edge}");

        var (stable, unstable) = stableBelow ? (traceBelow, traceAbove) : (traceAbove, traceBelow);

        Assert.True(Math.Abs(stable) < 2.0, $"|tr M| = {Math.Abs(stable):F6} on the stable side of q = {edge}");
        Assert.True(Math.Abs(unstable) > 2.0, $"|tr M| = {Math.Abs(unstable):F6} on the unstable side of q = {edge}");
    }

    [Fact]
    public void TheReportedAIncludesTheWaveformMean()
    {
        // The convention the duty-cycle rows rest on, stated at a duty cycle other
        // than one half: FromMathieu takes the DC a, the waveform mean 2d - 1 enters the
        // drive, and MathieuA reports both together, a + 2q(2d - 1).
        const double Q = 0.5;
        const double Duty = 0.55;

        var field = IdealQuadrupoleRf.FromMathieu(
            0.0, Q, Mass, Charge, Quantity.From(1.0, "MHz"), Quantity.From(4.0, "mm"),
            new RfWaveform.Rectangular(Duty));

        Assert.Equal(Q, field.MathieuQ(Mass, Charge), 1e-12);
        Assert.Equal(2.0 * Q * ((2.0 * Duty) - 1.0), field.MathieuA(Mass, Charge), 1e-12);
        Assert.Equal(0.1, field.MathieuA(Mass, Charge), 1e-12);
    }
}
