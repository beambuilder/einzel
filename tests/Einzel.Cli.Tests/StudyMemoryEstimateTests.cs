using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Sweeps;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>Tests that measure the machine, or the thread pool, and so must not share it.</summary>
[CollectionDefinition(nameof(RunsAlone), DisableParallelization = true)]
public sealed class RunsAlone;

/// <summary>A project holding the shipped mirror pair, and studies written over it.</summary>
/// <remarks>
/// A fixture rather than a test class, so the in-flight tests and the arithmetic tests share the
/// studies they write without one test class constructing another.
/// </remarks>
internal sealed class StudyProject : IDisposable
{
    private const string Template = "planar-mirror-pair";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-study-memory", Guid.NewGuid().ToString("N"));

    /// <summary>The sweep's channels, as the driver takes them.</summary>
    public static PerturbationChannel[] SweepChannels =>
    [
        new("capToCap", Quantity.From(0.1, "mm")),
        new("mirrorDepth", Quantity.From(0.1, "mm")),
        new("boardGap", Quantity.From(0.1, "mm")),
    ];

    /// <summary>The model every study here varies.</summary>
    public string ModelPath => Path.Combine(_root, "models", Template + ".json");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>A scan of the mirror separation.</summary>
    /// <remarks>
    /// 700 to 760 mm by default, which keeps the solve at one mesh: the domain runs to half the
    /// separation, and at 800 mm it crosses a power of two and the solve doubles, which is what
    /// <c>AStudyIsBilledAtTheDearestEndOfItsRange</c> is about and the other tests are not.
    /// </remarks>
    public string Scan(int points, int? maxParallelism, double from = 700.0, double to = 760.0) => Study(
        string.Create(
            CultureInfo.InvariantCulture,
            $"{{ \"schemaVersion\": \"0.1\", \"name\": \"separation\", \"model\": \"MODEL\", "
            + $"\"figureOfMerit\": \"flightTime\", {Parallelism(maxParallelism)}\"scan\": {{ "
            + $"\"parameter\": \"capToCap\", \"from\": {from}, \"to\": {to}, \"unit\": \"mm\", "
            + $"\"points\": {points} }} }}"));

    /// <summary>A tolerance sweep over three channels.</summary>
    public string Sweep(int draws, bool oneAtATime, int? maxParallelism) => Study(
        string.Create(
            CultureInfo.InvariantCulture,
            $"{{ \"schemaVersion\": \"0.1\", \"name\": \"tolerance\", \"model\": \"MODEL\", "
            + $"\"figureOfMerit\": \"flightTime\", {Parallelism(maxParallelism)}\"draws\": {draws}, \"seed\": 7, "
            + $"\"oneAtATime\": {(oneAtATime ? "true" : "false")}, \"channels\": [ "
            + $"{{ \"parameter\": \"capToCap\", \"halfWidth\": 0.1, \"unit\": \"mm\" }}, "
            + $"{{ \"parameter\": \"mirrorDepth\", \"halfWidth\": 0.1, \"unit\": \"mm\" }}, "
            + $"{{ \"parameter\": \"boardGap\", \"halfWidth\": 0.1, \"unit\": \"mm\" }} ] }}"));

    /// <summary>An optimization of the cap potential.</summary>
    public string Optimization() => Study(
        "{ \"schemaVersion\": \"0.1\", \"name\": \"focus\", \"model\": \"MODEL\", "
        + "\"figureOfMerit\": \"flightTime\", \"maxParallelism\": 8, \"variables\": [ "
        + "{ \"parameter\": \"capPotential\", \"minimum\": 4.6, \"maximum\": 5.0, \"unit\": \"kV\" } ], "
        + "\"algorithm\": \"nelderMead\", \"maximumEvaluations\": 25 }");

    /// <summary>A boundary search along the cap potential.</summary>
    public string Boundary() => Study(
        "{ \"schemaVersion\": \"0.1\", \"name\": \"edge\", \"model\": \"MODEL\", "
        + "\"figureOfMerit\": \"transmission\", \"maxParallelism\": 8, \"boundary\": { "
        + "\"parameter\": \"capPotential\", \"from\": 4600.0, \"to\": 5000.0, \"unit\": \"V\" } }");

