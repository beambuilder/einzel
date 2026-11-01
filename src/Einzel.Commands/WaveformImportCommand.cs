using System.Globalization;
using System.Text;
using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Fields.Analytic;

namespace Einzel.Commands;

/// <summary>What <c>einzel waveform import</c> is asked to do with a measured trace.</summary>
public sealed record WaveformImportOptions
{
    /// <summary>The trace file: one <c>time,value</c> row per sample (see <see cref="WaveformTrace"/>).</summary>
    public required string TracePath { get; init; }

    /// <summary>The nominal drive frequency in hertz; give this or <see cref="PeriodSeconds"/>.</summary>
    public double? FrequencyHz { get; init; }

    /// <summary>The nominal period in seconds; give this or <see cref="FrequencyHz"/>.</summary>
    public double? PeriodSeconds { get; init; }

    /// <summary>The phase origin: <c>rising</c> or <c>falling</c> midline crossing, or <c>level=&lt;v&gt;</c> rising through v.</summary>
    public string Trigger { get; init; } = "rising";

    /// <summary>The number of samples in the written period, three or more.</summary>
    public int Samples { get; init; } = 256;

    /// <summary>Whether to subtract the midline and divide by the amplitude, so the table spans [-1, 1], rather than divide by the larger extreme.</summary>
    public bool RemoveDc { get; init; }

    /// <summary>The written table's interpolation, or null for linear when an edge is found and cubic otherwise.</summary>
    public WaveformInterpolation? Interpolation { get; init; }

    /// <summary>The table to write, or null for <c>&lt;trace stem&gt;.period.csv</c> beside the trace.</summary>
    public string? OutPath { get; init; }

    /// <summary>Whether to overwrite an existing table.</summary>
    public bool Force { get; init; }

    /// <summary>The 1-based column of the time.</summary>
    public int TimeColumn { get; init; } = 1;

    /// <summary>The 1-based column of the value.</summary>
    public int ValueColumn { get; init; } = 2;

    /// <summary>The unit of the time column: <c>s</c>, <c>ms</c>, <c>us</c> or <c>ns</c>.</summary>
    public string TimeUnit { get; init; } = "s";

    /// <summary>How far, relative to the nominal, the found period may lie from it.</summary>
    public double PeriodTolerance { get; init; } = 0.02;

    /// <summary>Whether to refine the period; false keeps the nominal.</summary>
    public bool Refine { get; init; } = true;

    /// <summary>A 10-90 % transition narrower than this many cycles is an edge.</summary>
    public double EdgeWidthCycles { get; init; } = 0.1;

    /// <summary>The highest harmonic order reported.</summary>
    public int Orders { get; init; } = 15;
}

/// <summary>One edge of the averaged period.</summary>
/// <param name="Sense"><c>rising</c> or <c>falling</c>.</param>
/// <param name="Phase50">Where it crosses the midline, in cycles in [0, 1).</param>
/// <param name="Phase10">Where it crosses the 10 % level, unwrapped about <paramref name="Phase50"/>.</param>
/// <param name="Phase90">Where it crosses the 90 % level, unwrapped about <paramref name="Phase50"/>.</param>
/// <param name="Width10To90Cycles">The 10-90 % width in cycles.</param>
/// <param name="Time10To90Seconds">The 10-90 % time in seconds.</param>
public sealed record WaveformImportEdge(
    string Sense, double Phase50, double Phase10, double Phase90, double Width10To90Cycles, double Time10To90Seconds);

/// <summary>One harmonic of the written period.</summary>
/// <param name="Order">The multiple of the drive frequency; zero is the constant.</param>
/// <param name="Amplitude">Its amplitude relative to the fundamental's when that is non-zero, else absolute (in table units).</param>
/// <param name="Phase">Its phase in cycles.</param>
public sealed record WaveformImportHarmonic(int Order, double Amplitude, double Phase);

