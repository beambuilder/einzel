using System.Text.Json;
using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Einzel.Io;
using Einzel.Project;
using Einzel.Sweeps;

namespace Einzel.Commands;

/// <summary>
/// How much one channel alone moves the figure of merit, in the figure's own unit.
/// </summary>
/// <param name="Parameter">The channel's parameter.</param>
/// <param name="Low">Figure at the low end of its range, or null when the ion did not arrive.</param>
/// <param name="High">Figure at the high end.</param>
/// <param name="Swing">
/// The larger absolute departure from nominal, which is the ranking quantity:
/// what is wanted is which parameter binds first, not which has the steepest slope
/// in some averaged sense.
/// </param>
/// <remarks>
/// The driver ranks in SI, because ranking needs one scale and not a unit. This is
/// the same numbers at the boundary, converted into the unit the figure is
/// reported in - a swing printed as 2.0361E-08 beside a nominal in microseconds is
/// a bare number of the exact kind GRD-1 exists to prevent.
/// </remarks>
public sealed record SweepSensitivity(string Parameter, double? Low, double? High, double Swing);

/// <summary>What a tolerance sweep found.</summary>
public sealed record SweepOutcome
{
    /// <summary>The study file, as an absolute path.</summary>
    public required string StudyPath { get; init; }

    /// <summary>The model that was perturbed, as an absolute path.</summary>
    public required string ModelPath { get; init; }

    /// <summary>Which figure of merit was recorded, and its unit.</summary>
    public required FigureOfMeritInfo FigureOfMerit { get; init; }

    /// <summary>The figure at unperturbed parameters, in the figure's unit.</summary>
    public required double Nominal { get; init; }

    /// <summary>How many draws produced a figure.</summary>
    public required int Succeeded { get; init; }

    /// <summary>How many draws were taken.</summary>
    public required int Draws { get; init; }

    /// <summary>The distribution across the draws, as a GRD-1 envelope.</summary>
    public MeasuredJson? Distribution { get; init; }

    /// <summary>
    /// One-at-a-time attribution, largest swing first.
    /// </summary>
    /// <remarks>
    /// Section 13 calls this "the actual deliverable, since what is wanted is not
    /// only whether 100 to 300 microns suffices but which parameter binds first".
    /// </remarks>
    public required IReadOnlyList<SweepSensitivity> Sensitivity { get; init; }

    /// <summary>
    /// What the draws earned, distinct by code and counted.
    /// </summary>
    /// <remarks>
    /// GRD-2, at the seam a sweep used to lose it: the driver ranks by a bare
    /// double, so a field that missed its tolerance or a mode outside its validity
    /// left no trace on the study. These are the evaluations' warnings, which are
    /// not the same as <see cref="Distribution"/>'s - those are the distribution's
    /// own.
    /// </remarks>
    public IReadOnlyList<Core.Results.ValidityWarning> Warnings { get; init; } = [];

    /// <summary>Files written, relative to the project root.</summary>
    public required IReadOnlyList<string> Artifacts { get; init; }

    /// <summary>
    /// What the study's fields will cost: the channels at the nominal model and the study's
    /// extreme points, how many the cache holds, and the predicted peak. Set by a dry run and
    /// absent from a result, whose manifest records what was actually solved and loaded.
    /// </summary>
    public StudyFieldSurvey? Field { get; init; }
}

/// <summary>One point of a scan, as it is reported.</summary>
/// <param name="Value">The parameter's value there, in the scan's own unit.</param>
/// <param name="FigureOfMerit">The figure in its own unit, or null where none was produced.</param>
/// <param name="Failure">
/// Why none was produced, or null where one was. Kept per point rather than
/// summarised: on a stability scan the reason the figure stops existing is the
/// result, and "the ion was lost on rodYPlus" and "the geometry does not validate"
/// are different findings that would look identical as a blank.
/// </param>
public sealed record ScanRow(double Value, double? FigureOfMerit, string? Failure);

/// <summary>What a scan found.</summary>
public sealed record ScanOutcome
{
    /// <summary>The study file, as an absolute path.</summary>
    public required string StudyPath { get; init; }

    /// <summary>The model that was scanned, as an absolute path.</summary>
    public required string ModelPath { get; init; }

    /// <summary>Which figure of merit was recorded, and its unit.</summary>
    public required FigureOfMeritInfo FigureOfMerit { get; init; }

    /// <summary>Which parameter was varied.</summary>
    public required string Parameter { get; init; }

    /// <summary>The unit the scan's values are quoted in.</summary>
    public required string Unit { get; init; }

    /// <summary>How the points were spaced.</summary>
    public required string Spacing { get; init; }

    /// <summary>The figure at the model's own parameter value, or null.</summary>
    public double? Nominal { get; init; }

    /// <summary>How many points produced a figure.</summary>
    public required int Succeeded { get; init; }

    /// <summary>The curve, one row per point, in scan order (CLI-6).</summary>
    public required IReadOnlyList<ScanRow> Points { get; init; }

