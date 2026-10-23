using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Io.IonLists;
using Einzel.Project;
using Einzel.Transport;
using Einzel.Transport.Collisions;
using Einzel.Transport.Integration;

namespace Einzel.Commands;

/// <summary>One ion of a <c>fly</c>, as written to its result.</summary>
/// <param name="Id">The row's id.</param>
/// <param name="Tag">The row's tag; empty when none.</param>
/// <param name="Outcome">The integrator's outcome name.</param>
/// <param name="Surface">The electrode struck, by name, or null.</param>
/// <param name="XMm">Where the flight ended, x in millimetres.</param>
/// <param name="YMm">Where the flight ended, y in millimetres.</param>
/// <param name="ZMm">Where the flight ended, z in millimetres.</param>
/// <param name="TimeUs">The flight time in microseconds, counted from the ion's birth.</param>
/// <param name="TimeHalfWidthUs">The ladder's half-width on it; absent without the ladder.</param>
/// <param name="Steps">Accepted integration steps.</param>
/// <param name="Rejected">Rejected integration steps.</param>
/// <param name="Collisions">Collisions the reported flight made; zero in vacuum.</param>
/// <param name="MaximumRelativeEnergyDrift">The flight's largest relative energy drift; absent when not measurable.</param>
/// <param name="Warnings">The codes of the warnings this ion's flight earned.</param>
/// <remarks>
/// <see cref="TimeUs"/> is the flight time in seconds times 10^6, as a cloud's events in a
/// <c>run</c> result report it. A single-ion <c>run</c> converts its flight time through
/// the unit system instead, and the two can differ in the last place.
/// </remarks>
public sealed record FlyIonJson(
    int Id, string Tag, string Outcome, string? Surface, double XMm, double YMm, double ZMm, double TimeUs,
    double? TimeHalfWidthUs, int Steps, int Rejected, int Collisions, double MaximumRelativeEnergyDrift,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// The override group the row flew in (the manifest's <c>overrideGroups</c>); absent for a
    /// list without overrides.
    /// </summary>
    public int? Group { get; init; }
}

/// <summary>What <c>einzel fly</c> did: every row of an ion list, flown through one model.</summary>
/// <remarks>
/// Per-ion outcomes are data, as a cloud's are: an ion on an electrode is a result. The
/// counts are what a caller reads first, and <see cref="Ions"/> is the whole answer, in
/// list order.
/// </remarks>
public sealed record FlyOutcome
{
    /// <summary>The manifest, as written (or, for a dry run, as it would be).</summary>
    public required RunManifest Manifest { get; init; }

    /// <summary>The ion list, relative to the project root.</summary>
    public required string IonsPath { get; init; }

    /// <summary>Whether this only said what would be flown.</summary>
    public required bool DryRun { get; init; }

    /// <summary>How many rows the list expanded to.</summary>
    public required int Launched { get; init; }

    /// <summary>Rows per outcome name.</summary>
    public required IReadOnlyDictionary<string, int> Outcomes { get; init; }

    /// <summary>Rows per struck surface.</summary>
    public required IReadOnlyDictionary<string, int> Surfaces { get; init; }

    /// <summary>Rows whose integration gave up rather than ending anywhere.</summary>
    public required int IntegratorFailures { get; init; }

    /// <summary>How many rows could fly at once. A cost: the rows are the same at any setting.</summary>
    public required int MaxParallelism { get; init; }

    /// <summary>Whether each row ran the three-rung tolerance ladder.</summary>
    public required bool Ladder { get; init; }

    /// <summary>Seconds until the field was ready: solved, or loaded from the cache; summed over override groups.</summary>
    public double? FieldReadySeconds { get; init; }

    /// <summary>Seconds the rows took to fly; summed over override groups.</summary>
    public double? FlySeconds { get; init; }

    /// <summary>Dry run only: the solved channels the field needs, and how many the cache holds.</summary>
    public int? Channels { get; init; }

    /// <summary>Dry run only: how many of <see cref="Channels"/> the cache holds.</summary>
    public int? CachedChannels { get; init; }

    /// <summary>
    /// How many override groups the rows flew in, each through its own field; absent for a
    /// list without overrides. The manifest's <c>overrideGroups</c> says what each was.
    /// </summary>
    public int? Groups { get; init; }

