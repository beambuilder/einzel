using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Einzel.Cli;
using Einzel.Core.Model;

namespace Einzel.Cli.Tests;

/// <summary>
/// <c>einzel waveform import</c>: a scope trace in, a <c>tabulated</c> period file and the
/// waveform object a model takes out, with the report of what was measured; every trace or
/// option the import cannot use refused at exit 1 with its code.
/// </summary>
/// <remarks>
/// The traces are generated here as in <c>WaveformImportCommandTests</c>: 1.1 MHz, 1 GS/s, 25
/// periods, 180 V about 7.5 V, seeded Gaussian noise of 1.8 V (1 %).
/// </remarks>
public sealed partial class WaveformImportSurfaceTests : IDisposable
{
    private const double Frequency = 1.1e6;
    private const double Period = 1.0 / Frequency;
    private const string FrequencyText = "1100000";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "einzel-waveform-import-cli", Guid.NewGuid().ToString("N"));

    /// <summary>Creates the scratch directory.</summary>
    public WaveformImportSurfaceTests() => Directory.CreateDirectory(_root);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// <c>--json</c> on a duty-0.3 rectangle with 2 ns edges is the whole outcome: the period near
    /// the truth, two edges, the duty, the model settings, and a linear <c>tabulated</c> object
    /// with breakpoints naming a table that exists and reads as a model reads it.
    /// </summary>
    [Fact]
    public void TheJsonOutcomeCarriesTheTableAndWhatWasMeasured()
    {
        var import = Import(Write("rect.csv", Rectangle, 1.8, 3), "--json");

        Assert.Equal(Period, (double)import["periodSeconds"]!, Period * 1e-5);
        Assert.Equal(Period, (double)import["periodNominalSeconds"]!, 0.0);
        Assert.InRange((int)import["periodsFolded"]!, 24, 25);
        Assert.Equal(0.3, (double)import["dutyCycle"]!, 2e-3);
        Assert.Equal(2, import["edges"]!.AsArray().Count);
        Assert.True((bool)import["landAdvised"]!);
        Assert.Equal(0.0, (double)import["modelU"]!, 0.0);
        Assert.Equal(187.5, (double)import["modelV"]!, 1.0);
        Assert.Equal(1.8, (double)import["noiseRms"]!, 0.1);
        Assert.Equal(16, import["harmonics"]!.AsArray().Count);
        Assert.NotNull(import["thd"]);
        Assert.Equal(256, (int)import["samples"]!);

        var waveform = import["waveform"]!;
        Assert.Equal("tabulated", (string)waveform["type"]!);
        Assert.Equal("linear", (string)waveform["interpolation"]!);
        Assert.NotEmpty(waveform["breakpoints"]!.AsArray());

        var table = (string)import["table"]!;
        Assert.Equal(Path.Combine(_root, "rect.period.csv"), table);
        Assert.Equal(256, WaveformTable.Parse(File.ReadAllText(table), table).Phases.Count);
    }

    /// <summary>
    /// Without <c>--json</c> the output is the waveform object, then one sentence per report
    /// item: the period, the origin, the levels with the model's <c>U</c> and <c>V</c> under
    /// <c>--remove-dc</c>, the duty, each edge, the sharpest edge with the landing advice, the
    /// noise, the harmonics, the distortion, the table written and where a model looks for it.
    /// </summary>
    [Fact]
    public void ThePlainOutputIsTheObjectAndOneLinePerMeasurement()
    {
        var run = Run("waveform", "import", Write("rect.csv", Rectangle, 1.8, 4), "--frequency", FrequencyText, "--remove-dc");
        Assert.True(run.ExitCode == 0, run.Stderr);
        Assert.Empty(run.Stderr);

        var lines = run.Stdout.TrimEnd('\n').Split('\n');
        var close = Array.IndexOf(lines, "}");
        var waveform = JsonNode.Parse(string.Join('\n', lines[..(close + 1)]))!;
        Assert.Equal("tabulated", (string)waveform["type"]!);
        Assert.NotEmpty(waveform["breakpoints"]!.AsArray());

        var report = lines[(close + 1)..];
        Assert.Collection(
            report,
            line => Assert.StartsWith("period: nominal 9.09091E-07 s, found ", line, StringComparison.Ordinal),
            line => Assert.StartsWith("origin: the rising trigger puts phase 0 at t0 = ", line, StringComparison.Ordinal),
            line =>
            {
                var settings = Settings().Match(line);
                Assert.True(settings.Success, line);
                Assert.Equal(7.5, double.Parse(settings.Groups[1].Value, CultureInfo.InvariantCulture), 0.5);
                Assert.Equal(180.0, double.Parse(settings.Groups[2].Value, CultureInfo.InvariantCulture), 1.0);
            },
            line => Assert.Equal(0.3, Leading(line, "duty cycle: "), 2e-3),
            line => Assert.StartsWith("edge: rising at phase ", line, StringComparison.Ordinal),
            line => Assert.Equal(0.3, Leading(line, "edge: falling at phase "), 2e-3),
            line => Assert.EndsWith("so add \"transport\": { \"edges\": \"land\" } to the model to land on them", line, StringComparison.Ordinal),
            line => Assert.Equal(1.8, Leading(line, "noise: "), 0.1),
            line => Assert.StartsWith("harmonics (amplitude relative to the fundamental, phase in cycles): 0: ", line, StringComparison.Ordinal),
            line => Assert.StartsWith("THD ", line, StringComparison.Ordinal),
            line => Assert.Equal($"table: 256 samples written to {Path.Combine(_root, "rect.period.csv")}", line),
            line => Assert.Contains("a model resolves it relative to the model file", line, StringComparison.Ordinal));
    }

    /// <summary>
    /// The written table beside a model, and the printed object pasted into its drive with the
    /// file named as the model sees it and <c>transport.edges: land</c>, validate and fly.
    /// </summary>
    /// <remarks>
    /// <c>run</c> flies the source ion for about four drive periods; the outcome has no landing
    /// counter, so this shows the model is accepted and flies under <c>land</c>, not how often it landed.
    /// </remarks>
    [Fact]
    public void ThePrintedObjectBesideTheTableValidatesAndFlies()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var models = Path.Combine(_root, "models");
        Directory.CreateDirectory(models);
        var table = Path.Combine(models, "rect.period.csv");
        var run = Run("waveform", "import", Write("rect.csv", Rectangle, 1.8, 5), "--frequency", FrequencyText, "--out", table);
        Assert.True(run.ExitCode == 0, run.Stderr);

        var lines = run.Stdout.Split('\n');
        var waveform = JsonNode.Parse(string.Join('\n', lines[..(Array.IndexOf(lines, "}") + 1)]))!;
        Assert.Equal(Path.GetRelativePath(Directory.GetCurrentDirectory(), table).Replace('\\', '/'), (string)waveform["file"]!);
        waveform["file"] = "rect.period.csv";
        var model = Path.Combine(models, "imported.json");
        File.WriteAllText(model, Model(waveform.ToJsonString()));

        var validate = Run("validate", model, "--json");
        Assert.True(validate.ExitCode == 0, validate.Stderr + validate.Stdout);

        var flight = Run("run", model, "--json");
        Assert.True(flight.ExitCode == 0, flight.Stderr + flight.Stdout);
    }

    /// <summary>
    /// Every trace and option the import cannot use is refused at exit 1, with the usage line
    /// for a malformed call and the command's code otherwise; <c>--force</c> overwrites.
    /// </summary>
    /// <remarks>
    /// A missing <c>--frequency</c> and <c>--period</c>, or both, is the command's
    /// <c>VALUE_OUT_OF_BOUNDS</c> ("exactly one"), not the usage line: the call is well formed,
    /// and the refusal says what to pass.
    /// </remarks>
    [Fact]
    public void WhatCannotBeImportedIsRefusedAsEveryVerbRefuses()
    {
        var rect = Write("rect.csv", Rectangle, 0.0, 6);

        var gone = Path.Combine(_root, "absent.csv");
        Refused("SCHEMA_INVALID", gone, Run("waveform", "import", gone, "--frequency", FrequencyText));

        var single = Text("single.csv", string.Concat(Enumerable.Range(0, 2000).Select(i => string.Create(CultureInfo.InvariantCulture, $"{i * 1e-9:R}\n"))));
        Refused("SCHEMA_INVALID", "0 samples", Run("waveform", "import", single, "--frequency", FrequencyText));

        var rows = File.ReadAllLines(rect);
        rows[100] = "x,abc";
        Refused("SCHEMA_INVALID", "line 101", Run("waveform", "import", Text("bad.csv", string.Join('\n', rows)), "--frequency", FrequencyText));
        rows[100] = "1e-6";
        Refused("SCHEMA_INVALID", "line 101", Run("waveform", "import", Text("short-row.csv", string.Join('\n', rows)), "--frequency", FrequencyText));

        Refused("VALUE_OUT_OF_BOUNDS", "at least 2 are needed", Run("waveform", "import", Write("brief.csv", Rectangle, 0.0, 7, periods: 1), "--frequency", FrequencyText));
        Refused("VALUE_OUT_OF_BOUNDS", "no rising crossing", Run("waveform", "import", Write("flat.csv", _ => 0.0, 0.0, 8), "--frequency", FrequencyText));
        Refused("VALUE_OUT_OF_BOUNDS", "the trace repeats at", Run("waveform", "import", rect, "--frequency", "1155000"));
        Refused("VALUE_OUT_OF_BOUNDS", "fewer than 3", Run("waveform", "import", rect, "--frequency", FrequencyText, "--samples", "2"));
        Refused(
            "VALUE_OUT_OF_BOUNDS",
            "finer than the trace resolves",
            Run("waveform", "import", Write("coarse.csv", Rectangle, 0.0, 9, samplesPerPeriod: 100), "--frequency", FrequencyText, "--samples", "128"));
        Refused("VALUE_OUT_OF_BOUNDS", "exactly one of the frequency and the period", Run("waveform", "import", rect));
        Refused("VALUE_OUT_OF_BOUNDS", "exactly one of the frequency and the period", Run("waveform", "import", rect, "--frequency", FrequencyText, "--period", "1e-6"));

        Refused("usage: einzel waveform import <trace.csv>", null, Run("waveform", "import", "--frequency", FrequencyText));
        Refused("usage: einzel waveform import <trace.csv>", null, Run("waveform", "import", rect, "--frequency", "fast"));
        Refused("usage: einzel waveform import <trace.csv>", null, Run("waveform", "import", rect, "--frequency", FrequencyText, "--columns", "1"));
        Refused("usage: einzel waveform import <trace.csv>", null, Run("waveform", "import", rect, "--frequency", FrequencyText, "--interpolation", "spline"));

        var table = Text("taken.period.csv", "not a table\n");
        Refused("VALUE_OUT_OF_BOUNDS", "exists", Run("waveform", "import", rect, "--frequency", FrequencyText, "--out", table));
        Assert.Equal("not a table\n", File.ReadAllText(table));
        var forced = Run("waveform", "import", rect, "--frequency", FrequencyText, "--out", table, "--force");
        Assert.True(forced.ExitCode == 0, forced.Stderr);
        Assert.StartsWith("phase,value\n", File.ReadAllText(table), StringComparison.Ordinal);
    }

    /// <summary>
    /// A Tektronix-style five-column export, text in columns 1 to 3 beside the data and the
    /// time in nanoseconds, with <c>,</c> or <c>;</c> between fields, imports with
    /// <c>--columns 4,5 --time-unit ns</c> to the period the plain file gives.
    /// </summary>
    /// <param name="separator">The field separator.</param>
    [Theory]
    [InlineData(",")]
    [InlineData(";")]
    public void AFiveColumnExportInNanosecondsReadsWithItsColumnsAndUnit(string separator)
    {
        var plain = Write("rect.csv", Rectangle, 1.8, 10);
        var labels = new[] { "Record Length", "Sample Interval", "Trigger Point", "Source", "Vertical Units" };
        var tek = new StringBuilder();

        foreach (var (row, i) in File.ReadAllLines(plain).Skip(1).Select((row, i) => (row.Split(','), i)))
        {
            var time = double.Parse(row[0], CultureInfo.InvariantCulture) * 1e9;
            var label = i < labels.Length ? labels[i] + separator + "1" : separator;
            tek.Append(CultureInfo.InvariantCulture, $"{label}{separator}{separator}{time:R}{separator}{row[1]}\n");
        }

        var expected = (double)Import(plain, "--json")["periodSeconds"]!;
        var run = Run("waveform", "import", Text("tek.csv", tek.ToString()), "--columns", "4,5", "--time-unit", "ns", "--frequency", FrequencyText, "--json");
        Assert.True(run.ExitCode == 0, run.Stderr);
        Assert.Equal(expected, (double)JsonNode.Parse(run.Stdout)!["periodSeconds"]!, expected * 1e-6);
    }

    /// <summary>
    /// <c>--interpolation cubic</c> overrides the linear default of an edged trace and keeps its
    /// breakpoints; a sinusoid defaults to cubic with none, and says so. Without
    /// <c>--remove-dc</c> its settings line gives <c>V</c> as the larger extreme, 7.5 + 180 V
    /// here, and <c>U</c> as 0.
    /// </summary>
    [Fact]
    public void TheInterpolationFollowsTheEdgesUnlessAsked()
    {
        var cubic = Import(Write("rect.csv", Rectangle, 1.8, 11), "--interpolation", "cubic", "--json")["waveform"]!;
        Assert.Equal("cubic", (string)cubic["interpolation"]!);
        Assert.NotEmpty(cubic["breakpoints"]!.AsArray());

        var run = Run("waveform", "import", Write("sine.csv", p => Math.Sin(2.0 * Math.PI * p), 1.8, 12), "--frequency", FrequencyText);
        Assert.True(run.ExitCode == 0, run.Stderr);
        Assert.Contains("\"interpolation\": \"cubic\"", run.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("\"breakpoints\"", run.Stdout, StringComparison.Ordinal);
        Assert.Contains("\nno edge found; cubic table, no breakpoints\n", run.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("\nedge: ", run.Stdout, StringComparison.Ordinal);

        var peak = PeakSettings().Match(run.Stdout);
        Assert.True(peak.Success, run.Stdout);
        Assert.Equal(187.5, double.Parse(peak.Groups[1].Value, CultureInfo.InvariantCulture), 1.0);
    }

    /// <summary>
    /// A trace with no fundamental, a pure second harmonic at the nominal, has no distortion
    /// to report: the THD line says so and that the harmonic amplitudes are absolute.
    /// </summary>
    [Fact]
    public void ATraceWithoutAFundamentalHasNoDistortion()
    {
        var run = Run("waveform", "import", Write("second.csv", p => Math.Cos(4.0 * Math.PI * p), 0.0, 14), "--frequency", FrequencyText, "--no-refine");
        Assert.True(run.ExitCode == 0, run.Stderr);
        Assert.Contains("\nTHD undefined: no fundamental (the amplitudes above are absolute)\n", run.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("\nTHD ", run.Stdout.Replace("\nTHD undefined", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--no-refine</c> keeps the nominal to the bit, a refined fold does not, and the
    /// nominal given as <c>--period</c> is the same nominal as <c>--frequency</c>.
    /// </summary>
    [Fact]
    public void NoRefineKeepsTheNominal()
    {
        var trace = Write("rect.csv", Rectangle, 1.8, 13);

        var kept = Import(trace, "--no-refine", "--json");
        Assert.Equal(Period, (double)kept["periodSeconds"]!);
        Assert.Equal(0.0, (double)kept["periodDifferencePpm"]!);
        Assert.NotEqual(Period, (double)Import(trace, "--json")["periodSeconds"]!);

        var run = Run("waveform", "import", trace, "--period", Period.ToString("R", CultureInfo.InvariantCulture), "--no-refine", "--force", "--json");
        Assert.True(run.ExitCode == 0, run.Stderr);
        Assert.Equal(Period, (double)JsonNode.Parse(run.Stdout)!["periodSeconds"]!);
    }

    [GeneratedRegex(@"with --remove-dc: set U = (\S+), V = (\S+) in trace units")]
    private static partial Regex Settings();

    [GeneratedRegex(@"without --remove-dc: set V = (\S+) in trace units, U = 0 ")]
    private static partial Regex PeakSettings();

    /// <summary>A duty-0.3 rectangle with 2 ns 0-100 % edges, the rising one at phase 0.</summary>
    private static double Rectangle(double p)
    {
        const double ramp = 2e-9 * Frequency;
        const double duty = 0.3;
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
    }

    /// <summary>The number that follows <paramref name="prefix"/> at the start of <paramref name="line"/>.</summary>
    private static double Leading(string line, string prefix)
    {
        Assert.StartsWith(prefix, line, StringComparison.Ordinal);
        return double.Parse(line[prefix.Length..].Split(' ')[0], CultureInfo.InvariantCulture);
    }

    private static void Refused(string code, string? detail, (int ExitCode, string Stdout, string Stderr) run)
    {
        Assert.True(run.ExitCode == 1, $"exit {run.ExitCode}: {run.Stderr}{run.Stdout}");
        Assert.Contains(code, run.Stderr, StringComparison.Ordinal);
        Assert.Empty(run.Stdout);

        if (detail is not null)
        {
            Assert.Contains(detail, run.Stderr, StringComparison.Ordinal);
        }
    }

    private static JsonNode Import(string trace, params string[] extra)
    {
        var run = Run(["waveform", "import", trace, "--frequency", FrequencyText, "--force", .. extra]);
        Assert.True(run.ExitCode == 0, run.Stderr);
        return JsonNode.Parse(run.Stdout)!;
    }

    private string Text(string name, string text)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>A trace of <paramref name="shape"/> from a seeded random origin, 180 V about 7.5 V.</summary>
    private string Write(string name, Func<double, double> shape, double noise, int seed, int periods = 25, double? samplesPerPeriod = null)
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
            var gauss = Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble())) * Math.Cos(2.0 * Math.PI * random.NextDouble());
            text.Append(CultureInfo.InvariantCulture, $"{t:R},{7.5 + (180.0 * shape(p - Math.Floor(p))) + (noise * gauss):R}\n");
        }

        return Text(name, text.ToString());
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            return (Program.Main(args), stdout.ToString().ReplaceLineEndings("\n"), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    private static string Model(string waveform) => $$"""
        {
          "schemaVersion": "0.16",
          "name": "imported rod",
          "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [-8, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 10, "unit": "V" }
          },
          "fields": [
            { "type": "solved2d", "solve": {
                "drive": { "frequency": { "value": 1.1, "unit": "MHz" }, "waveform": {{waveform}} },
                "minX": { "value": -10, "unit": "mm" }, "maxX": { "value": 10, "unit": "mm" },
                "minY": { "value": -10, "unit": "mm" }, "maxY": { "value": 10, "unit": "mm" },
                "cellSize": { "value": 1, "unit": "mm" },
                "electrodes": [
                  { "name": "rod", "shape": "disc",
                    "centreX": { "value": 0, "unit": "mm" }, "centreY": { "value": -6, "unit": "mm" },
                    "radius": { "value": 1, "unit": "mm" },
                    "potential": { "value": 0, "unit": "V" }, "driveAmplitude": { "value": 5, "unit": "V" } }
                ] } }
          ],
          "detector": { "planePoint": { "value": [8, 0, 0], "unit": "mm" }, "normal": { "value": [-1, 0, 0] } },
          "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 10, "unit": "us" }, "edges": "land" }
        }
        """;
}
