using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Einzel.Core.Model;
using Einzel.Fields.Solved;

namespace Einzel.Project;

/// <summary>
/// The solved basis channels of a project, kept on disk under <c>.einzel/cache</c> so the next
/// run of the same geometry loads them instead of solving (FLD-3).
/// </summary>
/// <remarks>
/// <para>
/// <b>One entry per channel</b>, under <c>solver-&lt;SolverBehaviourVersion&gt;-&lt;ComputePath&gt;/</c>: the
/// potential as raw little-endian doubles in <c>Grid.Index</c> order, the
/// <c>ScalarField</c>'s own array byte for byte, in <c>&lt;hash&gt;.f64</c>; and a JSON record
/// beside it, written last, so an entry exists exactly when its record does. The key is the
/// channel hash, which covers the geometry, the mesh, the tolerance, the edges and what each
/// electrode holds; the solver-behaviour version and the compute path name the directory, so a
/// second compute path never writes over the first's entries, and the record repeats both. The
/// machine is recorded and not keyed: a solve is reproducible on one machine and not bit for bit
/// across machines, and a cache carried to another one serves the field solved on the original
/// machine, which is as accurate and not the bits the new machine would produce.
/// </para>
/// <para>
/// <b>Taint, never block.</b> A load checks the record against the channel it is asked for -
/// hash, version, compute path, grid, length - and the payload against its SHA-256. Anything
/// that fails is a miss with one line saying why, and the channel is solved and written over
/// it. A cache that cannot be written is a slower run, never a failed one. Writes go to a
/// temporary file renamed into place, so a run killed half way leaves no entry rather than a
/// short one. A big-endian machine does not use the cache at all.
/// </para>
/// <para>
/// <b>What is not kept.</b> Warnings: the shared-face check depends on stage states the key
/// does not cover, so a hit asks the geometry again. The report's scalars are kept, so an
/// unconverged solve warns on every reuse as it did when it was solved.
/// </para>
/// </remarks>
public sealed class FieldCache : ISolveStore
{
    /// <summary>The format tag every entry record carries.</summary>
    public const string Format = "einzel/basis-field/1";

    private readonly bool _read;
    private readonly Action<string>? _announce;
    private readonly Lock _gate = new();
    private readonly Lock _writing = new();
    private readonly List<(string ChannelHash, string Status, string? PayloadHash)> _log = [];
    private readonly HashSet<string> _tainted = [];
    private bool _complained;

