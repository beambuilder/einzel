using System.Collections.Concurrent;
using System.Globalization;
using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Einzel.Project;

namespace Einzel.Commands;

/// <summary>
/// The field side of one study run: the memo every evaluation builds its field through, and
/// the keys each evaluation's field was built from.
/// </summary>
/// <remarks>
/// <para>
/// <b>One memo per study.</b> A point that keeps the nominal model's channels shares the
/// nominal arrays instead of reading them from the cache, hashing them and holding a copy of
/// its own; the memo pins the first evaluation's channels and keeps the rest within the
/// study's budget (<see cref="StudyFields.Budget"/>).
/// </para>
/// <para>
/// <b>Recorded in point order, never in completion order</b> (CLI-5). Evaluations run at once
/// finish in any order, so each records its keys with the values of the study's parameters it
/// was compiled with, and the union is sorted by where those values stand in the driver's own
/// order of points: the nominal first, then the points, then the extremes a sweep attributes
/// to. A boundary search and an optimisation evaluate one at a time, in call order.
/// </para>
/// </remarks>
internal sealed class StudyFieldRun
{
    private readonly SolveMemo _memo;
    private readonly FieldCache? _cache;
    private readonly StudyDocument _study;
    private readonly string[] _parameters;
    private readonly ConcurrentQueue<(long Call, double[] At, IReadOnlyList<SolveKey> Keys)> _seen = new();
    private long _calls;
    private int _bound;
    private int _solvedSeen;

    /// <summary>Starts the field side of a study run.</summary>
    /// <param name="store">The store behind the memo, or null to solve every channel.</param>
    /// <param name="study">The study, for its budget.</param>
    /// <param name="parameters">The parameters the study varies, whose values place an evaluation among the points.</param>
    public StudyFieldRun(ISolveStore? store, StudyDocument study, IEnumerable<string> parameters)
    {
        _memo = new SolveMemo(store);
        _cache = store as FieldCache;
        _study = study;
        _parameters = [.. parameters];
    }

    /// <summary>The store each evaluation builds its field through.</summary>
    public ISolveStore Store => _memo;

    /// <summary>The memo, for its counts.</summary>
    public SolveMemo Memo => _memo;

    /// <summary>How many evaluations were made.</summary>
    public long Evaluations => Interlocked.Read(ref _calls);

    /// <summary>Records an evaluation's keys before its field is built.</summary>
    /// <param name="model">The model the evaluation compiled.</param>
    public void Record(CompiledModel model)
    {
        var call = Interlocked.Increment(ref _calls);
        _seen.Enqueue((call, Values(model.Parameters), SolveHash.Of(model)));
    }

    /// <summary>
    /// After an evaluation: the first one pins its channels and bounds the memo, and one that
    /// solved lets the collector take back the solver's working set.
    /// </summary>
    /// <remarks>
    /// <b>A solve leaves gigabytes of garbage behind it</b> - the mask, the cut links, the
    /// multigrid levels: 2.9 GB a channel on the 0.2 mm quadrupole - and an evaluation that
    /// shares its channels allocates almost nothing after it, so nothing prompts the collector
    /// before the next point's solve allocates as much again. A DC scan of the quadrupole peaked
    /// at 7.8 GB of working set where the same scan peaked at 4.5 GB while every evaluation still
    /// re-read its channels per ion, which did prompt it. Collecting once after an evaluation
    /// that solved costs milliseconds against the half-minute solve before it.
    /// </remarks>
    public void Evaluated()
    {
        if (Interlocked.Exchange(ref _bound, 1) == 0)
        {
            _memo.Bound(StudyFields.Budget(_study, _memo.HeldBytes));
        }

        var solved = _memo.Solved;

        if (Interlocked.Exchange(ref _solvedSeen, solved) != solved)
        {
            GC.Collect();
        }
    }

    /// <summary>The values of the study's parameters a model was compiled with, in SI.</summary>
    /// <param name="surface">The model's parameters.</param>
    /// <returns>One value per varied parameter; NaN for one the model does not declare.</returns>
    public double[] Values(ParameterSurface surface) =>
        [.. _parameters.Select(name => surface.Parameters.TryGetValue(name, out var resolved)
            ? resolved.Value.SiValue
            : double.NaN)];

    /// <summary>The values of the study's parameters an override set leaves, in SI.</summary>
    /// <param name="nominal">The nominal values.</param>
    /// <param name="overrides">The overrides.</param>
    /// <returns>The nominal values with the overridden ones replaced.</returns>
    public double[] Values(double[] nominal, IReadOnlyDictionary<string, Core.Units.Quantity> overrides) =>
        [.. _parameters.Select((name, k) => overrides.TryGetValue(name, out var value) ? value.SiValue : nominal[k])];

    /// <summary>The nominal evaluation's values: the first evaluation's.</summary>
    public double[] Nominal => _seen.Where(seen => seen.Call == 1).Select(seen => seen.At).FirstOrDefault() ?? [];

    /// <summary>Every key the evaluations built from, each once, in point order.</summary>
    /// <param name="points">
    /// The driver's points in its own order, as values of the varied parameters, the nominal
    /// excluded; null for a driver that evaluates one at a time, whose call order is its order.
    /// </param>
    /// <returns>The keys, and what became of each channel in the cache (null without a cache or a channel).</returns>
    public (IReadOnlyList<SolveKey> Solves, IReadOnlyList<FieldCacheUse>? Uses) Union(IReadOnlyList<double[]>? points)
    {
        int Position(double[] at)
        {
            for (var index = 0; index < points!.Count; index++)
            {
                if (points[index].SequenceEqual(at))
                {
                    return index;
                }
            }

            return points.Count;
        }

        var ordered = points is null
            ? _seen.OrderBy(seen => seen.Call)
            : _seen.OrderBy(seen => seen.Call == 1 ? -1 : Position(seen.At))
                .ThenBy(seen => string.Join(';', seen.Keys.Select(key => string.Join(',', key.ChannelHashes))), StringComparer.Ordinal);

        var distinct = new HashSet<string>(StringComparer.Ordinal);
        List<SolveKey> solves = [.. ordered.SelectMany(seen => seen.Keys).Where(key => distinct.Add(
            string.Create(CultureInfo.InvariantCulture, $"{key.Field}|{key.GeometryHash}|{string.Join(',', key.ChannelHashes)}")))];

        var uses = _cache?.Uses(solves) is { Count: > 0 } used
            ? [.. used.DistinctBy(use => use.ChannelHash)]
            : (IReadOnlyList<FieldCacheUse>?)null;

        return (solves, uses);
    }

    /// <summary>The one line a study prints about its fields, as <c>run</c> does.</summary>
    /// <returns>The line.</returns>
    public string Line() => string.Create(
        CultureInfo.InvariantCulture,
        $"  field: {_memo.Solved} channel(s) solved, {_memo.Loaded} loaded from the cache, {_memo.Reused} reused in memory, "
        + $"over {Evaluations} evaluation(s); {_memo.FieldSeconds:F1} s getting them");
}
