using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields.Solved;
using Einzel.Project;

namespace Einzel.Commands;

/// <summary>
/// What a study's fields will cost, worked out from its definition before any evaluation:
/// the channels at the nominal model and at the study's extreme points, which of them the
/// cache holds, and what the study will hold in memory.
/// </summary>
/// <remarks>
/// <para>
/// <b>Keys without solves.</b> Every point of a study compiles the document with its
/// overrides, and the compiled model names its channels (<c>SolveHash.Of</c>) without
/// solving anything. A point whose channels are the nominal model's - a drive amplitude, a
/// frequency, an ion - reuses the nominal field; a point that adds a channel - a geometry,
/// or a DC level of a driven model, which moves the one DC pattern - solves it.
/// </para>
/// <para>
/// <b>Sampled at the ends, not at every point.</b> A scan or a boundary search at its two
/// ends, an optimisation at the bounds of up to three variables, a tolerance sweep at each
/// channel's extremes. A study whose ends hold the nominal channels is taken to hold them at
/// every point, and one whose ends add channels to add them at every point.
/// </para>
/// </remarks>
public sealed record StudyFieldSurvey
{
    /// <summary>
    /// The solve keys of the nominal model, then of each sampled point that adds one, each key once.
    /// </summary>
    public required IReadOnlyList<SolveKey> Solves { get; init; }

    /// <summary>Distinct channels among <see cref="Solves"/>.</summary>
    public required int Channels { get; init; }

    /// <summary>How many of them the project's field cache holds; zero without a cache.</summary>
    public required int CachedChannels { get; init; }

    /// <summary>The nominal model's channels.</summary>
    public required int NominalChannels { get; init; }

    /// <summary>How many of the nominal model's channels the cache holds.</summary>
    public required int NominalCached { get; init; }

    /// <summary>
    /// The most channels a sampled point holds that the nominal model does not: zero for a
    /// study whose points only re-weight the nominal field.
    /// </summary>
    public required int PointChannels { get; init; }

    /// <summary>Whether the cache holds every channel the sampled points add.</summary>
    public required bool PointsCached { get; init; }

    /// <summary>How many evaluations the study makes, its nominal included where it evaluates one.</summary>
    public required int Evaluations { get; init; }

    /// <summary>How many evaluations may run at once: one for a boundary search or an optimisation.</summary>
    public required int MaxParallelism { get; init; }

    /// <summary>How many evaluations solve a channel; the nominal counts when it does.</summary>
    public required int SolvingEvaluations { get; init; }

    /// <summary>What one evaluation's solve holds at its peak, in mebibytes (the model's own peak).</summary>
    public required double SolvePeakMiB { get; init; }

    /// <summary>What the nominal model's channels hold, in mebibytes.</summary>
    public required double ChannelMiB { get; init; }

    /// <summary>
    /// What the study's memo may keep, in mebibytes: the study's <c>memoryBudget</c>, or four
    /// times the nominal channels, never below them.
    /// </summary>
    public required double BudgetMiB { get; init; }

    /// <summary>
    /// The study's peak, in mebibytes: the evaluations solving at once times one solve's peak,
    /// plus what the memo keeps (its budget when points add channels, the nominal channels
    /// otherwise).
    /// </summary>
    public required double PeakMiB { get; init; }

    /// <summary>
    /// Under a field basis whose driver finds its points as it runs (a boundary search, an
    /// optimisation): that those points are not sampled, and one that does not fit the plan
    /// solves its own patterns, which these counts do not include. Null otherwise.
    /// </summary>
    public string? BasisNote { get; init; }
}

/// <summary>Surveys a study's fields: see <see cref="StudyFieldSurvey"/>.</summary>
public static class StudyFields
{
    /// <summary>How many times the nominal field the memo keeps when a study names no budget.</summary>
    public const int DefaultBudgetFields = 4;