    /// <summary>
    /// The field's and the model's warnings, the gas's regime warnings for the model's own
    /// ion as <c>run</c> gives them (REG-2), and one when an integration gave up (GRD-2).
    /// </summary>
    public required IReadOnlyList<WarningJson> Warnings { get; init; }

    /// <summary>Every row, in list order.</summary>
    public required IReadOnlyList<FlyIonJson> Ions { get; init; }

    /// <summary>The files written, relative to the project root, this result among them.</summary>
    public required IReadOnlyList<string> Artifacts { get; init; }

    /// <summary>
    /// The exit code this outcome maps to, as <c>run</c>'s (CLI-3): 2 when a regime warning
    /// is a validity violation, which outranks 4 when an integration gave up; else 0.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public ExitCode ExitCode =>
        Warnings.Any(w => w.Code.StartsWith("regime.", StringComparison.Ordinal)
            && w.Severity == nameof(WarningSeverity.ValidityViolation))
            ? ExitCode.RegimeViolation
            : IntegratorFailures > 0 ? ExitCode.ConvergenceFailure : ExitCode.Success;
}

/// <summary>How <see cref="FlyCommand.Execute"/> runs.</summary>
public sealed record FlyOptions
{
    /// <summary>Validate and expand, say what would be flown, write nothing.</summary>
    public bool DryRun { get; init; }

    /// <summary>Run the three-rung tolerance ladder per row.</summary>
    public bool Ladder { get; init; }

    /// <summary>Write every row's path as one polyline in a VTU file under <c>.einzel/</c>.</summary>
    public bool ExportVtu { get; init; }

    /// <summary>The project's field cache, or null to solve everything.</summary>
    public FieldCache? FieldCache { get; init; }

    /// <summary>The run timestamp, supplied so the caller owns the clock.</summary>
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Where progress lines go, or null for silence.</summary>
    public Action<string>? Announce { get; init; }
}

