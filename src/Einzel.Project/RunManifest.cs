using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Einzel.Core.Model;

namespace Einzel.Project;

/// <summary>
/// The record that fully determines a run.
/// </summary>
/// <remarks>
/// <para>
/// PRJ-3: "A run manifest fully determines its run. Model hash, seeds, engine
/// version, transport mode, solver settings, extension identities. Results are
/// therefore regenerable rather than precious."
/// </para>
/// <para>
/// That property is what makes the rest of spec section 3 work. It is why
/// <c>.einzel/</c> can be discarded without losing anything, why version control
/// is optional rather than required, and why GRD-10 can detect drift in both
/// directions — a stored result can be checked against both the current model and
/// the currently installed engine, on a network share or a plain folder, with no
/// repository involved.
/// </para>
/// <para>
/// <see cref="SolverBehaviourVersion"/> is separate from
/// <see cref="EngineVersion"/> on purpose. FLD-3 makes it part of the field-cache
/// key and calls it "not optional, since after an engine update a cache computed
/// by the previous solver is silently wrong and nothing else would catch it". It
/// changes only when numerical behaviour changes, so a release that alters
/// nothing physical does not invalidate every cache in every project.
/// </para>
/// </remarks>
public sealed record RunManifest
{
    /// <summary>Content hash of the model document, as <c>sha256:</c> and 64 hex characters.</summary>
    public required string ModelHash { get; init; }

    /// <summary>The model this result is about, relative to the project root.</summary>
    /// <remarks>
    /// <para>
    /// <b>Which model, as distinct from which content.</b> The hash determines the run -
    /// that is PRJ-3, and it is what makes a result regenerable - but it does not say which
    /// file in the project the result is <em>about</em>, and two models may legitimately
    /// hold the same content.
    /// </para>
    /// <para>
    /// Without this, <c>verify</c> had to find the model by searching for one whose content
    /// still hashed to the recorded value, and the failure that produced is in the unsafe
    /// direction: editing the model that was actually run made its drift <b>disappear</b>,
    /// because the result silently re-attached to some other file that still matched and
    /// reported itself current. A project scaffolded by <c>init</c> and then given a corpus
    /// example of the same device is enough to reach it.
    /// </para>
    /// <para>
    /// Absent on manifests written before this field existed, and the hash search remains
    /// the fallback for those and for a model that has since been renamed - a hash survives
    /// a rename and a path does not, which is why the search was right to exist.
    /// </para>
    /// </remarks>
    public string? ModelPath { get; init; }

    /// <summary>SHA-256 hashes of imported data and the study file, keyed by project-relative path.</summary>
    /// <remarks>Null denotes a legacy manifest with no dependency inventory; an empty map means none were consumed.</remarks>
    public IReadOnlyDictionary<string, string>? InputHashes { get; init; }

    /// <summary>A relative path as a manifest should carry it.</summary>
    /// <param name="path">The path, in whatever the platform uses.</param>
    /// <returns>The same path with forward slashes.</returns>
    /// <remarks>
    /// <b>A manifest travels.</b> <c>results/</c> is small text and gets committed, and CI
    /// here runs on both Linux and Windows - so a backslash path written on one does not
    /// resolve on the other. Verify would then miss, fall back to the hash, find the model
    /// anyway and report a <em>rename</em> on every result that had crossed a platform: a
    /// false alarm on output the tool produced itself, which is the kind that teaches
    /// people to stop reading the tool.
    /// </remarks>
    public static string Portable(string path) =>
        path?.Replace('\\', '/') ?? string.Empty;

    /// <summary>A recorded path as this platform spells it.</summary>
    /// <param name="path">The path as the manifest carries it.</param>
    /// <returns>The same path with this platform's separator.</returns>
    public static string Local(string path) =>
        path?.Replace('/', System.IO.Path.DirectorySeparatorChar) ?? string.Empty;

    /// <summary>The model's declared schema version.</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>The engine build that produced the run.</summary>
    public required string EngineVersion { get; init; }

    /// <summary>
    /// Bumped only when numerical behaviour changes, independently of the engine
    /// version. Part of the field-cache key (FLD-3).
    /// </summary>
    public required int SolverBehaviourVersion { get; init; }

    /// <summary>
    /// What each solved field element was solved from: its geometry hash and one hash per
    /// basis channel. Empty when no element is solved. Absent on manifests written before
    /// it was recorded. A study records the union over its evaluations, each key once, in the
    /// order of the points that first needed it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ModelHash"/> answers "which text"; this answers "which physics". A comment
    /// edit moves the first and not this, and a tenth of a micron on an electrode moves this
    /// - which is the distinction a field cache needs, keyed on these together with
    /// <see cref="SolverBehaviourVersion"/> (FLD-3).
    /// </para>
    /// <para>
    /// Computed from the compiled model by the same decomposition the solve makes, not read
    /// off the solved channels, so it can be known before a solve - which is what lets a run
    /// that reuses a cached field record the key it looked up rather than one read off a
    /// solve that never happened.
    /// </para>
    /// </remarks>
    public IReadOnlyList<SolveKey>? Solves { get; init; }

    /// <summary>
    /// What became of each solved channel in the field cache: loaded, stored, replaced, or not
    /// writable, with the SHA-256 of the potential the run used. Absent when the run did not
    /// use the cache (<c>--no-cache</c>, a manifest written before the cache existed). A study
    /// and an ion list with overrides record each channel once, at its first use.
    /// </summary>
    /// <remarks>
    /// The payload hash is what lets <c>einzel verify</c> notice that the cache now holds a
    /// different field for a channel than this result was computed from, without reading the
    /// field itself (FLD-3, PRJ-3).
    /// </remarks>
    public IReadOnlyList<FieldCacheUse>? FieldCache { get; init; }

