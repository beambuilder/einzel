using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Einzel.Fields.Solved;

namespace Einzel.Project;

/// <summary>
/// Keeps the channels a process has loaded or solved in memory, in front of another store:
/// each channel is read from disk, or solved, once while it is held.
/// </summary>
/// <remarks>
/// <para>
/// For a command that builds many fields from one geometry, as <c>fly</c> does for an ion
/// list's override groups and a study does for its evaluations: a field whose voltages differ
/// from the last only in a drive amplitude needs the same channels with other weights, and
/// reading them from the project cache again costs a load and a SHA-256 per channel each time.
/// </para>
/// <para>
/// <b>Loads shared, solves copied once.</b> A loaded potential is never written after its
/// field is built, so every field built through this store reads the one array the inner
/// store gave. A solve is handed over as a span, so the memo keeps a copy of it
/// (<c>ToArray</c>): the field that solved the channel holds its own array and the memo another,
/// until that field is dropped, and every later field reads the memo's. A save passes through
/// to the inner store unchanged, which records it as it would without the memo. A field reads
/// the same bits whether its channels came from the memo, the cache or a solve, so nothing
/// computed through it depends on what the memo held.
/// </para>
/// <para>
/// <b>Unbounded until <see cref="Bound"/> is called.</b> Unbounded, it holds every channel until
/// it is dropped: 68 MB per channel of the 0.2 mm quadrupole, so 64 DC patterns and one RF
/// channel are about 4.4 GB, and a geometry sweep of a thousand draws would hold every draw's
/// field. Bounded, it pins what it holds at that moment and lets go of the least recently used
/// of the rest whenever it holds more than the budget.
/// </para>
/// <para>
/// <b>Two concurrent misses of one channel both solve it.</b> Nothing waits for a solve in
/// flight; both results are the same bits, the later save replaces the earlier in the store,
/// and the memo keeps one. A study evaluates its first point alone, so its points miss
/// together only on a channel that two points need and the first did not.
/// </para>
/// </remarks>
/// <param name="inner">The store behind the memo, or null for none.</param>
public sealed class SolveMemo(ISolveStore? inner) : ISolveStore
{
    private readonly Dictionary<string, Held> _held = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private long _bytes;
    private long _clock;
    private long? _budget;
    private int _loaded;
    private int _solved;
    private int _reused;
    private double _loadSeconds;
    private double _solveSeconds;

    /// <summary>Channels the inner store supplied.</summary>
    public int Loaded => _loaded;

    /// <summary>Channels solved and handed to this store.</summary>
    public int Solved => _solved;

    /// <summary>Channels served from memory, loaded or solved earlier in this process.</summary>
    public int Reused => _reused;

    /// <summary>Seconds spent getting channels: loading them from the inner store and solving them.</summary>
    /// <remarks>
    /// A load is timed here; a solve counts the seconds its record states. A channel served
    /// from memory costs nothing.
    /// </remarks>
    public double FieldSeconds
    {
        get
        {
            lock (_gate)
            {
                return _loadSeconds + _solveSeconds;
            }
        }
    }

    /// <summary>Bytes of potential held now.</summary>
    public long HeldBytes
    {
        get
        {
            lock (_gate)
            {
                return _bytes;
            }
        }
    }

    /// <summary>
    /// Pins every channel held now and keeps the rest within a budget from here on.
    /// </summary>
    /// <param name="budgetBytes">
    /// The most the memo keeps, pinned channels included. A pinned channel is never let go, so
    /// a budget below what is pinned keeps the pinned channels and nothing else.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="budgetBytes"/> is negative.</exception>
    /// <remarks>
    /// A study calls this after its first evaluation, so that evaluation's channels (the
    /// nominal model's, which a weight-only study reuses at every point) stay for the whole
    /// study, and the channels other points solve or load come and go, least recently used
    /// first. A channel let go is loaded or solved again when a later field needs it.
    /// </remarks>
    public void Bound(long budgetBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budgetBytes);

        lock (_gate)
        {
            foreach (var held in _held.Values)
            {
                held.Pinned = true;
            }

            _budget = budgetBytes;
            Trim();
        }
    }

    /// <inheritdoc/>
    public bool TryLoad(
        SolveEntry entry,
        [NotNullWhen(true)] out double[]? potential,
        [NotNullWhen(true)] out SolveRecord? record)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_gate)
        {
            if (_held.TryGetValue(entry.ChannelHash, out var held))
            {
                _reused++;
                held.LastUse = ++_clock;
                (potential, record) = (held.Potential, held.Record);
                return true;
            }
        }

        var started = Stopwatch.GetTimestamp();

        if (inner is not null && inner.TryLoad(entry, out potential, out record))
        {
            var seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;

            lock (_gate)
            {
                _loaded++;
                _loadSeconds += seconds;
                Keep(entry.ChannelHash, potential, record);
            }

            return true;
        }

        potential = null;
        record = null;
        return false;
    }

    /// <inheritdoc/>
    public void Save(SolveEntry entry, ReadOnlySpan<double> potential, SolveRecord record)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(record);

        var copy = potential.ToArray();

        lock (_gate)
        {
            _solved++;
            _solveSeconds += record.SolveSeconds;
            Keep(entry.ChannelHash, copy, record);
        }

        inner?.Save(entry, potential, record);
    }

    /// <summary>Holds a channel as the most recently used, then trims to the budget. Under the lock.</summary>
    private void Keep(string channelHash, double[] potential, SolveRecord record)
    {
        if (_held.Remove(channelHash, out var previous))
        {
            _bytes -= previous.Bytes;
        }

        var held = new Held(potential, record) { LastUse = ++_clock };
        _held[channelHash] = held;
        _bytes += held.Bytes;
        Trim();
    }

    /// <summary>Lets go of the least recently used unpinned channels while over the budget. Under the lock.</summary>
    private void Trim()
    {
        while (_budget is { } budget && _bytes > budget)
        {
            string? oldest = null;
            var used = long.MaxValue;

            foreach (var (hash, held) in _held)
            {
                if (!held.Pinned && held.LastUse < used)
                {
                    (oldest, used) = (hash, held.LastUse);
                }
            }

            if (oldest is null)
            {
                return;
            }

            _bytes -= _held[oldest].Bytes;
            _held.Remove(oldest);
        }
    }

    /// <summary>One channel the memo holds.</summary>
    private sealed class Held(double[] potential, SolveRecord record)
    {
        public double[] Potential { get; } = potential;

        public SolveRecord Record { get; } = record;

        public long Bytes { get; } = (long)potential.Length * sizeof(double);

        public bool Pinned { get; set; }

        public long LastUse { get; set; }
    }
}
