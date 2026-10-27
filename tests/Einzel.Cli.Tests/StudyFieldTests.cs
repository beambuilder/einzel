using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields.Solved;
using Einzel.Io;
using Einzel.Project;

namespace Einzel.Cli.Tests;

/// <summary>
/// A study's fields: one memo per study, so points that keep the nominal channels share them;
/// the manifest, the stderr line and the dry run say what was solved and loaded; the estimate
/// charges a solve only where a point adds a channel; and a study that will not fit says so.
/// </summary>
/// <remarks>
/// Every comparison is between runs made here: what a study reports against what the same
/// study did, never against a committed number.
/// </remarks>
public sealed class StudyFieldTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("einzel-studyfield-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// Two driven plates in a plane, as in <c>FlyOverrideTests</c>: the upper one at <c>U</c>, the
    /// lower at -1 V, the drive at <c>V</c> and <c>-V</c>. A new <c>V</c> re-weights the same two
    /// channels; a new <c>U</c> is a new DC pattern and a solve.
    /// </summary>
    private const string Plates = """
        {
          "schemaVersion": "0.14",
          "name": "plates",
          "parameters": {
            "U": { "value": 2, "unit": "V" },
            "V": { "value": 5, "unit": "V" }
          },
          "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0.5, 0], "unit": "mm" },
            "direction": { "value": [0, 0, 1] },
            "accelerationPotential": { "value": 200, "unit": "V" }
          },
          "fields": [{
            "type": "solved2d",
            "solve": {
              "drive": { "frequency": { "value": 1, "unit": "MHz" }, "waveform": "sinusoid" },
              "minX": { "value": -6, "unit": "mm" }, "maxX": { "value": 6, "unit": "mm" },
              "minY": { "value": -5, "unit": "mm" }, "maxY": { "value": 5, "unit": "mm" },
              "cellSize": { "value": 0.25, "unit": "mm" },
              "electrodes": [
                { "name": "upper", "shape": "rectangle",
                  "minX": { "value": -4, "unit": "mm" }, "maxX": { "value": 4, "unit": "mm" },
                  "minY": { "value": 2.1, "unit": "mm" }, "maxY": { "value": 3.1, "unit": "mm" },
                  "potential": { "expression": "U", "unit": "V" },
                  "driveAmplitude": { "expression": "V", "unit": "V" } },
                { "name": "lower", "shape": "rectangle",
                  "minX": { "value": -4, "unit": "mm" }, "maxX": { "value": 4, "unit": "mm" },
                  "minY": { "value": -3.1, "unit": "mm" }, "maxY": { "value": -2.1, "unit": "mm" },
                  "potential": { "value": -1, "unit": "V" },
                  "driveAmplitude": { "expression": "-V", "unit": "V" } }
              ]
            }
          }],
          "detector": { "planePoint": { "value": [0, 0, 20], "unit": "mm" }, "normal": { "value": [0, 0, -1] } },
          "transport": { "mode": "trajectory", "relativeTolerance": 1e-9, "maximumFlightTime": { "value": 40, "unit": "us" } }
        }
        """;

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

    /// <summary>A project holding the plates, and a scan of one of its parameters.</summary>
    private string Scan(string name, string parameter, double from, double to, int points, string extra = "")
    {
        var models = Path.Combine(_root, "models");

        if (!Directory.Exists(models))
        {
            Assert.Equal(0, Run("init", _root).ExitCode);
            Directory.CreateDirectory(models);
            File.WriteAllText(Path.Combine(models, "plates.json"), Plates);
        }

        var study = Path.Combine(_root, "studies", name + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(study)!);
        File.WriteAllText(study, FormattableString.Invariant($$"""
            {
              "name": "{{name}}",
              "model": "../models/plates.json",
              "figureOfMerit": "transmission",
              "ions": 3,
              {{extra}}
              "scan": { "parameter": "{{parameter}}", "from": {{from}}, "to": {{to}}, "unit": "V", "points": {{points}} }
            }
            """));

        return study;
    }

    private RunManifest Manifest(string name) =>
        RunManifest.FromJson(File.ReadAllText(Path.Combine(_root, "results", $"{name}.scan.manifest.json")))!;

    private static string FieldLine(string stderr) =>
        Assert.Single(stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries), line => line.StartsWith("  field: ", StringComparison.Ordinal)).TrimEnd('\r');

    /// <summary>The keys the plates compile to with one parameter set, as the study's points will.</summary>
    private static IReadOnlyList<SolveKey> KeysAt(string parameter, double volts) =>
        SolveHash.Of(ModelValidator.Validate(
            ModelJson.Parse(Plates),
            new Dictionary<string, Quantity>(StringComparer.Ordinal) { [parameter] = Quantity.From(volts, "V") }).Model!);

    /// <summary>
    /// An amplitude scan solves the nominal channels once and every point shares them in memory;
    /// its repeat loads them once from the cache. The manifest and the stderr line say so.
    /// </summary>
    /// <remarks>
    /// Mutations: the study's evaluator building through the cache rather than the memo (no
    /// reuse), the manifest without its solves, cache uses, parallelism or field seconds, and
    /// the counts on the line, each fail here.
    /// </remarks>
    [Fact]
    public void AnAmplitudeScanSolvesOnceAndItsPointsShareTheChannels()
    {
        var study = Scan("amplitude", "V", 4, 150, 4, "\"maxParallelism\": 2,");

        var cold = Run("scan", study, "--json");
        Assert.Equal(0, cold.ExitCode);
        Assert.StartsWith(
            "  field: 2 channel(s) solved, 0 loaded from the cache, 8 reused in memory, over 5 evaluation(s); ",
            FieldLine(cold.Stderr), StringComparison.Ordinal);

        var manifest = Manifest("amplitude");
        var key = Assert.Single(manifest.Solves!);
        Assert.Equal(KeysAt("V", 4)[0].ChannelHashes, key.ChannelHashes);
        Assert.Equal([FieldCacheUse.Stored, FieldCacheUse.Stored], manifest.FieldCache!.Select(use => use.Status));
        Assert.Equal(key.ChannelHashes, manifest.FieldCache!.Select(use => use.ChannelHash));
        Assert.Equal(2, manifest.MaxParallelism);
        Assert.True(manifest.FieldSeconds > 0.0);

        var warm = Run("scan", study, "--json");
        Assert.Equal(cold.Stdout, warm.Stdout);
        Assert.StartsWith(
            "  field: 0 channel(s) solved, 2 loaded from the cache, 8 reused in memory, over 5 evaluation(s); ",
            FieldLine(warm.Stderr), StringComparison.Ordinal);
        Assert.Equal([FieldCacheUse.Hit, FieldCacheUse.Hit], Manifest("amplitude").FieldCache!.Select(use => use.Status));
    }

    /// <summary>
    /// A DC scan of a driven model solves a channel per point, and the manifest lists every key
    /// once, in the order of the scan's points - the nominal first - whatever order four
    /// evaluations at once finished in (CLI-5).
    /// </summary>
    /// <remarks>
    /// Mutation: ordering the keys by their hashes rather than by the points they came from
    /// fails here. Ordering them by when each evaluation started fails only when evaluations
    /// start out of point order, which is a race; the 24-point scan at four at once makes it
    /// likely, not certain.
    /// </remarks>
    [Fact]
    public void AStudyManifestListsItsKeysInPointOrder()
    {
        var study = Scan("dc", "U", 4, 0, 5, "\"maxParallelism\": 4,");

        var run = Run("scan", study, "--json");
        Assert.Equal(0, run.ExitCode);
        // Five, not six: at U = 1 the DC pattern (1 V and -1 V) is the drive's pattern, so that
        // point's one channel is the RF channel the nominal solved.
        Assert.StartsWith("  field: 5 channel(s) solved, 0 loaded from the cache, ", FieldLine(run.Stderr), StringComparison.Ordinal);

        // The nominal U = 2 is also the middle point, whose key is then not repeated.
        IReadOnlyList<string>[] expected =
        [
            KeysAt("U", 2)[0].ChannelHashes,
            KeysAt("U", 4)[0].ChannelHashes,
            KeysAt("U", 3)[0].ChannelHashes,
            KeysAt("U", 1)[0].ChannelHashes,
            KeysAt("U", 0)[0].ChannelHashes,
        ];

        var solves = Manifest("dc").Solves!;
        Assert.Equal(expected.Length, solves.Count);

        for (var k = 0; k < expected.Length; k++)
        {
            Assert.Equal(expected[k], solves[k].ChannelHashes);
        }

        // The RF channel is shared by every point and listed once, after the nominal DC.
        Assert.Equal(
            expected.SelectMany(keys => keys).Distinct(StringComparer.Ordinal),
            Manifest("dc").FieldCache!.Select(use => use.ChannelHash));

        // And over 24 points, four at a time, each point's own DC key in descending U.
        Assert.Equal(0, Run("scan", Scan("long", "U", 30, 7, 24, "\"maxParallelism\": 4,"), "--json").ExitCode);
        Assert.Equal(
            [KeysAt("U", 2)[0].ChannelHashes, .. Enumerable.Range(0, 24).Select(k => KeysAt("U", 30 - k)[0].ChannelHashes)],
            Manifest("long").Solves!.Select(key => key.ChannelHashes));
    }

    /// <summary>
    /// A dry run says which channels the nominal model and the study's ends need, how many the
    /// cache holds, and whether the points re-weight the nominal channels or solve their own.
    /// </summary>
    /// <remarks>
    /// Mutations: the survey not sampling the ends, not asking the cache, or counting the
    /// nominal channels as a point's, each fail here.
    /// </remarks>
    [Fact]
    public void ADryRunSaysWhatTheFieldsWillCostAndWhatTheCacheHolds()
    {
        var amplitude = Scan("amplitude", "V", 4, 7, 4);
        var dc = Scan("dc", "U", 0, 4, 5);

        JsonNode Field(string study) => JsonNode.Parse(Run("scan", study, "--dry-run", "--json").Stdout)!["field"]!;

        var a = Field(amplitude);
        Assert.Equal((2, 0, 0, 1, 5), ((int)a["channels"]!, (int)a["cachedChannels"]!, (int)a["pointChannels"]!, (int)a["solvingEvaluations"]!, (int)a["evaluations"]!));

        var d = Field(dc);
        Assert.Equal((4, 0, 1, 6), ((int)d["channels"]!, (int)d["cachedChannels"]!, (int)d["pointChannels"]!, (int)d["solvingEvaluations"]!));
        Assert.Equal(3, d["solves"]!.AsArray().Count);

        Assert.Equal(0, Run("scan", amplitude).ExitCode);
        Assert.Equal(0, Run("scan", dc).ExitCode);

        a = Field(amplitude);
        Assert.Equal((2, 0), ((int)a["cachedChannels"]!, (int)a["solvingEvaluations"]!));

        d = Field(dc);
        Assert.Equal((4, 0, true), ((int)d["cachedChannels"]!, (int)d["solvingEvaluations"]!, (bool)d["pointsCached"]!));
        Assert.Contains("channel(s) in the cache", Run("scan", dc, "--dry-run").Stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// An ensemble figure builds its field once per evaluation and flies every member through
    /// it, as the energy scan does: seven ions read each channel once.
    /// </summary>
    /// <remarks>Mutation: building the field inside the member loop of <c>transmission</c> or <c>confined</c> fails here.</remarks>
    [Theory]
    [InlineData("transmission")]
    [InlineData("confined")]
    public void AnEnsembleFliesEveryMemberThroughOneField(string figure)
    {
        var model = ModelValidator.Validate(ModelJson.Parse(Plates)).Model!;
        var store = new CountingStore();

        _ = FiguresOfMerit.Evaluator(figure, 0.02, 7, store: store)(model);
        Assert.Equal((2, 2), (store.Loads, store.Saves));

        _ = FiguresOfMerit.Evaluator(figure, 0.02, 7, store: store)(model);
        Assert.Equal((4, 2), (store.Loads, store.Saves));
    }

    /// <summary>
    /// A study's result does not depend on what its memo kept: a DC scan with a budget of
    /// nothing, which keeps the nominal channels and lets go of every other, computes what the
    /// default budget computes.
    /// </summary>
    /// <remarks>Mutation: a memo that hands out a channel other than the one asked for fails here.</remarks>
    [Fact]
    public void AStudysResultDoesNotDependOnItsMemoryBudget()
    {
        var tight = Scan("tight", "U", 0, 4, 5, "\"memoryBudget\": { \"value\": 0, \"unit\": \"B\" },");
        var loose = Scan("loose", "U", 0, 4, 5);

        var a = JsonNode.Parse(Run("scan", tight, "--json", "--no-cache").Stdout)!["points"]!.ToJsonString();
        var b = JsonNode.Parse(Run("scan", loose, "--json", "--no-cache").Stdout)!["points"]!.ToJsonString();

        Assert.Equal(a, b);
    }

    /// <summary>
    /// A budget of nothing keeps the nominal channels and nothing else: a scan that visits one DC
    /// level twice solves it twice, where the default budget solves it once and reuses it. The
    /// upper plate holds <c>W x W</c>, so <c>W</c> = -3 and 3 are one DC pattern.
    /// </summary>
    /// <remarks>
    /// Mutations: the study never bounding its memo, a memo that never lets go, and a bound
    /// that does not pin the nominal channels, each fail here.
    /// </remarks>
    [Fact]
    public void ABudgetOfNothingKeepsOnlyTheNominalChannels()
    {
        var tight = Scan("tight", "W", -3, 3, 3, "\"memoryBudget\": { \"value\": 0, \"unit\": \"B\" }, \"maxParallelism\": 1,");
        var loose = Scan("loose", "W", -3, 3, 3, "\"maxParallelism\": 1,");
        File.WriteAllText(
            Path.Combine(_root, "models", "plates.json"),
            Plates.Replace("\"V\": { \"value\": 5, \"unit\": \"V\" }", "\"V\": { \"value\": 5, \"unit\": \"V\" }, \"W\": { \"value\": 2, \"unit\": \"V\" }, \"one\": { \"value\": 1, \"unit\": \"V\" }", StringComparison.Ordinal)
                .Replace("\"expression\": \"U\", \"unit\": \"V\"", "\"expression\": \"W * W / one\", \"unit\": \"V\"", StringComparison.Ordinal));

        // Nominal: its DC pattern and the RF, pinned. W = -3: a DC pattern of its own. W = 0:
        // another. W = 3: the first again, solved again under a budget of nothing.
        Assert.StartsWith(
            "  field: 5 channel(s) solved, 0 loaded from the cache, 3 reused in memory, ",
            FieldLine(Run("scan", tight, "--no-cache").Stderr), StringComparison.Ordinal);
        Assert.StartsWith(
            "  field: 4 channel(s) solved, 0 loaded from the cache, 4 reused in memory, ",
            FieldLine(Run("scan", loose, "--no-cache").Stderr), StringComparison.Ordinal);
    }

    /// <summary>A memory budget in a unit that is not memory is refused before anything is solved.</summary>
    [Fact]
    public void AMemoryBudgetInAnotherUnitIsRefused()
    {
        var study = Scan("budget", "V", 4, 7, 2, "\"memoryBudget\": { \"value\": 2, \"unit\": \"V\" },");

        var (exitCode, stdout, _) = Run("scan", study, "--json");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("/memoryBudget/unit", stdout + Run("scan", study).Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_root, "results")) && Directory.GetFiles(Path.Combine(_root, "results"), "budget.*").Length > 0);

        // And a unit that is memory is read in its own size.
        Assert.Equal(
            (2L << 30, 2_000_000_000L),
            (StudyBinding.MemoryBudget(new StudyDocument { MemoryBudget = new MemoryDocument { Value = 2, Unit = "GiB" } }),
             StudyBinding.MemoryBudget(new StudyDocument { MemoryBudget = new MemoryDocument { Value = 2, Unit = "GB" } })));
    }

    /// <summary>
    /// A memory budget states its value and its unit, and the schema says so: both required, the
    /// unit one of the seven memory units. A budget without its unit is refused at load, and a
    /// null unit is called null, not an empty name.
    /// </summary>
    /// <remarks>
    /// Mutations: either member not required, the schema's per-record enum hook ignoring a nested
    /// record, the units left out of the catalogue, and the null unit printed as an empty name,
    /// each fail here.
    /// </remarks>
    [Fact]
    public void AMemoryBudgetStatesItsValueAndItsUnit()
    {
        var (schemaExit, schema, _) = Run("schema", "--study");
        Assert.Equal(0, schemaExit);
        var memory = JsonNode.Parse(schema)!["$defs"]!["MemoryDocument"]!;
        Assert.Equal("[\"value\",\"unit\"]", memory["required"]!.ToJsonString());
        Assert.Equal(
            "[\"B\",\"kB\",\"MB\",\"GB\",\"KiB\",\"MiB\",\"GiB\"]",
            memory["properties"]!["unit"]!["enum"]!.ToJsonString());

        var bare = Scan("bare", "V", 4, 7, 2, "\"memoryBudget\": { \"value\": 2 },");
        var refused = Run("scan", bare, "--json");
        Assert.NotEqual(0, refused.ExitCode);
        Assert.Contains("missing required properties including: 'Unit'", refused.Stdout + refused.Stderr, StringComparison.Ordinal);

        var nameless = Scan("nameless", "V", 4, 7, 2, "\"memoryBudget\": { \"value\": 2, \"unit\": null },");
        var named = Run("scan", nameless, "--json");
        Assert.NotEqual(0, named.ExitCode);
        Assert.Contains("null is not an amount of memory", named.Stdout + named.Stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// A study whose predicted peak is more than this process may use says so before it starts,
    /// and one that fits says nothing.
    /// </summary>
    /// <remarks>Mutation: the warning left out, or given whatever the peak, fails here.</remarks>
    [Fact]
    public void AStudyThatWillNotFitSaysSoBeforeItStarts()
    {
        var study = Scan("dc", "U", 0, 4, 5, "\"maxParallelism\": 3,");
        var project = new ProjectLayout(_root);

        var said = new List<string>();
        _ = StudyCommand.Scan(study, project, dryRun: true, announce: said.Add, availableMemoryBytes: 1024);
        var warning = Assert.Single(said);
        Assert.StartsWith("  warning: Memory: this study's peak is ", warning, StringComparison.Ordinal);
        Assert.Contains("so it will not fit here", warning, StringComparison.Ordinal);
        Assert.Contains("lower maxParallelism", warning, StringComparison.Ordinal);

        said.Clear();
        _ = StudyCommand.Scan(study, project, dryRun: true, announce: said.Add, availableMemoryBytes: 1L << 40);
        Assert.Empty(said);
    }

    /// <summary>
    /// The survey's peak: evaluations solving at once times one solve, plus what the memo keeps.
    /// A DC scan's points solve, so parallelism multiplies; an amplitude scan's nominal solves
    /// alone and the points add nothing.
    /// </summary>
    /// <remarks>Mutation: a peak that ignores parallelism, or charges it to a study whose points do not solve, fails here.</remarks>
    [Fact]
    public void AStudysPeakCountsTheEvaluationsThatSolveAtOnce()
    {
        StudyFieldSurvey Survey(string study) => StudyFields.Survey(
            StudyCommand.Load(study).Study, ModelJson.Parse(Plates), Path.Combine(_root, "models"), new ProjectLayout(_root))!;

        var dc = Survey(Scan("dc", "U", 0, 4, 5, "\"maxParallelism\": 3,"));
        Assert.Equal((3 * dc.SolvePeakMiB) + dc.BudgetMiB, dc.PeakMiB, 9);
        Assert.Equal(StudyFields.DefaultBudgetFields * dc.ChannelMiB, dc.BudgetMiB, 9);

        var amplitude = Survey(Scan("amplitude", "V", 4, 7, 4, "\"maxParallelism\": 3,"));
        Assert.Equal(amplitude.SolvePeakMiB + amplitude.ChannelMiB, amplitude.PeakMiB, 9);

        Assert.Equal(0, Run("scan", Path.Combine(_root, "studies", "amplitude.json")).ExitCode);
        Assert.Equal(amplitude.ChannelMiB, Survey(Path.Combine(_root, "studies", "amplitude.json")).PeakMiB, 9);
    }

    /// <summary>
    /// <c>estimate</c> charges a solve only where a point adds a channel, counts the nominal
    /// evaluation, and loads what the cache holds.
    /// </summary>
    /// <remarks>
    /// Mutations: a solve charged per evaluation, the nominal evaluation not counted, and the
    /// cache not asked, each fail here.
    /// </remarks>
    [Fact]
    public void AnEstimateChargesASolveOnlyWhereAPointAddsAChannel()
    {
        var amplitude = Scan("amplitude", "V", 4, 7, 4);
        var dc = Scan("dc", "U", 0, 4, 5);
        var project = new ProjectLayout(_root);

        StudyEstimate Cost(string study) => EstimateCommand.ForStudy(study, calibrate: false, project: project).Study!;

        var a = Cost(amplitude);
        Assert.Equal((5, 1, 2, 0), (a.Evaluations, a.SolvingEvaluations, a.SolvedChannels, a.LoadedChannels));

        var d = Cost(dc);
        Assert.Equal((6, 6, 2 + 5, 0), (d.Evaluations, d.SolvingEvaluations, d.SolvedChannels, d.LoadedChannels));
        Assert.True(d.FieldSeconds > a.FieldSeconds);

        Assert.Equal(0, Run("scan", amplitude).ExitCode);
        a = Cost(amplitude);
        Assert.Equal((0, 0, 2), (a.SolvingEvaluations, a.SolvedChannels, a.LoadedChannels));
    }

    /// <summary>
    /// A bound memo keeps what it pinned, and lets go of the least recently used of the rest when
    /// it holds more than its budget; a channel let go is read from the store behind it again.
    /// </summary>
    /// <remarks>Mutations: no eviction, eviction of the most recently used, or of a pinned channel, each fail here.</remarks>
    [Fact]
    public void ABoundMemoKeepsWhatItPinnedAndLetsGoOfTheLeastRecentlyUsed()
    {
        var inner = new CountingStore();
        var memo = new SolveMemo(inner);

        memo.Save(Entry("a"), new double[8], Record(1.5));
        memo.Bound(3 * 64);

        memo.Save(Entry("b"), new double[8], Record(0.0));
        memo.Save(Entry("c"), new double[8], Record(0.0));
        Assert.Equal(3 * 64, memo.HeldBytes);

        Assert.True(memo.TryLoad(Entry("b"), out _, out _));
        memo.Save(Entry("d"), new double[8], Record(0.0));
        Assert.Equal(3 * 64, memo.HeldBytes);

        var loads = inner.Loads;
        Assert.True(memo.TryLoad(Entry("a"), out _, out _));
        Assert.True(memo.TryLoad(Entry("b"), out _, out _));
        Assert.True(memo.TryLoad(Entry("d"), out _, out _));
        Assert.Equal(loads, inner.Loads);

        Assert.True(memo.TryLoad(Entry("c"), out _, out _));
        Assert.Equal(loads + 1, inner.Loads);
        Assert.Equal((4, 1), (memo.Solved, memo.Loaded));
        Assert.True(memo.FieldSeconds >= 1.5);
    }

    /// <summary>A budget below what is pinned keeps the pinned channels and nothing else.</summary>
    [Fact]
    public void ABudgetBelowThePinnedKeepsThePinnedAlone()
    {
        var inner = new CountingStore();
        var memo = new SolveMemo(inner);

        memo.Save(Entry("a"), new double[8], Record(0.0));
        memo.Bound(0);
        memo.Save(Entry("b"), new double[8], Record(0.0));

        Assert.Equal(64, memo.HeldBytes);
        Assert.True(memo.TryLoad(Entry("a"), out _, out _));
        Assert.Equal(0, inner.Loads);
        Assert.True(memo.TryLoad(Entry("b"), out _, out _));
        Assert.Equal(1, inner.Loads);
    }

    private static SolveEntry Entry(string hash) => new()
    {
        ChannelHash = hash,
        GeometryHash = "g",
        Dimensions = 2,
        Nodes = [2, 4],
        Origin = [0.0, 0.0],
        Spacing = [1.0, 1.0],
        Edges = [],
        Holding = [1.0],
    };

    private static SolveRecord Record(double seconds) =>
        new(new SolveReport(true, 1, 1.0, 0.0, 0.1), 0, 0, seconds);

    /// <summary>A store that keeps what it is given and counts what it is asked.</summary>
    private sealed class CountingStore : ISolveStore
    {
        private readonly Dictionary<string, (double[] Potential, SolveRecord Record)> _held = [];

        public int Loads { get; private set; }

        public int Saves { get; private set; }

        public bool TryLoad(
            SolveEntry entry,
            [NotNullWhen(true)] out double[]? potential,
            [NotNullWhen(true)] out SolveRecord? record)
        {
            Loads++;
            var found = _held.TryGetValue(entry.ChannelHash, out var held);
            (potential, record) = found ? held : (null, null);
            return found;
        }

        public void Save(SolveEntry entry, ReadOnlySpan<double> potential, SolveRecord record)
        {
            Saves++;
            _held[entry.ChannelHash] = (potential.ToArray(), record);
        }
    }
}