    /// <summary>The transport mode used.</summary>
    public required string TransportMode { get; init; }

    /// <summary>The compute path: scalar, SIMD, or GPU.</summary>
    public required string ComputePath { get; init; }

    /// <summary>Random seeds. Empty for a deterministic single-ion run.</summary>
    public IReadOnlyList<long> Seeds { get; init; } = [];

    /// <summary>
    /// How many ions could fly at once, for an ion-list flight, or how many evaluations could
    /// run at once, for a study; absent elsewhere.
    /// </summary>
    /// <remarks>
    /// A cost and not an input: rows and points are computed and written by index, so the
    /// result is the same at any setting. Recorded because it explains the wall time and the
    /// memory beside it.
    /// </remarks>
    public int? MaxParallelism { get; init; }

    /// <summary>
    /// For a study: the seconds its evaluations spent getting solved channels, loading them
    /// from the field cache and solving them; absent elsewhere.
    /// </summary>
    /// <remarks>
    /// A channel shared in memory between evaluations costs nothing, so this is what the study
    /// paid for its fields, not what each evaluation would have paid alone.
    /// </remarks>
    public double? FieldSeconds { get; init; }

    /// <summary>
    /// For a study built under a field basis other than the default patterns: the plan, its
    /// channel hashes and the points that fell back to their own patterns; absent elsewhere.
    /// </summary>
    public FieldBasisRecord? FieldBasis { get; init; }

    /// <summary>
    /// For an ion-list flight whose rows override model parameters: one entry per group of
    /// rows with the same overrides, in the order the groups were flown. Absent elsewhere.
    /// </summary>
    /// <remarks>
    /// Each group was flown through the model compiled with its overrides, so its channel
    /// hashes are what its field was built from; <see cref="Solves"/> and
    /// <see cref="FieldCache"/> are then the union over the groups, each channel once.
    /// </remarks>
    public IReadOnlyList<OverrideGroup>? OverrideGroups { get; init; }

    /// <summary>Extension identities and versions. Empty when none were used (GRD-6).</summary>
    public IReadOnlyList<string> Extensions { get; init; } = [];

    /// <summary>
    /// The Python interpreter extensions ran against, when any did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRJ-3 says a manifest fully determines its run, and an extension result
    /// depends on which interpreter computed it - a different Python is a different
    /// numpy, a different rounding of a transcendental, and in the worst case a
    /// different answer from the same source file. Recording the engine version and
    /// not the interpreter would leave a run reproducible in the part this project
    /// wrote and unreproducible in the part it did not.
    /// </para>
    /// <para>
    /// Null when no extension ran, rather than filled in with whatever happened to
    /// be on the path. An interpreter that took no part in a run is not provenance,
    /// and recording it would imply it mattered. It is also why this is not
    /// discovered eagerly: finding an interpreter costs a process start, and a run
    /// that never needed one should not pay for it.
    /// </para>
    /// <para>
    /// EXT-6 wants a vendored interpreter, which would make this a version rather
    /// than a path. Until then it is whatever was discovered, and a path on one
    /// machine is not the same interpreter as the same path on another.
    /// </para>
    /// </remarks>
    public string? Interpreter { get; init; }

    /// <summary>
    /// The machine the run happened on. Recorded because spec section 8 requires
    /// run-to-run reproducibility on one machine but explicitly does not require
    /// bit-reproducibility across machines, so a comparison that crosses machines
    /// needs to know it did.
    /// </summary>
    public required string Machine { get; init; }

    /// <summary>When the run happened, in ISO 8601 UTC.</summary>
    public required string CreatedUtc { get; init; }

    /// <summary>Serialiser options: stable, diffable, newline terminated.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        NewLine = "\n",
    };

    /// <summary>Serialises the manifest.</summary>
    /// <returns>The manifest text, newline terminated.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions) + "\n";

    /// <summary>Parses a manifest.</summary>
    /// <param name="json">The manifest text.</param>
    /// <returns>The manifest, or null when the text is not one.</returns>
    public static RunManifest? FromJson(string json) =>
        JsonSerializer.Deserialize<RunManifest>(json, JsonOptions);
}

/// <summary>Content hashing for model documents and referenced artifacts.</summary>
/// <remarks>
/// PRJ-2: large artifacts are referenced by content hash, never embedded. The
/// same hash identifies a model in its manifest, which is what GRD-10 compares
/// against to detect that a model has moved on since a result was computed.
/// </remarks>
public static class ContentHash
{
    /// <summary>Hashes file bytes without loading the whole file into memory.</summary>
    /// <param name="path">The input file.</param>
    /// <returns>A SHA-256 content hash.</returns>
    public static string OfFile(string path)
    {
        using var stream = File.OpenRead(path);
        return "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
    }

    /// <summary>Hashes text as UTF-8, ignoring line-ending style.</summary>
    /// <param name="text">The text to hash.</param>
    /// <returns>The hash, as <c>sha256:</c> followed by 64 lowercase hex characters.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <remarks>
    /// Line endings are normalised before hashing. A model that round-trips
    /// through a Windows editor is the same model, and a hash that said otherwise
    /// would make GRD-10 report drift that did not happen — which is worse than
    /// useless, because a drift warning nobody believes is a drift warning nobody
    /// reads.
    /// </remarks>
    public static string OfText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var normalised = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalised));

        return "sha256:" + Convert.ToHexStringLower(digest);
    }
}