    /// <summary>A copy of the model with one parameter set, beside it.</summary>
    public string ModelWith(string parameter, double value)
    {
        Study("{}");

        var model = JsonNode.Parse(File.ReadAllText(ModelPath))!;
        model["parameters"]![parameter]!["value"] = value;

        var path = Path.Combine(_root, "models", $"{parameter}-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, model.ToJsonString());

        return path;
    }

    /// <summary>The model document and its directory, as a study's driver is handed them.</summary>
    public static (ModelDocument Document, string Directory) Document(string studyPath)
    {
        var (_, modelPath, _) = StudyCommand.Load(studyPath);

        return (ModelJson.Parse(File.ReadAllText(modelPath)), Path.GetDirectoryName(modelPath)!);
    }

    private string Study(string body)
    {
        if (!File.Exists(ModelPath))
        {
            Assert.Equal(0, Cli("init", _root));
            Assert.Equal(0, Cli("new", ModelPath, "--from-template", Template));
        }

        var path = Path.Combine(_root, "studies", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        File.WriteAllText(
            path, body.Replace("MODEL", "../models/" + Template + ".json", StringComparison.Ordinal));

        return path;
    }

    private static string Parallelism(int? maxParallelism) =>
        maxParallelism is { } p ? string.Create(CultureInfo.InvariantCulture, $"\"maxParallelism\": {p}, ") : string.Empty;

    private static int Cli(params string[] args)
    {
        var previousOut = Console.Out;
        var previousError = Console.Error;

        try
        {
            Console.SetOut(new StringWriter());
            Console.SetError(new StringWriter());
            return Program.Main(args);
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }
}

/// <summary>
/// A study's memory is one evaluation's - at the dearest point of its range - times how many
/// evaluations are in flight at once, and a diffusive run with its own space charge holds the
/// self-field's solve on top of its step.
/// </summary>
/// <remarks>
/// <para>
/// <c>ForStudy</c> multiplied the time by the evaluation count and left the memory at one
/// nominal evaluation's, so a scan running sixteen points at once was billed for one of them, and
/// a scan that grows the mesh was billed at its declared values. The arithmetic is asserted here;
/// that it describes what the drivers actually do is asserted by <see cref="StudyInFlightTests"/>,
/// which runs them.
/// </para>
/// <para>
/// Uncalibrated throughout. The memory is arithmetic on the declaration, so two estimates of one
/// model agree to the bit, which is what lets a study's figure be compared with a separate
/// estimate of its model - the thing <c>StudyEstimateTests</c> must not do with a time.
/// </para>
/// </remarks>
public sealed class StudyMemoryEstimateTests(ITestOutputHelper output) : IDisposable
{
    private readonly StudyProject _project = new();

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-study-memory-tubes", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _project.Dispose();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>A scan running two points at once is billed two evaluations.</summary>
    [Fact]
    public void AParallelScanHoldsOneEvaluationPerPointInFlight()
    {
        var costed = EstimateCommand.ForStudy(_project.Scan(points: 9, maxParallelism: 2), calibrate: false);
        var study = costed.Study!;
        var one = EstimateCommand.Execute(_project.ModelPath, calibrate: false).MemoryMiB;

        output.WriteLine($"{study.InFlight} in flight x {study.MemoryMiBPerEvaluation:F2} MiB = {costed.MemoryMiB:F2} MiB");
        output.WriteLine(costed.Basis[costed.Basis.IndexOf("Memory: this scan", StringComparison.Ordinal)..]);

        Assert.True(one > 0.0, "the model solves nothing, so there is nothing to multiply");
        Assert.Equal(2, study.InFlight);
        Assert.Equal(one, study.MemoryMiBPerEvaluation);
        Assert.Equal(2.0 * one, costed.MemoryMiB, 9);
        Assert.Contains("runs 2 evaluations at once (maxParallelism 2)", costed.Basis, StringComparison.Ordinal);
    }

    /// <summary>A scan with fewer points than its parallelism holds every point, and no more.</summary>
    [Fact]
    public void AScanShorterThanItsParallelismHoldsEveryPoint()
    {
        var costed = EstimateCommand.ForStudy(_project.Scan(points: 3, maxParallelism: 8), calibrate: false);

        Assert.Equal(3, costed.Study!.InFlight);
        Assert.Equal(3.0 * costed.Study.MemoryMiBPerEvaluation, costed.MemoryMiB, 9);
        Assert.Contains("every one it has to run at once, 3, which is fewer than maxParallelism 8", costed.Basis, StringComparison.Ordinal);
    }

    /// <summary>With nothing declared, a scan runs one point per processor.</summary>
    [Fact]
    public void AScanWithNoSettingRunsOnePerProcessor()
    {
        var points = Environment.ProcessorCount + 2;
        var costed = EstimateCommand.ForStudy(_project.Scan(points, maxParallelism: null), calibrate: false);

        Assert.Equal(Environment.ProcessorCount, costed.Study!.InFlight);
        Assert.Contains("one per processor on this machine", costed.Basis, StringComparison.Ordinal);
    }

    /// <summary>
    /// A sweep's draws and its attribution run one after the other, so it holds the wider of
    /// the two - here the attribution, three channels against two draws.
    /// </summary>
    [Fact]
    public void ASweepHoldsTheWiderOfItsTwoPhases()
    {
        var attributed = EstimateCommand.ForStudy(_project.Sweep(draws: 2, oneAtATime: true, maxParallelism: 8), calibrate: false);
        var drawn = EstimateCommand.ForStudy(_project.Sweep(draws: 6, oneAtATime: false, maxParallelism: 2), calibrate: false);

        Assert.Equal(3, attributed.Study!.InFlight);
        Assert.Equal(2, drawn.Study!.InFlight);
        Assert.Equal(2.0 * drawn.Study.MemoryMiBPerEvaluation, drawn.MemoryMiB, 9);
    }

    /// <summary>The searches evaluate one point at a time, so they hold one evaluation.</summary>
    [Fact]
    public void AnOptimizationAndABoundarySearchHoldOne()
    {
        foreach (var path in new[] { _project.Optimization(), _project.Boundary() })
        {
            var costed = EstimateCommand.ForStudy(path, calibrate: false);

            output.WriteLine($"{costed.Study!.Kind}: {costed.Study.InFlight} in flight");

            Assert.Equal(1, costed.Study.InFlight);
            Assert.Equal(costed.Study.MemoryMiBPerEvaluation, costed.MemoryMiB);
            Assert.Contains("evaluates one point at a time", costed.Basis, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A scan that grows the mesh is billed at the dearest end of its range, not at the model's
    /// declared values.
    /// </summary>
    /// <remarks>
    /// The mirror pair's solve runs from <c>-mirrorDepth</c> to <c>capToCap / 2</c>, so scanning
    /// the separation out to three meters takes the solve from 512 intervals along it to 2048. The
    /// expectation is a separate estimate of the model at that end, which is arithmetic and so
    /// agrees to the bit; billed at the nominal, the scan was a quarter of it.
    /// </remarks>
    [Fact]
    public void AStudyIsBilledAtTheDearestEndOfItsRange()
    {
        var costed = EstimateCommand.ForStudy(
            _project.Scan(points: 5, maxParallelism: 1, from: 300.0, to: 3000.0), calibrate: false);

        var nominal = EstimateCommand.Execute(_project.ModelPath, calibrate: false).MemoryMiB;
        var far = EstimateCommand.Execute(_project.ModelWith("capToCap", 3000.0), calibrate: false).MemoryMiB;

        output.WriteLine($"nominal {nominal:F2} MiB, at 3000 mm {far:F2} MiB, billed {costed.Study!.MemoryMiBPerEvaluation:F2} MiB");

        Assert.True(far > 2.0 * nominal, "the far end does not grow the mesh, so this tests nothing");
        Assert.Equal(far, costed.Study.MemoryMiBPerEvaluation);
        Assert.Equal(far, costed.MemoryMiB);
        Assert.Contains("dearest end of the study's range, where capToCap is varied", costed.Basis, StringComparison.Ordinal);
    }

    /// <summary>
    /// A study whose evaluations fit one at a time and not together says so, and names the
    /// setting that fixes it.
    /// </summary>
    [Fact]
    public void AStudyThatWillNotFitTogetherSaysSo()
    {
        var path = _project.Scan(points: 9, maxParallelism: 4);
        var one = EstimateCommand.Execute(_project.ModelPath, calibrate: false).MemoryMiB;

        // Room for two and a half evaluations: each fits, four at once do not.
        var room = (long)(2.5 * one * 1024.0 * 1024.0);
        var tight = EstimateCommand.ForStudy(path, calibrate: false, availableMemoryBytes: room);
        var plenty = EstimateCommand.ForStudy(path, calibrate: false, availableMemoryBytes: 1L << 40);

        output.WriteLine(tight.Basis[tight.Basis.IndexOf("Memory: this scan", StringComparison.Ordinal)..]);

        Assert.Contains("the study's peak is", tight.Basis, StringComparison.Ordinal);
        Assert.Contains("so it will not fit here.", tight.Basis, StringComparison.Ordinal);
        Assert.DoesNotContain("the model's peak is", tight.Basis, StringComparison.Ordinal);
        Assert.DoesNotContain("the study's peak is", plenty.Basis, StringComparison.Ordinal);
    }

    /// <summary>
    /// Mean-field space charge is charged once on top of a diffusive run, and once for a mixture
    /// too, with the summed charge it is solved for.
    /// </summary>
    /// <remarks>
    /// The species are stepped separately, each holding its own density and coefficients, but
    /// share one self-field - a single solve for their summed charge - so the term is not
    /// multiplied by the species count. Measured with two species it adds 69.3 to 69.4 bytes a
    /// node where one species adds 61.3: the same solve, plus the summed charge's own array.
    /// </remarks>
    [Fact]
    public void ASelfFieldIsChargedOnceOnTopOfTheStep()
    {
        static string Member(string name, double mobility) =>
            $$"""
            { "name": "{{name}}", "massToCharge": { "value": 622, "unit": "Da" }, "chargeNumber": 1,
              "mobility": { "zeroField": { "value": {{mobility}}, "unit": "m^2/(V s)" } },
              "population": 1000000 }
            """;

        var pair = $"\"species\": [ {Member("a", 0.05)}, {Member("b", 0.04)} ],";
        var nodes = (double)DiffusionRun.GridFor(Compile(DriftTubes.Cartesian(DriftTubes.Ion, DriftTubes.Mobility))).NodeCount;

        double Billed(string json) => EstimateCommand.Execute(Write(json), calibrate: false).MemoryMiB * 1024.0 * 1024.0;

        var one = (Billed(DriftTubes.Cartesian(DriftTubes.Ion, DriftTubes.Mobility, spaceCharge: "meanField"))
            - Billed(DriftTubes.Cartesian(DriftTubes.Ion, DriftTubes.Mobility))) / nodes;

        var two = (Billed(DriftTubes.Cartesian(species: pair, spaceCharge: "meanField"))
            - Billed(DriftTubes.Cartesian(species: pair))) / nodes;

        var charged = EstimateCommand.Execute(
            Write(DriftTubes.Cartesian(DriftTubes.Ion, DriftTubes.Mobility, spaceCharge: "meanField")), calibrate: false);

        output.WriteLine($"self-field: {one:F3} bytes a node with one species, {two:F3} with two, over {nodes} nodes");

        Assert.Equal(62.0, one, 9);
        Assert.Equal(70.0, two, 9);
        Assert.Contains("one mean-field self-field over the grid at 62 bytes a node", charged.Basis, StringComparison.Ordinal);
    }

    private string Write(string json)
    {
        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, $"tube-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);

        return path;
    }

    private static CompiledModel Compile(string json)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(json));

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.ToString())));

        return validation.Model!;
    }
}

/// <summary>
/// What the estimate says is in flight is what the drivers run - measured by running them.
/// </summary>
/// <remarks>
/// <para>
/// The estimate asks <see cref="ParameterScan.InFlight"/> and <see cref="ToleranceStudy.InFlight"/>
/// rather than restating them, and this runs the drivers to check that their loops do what those
/// say: <see cref="ParameterScan"/> and <see cref="ToleranceStudy"/> are handed the study's own
/// document and an evaluator that counts how many calls are inside it at once.
/// </para>
/// <para>
/// <b>A setting of two, below any runner's core count.</b> A driver that ignored
/// <c>maxParallelism</c> runs one per processor, and at a setting of four that is indistinguishable
/// on a four-core runner - the first version of this test used four, and caught that mutation only
/// on a sixteen-thread workstation. Two is below every machine this runs on that has more than two
/// processors, and the pool is made to start every thread asked for at once, so the overshoot
/// arrives while the first calls are still holding.
/// </para>
/// <para>
/// <b>The evaluator waits for the expected count, or for arrivals to stop</b>, and then holds a
/// moment longer. A driver that runs fewer at once goes quiet below the count; one that runs more
/// is seen above it while the first calls hold. A phase that can never reach the count - the
/// sweep's nominal evaluation runs alone - waits the quiet interval rather than a fixed timeout.
/// Nothing is solved: what the evaluator is handed is never looked at.
/// </para>
/// </remarks>
[Collection(nameof(RunsAlone))]
public sealed class StudyInFlightTests(ITestOutputHelper output) : IDisposable
{
    private readonly StudyProject _project = new();