/// <summary>A measured trace folded onto one period, as <c>einzel waveform import</c> reports it.</summary>
/// <param name="Trace">The trace, as an absolute path.</param>
/// <param name="PreambleLines">Non-blank lines skipped before the first sample.</param>
/// <param name="SamplesInTrace">Samples read.</param>
/// <param name="SampleIntervalSeconds">The mean time between samples.</param>
/// <param name="PeriodNominalSeconds">The period asked for.</param>
/// <param name="PeriodSeconds">The period found (the nominal when not refined).</param>
/// <param name="FrequencyHz">The found period's frequency.</param>
/// <param name="PeriodDifferencePpm">Found minus nominal, in parts per million of the nominal.</param>
/// <param name="PeriodsFolded">Full periods folded from the origin.</param>
/// <param name="SamplesPerPeriod">Samples folded per period.</param>
/// <param name="Trigger">The trigger, as given.</param>
/// <param name="OriginSeconds">The trace time of phase 0.</param>
/// <param name="Offset">The midline of the averaged period, halfway between its extremes, in trace units.</param>
/// <param name="Amplitude">Half the peak-to-peak of the averaged period, in trace units.</param>
/// <param name="Mean">The mean of the averaged period, in trace units.</param>
/// <param name="TableMean">The mean of the written table.</param>
/// <param name="NormalisationRule">How the table was scaled.</param>
/// <param name="ModelU">The drive's <c>U</c> that, with the table, gives back the trace.</param>
/// <param name="ModelV">The drive's <c>V</c> that, with the table, gives back the trace.</param>
/// <param name="DutyCycle">The fraction of the period above the midline.</param>
/// <param name="Edges">The edges found, in phase order from the fold's minimum.</param>
/// <param name="SharpestEdgeSeconds">The shortest 10-90 % time, or null without edges.</param>
/// <param name="CapSeconds">The integrator's step cap, a twentieth of the period.</param>
/// <param name="LandAdvised">Whether breakpoints were declared, so <c>transport.edges: land</c> applies.</param>
/// <param name="NoiseRms">The median within-bin rms about a straight line, in trace units.</param>
/// <param name="NoiseRmsRelative">The noise relative to <paramref name="Amplitude"/>.</param>
/// <param name="Harmonics">Orders 0 to the asked highest, fitted to the table.</param>
/// <param name="Thd">The root-sum-square of orders two and up relative to the fundamental, or null when the fundamental is zero.</param>
/// <param name="Table">The table written, as an absolute path.</param>
/// <param name="Samples">The number of samples in the table.</param>
/// <param name="Waveform">The model's <c>tabulated</c> waveform object for the table.</param>
public sealed record WaveformImportOutcome(
    string Trace,
    int PreambleLines,
    int SamplesInTrace,
    double SampleIntervalSeconds,
    double PeriodNominalSeconds,
    double PeriodSeconds,
    double FrequencyHz,
    double PeriodDifferencePpm,
    int PeriodsFolded,
    double SamplesPerPeriod,
    string Trigger,
    double OriginSeconds,
    double Offset,
    double Amplitude,
    double Mean,
    double TableMean,
    string NormalisationRule,
    double ModelU,
    double ModelV,
    double DutyCycle,
    IReadOnlyList<WaveformImportEdge> Edges,
    double? SharpestEdgeSeconds,
    double CapSeconds,
    bool LandAdvised,
    double NoiseRms,
    double NoiseRmsRelative,
    IReadOnlyList<WaveformImportHarmonic> Harmonics,
    double? Thd,
    string Table,
    int Samples,
    WaveformDocument Waveform);