    /// <summary>
    /// The adjacent pair the figure changes most between, and how wide that interval
    /// is as a fraction of the whole scan.
    /// </summary>
    /// <remarks>
    /// Deliberately not called a boundary. ACC-6 asks for a boundary resolved to one
    /// part in five hundred of the scan variable, which needs a bisection this does
    /// not do; what this says is where on the grid actually computed the figure moves
    /// fastest, and how coarse that grid is there. A reader can then tell a resolved
    /// transition from a scan too coarse to have found one.
    /// </remarks>
    public ScanTransition? Steepest { get; init; }

    /// <summary>
    /// Warnings the scan carries, per GRD-2 - its own, and every one its points
    /// earned.
    /// </summary>
    public IReadOnlyList<Core.Results.ValidityWarning> Warnings { get; init; } = [];

    /// <summary>Files written, relative to the project root.</summary>
    public required IReadOnlyList<string> Artifacts { get; init; }

    /// <summary>
    /// What the study's fields will cost: the channels at the nominal model and the study's
    /// extreme points, how many the cache holds, and the predicted peak. Set by a dry run and
    /// absent from a result, whose manifest records what was actually solved and loaded.
    /// </summary>
    public StudyFieldSurvey? Field { get; init; }
}

/// <summary>Where a scan's figure changes fastest, on the grid it was computed on.</summary>
/// <param name="Low">Lower end of the interval, in the scan's unit.</param>
/// <param name="High">Upper end.</param>
/// <param name="Change">
/// How much the figure moved across it, in the figure's unit, or null where the
/// figure stopped existing instead of moving.
/// </param>
/// <param name="FigureVanishes">
/// Whether the figure stopped existing across this interval rather than changing by
/// a finite amount. Said as its own flag because JSON has no infinity and a null
/// change would otherwise be indistinguishable from one that was never computed -
/// and on a stability scan the vanishing <em>is</em> the finding.
/// </param>
/// <param name="WidthFraction">
/// How wide the interval is as a fraction of the whole scan - the resolution ACC-6
/// is written in. One part in five hundred needs 501 points.
/// </param>
public sealed record ScanTransition(
    double Low, double High, double? Change, bool FigureVanishes, double WidthFraction);

/// <summary>What a Class B boundary search found.</summary>
public sealed record BoundaryOutcome
{
    /// <summary>The study file, as an absolute path.</summary>
    public required string StudyPath { get; init; }

    /// <summary>The model that was searched, as an absolute path.</summary>
    public required string ModelPath { get; init; }

    /// <summary>Which figure of merit located the boundary, and its unit.</summary>
    public required FigureOfMeritInfo FigureOfMerit { get; init; }

    /// <summary>Which parameter the boundary lies along.</summary>
    public required string Parameter { get; init; }

    /// <summary>The unit the boundary is quoted in.</summary>
    public required string Unit { get; init; }

    /// <summary>The threshold the figure crosses, and which side is inside.</summary>
    public required double Threshold { get; init; }

    /// <summary><c>above</c> or <c>below</c>.</summary>
    public required string Inside { get; init; }

    /// <summary>
    /// The boundary, as a GRD-1 envelope whose interval is the final bracket.
    /// </summary>
    /// <remarks>
    /// Not a value with an error bar: a bisection produces an interval known to
    /// contain the crossing, and the midpoint is a convention laid over it. Quoting
    /// the midpoint without the bracket would be quoting a boundary more precisely
    /// than it was measured.
    /// </remarks>
    public MeasuredJson? Boundary { get; init; }

    /// <summary>Evaluations spent.</summary>
    public required int Evaluations { get; init; }

    /// <summary>
    /// The bracket width as a fraction of the range - ACC-6's own currency.
    /// </summary>
    public required double ResolvedFraction { get; init; }

    /// <summary>Whether the requested resolution was reached.</summary>
    public required bool Converged { get; init; }

    /// <summary>
    /// How many points outside the bracket were tested for a second crossing.
    /// </summary>
    public int Probes { get; init; }

    /// <summary>
    /// Where the figure crossed the threshold again, in SI, or null if it never did.
    /// </summary>
    /// <remarks>
    /// A bisection cannot contradict its own assumption of a single crossing, so
    /// this is the only part of the result that carries evidence about whether the
    /// located edge is the edge of the region or one of several.
    /// </remarks>
    public double? SecondCrossingSi { get; init; }

    /// <summary>Warnings the search carries, and every one its evaluations earned.</summary>
    public IReadOnlyList<Core.Results.ValidityWarning> Warnings { get; init; } = [];

    /// <summary>Files written, relative to the project root.</summary>
    public required IReadOnlyList<string> Artifacts { get; init; }

    /// <summary>
    /// What the study's fields will cost: the channels at the nominal model and the study's
    /// extreme points, how many the cache holds, and the predicted peak. Set by a dry run and
    /// absent from a result, whose manifest records what was actually solved and loaded.
    /// </summary>
    public StudyFieldSurvey? Field { get; init; }
}

/// <summary>What an optimisation found.</summary>
public sealed record OptimiseOutcome
{
    /// <summary>The study file, as an absolute path.</summary>
    public required string StudyPath { get; init; }

