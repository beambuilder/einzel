using Einzel.Cli;
using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Einzel.Io;
using Einzel.Project;

namespace Einzel.Cli.Tests;

/// <summary>
/// The solve key at the surfaces that carry it: <c>einzel solve</c> and the run manifest.
/// </summary>
/// <remarks>
/// The arithmetic of the key is <c>SolveHashTests</c>' subject; these check the promise made
/// to a reader of a model. A comment, a rename and a reformat change the model hash and leave
/// the solve key alone; a tenth of a micron on one electrode moves it; and a run records the
/// same key <c>solve</c> prints, so a manifest can be matched to a cached field.
/// </remarks>
public sealed class SolveKeySurfaceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private const string Model = """
    {
      "schemaVersion": "0.8",
      "name": "two bars",
      "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [0, 0, 0], "unit": "mm" },
        "direction": { "value": [1, 0, 0] },
        "accelerationPotential": { "value": 1, "unit": "kV" }
      },
      "fields": [ { "type": "solved2d", "solve": {
        "minX": { "value": -5, "unit": "mm" }, "maxX": { "value": 65, "unit": "mm" },
        "minY": { "value": -15, "unit": "mm" }, "maxY": { "value": 15, "unit": "mm" },
        "cellSize": { "value": 0.5, "unit": "mm" },
        "electrodes": [
          { "name": "backA", "shape": "rectangle",
            "minX": { "value": 40, "unit": "mm" }, "maxX": { "value": 60, "unit": "mm" },
            "minY": { "value": 6, "unit": "mm" }, "maxY": { "value": 10, "unit": "mm" },
            "potential": { "value": 200, "unit": "V" } },
          { "name": "backB", "shape": "rectangle",
            "minX": { "value": 40, "unit": "mm" }, "maxX": { "value": 60, "unit": "mm" },
            "minY": { "value": -10, "unit": "mm" }, "maxY": { "value": -6, "unit": "mm" },
            "potential": { "value": 200, "unit": "V" } }
        ] } } ],
      "detector": {
        "planePoint": { "value": [30, 0, 0], "unit": "mm" },
        "normal": { "value": [-1, 0, 0] }
      },
      "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 50, "unit": "us" } }
    }
    """;

    private string Write(string name, string text)
    {
        var directory = Path.Combine(_root, "models");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static (int ExitCode, string Stdout) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(new StringWriter());
            return (Program.Main(args), stdout.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    /// <summary>
    /// What the plan's acceptance names: a comment edit keeps the key. A rename and a
    /// reformat are added because they are the same kind of edit - text the solver never
    /// reads - and each moves the model hash, which is the control.
    /// </summary>
    [Fact]
    public void ACommentARenameAndAReformatKeepTheSolveKey()
    {
        var edited = Model
            .Replace("\"fields\": [", "// the plates are the whole lens\n  \"fields\":   [", StringComparison.Ordinal)
            .Replace("\"backA\"", "\"upperPlate\"", StringComparison.Ordinal)
            .Replace("\"name\": \"two bars\"", "\"name\": \"two plates\"", StringComparison.Ordinal);

        var before = SolveCommand.Execute(Write("before.json", Model));
        var after = SolveCommand.Execute(Write("after.json", edited));

        Assert.NotEqual(before.ModelHash, after.ModelHash);

        var a = Assert.Single(before.Elements);
        var b = Assert.Single(after.Elements);
        Assert.StartsWith("sha256:", a.GeometryHash, StringComparison.Ordinal);
        Assert.Equal(a.GeometryHash, b.GeometryHash);
        Assert.Equal(a.ChannelHash, b.ChannelHash);
    }

    /// <summary>A tenth of a micron on one electrode face is a different solve.</summary>
    [Fact]
    public void ATenthOfAMicronOnOneElectrodeMovesTheGeometryHash()
    {
        var moved = Model.Replace(
            "\"minY\": { \"value\": 6, \"unit\": \"mm\" }",
            "\"minY\": { \"value\": 6.0001, \"unit\": \"mm\" }",
            StringComparison.Ordinal);

        Assert.NotEqual(Model, moved);

        var before = Assert.Single(SolveCommand.Execute(Write("before.json", Model)).Elements);
        var after = Assert.Single(SolveCommand.Execute(Write("after.json", moved)).Elements);

        Assert.NotEqual(before.GeometryHash, after.GeometryHash);
        Assert.NotEqual(before.ChannelHash, after.ChannelHash);
    }

    /// <summary>A potential is the channel of a static solve, not its geometry.</summary>
    [Fact]
    public void AStaticPotentialMovesTheChannelAndNotTheGeometry()
    {
        var raised = Model.Replace(
            "\"value\": 200, \"unit\": \"V\" } },", "\"value\": 250, \"unit\": \"V\" } },", StringComparison.Ordinal);

        var before = Assert.Single(SolveCommand.Execute(Write("before.json", Model)).Elements);
        var after = Assert.Single(SolveCommand.Execute(Write("after.json", raised)).Elements);

        Assert.Equal(before.GeometryHash, after.GeometryHash);
        Assert.NotEqual(before.ChannelHash, after.ChannelHash);
    }

    /// <summary>
    /// <c>einzel solve</c> prints both hashes in full, and the solver-behaviour version a
    /// cache needs beside them.
    /// </summary>
    [Fact]
    public void SolvePrintsTheKeyAndTheSolverBehaviourVersion()
    {
        var path = Write("bars.json", Model);
        var element = Assert.Single(SolveCommand.Execute(path).Elements);

        var (exitCode, stdout) = Run("solve", path);

        Assert.Equal(0, exitCode);
        Assert.Contains($"geometry {element.GeometryHash}", stdout, StringComparison.Ordinal);
        Assert.Contains($"channel  {element.ChannelHash}", stdout, StringComparison.Ordinal);
        Assert.Contains(
            $"solver behaviour {EngineBuild.SolverBehaviourVersion}", stdout, StringComparison.Ordinal);
    }

    private const string AnalyticThenVolume = """
    {
      "schemaVersion": "0.14",
      "name": "drift then a driven box",
      "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [0, 0, -2], "unit": "mm" },
        "direction": { "value": [0, 0, 1] },
        "accelerationPotential": { "value": 100, "unit": "V" }
      },
      "fields": [
        { "type": "uniform", "field": { "value": [0, 0, 10], "unit": "V/m" } },
        { "type": "solved3d", "solve3d": {
          "drive": { "frequency": { "value": 1, "unit": "MHz" }, "waveform": "sinusoid" },
          "minX": { "value": -3, "unit": "mm" }, "maxX": { "value": 3, "unit": "mm" },
          "minY": { "value": -3, "unit": "mm" }, "maxY": { "value": 3, "unit": "mm" },
          "minZ": { "value": -3, "unit": "mm" }, "maxZ": { "value": 3, "unit": "mm" },
          "cellSize": { "value": 0.5, "unit": "mm" },
          "electrodes": [
            { "name": "box", "shape": "box",
              "minX": { "value": 1, "unit": "mm" }, "maxX": { "value": 2, "unit": "mm" },
              "minY": { "value": -1, "unit": "mm" }, "maxY": { "value": 1, "unit": "mm" },
              "minZ": { "value": -1, "unit": "mm" }, "maxZ": { "value": 1, "unit": "mm" },
              "driveAmplitude": { "value": 50, "unit": "V" },
              "potential": { "value": 10, "unit": "V" } },
            { "name": "guard", "shape": "box",
              "minX": { "value": -2, "unit": "mm" }, "maxX": { "value": -1, "unit": "mm" },
              "minY": { "value": -1, "unit": "mm" }, "maxY": { "value": 1, "unit": "mm" },
              "minZ": { "value": -1, "unit": "mm" }, "maxZ": { "value": 1, "unit": "mm" },
              "potential": { "value": 5, "unit": "V" } }
          ] } }
      ],
      "detector": { "planePoint": { "value": [0, 0, 2], "unit": "mm" }, "normal": { "value": [0, 0, -1] } },
      "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 20, "unit": "us" } }
    }
    """;

    /// <summary>
    /// The key worked out before any solve names the element by its place in the document
    /// and its dimension, and lists the channels <c>solve</c> then reports for it.
    /// </summary>
    /// <remarks>
    /// An analytic element first, so the key's index is the document's and not a count of
    /// solved elements (mutation: <c>Field = keys.Count</c> fails), and a volume, so the
    /// three-dimensional branch of <see cref="SolveHash.Of"/> is the one checked (mutation:
    /// recording it as two-dimensional fails).
    /// </remarks>
    [Fact]
    public void TheKeyBeforeTheSolveNamesTheElementAndTheChannelsSolveReports()
    {
        var path = Write("volume.json", AnalyticThenVolume);
        var validation = ModelValidator.Validate(ModelJson.Parse(AnalyticThenVolume));
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Code + " " + e.Constraint)));

        var key = Assert.Single(SolveHash.Of(validation.Model!));
        var solved = SolveCommand.Execute(path).Elements;

        Assert.Equal(1, key.Field);
        Assert.Equal(3, key.Dimensions);
        Assert.Equal(2, key.ChannelHashes.Count);
        Assert.All(solved, e => Assert.Equal((1, 3, key.GeometryHash), (e.Index, e.Dimensions, e.GeometryHash)));
        Assert.Equal(key.ChannelHashes, solved.Select(e => e.ChannelHash));
    }

    /// <summary>
    /// A run records the key <c>solve</c> reports, worked out before the solve; an analytic
    /// model records an empty list rather than nothing, so a reader can tell a model with
    /// nothing solved from a manifest written before keys were recorded.
    /// </summary>
    [Fact]
    public void ARunRecordsTheKeySolveReports()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var path = Write("bars.json", Model);
        var element = Assert.Single(SolveCommand.Execute(path).Elements);

        Assert.Equal(0, Run("run", path).ExitCode);

        var manifest = RunManifest.FromJson(
            File.ReadAllText(Path.Combine(_root, "results", "bars.manifest.json")))!;
        var key = Assert.Single(manifest.Solves!);

        Assert.Equal(0, key.Field);
        Assert.Equal(2, key.Dimensions);
        Assert.Equal(element.GeometryHash, key.GeometryHash);
        Assert.Equal([element.ChannelHash], key.ChannelHashes);

        Assert.Equal(0, Run("run", Path.Combine(_root, "models", "reflectron.json")).ExitCode);
        var analytic = RunManifest.FromJson(
            File.ReadAllText(Path.Combine(_root, "results", "reflectron.manifest.json")))!;
        Assert.NotNull(analytic.Solves);
        Assert.Empty(analytic.Solves);
    }
}