    /// <summary>Opens a project's cache for the running build.</summary>
    /// <param name="project">The project.</param>
    /// <param name="read">
    /// False to write solves without ever loading one, for a command whose job is to measure
    /// the solve.
    /// </param>
    /// <param name="announce">Where a one-line notice goes when an entry is not used or cannot be written.</param>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    public FieldCache(ProjectLayout project, bool read = true, Action<string>? announce = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        Location = EntryDirectory(project, EngineBuild.SolverBehaviourVersion, EngineBuild.ComputePath);
        _read = read;
        _announce = announce;
    }

    /// <summary>The directory this build's entries live in.</summary>
    public string Location { get; }

    /// <summary>The directory the entries of one solver-behaviour version and compute path live in.</summary>
    /// <param name="project">The project.</param>
    /// <param name="solverBehaviourVersion">The version.</param>
    /// <param name="computePath">The compute path, as a manifest records it.</param>
    /// <returns>The directory, <c>solver-2-scalar</c> for example, which need not exist.</returns>
    public static string EntryDirectory(ProjectLayout project, int solverBehaviourVersion, string computePath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(computePath);

        return Path.Combine(
            project.FieldCache,
            string.Create(CultureInfo.InvariantCulture, $"solver-{solverBehaviourVersion}-{computePath}"));
    }

    /// <summary>The file stem of a channel's entry: its hash without the <c>sha256:</c> prefix.</summary>
    /// <param name="channelHash">The channel hash.</param>
    /// <returns>The 64 hex characters.</returns>
    public static string Stem(string channelHash)
    {
        ArgumentNullException.ThrowIfNull(channelHash);

        return channelHash.StartsWith("sha256:", StringComparison.Ordinal) ? channelHash[7..] : channelHash;
    }

    /// <inheritdoc/>
    public bool TryLoad(
        SolveEntry entry,
        [NotNullWhen(true)] out double[]? potential,
        [NotNullWhen(true)] out SolveRecord? record)
    {
        ArgumentNullException.ThrowIfNull(entry);

        potential = null;
        record = null;

        var stem = Path.Combine(Location, Stem(entry.ChannelHash));

        if (!_read || !Usable() || !File.Exists(stem + ".json"))
        {
            return false;
        }

        string? problem;

        try
        {
            var stored = FieldCacheEntry.Read(stem + ".json");
            problem = stored is null ? "its record does not parse" : Mismatch(stored, entry);

            if (problem is null)
            {
                var values = GC.AllocateUninitializedArray<double>(checked((int)entry.NodeCount));
                var bytes = MemoryMarshal.AsBytes(values.AsSpan());

                using (var stream = new FileStream(
                    stem + ".f64", FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan))
                {
                    if (stream.Length == bytes.Length)
                    {
                        stream.ReadExactly(bytes);
                    }
                    else
                    {
                        problem = $"its payload holds {stream.Length} bytes, not {bytes.Length}";
                    }
                }

                var hash = problem is null ? Hash(bytes) : null;

                if (hash is not null && hash != stored!.Payload.Sha256)
                {
                    problem = "its payload does not match its SHA-256";
                }

                if (problem is null)
                {
                    Log(entry.ChannelHash, FieldCacheUse.Hit, hash);
                    potential = values;
                    record = stored!.ToRecord();
                    return true;
                }
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Any failure is a miss: a record that parses into something no solve wrote must
            // cost a solve, never the run.
            problem = error.Message;
        }

        lock (_gate)
        {
            _tainted.Add(entry.ChannelHash);
        }

        _announce?.Invoke($"  field cache: entry {Stem(entry.ChannelHash)[..12]} is not used ({problem}); solving it again");
        return false;
    }

    /// <inheritdoc/>
    public void Save(SolveEntry entry, ReadOnlySpan<double> potential, SolveRecord record)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(record);

        if (!Usable())
        {
            return;
        }

        var bytes = MemoryMarshal.AsBytes(potential);
        var hash = Hash(bytes);
        var stem = Path.Combine(Location, Stem(entry.ChannelHash));
        var temporary = $"{stem}.{Guid.NewGuid():N}.tmp";
        string status;

        try
        {
            Directory.CreateDirectory(Location);

            bool replacing;

            lock (_gate)
            {
                replacing = _tainted.Contains(entry.ChannelHash);
            }

            // One write at a time: two channels solved in parallel can share a key, and a save
            // that deleted the record another had just put in place would leave a payload with
            // none. The hash above is computed outside, where it can overlap.
            lock (_writing)
            {
                replacing |= File.Exists(stem + ".json") || File.Exists(stem + ".f64");

                // The record goes first and comes back last: an entry is committed exactly while
                // its record exists, so a payload being replaced is never described by the old one.
                File.Delete(stem + ".json");

                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes);
                }

                File.Move(temporary, stem + ".f64", overwrite: true);

                var stored = FieldCacheEntry.Of(entry, record, Stem(entry.ChannelHash) + ".f64", bytes.Length, hash);
                File.WriteAllText(temporary, stored.ToJson());
                File.Move(temporary, stem + ".json", overwrite: true);
            }

            status = replacing ? FieldCacheUse.Replaced : FieldCacheUse.Stored;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            status = FieldCacheUse.Unwritable;

            try
            {
                File.Delete(temporary);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // Left for 'einzel cache prune'.
            }

            Complain($"  field cache: could not write {Location}: {error.Message}");
        }

        Log(entry.ChannelHash, status, hash);
    }

    /// <summary>What happened to each channel of a model, the first time this cache was asked about it.</summary>
    /// <param name="solves">The model's solve keys (<c>SolveHash.Of</c>).</param>
    /// <returns>One entry per channel the cache was asked about, in key order.</returns>
    /// <remarks>
    /// The first use, because a run builds its field more than once (the figures of merit
    /// rebuild it), and what a reader wants to know is whether the run started from the cache.
    /// </remarks>
    public IReadOnlyList<FieldCacheUse> Uses(IReadOnlyList<SolveKey> solves)
    {
        ArgumentNullException.ThrowIfNull(solves);

        var first = new Dictionary<string, (string Status, string? PayloadHash)>(StringComparer.Ordinal);

        lock (_gate)
        {
            foreach (var (channelHash, status, payloadHash) in _log)
            {
                first.TryAdd(channelHash, (status, payloadHash));
            }
        }

        var uses = new List<FieldCacheUse>();

        foreach (var key in solves)
        {
            for (var channel = 0; channel < key.ChannelHashes.Count; channel++)
            {
                if (first.TryGetValue(key.ChannelHashes[channel], out var use))
                {
                    uses.Add(new FieldCacheUse
                    {
                        Field = key.Field,
                        Channel = channel,
                        ChannelHash = key.ChannelHashes[channel],
                        Status = use.Status,
                        PayloadHash = use.PayloadHash,
                    });
                }
            }
        }

        return uses;
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>Why a stored record is not a solve of this entry, or null when it is.</summary>
    private static string? Mismatch(FieldCacheEntry stored, SolveEntry entry) =>
        stored.Format != Format ? $"its format is '{stored.Format}'"
        : stored.ChannelHash != entry.ChannelHash ? "it records another channel"
        : stored.SolverBehaviourVersion != EngineBuild.SolverBehaviourVersion
            ? $"it was solved by solver-behaviour version {stored.SolverBehaviourVersion}"
        : stored.ComputePath != EngineBuild.ComputePath ? $"it was solved on the {stored.ComputePath} path"
        : stored.Dimensions != entry.Dimensions
            || !stored.Grid.Nodes.SequenceEqual(entry.Nodes)
            || !stored.Grid.Origin.SequenceEqual(entry.Origin)
            || !stored.Grid.Spacing.SequenceEqual(entry.Spacing)
            || !stored.Edges.SequenceEqual(entry.Edges) ? "it was solved on another grid"
        : stored.Payload.Bytes != entry.NodeCount * sizeof(double) ? "its record gives another payload length"
        : null;

    private bool Usable()
    {
        if (BitConverter.IsLittleEndian)
        {
            return true;
        }

        Complain("  field cache: entries are little-endian and this machine is not, so nothing is cached");
        return false;
    }

    private void Complain(string notice)
    {
        lock (_gate)
        {
            if (_complained)
            {
                return;
            }

            _complained = true;
        }

        _announce?.Invoke(notice);
    }

    private void Log(string channelHash, string status, string? payloadHash)
    {
        lock (_gate)
        {
            _log.Add((channelHash, status, payloadHash));
        }
    }
}