    public void Dispose() => _project.Dispose();

    [Theory]
    [InlineData(9, 2)]
    [InlineData(3, 8)]
    [InlineData(0, null)]
    public void AScanRunsWhatTheEstimateSaysIsInFlight(int points, int? maxParallelism)
    {
        points = points == 0 ? Environment.ProcessorCount + 2 : points;

        var path = _project.Scan(points, maxParallelism);
        var expected = EstimateCommand.ForStudy(path, calibrate: false).Study!.InFlight;
        var (document, directory) = StudyProject.Document(path);

        var axis = new ScanAxis(
            "capToCap", Quantity.From(700.0, "mm"), Quantity.From(760.0, "mm"), points);

        var counter = new Concurrency(expected);
        WithThreads(() => ParameterScan.Run(document, axis, counter.Evaluate, directory, maxParallelism));

        output.WriteLine($"scan of {points} at maxParallelism {maxParallelism?.ToString(CultureInfo.InvariantCulture) ?? "unset"}: "
            + $"estimate {expected}, driver {counter.Most}");

        Assert.Equal(expected, counter.Most);
    }

    [Theory]
    [InlineData(6, false, 2)]
    [InlineData(2, true, 8)]
    public void ASweepRunsWhatTheEstimateSaysIsInFlight(int draws, bool oneAtATime, int maxParallelism)
    {
        var path = _project.Sweep(draws, oneAtATime, maxParallelism);
        var expected = EstimateCommand.ForStudy(path, calibrate: false).Study!.InFlight;
        var (document, directory) = StudyProject.Document(path);

        var counter = new Concurrency(expected);

        WithThreads(() => ToleranceStudy.Run(
            document, StudyProject.SweepChannels, counter.Evaluate, draws, seed: 7, oneAtATime,
            sourceDirectory: directory, maxParallelism: maxParallelism));

        output.WriteLine($"sweep of {draws} draws, attribution {oneAtATime}, maxParallelism {maxParallelism}: "
            + $"estimate {expected}, driver {counter.Most}");

        Assert.Equal(expected, counter.Most);
    }