    /// <summary>The model that was searched, as an absolute path.</summary>
    public required string ModelPath { get; init; }

    /// <summary>Which figure of merit was optimised, and its unit.</summary>
    public required FigureOfMeritInfo FigureOfMerit { get; init; }

    /// <summary>Which search ran.</summary>
    public required string Algorithm { get; init; }

    /// <summary>Which way was better.</summary>
    public required string Sense { get; init; }

    /// <summary>The optimum, one envelope per variable.</summary>
    public required IReadOnlyDictionary<string, MeasuredJson> Best { get; init; }

    /// <summary>The figure at the optimum, as a GRD-1 envelope.</summary>
    public required MeasuredJson Objective { get; init; }

    /// <summary>Objective evaluations spent.</summary>
    public required int Evaluations { get; init; }

    /// <summary>Evaluations that produced no figure of merit.</summary>
    public required int Failures { get; init; }

    /// <summary>Whether the search met its tolerance rather than its budget.</summary>
    public required bool Converged { get; init; }

    /// <summary>Every improvement on the best-so-far, in order.</summary>
    public required IReadOnlyList<OptimisationStep> History { get; init; }

    /// <summary>
    /// What the objective evaluations earned, distinct by code and counted.
    /// </summary>
    /// <remarks>
    /// GRD-2 at the same seam a sweep loses it, and it matters more here: an
    /// optimiser walks towards whatever scores best, so a corner of the box where
    /// the solve stops converging is somewhere it will actively go.
    /// </remarks>
    public IReadOnlyList<Core.Results.ValidityWarning> Warnings { get; init; } = [];

    /// <summary>Files written, relative to the project root.</summary>
    public required IReadOnlyList<string> Artifacts { get; init; }

    /// <summary>
    /// What the study's fields will cost: the channels at the nominal model and the study's
    /// extreme points, how many the cache holds, and the predicted peak. Set by a dry run and
    /// absent from a result, whose manifest records what was actually solved and loaded.
    /// </summary>
    public StudyFieldSurvey? Field { get; init; }
}

/// <summary>
/// Runs a study file: a tolerance sweep or an optimisation.
/// </summary>
/// <remarks>
/// <para>
/// The drivers in Einzel.Sweeps take a function from a validated model to a
/// number, which is what keeps them device-agnostic. A file cannot carry a
/// function, so a study names one out of <see cref="FiguresOfMerit"/> - and that
/// registry is the seam section 12's Python objectives will register into without
/// the drivers changing.
/// </para>
/// <para>
/// Both write their result beside the study as JSON. A sweep that has to be re-run
/// to be read is a sweep nobody reads twice.
/// </para>
/// </remarks>
public static class StudyCommand
{
    /// <summary>
    /// Describes a figure of merit, whether the engine computes it or an extension
    /// does.
    /// </summary>
    /// <remarks>
    /// An extension objective has no unit the engine can know, so it is reported
    /// dimensionless and under its own name. That is honest rather than lazy: the
    /// extension returns a bare number, and inventing a unit for it here would be
    /// the platform asserting something only the extension author knows.
    /// </remarks>
    private static FigureOfMeritInfo Figure(string name) =>
        ExtensionObjective.Names(name)
            ? new FigureOfMeritInfo(
                name,
                "1",
                $"Computed by extension '{name[ExtensionObjective.Prefix.Length..]}', not by the engine.",
                false)
            : FiguresOfMerit.Describe(name);

    /// <summary>Builds the evaluator a sweep or an optimiser drives.</summary>
    private static Func<CompiledModel, double?> Evaluate(
        string name,
        ProjectLayout project,
        double energySpread,
        int ions,
        WarningLedger ledger,
        StudyFieldRun fields,
        out ExtensionObjective.Provenance? provenance)
    {
        // THE SINK IS PER EVALUATION, NOT PER STUDY. Baking `ledger.Add` into one
        // evaluator made every evaluation report into one shared scope, which is right
        // only while they run strictly one after another: concurrently, the dedup that
        // makes the unit "one evaluation" instead of "one emission" would suppress one
        // evaluation's warning because another had just earned the same code.
        //
        // Rebuilding the evaluator per evaluation costs a name lookup and a delegate,
        // which is nothing against the field build and the flights every evaluation makes.
        Func<EvaluationWarnings, Func<CompiledModel, double?>> build;
        var store = fields.Store;

        if (!ExtensionObjective.Names(name))
        {
            provenance = null;
            build = evaluation =>
                FiguresOfMerit.Evaluator(name, energySpread, ions, evaluation.Add, store);
        }
        else
        {
            var extension = ExtensionObjective.Evaluator(
                name, project.Extensions, Path.Combine(project.Scratch, "ext"),
                out var used, energySpread, ions, store);

            provenance = used;

            // An extension reports nothing to the ledger, so it needs no sink and is
            // built once - it launches a process, which is not free to reconstruct.
            build = _ => extension;
        }

        // One evaluation is one draw however many warnings it emitted, and the
        // count is closed even when the evaluation throws - a study that dies
        // half way still has to say what it saw on the way. The first evaluation's
        // channels are pinned in the study's memo however it ended.
        return model =>
        {
            var evaluation = ledger.BeginEvaluation();
            fields.Record(model);

            try
            {
                return build(evaluation)(model);
            }
            finally
            {
                evaluation.Close();
                fields.Evaluated();
            }
        };
    }

