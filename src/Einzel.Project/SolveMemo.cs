using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Einzel.Fields.Solved;

namespace Einzel.Project;

/// <summary>
/// Keeps every channel a process has loaded or solved in memory, in front of another store:
/// each channel is read from disk, or solved, once per process.
/// </summary>
/// <remarks>
/// <para>
/// For a command that builds many fields from one geometry, as <c>fly</c> does for an ion
/// list's override groups: a group whose voltages differ from the last only in a drive
/// amplitude needs the same channels with other weights, and reading them from the project
/// cache again costs a load and a SHA-256 per channel each time.
/// </para>
/// <para>
/// <b>Loads shared, solves copied once.</b> A loaded potential is never written after its
/// field is built, so every field built through this store reads the one array the inner
/// store gave. A solve is handed over as a span, so the memo keeps a copy of it
/// (<c>ToArray</c>): the field that solved the channel holds its own array and the memo another,
/// until that field is dropped, and every later field reads the memo's. A save passes through
/// to the inner store unchanged, which records it as it would without the memo.
/// </para>
/// <para>
/// <b>Unbounded.</b> It holds every channel until it is dropped: 68 MB per channel of the
/// 0.2 mm quadrupole, so 64 DC patterns and one RF channel are about 4.4 GB.
/// </para>
/// </remarks>
/// <param name="inner">The store behind the memo, or null for none.</param>
public sealed class SolveMemo(ISolveStore? inner) : ISolveStore
{
    private readonly ConcurrentDictionary<string, (double[] Potential, SolveRecord Record)> _held = new(StringComparer.Ordinal);
    private int _loaded;
    private int _solved;
    private int _reused;

    /// <summary>Channels the inner store supplied.</summary>
    public int Loaded => _loaded;

    /// <summary>Channels solved and handed to this store.</summary>
    public int Solved => _solved;

    /// <summary>Channels served from memory, loaded or solved earlier in this process.</summary>
    public int Reused => _reused;

    /// <inheritdoc/>
    public bool TryLoad(
        SolveEntry entry,
        [NotNullWhen(true)] out double[]? potential,
        [NotNullWhen(true)] out SolveRecord? record)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_held.TryGetValue(entry.ChannelHash, out var held))
        {
            Interlocked.Increment(ref _reused);
            (potential, record) = held;
            return true;
        }

        if (inner is not null && inner.TryLoad(entry, out potential, out record))
        {
            Interlocked.Increment(ref _loaded);
            _held[entry.ChannelHash] = (potential, record);
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

        Interlocked.Increment(ref _solved);
        _held[entry.ChannelHash] = (potential.ToArray(), record);
        inner?.Save(entry, potential, record);
    }
}