    /// <summary>Runs a driver with the thread pool able to start every thread it asks for at once.</summary>
    /// <remarks>
    /// The pool starts up to its minimum thread count immediately and injects the rest slowly, about
    /// one every half second. A test thread is itself a pool thread, so at one per processor the
    /// last evaluation waited for an injection, and the count read one short - 15 of 16 here - with
    /// nothing wrong in the driver. Raising the minimum for the run measures the driver rather than
    /// the pool, and lets a driver that overshoots be seen at once. Process-wide, which is why this
    /// collection runs alone, and restored afterwards.
    /// </remarks>
    private static T WithThreads<T>(Func<T> run)
    {
        ThreadPool.GetMinThreads(out var workers, out var completion);

        try
        {
            ThreadPool.SetMinThreads(Math.Max(workers, 4 * Environment.ProcessorCount), completion);
            return run();
        }
        finally
        {
            ThreadPool.SetMinThreads(workers, completion);
        }
    }

    /// <summary>Counts how many evaluations are inside it at once.</summary>
    private sealed class Concurrency(int expected)
    {
        /// <summary>How long arrivals may stop before a phase is taken to have no more coming.</summary>
        private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(200);

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private int _current;
        private int _most;
        private long _lastArrivalTicks;

        public int Most => Volatile.Read(ref _most);