/// <summary>What became of one basis channel in a run that used the field cache.</summary>
/// <remarks>
/// Recorded in the run manifest, so <c>einzel verify</c> can tell later whether the cache still
/// holds the field the result was computed from.
/// </remarks>
public sealed record FieldCacheUse
{
    /// <summary>Loaded from the cache.</summary>
    public const string Hit = "hit";

    /// <summary>Solved and written to the cache, where nothing was before.</summary>
    public const string Stored = "stored";

    /// <summary>Solved and written over an entry that was there, or that failed its check.</summary>
    public const string Replaced = "replaced";

    /// <summary>Solved, and the cache could not be written.</summary>
    public const string Unwritable = "unwritable";

    /// <summary>Which field element, counted from zero.</summary>
    public required int Field { get; init; }

    /// <summary>Which basis channel of it, in the order the decomposition found them.</summary>
    public required int Channel { get; init; }

    /// <summary>The channel hash: the entry's key.</summary>
    public required string ChannelHash { get; init; }

    /// <summary><c>hit</c>, <c>stored</c>, <c>replaced</c> or <c>unwritable</c>.</summary>
    public required string Status { get; init; }

    /// <summary>The SHA-256 of the potential the run used, as <c>sha256:</c> and 64 hex characters.</summary>
    public string? PayloadHash { get; init; }
}

