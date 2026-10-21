using Einzel.Project;

namespace Einzel.Commands;

/// <summary>One stored result, checked against the world as it is now.</summary>
public sealed record VerifiedResult
{
    /// <summary>The manifest file, relative to the project root.</summary>
    public required string Manifest { get; init; }

    /// <summary>The model the manifest names, relative to the project root, or null when it is gone.</summary>
    public string? Model { get; init; }

    /// <summary>The model path the manifest recorded, whether or not it is still there.</summary>
    /// <remarks>
    /// Distinct from <see cref="Model"/>, which is where the model was <em>found</em>. They
    /// differ in the two cases worth naming: a rename, where the content turned up
    /// elsewhere, and a result whose model is gone - and a caller listing that second case
    /// can say which file rather than "(gone)".
    /// </remarks>
    public string? RecordedModel { get; init; }

    /// <summary>Whether the model still hashes to what the manifest recorded.</summary>
    public required bool ModelMatches { get; init; }

    /// <summary>Whether the engine that produced it is the one installed.</summary>
    public required bool EngineMatches { get; init; }

    /// <summary>Whether the numerical behaviour version still matches (FLD-3).</summary>
    public required bool SolverMatches { get; init; }

    /// <summary>Whether the run happened on this machine.</summary>
    public required bool SameMachine { get; init; }

    /// <summary>
    /// What makes this result no longer the answer, in the order a reader wants it.
    /// </summary>
    public required IReadOnlyList<string> Drift { get; init; }

    /// <summary>
    /// True of the run but not invalidating.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="Drift"/> deliberately. A result made by a
    /// different engine build whose numerical behaviour is identical still stands,
    /// and filing that beside a model that has been edited underneath a result
    /// would train a reader to ignore both.
    /// </remarks>
    public required IReadOnlyList<string> Notes { get; init; }

    /// <summary>Whether the result still stands for the model and engine as they are.</summary>
    public bool Current => Drift.Count == 0;
}

/// <summary>The outcome of verifying a project's stored results.</summary>
public sealed record VerifyOutcome
{
    /// <summary>The project root.</summary>
    public required string Root { get; init; }

    /// <summary>One entry per manifest found, ordered by path.</summary>
    public required IReadOnlyList<VerifiedResult> Results { get; init; }

    /// <summary>How many results still stand.</summary>
    public int Current => Results.Count(r => r.Current);

    /// <summary>Whether every result still stands.</summary>
    public bool AllCurrent => Results.All(r => r.Current);
}

/// <summary>
/// Checks stored results against the model and the engine as they are now.
/// </summary>
/// <remarks>
/// <para>
/// GRD-10 asks for drift detection in both directions, and PRJ-3 is what makes it
/// possible: a manifest fully determines its run, so a result carries enough to
/// say whether it is still the answer. Two ways it stops being one, and they fail
/// differently.
/// </para>
/// <para>
/// The <em>model</em> moved on: someone edited the geometry after the run, and the
/// number in results/ answers a question nobody is asking any more. The hash
/// catches that exactly.
/// </para>
/// <para>
/// The <em>engine</em> moved on: the model is untouched but the solver is not the
/// one that produced the answer. The solver-behaviour version is separate from the
/// engine version precisely so this does not fire on a release that changed
/// nothing physical - FLD-3 calls it "not optional, since after an engine update a
/// cache computed by the previous solver is silently wrong and nothing else would
/// catch it".
/// </para>
/// <para>
/// Nothing is recomputed. That is the point: verification has to be cheap enough
/// to run on a whole project, and a check that costs as much as the run it is
/// checking would not be run.
/// </para>
/// </remarks>
public static class VerifyCommand
{
    private static readonly string[] StudyManifestSuffixes =
        [".sweep.manifest.json", ".scan.manifest.json", ".boundary.manifest.json", ".optimise.manifest.json"];

    /// <summary>Verifies every stored result in a project.</summary>
    /// <param name="root">The project root.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentException"><paramref name="root"/> is null or blank.</exception>
    public static VerifyOutcome Execute(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var layout = new ProjectLayout(Path.GetFullPath(root));
        var results = new List<VerifiedResult>();

        if (!Directory.Exists(layout.Results))
        {
            return new VerifyOutcome { Root = layout.Root, Results = results };
        }

        // Ordered, for CLI-5: a report that reorders between runs makes every diff
        // of it noisy for no reason.
        var manifests = Directory.GetFiles(layout.Results, "*.manifest.json");
        Array.Sort(manifests, StringComparer.Ordinal);

        foreach (var path in manifests)
        {
            results.Add(Verify(layout, path));
        }

        return new VerifyOutcome { Root = layout.Root, Results = results };
    }