/// <summary>Folds a measured trace onto one averaged period and writes it as a model's table.</summary>
/// <remarks>
/// The period is refined near the nominal by the tightest fold, the origin is a crossing
/// of the averaged period, and the table holds the bin means of every full period from
/// there, at the bin centres, with the edges declared as breakpoints.
/// </remarks>
public static class WaveformImportCommand
{
    /// <summary>Reads, folds, averages and writes.</summary>
    /// <remarks>
    /// A nominal at half the true frequency is not detected: the fold at twice the period is
    /// as tight, so the table then holds two periods.
    /// </remarks>
    /// <param name="options">What to do.</param>
    /// <returns>The period, origin, scale, edges, noise and harmonics, and the table written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="EinzelException">
    /// The trace cannot be read or is malformed, or the table is the trace, lies in a missing
    /// directory or cannot be written (<c>SCHEMA_INVALID</c>); or an option is out
    /// of range, the table exists without <see cref="WaveformImportOptions.Force"/>, the period
    /// found lies beyond the tolerance, the trigger never fires, fewer than two full periods
    /// follow it, or the period does not fill every output bin (<c>VALUE_OUT_OF_BOUNDS</c>).
    /// </exception>
    public static WaveformImportOutcome Execute(WaveformImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var path = options.TracePath;
        var invariant = CultureInfo.InvariantCulture;
        var nominal = options.PeriodSeconds ?? (1.0 / options.FrequencyHz) ?? double.NaN;
        Require(path, (options.PeriodSeconds is null) != (options.FrequencyHz is null) && nominal > 0.0 && double.IsFinite(nominal),
            "give exactly one of the frequency and the period, positive and finite", "pass --frequency <Hz> or --period <s>");
        var scale = options.TimeUnit switch { "s" => 1.0, "ms" => 1e-3, "us" => 1e-6, "ns" => 1e-9, _ => double.NaN };
        Require(path, double.IsFinite(scale), $"the time unit '{options.TimeUnit}' is not s, ms, us or ns", "pass --time-unit s|ms|us|ns");
        Require(path, options.TimeColumn >= 1 && options.ValueColumn >= 1 && options.TimeColumn != options.ValueColumn,
            "the time and value columns must be two different columns, counted from 1", "pass --columns t,v");
        Require(path, options.PeriodTolerance is > 0.0 and <= 0.25, "the period tolerance must be in (0, 0.25]", "pass --period-tolerance 0.02");
        Require(path, options.EdgeWidthCycles is > 0.0 and <= 1.0, "the edge width must be in (0, 1] cycles", "pass --edge-width 0.1");
        var n = options.Samples;
        Require(path, n >= 3, string.Create(invariant, $"--samples {n} is fewer than 3"), "pass --samples 3 or more");
        Require(path, options.Orders >= 1 && (2L * options.Orders) + 1 <= n,
            string.Create(invariant, $"the harmonic orders must be one or more, and {n} samples determine at most {(n - 1) / 2}"),
            "pass fewer --orders, or more --samples");
        var level = Level(options.Trigger, path);

        var stem = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", Path.GetFileNameWithoutExtension(path));
        var outPath = options.OutPath ?? stem + ".period.csv";
        var table = Path.GetFullPath(outPath);
        var directory = Path.GetDirectoryName(table) ?? ".";
        var sameFile = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        RequirePath(outPath, !string.Equals(table, Path.GetFullPath(path), sameFile), $"the table '{outPath}' is the trace itself", "pass --out another file");
        RequirePath(outPath, Directory.Exists(directory), $"the table's directory '{directory}' does not exist",
            "create it, or pass --out a file in an existing directory");
        Require(outPath, options.Force || !File.Exists(table), $"the table '{outPath}' exists", "pass --force to overwrite it, or --out another file");

        string text;

        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw PathRefusal(path, $"the trace '{path}' could not be read: {ex.Message}", "check that the file exists and is readable");
        }

        var trace = WaveformTrace.Parse(text, path, options.TimeColumn, options.ValueColumn, scale);
        var (t, y) = (trace.Times, trace.Values);