/// <summary>The record kept beside a cached potential.</summary>
/// <remarks>
/// Written last, so its presence means the entry is complete. Everything a load checks, the
/// report a hit hands back, and the provenance a reader of the directory wants; never warnings.
/// </remarks>
public sealed record FieldCacheEntry
{
    /// <summary>Serialiser options: those of a run manifest, with enumerations by name and non-finite numbers allowed.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(RunManifest.JsonOptions)
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary><see cref="FieldCache.Format"/>.</summary>
    public required string Format { get; init; }

    /// <summary>The channel hash, the key.</summary>
    public required string ChannelHash { get; init; }

    /// <summary>The element's geometry hash.</summary>
    public required string GeometryHash { get; init; }

    /// <summary>Two for a plane, three for a volume.</summary>
    public required int Dimensions { get; init; }

    /// <summary>The solver-behaviour version that solved it.</summary>
    public required int SolverBehaviourVersion { get; init; }

    /// <summary>The compute path that solved it.</summary>
    public required string ComputePath { get; init; }

    /// <summary>The grid the potential lies on.</summary>
    public required FieldCacheGrid Grid { get; init; }

    /// <summary>The edge or face conditions the potential carries.</summary>
    public required IReadOnlyList<EdgeCondition> Edges { get; init; }

    /// <summary>What each declared electrode held, in declaration order.</summary>
    public required IReadOnlyList<double> Holding { get; init; }

    /// <summary>How the solve went.</summary>
    public required FieldCacheReport Report { get; init; }

    /// <summary>Finest-level nodes the mask held fixed.</summary>
    public required int FixedNodes { get; init; }

    /// <summary>Stencil arms a conductor surface cut.</summary>
    public required int CutLinks { get; init; }

    /// <summary>The potential's file, length and SHA-256.</summary>
    public required FieldCachePayload Payload { get; init; }

    /// <summary>Wall time of the mask and the solve.</summary>
    public required double SolveSeconds { get; init; }

    /// <summary>The engine build that solved it.</summary>
    public required string EngineVersion { get; init; }

    /// <summary>The machine that solved it.</summary>
    public required string Machine { get; init; }

    /// <summary>When it was written, in ISO 8601 UTC.</summary>
    public required string CreatedUtc { get; init; }

    /// <summary>The record of a channel just solved.</summary>
    internal static FieldCacheEntry Of(SolveEntry entry, SolveRecord record, string file, long bytes, string sha256) => new()
    {
        Format = FieldCache.Format,
        ChannelHash = entry.ChannelHash,
        GeometryHash = entry.GeometryHash,
        Dimensions = entry.Dimensions,
        SolverBehaviourVersion = EngineBuild.SolverBehaviourVersion,
        ComputePath = EngineBuild.ComputePath,
        Grid = new FieldCacheGrid { Nodes = entry.Nodes, Origin = entry.Origin, Spacing = entry.Spacing },
        Edges = entry.Edges,
        Holding = entry.Holding,
        Report = new FieldCacheReport
        {
            Converged = record.Report.Converged,
            Cycles = record.Report.Cycles,
            InitialResidual = record.Report.InitialResidual,
            FinalResidual = record.Report.FinalResidual,
            ConvergenceFactor = record.Report.ConvergenceFactor,
            Levels = record.Report.Levels,
            Sweeps = record.Report.Sweeps,
            CoarsestNodes = record.Report.CoarsestNodes,
            Galerkin = record.Report.Galerkin,
        },
        FixedNodes = record.FixedNodes,
        CutLinks = record.CutLinks,
        Payload = new FieldCachePayload { File = file, Bytes = bytes, Sha256 = sha256 },
        SolveSeconds = record.SolveSeconds,
        EngineVersion = EngineBuild.Version,
        Machine = Environment.MachineName,
        CreatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
    };