    private static VerifiedResult Verify(ProjectLayout layout, string manifestPath)
    {
        var relative = Path.GetRelativePath(layout.Root, manifestPath);
        RunManifest? manifest;

        try
        {
            manifest = RunManifest.FromJson(File.ReadAllText(manifestPath));
        }
        catch (System.Text.Json.JsonException)
        {
            manifest = null;
        }

        if (manifest is null)
        {
            return new VerifiedResult
            {
                Manifest = relative,
                ModelMatches = false,
                EngineMatches = false,
                SolverMatches = false,
                SameMachine = false,
                Drift = ["this manifest cannot be read, so nothing about the result it describes can be established"],
                Notes = [],
            };
        }

        // Which model this result is about comes from the manifest, and whether that
        // model has moved comes from the hash. Those are two questions and conflating
        // them is what went wrong.
        //
        // Before the path was recorded there was only the hash, so identity had to be
        // "whichever file still hashes to this" - and the consequence runs in the unsafe
        // direction. Two models may legitimately hold the same content: edit the one that
        // was actually run and its result silently re-attaches to the other, which still
        // matches, and reports itself **current**. The drift does not move, it disappears.
        // A project scaffolded by `init` and then given a corpus example of the same
        // device reaches it without trying.
        //
        // The hash search is kept as the fallback, because it is right for the two cases
        // it was written for: a manifest older than this field, and a model that has been
        // renamed since - a hash survives a rename and a path does not.
        //
        // An earlier version derived the path from the manifest's own filename, which
        // works for reflectron.manifest.json and fails for anything else: a sweep writes
        // tolerance.sweep.manifest.json, verify went looking for models/tolerance.sweep.json,
        // and reported a result it had just written as STALE with exit 1. That stem is
        // still the first thing the hash search tries.
        var stem = Path.GetFileNameWithoutExtension(relative);
        stem = stem.EndsWith(".manifest", StringComparison.Ordinal) ? stem[..^".manifest".Length] : stem;

        var recorded = manifest.ModelPath is { Length: > 0 } named
            ? Path.Combine(layout.Root, RunManifest.Local(named))
            : null;

        var renamed = false;
        string? modelPath;

        if (recorded is not null && File.Exists(recorded))
        {
            modelPath = recorded;
        }
        else
        {
            modelPath = FindByHash(layout, manifest.ModelHash, stem);

            // A recorded path that is gone while some other file still matches is a
            // rename, and saying so is the difference between "your model moved" and a
            // result quietly changing what it is about.
            renamed = recorded is not null && modelPath is not null;
        }

        var modelExists = modelPath is not null;

        var modelMatches = modelExists
            && string.Equals(
                ContentHash.OfText(File.ReadAllText(modelPath!)), manifest.ModelHash, StringComparison.Ordinal);

        var engineMatches = string.Equals(
            manifest.EngineVersion, EngineBuild.Version, StringComparison.Ordinal);

        var solverMatches = manifest.SolverBehaviourVersion == EngineBuild.SolverBehaviourVersion;
        var sameMachine = string.Equals(manifest.Machine, Environment.MachineName, StringComparison.Ordinal);

        var drift = new List<string>();
        var notes = new List<string>();

        if (!modelExists)
        {
            drift.Add(
                manifest.ModelPath is { Length: > 0 } gone
                    ? $"the model this result came from is gone: '{gone}' is not there and "
                      + $"no other file in models/ has the hash {manifest.ModelHash}"
                    : "the model this result came from is gone: no file in models/ has the "
                      + $"hash {manifest.ModelHash}");
        }
        else if (renamed)
        {
            // A note rather than drift: the content is unchanged, so the result still
            // answers the question. What has changed is where the question lives.
            //
            // Worded as what is actually known, which is less than it looks. Content alone
            // cannot tell a rename from a twin that was there all along - both leave the
            // recorded path gone and identical bytes elsewhere - so saying "renamed" would
            // be asserting a history nothing here observed.
            notes.Add(
                $"the model recorded for this result, '{manifest.ModelPath}', is no longer "
                + $"there; a file with the same content is at "
                + $"'{Path.GetRelativePath(layout.Root, modelPath!)}', so the result still "
                + "stands. That is a rename if nothing else held that content");
        }

        if (modelExists && !modelMatches)
        {
            drift.Add("the model has been edited since this result was computed, so it answers a question "
                + "about a geometry that no longer exists");
        }

        if (manifest.InputHashes is { } inputs)
        {
            if (modelMatches)
            {
                var document = Io.ModelJson.Parse(File.ReadAllText(modelPath!));
                foreach (var reference in RunInputs.ImportedPaths(document))
                {
                    var resolved = Path.GetFullPath(reference, Path.GetDirectoryName(modelPath!)!);
                    var relativeInput = RunManifest.Portable(Path.GetRelativePath(layout.Root, resolved));
                    if (!inputs.ContainsKey(relativeInput))
                        drift.Add($"the model resolves an input not recorded by this run: '{relativeInput}'");
                }
            }
            foreach (var (input, expected) in inputs.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var path = Path.GetFullPath(RunManifest.Local(input), layout.Root);
                try
                {
                    if (!File.Exists(path)) drift.Add($"input file is missing: '{input}'");
                    else if (ContentHash.OfFile(path) != expected) drift.Add($"input file has changed: '{input}'");
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    drift.Add($"input file cannot be verified: '{input}': {error.Message}");
                }
            }
        }
        else if ((modelMatches && RunInputs.ImportedPaths(Io.ModelJson.Parse(File.ReadAllText(modelPath!))).Any())
            || StudyManifestSuffixes.Any(suffix => relative.EndsWith(suffix, StringComparison.Ordinal)))
        {
            drift.Add("this legacy manifest does not record the study/imported inputs; rerun to establish their provenance");
        }

        // The field cache (FLD-3, D5 of the cache's design): the manifest holds the SHA-256 of
        // every potential the run used. An entry gone is regenerable and the next run solves
        // it again; an entry holding another field is drift, because the next run would load
        // it. Records only: no payload is read, which is the loader's job on every hit. Under
        // another solver version the solver line below already says everything.
        if (solverMatches && manifest.FieldCache is { } uses)
        {
            foreach (var use in uses.Where(u => u.PayloadHash is not null))
            {
                var entry = FieldCache.Find(
                    layout, manifest.SolverBehaviourVersion, manifest.ComputePath, use.ChannelHash);

                if (entry is null)
                {
                    notes.Add($"field {use.Field} channel {use.Channel} is not in the field cache; "
                        + "the next run solves it again");
                }
                else if (entry.Payload.Sha256 != use.PayloadHash)
                {
                    drift.Add($"the field cache holds another field for field {use.Field} channel "
                        + $"{use.Channel} than this run used, and the next run would load it");
                }
            }
        }

        if (!solverMatches)
        {
            // The one that would otherwise be silent. An engine update that changed
            // numerical behaviour makes a stored result wrong in a way nothing
            // about the file would show.
            drift.Add($"the solver has changed behaviour since this run: version {manifest.SolverBehaviourVersion} "
                + $"produced it, this build is {EngineBuild.SolverBehaviourVersion}");
        }
        else if (!engineMatches)
        {
            notes.Add($"a different engine build produced this: {manifest.EngineVersion}, against "
                + $"{EngineBuild.Version} installed. The solver-behaviour version is the same, so the "
                + "numbers stand");
        }

        if (!sameMachine)
        {
            // Section 8 requires run-to-run reproducibility on one machine and
            // explicitly does not require bit-reproducibility across machines, so
            // a comparison that crosses one needs to know it did.
            notes.Add($"produced on {manifest.Machine}, not on {Environment.MachineName}. Results are "
                + "reproducible run to run on one machine and not bit-identical across machines");
        }

        return new VerifiedResult
        {
            Manifest = relative,
            Model = modelPath is null ? null : Path.GetRelativePath(layout.Root, modelPath),
            RecordedModel = manifest.ModelPath is { Length: > 0 } was
                ? RunManifest.Local(was)
                : null,
            ModelMatches = modelMatches,
            EngineMatches = engineMatches,
            SolverMatches = solverMatches,
            SameMachine = sameMachine,
            Drift = drift,
            Notes = notes,
        };
    }
    /// <summary>
    /// Finds the model a manifest came from, by hash.
    /// </summary>
    /// <remarks>
    /// The name is tried first because it is nearly always right and costs one
    /// stat; the hash scan is the fallback that makes the answer correct rather
    /// than merely usual. A manifest whose model has been renamed still verifies,
    /// which is the whole reason a hash is what gets recorded.
    /// </remarks>
    private static string? FindByHash(ProjectLayout layout, string hash, string stem)
    {
        var byName = Path.Combine(layout.Models, stem + ".json");

        if (File.Exists(byName)
            && string.Equals(ContentHash.OfText(File.ReadAllText(byName)), hash, StringComparison.Ordinal))
        {
            return byName;
        }

        if (!Directory.Exists(layout.Models))
        {
            return File.Exists(byName) ? byName : null;
        }

        foreach (var candidate in Directory.EnumerateFiles(layout.Models, "*.json").Order(StringComparer.Ordinal))
        {
            if (string.Equals(ContentHash.OfText(File.ReadAllText(candidate)), hash, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        // Nothing matches. Return the name-derived path when it exists, so the
        // report says "changed" rather than "gone" - a model that was edited is a
        // different thing from one that was deleted.
        return File.Exists(byName) ? byName : null;
    }

}
