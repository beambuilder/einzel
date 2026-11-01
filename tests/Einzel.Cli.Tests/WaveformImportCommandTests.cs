using System.Globalization;
using System.Text;
using Einzel.Commands;
using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Fields.Analytic;

namespace Einzel.Cli.Tests;

/// <summary>
/// The numerics of <c>einzel waveform import</c> on synthetic scope traces with a known
/// truth: 25 periods at 1.1 MHz sampled every nanosecond from a negative start, amplitude
/// 180 V about 7.5 V, the rising midline crossing of the shape at phase 0, Gaussian noise
/// from a seeded generator.
/// </summary>
public sealed class WaveformImportCommandTests : IDisposable
{
    private const double Frequency = 1.1e6;
    private const double Period = 1.0 / Frequency;
    private const double Amplitude = 180.0;
    private const double Offset = 7.5;
    private const double Noise = 1.8;
    private const double RectRamp = 2e-9 * Frequency;
    private const double TrapRamp = 0.05 / 0.8;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "einzel-waveform-import", Guid.NewGuid().ToString("N"));

    /// <summary>Creates the scratch directory.</summary>
    public WaveformImportCommandTests() => Directory.CreateDirectory(_root);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Theory]
    [InlineData(0.0, 1e-5)]
    [InlineData(Noise, 1e-4)]
    public void ThePeriodIsFoundWhenTheNominalIsOnePercentOff(double noise, double tolerance)
    {
        var trace = Write(Trapezoid(0.5, TrapRamp), noise, 11);

        var outcome = Import(trace.Path, o => o with { FrequencyHz = Frequency * 1.0103 });

        Assert.InRange(Math.Abs(outcome.PeriodSeconds - Period) / Period, 0.0, tolerance);
        Assert.Equal(Period / 1.0103, outcome.PeriodNominalSeconds, 1e-18);
        Assert.Equal(25, outcome.PeriodsFolded);
    }

    [Fact]
    public void ANominalFivePercentOffIsRefusedAndNoRefineKeepsTheNominal()
    {
        var trace = Write(Trapezoid(0.5, TrapRamp), 0.0, 12);

        var failure = Assert.Throws<EinzelException>(() => Import(trace.Path, o => o with { FrequencyHz = Frequency * 1.05 }));
        var kept = Import(trace.Path, o => o with { FrequencyHz = null, PeriodSeconds = Period * 1.001, Refine = false });

        Assert.Equal(ErrorCodes.ValueOutOfBounds, failure.Error.Code);
        Assert.Equal(trace.Path, failure.Error.Path);
        Assert.Contains("repeats at", failure.Error.Constraint, StringComparison.Ordinal);
        Assert.Equal(Period * 1.001, kept.PeriodSeconds);
    }

    /// <summary>
    /// Within a fifth of a nanosecond: the refinement folds onto 227 bins of four samples, which
    /// place a two-sample edge's period to 4e-6 to 1e-5 (0.1 ns here) because each sample is
    /// shared between its two nearest bins; on one bin alone it is
    /// more than 0.15 ns off.
    /// </summary>
    [Fact]
    public void TheRisingOriginOfASharpRectangleIsItsTrueEdgeWithinAFifthOfANanosecond()
    {
        var trace = Write(Trapezoid(0.5, RectRamp), 0.0, 13);

        var outcome = Import(trace.Path);

        Assert.InRange(Math.Abs(outcome.OriginSeconds - trace.T0), 0.0, 0.2e-9);
    }

    [Fact]
    public void AFallingTriggerAndALevelTriggerFindTheirOwnCrossings()
    {
        var trace = Write(Trapezoid(0.5, TrapRamp), 0.0, 14);
        var falling = trace.T0 + (0.5 * Period);
        falling -= falling - Period >= trace.Start ? Period : 0.0;

        var down = Import(trace.Path, o => o with { Trigger = "falling" });
        var level = Import(trace.Path, o => o with { Trigger = "level=97.5" });

        // 97.5 V is half way up the ramp from the midline: a quarter of the full ramp after it.
        Assert.InRange(Math.Abs(down.OriginSeconds - falling), 0.0, 0.1e-9);
        Assert.InRange(Math.Abs(level.OriginSeconds - (trace.T0 + (TrapRamp / 4.0 * Period))), 0.0, 0.1e-9);
    }

    [Fact]
    public void ANoisySinusoidTriggersWithinOneNanosecondAndAConstantTraceIsRefused()
    {
        var sine = Write(Sinusoid, Noise, 15);
        var flat = Write(_ => 0.0, 0.0, 16);

        var outcome = Import(sine.Path);
        var failure = Assert.Throws<EinzelException>(() => Import(flat.Path));

        Assert.InRange(Math.Abs(outcome.OriginSeconds - sine.T0), 0.0, 1e-9);
        Assert.Equal(ErrorCodes.ValueOutOfBounds, failure.Error.Code);
        Assert.Contains("no rising crossing", failure.Error.Constraint, StringComparison.Ordinal);
    }

    /// <summary>
    /// A bump in the low half that crosses the midline but not the hysteresis band is not
    /// the trigger: the fold falls back below the band, and the crossing it had is dropped.
    /// Over four start phases, some arm before the bump.
    /// </summary>
    [Theory]
    [InlineData(29)]
    [InlineData(30)]
    [InlineData(31)]
    [InlineData(32)]
    public void ABumpThroughTheMidlineInsideTheHysteresisDoesNotTrigger(int seed)
    {
        var trapezoid = Trapezoid(0.5, TrapRamp);
        var trace = Write(p => trapezoid(p) + (p is > 0.6 and < 0.65 ? 1.05 : 0.0), 0.0, seed);

        var outcome = Import(trace.Path);

        Assert.InRange(Math.Abs(outcome.OriginSeconds - trace.T0), 0.0, 1e-9);
    }

    /// <summary>
    /// R5: the rising edge sits on a bin boundary by construction; a falling edge at duty
    /// 0.3 sits at 76.8 bins of 256, inside bin 76, whose mean is between the levels and
    /// moves the table's crossing to where the edge is rather than to a bin edge.
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(0.3)]
    public void TheTablesMidlineCrossingsSitAtTheJumpsOnABinBoundaryAndInsideABin(double duty)
    {
        var trace = Write(Trapezoid(duty, RectRamp), 0.0, 17);

        var outcome = Import(trace.Path, o => o with { RemoveDc = true });
        var table = WaveformTable.Parse(File.ReadAllText(outcome.Table), "table");
        var bins = Crossings(table.Values).Select(c => c * 256.0).ToArray();

        Assert.Equal(2, bins.Length);
        Assert.Contains(bins, b => Math.Abs(Circular(b, 0.0, 256.0)) < 0.1);
        Assert.Contains(bins, b => Math.Abs(b - (duty * 256.0)) < 0.1);

        if (duty == 0.3)
        {
            Assert.InRange(table.Values[76], 0.4, 0.8);
        }
    }

    [Theory]
    [InlineData("trapezoid", 0.5, TrapRamp)]
    [InlineData("trapezoid", 0.3, TrapRamp)]
    [InlineData("rect", 0.5, RectRamp)]
    [InlineData("sinusoid", 0.5, 0.0)]
    public void TheWrittenTableReadsBackAsADriveThatFollowsTheShape(string kind, double duty, double ramp)
    {
        var shape = kind == "sinusoid" ? Sinusoid : Trapezoid(duty, ramp);
        var slope = kind == "sinusoid" ? 2.0 * Math.PI : 2.0 / ramp;
        var trace = Write(shape, Noise, 18);

        var outcome = Import(trace.Path, o => o with { RemoveDc = true });
        var table = WaveformTable.Parse(File.ReadAllText(outcome.Table), "table");
        var interpolation = outcome.Waveform.Interpolation == "cubic" ? WaveformInterpolation.Cubic : WaveformInterpolation.Linear;
        var drive = new RfWaveform.Tabulated(table.Phases, table.Values, interpolation, outcome.Waveform.Breakpoints);
        var bound = (slope * Amplitude / 256.0) + (3.0 * outcome.NoiseRms);

        for (var i = 0; i < 1000; i++)
        {
            var p = (i + 0.5) / 1000.0;
            Assert.InRange(Math.Abs(outcome.ModelU + (outcome.ModelV * drive.At(p)) - (Offset + (Amplitude * shape(p)))), 0.0, bound);
        }

        Assert.Equal("tabulated", outcome.Waveform.Type);
        Assert.Equal(Path.GetRelativePath(Directory.GetCurrentDirectory(), outcome.Table).Replace('\\', '/'), outcome.Waveform.File);
        Assert.Equal(kind == "sinusoid" ? "cubic" : "linear", outcome.Waveform.Interpolation);

        if (kind == "sinusoid")
        {
            Assert.Null(outcome.Waveform.Breakpoints);
            Assert.False(outcome.LandAdvised);
            return;
        }

        // The declared pair sits at the linear table's kinks: each ramp's corners (0 % and
        // 100 %, at phase50 -+ half the full ramp), the outer side of each within one bin. For
        // the 5 % trapezoid that is 8 bins out from the 50 % crossing, beyond the 10/90 levels.
        // A 2 ns ramp straddles a bin boundary and has no plateau sample within a bin of its
        // corners; its breakpoints are checked in the next test.
        var declared = outcome.Waveform.Breakpoints!;
        // Eight either way: the trapezoid's corners fall on bin boundaries (8 bins from each
        // crossing), so the table rounds each corner over two kinks, a plateau sample and the
        // half-slope sample after it.
        Assert.Equal(8, declared.Count);

        if (kind == "rect")
        {
            return;
        }

        foreach (var (start, end) in new[] { (-0.5 * ramp, 0.5 * ramp), (duty - (0.5 * ramp), duty + (0.5 * ramp)) })
        {
            Assert.Contains(declared, b => Circular(start, b, 1.0) is >= 0.0 and <= 1.0 / 256.0);
            Assert.Contains(declared, b => Circular(b, end, 1.0) is >= 0.0 and <= 1.0 / 256.0);
        }

        Assert.True(outcome.LandAdvised);
    }

    [Fact]
    public void ASharpRectangleDeclaresThePlateauSamplesAndTheSteepSegmentOfEachEdge()
    {
        var trace = Write(Trapezoid(0.5, RectRamp), 0.0, 19);

        var declared = Import(trace.Path).Waveform.Breakpoints!;

        // Each 2 ns edge straddles a bin boundary (the rising one at 0, the falling one at 128),
        // so the bins either side hold part of it: the table kinks at the plateau sample one
        // further out and at both ends of the steep segment across the boundary.
        Assert.Equal([0.5 / 256, 1.5 / 256, 126.5 / 256, 127.5 / 256, 128.5 / 256, 129.5 / 256, 254.5 / 256, 255.5 / 256], declared);
    }

    /// <summary>
    /// 500 periods at 100.37 samples each, or 5000 at 10.37: folded onto bins finer than the
    /// sample spacing, a period near the nominal at which every bin holds one phase of the
    /// sample lattice folds as tight as the true one; the refinement's bins are set by the
    /// samples per period, not by the record's length, so it does not.
    /// </summary>
    [Theory]
    [InlineData(100.37, 500, 1.0103)]
    [InlineData(10.37, 5000, 1.0103)]
    [InlineData(8.1, 5000, 1.003)]
    public void ALongRecordAtFewSamplesPerPeriodRefinesToTheTruePeriod(double samplesPerPeriod, int periods, double nominalOff)
    {
        var trace = Write(Trapezoid(0.5, TrapRamp), Noise, 33, periods, samplesPerPeriod);

        var outcome = Import(trace.Path, o => o with { FrequencyHz = Frequency * nominalOff, Samples = 8, Orders = 3 });

        Assert.InRange(Math.Abs(outcome.PeriodSeconds - Period) / Period, 0.0, 1e-5);
        Assert.Equal(2, outcome.Edges.Count);
    }

    /// <summary>
    /// At 2048 samples the 5 % trapezoid's ramps take 128 samples each, and the noise of a bin
    /// mean is a fifth of the step per sample: the line through the ramp meets the plateaus at
    /// the corners, and the straight ramp between them declares nothing.
    /// </summary>
    [Fact]
    public void AFineTableDeclaresTheTrapezoidsCornersAndNothingInsideItsRamps()
    {
        const int n = 2048;
        var trace = Write(Trapezoid(0.5, TrapRamp), Noise, 34);

        var declared = Import(trace.Path, o => o with { Samples = n, RemoveDc = true }).Waveform.Breakpoints!;

        foreach (var (start, end) in new[] { (-0.5 * TrapRamp, 0.5 * TrapRamp), (0.5 - (0.5 * TrapRamp), 0.5 + (0.5 * TrapRamp)) })
        {
            Assert.Contains(declared, b => Circular(start, b, 1.0) is >= 0.0 and <= 1.0 / n);
            Assert.Contains(declared, b => Circular(b, end, 1.0) is >= 0.0 and <= 1.0 / n);
            Assert.DoesNotContain(declared, b => Circular(b, start, 1.0) > 2.0 / n && Circular(end, b, 1.0) > 2.0 / n);
        }
    }

    /// <summary>
    /// A narrow pulse at N = 256: 7.5-bin ramps (6 bins from 10 to 90 %) and a top only two
    /// samples wide. The plateau beyond the rising edge's walk end runs into the falling edge
    /// at once, so the corners are not fitted there and the walk's ends stand.
    /// </summary>
    [Fact]
    public void ATopTwoSamplesWideKeepsItsCornersAtTheWalksEnds()
    {
        const double ramp = 7.5 / 256.0;
        var trace = Write(Trapezoid(9.5 / 256.0, ramp), 0.0, 37);

        var declared = Import(trace.Path).Waveform.Breakpoints!.Select(b => b * 256.0).Select(b => b > 128.0 ? b - 256.0 : b).ToArray();

        // The top corners at 3.75 and 5.75 bins; the ramps from -3.75 to 3.75 and 5.75 to 13.25.
        Assert.Contains(declared, b => Math.Abs(b - 3.75) <= 1.0);
        Assert.Contains(declared, b => Math.Abs(b - 5.75) <= 1.0);
        Assert.DoesNotContain(declared, b => b is > -2.75 and < 2.75);
        Assert.DoesNotContain(declared, b => b is > 6.75 and < 12.25);
    }

    [Fact]
    public void AtATwoNanosecondEdgeOnAFinerTableTheSteepSegmentsEndsAreDeclared()
    {
        const int n = 1024;
        var trace = Write(Trapezoid(0.5, RectRamp), 0.0, 35);

        var declared = Import(trace.Path, o => o with { Samples = n }).Waveform.Breakpoints!;

        // 2.25 bins of ramp centred on the bin boundaries at 0 and 512.
        Assert.Contains((n - 0.5) / n, declared);
        Assert.Contains(0.5 / n, declared);
        Assert.Contains(511.5 / n, declared);
        Assert.Contains(512.5 / n, declared);
    }

    [Fact]
    public void EveryOptionTheImportCannotUseIsRefusedBeforeTheTraceIsFolded()
    {
        var trace = Write(Trapezoid(0.5, TrapRamp), 0.0, 36, periods: 3);
        var gone = Path.Combine(_root, "gone");

        void Refused(string code, string named, Func<WaveformImportOptions, WaveformImportOptions> change)
        {
            var failure = Assert.Throws<EinzelException>(() => Import(trace.Path, change));
            Assert.Equal(code, failure.Error.Code);
            Assert.Contains(named, failure.Error.Constraint, StringComparison.Ordinal);
            Assert.False(string.IsNullOrEmpty(failure.Error.Suggestion));
        }

        var bounds = ErrorCodes.ValueOutOfBounds;
        Refused(bounds, "exactly one", o => o with { PeriodSeconds = Period });
        Refused(bounds, "exactly one", o => o with { FrequencyHz = null });
        Refused(bounds, "exactly one", o => o with { FrequencyHz = -1.0 });
        Refused(bounds, "fewer than 3", o => o with { Samples = 2 });
        Refused(bounds, "fewer than 3", o => o with { Samples = -5 });
        Refused(bounds, "is more than half the", o => o with { Samples = 3000, Orders = 1 });
        Refused(bounds, "orders", o => o with { Orders = 0 });
        Refused(bounds, "orders", o => o with { Orders = 128 });
        Refused(bounds, "columns", o => o with { TimeColumn = 0 });
        Refused(bounds, "columns", o => o with { ValueColumn = -1 });
        Refused(bounds, "columns", o => o with { TimeColumn = 2 });
        Refused(bounds, "time unit 'min'", o => o with { TimeUnit = "min" });
        Refused(bounds, "level=abc", o => o with { Trigger = "level=abc" });
        Refused(bounds, "rising=1", o => o with { Trigger = "rising=1" });
        Refused(bounds, "tolerance", o => o with { PeriodTolerance = 0.0 });
        Refused(bounds, "tolerance", o => o with { PeriodTolerance = 0.3 });
        Refused(bounds, "tolerance", o => o with { PeriodTolerance = double.NaN });
        Refused(bounds, "edge width", o => o with { EdgeWidthCycles = 0.0 });
        var sparse = Write(Trapezoid(0.5, TrapRamp), 0.0, 38, periods: 20, samplesPerPeriod: 4.5);
        var tooSparse = Assert.Throws<EinzelException>(() => Import(sparse.Path, o => o with { Samples = 3, Orders = 1 }));
        Assert.Equal(bounds, tooSparse.Error.Code);
        Assert.Contains("4.50 samples per period", tooSparse.Error.Constraint, StringComparison.Ordinal);
        Assert.Contains("at least 5 samples per period", tooSparse.Error.Constraint, StringComparison.Ordinal);
        Refused(ErrorCodes.SchemaInvalid, "is the trace itself", o => o with { OutPath = trace.Path });
        Refused(ErrorCodes.SchemaInvalid, $"'{gone}' does not exist", o => o with { OutPath = Path.Combine(gone, "t.csv") });

        // A tolerance of a quarter is still allowed.
        Assert.Equal(3, Import(trace.Path, o => o with { PeriodTolerance = 0.25 }).PeriodsFolded);
    }

    [Fact]
    public void BothNormalisationsGiveTheTraceBackThroughUAndV()
    {
        var trace = Write(Trapezoid(0.3, TrapRamp), 0.0, 20);

        var removed = Import(trace.Path, o => o with { RemoveDc = true });
        var removedTable = WaveformTable.Parse(File.ReadAllText(removed.Table), "table");
        var peak = Import(trace.Path, o => o with { Force = true });
        var peakTable = WaveformTable.Parse(File.ReadAllText(peak.Table), "table");

        Assert.Equal(Offset, removed.ModelU, 1e-6);
        Assert.Equal(Amplitude, removed.ModelV, 1e-6);
        Assert.Equal(-1.0, removedTable.Values.Min(), 1e-9);
        Assert.Equal(1.0, removedTable.Values.Max(), 1e-9);
        Assert.Equal(Offset - (0.4 * Amplitude), removed.Mean, 0.05);
        Assert.Equal(-0.4, removed.TableMean, 1e-3);
        Assert.Equal(0.0, peak.ModelU);
        Assert.Equal(Offset + Amplitude, peak.ModelV, 1e-6);
        Assert.Equal(1.0, peakTable.Values.Max(), 1e-12);
        Assert.Equal((Offset - Amplitude) / (Offset + Amplitude), peakTable.Values.Min(), 1e-6);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(Noise)]
    public void DutyEdgesAndNoiseAreMeasured(double noise)
    {
        var trapezoid = Import(Write(Trapezoid(0.3, TrapRamp), noise, 21).Path);
        var rect = Import(Write(Trapezoid(0.5, RectRamp), noise, 22).Path);
        var bin = Period / 256.0;

        Assert.InRange(Math.Abs(trapezoid.DutyCycle - 0.3), 0.0, 1.0 / 256.0);
        Assert.Equal(["rising", "falling"], trapezoid.Edges.Select(e => e.Sense));
        Assert.All(trapezoid.Edges, e => Assert.InRange(Math.Abs(e.Time10To90Seconds - (0.05 * Period)), 0.0, bin));
        Assert.InRange(Math.Abs(Circular(trapezoid.Edges[0].Phase50, 0.0, 1.0)), 0.0, 1.0 / 256.0);
        Assert.InRange(Math.Abs(trapezoid.Edges[1].Phase50 - 0.3), 0.0, 1.0 / 256.0);
        Assert.InRange(trapezoid.Edges[1].Phase90, 0.3 - 0.025 - (1.0 / 256.0), 0.3 - 0.025 + (1.0 / 256.0));
        Assert.All(rect.Edges, e => Assert.InRange(e.Time10To90Seconds, 1.6e-9 - (bin / 4.0), 1.6e-9 + (bin / 4.0)));
        Assert.Equal(rect.Edges.Min(e => e.Time10To90Seconds), rect.SharpestEdgeSeconds);
        Assert.Equal(rect.PeriodSeconds / 20.0, rect.CapSeconds);

        if (noise == 0.0)
        {
            Assert.InRange(trapezoid.NoiseRms, 0.0, 0.05);
            Assert.InRange(rect.NoiseRms, 0.0, 0.05);
        }
        else
        {
            Assert.InRange(trapezoid.NoiseRms, 0.8 * noise, 1.2 * noise);
            Assert.InRange(rect.NoiseRmsRelative, 0.8 * noise / Amplitude, 1.2 * noise / Amplitude);
        }
    }

    [Fact]
    public void TheHarmonicsOfASinusoidAreOneOrderAndASquaresDistortionIsItsOddSeries()
    {
        var sine = Import(Write(Sinusoid, 0.0, 23).Path);
        var square = Import(Write(Trapezoid(0.5, RectRamp), 0.0, 24).Path, o => o with { Orders = 127 });

        // The odd harmonics 1/n of a square wave, n from 3 up: sqrt(pi^2/8 - 1).
        Assert.Equal(16, sine.Harmonics.Count);
        Assert.Equal(1.0, sine.Harmonics[1].Amplitude);
        Assert.InRange(sine.Thd!.Value, 0.0, 1e-3);
        Assert.InRange(sine.NoiseRms, 0.0, 0.05);
        Assert.Null(sine.SharpestEdgeSeconds);
        Assert.Equal(128, square.Harmonics.Count);
        Assert.Equal(Math.Sqrt((Math.PI * Math.PI / 8.0) - 1.0), square.Thd!.Value, 0.02);
        Assert.Equal(1.0 / 3.0, square.Harmonics[3].Amplitude, 0.01);
    }

    /// <summary>
    /// A clean sinusoid at 1 GS/s on the exact period: a bin's samples are not symmetric about
    /// its centre (their phases drift 0.09 sample per period across the fold), so a bin mean is
    /// off the shape by its slope times their mean offset, 1.3e-4 of the amplitude here; the bin's
    /// line in phase at its centre is 5e-7 off. The residual is about the table's own
    /// least-squares sine. Not refined, so the period found (1e-6 off) does not enter.
    /// </summary>
    [Fact]
    public void AClean256PointSinusoidTableIsItsSineToTwoPartsPerMillion()
    {
        var outcome = Import(Write(Sinusoid, 0.0, 38).Path, o => o with { Refine = false });

        Assert.InRange(SineResidual(WaveformTable.Parse(File.ReadAllText(outcome.Table), "table")), 0.0, 2e-6);
    }

    /// <summary>The rms of a table about its least-squares sine <c>a sin + b cos + c</c>, relative to the sine's amplitude.</summary>
    private static double SineResidual(WaveformTable table)
    {
        var rows = table.Phases.Select(p => new[] { Math.Sin(2.0 * Math.PI * p), Math.Cos(2.0 * Math.PI * p), 1.0 }).ToArray();
        var m = new double[3, 4];

        for (var k = 0; k < rows.Length; k++)
        {
            for (var i = 0; i < 3; i++)
            {
                for (var j = 0; j < 3; j++)
                {
                    m[i, j] += rows[k][i] * rows[k][j];
                }

                m[i, 3] += rows[k][i] * table.Values[k];
            }
        }

        // Gauss-Jordan on the 3 x 3 normal equations (diagonally dominant on a uniform grid).
        for (var i = 0; i < 3; i++)
        {
            for (var r = 0; r < 3; r++)
            {
                var f = r == i ? 0.0 : m[r, i] / m[i, i];

                for (var c = 0; c < 4; c++)
                {
                    m[r, c] -= f * m[i, c];
                }
            }
        }

        var (a, b, d) = (m[0, 3] / m[0, 0], m[1, 3] / m[1, 1], m[2, 3] / m[2, 2]);
        var rms = Math.Sqrt(Enumerable.Range(0, rows.Length)
            .Average(k => Math.Pow(table.Values[k] - (a * rows[k][0]) - (b * rows[k][1]) - d, 2.0)));
        return rms / Math.Sqrt((a * a) + (b * b));
    }

    /// <summary>
    /// A trace of the second harmonic only, at the nominal fundamental: the fundamental is
    /// zero, so the distortion is null and the amplitudes stay absolute, with no NaN.
    /// </summary>
    [Fact]
    public void AZeroFundamentalLeavesTheDistortionNullAndTheAmplitudesAbsolute()
    {
        var outcome = Import(Write(p => Math.Cos(4.0 * Math.PI * p), 0.0, 39).Path, o => o with { RemoveDc = true });

        Assert.Null(outcome.Thd);
        Assert.All(outcome.Harmonics, h => Assert.True(double.IsFinite(h.Amplitude) && double.IsFinite(h.Phase)));
        Assert.Equal(1.0, outcome.Harmonics[2].Amplitude, 1e-3);
        Assert.InRange(outcome.Harmonics[1].Amplitude, 0.0, 1e-3);
    }

    [Fact]
    public void ABinThePeriodDoesNotFillTooFewSamplesAndTooFewPeriodsAreRefused()
    {
        var trace = Write(Trapezoid(0.5, TrapRamp), 0.0, 25);
        var short1 = Write(Trapezoid(0.5, TrapRamp), 0.0, 26, periods: 1);
        // Exactly 100 samples per period: every period samples the same 100 phases.
        var three = Write(Trapezoid(0.5, TrapRamp), 0.0, 28, periods: 3, samplesPerPeriod: 100.0);

        var fine = Assert.Throws<EinzelException>(() => Import(three.Path, o => o with { Samples = 128 }));
        var two = Assert.Throws<EinzelException>(() => Import(trace.Path, o => o with { Samples = 2 }));
        var periods = Assert.Throws<EinzelException>(() => Import(short1.Path, o => o with { Refine = false }));

        Assert.Contains("is finer than the trace resolves", fine.Error.Constraint, StringComparison.Ordinal);
        Assert.Contains("samples per period", fine.Error.Constraint, StringComparison.Ordinal);
        Assert.Contains("fewer than 3", two.Error.Constraint, StringComparison.Ordinal);
        Assert.Contains("full periods", periods.Error.Constraint, StringComparison.Ordinal);
        Assert.All([fine, two, periods], f => Assert.Equal(ErrorCodes.ValueOutOfBounds, f.Error.Code));
    }

    [Fact]
    public void AnExistingTableIsRefusedBeforeTheTraceIsReadUnlessForced()
    {
        var trace = Write(Trapezoid(0.5, TrapRamp), 0.0, 27);
        var table = Path.Combine(_root, "kept.csv");
        File.WriteAllText(table, "old");

        var missing = Assert.Throws<EinzelException>(() => Import(Path.Combine(_root, "absent.csv"), o => o with { OutPath = table, Force = false }));
        var unreadable = Assert.Throws<EinzelException>(() => Import(Path.Combine(_root, "absent.csv")));
        var forced = Import(trace.Path, o => o with { OutPath = table, Force = true });

        Assert.Equal(ErrorCodes.ValueOutOfBounds, missing.Error.Code);
        Assert.Equal(table, missing.Error.Path);
        Assert.Equal(ErrorCodes.SchemaInvalid, unreadable.Error.Code);
        Assert.Equal(Path.GetFullPath(table), forced.Table);
        Assert.StartsWith("phase,value\n", File.ReadAllText(table), StringComparison.Ordinal);
        Assert.Equal(Path.Combine(_root, "trace27.period.csv"), Import(trace.Path).Table);
    }

    private static Func<double, double> Trapezoid(double duty, double ramp) => p =>
    {
        var q = p - Math.Floor(p);
        q -= q >= 1.0 - (ramp / 2.0) ? 1.0 : 0.0;

        if (Math.Abs(q) <= ramp / 2.0)
        {
            return 2.0 * q / ramp;
        }

        if (Math.Abs(q - duty) <= ramp / 2.0)
        {
            return -2.0 * (q - duty) / ramp;
        }

        return q > ramp / 2.0 && q < duty - (ramp / 2.0) ? 1.0 : -1.0;
    };

    private static double Sinusoid(double p) => Math.Sin(2.0 * Math.PI * p);

    /// <summary>The signed distance from <paramref name="b"/> to <paramref name="a"/> around a cycle of <paramref name="cycle"/>.</summary>
    private static double Circular(double a, double b, double cycle) => a - b - (cycle * Math.Round((a - b) / cycle));

    /// <summary>The table's midline crossings in phase, linear between the bin centres.</summary>
    private static List<double> Crossings(IReadOnlyList<double> values)
    {
        var n = values.Count;
        var mid = (values.Max() + values.Min()) / 2.0;
        var found = new List<double>();

        for (var k = 0; k < n; k++)
        {
            var (a, b) = (values[k] - mid, values[(k + 1) % n] - mid);

            if ((a < 0.0) != (b < 0.0))
            {
                var phase = (k + 0.5 + (a / (a - b))) / n;
                found.Add(phase - Math.Floor(phase));
            }
        }

        return [.. found.Order()];
    }

    private static WaveformImportOutcome Import(string path, Func<WaveformImportOptions, WaveformImportOptions>? change = null)
    {
        var options = new WaveformImportOptions { TracePath = path, FrequencyHz = Frequency, Force = true };
        return WaveformImportCommand.Execute(change is null ? options : change(options));
    }

    private (string Path, double T0, double Start) Write(
        Func<double, double> shape, double noise, int seed, int periods = 25, double? samplesPerPeriod = null)
    {
        var dt = samplesPerPeriod is { } perPeriod ? Period / perPeriod : 1e-9;
        var random = new Random(seed);
        var (u, v) = (0.05 + (0.9 * random.NextDouble()), 0.05 + (0.9 * random.NextDouble()));
        const double start = -3.1e-6;
        var t0 = start + (u * Period);
        var count = (int)Math.Floor((u + periods + v) * Period / dt) + 1;
        var text = new StringBuilder("time,value\n");

        for (var i = 0; i < count; i++)
        {
            var t = start + (i * dt);
            var p = (t - t0) / Period;

            // Box-Muller from the same generator, so a seed fixes the timing and the noise.
            var gauss = Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble())) * Math.Cos(2.0 * Math.PI * random.NextDouble());
            text.Append(CultureInfo.InvariantCulture, $"{t:R},{Offset + (Amplitude * shape(p - Math.Floor(p))) + (noise * gauss):R}\n");
        }

        var path = Path.Combine(_root, $"trace{seed}.csv");
        File.WriteAllText(path, text.ToString());
        return (path, t0, start);
    }
}