        // The nominal samples per period at the median spacing; two periods are the least folded.
        var spacing = Enumerable.Range(1, t.Count - 1).Select(i => t[i] - t[i - 1]).Order().ElementAt((t.Count - 1) / 2);
        var nominalPerPeriod = nominal / spacing;
        Require(path, nominalPerPeriod >= 5.0,
            string.Create(
                invariant, $"the nominal period holds {nominalPerPeriod:F2} samples per period, and the importer needs at least 5 samples per period"),
            "check the frequency or the time unit");
        Require(path, n <= t.Count / 2,
            string.Create(invariant, $"--samples {n} is more than half the {t.Count} samples in the trace"),
            "pass --samples between 3 and the samples per period");

        // The fine fold resolves an edge below the output bin, for the trigger and the edges (an
        // empty fine bin is interpolated from its neighbours by Means). The refinement folds
        // coarser, a quarter of the samples per period, at least 4 and so at least 1.25 sample
        // spacings wide: on a long record with bins finer than the spacing, a period at which
        // every bin holds one phase of the sample lattice folds just as tight as the true one.
        var fine = Math.Max(n, Math.Min(t.Count / 16, 4096));
        var refineBins = Math.Clamp((int)Math.Round(nominalPerPeriod / 4.0), 4, 1024);

        var period = options.Refine ? WaveformImportAnalysis.Refine(t, y, nominal, options.PeriodTolerance, refineBins) : nominal;
        Require(path, Math.Abs(period - nominal) <= options.PeriodTolerance * nominal,
            string.Create(invariant, $"the trace repeats at {period:R} s, and the nominal period is {nominal:R} s"),
            "check the frequency, or widen --period-tolerance");

        var (rawSum, rawCount) = WaveformImportAnalysis.Fold(t, y, 0, t.Count, t[0], period, fine);
        var raw = WaveformImportAnalysis.Means(rawSum, rawCount);
        var half = (raw.Max() - raw.Min()) / 2.0;
        var sense = options.Trigger == "falling" ? -1 : 1;
        var phase0 = WaveformImportAnalysis.Crossing(raw, level ?? ((raw.Max() + raw.Min()) / 2.0), 0.1 * half, sense);
        Require(path, phase0 is not null, $"the averaged period has no {options.Trigger} crossing",
            "use --trigger level=<v> inside the trace's range, or falling");
        var origin = t[0] + (phase0!.Value * period);

        var periods = (int)Math.Floor((t[^1] - origin) / period);
        Require(path, periods >= 2, string.Create(invariant, $"the trace holds {periods} full periods after the origin, and at least 2 are needed"),
            "record a longer trace, or check the frequency");
        var from = Enumerable.Range(0, t.Count).First(i => t[i] >= origin);
        var to = Enumerable.Range(from, t.Count - from).FirstOrDefault(i => t[i] >= origin + (periods * period), t.Count);
        var perPeriod = (to - from) / (double)periods;

        var (sum, count) = WaveformImportAnalysis.Fold(t, y, from, to, origin, period, n);
        Require(path, count.All(c => c > 0),
            string.Create(invariant,$"--samples {n} is finer than the trace resolves: {perPeriod:F1} samples per period"),
            "pass --samples between 3 and the samples per period");
        var mean = Enumerable.Range(0, n).Select(k => sum[k] / count[k]).ToArray();
        var noise = WaveformImportAnalysis.Noise(t, y, from, to, origin, period, mean);

        var offset = (mean.Max() + mean.Min()) / 2.0;
        var amplitude = (mean.Max() - mean.Min()) / 2.0;
        var (u, v) = options.RemoveDc ? (offset, amplitude) : (0.0, Math.Max(Math.Abs(mean.Max()), Math.Abs(mean.Min())));
        var values = mean.Select(m => (m - u) / v).ToArray();
        var phases = Enumerable.Range(0, n).Select(k => (k + 0.5) / n).ToArray();

        var (fineSum, fineCount) = WaveformImportAnalysis.Fold(t, y, from, to, origin, period, fine);
        var fold = WaveformImportAnalysis.Means(fineSum, fineCount);
        var edges = new List<WaveformImportEdge>();
        var breakpoints = new SortedSet<double>();