/// <summary>Flies an ion list through a model: <c>einzel fly</c>.</summary>
/// <remarks>
/// <para>
/// A separate verb rather than a mode of <c>run</c>, so neither the run result nor its
/// write-count changes: <c>run</c> answers with a laddered flight time for one ion, this
/// with one row per ion. The model is validated as <c>run</c> validates it, the field is
/// built through the same cache, and the rows fly through the core a cloud flies through.
/// </para>
/// <para>
/// The ion list is an input like an imported gas field: its bytes are hashed into the
/// manifest, so <c>verify</c> reports a result whose list was edited since, and the expanded
/// rows are written beside the result as <c>.ions.csv</c>, from which the flight reproduces.
/// </para>
/// <para>
/// Rows that override model parameters fly in groups, one per distinct set of overrides:
/// the model is compiled with the group's values and must differ from the model as written
/// only in its fields, the group's field is built through an in-process memo over the
/// cache, and the group's rows fly by their index in the whole list. Groups fly one after
/// another, the rows of a group at once.
/// </para>
/// </remarks>
public static class FlyCommand
{
    /// <summary>Validates, expands, flies and writes.</summary>
    /// <param name="modelPath">The model.</param>
    /// <param name="ionsPath">The ion list, <c>.ions.json</c> or <c>.csv</c>.</param>
    /// <param name="project">The project the outputs belong to.</param>
    /// <param name="options">How to run.</param>
    /// <returns>
    /// The outcome, or null with the validation failure and the file its pointers address -
    /// the model, or the ion list.
    /// </returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="FileNotFoundException">The model or the ion list does not exist.</exception>
    public static (FlyOutcome? Fly, ValidateOutcome Validation, string ErrorsIn) Execute(
        string modelPath, string ionsPath, ProjectLayout project, FlyOptions options)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);

        var validation = RunCommand.Validate(modelPath);
        var ions = Path.GetFullPath(ionsPath);

        if (!validation.Valid)
        {
            return (null, validation, validation.ModelPath);
        }

        if (!File.Exists(ions))
        {
            throw new FileNotFoundException($"ion list not found: {ions}", ions);
        }

        var document = ModelJson.Parse(File.ReadAllText(validation.ModelPath));
        var directory = Path.GetDirectoryName(validation.ModelPath)!;
        var model = ModelValidator.Validate(document, null, directory).Model!;

        if (Refusals(model) is { Count: > 0 } refused)
        {
            return (null, validation with { Valid = false, Errors = refused }, validation.ModelPath);
        }

        // Hashed before anything reads them (PRJ-3): the manifest records the bytes this
        // flight consumed, and a file that changes before the result is written is caught.
        var inputs = RunInputs.Capture(document, validation.ModelPath, project.Root, ionsPath: ions);
        var expansion = IonList.Load(ions, model);

        if (!expansion.IsValid)
        {
            return (null, validation with { Valid = false, Errors = expansion.Errors }, ions);
        }

        var rows = expansion.Rows!;
        var overridden = expansion.OverrideSets.Count > 0;
        var (groups, groupErrors) = Groups(document, directory, model, expansion);

        if (groupErrors.Count > 0)
        {
            return (null, validation with { Valid = false, Errors = groupErrors }, ions);
        }

        var gas = GasFlowImport.Resolve(model.Gas, directory);
        var parallelism = expansion.MaxParallelism ?? Environment.ProcessorCount;
        var stem = $"{Path.GetFileNameWithoutExtension(validation.ModelPath)}.{IonsStem(ions)}.fly";

        var manifest = new RunManifest
        {
            ModelHash = validation.ModelHash,
            InputHashes = inputs,
            ModelPath = RunManifest.Portable(Path.GetRelativePath(project.Root, validation.ModelPath)),
            SchemaVersion = document.SchemaVersion,
            EngineVersion = EngineBuild.Version,
            SolverBehaviourVersion = EngineBuild.SolverBehaviourVersion,
            Solves = overridden ? Union(groups.Select(group => group.Solves)) : groups[0].Solves,
            TransportMode = model.TransportMode,
            ComputePath = EngineBuild.ComputePath,

            // The list's set seeds, then the gas's: what a reader needs to regenerate the
            // rows and their collisions. The gas seed alone is in the model hash already.
            Seeds = [.. expansion.Seeds, .. gas.IsPresent ? [(long)model.Gas.Seed] : Array.Empty<long>()],
            MaxParallelism = parallelism,
            OverrideGroups = overridden ? [.. groups.Select(group => group.Record)] : null,
            Machine = Environment.MachineName,
            CreatedUtc = options.TimestampUtc.ToUniversalTime().ToString("O"),
        };

        var outcome = new FlyOutcome
        {
            Manifest = manifest,
            IonsPath = RunManifest.Portable(Path.GetRelativePath(project.Root, ions)),
            DryRun = options.DryRun,
            Launched = rows.Count,
            Outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal),
            Surfaces = new SortedDictionary<string, int>(StringComparer.Ordinal),
            IntegratorFailures = 0,
            MaxParallelism = parallelism,
            Ladder = options.Ladder,
            Warnings = [],
            Ions = [],
            Artifacts = [],
            Groups = overridden ? groups.Count : null,
        };

        if (options.DryRun)
        {
            // A channel two groups share is one channel to load or solve.
            var channels = (manifest.Solves ?? []).SelectMany(key => key.ChannelHashes).ToList();
            channels = overridden ? [.. channels.Distinct()] : channels;
            bool Cached(string hash) => options.FieldCache is not null && FieldCache.Find(
                project, EngineBuild.SolverBehaviourVersion, EngineBuild.ComputePath, hash) is not null;

            return (outcome with
            {
                Manifest = manifest with
                {
                    OverrideGroups = manifest.OverrideGroups?.Select(group => group with
                    {
                        CachedChannels = group.ChannelHashes.Count(Cached),
                    }).ToList(),
                },
                Channels = channels.Count,
                CachedChannels = options.FieldCache is null ? 0 : channels.Count(Cached),
            }, validation, validation.ModelPath);
        }

        var (point, normal) = (model.DetectorPoint, model.DetectorNormal);
        TrajectoryStopFunction detector = (in PhaseState state) => Vec3.Dot(state.Position - point, normal);
        var settings = new IntegrationSettings
        {
            RelativeTolerance = model.RelativeTolerance,
            MaximumFlightTime = model.MaximumFlightTimeSi,
        };

        // Each channel is read from the cache, or solved, once per flight however many groups
        // need it; without overrides there is one field and the cache alone, as before.
        var memo = overridden ? new SolveMemo(options.FieldCache) : null;
        var store = memo ?? (Fields.Solved.ISolveStore?)options.FieldCache;
        var flights = new IonFlight[rows.Count];
        var fieldAndRegime = new List<(string Group, ValidityWarning Warning)>();
        var records = new List<OverrideGroup>();
        var (fieldReady, flySeconds) = (0.0, 0.0);
        IReadOnlyList<FieldCacheUse>? uses = null;

        foreach (var group in groups)
        {
            var (loaded, solved, reused) = (memo?.Loaded ?? 0, memo?.Solved ?? 0, memo?.Reused ?? 0);
            var clock = Stopwatch.StartNew();
            var (field, fieldWarnings) = FieldAssembly.BuildReported(group.Model, store);
            var ready = clock.Elapsed.TotalSeconds;

            if (memo is null)
            {
                uses = options.FieldCache?.Uses(manifest.Solves!) is { Count: > 0 } used ? used : null;

                if (uses is not null)
                {
                    var hits = uses.Count(use => use.Status == FieldCacheUse.Hit);
                    options.Announce?.Invoke($"  field: {hits} channel(s) from the cache, {uses.Count - hits} solved");
                }
            }

            clock.Restart();
            var flown = IonListFlight.Fly(
                group.Indices is null ? rows : [.. group.Indices.Select(index => rows[index])],
                field, settings, detector, gas, model.Gas.Seed, parallelism, options.Ladder,
                options.ExportVtu ? () => new TrajectoryRecorder(model.SampleIntervalSi) : null,
                group.Indices);
            var seconds = clock.Elapsed.TotalSeconds;

            for (var k = 0; k < flown.Count; k++)
            {
                flights[group.Indices?[k] ?? k] = flown[k];
            }

            (fieldReady, flySeconds) = (fieldReady + ready, flySeconds + seconds);
            var label = $"group {group.Record.Index} ({group.Record.Describe()})";
            fieldAndRegime.AddRange(((ValidityWarning[])
            [
                .. fieldWarnings, .. RegimeWarnings(group.Model, field, settings, detector, gas),
                .. RunCommand.HalfSpaceDepthWarnings(group.Model),
            ]).Select(warning => (label, warning)));

            if (memo is not null)
            {
                var record = group.Record with
                {
                    Loaded = memo.Loaded - loaded,
                    Solved = memo.Solved - solved,
                    Reused = memo.Reused - reused,
                    FieldReadySeconds = ready,
                };

                records.Add(record);
                options.Announce?.Invoke(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  group {record.Index} ({record.Describe()}): {record.Rows} ion(s); field: {record.Loaded} channel(s) from the cache, "
                    + $"{record.Solved} solved, {record.Reused} already held; ready in {ready:F1} s, flown in {seconds:F1} s"));
            }
        }

        if (memo is not null)
        {
            uses = options.FieldCache?.Uses(manifest.Solves!) is { Count: > 0 } used
                ? [.. used.DistinctBy(use => use.ChannelHash)]
                : null;
        }

        options.Announce?.Invoke(string.Create(
            CultureInfo.InvariantCulture,
            $"  flew {rows.Count} ions in {flySeconds:F1} s on up to {Math.Min(parallelism, Math.Max(1, rows.Count))} thread(s)"));

        // A thrown row is a defect, not an outcome: the integrator throws nothing in flight.
        // Rethrown with its own type and stack, so the CLI reports it as one (exit 6).
        if (flights.FirstOrDefault(f => f.Failure is not null)?.Failure is { } thrown)
        {
            ExceptionDispatchInfo.Throw(thrown);
        }

        var ionRows = flights.Select((flight, k) => Row(flight) with { Group = overridden ? expansion.RowGroups[k] : null }).ToList();
        var failures = flights.Count(f => !f.Result!.Outcome.Completed());

        // Per group, each warning once: thirty groups of one gas give one regime warning, and
        // a group whose voltages leave the regime adds its own, named by its group.
        List<ValidityWarning> warnings =
        [
            .. overridden ? Attributed(fieldAndRegime) : fieldAndRegime.Select(pair => pair.Warning),
            .. RunCommand.ProvenanceWarnings(model),
        ];

        if (failures > 0)
        {
            warnings.Add(new ValidityWarning(
                "fly.integrator-failures",
                $"{failures} of {rows.Count} ion(s) ended because the integrator gave up rather than "
                + "because the ion arrived anywhere; their rows stop part way and carry no bound",
                WarningSeverity.ValidityViolation));
        }

        Directory.CreateDirectory(project.Results);
        string Relative(string path) => Path.GetRelativePath(project.Root, path);

        // The trajectories are large and regenerable, so they go beside the cache, as a run's.
        var manifestPath = Path.Combine(project.Results, $"{stem}.manifest.json");
        var vtuPath = Path.Combine(project.Scratch, $"{stem}.trajectories.vtu");

        var artifacts = new List<string>
        {
            Relative(manifestPath),
            Relative(Path.Combine(project.Results, $"{stem}.json")),
            Relative(Path.Combine(project.Results, $"{stem}.csv")),
            Relative(Path.Combine(project.Results, $"{stem}.ions.csv")),
        };

        if (options.ExportVtu)
        {
            artifacts.Add(Relative(vtuPath));
        }

        // One stem per model and list name, so a.ions.json and a.csv write the same files:
        // a flight of another list replaces the result, and says so. A path left by an
        // earlier --vtu flight is not this flight's path.
        if (OtherList(manifestPath, inputs, RunManifest.Portable(Path.GetRelativePath(project.Root, ions))) is { } other)
        {
            options.Announce?.Invoke(
                $"  warning: {Relative(manifestPath)} held a flight of {other}; this flight of another list replaces it");
        }

        if (!options.ExportVtu && File.Exists(vtuPath))
        {
            File.Delete(vtuPath);
        }

        outcome = outcome with
        {
            Manifest = manifest with
            {
                InputHashes = RunInputs.Checked(inputs, project.Root),
                FieldCache = uses,
                OverrideGroups = overridden ? records : null,
            },
            Outcomes = Count(ionRows.Select(row => row.Outcome)),
            Surfaces = Count(ionRows.Select(row => row.Surface).OfType<string>()),
            IntegratorFailures = failures,
            FieldReadySeconds = fieldReady,
            FlySeconds = flySeconds,
            Warnings = [.. warnings.Select(w => new WarningJson
            {
                Code = w.Code,
                Message = w.Message,
                Severity = w.Severity.ToString(),
                Suppressible = w.IsSuppressible,
            })],
            Ions = ionRows,
            Artifacts = artifacts,
        };

        File.WriteAllText(Path.Combine(project.Root, artifacts[0]), outcome.Manifest.ToJson());
        File.WriteAllText(Path.Combine(project.Root, artifacts[1]), CommandJson.Write(outcome));
        File.WriteAllText(Path.Combine(project.Root, artifacts[2]), Csv(ionRows, expansion.OverrideSets));
        File.WriteAllText(Path.Combine(project.Root, artifacts[3]), IonListCsv.Write(rows, expansion.RowGroups, expansion.OverrideSets));

        if (options.ExportVtu)
        {
            var truncated = flights.Where(f => f.TrajectoryTruncated).Select(f => f.Row.Id).ToList();
            var omitted = flights.Where(f => (f.Trajectory?.Count ?? 0) < 2).Select(f => f.Row.Id).ToList();

            Directory.CreateDirectory(project.Scratch);
            File.WriteAllText(
                vtuPath,
                VtuWriter.WriteTrajectories(
                    [.. flights.Select(f => (f.Row.Id, (int)f.Result!.Outcome, f.Trajectory ?? []))],
                    [
                        $"engine: {EngineBuild.Version}",
                        $"model: {validation.ModelHash}",
                        $"ions: {outcome.IonsPath} {inputs[outcome.IonsPath]}",
                        $"samples every {model.SampleIntervalSi:G6} s of flight; time_us counts from each ion's birth",
                        options.Ladder ? "paths: one flight per ion at the model tolerance, the ladder's first rung" : "paths: the reported flights",
                        "outcome codes: " + string.Join(", ", Enum.GetValues<TrajectoryOutcome>().Select(o => $"{(int)o} {o}")),
                        truncated.Count > 0
                            ? $"TRUNCATED: sample capacity reached for id(s) {string.Join(' ', truncated)}; the tail of those paths is missing"
                            : "complete: no path reached the sample capacity",
                        .. omitted.Count > 0
                            ? [$"omitted: id(s) {string.Join(' ', omitted)} recorded fewer than two samples and have no line"]
                            : Array.Empty<string>(),
                    ]));
        }

        return (outcome, validation, validation.ModelPath);
    }

    /// <summary>
    /// The groups' warnings, each once. A code whose message differs between groups (a regime
    /// measured at another frequency, a depth at another potential) says which group each
    /// message belongs to: <c>group 3 (f = 0.05 MHz): ...</c>.
    /// </summary>
    private static IEnumerable<ValidityWarning> Attributed(List<(string Group, ValidityWarning Warning)> warnings)
    {
        var varied = warnings
            .GroupBy(pair => pair.Warning.Code, StringComparer.Ordinal)
            .Where(code => code.Select(pair => pair.Warning.Message).Distinct(StringComparer.Ordinal).Skip(1).Any())
            .Select(code => code.Key)
            .ToHashSet(StringComparer.Ordinal);

        return warnings
            .Select(pair => varied.Contains(pair.Warning.Code)
                ? pair.Warning with { Message = $"{pair.Group}: {pair.Warning.Message}" }
                : pair.Warning)
            .Distinct();
    }

    /// <summary>One group of rows with the same overrides, and the model compiled with them.</summary>
    /// <param name="Model">The model compiled with the group's overrides.</param>
    /// <param name="Indices">The group's rows by index in the list; null when the group is the whole list.</param>
    /// <param name="Solves">The solve keys of the group's model.</param>
    /// <param name="Record">What the manifest records about the group.</param>
    private sealed record FlightGroup(
        CompiledModel Model, IReadOnlyList<int>? Indices, IReadOnlyList<SolveKey> Solves, OverrideGroup Record);

    /// <summary>
    /// The groups a list flies in: the model itself for a list without overrides, else one
    /// compiled model per distinct set of overrides, each checked as the model is and checked
    /// to differ from it only in the fields. Every group is compiled before any field is built,
    /// so a refused group costs no solve.
    /// </summary>
    private static (List<FlightGroup> Groups, List<EinzelError> Errors) Groups(
        ModelDocument document, string directory, CompiledModel model, IonListExpansion expansion)
    {
        var rows = expansion.Rows!;
        var groups = new List<FlightGroup>();
        var errors = new List<EinzelError>();
        var phased = expansion.RfPhaseRows.ToHashSet();
        var frequencies = IonList.Frequencies(model);

        if (expansion.OverrideSets.Count == 0)
        {
            var solves = Fields.Solved.SolveHash.Of(model);
            groups.Add(new FlightGroup(model, null, solves, Record(0, null, rows.Count, solves)));
            return (groups, errors);
        }

        for (var g = 0; g < expansion.OverrideSets.Count; g++)
        {
            var set = expansion.OverrideSets[g];
            var compiled = model;
            var indices = Enumerable.Range(0, rows.Count).Where(k => expansion.RowGroups[k] == g).ToList();

            // An error about the compiled model is the list's: its overrides made the model so.
            EinzelError At(EinzelError error) => error with
            {
                Path = set.DeclaredAt,
                Constraint = $"with these overrides the model is refused at {error.Path}: {error.Constraint}",
            };

            if (set.Values.Count > 0)
            {
                var validation = ModelValidator.Validate(
                    document,
                    set.Values.ToDictionary(
                        pair => pair.Key,
                        pair => Quantity.Si(pair.Value, model.Parameters.Parameters[pair.Key].Value.Dimension),
                        StringComparer.Ordinal),
                    directory);

                if (!validation.IsValid)
                {
                    errors.AddRange(validation.Errors.Select(At));
                    continue;
                }

                compiled = validation.Model!;

                if (Refusals(compiled) is { Count: > 0 } refused)
                {
                    errors.AddRange(refused.Select(At));
                    continue;
                }

                if (Changed(model, compiled) is { Count: > 0 } changed)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.SchemaInvalid,
                        Path = set.DeclaredAt,
                        Constraint = "an override may change only the fields; these change the model's "
                            + string.Join(", ", changed),
                        Suggestion = "state the ion, launch and birth on the row, and keep the parameters "
                            + "they are derived from out of the overrides",
                    });
                    continue;
                }

                // A row's rfPhase became a birth time with the frequency as written; under
                // another frequency it would be born at another phase than the one it states.
                if (indices.FirstOrDefault(phased.Contains, -1) is var first and >= 0
                    && !IonList.Frequencies(compiled).SequenceEqual(frequencies))
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.SchemaInvalid,
                        Path = set.DeclaredAt,
                        Constraint = "an override that changes the drive frequency moves what rfPhase means; "
                            + $"row id {rows[first].Id} of this group is born by rfPhase",
                        Suggestion = "state birthTime on these rows",
                    });
                    continue;
                }
            }

            var solves = Fields.Solved.SolveHash.Of(compiled);
            groups.Add(new FlightGroup(compiled, indices, solves, Record(g, set, indices.Count, solves)));
        }

        return (groups, errors);
    }

    private static OverrideGroup Record(int index, IonOverrideSet? set, int rows, IReadOnlyList<SolveKey> solves) => new()
    {
        Index = index,
        Overrides = (set?.Values ?? new Dictionary<string, double>()).ToDictionary(
            pair => pair.Key,
            pair => new OverrideValue
            {
                Value = set!.Written?[pair.Key].Value,
                Unit = set.Written?[pair.Key].Unit,
                Si = pair.Value,
            },
            StringComparer.Ordinal),
        Rows = rows,
        ChannelHashes = [.. solves.SelectMany(key => key.ChannelHashes)],
    };

    /// <summary>What a group's model states differently from the model as written, outside its fields.</summary>
    private static List<string> Changed(CompiledModel model, CompiledModel group)
    {
        static (string Name, object Value)[] Of(CompiledModel m) =>
        [
            ("ion", (m.MassSi, m.ChargeSi)),
            ("source", (m.SourcePosition, m.SourceDirection, m.AccelerationPotentialSi, m.EnergyFraction)),
            ("detector", (m.DetectorPoint, m.DetectorNormal)),
            ("integration settings", (m.RelativeTolerance, m.SampleIntervalSi)),
            ("maximum flight time", m.MaximumFlightTimeSi),
            ("gas", m.Gas),
        ];

        return [.. Of(model).Zip(Of(group)).Where(pair => !Equals(pair.First.Value, pair.Second.Value)).Select(pair => pair.First.Name)];
    }

    /// <summary>The solve keys of every group, each key once, in order of first appearance.</summary>
    private static List<SolveKey> Union(IEnumerable<IReadOnlyList<SolveKey>> keys)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        return [.. keys.SelectMany(list => list).Where(key => seen.Add(
            $"{key.Field}|{key.GeometryHash}|{string.Join(',', key.ChannelHashes)}"))];
    }

    /// <summary>What <c>fly</c> refuses in a model, each by the declaration that makes it so (D8).</summary>
    private static List<EinzelError> Refusals(CompiledModel model)
    {
        var errors = new List<EinzelError>();

        void Refuse(bool when, string path, string constraint, string suggestion)
        {
            if (when)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = path,
                    Constraint = constraint,
                    Suggestion = suggestion,
                });
            }
        }

        Refuse(model.SpaceChargeMode != "none", "/transport/spaceCharge",
            "fly flies each ion alone, so ions that push on each other cannot be flown as a list",
            "fly the packet with einzel run and a source cloud, or remove spaceCharge");
        Refuse(model.TransportMode == "diffusion" && !model.NeedsSequencedTransport, "/transport/mode",
            "a diffusive model evolves a density and has no trajectories to fly one per row",
            "fly a list through a trajectory model");
        Refuse(model.NeedsSequencedTransport, "/sequence",
            "a sequence with a diffusive phase or a mode change is run phase by phase, not flown one trajectory per row",
            "use einzel run for the sequenced model");
        Refuse(model.IsMixture, "/species",
            "the model declares a mixture under 'species'; an ion list takes its defaults from one 'ion'",
            "declare one 'ion' and give each row its own massToCharge");
        Refuse(model.Cloud.IsCloud, "/source/cloud",
            "the model declares a source cloud; an ion list states the launch itself",
            "move the cloud into the list as a 'cloud' set");

        return errors;
    }

    /// <summary>
    /// REG-2 for the model's own ion, as <c>run</c> measures it: the gas's numbers at the launch
    /// speed and over the flight that ion makes, one integration at the model tolerance with
    /// its collisions seeded as <c>run</c>'s. Nothing in vacuum, where there is no regime to leave.
    /// </summary>
    private static IReadOnlyList<ValidityWarning> RegimeWarnings(
        CompiledModel model, IElectrostaticField field, IntegrationSettings settings,
        TrajectoryStopFunction detector, BackgroundGas gas)
    {
        if (!gas.IsPresent)
        {
            return [];
        }

        var species = IonSpecies.FromModel(model);
        var launch = new PhaseState(model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());
        var nominal = TrajectoryIntegrator.Integrate(
            launch, species, field, settings, detector,
            collisions: new CollisionSampler(gas, species.MassSi, species.ChargeSi, model.Gas.Seed));
        var regime = RegimeDiagnostics.Measure(
            gas, species, Math.Max(launch.Velocity.Length, 1.0), nominal.FlightTimeSeconds,
            RunCommand.SmallestAperture(model), RunCommand.DriveFrequency(model));

        return RegimeDiagnostics.ForTrajectoryMode(gas, regime);
    }

    /// <summary>The inputs an existing manifest at this stem recorded, when its ion list was another.</summary>
    private static string? OtherList(string manifestPath, IReadOnlyDictionary<string, string> inputs, string ions)
    {
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            var recorded = RunManifest.FromJson(File.ReadAllText(manifestPath))?.InputHashes ?? new Dictionary<string, string>();

            return recorded.ContainsKey(ions)
                ? null
                : string.Join(", ", recorded.Keys.Where(key => !inputs.ContainsKey(key)).DefaultIfEmpty("an unrecorded ion list"));
        }
        catch (JsonException)
        {
            return "an unreadable manifest";
        }
    }

    private static string IonsStem(string ions)
    {
        var name = Path.GetFileName(ions);

        foreach (var suffix in (string[])[".ions.json", ".ions.csv", ".json", ".csv"])
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return name[..^suffix.Length];
            }
        }

        return name;
    }

    private static FlyIonJson Row(IonFlight flight)
    {
        var result = flight.Result!;
        var at = result.FinalState.Position;

        return new FlyIonJson(
            flight.Row.Id, flight.Row.Tag, result.Outcome.ToString(), result.StruckSurface,
            at.X * 1e3, at.Y * 1e3, at.Z * 1e3, result.FlightTimeSeconds * 1e6,
            flight.FlightTimeHalfWidthSeconds * 1e6, result.AcceptedSteps, result.RejectedSteps, flight.Collisions,
            result.MaximumRelativeEnergyDrift, [.. flight.Warnings.Select(w => w.Code)]);
    }

    private static SortedDictionary<string, int> Count(IEnumerable<string> names)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var name in names)
        {
            counts[name] = counts.GetValueOrDefault(name) + 1;
        }

        return counts;
    }

    /// <summary>
    /// One line per ion: what a spreadsheet or a corpus adapter reads. With overrides, each row's
    /// group and its overridden values in SI follow, as in the <c>.ions.csv</c>.
    /// </summary>
    private static string Csv(IEnumerable<FlyIonJson> rows, IReadOnlyList<IonOverrideSet> sets)
    {
        var names = IonListCsv.ParameterNames(sets);
        var text = new StringBuilder("id,tag,outcome,surface,x_mm,y_mm,z_mm,time_us,steps,rejected,collisions,warnings");

        static string R(double value) =>
            double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : string.Empty;

        if (sets.Count > 0)
        {
            text.Append(",group").Append(string.Concat(names.Select(name => $",{IonListCsv.ParameterPrefix}{name}")));
        }

        text.Append('\n');

        foreach (var row in rows)
        {
            text.Append(CultureInfo.InvariantCulture, $"{row.Id},{row.Tag},{row.Outcome},{row.Surface},")
                .Append(CultureInfo.InvariantCulture, $"{R(row.XMm)},{R(row.YMm)},{R(row.ZMm)},{R(row.TimeUs)},")
                .Append(CultureInfo.InvariantCulture, $"{row.Steps},{row.Rejected},{row.Collisions},{string.Join(';', row.Warnings)}");

            if (row.Group is { } group)
            {
                text.Append(CultureInfo.InvariantCulture, $",{group}").Append(string.Concat(names.Select(name =>
                    "," + (sets[group].Values.TryGetValue(name, out var value) ? R(value) : string.Empty))));
            }

            text.Append('\n');
        }

        return text.ToString();
    }
}
