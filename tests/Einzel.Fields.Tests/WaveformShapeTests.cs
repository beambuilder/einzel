using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields.Analytic;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// The two shapes a drive can now be written in besides the built-in ones: an expression
/// of the phase, and a table over one period.
/// </summary>
/// <remarks>
/// <para>
/// Each is checked against something it must reduce to. An expression that writes a
/// square wave must <em>be</em> the rectangular wave, to the bit, because the corpus case
/// built on it will be compared verdict for verdict. A table of a cosine must converge on
/// the sinusoid at the order its interpolant promises, and its mean must be the exact
/// integral of what it interpolates rather than another approximation on top.
/// </para>
/// </remarks>
public sealed class WaveformShapeTests(ITestOutputHelper output)
{
    private static readonly Dictionary<string, Quantity> NoParameters = new(StringComparer.Ordinal);

    private static RfWaveform.Analytic Analytic(string expression, IReadOnlyList<double>? breakpoints = null, params (string Name, double Value)[] parameters) =>
        new(expression, parameters.ToDictionary(p => p.Name, p => Quantity.Number(p.Value), StringComparer.Ordinal), breakpoints);

    /// <summary>Ten thousand phases: in the cycle, a few cycles either side, far out, and on the edges.</summary>
    private static double[] Phases()
    {
        var random = new Random(20261028);
        var phases = new List<double> { 0.0, 0.5, -0.5, 1.0, -1e-300, 1e-300, Math.BitDecrement(0.5), Math.BitIncrement(0.5), Math.BitDecrement(1.0), -3.0 };

        while (phases.Count < 10_000)
        {
            phases.Add((phases.Count % 4) switch
            {
                0 => random.NextDouble(),
                1 => (random.NextDouble() - 0.5) * 100.0,
                2 => (random.NextDouble() - 0.5) * 2e9,
                _ => Math.Round(random.NextDouble() * 64.0) / 8.0 - 4.0,
            });
        }

        return [.. phases];
    }

    private static double[] CosineSamples(int count, out double[] phases)
    {
        phases = [.. Enumerable.Range(0, count).Select(k => k / (double)count)];
        return [.. phases.Select(p => Math.Cos(2.0 * Math.PI * p))];
    }

