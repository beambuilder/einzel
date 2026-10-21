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

    /// <summary>When the one entry in the cache was written; it changes only when the channel is solved.</summary>
    private string Written()
    {
        var status = FieldCache.Status(new ProjectLayout(_root));
        Assert.Equal(1, status.Entries);

        var record = Directory.GetFiles(Entries(), "*.json");

        return JsonNode.Parse(File.ReadAllText(Assert.Single(record)))!["createdUtc"]!.GetValue<string>();
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

    /// <summary>
    /// <c>solve</c> keeps what it solves for the next run, and never loads: it exists to
    /// measure a solve. The run after it loads the channel; a second <c>solve</c> solves it
    /// again and writes it again.
    /// </summary>
    /// <remarks>
    /// Mutations: <c>SolveCommand</c> not handing the store to the builders (nothing is kept,
    /// and the run solves), and <c>Program</c> opening the cache to read (the second solve
    /// loads, and the entry is not written again). Each fails here.
    /// </remarks>
    [Fact]
    public void SolveKeepsWhatItSolvesAndNeverLoads()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var path = Write("bars.json", Model());

        Assert.Equal(0, Run("solve", path).ExitCode);
        var solved = Written();

        Assert.Equal(0, Run("run", path).ExitCode);
        Assert.Equal(FieldCacheUse.Hit, Assert.Single(Manifest().FieldCache!).Status);
        Assert.Equal(solved, Written());

        Assert.Equal(0, Run("solve", path).ExitCode);
        Assert.NotEqual(solved, Written());
    }

    /// <summary>
    /// A scan across a parameter that moves no electrode solves the field once: a second scan
    /// of it loads the channel at every point and writes nothing.
    /// </summary>
    /// <remarks>Mutation: <c>StudyCommand</c> not handing the store to its evaluator fails here.</remarks>
    [Fact]
    public void AScanThatMovesNoElectrodeLoadsTheFieldAtEveryPoint()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        Write("bars.json", Model());
        var study = Path.Combine(_root, "studies", "energy.json");
        File.WriteAllText(study, """
            {
              "name": "energy-scan",
              "model": "../models/bars.json",
              "figureOfMerit": "flightTime",
              "scan": { "parameter": "energy", "from": 900, "to": 1100, "unit": "V", "points": 3 }
            }
            """);

        Assert.Equal(0, Run("scan", study).ExitCode);
        var first = Written();

        Assert.Equal(0, Run("scan", study).ExitCode);
        Assert.Equal(first, Written());
    }

    /// <summary>
    /// <c>einzel test</c> flies its model once per expectation, and loads the field after the
    /// first: a second test run writes nothing to the cache.
    /// </summary>
    /// <remarks>Mutation: <c>TestCommand</c> not handing the store to the evaluator fails here.</remarks>
    [Fact]
    public void ProjectTestsLoadTheFieldTheirFirstFlightSolved()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        Write("bars.json", Model());

        foreach (var file in Directory.GetFiles(Path.Combine(_root, "tests")))
        {
            File.Delete(file);
        }

        File.WriteAllText(Path.Combine(_root, "tests", "bars.json"), """
            {
              "model": "../models/bars.json",
              "expect": [
                { "figureOfMerit": "flightTime", "value": 1.5433, "unit": "us", "tolerance": 1e-3 },
                { "figureOfMerit": "energyDrift", "value": 0, "unit": "1", "tolerance": 1 }
              ]
            }
            """);

        Assert.Equal(0, Run("test", _root).ExitCode);
        var first = Written();

        Assert.Equal(0, Run("test", _root).ExitCode);
        Assert.Equal(first, Written());
    }

    /// <summary>
    /// <c>verify</c> reads the cache's records against the manifest and never a payload: an
    /// entry gone is a note, since the next run solves it again; an entry now holding another
    /// field - here, another model's channel copied over it - is drift, since the next run
    /// would load it.
    /// </summary>
    /// <remarks>
    /// Mutations: skipping the check (no note, no drift), and calling a missing entry drift
    /// (the result goes stale on a regenerable deletion). Each fails here.
    /// </remarks>
    [Fact]
    public void VerifyNotesAMissingEntryAndCallsAnotherFieldDrift()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var path = Write("bars.json", Model());
        var other = Write("other.json", Model().Replace("\"value\": 200", "\"value\": 210", StringComparison.Ordinal));
        var project = new ProjectLayout(_root);

        Assert.Equal(0, Run("run", path).ExitCode);
        var clean = Assert.Single(VerifyCommand.Execute(_root).Results);
        Assert.True(clean.Current);
        Assert.DoesNotContain(clean.Notes, n => n.Contains("field cache", StringComparison.Ordinal));

        Directory.Delete(project.FieldCache, recursive: true);
        var missing = Assert.Single(VerifyCommand.Execute(_root).Results);
        Assert.True(missing.Current);
        Assert.Contains(missing.Notes, n => n.Contains("field 0 channel 0 is not in the field cache", StringComparison.Ordinal));

        Assert.Equal(0, Run("run", path).ExitCode);
        Assert.Equal(0, Run("run", other).ExitCode);

        var directory = Entries();
        var mine = FieldCache.Stem(Assert.Single(Manifest().FieldCache!).ChannelHash);
        var theirs = Directory.GetFiles(directory, "*.f64").Select(Path.GetFileNameWithoutExtension).Single(stem => stem != mine)!;

        foreach (var extension in new[] { ".f64", ".json" })
        {
            File.Copy(Path.Combine(directory, theirs + extension), Path.Combine(directory, mine + extension), overwrite: true);
        }

        var swapped = VerifyCommand.Execute(_root).Results.Single(r => r.Manifest.EndsWith("bars.manifest.json", StringComparison.Ordinal));
        Assert.False(swapped.Current);
        Assert.Contains(swapped.Drift, d => d.Contains("holds another field for field 0 channel 0", StringComparison.Ordinal));
        Assert.Equal(1, Run("verify", _root).ExitCode);
    }

    /// <summary>
    /// <c>cache status</c> counts what is kept; <c>prune</c> deletes a leftover temporary file
    /// and keeps the entry; <c>clear</c> deletes everything; and neither deletes anything
    /// under <c>--dry-run</c>, which names the same files.
    /// </summary>
    /// <remarks>
    /// Mutations: the dry-run flag ignored, and <c>prune</c> dispatched to <c>clear</c>. Each
    /// fails here; the help line removed fails <c>HelpCoversEveryVerbTests</c>.
    /// </remarks>
    [Fact]
    public void TheCacheVerbsSayWhatIsKeptAndDeleteOnlyWhenNotDry()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        Assert.Equal(0, Run("run", Write("bars.json", Model())).ExitCode);

        var project = new ProjectLayout(_root);
        var directory = Entries();
        var leftover = Path.Combine(directory, "interrupted.1234.tmp");
        File.WriteAllText(leftover, "partial");

        static JsonNode Json(string stdout) => JsonNode.Parse(stdout)!;

        var status = Json(Run("cache", "status", _root, "--json").Stdout);
        Assert.Equal((1, 1), (status["entries"]!.GetValue<int>(), status["temporary"]!.GetValue<int>()));

        var preview = Json(Run("cache", "prune", _root, "--json", "--dry-run").Stdout);
        Assert.Single(preview["removed"]!.AsArray());
        Assert.True(File.Exists(leftover));

        Assert.Single(Json(Run("cache", "prune", _root, "--json").Stdout)["removed"]!.AsArray());
        Assert.False(File.Exists(leftover));
        Assert.Equal(1, FieldCache.Status(project).Entries);

        Assert.Equal(2, Json(Run("cache", "clear", _root, "--json", "--dry-run").Stdout)["removed"]!.AsArray().Count);
        Assert.Equal(1, FieldCache.Status(project).Entries);

        Assert.Equal(0, Run("cache", "clear", _root).ExitCode);
        Assert.False(Directory.Exists(project.FieldCache));
        Assert.Equal(1, Run("cache", "drop", _root).ExitCode);
    }

    /// <summary>
    /// <c>cache</c> finds the project as <c>run</c> does, by walking up to the directory that
    /// holds <c>models/</c>: asked from inside <c>models/</c>, it sees the cache a run wrote.
    /// </summary>
    /// <remarks>Mutation: taking the directory as given, as the verb first did, finds no entry and fails here.</remarks>
    [Fact]
    public void TheCacheVerbFindsTheProjectFromInsideIt()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        Assert.Equal(0, Run("run", Write("bars.json", Model())).ExitCode);

        var status = JsonNode.Parse(Run("cache", "status", Path.Combine(_root, "models"), "--json").Stdout)!;
        Assert.Equal(1, status["entries"]!.GetValue<int>());
    }

    /// <summary>
    /// A cache record that parses with a part missing does not make <c>verify</c> fail: the
    /// entry is unreadable, which is the note for an entry gone, and the run after it solves.
    /// </summary>
    /// <remarks>
    /// Mutation: removing the null check in <c>FieldCacheEntry.Read</c> lets <c>verify</c>
    /// read the null payload, and the exception fails this.
    /// </remarks>
    [Fact]
    public void VerifyOfARecordWithAPartMissingIsANote()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        Assert.Equal(0, Run("run", Write("bars.json", Model())).ExitCode);

        var file = Assert.Single(Directory.GetFiles(Entries(), "*.json"));
        var record = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        record["payload"] = null;
        record["grid"] = null;
        File.WriteAllText(file, record.ToJsonString());

        var result = Assert.Single(VerifyCommand.Execute(_root).Results);
        Assert.True(result.Current);
        Assert.Contains(result.Notes, n => n.Contains("field 0 channel 0 is not in the field cache", StringComparison.Ordinal));
        Assert.Equal(0, Run("verify", _root).ExitCode);
    }

    /// <summary>
    /// A study whose figure of merit is an extension hands it built-in figures, and those go
    /// through the cache like any other evaluation: the first evaluation stores the field, the
    /// next loads it.
    /// </summary>
    /// <remarks>
    /// The evaluator is called directly: the figures are computed before the extension's
    /// process starts, so what the process then does - and on a machine whose sandbox cannot
    /// start Python, it fails - does not decide this test. Mutation: <c>ExtensionObjective</c>
    /// computing the figures without the store records nothing, and this fails.
    /// </remarks>
    [Fact]
    public void AnExtensionObjectiveComputesItsFiguresThroughTheCache()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var path = Write("bars.json", Model());
        Assert.Equal(0, Run("ext", "register", "shortest", "--project", _root).ExitCode);

        var project = new ProjectLayout(_root);
        var model = ModelValidator.Validate(
            ModelJson.Parse(File.ReadAllText(path)), null, Path.GetDirectoryName(path)).Model!;
        var keys = SolveHash.Of(model);

        IReadOnlyList<string> Evaluate()
        {
            var cache = new FieldCache(project);
            var objective = ExtensionObjective.Evaluator(
                "ext:shortest", project.Extensions, Path.Combine(project.Scratch, "ext"), out _, store: cache);

            try
            {
                _ = objective(model);
            }
            catch (Core.Errors.EinzelException)
            {
                // The extension's own process; see the remarks.
            }

            return [.. cache.Uses(keys).Select(u => u.Status)];
        }

        Assert.Equal([FieldCacheUse.Stored], Evaluate());
        Assert.Equal([FieldCacheUse.Hit], Evaluate());
    }
}
