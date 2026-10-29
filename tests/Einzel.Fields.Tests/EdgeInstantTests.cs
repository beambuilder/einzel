using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields.Analytic;
using Einzel.Fields.Solved;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// <see cref="ITimeVaryingField.NextEdgeAfter"/>: the first double at which a field is on the
/// far side of a declared breakpoint, so the double before it reads the old level.
/// </summary>
/// <remarks>
/// The nominal instant <c>(n + b - phase) / f</c> is not that double: the field computes its
/// phase in doubles and at the nominal instant can still be short of the breakpoint. Every
/// test here asserts the two-sided condition on the field's own evaluation, never on the
/// nominal time, because a test against the nominal time would pass a schedule that an
/// integrator landing on it would see on the wrong side.
/// </remarks>
public sealed class EdgeInstantTests(ITestOutputHelper output)
{
    private const double Megahertz = 1.0e6;

    /// <summary>A uniform unit field along x: the weight is the field's x component.</summary>
    private sealed class Uniform : IElectrostaticField
    {
        public double PotentialAt(in Vec3 position) => -position.X;

        public Vec3 ElectricFieldAt(in Vec3 position) => Vec3.UnitX;
    }

    private static readonly Vec3 Probe = new(1.0e-3, 0.0, 0.0);

    private static double Ulp(double x) => Math.BitIncrement(Math.Abs(x)) - Math.Abs(x);

    /// <summary>One channel per phase, each tapping drive 0 at amplitude one.</summary>
    private static DrivenSolvedField Solved(RfWaveform waveform, double frequencyHz, params double[] phases) =>
        DrivenSolvedField.Create(
            [.. phases.Select(_ => (IElectrostaticField)new Uniform())],
            [.. phases.Select(_ => 0.0)],
            [.. phases.Select(p => (IReadOnlyList<WeightTerm>)[new WeightTerm(0, 1.0, p)])],
            [frequencyHz],
            [waveform]);

    private static double X(ITimeVaryingField field, double t) => field.ElectricFieldAt(Probe, t).X;

    private static void AssertTwoSided(ITimeVaryingField field, double edge, string what) =>
        Assert.True(
            X(field, Math.BitDecrement(edge)) != X(field, edge),
            $"{what}: the field reads {X(field, edge)} both one ulp before {edge:R} s and at it - "
            + "the reported instant is not where the field flips");

    private static IdealQuadrupoleRf Quadrupole(RfWaveform waveform) => IdealQuadrupoleRf.FromMathieu(
        0.0, 0.5, Quantity.From(100.0, "u"), Quantity.From(1.0, "e"),
        Quantity.From(1.0, "MHz"), Quantity.From(4.0, "mm"), waveform);