    [Fact]
    public void AnExpressionOfASquareWaveIsTheRectangularWaveToTheBit()
    {
        var analytic = Analytic("1 - 2*floor(2*p)", [0.0, 0.5]);
        var rectangular = new RfWaveform.Rectangular(0.5);

        foreach (var phase in Phases())
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(rectangular.At(phase)), BitConverter.DoubleToInt64Bits(analytic.At(phase)));
        }
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(0.25)]
    [InlineData(0.7)]
    public void AnExpressionOfADutyCycleIsTheRectangularWaveToTheBit(double duty)
    {
        var analytic = Analytic("1 - 2*floor(p + 1 - d)", [0.0, duty], ("d", duty));
        var rectangular = new RfWaveform.Rectangular(duty);

        foreach (var phase in Phases())
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(rectangular.At(phase)), BitConverter.DoubleToInt64Bits(analytic.At(phase)));
        }
    }

    [Fact]
    public void APhaseJustBelowAWholeCycleIsTheEndOfTheCycleNotItsStart()
    {
        // -1e-300 wraps to 1.0 in rounding; floor(2 * 1.0) would read it as a third level.
        Assert.Equal(-1.0, Analytic("1 - 2*floor(2*p)").At(-1e-300));
        Assert.Equal(1.0, Analytic("2*p - 1").At(-1e-300), 1e-15);
    }

    [Theory]
    [InlineData("1 - 4*abs(p - 0.5)", 0.0, -1.0)]
    [InlineData("1 - 4*abs(p - 0.5)", 0.25, 0.0)]
    [InlineData("1 - 4*abs(p - 0.5)", 0.5, 1.0)]
    [InlineData("max(-1, min(1, (1 - 4*abs(p - 0.5)) / (1 - 2*r)))", 0.5, 1.0)]
    [InlineData("max(-1, min(1, (1 - 4*abs(p - 0.5)) / (1 - 2*r)))", 0.4, 1.0)]
    [InlineData("max(-1, min(1, (1 - 4*abs(p - 0.5)) / (1 - 2*r)))", 0.25, 0.0)]
    [InlineData("max(-1, min(1, (1 - 4*abs(p - 0.5)) / (1 - 2*r)))", 0.2, -1.0 / 3.0)]
    [InlineData("max(-1, min(1, (1 - 4*abs(p - 0.5)) / (1 - 2*r)))", 0.05, -1.0)]
    [InlineData("max(-c, min(c, sinPi(2*p))) / c", 0.25, 1.0)]
    [InlineData("max(-c, min(c, sinPi(2*p))) / c", 0.75, -1.0)]
    [InlineData("max(-c, min(c, sinPi(2*p))) / c", 0.0, 0.0)]
    [InlineData("1 - 2*floor(mod(k*p, 1) + 1 - w)", 0.05, 1.0)]
    [InlineData("1 - 2*floor(mod(k*p, 1) + 1 - w)", 0.15, -1.0)]
    [InlineData("1 - 2*floor(mod(k*p, 1) + 1 - w)", 0.30, 1.0)]
    [InlineData("1 - 2*floor(mod(k*p, 1) + 1 - w)", 0.45, -1.0)]
    public void TheHandoffsShapesAreExpressibleAndTakeTheirValues(string expression, double phase, double expected)
    {
        // Rise fraction 0.2 (the trapezoid reaches its levels a fifth of a cycle apart),
        // clip at 0.6 of the sine's peak, four pulses each 0.4 of their slot.
        var waveform = Analytic(expression, null, ("r", 0.2), ("c", 0.6), ("k", 4.0), ("w", 0.4));

        Assert.Equal(expected, waveform.At(phase), 1e-12);
        Assert.Equal(expected, waveform.At(phase - 3.0), 1e-12);
    }

    [Fact]
    public void ASquareWavesMeanIsExactlyZeroWhenItsEdgesAreDeclared()
    {
        Assert.Equal(0.0, Analytic("1 - 2*floor(2*p)", [0.0, 0.5]).Mean);
    }

    [Fact]
    public void ADutyCyclesMeanIsTwoDMinusOneWhenItsEdgesAreDeclared()
    {
        var mean = Analytic("1 - 2*floor(p + 1 - d)", [0.0, 0.3], ("d", 0.3)).Mean;

        output.WriteLine($"mean {mean:R}, against -0.4 by {mean + 0.4:E2}");
        Assert.Equal(-0.4, mean, 1e-12);
    }

    [Fact]
    public void AnUndeclaredJumpCostsTheMeanLessThanOnePanel()
    {
        var mean = Analytic("1 - 2*floor(p + 1 - d)", null, ("d", 0.3)).Mean;

        output.WriteLine($"undeclared: mean {mean:R}, off by {mean + 0.4:E2}");
        Assert.InRange(Math.Abs(mean + 0.4), 1e-6, 2.0 / 1024);
    }

    [Fact]
    public void ASineHasNoMeanAndAConstantIsItsOwn()
    {
        Assert.Equal(0.0, Analytic("sinPi(2*p)").Mean, 1e-15);
        Assert.Equal(0.25, Analytic("0.25").Mean);
        Assert.Equal(1.0 / 3.0, Analytic("p*p").Mean, 1e-15);
    }

    [Theory]
    [InlineData("log(p)")]
    [InlineData("sqrt(p - 0.5)")]
    [InlineData("1 / (p - 0.25)")]
    public void AnExpressionThatIsNotFiniteOverTheCycleIsRefused(string expression)
    {
        Assert.Throws<ArgumentException>(() => Analytic(expression));
    }

    [Fact]
    public void ADeclaredBreakpointIsSampledWhereTheUniformGridMissesIt()
    {
        // 0.3 is not a multiple of 1/4096, so only the breakpoint lands on the pole.
        _ = Analytic("1 / (p - 0.3)");
        Assert.Throws<ArgumentException>(() => Analytic("1 / (p - 0.3)", [0.3]));
    }

    [Fact]
    public void ThePhaseJustBelowEachBreakpointIsSampled()
    {
        // Finite at the breakpoint and on the whole uniform grid, infinite one step below
        // it: only the just-below probe sees these. 0.29999999999999993 is the double
        // before 0.3; p + 1e-15 reaches 1 only in the last few ulps of the cycle.
        _ = Analytic("1 / (p - 0.29999999999999993)");
        Assert.Throws<ArgumentException>(() => Analytic("1 / (p - 0.29999999999999993)", [0.3]));

        _ = Analytic("1 / (1 - floor(p + 1e-15))");
        Assert.Throws<ArgumentException>(() => Analytic("1 / (1 - floor(p + 1e-15))", [0.0]));
    }

    [Theory]
    [InlineData(WaveformInterpolation.Linear)]
    [InlineData(WaveformInterpolation.Cubic)]
    public void ReadingAShapeAllocatesNothing(WaveformInterpolation interpolation)
    {
        CosineSamples(64, out var phases);
        RfWaveform[] shapes =
        [
            Analytic("max(-1, min(1, (1 - 4*abs(p - 0.5)) / (1 - 2*r)))", null, ("r", 0.2)),
            new RfWaveform.Tabulated(phases, CosineSamples(64, out _), interpolation),
        ];

        foreach (var shape in shapes)
        {
            var sink = 0.0;

            for (var i = 0; i < 1000; i++)
            {
                sink += shape.At(i * 0.000123457);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();

            for (var i = 0; i < 100_000; i++)
            {
                sink += shape.At(i * 0.000123457);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            output.WriteLine($"{shape.GetType().Name}: {allocated} bytes over 1e5 calls (sum {sink:G3})");
            Assert.Equal(0, allocated);
        }
    }

    [Fact]
    public void AnExpressionThatDoesNotCompileIsRefusedInTheEvaluatorsTerms()
    {
        Assert.Throws<EinzelException>(() => Analytic("1 - q*p"));
    }

    [Theory]
    [InlineData(new[] { 0.5, 0.2 })]
    [InlineData(new[] { 0.2, 0.2 })]
    [InlineData(new[] { 1.0 })]
    [InlineData(new[] { -0.1 })]
    [InlineData(new[] { double.NaN })]
    public void BreakpointsOutOfOrderOrOutsideTheCycleAreRefused(double[] breakpoints)
    {
        Assert.Throws<ArgumentException>(() => Analytic("p", breakpoints));
        Assert.Throws<ArgumentException>(() => new RfWaveform.Tabulated([0.0, 0.3, 0.6], [0.0, 1.0, 0.0], breakpoints: breakpoints));
    }

    [Fact]
    public void TheBuiltInShapesDeclareTheirJumps()
    {
        Assert.Equal([0.0, 0.3], new RfWaveform.Rectangular(0.3).Breakpoints);
        Assert.Empty(new RfWaveform.Sinusoid().Breakpoints);
        Assert.Empty(new RfWaveform.Harmonic([new HarmonicTerm(1, 1.0, 0.0)]).Breakpoints);
        Assert.Equal([0.0, 0.5], Analytic("1 - 2*floor(2*p)", [0.0, 0.5]).Breakpoints);
    }

    [Fact]
    public void NeitherNewShapeClaimsASingleHarmonic()
    {
        // Even an expression that is exactly a cosine: what a shape is cannot be told from
        // its text, and the effective-potential path must refuse rather than guess.
        CosineSamples(64, out var phases);

        Assert.True(double.IsNaN(Analytic("cosPi(2*p)").SingleHarmonicOrder));
        Assert.True(double.IsNaN(new RfWaveform.Tabulated(phases, CosineSamples(64, out _)).SingleHarmonicOrder));
    }

    [Theory]
    [InlineData(WaveformInterpolation.Linear, 4.0)]
    [InlineData(WaveformInterpolation.Cubic, 16.0)]
    public void ATableOfACosineConvergesOnTheSinusoidAtTheInterpolantsOrder(WaveformInterpolation interpolation, double ratio)
    {
        var sinusoid = new RfWaveform.Sinusoid();
        var errors = new List<double>();

        foreach (var count in new[] { 16, 32, 64, 128 })
        {
            var values = CosineSamples(count, out var phases);
            var table = new RfWaveform.Tabulated(phases, values, interpolation);
            var worst = 0.0;

            for (var k = 0; k <= 10_000; k++)
            {
                var phase = k / 10_000.0;
                worst = Math.Max(worst, Math.Abs(table.At(phase) - sinusoid.At(phase)));
            }

            output.WriteLine($"{interpolation} N={count}: max error {worst:E3}, mean {table.Mean:E2}");
            errors.Add(worst);
        }

        for (var i = 1; i < errors.Count; i++)
        {
            Assert.InRange(errors[i - 1] / errors[i], ratio * 0.9, ratio * 1.1);
        }
    }

    [Theory]
    [InlineData(WaveformInterpolation.Linear)]
    [InlineData(WaveformInterpolation.Cubic)]
    public void ATablesMeanIsExactToRoundingForAConstantAndATriangle(WaveformInterpolation interpolation)
    {
        Assert.Equal(0.7, new RfWaveform.Tabulated([0.0, 0.125, 0.25, 0.5, 0.875], [0.7, 0.7, 0.7, 0.7, 0.7], interpolation).Mean, 1e-15);

        // Uniform samples of a triangle peaking at the half cycle; on a uniform grid the
        // spline's curvatures sum to zero and its integral is the trapezoid's.
        Assert.Equal(0.5, new RfWaveform.Tabulated([0.0, 0.25, 0.5, 0.75], [0.0, 0.5, 1.0, 0.5], interpolation).Mean, 1e-15);
    }

    [Theory]
    [InlineData(WaveformInterpolation.Linear)]
    [InlineData(WaveformInterpolation.Cubic)]
    public void ATablesMeanIsTheExactIntegralOfItsInterpolantOnAnUnevenGrid(WaveformInterpolation interpolation)
    {
        // Simpson's rule is exact for a cubic, so applied per segment it integrates either
        // interpolant to rounding - the reference a closed form can be checked against.
        double[] phases = [0.05, 0.1, 0.3, 0.35, 0.6, 0.9];
        double[] values = [0.2, -0.4, 1.0, 0.3, -0.9, 0.5];
        var table = new RfWaveform.Tabulated(phases, values, interpolation);
        var integral = 0.0;

        for (var i = 0; i < phases.Length; i++)
        {
            var start = phases[i];
            var end = i + 1 < phases.Length ? phases[i + 1] : phases[0] + 1.0;
            var near = Math.BitDecrement(end);
            integral += (end - start) / 6.0 * (table.At(start) + (4.0 * table.At((start + end) / 2.0)) + table.At(near));
        }

        output.WriteLine($"{interpolation}: Mean {table.Mean:R}, Simpson {integral:R}");
        Assert.Equal(integral, table.Mean, 1e-14);
    }

    [Theory]
    [InlineData(WaveformInterpolation.Linear)]
    [InlineData(WaveformInterpolation.Cubic)]
    public void ATableRunsAcrossTheWrapWithoutASeam(WaveformInterpolation interpolation)
    {
        double[] phases = [0.1, 0.3, 0.6, 0.8];
        double[] values = [1.0, -0.5, 0.25, -1.0];
        var table = new RfWaveform.Tabulated(phases, values, interpolation);
        const double Step = 1e-7;

        Assert.Equal(table.At(0.0), table.At(Math.BitDecrement(1.0)), 1e-14);
        Assert.Equal(table.At(0.0), table.At(-1.0), 1e-14);
        Assert.Equal(values[0], table.At(0.1), 1e-15);
        Assert.Equal(values[0], table.At(1.1), 1e-14);

        if (interpolation == WaveformInterpolation.Linear)
        {
            // Half-way along the closing segment, from 0.8 to 1.1.
            Assert.Equal((values[3] + values[0]) / 2.0, table.At(0.95), 1e-14);
        }
        else
        {
            // Slope and curvature continuous at phase zero, which is not a knot, and at the
            // first knot, which closes the period: one-sided differences either side agree.
            // The cubic term moves the curvature a little over the few steps the second
            // differences span; a curvature jump would be the size of the curvature itself.
            const double Wide = 1e-5;

            foreach (var at in new[] { 0.0, 0.1 })
            {
                var left = (table.At(at) - table.At(at - Step)) / Step;
                var right = (table.At(at + Step) - table.At(at)) / Step;
                Assert.Equal(left, right, 1e-4);

                var curveLeft = (table.At(at) - (2.0 * table.At(at - Wide)) + table.At(at - (2.0 * Wide))) / (Wide * Wide);
                var curveRight = (table.At(at + (2.0 * Wide)) - (2.0 * table.At(at + Wide)) + table.At(at)) / (Wide * Wide);
                output.WriteLine($"at {at}: curvature {curveLeft:G6} left, {curveRight:G6} right");
                Assert.Equal(curveLeft, curveRight, 0.1);
            }
        }
    }

    [Theory]
    [InlineData(new[] { 0.0, 0.5 }, new[] { 1.0, -1.0 })]
    [InlineData(new[] { 0.0, 0.5, 0.25 }, new[] { 1.0, -1.0, 0.0 })]
    [InlineData(new[] { 0.0, 0.5, 1.0 }, new[] { 1.0, -1.0, 0.0 })]
    [InlineData(new[] { 0.0, 0.5, 0.7 }, new[] { 1.0, double.PositiveInfinity, 0.0 })]
    [InlineData(new[] { 0.0, 0.5, 0.7 }, new[] { 1.0, -1.0 })]
    public void AMalformedTableIsRefused(double[] phases, double[] values)
    {
        Assert.Throws<ArgumentException>(() => new RfWaveform.Tabulated(phases, values));
    }

    [Fact]
    public void AReadTableBuildsTheWaveform()
    {
        var read = WaveformTable.Parse("phase,value\n0,1\n0.25,0\n0.5,-1\n0.75,0", "/w");
        var table = new RfWaveform.Tabulated(read.Phases, read.Values, WaveformInterpolation.Cubic);

        Assert.Equal(-1.0, table.At(0.5), 1e-15);
    }

    [Fact]
    public void TwoExpressionsAreEqualWhenTheirTextValuesAndBreakpointsAre()
    {
        RfWaveform first = Analytic("1 - 2*floor(p + 1 - d)", [0.0, 0.3], ("d", 0.3), ("unused", 7.0));
        RfWaveform second = Analytic("1 - 2*floor(p + 1 - d)", [0.0, 0.3], ("d", 0.3));

        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual<RfWaveform>(first, Analytic("1 - 2*floor(p + 1 - d)", [0.0, 0.3], ("d", 0.31)));
        Assert.NotEqual<RfWaveform>(first, Analytic("1 - 2*floor(p + 1 - d)", [0.0, 0.31], ("d", 0.3)));
        Assert.NotEqual<RfWaveform>(first, Analytic("1 - 2*floor(p + 1-d)", [0.0, 0.3], ("d", 0.3)));
        Assert.NotEqual<RfWaveform>(first, (RfWaveform)new RfWaveform.Rectangular(0.3));
    }

    [Fact]
    public void TwoTablesAreEqualWhenTheirSamplesInterpolationAndBreakpointsAre()
    {
        RfWaveform first = new RfWaveform.Tabulated(new List<double> { 0.0, 0.3, 0.6 }, new List<double> { 0.0, 1.0, 0.0 });
        RfWaveform second = new RfWaveform.Tabulated([0.0, 0.3, 0.6], [0.0, 1.0, 0.0]);

        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual<RfWaveform>(first, new RfWaveform.Tabulated([0.0, 0.3, 0.6], [0.0, 1.0, 0.1]));
        Assert.NotEqual<RfWaveform>(first, new RfWaveform.Tabulated([0.0, 0.3, 0.7], [0.0, 1.0, 0.0]));
        Assert.NotEqual<RfWaveform>(first, new RfWaveform.Tabulated([0.0, 0.3, 0.6], [0.0, 1.0, 0.0], WaveformInterpolation.Cubic));
        Assert.NotEqual<RfWaveform>(first, new RfWaveform.Tabulated([0.0, 0.3, 0.6], [0.0, 1.0, 0.0], breakpoints: [0.3]));
    }

    [Fact]
    public void ManyFlightsReadOneShapeAtOnceAndGetWhatOneFlightGets()
    {
        CosineSamples(64, out var phases);
        RfWaveform[] shapes =
        [
            Analytic("max(-1, min(1, (1 - 4*abs(p - 0.5)) / (1 - 2*r)))", null, ("r", 0.2)),
            new RfWaveform.Tabulated(phases, CosineSamples(64, out _), WaveformInterpolation.Cubic),
        ];

        foreach (var shape in shapes)
        {
            const int Count = 100_000;
            var sequential = new double[Count];
            var parallel = new double[Count];

            for (var i = 0; i < Count; i++)
            {
                sequential[i] = shape.At(i * 0.000123457);
            }

            Parallel.For(0, Count, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i => parallel[i] = shape.At(i * 0.000123457));

            Assert.Equal(sequential, parallel);
        }
    }
}