        // The thresholds scale with the grid: the noise of a bin mean, and a quarter of the edge's
        // own mean step per output sample between its 10/90 levels, so neither depends on N.
        var swing = values.Max() - values.Min();
        var binNoise = noise / v / Math.Sqrt((to - from) / (double)n);

        foreach (var (rising, leave, enter, middle) in WaveformImportAnalysis.Edges(fold, options.EdgeWidthCycles))
        {
            var (p50, pLeave, pEnter) = ((middle + 0.5) / fine, (leave + 0.5) / fine, (enter + 0.5) / fine);
            var wrap = Math.Floor(p50);
            edges.Add(new WaveformImportEdge(
                rising ? "rising" : "falling",
                p50 - wrap,
                (rising ? pLeave : pEnter) - wrap,
                (rising ? pEnter : pLeave) - wrap,
                pEnter - pLeave,
                (pEnter - pLeave) * period));
            var sign = rising ? 1.0 : -1.0;
            var first = (int)Math.Floor((pLeave * n) - 0.5);
            var last = (int)Math.Ceiling((pEnter * n) - 0.5);
            // The mean step per sample of a ramp the table resolves; a jump inside one or two bins
            // has no ramp step, and the walk then follows its partial bins to the plateaus.
            var width = (pEnter - pLeave) * n;
            var rampStep = width >= 1.0 ? 0.8 * swing / width : 0.0;
            var walk = Math.Max(Math.Max(3.0 * binNoise, 0.25 * rampStep), 1e-3 * swing);
            // A second difference carries sqrt(6) times the noise of a bin mean: 12 of it is 4.9 sigma.
            var kink = Math.Max(Math.Max(12.0 * binNoise, 0.25 * rampStep), 1e-3 * swing);

            // Outward from the 10/90 crossings while the table still moves the edge's way over the
            // next two samples, so one noisy step inside a ramp does not stop the walk: it stops
            // at the plateaus, where the linear table kinks.
            for (var walked = 0; walked < n && sign * (values[Mod(first, n)] - values[Mod(first - 2, n)]) > walk; walked++)
            {
                first--;
            }

            for (var walked = 0; walked < n && sign * (values[Mod(last + 2, n)] - values[Mod(last, n)]) > walk; walked++)
            {
                last++;
            }

            // Where the ramp holds four samples or more, its corners are where its straight line
            // meets the plateaus: a local step cannot place them to a sample once the noise of a
            // bin mean nears the step per sample (a 5 % ramp at N = 2048 and 1 % noise).
            (first, last) = WaveformImportAnalysis.Corners(values, first, last, (pLeave * n) - 0.5, (pEnter * n) - 0.5);

            // The two plateau samples, and every sample between them where the slope turns.
            breakpoints.Add(phases[Mod(first, n)]);
            breakpoints.Add(phases[Mod(last, n)]);

            for (var k = first + 1; k < last; k++)
            {
                var turn = values[Mod(k + 1, n)] - (2.0 * values[Mod(k, n)]) + values[Mod(k - 1, n)];

                if (Math.Abs(turn) > kink)
                {
                    breakpoints.Add(phases[Mod(k, n)]);
                }
            }
        }

        HarmonicFit fit;

        try
        {
            fit = RfWaveform.Harmonic.Fit(phases, values, options.Orders);
        }
        catch (ArgumentException ex)
        {
            throw Refusal(path, string.Create(invariant, $"the {n} samples do not determine {options.Orders} orders: {ex.Message}"), "pass fewer --orders");
        }

        var terms = fit.Waveform.Terms;
        // A fundamental below a thousandth of the largest order is taken as zero (the distortion
        // would be over a thousand): the amplitudes then stay absolute.
        var fundamental = terms[1].Amplitude > 1e-3 * terms.Max(h => h.Amplitude) ? terms[1].Amplitude : (double?)null;
        var harmonics = terms.Select(h => new WaveformImportHarmonic(h.Order, h.Amplitude / (fundamental ?? 1.0), h.Phase)).ToArray();