    private static FieldRegion Box() => new(-0.010, 0.010, -0.010, 0.010, -0.010, 0.010, 0.001);

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.3)]
    public void TheFirstEdgeIsTheDoubleWhereTheWeightFlips(double duty)
    {
        var field = Solved(new RfWaveform.Rectangular(duty), Megahertz, 0.0);

        var edge = field.NextEdgeAfter(0.0);

        output.WriteLine($"duty {duty}: edge {edge:R} s, nominal {duty / Megahertz:R} s");
        Assert.True(Math.Abs(edge - (duty / Megahertz)) <= 2 * Ulp(edge), $"edge {edge:R} s");
        Assert.Equal(1.0, field.WeightAt(0, Math.BitDecrement(edge)));
        Assert.Equal(-1.0, field.WeightAt(0, edge));
    }

    [Fact]
    public void TwoTapPhasesOnOneDriveGiveFourEdgesAPeriod()
    {
        var field = Solved(new RfWaveform.Rectangular(0.5), Megahertz, 0.0, 0.25);

        var t = 0.0;
        for (var i = 1; i <= 40; i++)
        {
            var next = field.NextEdgeAfter(t);
            Assert.True(next > t);
            Assert.True(
                field.WeightAt(0, Math.BitDecrement(next)) != field.WeightAt(0, next)
                || field.WeightAt(1, Math.BitDecrement(next)) != field.WeightAt(1, next),
                $"edge {i} at {next:R} s flips neither tap");
            Assert.True(Math.Abs(next - (i * 0.25 / Megahertz)) <= 4 * Ulp(next), $"edge {i} at {next:R} s");
            t = next;
        }
    }

    [Fact]
    public void TwoDrivesGiveTheEarlierEdge()
    {
        var field = DrivenSolvedField.Create(
            [new Uniform(), new Uniform()],
            [0.0, 0.0],
            [[new WeightTerm(0, 1.0, 0.0)], [new WeightTerm(1, 1.0, 0.0)]],
            [Megahertz, 1.7 * Megahertz],
            [new RfWaveform.Rectangular(0.5), new RfWaveform.Rectangular(0.5)]);

        var edge = field.NextEdgeAfter(0.0);

        Assert.True(Math.Abs(edge - (0.5 / (1.7 * Megahertz))) <= 2 * Ulp(edge), $"edge {edge:R} s");
        Assert.NotEqual(field.WeightAt(1, Math.BitDecrement(edge)), field.WeightAt(1, edge));
    }

    [Fact]
    public void ASmoothDriveHasNoEdges()
    {
        Assert.Equal(double.PositiveInfinity, Solved(new RfWaveform.Sinusoid(), Megahertz, 0.0).NextEdgeAfter(0.0));
    }

    [Fact]
    public void AnOperatingPointKeepsTheDrivesEdges()
    {
        // The operating point holds which stage, not where in the cycle: the drive still runs.
        var field = Solved(new RfWaveform.Rectangular(0.5), Megahertz, 0.0);

        Assert.Equal(field.NextEdgeAfter(3.0e-6), field.AtOperatingPoint(1.0e-6).NextEdgeAfter(3.0e-6));
    }

    [Fact]
    public void AZeroAmplitudeTapLandsOnNothing()
    {
        var field = DrivenSolvedField.Create(
            [new Uniform()], [0.0], [[new WeightTerm(0, 0.0, 0.0)]], [Megahertz], [new RfWaveform.Rectangular(0.5)]);
        var tiny = DrivenSolvedField.Create(
            [new Uniform()], [0.0], [[new WeightTerm(0, 1.0e-300, 0.0)]], [Megahertz], [new RfWaveform.Rectangular(0.5)]);

        Assert.Equal(double.PositiveInfinity, field.NextEdgeAfter(0.0));
        Assert.True(double.IsFinite(tiny.NextEdgeAfter(0.0)), "any non-zero amplitude declares its edges");
    }

    [Fact]
    public void AKinkIsCrossedOnTheFieldsOwnPhase()
    {
        var triangle = new RfWaveform.Analytic(
            "1 - 4*abs(p - 0.5)", new Dictionary<string, Quantity>(StringComparer.Ordinal), [0.0, 0.5]);
        var field = Solved(triangle, Megahertz, 0.0);

        var edge = field.NextEdgeAfter(0.0);

        // The kink is continuous, so the weight cannot tell the sides apart; the phase the
        // weight is computed at can, and it is what the contract is about.
        Assert.True(Megahertz * Math.BitDecrement(edge) < 0.5, $"edge {edge:R} s is past the kink one ulp early");
        Assert.True(Megahertz * edge >= 0.5, $"edge {edge:R} s is short of the kink");
    }

    [Fact]
    public void AShiftedPhaseCrossesAtTheShiftedInstant()
    {
        // Phase 0.75 with the falling edge at 0.5: the first crossing is the rising edge at
        // 0.25 / f. From just before zero the falling edge at -0.25 / f is behind and the
        // crossing near zero is found although f t is tiny against the phase there.
        var field = Solved(new RfWaveform.Rectangular(0.5), Megahertz, 0.75);

        var first = field.NextEdgeAfter(0.0);
        var nearZero = field.NextEdgeAfter(-0.26e-6);

        Assert.True(Math.Abs(first - (0.25 / Megahertz)) <= 4 * Ulp(first), $"edge {first:R} s");
        AssertTwoSided(field, first, "phase 0.75");
        Assert.True(Math.Abs(nearZero + (0.25 / Megahertz)) <= 4 * Ulp(nearZero), $"edge {nearZero:R} s");
        AssertTwoSided(field, nearZero, "phase 0.75, before zero");
    }

    [Theory]
    [InlineData(0.5, 0.0)]
    [InlineData(0.3, 0.0)]
    [InlineData(0.5, 1.0 / 3.0e6)]
    [InlineData(0.3, 1.0 / 3.0e6)]
    [InlineData(0.5, 7.3e-3)]
    [InlineData(0.3, 7.3e-3)]
    public void AHundredThousandEdgesFollowEachOther(double duty, double offset)
    {
        var inner = Solved(new RfWaveform.Rectangular(duty), Megahertz, 0.0);
        ITimeVaryingField field = offset == 0.0 ? inner : new TimeShiftedField(inner, offset);

        const int count = 100_000;
        var t = 0.0;
        var first = 0.0;

        for (var i = 0; i < count; i++)
        {
            var next = field.NextEdgeAfter(t);

            if (next <= t || X(field, Math.BitDecrement(next)) == X(field, next))
            {
                Assert.Fail($"edge {i} at {next:R} s after {t:R} s is not strictly later and two-sided");
            }

            first = i == 0 ? next : first;
            t = next;
        }

        // Two edges a period: the last edge is (count - 1) / 2 periods after the first,
        // on the breakpoint the alternation lands on.
        var firstPhase = (first + offset) * Megahertz;
        var firstIsRising = Math.Abs(firstPhase - Math.Round(firstPhase)) < 1.0e-6;
        var firstCycle = firstIsRising ? Math.Round(firstPhase) : Math.Floor(firstPhase);
        const int steps = count - 1;
        var lastCycle = firstIsRising ? firstCycle + (steps / 2) : firstCycle + ((steps + 1) / 2);
        var lastBreakpoint = (steps % 2 == 0) == firstIsRising ? 0.0 : duty;
        var nominal = ((lastCycle + lastBreakpoint) / Megahertz) - offset;

        output.WriteLine($"duty {duty}, offset {offset:R}: last edge {t:R} s, nominal {nominal:R} s");
        Assert.True(Math.Abs(t - nominal) <= 4 * Ulp(t + offset), $"last edge {t:R} s, nominal {nominal:R} s");
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.3)]
    public void NominalInstantsAreOftenStillPreEdge(double duty)
    {
        // Counted and reported: derivation.md predicts the ideal quadrupole's share from a
        // Python replica of its arithmetic. The assertion is only that the count is the
        // field's own, i.e. that every nominal instant is within reach of a reported one.
        var quadrupole = Quadrupole(new RfWaveform.Rectangular(duty));
        var solved = Solved(new RfWaveform.Rectangular(duty), Megahertz, 0.0);

        int PreEdge(ITimeVaryingField field, int from)
        {
            var count = 0;
            for (var n = from; n < from + 100; n++)
            {
                foreach (var b in new[] { 0.0, duty })
                {
                    var nominal = (n + b) / Megahertz;
                    // From a tenth of a period before: the crossing at this nominal instant is the
                    // next one, early or late, since breakpoints are 0.3 periods apart or more.
                    var edge = field.NextEdgeAfter(nominal - (0.1 / Megahertz));
                    Assert.True(Math.Abs(edge - nominal) <= 4 * Ulp(nominal), $"nominal {nominal:R} s, edge {edge:R} s");
                    count += edge > nominal ? 1 : 0;
                }
            }

            return count;
        }

        output.WriteLine(
            $"duty {duty}: pre-edge nominal instants of 200, n = 0..99 (n = 1..100): "
            + $"ideal quadrupole {PreEdge(quadrupole, 0)} ({PreEdge(quadrupole, 1)}), "
            + $"solved {PreEdge(solved, 0)} ({PreEdge(solved, 1)})");
    }

    [Fact]
    public void TheQuadrupoleFieldChangesSignAcrossItsEdge()
    {
        var field = Quadrupole(new RfWaveform.Rectangular(0.5));

        var edge = field.NextEdgeAfter(0.0);
        var before = X(field, Math.BitDecrement(edge));
        var after = X(field, edge);

        output.WriteLine(
            $"edge {edge:R} s: E_x {before:G6} then {after:G6} V/m; DriveAt(5e-7) {field.DriveAt(5.0e-7):G6} V");
        Assert.True(Math.Abs(edge - 5.0e-7) <= 2 * Ulp(edge), $"edge {edge:R} s");
        Assert.True(before * after < 0.0, $"E_x {before} then {after}");
        Assert.Equal(double.PositiveInfinity, Quadrupole(new RfWaveform.Sinusoid()).NextEdgeAfter(0.0));
    }

    [Fact]
    public void TheUniformFieldChangesSignAcrossItsEdges()
    {
        var field = OscillatingUniformField.Create(Vec3.UnitX, Quantity.From(1.0, "MHz"), new RfWaveform.Rectangular(0.3));

        var t = 0.0;
        for (var i = 0; i < 100; i++)
        {
            var next = field.NextEdgeAfter(t);
            Assert.True(next > t);
            Assert.True(X(field, Math.BitDecrement(next)) * X(field, next) < 0.0, $"edge {i} at {next:R} s");
            t = next;
        }

        Assert.Equal(
            double.PositiveInfinity,
            OscillatingUniformField.Create(Vec3.UnitX, Quantity.From(1.0, "MHz")).NextEdgeAfter(0.0));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0 / 3.0e6)]
    [InlineData(7.3e-3)]
    [InlineData(2.9e-2)]
    public void AShiftedFieldFlipsAtTheInstantItReports(double offset)
    {
        var field = new TimeShiftedField(Quadrupole(new RfWaveform.Rectangular(0.3)), offset);

        foreach (var from in new[] { 0.0, 1.0e-7, 3.3e-6, 4.1e-4 })
        {
            var edge = field.NextEdgeAfter(from);
            Assert.True(edge > from);
            AssertTwoSided(field, edge, $"offset {offset:R}, from {from:R}");
        }
    }

    [Fact]
    public void ASequenceReportsTheEdgesOfTheStateInForceUpToItsBoundary()
    {
        const double boundary = 2.2e-6;
        var field = new SequencedField(
            [Solved(new RfWaveform.Rectangular(0.5), Megahertz, 0.0), Solved(new RfWaveform.Rectangular(0.3), Megahertz, 0.0)],
            [boundary, 1.0e-3]);

        var t = 0.0;
        for (var i = 1; i <= 4; i++)
        {
            t = field.NextEdgeAfter(t);
            Assert.True(Math.Abs(t - (i * 0.5e-6)) <= 2 * Ulp(t), $"edge {i} at {t:R} s");
            AssertTwoSided(field, t, $"edge {i}");
        }

        // The first state's next edge, 2.5 us, lies past the boundary: the boundary is the
        // switch's to report, and the second state's edges start there.
        Assert.Equal(double.PositiveInfinity, field.NextEdgeAfter(t));
        Assert.Equal(boundary, field.NextSwitchAfter(t));

        var next = field.NextEdgeAfter(boundary);
        Assert.True(Math.Abs(next - 2.3e-6) <= 2 * Ulp(next), $"first edge of the second state at {next:R} s");
        AssertTwoSided(field, next, "the second state");

        var still = new SequencedField([FieldFreeSpace.Instance, FieldFreeSpace.Instance], [1.0e-6, 2.0e-6]);
        Assert.Equal(double.PositiveInfinity, still.NextEdgeAfter(0.0));
    }

    [Fact]
    public void ASuperpositionReportsTheEarliestMember()
    {
        var slow = Solved(new RfWaveform.Rectangular(0.5), Megahertz, 0.0);
        var fast = Solved(new RfWaveform.Rectangular(0.5), 1.7 * Megahertz, 0.0);
        var field = new DrivenSuperposedField([slow, FieldFreeSpace.Instance, fast]);

        Assert.Equal(fast.NextEdgeAfter(0.0), field.NextEdgeAfter(0.0));
        Assert.Equal(slow.NextEdgeAfter(4.0e-7), field.NextEdgeAfter(4.0e-7));
    }

    [Fact]
    public void ARegionForwardsEdgesAndSwitches()
    {
        var quadrupole = Quadrupole(new RfWaveform.Rectangular(0.3));
        var bounded = Assert.IsAssignableFrom<ITimeVaryingField>(BoundedField.Around(quadrupole, Box()));
        var sequence = new SequencedField([quadrupole, quadrupole], [1.5e-6, 1.0e-3]);
        var boundedSequence = Assert.IsAssignableFrom<ITimeVaryingField>(BoundedField.Around(sequence, Box()));

        Assert.Equal(quadrupole.NextEdgeAfter(1.0e-7), bounded.NextEdgeAfter(1.0e-7));
        Assert.Equal(1.5e-6, boundedSequence.NextSwitchAfter(0.0));
    }

    [Fact]
    public void EveryTimeVaryingFieldARegionWrapsTodayHasNoSwitch()
    {
        // Forwarding the switch changes nothing a model builds today: a region is applied
        // only to an analytic element (FieldAssembly.Analytic), and both time-varying
        // analytic fields answer infinity. That is what keeps the default path's landings,
        // and so its bits, as they were; a new analytic field with switches shows up here.
        ITimeVaryingField[] analytic =
        [
            Quadrupole(new RfWaveform.Rectangular(0.3)),
            Quadrupole(new RfWaveform.Sinusoid()),
            OscillatingUniformField.Create(Vec3.UnitX, Quantity.From(1.0, "MHz"), new RfWaveform.Rectangular(0.3)),
        ];

        foreach (var field in analytic)
        {
            var bounded = Assert.IsAssignableFrom<ITimeVaryingField>(BoundedField.Around(field, Box()));
            foreach (var t in new[] { 0.0, 5.0e-7, 1.0e-3 })
            {
                Assert.Equal(double.PositiveInfinity, bounded.NextSwitchAfter(t));
            }
        }
    }

    [Fact]
    public void FindingAnEdgeAllocatesNothing()
    {
        ITimeVaryingField[] fields =
        [
            Solved(new RfWaveform.Rectangular(0.3), Megahertz, 0.0, 0.25),
            Quadrupole(new RfWaveform.Rectangular(0.3)),
        ];

        foreach (var field in fields)
        {
            var sink = 0.0;
            for (var i = 0; i < 1000; i++)
            {
                sink += field.NextEdgeAfter(i * 1.0e-8);
            }

            // The smallest of five passes, as for the waveform shapes: the JIT's tier-up may
            // land a few kilobytes once on some pass, an allocation per call lands on every one.
            var smallest = long.MaxValue;
            for (var pass = 0; pass < 5; pass++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 10_000; i++)
                {
                    sink += field.NextEdgeAfter(i * 1.0e-8);
                }

                smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
            }

            output.WriteLine($"{field.GetType().Name}: {smallest} bytes over 1e4 calls (sum {sink:G3})");
            Assert.Equal(0, smallest);
        }
    }

    [Fact]
    public void TheRectangularBreakpointsAreBuiltOnceAndReadOnly()
    {
        var rectangular = new RfWaveform.Rectangular(0.3);

        Assert.Same(rectangular.Breakpoints, rectangular.Breakpoints);
        Assert.IsNotType<double[]>(rectangular.Breakpoints);
        Assert.Equal([0.0, 0.7], (rectangular with { DutyCycle = 0.7 }).Breakpoints);
        Assert.Equal([0.0, 0.3], rectangular.Breakpoints);

        _ = rectangular.Breakpoints;
        Assert.Equal(new RfWaveform.Rectangular(0.3), rectangular);
        Assert.Equal(new RfWaveform.Rectangular(0.3).GetHashCode(), rectangular.GetHashCode());
        Assert.NotEqual(new RfWaveform.Rectangular(0.4), rectangular);
    }

    [Fact]
    public void MeasureTheCostOfACall()
    {
        var field = Solved(new RfWaveform.Rectangular(0.5), Megahertz, 0.0, 0.5);
        var t = 0.0;
        for (var i = 0; i < 10_000; i++)
        {
            t = field.NextEdgeAfter(t);
        }

        // The fastest of five passes: the first runs while the tiered JIT is still waiting
        // to recompile the method, which it does after a delay rather than a call count.
        const int calls = 1_000_000;
        var fastest = double.PositiveInfinity;
        for (var pass = 0; pass < 5; pass++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < calls; i++)
            {
                t = field.NextEdgeAfter(t);
            }

            fastest = Math.Min(fastest, watch.Elapsed.TotalNanoseconds / calls);
        }

        output.WriteLine($"one drive, two taps: {fastest:F1} ns per call (t {t:G3})");
        Assert.True(t > 0.0);
    }
}

/// <summary>The search itself, from a nominal instant far from the crossing.</summary>
public sealed class EdgeSearchTests
{
    [Theory]
    [InlineData(0.75, -0.26e-6, 0.5e-6)]
    [InlineData(-0.75, 0.76e-6, 1.5e-6)]
    [InlineData(1.0e9, 0.0, 0.5e-6)]
    [InlineData(1.0e300, 0.0, 0.5e-6)]
    [InlineData(-1.0e300, 0.0, 0.5e-6)]
    public void ANominalInstantAnywhereStillFindsTheCrossing(double wrongPhase, double after, double expected)
    {
        // The nominal instant is computed with a phase the clock does not use, so the search
        // must widen across zero or most of the range of doubles before it brackets the flip.
        var clock = new DriveClock(1.0e6, 0.0);

        var edge = EdgeInstants.Next(clock, 1.0e6, wrongPhase, 0.5, after);

        Assert.True(Math.Abs(edge - expected) <= 1.0e-21, $"edge {edge:R} s, expected {expected:R} s");
        Assert.True(clock.PhaseAt(Math.BitDecrement(edge)) < Math.Floor(clock.PhaseAt(edge)) + 0.5);
    }
}