    /// <summary>The report a hit hands back, its warnings empty.</summary>
    internal SolveRecord ToRecord() => new(
        new SolveReport(
            Report.Converged, Report.Cycles, Report.InitialResidual, Report.FinalResidual, Report.ConvergenceFactor)
        {
            Levels = Report.Levels,
            Sweeps = Report.Sweeps,
            CoarsestNodes = Report.CoarsestNodes,
            Galerkin = Report.Galerkin,
        },
        FixedNodes,
        CutLinks,
        SolveSeconds);

    internal string ToJson() => JsonSerializer.Serialize(this, JsonOptions) + "\n";

    /// <summary>A record from disk, or null when it does not parse or lacks what a load reads.</summary>
    /// <remarks>
    /// <c>required</c> asks only that a property be present: <c>"grid": null</c> deserialises,
    /// and a load would then fail on it. Such a record is treated as one that does not parse.
    /// </remarks>
    internal static FieldCacheEntry? Read(string path)
    {
        try
        {
            var entry = JsonSerializer.Deserialize<FieldCacheEntry>(File.ReadAllText(path), JsonOptions);

            return entry is
            {
                Format: not null, ChannelHash: not null, ComputePath: not null,
                Grid: { Nodes: not null, Origin: not null, Spacing: not null },
                Edges: not null, Report: not null, Payload.Sha256: not null,
            }
                ? entry
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>The grid a cached potential lies on, x first.</summary>
public sealed record FieldCacheGrid
{
    /// <summary>Nodes along each axis.</summary>
    public required IReadOnlyList<int> Nodes { get; init; }

    /// <summary>The first node's coordinate along each axis, in metres.</summary>
    public required IReadOnlyList<double> Origin { get; init; }

    /// <summary>The spacing along each axis, in metres.</summary>
    public required IReadOnlyList<double> Spacing { get; init; }
}

/// <summary>The scalars of a <see cref="SolveReport"/>, without its warnings.</summary>
public sealed record FieldCacheReport
{
    /// <summary><see cref="SolveReport.Converged"/>.</summary>
    public required bool Converged { get; init; }

    /// <summary><see cref="SolveReport.Cycles"/>.</summary>
    public required int Cycles { get; init; }

    /// <summary><see cref="SolveReport.InitialResidual"/>.</summary>
    public required double InitialResidual { get; init; }

    /// <summary><see cref="SolveReport.FinalResidual"/>.</summary>
    public required double FinalResidual { get; init; }

    /// <summary><see cref="SolveReport.ConvergenceFactor"/>.</summary>
    public required double ConvergenceFactor { get; init; }

    /// <summary><see cref="SolveReport.Levels"/>.</summary>
    public required int Levels { get; init; }

    /// <summary><see cref="SolveReport.Sweeps"/>.</summary>
    public required long Sweeps { get; init; }

    /// <summary><see cref="SolveReport.CoarsestNodes"/>.</summary>
    public required long CoarsestNodes { get; init; }

    /// <summary><see cref="SolveReport.Galerkin"/>.</summary>
    public required bool Galerkin { get; init; }
}

/// <summary>Where a cached potential is and what it hashes to.</summary>
public sealed record FieldCachePayload
{
    /// <summary>The file name, beside the record.</summary>
    public required string File { get; init; }

    /// <summary>Its length: eight bytes a node.</summary>
    public required long Bytes { get; init; }

    /// <summary>Its SHA-256, as <c>sha256:</c> and 64 hex characters.</summary>
    public required string Sha256 { get; init; }
}
