using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Einzel.Cli;
using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Io;
using Einzel.Library;
using Einzel.Project;

namespace Einzel.Cli.Tests;

/// <summary>
/// The waveform object at the surface: a table is an input the manifest records and
/// <c>verify</c> re-checks, <c>einzel schema</c> spells both forms, and every shipped
/// document means what it meant before the object form existed.
/// </summary>
public sealed class WaveformSurfaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "einzel-waveform", Guid.NewGuid().ToString("N"));

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// A tabulated drive's file is read beside the model, its SHA-256 lands in the
    /// manifest's <c>inputHashes</c> under the drive's pointer, and <c>verify</c> flags the
    /// run stale when the table changes and current again when it is restored.
    /// </summary>
    [Fact]
    public void ATablesHashIsRecordedAndVerifyNoticesWhenItChanges()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var models = Path.Combine(_root, "models");
        var table = Path.Combine(models, "tables", "drive.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(table)!);
        File.WriteAllText(table, string.Join('\n', Enumerable.Range(0, 16).Select(k =>
            string.Create(CultureInfo.InvariantCulture, $"{k / 16.0},{Math.Cos(2 * Math.PI * k / 16.0)}"))));
        var model = Path.Combine(models, "tabulated.json");
        File.WriteAllText(model, Model("""{ "type": "tabulated", "file": "tables/drive.csv", "interpolation": "cubic" }"""));

        var run = Run("run", model, "--json");
        Assert.True(run.ExitCode == 0, run.Stderr + run.Stdout);

        var manifestPath = Directory.GetFiles(Path.Combine(_root, "results"), "*.manifest.json").Single();
        var manifest = RunManifest.FromJson(File.ReadAllText(manifestPath))!;
        var recorded = Assert.Single(manifest.InputHashes!);
        Assert.Equal("models/tables/drive.csv", recorded.Key);
        Assert.Equal(ContentHash.OfFile(table), recorded.Value);
        Assert.True(VerifyCommand.Execute(_root).AllCurrent);

        var original = File.ReadAllText(table);
        File.AppendAllText(table, "\n0.99,0.9\n");
        Assert.False(VerifyCommand.Execute(_root).AllCurrent);
        File.WriteAllText(table, original);
        Assert.True(VerifyCommand.Execute(_root).AllCurrent);
    }

    /// <summary>
    /// Every tabulated drive is an input, under the pointer of the drive that names it: in a
    /// plane's <c>drives</c> list and in a volume's single <c>drive</c>; other shapes are not.
    /// </summary>
    /// <remarks>
    /// Read through reflection because the reference list is private to <c>RunInputs</c>; its
    /// pointer is what a changed-input refusal reports, and nothing public prints it.
    /// </remarks>
    [Fact]
    public void EveryTableIsAnInputUnderItsDrivesPointer()
    {
        static DriveDocument Table(string file) =>
            new() { Name = file, Waveform = new WaveformDocument { Type = "tabulated", File = file } };

        var document = new ModelDocument
        {
            Fields =
            [
                new FieldDocument
                {
                    Type = "solved2d",
                    Solve = new SolvedFieldDocument
                    {
                        Drives = [new DriveDocument { Name = "main", Waveform = new WaveformDocument { Type = "analytic", Expression = "1" } }, Table("a.csv")],
                    },
                },
                new FieldDocument { Type = "solved3d", Solve3d = new SolvedField3DDocument { Drive = Table("b.csv") } },
            ],
        };

        var references = (IEnumerable<(string Path, string Pointer)>)typeof(VerifyCommand).Assembly
            .GetType("Einzel.Commands.RunInputs")!
            .GetMethod("References", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [document])!;

        Assert.Equal(
            [("a.csv", "/fields/0/solve/drives/1/waveform/file"), ("b.csv", "/fields/1/solve3d/drive/waveform/file")],
            references.ToList());
    }

    /// <summary><c>einzel schema</c> describes <c>waveform</c> as a name or an object, with the object's keys and types.</summary>
    [Fact]
    public void TheSchemaSpellsTheWaveformAsANameOrAnObject()
    {
        var (exitCode, stdout, _) = Run("schema");
        Assert.Equal(0, exitCode);

        var schema = JsonNode.Parse(stdout)!;
        var defs = schema["$defs"]!;
        var waveform = defs["DriveDocument"]!["properties"]!["waveform"]!["anyOf"]!.AsArray();

        Assert.Equal(["sinusoid", "rectangular"], waveform[0]!["enum"]!.AsArray().Select(v => (string)v!));
        Assert.Equal("#/$defs/WaveformDocument", (string)waveform[1]!["$ref"]!);

        var properties = defs["WaveformDocument"]!["properties"]!.AsObject();
        Assert.Equal(
            ["type", "expression", "file", "interpolation", "terms", "dutyCycle", "breakpoints"],
            properties.Select(p => p.Key));
        Assert.Equal(
            ["sinusoid", "rectangular", "analytic", "tabulated", "harmonic"],
            properties["type"]!["enum"]!.AsArray().Select(v => (string)v!));
        Assert.Equal(["linear", "cubic"], properties["interpolation"]!["enum"]!.AsArray().Select(v => (string)v!));
        Assert.Equal("0.15", (string)schema["x-schemaVersion"]!);
    }

    /// <summary>
    /// Every shipped example and template validates to the same compiled drives when its
    /// version is moved to 0.15 and each short-form waveform is rewritten as the object form
    /// (a rectangular wave's duty cycle moved inside). What may differ is exactly that: the
    /// <c>schemaVersion</c> string and the spelling of the waveform; the validity, the error
    /// list and every compiled drive may not.
    /// </summary>
    [Fact]
    public void EveryShippedDocumentMeansTheSameInEitherSpelling()
    {
        var documents = ExampleModels.Names.Select(n => ("example " + n, ExampleModels.Read(n), (string?)n))
            .Concat(DeviceTemplates.Names().Select(n => ("template " + n, DeviceTemplates.Read(n), (string?)null)));
        var rewritten = 0;

        foreach (var (name, text, example) in documents)
        {
            var directory = Path.Combine(_root, name.Replace(' ', '-'));
            Directory.CreateDirectory(directory);

            if (example is not null)
            {
                ExampleModels.WriteAssets(example, directory);
            }

            var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!;
            node["schemaVersion"] = "0.15";
            rewritten += Rewrite(node);

            var before = ModelValidator.Validate(ModelJson.Parse(text), null, directory);
            var after = ModelValidator.Validate(ModelJson.Parse(node.ToJsonString()), null, directory);

            Assert.True(before.IsValid == after.IsValid, name);
            Assert.Equal(
                before.Errors.Select(e => $"{e.Code} {e.Path} {e.Constraint}"),
                after.Errors.Select(e => $"{e.Code} {e.Path} {e.Constraint}"));

            if (before.IsValid)
            {
                Assert.Equal(Drives(before.Model!), Drives(after.Model!));
            }
        }

        // The corpus has drives to rewrite, or this test checks nothing.
        Assert.True(rewritten > 10, $"only {rewritten} waveforms rewritten");
    }

    /// <summary>Rewrites every drive's short-form waveform as the object form; returns how many.</summary>
    private static int Rewrite(JsonNode? node)
    {
        var count = 0;

        if (node is JsonObject obj)
        {
            var drives = (obj["drive"] is JsonObject one ? [one] : Array.Empty<JsonObject>())
                .Concat(obj["drives"] is JsonArray several ? several.OfType<JsonObject>() : []);

            foreach (var drive in drives)
            {
                if (drive["waveform"] is not JsonValue value || !value.TryGetValue<string>(out var shape))
                {
                    continue;
                }

                var replacement = new JsonObject { ["type"] = shape };

                if (drive["dutyCycle"] is { } duty)
                {
                    // Inside a rectangular object; a sinusoid's duty cycle was validated
                    // and ignored, and the object form refuses it.
                    drive.Remove("dutyCycle");

                    if (shape == "rectangular")
                    {
                        replacement["dutyCycle"] = duty;
                    }
                }

                drive["waveform"] = replacement;
                count++;
            }

            foreach (var child in obj.Select(p => p.Value).ToArray())
            {
                count += Rewrite(child);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array.ToArray())
            {
                count += Rewrite(child);
            }
        }

        return count;
    }

    private static List<CompiledDrive> Drives(CompiledModel model) =>
        [.. model.Fields.SelectMany(f => (f.Solve?.Drives ?? []).Concat(f.Solve3D?.Drives ?? []))];

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
          "name": "tabulated rod",
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