    /// <summary>Surveys a study's fields without solving anything.</summary>
    /// <param name="study">The study.</param>
    /// <param name="document">The model it studies.</param>
    /// <param name="directory">The directory the model was read from.</param>
    /// <param name="project">The project whose field cache is asked, or null to count nothing as cached.</param>
    /// <returns>The survey, or null when the nominal model does not compile.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="study"/> or <paramref name="document"/> is null.</exception>
    public static StudyFieldSurvey? Survey(
        StudyDocument study, ModelDocument document, string? directory, ProjectLayout? project)
    {
        ArgumentNullException.ThrowIfNull(study);
        ArgumentNullException.ThrowIfNull(document);

        if (ModelValidator.Validate(document, null, directory).Model is not { } nominal)
        {
            return null;
        }

        // Under a field basis the keys are the plan's, so the survey counts the channels the
        // study will actually solve.
        var basis = StudyFieldBasis.For(study, document, directory);
        IReadOnlyList<SolveKey> Keys(CompiledModel model) => SolveHash.Of(model, basis?.PlanFor(model));

        var nominalKeys = Keys(nominal);
        var nominalChannels = nominalKeys.SelectMany(key => key.ChannelHashes).Distinct(StringComparer.Ordinal).ToList();
        var keys = new List<SolveKey>(nominalKeys);
        var added = new HashSet<string>(StringComparer.Ordinal);
        var pointChannels = 0;

        foreach (var overrides in Samples(study, nominal.Parameters))
        {
            // A sample outside a declared bound is refused by validation, as the study's own
            // point there would be; it says nothing about the field.
            if (ModelValidator.Validate(document, overrides, directory).Model is not { } compiled)
            {
                continue;
            }

            var sampled = Keys(compiled);
            var adds = sampled.SelectMany(key => key.ChannelHashes)
                .Where(hash => !nominalChannels.Contains(hash, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            pointChannels = Math.Max(pointChannels, adds.Count);
            added.UnionWith(adds);
            keys.AddRange(sampled.Where(key => !keys.Any(seen => Same(seen, key))));
        }

        bool Cached(string hash) => project is not null && FieldCache.Find(
            project, EngineBuild.SolverBehaviourVersion, EngineBuild.ComputePath, hash) is not null;

        var channels = keys.SelectMany(key => key.ChannelHashes).Distinct(StringComparer.Ordinal).ToList();
        var (evaluations, parallel) = Extent(study);
        var parallelism = parallel ? Math.Max(1, study.MaxParallelism ?? Environment.ProcessorCount) : 1;
        var nominalCached = nominalChannels.Count(Cached);
        var pointsCached = added.All(Cached);

        var pointsSolve = pointChannels > 0 && !pointsCached;
        var solving = (pointsSolve ? evaluations - 1 : 0) + (nominalCached < nominalChannels.Count ? 1 : 0);
        var atOnce = pointsSolve ? Math.Min(parallelism, Math.Max(1, evaluations - 1)) : Math.Min(solving, 1);

        var (solvePeak, channelBytes) = Memory(nominal);

        if (basis is not null)
        {
            // The plan's channels, each the size the default decomposition's are.
            channelBytes *= (double)nominalChannels.Count / Math.Max(1, SolveHash.Of(nominal).Sum(key => key.ChannelHashes.Count));
        }
        var budget = (double)Budget(study, (long)channelBytes);

        return new StudyFieldSurvey
        {
            Solves = keys,
            Channels = channels.Count,
            CachedChannels = channels.Count(Cached),
            NominalChannels = nominalChannels.Count,
            NominalCached = nominalCached,
            PointChannels = pointChannels,
            PointsCached = pointsCached,
            Evaluations = evaluations,
            MaxParallelism = parallelism,
            SolvingEvaluations = solving,
            SolvePeakMiB = solvePeak / Mebibyte,
            ChannelMiB = channelBytes / Mebibyte,
            BudgetMiB = budget / Mebibyte,
            PeakMiB = ((atOnce * solvePeak) + (pointChannels > 0 ? budget : channelBytes)) / Mebibyte,
            BasisNote = basis is { SamplesEveryPoint: false }
                ? "search points are not sampled; a point that does not fit the plan solves its own patterns"
                : null,
        };
    }

    /// <summary>The memo budget a study runs with, in bytes, once its nominal channels are held.</summary>
    /// <param name="study">The study.</param>
    /// <param name="nominalBytes">What the first evaluation's channels hold.</param>
    /// <returns>The study's <c>memoryBudget</c>, or four times the nominal channels; never below them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="study"/> is null.</exception>
    public static long Budget(StudyDocument study, long nominalBytes)
    {
        ArgumentNullException.ThrowIfNull(study);

        return Math.Max(StudyBinding.MemoryBudget(study) ?? (DefaultBudgetFields * nominalBytes), nominalBytes);
    }

    /// <summary>
    /// How many evaluations a study makes, its nominal included where the driver evaluates one,
    /// and whether they may run at once.
    /// </summary>
    /// <param name="study">The study.</param>
    /// <returns>The count, which is a ceiling for a search, and whether the driver runs points in parallel.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="study"/> is null.</exception>
    /// <remarks>
    /// A scan and a sweep evaluate the nominal model alone before their points, and count it
    /// here; an optimisation's first evaluation is its starting point, inside its budget; a
    /// boundary search evaluates its bracket and never the nominal.
    /// </remarks>
    public static (int Evaluations, bool Parallel) Extent(StudyDocument study)
    {
        ArgumentNullException.ThrowIfNull(study);

        if (study.Scan is { } scan)
        {
            return (Math.Max(scan.Points, 1) + 1, true);
        }

        if (study.Boundary is { } boundary)
        {
            return (Math.Max(boundary.MaximumEvaluations, 1), false);
        }

        if (study.Variables is { Count: > 0 })
        {
            return (Math.Max(study.MaximumEvaluations, 1), false);
        }

        var attribution = study.OneAtATime ? 2 * (study.Channels?.Count ?? 0) : 0;

        return (Math.Max(study.Draws + attribution, 1) + 1, true);
    }

    /// <summary>The overrides of the points a survey samples.</summary>
    /// <param name="study">The study.</param>
    /// <param name="surface">The nominal model's parameters, for a sweep's channel extremes.</param>
    /// <returns>One override set per sampled point.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<IReadOnlyDictionary<string, Quantity>> Samples(
        StudyDocument study, ParameterSurface surface)
    {
        ArgumentNullException.ThrowIfNull(study);
        ArgumentNullException.ThrowIfNull(surface);

        var samples = EstimateCommand.Extremes(study);

        if (study.Scan is not null || study.Boundary is not null || study.Variables is { Count: > 0 }
            || study.Channels is not { Count: > 0 })
        {
            return samples;
        }

        IReadOnlyList<Sweeps.PerturbationChannel> channels;

        try
        {
            channels = StudyBinding.Channels(study);
        }
        catch (Core.Errors.EinzelException)
        {
            // A channel the sweep itself will refuse; the sweep says why, not the survey.
            return samples;
        }

        foreach (var channel in channels)
        {
            if (!surface.Parameters.ContainsKey(channel.Parameter))
            {
                continue;
            }

            var (low, high) = channel.Extremes(surface[channel.Parameter]);

            samples.Add(new Dictionary<string, Quantity>(StringComparer.Ordinal) { [channel.Parameter] = low });
            samples.Add(new Dictionary<string, Quantity>(StringComparer.Ordinal) { [channel.Parameter] = high });
        }

        return samples;
    }

    /// <summary>
    /// What one evaluation's solve holds at its peak, and what the model's channels hold once
    /// solved, in bytes: elements in document order, each keeping its channels while the next
    /// solves, as <c>estimate</c> counts a model.
    /// </summary>
    /// <param name="model">The compiled model.</param>
    /// <returns>The solve's peak and the channels' bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    public static (double PeakBytes, double ChannelBytes) Memory(CompiledModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var (peak, retained) = (0.0, 0.0);

        foreach (var element in model.Fields)
        {
            var plan = element.Solve3D is { } volume ? GeometryBuilder3D.Memory(Geometry3D.Of(volume))
                : element.Solve is { } plane ? GeometryBuilder.Memory(plane)
                : null;

            if (plan is not null)
            {
                peak = Math.Max(peak, retained + plan.PeakBytes);
                retained += plan.RetainedBytes;
            }
        }

        return (peak, retained);
    }

    private const double Mebibyte = 1024.0 * 1024.0;

    private static bool Same(SolveKey left, SolveKey right) =>
        left.Field == right.Field
        && string.Equals(left.GeometryHash, right.GeometryHash, StringComparison.Ordinal)
        && left.ChannelHashes.SequenceEqual(right.ChannelHashes, StringComparer.Ordinal);
}