    /// <summary>
    /// Writes the manifest a study result references.
    /// </summary>
    /// <remarks>
    /// GRD-7: every result references a manifest. Studies wrote results and no
    /// manifest at all until this, which is the requirement missing rather than
    /// merely thin - a sweep is exactly the operation whose thousand draws are
    /// worth being able to regenerate, and nothing recorded what produced them.
    /// </remarks>
    private static string WriteManifest(
        ProjectLayout project,
        string studyPath,
        string kind,
        string modelPath,
        string modelText,
        string schemaVersion,
        string transportMode,
        long seed,
        ExtensionObjective.Provenance? extension,
        DateTimeOffset timestampUtc,
        IReadOnlyDictionary<string, string> inputHashes,
        StudyFieldRun fields,
        IReadOnlyList<double[]>? points,
        int maxParallelism)
    {
        var (solves, uses) = fields.Union(points);

        var manifest = new RunManifest
        {
            ModelHash = ContentHash.OfText(modelText),
            InputHashes = RunInputs.Checked(inputHashes, project.Root),

            // A study's result is about the model the study names. Recorded, so verify
            // identifies it by name and uses the hash only to decide whether it moved -
            // rather than the other way round, which loses a drift signal when two models
            // hold the same content.
            ModelPath = RunManifest.Portable(
                Path.GetRelativePath(project.Root, modelPath)),
            SchemaVersion = schemaVersion,
            EngineVersion = EngineBuild.Version,
            SolverBehaviourVersion = EngineBuild.SolverBehaviourVersion,
            TransportMode = transportMode,
            ComputePath = EngineBuild.ComputePath,

            // Every key an evaluation built its field from and what became of each channel in
            // the cache, in point order; how many evaluations could run at once; and the
            // seconds spent loading and solving channels.
            Solves = solves,
            FieldCache = uses,
            MaxParallelism = maxParallelism,
            FieldSeconds = fields.Memo.FieldSeconds,
            Seeds = [seed],
            Extensions = extension is null ? [] : [extension.Identity],
            Interpreter = extension?.Interpreter,
            Machine = Environment.MachineName,
            CreatedUtc = timestampUtc.ToUniversalTime().ToString("O"),
        };

        Directory.CreateDirectory(project.Results);

        var stem = Path.GetFileNameWithoutExtension(studyPath);
        var path = Path.Combine(project.Results, $"{stem}.{kind}.manifest.json");

        File.WriteAllText(path, manifest.ToJson());

        return Path.GetRelativePath(project.Root, path);
    }

    private static readonly JsonSerializerOptions Reading = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Reads a study file.</summary>
    /// <param name="studyPath">Path to the study.</param>
    /// <returns>The study, and the model it names, as absolute paths.</returns>
    /// <exception cref="ArgumentException"><paramref name="studyPath"/> is null or blank.</exception>
    /// <exception cref="EinzelException">The file is not a study, or names no model.</exception>
    public static (StudyDocument Study, string ModelPath, string StudyPath) Load(string studyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studyPath);

        var absolute = Path.GetFullPath(studyPath);
        StudyDocument? study;

