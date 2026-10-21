using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Einzel.Cli;
using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Einzel.Io;
using Einzel.Project;

namespace Einzel.Cli.Tests;

/// <summary>
/// The field cache where a person meets it: <c>einzel run</c>, its manifest, and the line it
/// prints.
/// </summary>
/// <remarks>
/// Every comparison is between runs made here, in one process: a cached result is checked
/// against the uncached one this machine produces, never against a committed number.
/// </remarks>
public sealed class FieldCacheSurfaceTests : IDisposable
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

    /// <summary>Two bars in a plane, from <c>SolveKeySurfaceTests</c>: one solved element of one channel.</summary>
    private const string Bars = """
    { "type": "solved2d", "solve": {
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
        ] } }
    """;

    /// <summary>
    /// The bars, launched at a declared energy that moves no electrode; a cloud on request.
    /// </summary>
    private static string Model(string cloud = "") => $$"""
    {
      "schemaVersion": "0.8",
      "name": "two bars",
      "parameters": { "energy": { "value": 1000, "unit": "V", "minimum": 500, "maximum": 2000 } },
      "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [0, 0, 0], "unit": "mm" },
        "direction": { "value": [1, 0, 0] },
        "accelerationPotential": { "expression": "energy", "unit": "V" }{{cloud}}
      },
      "fields": [ {{Bars}} ],
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

    private RunManifest Manifest(string stem = "bars") =>
        RunManifest.FromJson(File.ReadAllText(Path.Combine(_root, "results", $"{stem}.manifest.json")))!;

    /// <summary>The directory this build keeps its entries in.</summary>
    private string Entries() =>
        FieldCache.EntryDirectory(new ProjectLayout(_root), EngineBuild.SolverBehaviourVersion, EngineBuild.ComputePath);

    /// <summary>The stored result without the two things that differ by design: when, and how the field was had.</summary>
    private string Result()
    {
        var result = JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "results", "bars.result.json")))!;
        var manifest = result["manifest"]!.AsObject();

        Assert.True(manifest.Remove("createdUtc"));
        manifest.Remove("fieldCache");

        return result.ToJsonString();
    }

    /// <summary>
    /// A second run of an unchanged model loads its channel, says so, records <c>hit</c> with
    /// the payload hash the first run stored, and stores the same result as the first run and
    /// as a run without the cache, apart from its time.
    /// </summary>
    /// <remarks>
    /// Mutations: <c>RunCommand</c> not handing the cache to the build (nothing is recorded);
    /// the manifest site not recording it; the line not printed. Each fails here.
    /// </remarks>
    [Fact]
    public void ASecondRunLoadsWhatTheFirstSolvedAndStoresTheSameResult()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var path = Write("bars.json", Model());

        var first = Run("run", path);
        Assert.Equal(0, first.ExitCode);
        Assert.Contains("field: 0 channel(s) from the cache, 1 solved", first.Stderr, StringComparison.Ordinal);
        var stored = Assert.Single(Manifest().FieldCache!);
        var firstResult = Result();

        var second = Run("run", path);
        Assert.Equal(0, second.ExitCode);
        Assert.Contains("field: 1 channel(s) from the cache, 0 solved", second.Stderr, StringComparison.Ordinal);
        var hit = Assert.Single(Manifest().FieldCache!);
        var secondResult = Result();

        Assert.Equal(0, Run("run", path, "--no-cache").ExitCode);
        Assert.Null(Manifest().FieldCache);

        Assert.Equal((FieldCacheUse.Stored, FieldCacheUse.Hit), (stored.Status, hit.Status));
        Assert.Equal(stored with { Status = FieldCacheUse.Hit }, hit);
        Assert.Equal(Assert.Single(Assert.Single(Manifest().Solves!).ChannelHashes), hit.ChannelHash);
        Assert.Equal(firstResult, secondResult);
        Assert.Equal(firstResult, Result());
    }

    /// <summary>
    /// Every manifest a run writes records the cache beside the solve keys - four run paths,
    /// counted in the source the way <c>ResultDocumentTests</c> counts their result documents,
    /// because a path that recorded no cache use would let <c>verify</c> miss its drift.
    /// </summary>
    /// <remarks>Mutation: deleting the line from any one manifest site fails here.</remarks>
    [Fact]
    public void EveryRunManifestRecordsTheCacheBesideTheKeys()
    {
        var source = File.ReadAllText(Source("src", "Einzel.Commands", "RunCommand.cs"));

        var keys = source.Split("Solves = Fields.Solved.SolveHash.Of(model),").Length - 1;
        var cache = source.Split("            FieldCache = ").Length - 1;

        Assert.Equal(4, keys);
        Assert.Equal(keys, cache);
    }

    private static string Source(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"{Path.Combine(parts)} is not above the test binary");
    }

    /// <summary><c>--no-cache</c> neither reads nor writes: a run with it leaves no cache behind.</summary>
    /// <remarks>Mutation: <c>Program</c> ignoring the flag fails here.</remarks>
    [Fact]
    public void ARunWithNoCacheWritesNothing()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var path = Write("bars.json", Model());

        var run = Run("run", path, "--no-cache");

        Assert.Equal(0, run.ExitCode);
        Assert.False(Directory.Exists(new ProjectLayout(_root).FieldCache));
        Assert.DoesNotContain("from the cache", run.Stderr, StringComparison.Ordinal);
        Assert.Null(Manifest().FieldCache);
    }

    /// <summary>
    /// The figures of merit rebuild the field - a run of a cloud flies it again and measures
    /// the turn-around time - and with a cache in hand they load it rather than solving it
    /// again: the flight, the evaluator and the measurement each find the channel the first
    /// flight stored.
    /// </summary>
    /// <remarks>
    /// Mutation: <c>FiguresOfMerit.Setup</c> building without the store it was handed fails
    /// here; so does <c>FlyCloud</c> not passing it on.
    /// </remarks>
    [Fact]
    public void TheFiguresOfMeritRebuildTheFieldThroughTheCache()
    {
        var path = Write("cloud.json", Model("""
            ,
                "cloud": { "ions": 4, "seed": 1, "temperature": { "value": 300, "unit": "K" } }
            """));
        var model = ModelValidator.Validate(
            ModelJson.Parse(File.ReadAllText(path)), null, Path.GetDirectoryName(path)).Model!;
        var project = new ProjectLayout(_root);
        var keys = SolveHash.Of(model);

        IReadOnlyList<string> Statuses(FieldCache cache) => [.. cache.Uses(keys).Select(u => u.Status)];

        var flown = new FieldCache(project);
        _ = FiguresOfMerit.FlyCloud(model, store: flown);
        Assert.Equal([FieldCacheUse.Stored], Statuses(flown));

        var again = new FieldCache(project);
        _ = FiguresOfMerit.FlyCloud(model, store: again);
        Assert.Equal([FieldCacheUse.Hit], Statuses(again));

        var evaluated = new FieldCache(project);
        _ = FiguresOfMerit.Evaluator("turnAroundTime", store: evaluated)(model);
        Assert.Equal([FieldCacheUse.Hit], Statuses(evaluated));

        var measured = new FieldCache(project);
        _ = FiguresOfMerit.Measure("transmission", model, report: _ => { }, store: measured);
        Assert.Equal([FieldCacheUse.Hit], Statuses(measured));
    }

    /// <summary>
    /// The ensemble a cloud run reports is flown in the field the cache holds. An entry forged
    /// to a zero potential, with its record's SHA-256 made to match, is loaded by the run, and
    /// the run's arrival peak and turn-around time are the ones that field gives, not the ones
    /// the solved field gives.
    /// </summary>
    /// <remarks>
    /// The forged entry stands for any entry the run loads: the property is that the ensemble
    /// figures come from the same field as the rest of the run. Mutation: <c>RunCommand</c>'s
    /// <c>Ensemble</c> flying the cloud, or measuring the turn-around time, without the store
    /// solves the real field for it, and this fails.
    /// </remarks>
    [Fact]
    public void AForgedEntryReachesTheEnsemble()
    {
        var path = Write("cloud.json", Model("""
            ,
                "cloud": { "ions": 4, "seed": 1, "temperature": { "value": 300, "unit": "K" } }
            """));
        var model = ModelValidator.Validate(
            ModelJson.Parse(File.ReadAllText(path)), null, Path.GetDirectoryName(path)).Model!;
        var project = new ProjectLayout(_root);

        Assert.NotNull(RunCommand.Execute(path, project, false, DateTimeOffset.UtcNow, fieldCache: new FieldCache(project)).Run);

        var payload = Assert.Single(Directory.GetFiles(Entries(), "*.f64"));
        var zeros = new byte[new FileInfo(payload).Length];
        File.WriteAllBytes(payload, zeros);
        var record = JsonNode.Parse(File.ReadAllText(Path.ChangeExtension(payload, ".json")))!;
        record["payload"]!["sha256"] = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(zeros));
        File.WriteAllText(Path.ChangeExtension(payload, ".json"), record.ToJsonString());

        var ensemble = RunCommand.Execute(path, project, false, DateTimeOffset.UtcNow, fieldCache: new FieldCache(project)).Run!.Ensemble!;

        var forged = FiguresOfMerit.FlyCloud(model, store: new FieldCache(project)).Peak!;
        var forgedTurnAround = FiguresOfMerit.Evaluator("turnAroundTime", store: new FieldCache(project))(model)!.Value;
        var solved = FiguresOfMerit.FlyCloud(model).Peak!;
        var solvedTurnAround = FiguresOfMerit.Evaluator("turnAroundTime")(model)!.Value;

        Assert.NotEqual(solved.GaussianEquivalentFwhmSeconds, forged.GaussianEquivalentFwhmSeconds);
        Assert.NotEqual(solvedTurnAround, forgedTurnAround);
        Assert.Equal(forged.GaussianEquivalentFwhmSeconds * 1e9, ensemble.GaussianFwhmNs);
        Assert.Equal(forgedTurnAround * 1e9, ensemble.TurnAroundFwhmNs);
    }

    /// <summary>
    /// A sequenced run - its own manifest site - records the cache as the trajectory run does:
    /// <c>stored</c> the first time, <c>hit</c> the second.
    /// </summary>
    /// <remarks>
    /// The trap-then-extract sequence of <c>SequencedRunTests</c>, in the bars' field instead
    /// of none. Mutation: the sequenced manifest site not recording the cache fails here.
    /// </remarks>
    [Fact]
    public void ASequencedRunRecordsTheCacheInItsManifest()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var path = Write("sequenced.json", SequencedRunTests.Model
            .Replace("\"ions\": 200", "\"ions\": 20", StringComparison.Ordinal)
            .Replace("\"fields\": [{ \"type\": \"fieldFree\" }]", $"\"fields\": [ {Bars} ]", StringComparison.Ordinal));

        Assert.Equal(0, Run("run", path).ExitCode);
        Assert.Equal(FieldCacheUse.Stored, Assert.Single(Manifest("sequenced").FieldCache!).Status);

        Assert.Equal(0, Run("run", path).ExitCode);
        var manifest = Manifest("sequenced");
        Assert.Contains(" -> ", manifest.TransportMode, StringComparison.Ordinal);
        Assert.Equal(FieldCacheUse.Hit, Assert.Single(manifest.FieldCache!).Status);
    }

    /// <summary>
    /// The shell's results view runs the model as <c>einzel run</c> does, through the
    /// project's cache: its second run loads the field and its manifest says so.
    /// </summary>
    /// <remarks>Mutation: <c>ResultsCommand</c> running without the cache records nothing, and this fails.</remarks>
    [Fact]
    public void TheResultsViewRunsThroughTheCacheAsTheCliDoes()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var path = Write("bars.json", Model());

        _ = ResultsCommand.Execute(path);
        Assert.Equal(FieldCacheUse.Stored, Assert.Single(Manifest().FieldCache!).Status);

        _ = ResultsCommand.Execute(path);
        Assert.Equal(FieldCacheUse.Hit, Assert.Single(Manifest().FieldCache!).Status);
    }
}
