using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Einzel.Cli;
using Einzel.Commands;
using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Io;
using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// <c>transport.edges</c> at the surface: the validator takes <c>cap</c>, <c>land</c> or
/// nothing and refuses anything else at its pointer, <c>einzel schema</c> lists both
/// values, the flag reaches the integrator through <c>run</c> and <c>fly</c> and every
/// other place that builds integration settings, and <c>fly</c> tells two models apart by it.
/// </summary>
public sealed partial class EdgeLandingSurfaceTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("einzel-edges-").FullName;

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary><c>cap</c>, <c>land</c> and an absent key validate; only <c>land</c> lands.</summary>
    /// <param name="edges">The value written, or null for no key.</param>
    /// <param name="lands">Whether the compiled model lands on edges.</param>
    [Theory]
    [InlineData(null, false)]
    [InlineData("cap", false)]
    [InlineData("land", true)]
    public void CapLandAndAbsentValidate(string? edges, bool lands)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(Model(edges)), null, _root);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Constraint)));
        Assert.Equal(lands, validation.Model!.LandOnEdges);
    }

    /// <summary>
    /// A misspelt or invented value is refused at <c>/transport/edges</c>, with a suggestion
    /// that names both values, rather than read as either.
    /// </summary>
    /// <param name="edges">The value written.</param>
    [Theory]
    [InlineData("Land")]
    [InlineData("on")]
    public void AnythingElseIsRefusedAtItsPointer(string edges)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(Model(edges)), null, _root);

        Assert.False(validation.IsValid);
        var error = Assert.Single(validation.Errors);
        Assert.Equal(ErrorCodes.SchemaInvalid, error.Code);
        Assert.Equal("/transport/edges", error.Path);
        Assert.Contains("\"cap\"", error.Suggestion, StringComparison.Ordinal);
        Assert.Contains("\"land\"", error.Suggestion, StringComparison.Ordinal);
    }

    /// <summary>A 0.15 document validates under this build to the model it was, at version 0.16.</summary>
    [Fact]
    public void AVersion015DocumentMeansWhatItMeant()
    {
        var old = ModelValidator.Validate(ModelJson.Parse(Model(null).Replace("\"0.16\"", "\"0.15\"", StringComparison.Ordinal)), null, _root);
        var current = ModelValidator.Validate(ModelJson.Parse(Model(null)), null, _root);

        Assert.True(old.IsValid && current.IsValid);
        Assert.Equal("0.16", ModelSchema.CurrentVersion);
        Assert.Contains("0.15", ModelSchema.SupportedVersions);
        Assert.False(old.Model!.LandOnEdges);
        Assert.Equal(
            old.Model.ToString().Replace("SchemaVersion = 0.15", "SchemaVersion = 0.16", StringComparison.Ordinal),
            current.Model!.ToString());
    }

    /// <summary><c>einzel schema</c> lists <c>cap</c> and <c>land</c> for <c>transport.edges</c>.</summary>
    [Fact]
    public void TheSchemaListsBothValues()
    {
        var (exitCode, stdout, _) = Run("schema");
        Assert.Equal(0, exitCode);

        var edges = JsonNode.Parse(stdout)!["$defs"]!["TransportDocument"]!["properties"]!["edges"]!;
        Assert.Equal(["cap", "land"], edges["enum"]!.AsArray().Select(v => (string)v!));
    }

    /// <summary>
    /// <c>fly</c>'s override check compares the integration settings a group would fly
    /// with, and the edge rule is one of them: a group that landed where the model caps would
    /// be a different flight.
    /// </summary>
    [Fact]
    public void FlyTellsACapModelFromALandModel()
    {
        var cap = ModelValidator.Validate(ModelJson.Parse(Model("cap")), null, _root).Model!;
        var land = ModelValidator.Validate(ModelJson.Parse(Model("land")), null, _root).Model!;
        var changed = typeof(FlyCommand).GetMethod("Changed", BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.Empty((List<string>)changed.Invoke(null, [cap, cap])!);
        Assert.Equal(["integration settings"], (List<string>)changed.Invoke(null, [cap, land])!);
    }

    /// <summary>
    /// The flag reaches the integrator through <c>run</c> and through <c>fly</c>: the same
    /// rectangular drive, flown under <c>land</c>, takes fewer steps in <c>run</c> and rejects
    /// fewer in <c>fly</c> than under <c>cap</c>.
    /// </summary>
    [Fact]
    public void RunAndFlyLandWhenTheModelSaysSo()
    {
        var steps = new Dictionary<string, (int Run, int Fly)>();

        foreach (var edges in new[] { "cap", "land" })
        {
            var model = Write($"models/{edges}.json", Model(edges));
            var run = Run("run", model, "--project", _root, "--json");
            Assert.True(run.ExitCode == 0, run.Stderr + run.Stdout);
            var runSteps = (int)JsonNode.Parse(run.Stdout)!["acceptedSteps"]!;

            var list = Write($"ions/{edges}.ions.json", """{ "schemaVersion": "0.1", "ions": [ { "id": 1 } ] }""");
            var fly = Run("fly", model, "--ions", list, "--project", _root, "--json");
            Assert.True(fly.ExitCode == 0, fly.Stderr + fly.Stdout);
            var flyRejected = (int)JsonNode.Parse(fly.Stdout)!["ions"]![0]!["rejected"]!;

            steps[edges] = (runSteps, flyRejected);
            output.WriteLine($"{edges}: run steps {runSteps}, fly rejected {flyRejected}");
        }

        // run reports accepted steps only; a landing flight takes fewer of them.
        Assert.True(steps["land"].Run < steps["cap"].Run, "run did not land");
        Assert.True(steps["land"].Fly < steps["cap"].Fly, "fly did not land");
    }

    /// <summary>
    /// Every place a command or renderer builds integration settings from a model carries the
    /// model's edge rule. The four studies, the sequenced run, the previews and the renderers
    /// each build their own; a site that forgot would fly <c>cap</c> under a <c>land</c>
    /// document with nothing to show for it, so the sites are read rather than trusted.
    /// </summary>
    [Fact]
    public void EverySettingsSiteCarriesTheEdgeRule()
    {
        var src = Path.Combine(RepositoryRoot(), "src");
        var sites = 0;

        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.Combine("Einzel.Transport", "Integration"), StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);

            foreach (Match site in SettingsSite().Matches(text))
            {
                // MirrorPair's is a library default, not built from a model.
                if (site.Value.Contains("model", StringComparison.Ordinal) || site.Value.Contains("coarse", StringComparison.Ordinal))
                {
                    sites++;
                    Assert.True(
                        site.Value.Contains("LandOnEdges =", StringComparison.Ordinal),
                        $"{Path.GetRelativePath(src, file)}: {site.Value}");
                }
            }
        }

        Assert.Equal(11, sites);
    }

    [GeneratedRegex(@"new (?:Transport\.Integration\.)?IntegrationSettings\s*\{[^}]*\}")]
    private static partial Regex SettingsSite();

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Einzel.Commands", "Examples")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("repository root not found");
    }

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
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

    // A rod driven by a 1 MHz square wave beside a 10 V ion's path: ten periods, twenty edges.
    private static string Model(string? edges) => $$"""
        {
          "schemaVersion": "0.16",
          "name": "square rod",
          "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [-8, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 10, "unit": "V" }
          },
          "fields": [
            { "type": "solved2d", "solve": {
                "drive": { "frequency": { "value": 1, "unit": "MHz" }, "waveform": { "type": "rectangular", "dutyCycle": 0.5 } },
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
          "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 10, "unit": "us" }{{(edges is null ? "" : $", \"edges\": \"{edges}\"")}} }
        }
        """;
}