        try
        {
            study = JsonSerializer.Deserialize<StudyDocument>(File.ReadAllText(absolute), Reading);
        }
        catch (JsonException failure)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/",
                Constraint = $"{absolute} is not valid JSON: {failure.Message}",
                Suggestion = "a study is a JSON document; run 'einzel schema --study' for its shape",
            });
        }

        if (study is null)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/",
                Constraint = $"{absolute} is empty",
                Suggestion = "run 'einzel schema --study' for the shape of a study file",
            });
        }

        if (string.IsNullOrWhiteSpace(study.Model))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/model",
                Constraint = "a study names the model it studies",
                Suggestion = "add \"model\": \"../models/yours.json\", relative to this study file",
            });
        }

        // A budget in a unit that is not memory is refused here, before anything is solved.
        _ = StudyBinding.MemoryBudget(study);

        // Relative to the study, not to the working directory: a study travels
        // with the project and should mean the same thing from anywhere in it.
        var modelPath = Path.GetFullPath(
            Path.Combine(Path.GetDirectoryName(absolute) ?? ".", study.Model));

        return (study, modelPath, absolute);
    }

    /// <summary>Runs a tolerance sweep.</summary>
    /// <param name="studyPath">Path to the study file.</param>
    /// <param name="project">Where artifacts belong.</param>
    /// <param name="dryRun">Report what would be done and compute nothing (CLI-4).</param>
    /// <param name="store">
    /// Where the study's memo loads solved channels from and keeps them, or null to solve every
    /// one. A study whose points keep the nominal model's channels solves them once, if at all,
    /// and every point shares them in memory.
    /// </param>
    /// <param name="announce">Where the study's diagnostics go: its memory warning and its field line.</param>
    /// <param name="availableMemoryBytes">
    /// What the process may use, for the memory warning; this machine's when omitted.
    /// </param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="studyPath"/> is null or blank.</exception>
    /// <exception cref="EinzelException">The study or the model does not validate.</exception>
    public static SweepOutcome Sweep(
        string studyPath,
        ProjectLayout project,
        bool dryRun = false,
        ISolveStore? store = null,
        Action<string>? announce = null,
        long? availableMemoryBytes = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var (study, modelPath, absolute) = Load(studyPath);
        var figure = Figure(Required(study.FigureOfMerit));
        var channels = StudyBinding.Channels(study);
        var modelText = File.ReadAllText(modelPath);
        var document = ModelJson.Parse(modelText);
        var survey = Survey(study, document, modelPath, project, store, announce, availableMemoryBytes);

        if (dryRun)
        {
            return new SweepOutcome
            {
                StudyPath = absolute,
                ModelPath = modelPath,
                FigureOfMerit = figure,
                Nominal = double.NaN,
                Succeeded = 0,
                Draws = study.Draws,
                Sensitivity = [],
                Artifacts = [],
                Field = survey,
            };
        }

        var inputHashes = RunInputs.Capture(document, modelPath, project.Root, absolute);
        var ledger = new WarningLedger();

        var fields = new StudyFieldRun(store, study, channels.Select(channel => channel.Parameter));
        var evaluate = Evaluate(
            figure.Name, project, study.EnergySpread, study.Ions, ledger, fields, out var extension);

        var result = ToleranceStudy.Run(
            document,
            channels,
            evaluate,
            study.Draws,
            study.Seed,
            study.OneAtATime,
            figure.Dimension,
            Path.GetDirectoryName(modelPath),
            study.MaxParallelism);

        announce?.Invoke(fields.Line());

        // The sweep's own order of points: its draws, then each channel's two extremes.
        var nominal = fields.Nominal;
        var surface = ModelValidator.Validate(document, null, Path.GetDirectoryName(modelPath)).Model?.Parameters;
        List<double[]> points = [.. result.Draws.Select(draw => fields.Values(nominal, draw.Parameters))];

        if (study.OneAtATime && surface is not null && nominal.Length > 0)
        {
            foreach (var channel in channels.Where(channel => surface.Parameters.ContainsKey(channel.Parameter)))
            {
                var (low, high) = channel.Extremes(surface[channel.Parameter]);
                points.Add(fields.Values(nominal, new Dictionary<string, Core.Units.Quantity> { [channel.Parameter] = low }));
                points.Add(fields.Values(nominal, new Dictionary<string, Core.Units.Quantity> { [channel.Parameter] = high }));
            }
        }

        // One conversion from SI into the figure's unit, applied to everything the
        // sweep reports, so nothing leaves here as a bare number under a label it
        // does not match.
        var scale = 1.0 / Core.Units.Quantity.From(1.0, figure.Unit).SiValue;

        var outcome = new SweepOutcome
        {
            StudyPath = absolute,
            ModelPath = modelPath,
            FigureOfMerit = figure,
            Nominal = result.Nominal * scale,
            Succeeded = result.Succeeded,
            Draws = result.Draws.Count,
            Distribution = result.Distribution is { } distribution
                ? MeasuredJson.From(distribution, figure.Unit)
                : null,
            Sensitivity = [.. result.Sensitivity.Select(c => new SweepSensitivity(
                c.Parameter, c.Low * scale, c.High * scale, c.Swing * scale))],
            Warnings = ledger.Collected,
            Artifacts = [],
        };

        return outcome with
        {
            Artifacts =
            [
                Write(project, absolute, "sweep", outcome),
                WriteManifest(
                    project, absolute, "sweep", modelPath, modelText,
                    document.SchemaVersion,
                    document.Transport?.Mode ?? "trajectory", study.Seed, extension,
                    DateTimeOffset.UtcNow, inputHashes,
                    fields, points, Math.Max(1, study.MaxParallelism ?? Environment.ProcessorCount)),
            ],
        };
    }

    /// <summary>Runs a scan.</summary>
    /// <param name="studyPath">Path to the study file.</param>
    /// <param name="project">Where artifacts belong.</param>
    /// <param name="dryRun">Report what would be done and compute nothing (CLI-4).</param>
    /// <param name="store">
    /// Where the study's memo loads solved channels from and keeps them, or null to solve every
    /// one. A study whose points keep the nominal model's channels solves them once, if at all,
    /// and every point shares them in memory.
    /// </param>
    /// <param name="announce">Where the study's diagnostics go: its memory warning and its field line.</param>
    /// <param name="availableMemoryBytes">
    /// What the process may use, for the memory warning; this machine's when omitted.
    /// </param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="studyPath"/> is null or blank.</exception>
    /// <exception cref="EinzelException">The study or the model does not validate.</exception>
    /// <remarks>
    /// The operation every curve in this engine has so far been produced by a loop in
    /// a test file: the low-mass cut-off scans, the extraction-slot scan, the drift
    /// scan. Written as a study it gets what those did not - a manifest, a result
    /// file beside the model, and a form an agent can author.
    /// </remarks>
    public static ScanOutcome Scan(
        string studyPath,
        ProjectLayout project,
        bool dryRun = false,
        ISolveStore? store = null,
        Action<string>? announce = null,
        long? availableMemoryBytes = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var (study, modelPath, absolute) = Load(studyPath);
        var figure = Figure(Required(study.FigureOfMerit));
        var axis = StudyBinding.Axis(study);
        var modelText = File.ReadAllText(modelPath);
        var document = ModelJson.Parse(modelText);
        var survey = Survey(study, document, modelPath, project, store, announce, availableMemoryBytes);

        var unit = study.Scan!.Unit!;
        var spacing = axis.Spacing.ToString().ToLowerInvariant();

        if (dryRun)
        {
            return new ScanOutcome
            {
                StudyPath = absolute,
                ModelPath = modelPath,
                FigureOfMerit = figure,
                Parameter = axis.Parameter,
                Unit = unit,
                Spacing = spacing,
                Succeeded = 0,
                Points = [],
                Artifacts = [],
                Field = survey,
            };
        }

        var inputHashes = RunInputs.Capture(document, modelPath, project.Root, absolute);
        var ledger = new WarningLedger();

        var fields = new StudyFieldRun(store, study, [axis.Parameter]);
        var evaluate = Evaluate(
            figure.Name, project, study.EnergySpread, study.Ions, ledger, fields, out var extension);

        var result = ParameterScan.Run(
            document, axis, evaluate, Path.GetDirectoryName(modelPath),
            study.MaxParallelism);

        announce?.Invoke(fields.Line());

        // One conversion out of SI for the figure and one for the scan variable, so
        // nothing leaves here as a bare number under a label it does not match.
        var figureScale = 1.0 / Core.Units.Quantity.From(1.0, figure.Unit).SiValue;
        var axisScale = 1.0 / Core.Units.Quantity.From(1.0, unit).SiValue;

        var span = Math.Abs(axis.To.SiValue - axis.From.SiValue);

        var outcome = new ScanOutcome
        {
            StudyPath = absolute,
            ModelPath = modelPath,
            FigureOfMerit = figure,
            Parameter = axis.Parameter,
            Unit = unit,
            Spacing = spacing,
            Nominal = result.Nominal * figureScale,
            Succeeded = result.Succeeded,
            Points =
            [
                .. result.Points.Select(p => new ScanRow(
                    p.ValueSi * axisScale, p.FigureOfMerit * figureScale, p.Failure)),
            ],
            Steepest = result.SteepestInterval is { } steepest && span > 0.0
                ? new ScanTransition(
                    steepest.LowSi * axisScale,
                    steepest.HighSi * axisScale,
                    double.IsPositiveInfinity(steepest.Change) ? null : steepest.Change * figureScale,
                    double.IsPositiveInfinity(steepest.Change),
                    Math.Abs(steepest.HighSi - steepest.LowSi) / span)
                : null,
            Warnings = [.. result.Warnings, .. ledger.Collected],
            Artifacts = [],
        };

        return outcome with
        {
            Artifacts =
            [
                Write(project, absolute, "scan", outcome),
                WriteManifest(
                    project, absolute, "scan", modelPath, modelText,
                    document.SchemaVersion,
                    document.Transport?.Mode ?? "trajectory", study.Seed, extension,
                    DateTimeOffset.UtcNow, inputHashes,
                    fields, [.. Enumerable.Range(0, axis.Points).Select(index => new[] { axis.At(index).SiValue })],
                    Math.Max(1, study.MaxParallelism ?? Environment.ProcessorCount)),
            ],
        };
    }

    /// <summary>Locates a Class B boundary by bisection.</summary>
    /// <param name="studyPath">Path to the study file.</param>
    /// <param name="project">Where artifacts belong.</param>
    /// <param name="dryRun">Report what would be done and compute nothing (CLI-4).</param>
    /// <param name="store">
    /// Where the study's memo loads solved channels from and keeps them, or null to solve every
    /// one. A study whose points keep the nominal model's channels solves them once, if at all,
    /// and every point shares them in memory.
    /// </param>
    /// <param name="announce">Where the study's diagnostics go: its memory warning and its field line.</param>
    /// <param name="availableMemoryBytes">
    /// What the process may use, for the memory warning; this machine's when omitted.
    /// </param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="studyPath"/> is null or blank.</exception>
    /// <exception cref="EinzelException">The study or the model does not validate.</exception>
    /// <remarks>
    /// The half of §12's Class B that a scan cannot do. A scan says which of its
    /// intervals the figure moves fastest across; this says where the crossing is,
    /// to ACC-6's one part in five hundred, for about eleven evaluations instead of
    /// five hundred and one.
    /// </remarks>
    public static BoundaryOutcome Boundary(
        string studyPath,
        ProjectLayout project,
        bool dryRun = false,
        ISolveStore? store = null,
        Action<string>? announce = null,
        long? availableMemoryBytes = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var (study, modelPath, absolute) = Load(studyPath);
        var figure = Figure(Required(study.FigureOfMerit));
        var (axis, threshold, sense, resolution, budget) = StudyBinding.Boundary(study);
        var modelText = File.ReadAllText(modelPath);
        var document = ModelJson.Parse(modelText);
        var survey = Survey(study, document, modelPath, project, store, announce, availableMemoryBytes);

        var unit = study.Boundary!.Unit!;
        var inside = sense.ToString().ToLowerInvariant();

        if (dryRun)
        {
            return new BoundaryOutcome
            {
                StudyPath = absolute,
                ModelPath = modelPath,
                FigureOfMerit = figure,
                Parameter = axis.Parameter,
                Unit = unit,
                Threshold = threshold,
                Inside = inside,
                Evaluations = 0,
                ResolvedFraction = double.NaN,
                Converged = false,
                Artifacts = [],
                Field = survey,
            };
        }

        var inputHashes = RunInputs.Capture(document, modelPath, project.Root, absolute);
        var ledger = new WarningLedger();

        var fields = new StudyFieldRun(store, study, [axis.Parameter]);
        var evaluate = Evaluate(
            figure.Name, project, study.EnergySpread, study.Ions, ledger, fields, out var extension);

        var result = BoundarySearch.Run(
            document,
            axis,
            evaluate,
            threshold,
            sense,
            resolution,
            budget,
            Path.GetDirectoryName(modelPath));

        announce?.Invoke(fields.Line());

        var outcome = new BoundaryOutcome
        {
            StudyPath = absolute,
            ModelPath = modelPath,
            FigureOfMerit = figure,
            Parameter = axis.Parameter,
            Unit = unit,
            Threshold = threshold,
            Inside = inside,
            Boundary = MeasuredJson.From(result.Boundary, unit),
            Evaluations = result.Evaluations,
            ResolvedFraction = result.ResolvedFraction,
            Converged = result.MetAccuracyTarget,
            Probes = result.Probes,
            SecondCrossingSi = result.SecondCrossingSi,
            Warnings = [.. result.Warnings, .. ledger.Collected],
            Artifacts = [],
        };

        return outcome with
        {
            Artifacts =
            [
                Write(project, absolute, "boundary", outcome),
                WriteManifest(
                    project, absolute, "boundary", modelPath, modelText,
                    document.SchemaVersion,
                    document.Transport?.Mode ?? "trajectory", study.Seed, extension,
                    DateTimeOffset.UtcNow, inputHashes,
                    fields, null, 1),
            ],
        };
    }

    /// <summary>Runs an optimisation.</summary>
    /// <param name="studyPath">Path to the study file.</param>
    /// <param name="project">Where artifacts belong.</param>
    /// <param name="dryRun">Report what would be done and compute nothing (CLI-4).</param>
    /// <param name="store">
    /// Where the study's memo loads solved channels from and keeps them, or null to solve every
    /// one. A study whose points keep the nominal model's channels solves them once, if at all,
    /// and every point shares them in memory.
    /// </param>
    /// <param name="announce">Where the study's diagnostics go: its memory warning and its field line.</param>
    /// <param name="availableMemoryBytes">
    /// What the process may use, for the memory warning; this machine's when omitted.
    /// </param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="studyPath"/> is null or blank.</exception>
    /// <exception cref="EinzelException">The study or the model does not validate.</exception>
    public static OptimiseOutcome Optimise(
        string studyPath,
        ProjectLayout project,
        bool dryRun = false,
        ISolveStore? store = null,
        Action<string>? announce = null,
        long? availableMemoryBytes = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var (study, modelPath, absolute) = Load(studyPath);
        var figure = Figure(Required(study.FigureOfMerit));
        var variables = StudyBinding.Variables(study);
        var algorithm = StudyBinding.Algorithm(study);
        var sense = StudyBinding.Sense(study, figure);
        var modelText = File.ReadAllText(modelPath);
        var document = ModelJson.Parse(modelText);
        var survey = Survey(study, document, modelPath, project, store, announce, availableMemoryBytes);

        if (dryRun)
        {
            return new OptimiseOutcome
            {
                StudyPath = absolute,
                ModelPath = modelPath,
                FigureOfMerit = figure,
                Algorithm = algorithm.ToString(),
                Sense = sense.ToString(),
                Best = new Dictionary<string, MeasuredJson>(StringComparer.Ordinal),
                Objective = MeasuredJson.From(Empty(figure), figure.Unit),
                Evaluations = 0,
                Failures = 0,
                Converged = false,
                History = [],
                Artifacts = [],
                Field = survey,
            };
        }

        var inputHashes = RunInputs.Capture(document, modelPath, project.Root, absolute);
        var ledger = new WarningLedger();

        var fields = new StudyFieldRun(store, study, variables.Select(variable => variable.Parameter));

        var result = Optimiser.Run(
            document,
            variables,
            Evaluate(figure.Name, project, study.EnergySpread, study.Ions, ledger, fields, out var extension),
            sense,
            algorithm,
            new OptimisationSettings
            {
                MaximumEvaluations = study.MaximumEvaluations,
                ParameterTolerance = study.ParameterTolerance,
                ObjectiveTolerance = study.ObjectiveTolerance,
                ObjectiveDimension = figure.Dimension,
                Seed = study.Seed,
            },
            Path.GetDirectoryName(modelPath));

        announce?.Invoke(fields.Line());

        var best = new Dictionary<string, MeasuredJson>(StringComparer.Ordinal);

        foreach (var (name, measured) in result.Best.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            // Reported in the parameter's own dimension. A length that came back
            // in metres when the model was written in millimetres is technically
            // correct and practically useless.
            best[name] = MeasuredJson.From(measured, UnitFor(measured));
        }

        var outcome = new OptimiseOutcome
        {
            StudyPath = absolute,
            ModelPath = modelPath,
            FigureOfMerit = figure,
            Algorithm = algorithm.ToString(),
            Sense = sense.ToString(),
            Best = best,
            Objective = MeasuredJson.From(result.Objective, figure.Unit),
            Evaluations = result.Evaluations,
            Failures = result.Failures,
            Converged = result.Converged,
            History = result.History,
            Warnings = ledger.Collected,
            Artifacts = [],
        };

        return outcome with
        {
            Artifacts =
            [
                Write(project, absolute, "optimise", outcome),
                WriteManifest(
                    project, absolute, "optimise", modelPath, modelText,
                    document.SchemaVersion,
                    document.Transport?.Mode ?? "trajectory", study.Seed, extension,
                    DateTimeOffset.UtcNow, inputHashes,
                    fields, null, 1),
            ],
        };
    }

    /// <summary>
    /// Surveys a study's fields before it starts, and warns when its predicted peak will not
    /// fit in what this process may use.
    /// </summary>
    /// <remarks>
    /// Null when the survey cannot be made - a model that does not compile, a study the driver
    /// will refuse - so that the driver, not the survey, reports what is wrong with it.
    /// </remarks>
    private static StudyFieldSurvey? Survey(
        StudyDocument study,
        ModelDocument document,
        string modelPath,
        ProjectLayout project,
        ISolveStore? store,
        Action<string>? announce,
        long? availableMemoryBytes)
    {
        StudyFieldSurvey? survey;

        try
        {
            survey = StudyFields.Survey(
                study, document, Path.GetDirectoryName(modelPath), store is FieldCache ? project : null);
        }
        catch (EinzelException)
        {
            return null;
        }

        var available = availableMemoryBytes ?? GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

        if (survey is not null && available > 0 && survey.PeakMiB * 1024.0 * 1024.0 > available)
        {
            announce?.Invoke(
                "  warning: " + EstimateCommand.CapacityNote(survey.PeakMiB, available, "this study's")
                + (survey.PointChannels > 0 && survey.MaxParallelism > 1
                    ? " Its points solve channels of their own, so a lower maxParallelism lowers it."
                    : string.Empty));
        }

        return survey;
    }

    /// <summary>An empty envelope, for a dry run that computed nothing.</summary>
    private static Core.Results.Measured Empty(FigureOfMeritInfo figure)
    {
        var zero = Core.Units.Quantity.From(0.0, figure.Unit);

        return new Core.Results.Measured(
            zero,
            Core.Results.UncertaintyInterval.Symmetric(zero, zero, 1.0),
            new Core.Results.Evidence.Search(0, Converged: false, SpreadSi: 0.0),
            [
                new Core.Results.ValidityWarning(
                    "study.dry-run",
                    "nothing was computed: this is what the study would do, not what it found",
                    Core.Results.WarningSeverity.Qualified),
            ]);
    }

    /// <summary>A display unit for a parameter's dimension.</summary>
    /// <remarks>
    /// Millimetres for a length and volts for a potential, because that is what
    /// the models are written in; anything else falls back to coherent SI, which
    /// the envelope can always render.
    /// </remarks>
    private static string UnitFor(Core.Results.Measured measured)
    {
        if (measured.Dimension == Core.Units.Quantity.From(1.0, "mm").Dimension)
        {
            return "mm";
        }

        if (measured.Dimension == Core.Units.Quantity.From(1.0, "V").Dimension)
        {
            return "V";
        }

        return "1";
    }

    private static string Required(string? figureOfMerit)
    {
        if (!string.IsNullOrWhiteSpace(figureOfMerit))
        {
            return figureOfMerit;
        }

        throw new EinzelException(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = "/figureOfMerit",
            Constraint = "a study says which figure of merit it records",
            Suggestion = $"one of: {string.Join(", ", FiguresOfMerit.All.Select(f => f.Name))}",
        });
    }

    private static string Write<T>(ProjectLayout project, string studyPath, string kind, T outcome)
    {
        Directory.CreateDirectory(project.Results);

        var stem = Path.GetFileNameWithoutExtension(studyPath);
        var path = Path.Combine(project.Results, $"{stem}.{kind}.json");
        File.WriteAllText(path, CommandJson.Write(outcome));

        return Path.GetRelativePath(project.Root, path);
    }
}
