using System.Globalization;
using System.Text.Json.Nodes;
using Einzel.Cli;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Io;

namespace Einzel.Cli.Tests;

/// <summary>
/// <c>einzel waveform fit</c>: a sampled period in, the harmonic waveform object a model
/// accepts out, with its residual; a malformed table or a missing order count refused as
/// every other verb refuses.
/// </summary>
public sealed class WaveformFitSurfaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "einzel-waveform-fit", Guid.NewGuid().ToString("N"));

    /// <summary>Creates the scratch directory.</summary>
    public WaveformFitSurfaceTests() => Directory.CreateDirectory(_root);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// A cosine sampled at 64 phases and fitted to five orders is one term of order 1,
    /// amplitude 1 and phase 0, every other order zero, residual below 1e-9.
    /// </summary>
    [Fact]
    public void ACosineTableIsOneTermOfOrderOne()
    {
        var table = Table("cos.csv", 64, p => Math.Cos(2.0 * Math.PI * p));
        var fit = Fit(table, 5);

        Assert.Equal(64, (int)fit["samples"]!);
        Assert.Equal(5, (int)fit["orders"]!);
        Assert.Equal("harmonic", (string)fit["waveform"]!["type"]!);

        foreach (var term in fit["waveform"]!["terms"]!.AsArray())
        {
            var amplitude = (double)term!["amplitude"]!;

            if ((int)term["order"]! == 1)
            {
                Assert.Equal(1.0, amplitude, 1e-9);
                Assert.Equal(0.0, (double)term["phase"]!, 1e-9);
            }
            else
            {
                Assert.True(Math.Abs(amplitude) < 1e-9, term.ToJsonString());
            }
        }

        Assert.True((double)fit["residualRms"]! < 1e-9);
    }

    /// <summary>
    /// A square wave at 1024 midpoints, fitted to seven orders, is the odd orders at
    /// <c>4 / (π n)</c> as sines, and nothing on the even ones.
    /// </summary>
    [Fact]
    public void ASquareWaveTableIsItsOddOrders()
    {
        var fit = Fit(Table("square.csv", 1024, p => p < 0.5 ? 1.0 : -1.0, 0.5), 7);

        foreach (var term in fit["waveform"]!["terms"]!.AsArray())
        {
            var order = (int)term!["order"]!;
            var amplitude = (double)term["amplitude"]!;

            if (order % 2 == 1)
            {
                Assert.Equal(4.0 / (Math.PI * order), amplitude, 1e-3);
                Assert.Equal(-0.25, (double)term["phase"]!, 1e-9);
            }
            else
            {
                Assert.True(Math.Abs(amplitude) < 1e-9, term.ToJsonString());
            }
        }
    }

    /// <summary>
    /// A malformed table is refused with the table reader's own error, naming the line, and
    /// exit code 1; so are a missing <c>--orders</c> (the usage line) and more orders than
    /// the samples determine.
    /// </summary>
    [Fact]
    public void WhatCannotBeFittedIsRefusedAsEveryVerbRefuses()
    {
        var bad = Path.Combine(_root, "bad.csv");
        File.WriteAllText(bad, "phase,value\n0,1\n0.5,abc\n0.7,0\n");
        var malformed = Run("waveform", "fit", bad, "--orders", "1", "--json");
        Assert.Equal(1, malformed.ExitCode);
        Assert.Contains("SCHEMA_INVALID", malformed.Stderr, StringComparison.Ordinal);
        Assert.Contains("line 3", malformed.Stderr, StringComparison.Ordinal);
        Assert.Empty(malformed.Stdout);

        var table = Table("cos.csv", 8, p => Math.Cos(2.0 * Math.PI * p));
        var missing = Run("waveform", "fit", table, "--json");
        Assert.Equal(1, missing.ExitCode);
        Assert.Contains("usage: einzel waveform fit", missing.Stderr, StringComparison.Ordinal);

        var tooMany = Run("waveform", "fit", table, "--orders", "4");
        Assert.Equal(1, tooMany.ExitCode);
        Assert.Contains("VALUE_OUT_OF_BOUNDS", tooMany.Stderr, StringComparison.Ordinal);

        // 2N + 1 would overflow an int: refused, not wrapped into a small count.
        var absurd = Run("waveform", "fit", table, "--orders", "1073741824");
        Assert.Equal(1, absurd.ExitCode);
        Assert.Contains("VALUE_OUT_OF_BOUNDS", absurd.Stderr, StringComparison.Ordinal);
        Assert.Contains("orders 0 to 1073741824 are 2147483649 coefficients", absurd.Stderr, StringComparison.Ordinal);

        var gone = Path.Combine(_root, "absent.csv");
        var missingFile = Run("waveform", "fit", gone, "--orders", "1");
        Assert.Equal(1, missingFile.ExitCode);
        Assert.Contains("SCHEMA_INVALID", missingFile.Stderr, StringComparison.Ordinal);
        Assert.Contains(gone, missingFile.Stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// A valid table whose 100 samples cover only the first tenth of the period has enough
    /// samples for ten orders but cannot separate them: refused as <c>VALUE_OUT_OF_BOUNDS</c>
    /// with exit 1, not passed through as an internal error.
    /// </summary>
    [Fact]
    public void ATraceTooShortToSeparateTheOrdersIsRefused()
    {
        var path = Path.Combine(_root, "short.csv");
        File.WriteAllText(path, string.Concat(Enumerable.Range(0, 100).Select(i =>
            string.Create(CultureInfo.InvariantCulture, $"{i * 0.001:R},{Math.Cos(2.0 * Math.PI * i * 0.001):R}\n"))));

        var run = Run("waveform", "fit", path, "--orders", "10", "--json");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("VALUE_OUT_OF_BOUNDS", run.Stderr, StringComparison.Ordinal);
        Assert.Contains("do not separate 10 orders", run.Stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// Without <c>--json</c> the output is the waveform object and one residual line: the
    /// object parses, is a harmonic waveform, and validates inside a drive. A constant table
    /// prints no phase of -1/2 or -0.
    /// </summary>
    [Fact]
    public void ThePlainOutputIsTheObjectAModelTakes()
    {
        var run = Run("waveform", "fit", Table("triangle.csv", 64, p => 1.0 - (4.0 * Math.Abs(p - 0.5))), "--orders", "6");
        Assert.True(run.ExitCode == 0, run.Stderr);
        var lines = run.Stdout.TrimEnd('\n').Split('\n');
        Assert.StartsWith("orders 0 to 6 fitted to 64 samples: residual rms ", lines[^1], StringComparison.Ordinal);

        var printed = string.Join('\n', lines[..^1]);
        Assert.Equal("harmonic", (string)JsonNode.Parse(printed)!["type"]!);
        Assert.Equal(1, Driven(printed).ChannelCount);

        var constant = Run("waveform", "fit", Table("flat.csv", 16, _ => 0.5), "--orders", "7");
        Assert.True(constant.ExitCode == 0, constant.Stderr);
        Assert.DoesNotContain("\"phase\": -0.5", constant.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("\"phase\": -0\n", constant.Stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// The printed waveform object pasted into a driven model validates, and the field it
    /// builds weighs its channel, at every sample phase, as the tabulated original does to
    /// within the reported largest residual.
    /// </summary>
    /// <remarks>
    /// A triangle wave, so the 15-order fit is not exact and the residual is a real bound
    /// (about 2e-2, at the kinks); at the sample phases the table returns its samples, so the two fields
    /// may differ there by the residual and no more.
    /// </remarks>
    [Fact]
    public void ThePrintedObjectDrivesAModelAsTheTableDoesWithinTheResidual()
    {
        var table = Table("triangle.csv", 64, p => 1.0 - (4.0 * Math.Abs(p - 0.5)));
        var fit = Fit(table, 15);
        var residual = (double)fit["residualMax"]!;
        Assert.InRange(residual, 1e-3, 5e-2);

        var harmonic = Driven(fit["waveform"]!.ToJsonString());
        var tabulated = Driven($$"""{ "type": "tabulated", "file": "{{table.Replace('\\', '/')}}", "interpolation": "cubic" }""");
        var scale = Math.Abs(tabulated.WeightAt(0, 0.0));

        for (var i = 0; i < 64; i++)
        {
            var t = i / 64.0 * 1e-6;
            Assert.Equal(tabulated.WeightAt(0, t), harmonic.WeightAt(0, t), (residual * scale) + 1e-12);
        }
    }

    private string Table(string name, int count, Func<double, double> shape, double offset = 0.0)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "phase,value\n" + string.Concat(Enumerable.Range(0, count).Select(i =>
        {
            var p = (i + offset) / count;
            return string.Create(CultureInfo.InvariantCulture, $"{p:R},{shape(p):R}\n");
        })));
        return path;
    }

    private static JsonNode Fit(string table, int orders)
    {
        var run = Run("waveform", "fit", table, "--orders", orders.ToString(CultureInfo.InvariantCulture), "--json");
        Assert.True(run.ExitCode == 0, run.Stderr);
        return JsonNode.Parse(run.Stdout)!;
    }

    private static DrivenSolvedField Driven(string waveform)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(Model(waveform)));
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => $"{e.Code} {e.Path} {e.Constraint}")));
        var field = FieldAssembly.BuildReported(validation.Model!).Field;
        var driven = field as DrivenSolvedField
            ?? (field as SuperposedField)?.Elements.OfType<DrivenSolvedField>().FirstOrDefault();
        Assert.True(driven is not null, $"no driven field in {field.GetType().Name}");
        return driven;
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
            return (Program.Main(args), stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    private static string Model(string waveform) => $$"""
        {
          "schemaVersion": "0.15",
          "name": "fitted rod",
          "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [-8, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 10, "unit": "V" }
          },
          "fields": [
            { "type": "solved2d", "solve": {
                "drive": { "frequency": { "value": 1, "unit": "MHz" }, "waveform": {{waveform}} },
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
          "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 10, "unit": "us" } }
        }
        """;
}