        public double? Evaluate(CompiledModel model)
        {
            var now = Interlocked.Increment(ref _current);
            Interlocked.Exchange(ref _lastArrivalTicks, _clock.Elapsed.Ticks);

            for (var seen = Volatile.Read(ref _most); now > seen; seen = Volatile.Read(ref _most))
            {
                if (Interlocked.CompareExchange(ref _most, now, seen) == seen)
                {
                    break;
                }
            }

            while (Volatile.Read(ref _current) < expected
                && _clock.Elapsed - TimeSpan.FromTicks(Interlocked.Read(ref _lastArrivalTicks)) < Quiet)
            {
                Thread.Sleep(1);
            }

            // Long enough for any evaluation the driver should not have started to arrive.
            Thread.Sleep(50);

            Interlocked.Decrement(ref _current);
            return 1.0;
        }
    }
}

/// <summary>
/// The self-field's memory, measured as the estimate states it: the live set, sampled through a
/// real diffusive run.
/// </summary>
/// <remarks>
/// <para>
/// The same sampler as <c>SolveMemoryTests</c> in <c>Einzel.Fields.Tests</c> - a forced full
/// collection, then the collector's own record of what survived it, waiting as long again as the
/// collection took - copied rather than shared, since that one is a private helper of a test
/// assembly this one does not reference. See its remarks for why each of those choices was made.
/// </para>
/// <para>
/// <b>Two geometries.</b> A Cartesian tube with nothing in it, where the self-field's mask fixes
/// only the edges; and a cylindrical bore whose earthed wall lies inside the density grid, where
/// the mask fixes every absorbing node of the wall and the solve is the axisymmetric one.
/// </para>
/// <para>
/// <b>The run has to refresh inside the stepping loop</b>, and the test asserts that it did. The
/// first Poisson solve is done before the loop, when the step's own arrays do not exist yet, and a
/// run of one step reads 25 bytes a node for the self-field where the run reads 61: every later
/// refresh solves on top of the step. A run too short to refresh would pass against a statement
/// of 25 and say nothing about the peak.
/// </para>
/// <para>
/// <b>Release holds what the estimate states; Debug holds one array more.</b> Measured in fresh
/// processes on the same run: 61.4 to 61.6 bytes a node optimized, 68.7 to 70.1 in Debug, which
/// keeps a local alive to the end of its method where the optimizer ends it at its last use. The
/// estimate is for a released build, and CI tests Release, so the band is main's 0.90 to 1.02
/// there; a Debug run is allowed the eight bytes it was measured to hold and no more.
/// </para>
/// <para>
/// <b>The middle of five, where that one keeps the middle of three</b>, because the first reading
/// is already an outlier. The first mean-field run in a process reads about 300 KiB - 18 bytes a
/// node here - above the four after it (5,326 against 5,029), which is one-time allocation on the
/// first self-field solve; the middle of three then rests on a single further outlier either way,
/// and this test reads a difference of two peaks, so it meets one twice. It read 61.4 in every
/// one of some thirty runs, idle and with every core loaded, except one that failed inside a
/// mutation run and could not be reproduced, and one that read 59.3 while sixteen processes were
/// starting. Every reading is printed, so the next one can be read rather than guessed at.
/// </para>
/// </remarks>
[Collection(nameof(RunsAlone))]
public sealed class SelfFieldMemoryTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("cartesian")]
    [InlineData("cylindrical")]
    public void ASelfFieldHoldsWhatTheEstimateCharges(string geometry)
    {
        const int IntervalsX = 128, IntervalsY = 128;
        const double Flight = 6.0;

        string Model(string? spaceCharge) => geometry == "cartesian"
            ? DriftTubes.Cartesian(DriftTubes.Ion, DriftTubes.Mobility, spaceCharge: spaceCharge,
                intervalsX: IntervalsX, intervalsY: IntervalsY, flightMicroseconds: Flight)
            : DriftTubes.Bore(spaceCharge, IntervalsX, IntervalsY, Flight);

        var root = Path.Combine(Path.GetTempPath(), "einzel-self-field-memory", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var plain = Path.Combine(root, "plain.json");
            var charged = Path.Combine(root, "charged.json");

            File.WriteAllText(plain, Model(null));
            File.WriteAllText(charged, Model("meanField"));

            var stated = (EstimateCommand.Execute(charged, calibrate: false).MemoryMiB
                - EstimateCommand.Execute(plain, calibrate: false).MemoryMiB) * 1024.0 * 1024.0;

            var (withField, solves, nodes) = Peak(charged, output);
            var (without, _, _) = Peak(plain, output);

            var measured = withField - without;

            output.WriteLine(
                $"{geometry} self-field: {measured / nodes:F1} bytes a node measured over {solves} solves, "
                + $"{stated / nodes:F1} stated; the run alone {without / nodes:F1}");

            var optimized = typeof(Transport.Diffusion.DensitySelfField).Assembly
                .GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled != true;

            output.WriteLine(optimized ? "optimized build" : "Debug build: one more array is held");

            Assert.True(solves >= 2, $"the self-field was solved {solves} time(s), so no refresh happened inside the loop");
            Assert.InRange(measured / stated, 0.90, optimized ? 1.02 : 1.15);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A diffusive run's live peak above its baseline, its self-field solves, and its grid's nodes.</summary>
    private static (double Bytes, int Solves, long Nodes) Peak(string path, ITestOutputHelper output)
    {
        var validation = ModelValidator.Validate(
            ModelJson.Parse(File.ReadAllText(path)), null, Path.GetDirectoryName(path));

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.ToString())));

        var model = validation.Model!;
        var (field, warnings) = FieldAssembly.BuildReported(model);
        var solves = 0;

        var (bytes, readings) = PeakLive(() =>
        {
            var outcome = DiffusionRun.Execute(model, field, warnings);
            solves = outcome.Result.SelfFieldSolves;
            return outcome;
        });

        output.WriteLine(
            $"{Path.GetFileNameWithoutExtension(path)}: "
            + string.Join(", ", readings.Select(r => (r / 1024.0).ToString("F1", CultureInfo.InvariantCulture)))
            + " KiB above baseline");

        return (bytes, solves, DiffusionRun.GridFor(model).NodeCount);
    }

    private static (double Middle, double[] Readings) PeakLive(Func<object> build)
    {
        static long Live()
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

            return Survived();
        }

        static long Survived()
        {
            var info = GC.GetGCMemoryInfo(GCKind.FullBlocking);
            var live = 0L;

            foreach (var generation in info.GenerationInfo)
            {
                live += generation.SizeAfterBytes - generation.FragmentationAfterBytes;
            }

            return live;
        }

        static double Once(Func<object> build)
        {
            var baseline = Live();
            var peak = baseline;
            var stop = 0;

            var sampler = new Thread(() =>
            {
                var clock = Stopwatch.StartNew();

                while (Volatile.Read(ref stop) == 0)
                {
                    var started = clock.Elapsed;

                    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
                    peak = Math.Max(peak, Survived());

                    var resume = clock.Elapsed + (clock.Elapsed - started);

                    while (clock.Elapsed < resume && Volatile.Read(ref stop) == 0)
                    {
                        Thread.SpinWait(32);
                    }
                }
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
            };

            sampler.Start();

            object? built = null;

            try
            {
                built = build();
            }
            finally
            {
                Volatile.Write(ref stop, 1);
                sampler.Join();
            }

            GC.KeepAlive(built);

            return peak - baseline;
        }

        double[] readings = [Once(build), Once(build), Once(build), Once(build), Once(build)];
        var sorted = readings.Order().ToArray();

        return (sorted[2], readings);
    }
}