        var body = new StringBuilder("phase,value\n");

        for (var k = 0; k < n; k++)
        {
            body.Append(CultureInfo.InvariantCulture, $"{phases[k]:R},{values[k]:R}\n");
        }

        var written = body.ToString();

        // The table must read back as a model reads it; a failure here is this code's, not the trace's.
        try
        {
            _ = WaveformTable.Parse(written, outPath);
        }
        catch (EinzelException ex)
        {
            throw new InvalidOperationException($"the importer wrote a table its reader refuses: {ex.Message}", ex);
        }

        try
        {
            File.WriteAllText(table, written);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw PathRefusal(outPath, $"the table '{outPath}' could not be written: {ex.Message}", "check that its directory is writable");
        }

        var interpolation = options.Interpolation
            ?? (edges.Count > 0 ? WaveformInterpolation.Linear : WaveformInterpolation.Cubic);
        var waveform = new WaveformDocument
        {
            Type = "tabulated",
            File = Path.GetRelativePath(Directory.GetCurrentDirectory(), table).Replace('\\', '/'),
            Interpolation = interpolation == WaveformInterpolation.Linear ? "linear" : "cubic",
            Breakpoints = breakpoints.Count > 0 ? [.. breakpoints] : null,
        };

        return new WaveformImportOutcome(
            Path.GetFullPath(path),
            trace.PreambleLines,
            t.Count,
            (t[^1] - t[0]) / (t.Count - 1),
            nominal,
            period,
            1.0 / period,
            (period - nominal) / nominal * 1e6,
            periods,
            perPeriod,
            options.Trigger,
            origin,
            offset,
            amplitude,
            mean.Average(),
            values.Average(),
            options.RemoveDc ? "remove-dc: (value - midline) / amplitude" : "peak: value / largest |value|",
            u,
            v,
            WaveformImportAnalysis.Duty(fold),
            edges,
            edges.Count > 0 ? edges.Min(e => e.Time10To90Seconds) : null,
            period / 20.0,
            breakpoints.Count > 0,
            noise,
            noise / amplitude,
            harmonics,
            Math.Sqrt(terms.Skip(2).Sum(h => h.Amplitude * h.Amplitude)) / fundamental,
            table,
            n,
            waveform);
    }

    /// <summary>The level of a <c>level=&lt;v&gt;</c> trigger, null for the midline.</summary>
    private static double? Level(string trigger, string path)
    {
        if (trigger is "rising" or "falling")
        {
            return null;
        }

        if (!(trigger.StartsWith("level=", StringComparison.Ordinal)
            && double.TryParse(trigger.AsSpan(6), NumberStyles.Float, CultureInfo.InvariantCulture, out var level)
            && double.IsFinite(level)))
        {
            throw Refusal(path, $"the trigger '{trigger}' is not rising, falling or level=<v>", "pass --trigger rising|falling|level=<v>");
        }

        return level;
    }

    private static int Mod(int k, int n) => ((k % n) + n) % n;

    private static void Require(string path, bool condition, string constraint, string suggestion)
    {
        if (!condition)
        {
            throw Refusal(path, constraint, suggestion);
        }
    }

    private static void RequirePath(string path, bool condition, string constraint, string suggestion)
    {
        if (!condition)
        {
            throw PathRefusal(path, constraint, suggestion);
        }
    }

    private static EinzelException PathRefusal(string path, string constraint, string suggestion) =>
        new(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = path,
            Constraint = constraint,
            Observed = new ObservedValue(0.0, path),
            Suggestion = suggestion,
        });

    private static EinzelException Refusal(string path, string constraint, string suggestion) =>
        new(new EinzelError
        {
            Code = ErrorCodes.ValueOutOfBounds,
            Path = path,
            Constraint = constraint,
            Suggestion = suggestion,
        });
}
