using System.Text.Json.Nodes;
using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Einzel.Project;

namespace Einzel.Cli.Tests;

/// <summary>
/// The file-backed field cache: what it writes, what it refuses to load, and what its
/// housekeeping deletes.
/// </summary>
/// <remarks>
/// <para>
/// Each test writes into a temporary project and reads back what this process wrote: the
/// payload is whatever the potential was, and nothing here is compared with a committed hash.
/// The potentials are arbitrary numbers, since the cache neither solves nor knows what a
/// field is; whether a builder uses what comes back is <c>SolveStoreTests</c>' subject.
/// </para>
/// <para>
/// "Not used" means a miss: <see cref="FieldCache.TryLoad"/> returns false, a one-line notice
/// says why, and the next save writes over the entry and records <c>replaced</c>.
/// </para>
/// </remarks>
public sealed class FieldCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-tests", Guid.NewGuid().ToString("N"));

    private readonly List<string> _notices = [];

    public FieldCacheTests() => Project = new ProjectLayout(_root);

    private ProjectLayout Project { get; }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static readonly double[] Potential = [.. Enumerable.Range(0, 20).Select(i => Math.Sin(i) * 100.0)];

    private static SolveEntry Entry(string channel = "a") => new()
    {
        ChannelHash = "sha256:" + new string(channel[0], 64),
        GeometryHash = "sha256:" + new string('9', 64),
        Dimensions = 2,
        Nodes = [5, 4],
        Origin = [-0.002, -0.0015],
        Spacing = [0.001, 0.001],
        Edges = [EdgeCondition.Neumann, EdgeCondition.Dirichlet, EdgeCondition.Dirichlet, EdgeCondition.Dirichlet],
        Holding = [1.0, -0.0, 0.25],
    };

    private static readonly SolveRecord Record = new(
        new SolveReport(false, 17, 3.5, 1e-7, 0.42) { Levels = 2, Sweeps = 340, CoarsestNodes = 99, Galerkin = true },
        FixedNodes: 6,
        CutLinks: 11,
        SolveSeconds: 0.25);

    private FieldCache Cache(bool read = true) => new(Project, read, _notices.Add);

    private string PathOf(SolveEntry entry, string extension) =>
        Path.Combine(
            FieldCache.EntryDirectory(Project, EngineBuild.SolverBehaviourVersion, EngineBuild.ComputePath),
            FieldCache.Stem(entry.ChannelHash) + extension);

    private static IReadOnlyList<string> Statuses(FieldCache cache, params SolveEntry[] entries) =>
        [.. cache.Uses([new SolveKey
        {
            Field = 0, Dimensions = 2, GeometryHash = entries[0].GeometryHash, ChannelHashes = [.. entries.Select(e => e.ChannelHash)],
        }]).Select(u => u.Status)];

    /// <summary>
    /// A channel saved is loaded back by another instance - the next run - with its values and
    /// its report, and the files are the raw doubles and a record with no temporary left over.
    /// </summary>
    /// <remarks>Mutation: a record that drops the report's sweep count fails the report comparison.</remarks>
    [Fact]
    public void AChannelSavedIsTheChannelLoaded()
    {
        var entry = Entry();
        var writer = Cache();
        writer.Save(entry, Potential, Record);

        var reader = Cache();
        Assert.True(reader.TryLoad(entry, out var loaded, out var record));

        Assert.Equal(Potential, loaded);
        Assert.Equal(Record, record);
        Assert.Equal(Potential.Length * sizeof(double), new FileInfo(PathOf(entry, ".f64")).Length);
        Assert.Contains($"\"format\": \"{FieldCache.Format}\"", File.ReadAllText(PathOf(entry, ".json")), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Project.FieldCache, "*.tmp", SearchOption.AllDirectories));
        Assert.Equal([FieldCacheUse.Stored], Statuses(writer, entry));
        Assert.Equal([FieldCacheUse.Hit], Statuses(reader, entry));
        Assert.Empty(_notices);
    }

    /// <summary>
    /// One byte changed in a payload is a miss with a notice; the save after it replaces the
    /// entry, which then loads again.
    /// </summary>
    /// <remarks>Mutation: skipping the SHA-256 comparison loads the corrupted payload, and this fails.</remarks>
    [Fact]
    public void ACorruptedPayloadIsSolvedAgainAndReplaced()
    {
        var entry = Entry();
        Cache().Save(entry, Potential, Record);

        var bytes = File.ReadAllBytes(PathOf(entry, ".f64"));
        bytes[37] ^= 0x01;
        File.WriteAllBytes(PathOf(entry, ".f64"), bytes);

        var cache = Cache();
        Assert.False(cache.TryLoad(entry, out _, out _));
        Assert.Contains("SHA-256", Assert.Single(_notices), StringComparison.Ordinal);

        cache.Save(entry, Potential, Record);
        Assert.Equal([FieldCacheUse.Replaced], Statuses(cache, entry));
        Assert.True(Cache().TryLoad(entry, out var loaded, out _));
        Assert.Equal(Potential, loaded);
    }

    /// <summary>
    /// An entry whose record names another solver-behaviour version or another compute path is
    /// not used, whatever its payload holds; and this build's entries live under a directory
    /// named by both, so a version bump leaves every older entry behind and a second compute
    /// path never writes over the first's.
    /// </summary>
    /// <remarks>
    /// Mutations: dropping the version comparison, or the compute-path comparison, each fail
    /// their case; dropping the version or the compute path from the directory fails the path
    /// check.
    /// </remarks>
    [Theory]
    [InlineData("\"solverBehaviourVersion\": 2", "\"solverBehaviourVersion\": 1", "version 1")]
    [InlineData("\"computePath\": \"scalar\"", "\"computePath\": \"simd\"", "simd")]
    public void AnEntryOfAnotherSolverIsNotUsed(string recorded, string other, string said)
    {
        Assert.Equal(2, EngineBuild.SolverBehaviourVersion);
        Assert.Equal("scalar", EngineBuild.ComputePath);

        var entry = Entry();
        Cache().Save(entry, Potential, Record);

        var record = File.ReadAllText(PathOf(entry, ".json"));
        Assert.Contains(recorded, record, StringComparison.Ordinal);
        File.WriteAllText(PathOf(entry, ".json"), record.Replace(recorded, other, StringComparison.Ordinal));

        Assert.False(Cache().TryLoad(entry, out _, out _));
        Assert.Contains(said, Assert.Single(_notices), StringComparison.Ordinal);
        Assert.Equal(
            Path.Combine(Project.Root, ".einzel", "cache", "solver-2-scalar"),
            FieldCache.EntryDirectory(Project, EngineBuild.SolverBehaviourVersion, EngineBuild.ComputePath));
    }

    /// <summary>
    /// A record that names another channel, or carries another format tag, is not used. Another
    /// channel of the same element has the same grid, edges and length, so without the channel
    /// check its potential would load as this one's: a wrong hit, not a miss.
    /// </summary>
    /// <remarks>
    /// Entry b's files copied over a's stand for an entry renamed by hand or by a defect.
    /// Mutations: dropping the channel-hash comparison loads b's field as a's, and dropping the
    /// format comparison loads the retagged record; each fails its case.
    /// </remarks>
    [Theory]
    [InlineData("channel", "another channel")]
    [InlineData("format", "its format is 'einzel/basis-field/2'")]
    public void ARecordOfAnotherChannelOrFormatIsNotUsed(string change, string said)
    {
        var (a, b) = (Entry("a"), Entry("b"));
        Cache().Save(a, Potential, Record);
        Cache().Save(b, [.. Potential.Select(v => -v)], Record);

        if (change == "channel")
        {
            File.Copy(PathOf(b, ".f64"), PathOf(a, ".f64"), overwrite: true);
            File.Copy(PathOf(b, ".json"), PathOf(a, ".json"), overwrite: true);
        }
        else
        {
            var record = File.ReadAllText(PathOf(a, ".json"));
            File.WriteAllText(PathOf(a, ".json"), record.Replace(FieldCache.Format, "einzel/basis-field/2", StringComparison.Ordinal));
        }

        Assert.False(Cache().TryLoad(a, out _, out _));
        Assert.Contains(said, Assert.Single(_notices), StringComparison.Ordinal);
    }

    /// <summary>
    /// A record that parses with a part missing - <c>"grid": null</c> and its kin - is a record
    /// that does not parse: a miss with a notice, never an exception out of the load, and the
    /// save after it replaces the entry, which then loads.
    /// </summary>
    /// <remarks>
    /// <c>required</c> asks only that a property be present, so these deserialise. Mutation:
    /// removing the null check in <c>FieldCacheEntry.Read</c> turns each into a
    /// <c>NullReferenceException</c> inside the load, whose notice no longer says the record
    /// does not parse, and this fails; <c>FieldCacheSurfaceTests</c> has <c>verify</c>'s side.
    /// </remarks>
    [Theory]
    [InlineData("grid")]
    [InlineData("edges")]
    [InlineData("payload")]
    [InlineData("grid.nodes")]
    [InlineData("report")]
    public void ARecordWithAPartMissingIsSolvedAgainAndReplaced(string part)
    {
        var entry = Entry();
        Cache().Save(entry, Potential, Record);

        var record = JsonNode.Parse(File.ReadAllText(PathOf(entry, ".json")))!.AsObject();
        var (owner, name) = part == "grid.nodes" ? (record["grid"]!.AsObject(), "nodes") : (record, part);
        Assert.True(owner.ContainsKey(name));
        owner[name] = null;
        File.WriteAllText(PathOf(entry, ".json"), record.ToJsonString());

        var cache = Cache();
        Assert.False(cache.TryLoad(entry, out _, out _));
        Assert.Contains("its record does not parse", Assert.Single(_notices), StringComparison.Ordinal);

        cache.Save(entry, Potential, Record);
        Assert.Equal([FieldCacheUse.Replaced], Statuses(cache, entry));
        Assert.True(Cache().TryLoad(entry, out var loaded, out _));
        Assert.Equal(Potential, loaded);
    }

    /// <summary>
    /// Saves of one entry racing each other - channels solved in parallel that share a key -
    /// leave one complete entry, no temporary file, and no exception.
    /// </summary>
    /// <remarks>
    /// Mutation: taking the write lock out of <c>Save</c> lets one save delete the record
    /// another has just put in place, and an entry is left without one; this fails within
    /// its rounds.
    /// </remarks>
    [Fact]
    public void SavesOfOneEntryRacingEachOtherLeaveOneEntry()
    {
        var entry = Entry();

        for (var round = 0; round < 20; round++)
        {
            var cache = Cache();
            Parallel.For(0, 8, _ => cache.Save(entry, Potential, Record));

            Assert.True(Cache().TryLoad(entry, out var loaded, out _), $"round {round}: {string.Join("; ", _notices)}");
            Assert.Equal(Potential, loaded);
            Assert.Empty(Directory.GetFiles(Project.FieldCache, "*.tmp", SearchOption.AllDirectories));
            Assert.Empty(_notices);
        }
    }

    /// <summary>
    /// The same key asked for on another grid, or with other edge conditions, is not used: equal
    /// hashes would then be a defect in the key, and the record is where it would show.
    /// </summary>
    /// <remarks>Mutation: dropping the grid comparison fails every case.</remarks>
    [Theory]
    [InlineData("nodes")]
    [InlineData("origin")]
    [InlineData("spacing")]
    [InlineData("edges")]
    public void AnEntryOnAnotherGridIsNotUsed(string change)
    {
        Cache().Save(Entry(), Potential, Record);

        var asked = change switch
        {
            "nodes" => Entry() with { Nodes = [4, 5] },
            "origin" => Entry() with { Origin = [-0.002, -0.0015 + 1e-12] },
            "spacing" => Entry() with { Spacing = [0.001, 0.0010000000000000002] },
            _ => Entry() with { Edges = [.. Enumerable.Repeat(EdgeCondition.Dirichlet, 4)] },
        };

        Assert.False(Cache().TryLoad(asked, out _, out _));
        Assert.Contains("another grid", Assert.Single(_notices), StringComparison.Ordinal);
    }

    /// <summary>
    /// A write that fails part way leaves no entry: the record is written after the payload, by
    /// rename, so a payload that cannot be put in place has no record describing it and no
    /// temporary file is left behind. The run is told once and goes on.
    /// </summary>
    /// <remarks>
    /// The payload's destination is made a directory, so the rename into place fails.
    /// Mutations: writing the record before the payload leaves a record and fails here;
    /// not deleting the temporary file fails here; letting the exception out fails here.
    /// </remarks>
    [Fact]
    public void AWriteThatFailsLeavesNoEntry()
    {
        var entry = Entry();
        Directory.CreateDirectory(PathOf(entry, ".f64"));

        var cache = Cache();
        cache.Save(entry, Potential, Record);

        Assert.Equal([FieldCacheUse.Unwritable], Statuses(cache, entry));
        Assert.False(File.Exists(PathOf(entry, ".json")));
        Assert.Empty(Directory.GetFiles(Project.FieldCache, "*.tmp", SearchOption.AllDirectories));
        Assert.Single(_notices);
        Assert.False(Cache().TryLoad(entry, out _, out _));
    }

    /// <summary>
    /// A cache opened only to write - <c>einzel solve</c>'s - never loads, and records the
    /// entry it writes over as replaced.
    /// </summary>
    /// <remarks>Mutation: ignoring the flag loads the entry, and this fails.</remarks>
    [Fact]
    public void ACacheOpenedToWriteNeverLoads()
    {
        var entry = Entry();
        Cache().Save(entry, Potential, Record);

        var writer = Cache(read: false);
        Assert.False(writer.TryLoad(entry, out _, out _));
        writer.Save(entry, Potential, Record);

        Assert.Equal([FieldCacheUse.Replaced], Statuses(writer, entry));
        Assert.True(Cache().TryLoad(entry, out _, out _));
    }

    /// <summary>
    /// The manifest's record of a run maps each channel the cache was asked about to its field
    /// and channel, and keeps the first thing that happened to it: a run that stored a channel
    /// and then loaded it for its figures of merit started from a solve.
    /// </summary>
    /// <remarks>Mutation: keeping the last use rather than the first fails here.</remarks>
    [Fact]
    public void AUseIsTheFirstThingThatHappenedToAChannel()
    {
        var (a, b) = (Entry("a"), Entry("b"));
        var cache = Cache();

        Assert.False(cache.TryLoad(b, out _, out _));
        cache.Save(b, Potential, Record);
        Assert.True(cache.TryLoad(b, out _, out _));

        var uses = cache.Uses([
            new SolveKey { Field = 0, Dimensions = 2, GeometryHash = a.GeometryHash, ChannelHashes = [a.ChannelHash] },
            new SolveKey { Field = 1, Dimensions = 2, GeometryHash = b.GeometryHash, ChannelHashes = [a.ChannelHash, b.ChannelHash] },
        ]);

        var use = Assert.Single(uses);
        Assert.Equal((1, 1, b.ChannelHash, FieldCacheUse.Stored), (use.Field, use.Channel, use.ChannelHash, use.Status));
        Assert.StartsWith("sha256:", use.PayloadHash, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>status</c> counts what is there; <c>prune</c> deletes other versions and compute
    /// paths, files that are half an entry and temporary files, and leaves this build's entries;
    /// <c>clear</c> deletes everything. A dry run of either deletes nothing and names the same
    /// files.
    /// </summary>
    /// <remarks>
    /// Mutations: prune keeping orphans, or deleting this version's entries, fails here; a dry
    /// run that deletes fails here.
    /// </remarks>
    [Fact]
    public void PruneDeletesWhatNoRunWillLoadAndClearDeletesEverything()
    {
        Cache().Save(Entry("a"), Potential, Record);
        Cache().Save(Entry("b"), Potential, Record);

        File.Delete(PathOf(Entry("b"), ".json"));
        File.WriteAllText(PathOf(Entry("c"), ".1234.tmp"), "partial");

        var old = FieldCache.EntryDirectory(Project, 1, EngineBuild.ComputePath);
        var simd = FieldCache.EntryDirectory(Project, EngineBuild.SolverBehaviourVersion, "simd");

        foreach (var directory in new[] { old, simd })
        {
            Directory.CreateDirectory(directory);
            File.Copy(PathOf(Entry("a"), ".f64"), Path.Combine(directory, "other.f64"));
            File.Copy(PathOf(Entry("a"), ".json"), Path.Combine(directory, "other.json"));
        }

        var status = FieldCache.Status(Project);
        Assert.Equal((1, 2, 1, 1), (status.Entries, status.OtherVersionEntries, status.Orphans, status.Temporary));
        Assert.Equal(
            (EngineBuild.SolverBehaviourVersion, EngineBuild.ComputePath),
            (status.SolverBehaviourVersion, status.ComputePath));

        var preview = FieldCache.Prune(Project, dryRun: true);
        Assert.True(preview.DryRun);
        Assert.Equal(6, preview.Removed.Count);
        Assert.True(File.Exists(Path.Combine(old, "other.f64")));

        var pruned = FieldCache.Prune(Project, dryRun: false);
        Assert.Equal(preview.Removed, pruned.Removed);
        Assert.Equal(preview.Bytes, pruned.Bytes);
        Assert.False(Directory.Exists(old));
        Assert.False(Directory.Exists(simd));
        Assert.True(Cache().TryLoad(Entry("a"), out _, out _));

        var after = FieldCache.Status(Project);
        Assert.Equal((1, 0, 0, 0), (after.Entries, after.OtherVersionEntries, after.Orphans, after.Temporary));

        Assert.Equal(2, FieldCache.Clear(Project, dryRun: true).Removed.Count);
        Assert.True(File.Exists(PathOf(Entry("a"), ".f64")));
        Assert.Equal(2, FieldCache.Clear(Project, dryRun: false).Removed.Count);
        Assert.False(Directory.Exists(Project.FieldCache));
    }
}
