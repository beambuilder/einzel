using Einzel.Core.Errors;
using Einzel.Core.Results;
using Einzel.Project;

namespace Einzel.Commands;

/// <summary>One number a run produced, in the shape a reader sees it.</summary>
/// <param name="Name">What it is, in words.</param>
/// <param name="Value">The magnitude, formatted for reading.</param>
/// <param name="Unit">The unit it is in, or empty where it is a count.</param>
/// <param name="Interval">The uncertainty, formatted, where there is one.</param>
/// <param name="Evidence">What stands behind it, by kind.</param>
/// <remarks>
/// <b>Formatted here rather than in the page</b>, so a second consumer of this command -
/// the shell, an agent - gets the same rendering of the same number. A page that formatted
/// its own would be a second opinion about how many figures a quantity deserves, and the
/// two would part company.
/// </remarks>
public sealed record ReportedNumber(
    string Name,
    string Value,
    string Unit,
    string? Interval,
    string? Evidence);

/// <summary>One phase of a sequenced run, as the timeline a reader follows.</summary>
/// <param name="Name">The phase, as the model author named it.</param>
/// <param name="Mode">Which transport description it ran in.</param>
/// <param name="EndsAtUs">When it ended, on the instrument's clock, in microseconds.</param>
/// <param name="Population">Real ions still in the packet when it ended.</param>
/// <param name="Trajectories">
/// How many trajectories carried it, or absent for a diffusive phase - where a density is
/// a field rather than a count of anything, and a zero there would read as an instrument
/// that had lost every ion (RND-8's argument, met on a number rather than on a drawing).
/// </param>
/// <param name="CentroidMm">Where the packet was along the axis when the phase ended.</param>
/// <param name="AxialSpreadMm">
/// One standard deviation of position along the axis, or absent where there was nothing
/// left to measure it on.
/// </param>
/// <param name="RadialSpreadMm">The same across it.</param>
/// <param name="Converted">Whether the packet crossed between descriptions here (SEQ-1).</param>
/// <remarks>
/// <para>
/// <b>A timeline rather than a list of scalars</b>, because a sequenced run's answer is how
/// a quantity moved through the phases and a flat name/value list has nowhere to put the
/// instant each number belongs to. The summary counts stay in <see cref="ReportedRun.Numbers"/>
/// - those genuinely are scalars about the whole run.
/// </para>
/// <para>
/// <b>The width is why this exists.</b> <c>DensityField.Spread</c> has been computed since
/// the diffusive mode was built and the phase record carried only the centroid, so a study
/// whose entire subject is how wide a packet is had no way to read one out - the recurring
/// shape here where a quantity is computed and nothing downstream can see it. Splitting a
/// hold into phases then turns this table into a relaxation curve with no new capability at
/// all, which is what makes it worth carrying per phase rather than per run.
/// </para>
/// </remarks>
public sealed record ReportedPhase(
    string Name,
    string Mode,
    string EndsAtUs,
    string Population,
    int? Trajectories,
    string CentroidMm,
    string? AxialSpreadMm,
    string? RadialSpreadMm,
    bool Converted);

/// <summary>One run, as an account of it rather than as a document.</summary>
/// <param name="Manifest">The manifest, relative to the project root.</param>
/// <param name="Result">The result document, relative to the root; absent where none.</param>
/// <param name="Model">The model it names, relative to the root, where it is still there.</param>
/// <param name="RecordedModel">The model path as the manifest recorded it.</param>
/// <param name="ModelHash">The hash of the model as it was run.</param>
/// <param name="EngineVersion">Which build produced it.</param>
/// <param name="SolverBehaviourVersion">Which numerics.</param>
/// <param name="TransportMode">Which description the run was in.</param>
/// <param name="Machine">Where it ran.</param>
/// <param name="CreatedUtc">When, as the manifest recorded it.</param>
/// <param name="Outcome">How it ended, as the engine named it.</param>
/// <param name="Completed">Whether the engine finished what it was asked to do.</param>
/// <param name="Numbers">What came out, each with its unit and interval.</param>
/// <param name="Phases">
/// The timeline, where the run had one, in order. Empty for every other kind of run rather
/// than absent, since "this run was not sequenced" and "its phases could not be read" are
/// not distinctions this record has to carry: a run with no sequence has no timeline to
/// have failed at.
/// </param>
/// <param name="Warnings">What is active on it (GRD-2), by severity.</param>
/// <param name="Artifacts">What it wrote, relative to the root.</param>
/// <param name="Current">Whether it is still the answer for the model as it stands.</param>
/// <param name="Drift">What makes it no longer the answer.</param>
/// <param name="Notes">True of it and not invalidating.</param>
/// <param name="Unreadable">Why nothing above could be established, where that is so.</param>
/// <param name="Unfinished">
/// How far a run that never finished had got, from the checkpoint it left, where it left
/// one. Distinct from <paramref name="Result"/> being absent: both mean there is no answer,
/// and "this run has not been run" and "this run ran for six hours and was killed in its
/// fifth phase" call for entirely different things from the reader.
/// </param>
/// <param name="NotRendered">
/// Why this page shows no numbers although an answer is stored - which is a study's
/// result, whose record shape is not a run's. Kept apart from <paramref name="Unreadable"/>
/// deliberately: one is a document this build cannot load, which is a defect, and the other
/// is a document that is complete and that this page does not draw yet, which is a stated
/// gap. Filing them together would report every study in a project as broken.
/// </param>
public sealed record ReportedRun(
    string Manifest,
    string? Result,
    string? Model,
    string? RecordedModel,
    string ModelHash,
    string EngineVersion,
    int SolverBehaviourVersion,
    string TransportMode,
    string Machine,
    string CreatedUtc,
    string? Outcome,
    bool? Completed,
    IReadOnlyList<ReportedNumber> Numbers,
    IReadOnlyList<ReportedPhase> Phases,
    IReadOnlyList<ValidityWarning> Warnings,
    IReadOnlyList<string> Artifacts,
    bool Current,
    IReadOnlyList<string> Drift,
    IReadOnlyList<string> Notes,
    string? Unreadable,
    string? NotRendered,
    string? Unfinished);

/// <summary>An account of what a project has run.</summary>
/// <param name="Root">The project root, absolute.</param>
/// <param name="Runs">One per manifest, newest first.</param>
/// <param name="Figures">Figures already rendered here, relative to the root.</param>
/// <param name="Warnings">What a reader needs alongside the whole account.</param>
public sealed record ReportOutcome(
    string Root,
    IReadOnlyList<ReportedRun> Runs,
    IReadOnlyList<string> Figures,
    IReadOnlyList<ValidityWarning> Warnings)
{
    /// <summary>How many runs are still the answer for their model.</summary>
    public int Current => Runs.Count(r => r.Current);

    /// <summary>
    /// How many stored no answer at all, only provenance.
    /// </summary>
    /// <remarks>
    /// A run whose result is <em>there and unreadable</em> is not one of these. Both leave
    /// this report with no numbers to show, and they are different problems - one has never
    /// been stored and re-running the model fixes it, the other is a document on disk that
    /// this build cannot load, which is a defect in the surface rather than in the project.
    /// The first version of this counted them together and told the reader the wrong one.
    /// </remarks>
    public int WithoutResult => Runs.Count(StoredNothing);

    /// <summary>How many stored an answer this page does not draw - a study's.</summary>
    public int NotRendered => Runs.Count(StoredAStudysAnswer);

    /// <summary>How many stored a result this build cannot read.</summary>
    public int Unreadable => Runs.Count(StoredSomethingUnreadable);

    /// <summary>How many were interrupted before they could store an answer.</summary>
    public int Interrupted => Runs.Count(WasInterrupted);

    /// <summary>Whether a run stored no answer at all.</summary>
    /// <param name="run">The run.</param>
    /// <returns>Whether it stored nothing.</returns>
    /// <remarks>
    /// <b>Named so that the count and the diagnostic cannot disagree.</b> Both were spelled
    /// out, and when a third state arrived only one of the two learned about it - so the
    /// report said "0 runs stored no result" in one field and warned that one had in
    /// another, in the same document. Two implementations of one quantity is the defect that
    /// made `run` and `test` disagree twice in this project; a predicate is small enough to
    /// look harmless spelled twice, which is exactly why it was.
    /// </remarks>
    public static bool StoredNothing(ReportedRun run) => run is not null
        && run.Result is null
        && run.Unreadable is null
        && run.NotRendered is null
        && run.Unfinished is null;

    /// <summary>Whether a run was interrupted before it could store an answer.</summary>
    /// <param name="run">The run.</param>
    /// <returns>Whether its own checkpoint says it did not finish.</returns>
    /// <remarks>
    /// <b>The fourth state, and the predicate above had to learn about it.</b> That comment
    /// describes a count and a warning disagreeing in one document because a third state
    /// arrived and only one of the two was told; a fourth then arrived and the same thing
    /// happened again - a run still in flight was counted as one that "stored a manifest and
    /// no result document ... Re-running the model stores one", which is advice for an
    /// entirely different problem and would have somebody restart a run that is working.
    /// Found in real output rather than by a test, which is the third time on this command.
    /// </remarks>
    public static bool WasInterrupted(ReportedRun run)
        => run is not null && run.Unfinished is not null;

    /// <summary>Whether a run stored an answer of a kind this page does not draw.</summary>
    /// <param name="run">The run.</param>
    /// <returns>Whether its answer is a study's.</returns>
    public static bool StoredAStudysAnswer(ReportedRun run)
        => run is not null && run.NotRendered is not null;

    /// <summary>Whether a run stored an answer this build cannot load.</summary>
    /// <param name="run">The run.</param>
    /// <returns>Whether its answer is unreadable.</returns>
    public static bool StoredSomethingUnreadable(ReportedRun run)
        => run is not null && run.Unreadable is not null;

    /// <summary>
    /// How many were computed outside the validity of the model used (GRD-3).
    /// </summary>
    /// <remarks>
    /// A validity violation specifically, not "anything above advisory". Only advisory is
    /// suppressible, so a count of the unsuppressible ones is a count of nearly every run
    /// there is - eleven of this project's own thirty-nine corpus examples carry one while
    /// behaving exactly as designed. The number worth a headline is the one that says the
    /// physics was asked outside where it holds.
    /// </remarks>
    public int OutsideValidity => Runs.Count(
        r => r.Warnings.Any(w => w.Severity == WarningSeverity.ValidityViolation));
}

/// <summary>
/// An account of a project's runs that a person can read (Amendment 43).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every input for this existed and nothing rendered them.</b> <c>results/*.result.json</c>
/// carries the numbers with their GRD-1 envelopes, the manifests carry model hash, engine
/// version, seeds, solver-behaviour version and machine, and <c>CLI-1</c> puts
/// <c>--json</c> on every verb - all of it for a consumer that parses. What no requirement
/// asks for is an account of the work: what was run, what came out, and which caveats rode
/// along. Following a stretch of engine work meant reading terminal scrollback or a git
/// log.
/// </para>
/// <para>
/// <b>A view, not a recorder, and that is the load-bearing decision.</b> <c>PRJ-4</c>'s
/// argument is that the durable record of a design is the document and its history, with
/// <c>.einzel/</c> regenerable and discardable. So this holds no state of its own: it reads
/// the results and manifests that are already there and cannot drift from what actually
/// ran. A recorder would be a second account of the same events, and the two would part
/// company - which is the failure the generated half of <c>AGENTS.md</c> exists to prevent,
/// one level up.
/// </para>
/// <para>
/// <b>Built on <c>einzel verify</c> rather than beside it</b>, for the reason
/// <see cref="ProjectCommand"/> gives: verify already separates an edited model from a
/// changed engine build, and recomputing that distinction here would be a second
/// implementation of something that took thought to get right. What this adds is the half
/// verify does not read at all - the stored answer.
/// </para>
/// <para>
/// <b>Reachable from the CLI, so AGT-2 holds.</b> An agent gets the same account, in the
/// same order, through <c>--json</c>. Nothing here is a shell capability.
/// </para>
/// </remarks>
public static class ReportCommand
{
    /// <summary>Reads a project and accounts for every run stored in it.</summary>
    /// <param name="root">The project root.</param>
    /// <returns>The account.</returns>
    /// <exception cref="ArgumentException"><paramref name="root"/> is blank.</exception>
    /// <exception cref="EinzelException">There is no project there.</exception>
    public static ReportOutcome Execute(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var layout = new ProjectLayout(Path.GetFullPath(root));

        if (!Directory.Exists(layout.Root))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/",
                Constraint = $"there is no directory at '{layout.Root}'",
                Suggestion = "run `einzel init <dir>` to create a project there",
                Severity = ErrorSeverity.Error,
            });
        }

        var warnings = new List<ValidityWarning>();

        // One verify pass, so drift and its two kinds come from the one implementation of
        // that distinction rather than from a second reading of the same fields.
        var verified = VerifyCommand.Execute(layout.Root);
        var runs = new List<ReportedRun>();

        foreach (var result in verified.Results)
        {
            runs.Add(Describe(layout, result));
        }

        // A RUN THAT WAS KILLED HAS NO MANIFEST, because the manifest is written after the
        // run returns - it records the modes the run actually used. So `verify` cannot see
        // one and neither could this, which would have made the account of an interrupted
        // run unreachable for exactly the case it was written for. The checkpoint carries
        // what a manifest would (PRJ-3) and is listed here on its own.
        runs.AddRange(Interrupted(layout, runs));

        // Newest first, and ordinally within an instant. A report is read for what
        // happened last, which a path ordering buries; CLI-5 needs the ordering to be
        // deterministic rather than chronological, and a tie broken by path is both.
        runs.Sort((a, b) =>
        {
            var byTime = string.CompareOrdinal(b.CreatedUtc, a.CreatedUtc);

            return byTime != 0 ? byTime : string.CompareOrdinal(a.Manifest, b.Manifest);
        });

        if (runs.Count == 0)
        {
            warnings.Add(new ValidityWarning(
                "report.nothing-run",
                "there are no stored runs here, so this report has nothing to account for. "
                + "Results are regenerable and may simply have been discarded (PRJ-4) - "
                + "`einzel run` writes one",
                WarningSeverity.Provenance));
        }

        var withoutResult = runs.Count(ReportOutcome.StoredNothing);

        if (withoutResult > 0)
        {
            // The defect this command was built on top of, kept as a diagnostic because a
            // reader looking at a run with no numbers should be told why rather than left
            // to conclude the run produced none.
            warnings.Add(new ValidityWarning(
                "report.manifest-without-result",
                $"{withoutResult} run(s) here stored a manifest and no result document, so "
                + "their provenance is complete and their answer is nowhere. Re-running the "
                + "model stores one",
                WarningSeverity.Qualified));
        }

        var interrupted = runs.Count(ReportOutcome.WasInterrupted);

        if (interrupted > 0)
        {
            // Said rather than counted silently, and said differently from a run that
            // stored nothing: this one ran, and what it wants is longer, a coarser mesh, or
            // a machine nobody is going to reboot. It may also still be going, which is the
            // commonest reason to be reading a report at all.
            warnings.Add(new ValidityWarning(
                "report.run-interrupted",
                $"{interrupted} run(s) here left a checkpoint and no result, so they did not "
                + "finish - or have not finished yet. Each says how far it got and carries "
                + "the phases that completed; the checkpoint is removed when a run writes "
                + "its answer",
                WarningSeverity.Qualified));
        }

        var unreadable = runs.Count(ReportOutcome.StoredSomethingUnreadable);

        if (unreadable > 0)
        {
            // A different problem from having stored nothing, and a worse one: the answer
            // is on disk and this build cannot load it, so PRJ-3's regenerate-and-compare
            // has a document it cannot compare against. Named separately because
            // re-running the model fixes the first and not this.
            warnings.Add(new ValidityWarning(
                "report.result-unreadable",
                $"{unreadable} stored result(s) here do not read back as a run result, so "
                + "their numbers cannot be shown. That is a defect in this surface rather "
                + "than in the project - the reason is given on each run",
                WarningSeverity.ValidityViolation));
        }

        var notDrawn = runs.Count(ReportOutcome.StoredAStudysAnswer);

        if (notDrawn > 0)
        {
            // A stated gap rather than a fault, and it is said once at the top so a reader
            // does not go looking for a defect. Advisory, since nothing about the project
            // or the stored answers is wrong - this page simply draws one of the two kinds.
            warnings.Add(new ValidityWarning(
                "report.study-not-rendered",
                $"{notDrawn} stored answer(s) here belong to studies rather than runs - a "
                + "sweep, scan, optimisation or boundary search - and this report does not "
                + "draw that kind yet. They are complete, and readable through the study "
                + "verbs' own `--json`",
                WarningSeverity.Advisory));
        }

        return new ReportOutcome(layout.Root, runs, [.. Figures(layout)], warnings);
    }

    /// <summary>Accounts for the project a model belongs to.</summary>
    /// <param name="modelPath">Any model in it.</param>
    /// <returns>The account.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is blank.</exception>
    /// <remarks>
    /// The walk lives here rather than in the caller for the reason
    /// <see cref="ProjectCommand.ForModel"/> gives, and falls back to the model's own
    /// directory rather than the working directory - the correction
    /// <c>InferProjectRoot</c> needed after a study wrote into whatever tree the caller
    /// happened to be standing in.
    /// </remarks>
    public static ReportOutcome ForModel(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        var absolute = Path.GetFullPath(modelPath);

        return Execute(
            ProjectLayout.Find(absolute)?.Root
            ?? Path.GetDirectoryName(absolute)
            ?? ".");
    }

    /// <summary>One run, from its manifest and whatever answer sits beside it.</summary>
    private static ReportedRun Describe(ProjectLayout layout, VerifiedResult verified)
    {
        var manifestPath = Path.Combine(layout.Root, verified.Manifest);
        RunManifest? manifest;

        try
        {
            manifest = RunManifest.FromJson(File.ReadAllText(manifestPath));
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException
                or System.Text.Json.JsonException)
        {
            // Verify read this file a moment ago, so what is left is a race - and reporting
            // on a project while a study is writing into it is the ordinary case rather than
            // an exotic one, since the reason to want a report is that something has been
            // running. Reported as a run that could not be read, not thrown: one unreadable
            // manifest must not take the account of every other run down with it.
            manifest = null;
        }

        if (manifest is null)
        {
            return new ReportedRun(
                verified.Manifest, null, verified.Model, verified.RecordedModel,
                "", "", 0, "", "", "", null, null, [], [], [], [],
                false, verified.Drift, verified.Notes,
                "this manifest cannot be read, so nothing about the run it describes can "
                + "be established",
                null,
                null);
        }

        // The result document sits beside the manifest under the same stem. Derived from
        // the manifest's own name rather than from the model's, because a study writes
        // `tolerance.sweep.manifest.json` for `models/tolerance.json` - the mistake verify
        // was corrected for, met here in the same shape.
        var stem = verified.Manifest.EndsWith(".manifest.json", StringComparison.Ordinal)
            ? verified.Manifest[..^".manifest.json".Length]
            : Path.ChangeExtension(verified.Manifest, null);

        var resultRelative = stem + ".result.json";
        var resultPath = Path.Combine(layout.Root, resultRelative);

        RunOutcome? stored = null;
        string? unreadable = null;
        string? notRendered = null;
        string? unfinished = null;
        IReadOnlyList<ReportedPhase> phases = [];

        if (File.Exists(resultPath))
        {
            try
            {
                stored = CommandJson.Read<RunOutcome>(File.ReadAllText(resultPath));
            }
            catch (Exception exception)
                when (exception is System.Text.Json.JsonException or NotSupportedException
                    or IOException or UnauthorizedAccessException)
            {
                // Named rather than swallowed. A result an older build wrote may not read
                // back into this build's record, and "there is a file here I could not
                // read" is a different statement from "this run produced no numbers". The
                // IO cases are the same race the manifest read above guards against - a
                // report is most wanted while something is still writing.
                unreadable =
                    $"'{resultRelative}' is there and does not read back as a run result: "
                    + exception.Message;
            }
        }
        else if (File.Exists(Path.Combine(layout.Root, stem + ".progress.json")))
        {
            // A RUN THAT DID NOT FINISH, AND ITS OWN ACCOUNT OF HOW FAR IT GOT. The
            // checkpoint exists because a diffusive window can be hundreds of thousands of
            // steps and a machine nobody controls reboots; reporting such a run as "its
            // answer is nowhere" is true and discards the phases that DID finish, which for
            // a study that splits a hold into phases is most of the measurement.
            unfinished = Unfinished(
                Path.Combine(layout.Root, stem + ".progress.json"), out phases, out _);
        }
        else if (File.Exists(Path.Combine(layout.Root, stem + ".json")))
        {
            // A STUDY'S ANSWER, WHICH IS NOT A RUN'S. `results/` holds two kinds of stored
            // answer: a run writes `X.result.json` beside `X.manifest.json`, and a study
            // writes `X.json` beside it - a sweep, a scan, an optimisation or a boundary
            // search, each with its own record shape rather than a `RunOutcome`.
            //
            // The first version of this looked only for `X.result.json`, so every study
            // result in a project was reported as a run that stored nothing, complete with
            // the warning about it - the same "wired into N-1 of N" mistake this command
            // was written to expose, made in the command itself, and it would have been
            // loudest on the projects with the most work in them.
            //
            // Named rather than rendered. Reading four more record types to show a
            // distribution or a bisection bracket is a real extension of this report and
            // not a line of plumbing, so what it says is that the answer is there and this
            // page does not draw it yet - which is a different statement from an answer
            // that was never stored.
            notRendered =
                $"'{stem}.json' is a study's or a fly's answer rather than a run's - a sweep, "
                + "scan, optimisation or boundary search, or an ion-list flight - and this report "
                + "does not draw one yet. The document is there and complete; `einzel "
                + "sweep|scan|optimise|boundary|fly --json` reads it";
        }

        return new ReportedRun(
            verified.Manifest,
            stored is null ? null : resultRelative,
            verified.Model,
            verified.RecordedModel,
            manifest.ModelHash,
            manifest.EngineVersion,
            manifest.SolverBehaviourVersion,
            manifest.TransportMode,
            manifest.Machine,
            manifest.CreatedUtc,
            stored?.Outcome,
            stored?.Completed,
            stored is null ? [] : [.. Numbers(stored)],
            stored is null ? phases : Phases(stored),
            stored is null ? [] : [.. Warnings(stored)],
            // THE RESULT DOCUMENT IS ADDED IF THE STORED LIST OMITS IT, AND IT ALWAYS DOES.
            // A run appends the result path to its artifacts *after* serialising, because a
            // document cannot list itself before it exists - so the stored list is complete
            // about everything except the file it is. The terminal shows the fuller list
            // because it prints the returned record rather than the written one, and a
            // report reading the file would otherwise say a run wrote no result while
            // quoting numbers out of it.
            stored is null
                ? []
                : [.. stored.Artifacts.Contains(resultRelative, StringComparer.OrdinalIgnoreCase)
                    ? stored.Artifacts
                    : [.. stored.Artifacts, resultRelative]],
            verified.Current,
            verified.Drift,
            verified.Notes,
            unreadable,
            notRendered,
            unfinished);
    }

    /// <summary>Runs that left a checkpoint and no manifest, because they were killed.</summary>
    /// <param name="layout">The project.</param>
    /// <param name="described">The runs already accounted for, by manifest.</param>
    /// <returns>One per orphan checkpoint, newest first is applied by the caller.</returns>
    /// <remarks>
    /// <para>
    /// <b>Only the orphans.</b> A checkpoint beside a manifest is already reported through
    /// that manifest, and a run that finished has no checkpoint at all - it is removed when
    /// the answer is written. So this is the narrow case that nothing else can see, which is
    /// also the commonest way a long run ends on a machine nobody controls.
    /// </para>
    /// <para>
    /// <b>The guard is matched on the file name, because the two sides of it did not agree
    /// about a separator.</b> A checkpoint's stem was normalised through
    /// <see cref="RunManifest.Portable"/> and a manifest's was taken from the record as it
    /// stood, which on Windows is `results\name.manifest.json` against
    /// `results/name.progress.json` - equal under no string comparison there is. So the
    /// guard never fired, and a finished run whose stem also had a checkpoint was reported
    /// twice: once as standing, and once as a run that "left a checkpoint and no result",
    /// which was said of a run that had one. The counts then did not partition the runs,
    /// which is the property a lesson was written about the last time it broke here.
    /// </para>
    /// <para>
    /// Matching on the file name rather than normalising both sides, because both
    /// enumerations are over <c>layout.Results</c> itself - a flat directory, so a stem is
    /// unique in it and a separator cannot enter the comparison at all.
    /// </para>
    /// <para>
    /// <b>A checkpoint beside a result is usually a LATER attempt</b> that was abandoned -
    /// the one that produced the result removed its own. It is skipped rather than reported,
    /// because what the page is for is saying what stands, and an abandoned re-run of a
    /// question already answered is not a state anybody has to act on.
    /// </para>
    /// </remarks>
    private static IEnumerable<ReportedRun> Interrupted(
        ProjectLayout layout, IReadOnlyList<ReportedRun> described)
    {
        if (!Directory.Exists(layout.Results))
        {
            yield break;
        }

        var accounted = described
            .Select(run => run.Manifest.EndsWith(".manifest.json", StringComparison.Ordinal)
                ? run.Manifest[..^".manifest.json".Length]
                : Path.ChangeExtension(run.Manifest, null))
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var checkpoints = Directory
            .EnumerateFiles(layout.Results, "*.progress.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal);

        foreach (var path in checkpoints)
        {
            var relative = RunManifest.Portable(Path.GetRelativePath(layout.Root, path));
            var stem = relative[..^".progress.json".Length];

            if (accounted.Contains(Path.GetFileName(stem)))
            {
                continue;
            }

            var sentence = Unfinished(path, out var phases, out var run);

            if (sentence is null || run is null)
            {
                continue;
            }

            yield return new ReportedRun(
                relative,
                null,
                File.Exists(Path.Combine(layout.Root, run.Model)) ? run.Model : null,
                run.Model,
                run.ModelHash,
                run.EngineVersion,
                run.SolverBehaviourVersion,

                // From the phases it got through, since nothing recorded the intent. Empty
                // where it was killed before the first one finished, which is honest: the
                // modes a run *used* are not knowable from a run that has not used them.
                string.Join(
                    " -> ", phases.Select(p => p.Mode).Distinct(StringComparer.Ordinal)),
                run.Machine,
                run.StartedUtc,
                null,
                false,
                [],
                phases,
                [],
                [relative],

                // NEVER CURRENT, whatever the model hash says. `verify`'s question is
                // whether a stored answer still stands, and there is no stored answer -
                // reporting an interrupted run as current would be the shape of answer that
                // stops an investigation.
                false,
                [],
                [],
                null,
                null,
                sentence);
        }
    }

    /// <summary>How far a run that never finished had got, from its own checkpoint.</summary>
    /// <param name="path">The checkpoint.</param>
    /// <param name="phases">The phases that finished, whole, in the shape the page draws.</param>
    /// <param name="run">Which run it belongs to, as a manifest would say it.</param>
    /// <returns>A sentence, or null where the checkpoint cannot be read.</returns>
    /// <remarks>
    /// <b>The completed phases go through the same rendering a finished run's do</b>, because
    /// they are the same record - a checkpoint and a result describe a phase through one
    /// conversion. So the timeline table on this page does not know or care whether the run
    /// that produced it lived to write an answer, which is what makes a killed run's five
    /// finished phases worth as much as a finished run's.
    /// </remarks>
    private static string? Unfinished(
        string path,
        out IReadOnlyList<ReportedPhase> phases,
        out RunCheckpointProvenance? run)
    {
        phases = [];
        run = null;

        RunCheckpointJson? checkpoint;

        try
        {
            checkpoint = CommandJson.Read<RunCheckpointJson>(File.ReadAllText(path));
        }
        catch (Exception exception)
            when (exception is System.Text.Json.JsonException or NotSupportedException
                or IOException or UnauthorizedAccessException)
        {
            // The commonest reason to be reading a project is that something is still
            // running in it, so a checkpoint half written is the ordinary case rather than
            // an exotic one - and it is not worth a diagnostic of its own, since the run it
            // belongs to will overwrite it within the interval.
            return null;
        }

        if (checkpoint is null)
        {
            return null;
        }

        phases = [.. checkpoint.Completed.Select(Rendered)];
        run = checkpoint.Run;

        var culture = System.Globalization.CultureInfo.InvariantCulture;

        var where = checkpoint.PhaseCount > 1
            ? string.Format(
                culture,
                "phase {0} of {1}, '{2}', ",
                checkpoint.PhaseIndex,
                checkpoint.PhaseCount,
                checkpoint.Phase)
            : "";

        return string.Format(
            culture,
            "this run did not finish. Its own checkpoint has it {0}{1:G6} us into {2:G6}, "
            + "after {3:N0} step(s) and {4:F0} s of wall clock, with {5} phase(s) complete. "
            + "Re-running the model replaces this with an answer",
            where,
            checkpoint.AtUs,
            checkpoint.OfUs,
            checkpoint.Steps,
            checkpoint.ElapsedSeconds,
            checkpoint.Completed.Count);
    }

    /// <summary>One stored phase, in the shape the page draws.</summary>
    private static ReportedPhase Rendered(SequencePhaseJson phase)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        return new ReportedPhase(
            phase.Name,
            phase.Mode,
            phase.EndsAtUs.ToString("G6", culture),
            phase.Population.ToString("G6", culture),
            phase.Mode == "diffusion" ? null : phase.Trajectories,
            Axis(phase.CentroidMm, 0) ?? "—",
            Axis(phase.SpreadMm, 0),
            Axis(phase.SpreadMm, 1),
            phase.Converted);
    }

    /// <summary>One component of a length, formatted, or null where there is none.</summary>
    private static string? Axis(IReadOnlyList<double>? components, int axis)
        => components is not null && components.Count > axis
            ? components[axis].ToString(
                "F4", System.Globalization.CultureInfo.InvariantCulture)
            : null;

    /// <summary>
    /// The numbers a run produced, in the order a reader wants them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing is invented and nothing is recomputed.</b> Every entry is a field of the
    /// stored result; a figure the run did not produce is absent rather than zero, which is
    /// the rule the rest of this surface reached after four non-finite doubles took a
    /// serialiser down. So a diffusive run has no flight time here, and that is a
    /// statement rather than a gap.
    /// </para>
    /// <para>
    /// <b>The flight time is asked for rather than inferred.</b> <c>HasFlightTime</c> is a
    /// required member set at each construction site with its reason, precisely so a fifth
    /// kind of run cannot walk back into <c>flight time NaN +/- NaN</c> - and reading the
    /// mode to decide is the proxy that stopped being equivalent the moment a third mode
    /// existed.
    /// </para>
    /// </remarks>
    private static IEnumerable<ReportedNumber> Numbers(RunOutcome run)
    {
        if (run.HasFlightTime)
        {
            var flight = run.FlightTime;

            yield return new ReportedNumber(
                "flight time",
                flight.Value.ToString("G7", System.Globalization.CultureInfo.InvariantCulture),
                flight.Unit,
                Interval(flight),
                flight.Evidence.Kind);
        }

        if (run.Ensemble is { } ensemble)
        {
            yield return Count("launched", ensemble.Launched);
            yield return Count("arrived", ensemble.Arrived);

            yield return Percent("transmission", ensemble.Transmission);

            // Only where it says something. A line of zeros on every beamline is noise,
            // which is the argument `einzel run` reached for the same quantity.
            if (ensemble.Confined.Value > 0.0)
            {
                yield return Percent("confined", ensemble.Confined);
            }

            if (ensemble.Collisions > 0)
            {
                yield return Count("collisions", ensemble.Collisions);
            }
        }

        if (run.Diffusion is { } diffusion)
        {
            yield return Number("population launched", diffusion.Launched, "ions", "G6");
            yield return Number("collected", diffusion.Collected, "ions", "G6");
            yield return Number("still inside", diffusion.Remaining, "ions", "G6");
            yield return Fraction("transmission", diffusion.Transmission);

            if (diffusion.MeanTransitUs is { } transit)
            {
                yield return Number("mean transit", transit, "us", "G6");
            }

            if (diffusion.TransitSpreadUs is { } spread)
            {
                yield return Number("transit spread", spread, "us", "G6");
            }

            yield return Number(
                diffusion.MobilityDerived
                    ? "mobility (derived from the cross section)"
                    : "mobility (as declared)",
                diffusion.MobilitySi,
                "m^2/(V s)",
                "G6");

            yield return Count("steps", diffusion.Steps);
        }

        if (run.Sequence is { } sequence)
        {
            yield return Count("phases", sequence.Phases.Count);
            yield return Count("mode conversions", sequence.Conversions);
            yield return Number("arrived", sequence.ArrivedIons, "ions", "G6");

            if (sequence.MeanArrivalUs is { } arrival)
            {
                yield return Number("mean arrival", arrival, "us", "G6");
            }

            if (sequence.ArrivalSpreadUs is { } spread)
            {
                yield return Number("arrival spread", spread, "us", "G6");
            }
        }

        if (run.Mixture is { } mixture)
        {
            yield return Count("populations", mixture.Species.Count);
            yield return Count("shared steps", mixture.Steps);
        }

        // THE REGIME, WHETHER OR NOT ANYTHING CROSSED A THRESHOLD. REG-2's own argument is
        // that "a reader who sees Kn = 40 knows the run was checked; one who sees nothing
        // cannot tell that from its not having been checked" - which is a statement about
        // a reader, so it reaches this page or it does not hold here at all. Dropping the
        // block because no warning fired would be the ninth time evidence about a
        // computation was dropped at a seam in this project, on the requirement that
        // exists to prevent exactly that.
        if (run.Regime is { } regime)
        {
            yield return Number("gas pressure", regime.PressureMbar, "mbar", "G4");
            yield return Number("mean free path", regime.MeanFreePathMm, "mm", "G4");
            yield return Number("Knudsen number", regime.Knudsen, "", "G4");
            yield return Number(
                "collisions per flight", regime.CollisionsPerFlight, "", "G4");

            // Absent where there is no drive, because an ion that completes no oscillation
            // in a field that has none is not a small number of collisions per cycle.
            if (regime.CollisionsPerRfCycle is { } perCycle)
            {
                yield return Number("collisions per RF cycle", perCycle, "", "G4");
            }
        }

        // Energy drift is a diagnostic rather than a figure of merit, and it is NaN by
        // design wherever there is no conserved scale to be relative to - a driven field
        // does work deliberately, and an ion released from rest at zero potential has no
        // scale at all. Absent, because zero is this quantity's ideal answer and printing
        // it for "not measured" is the exact confusion that reached four shipped examples.
        if (double.IsFinite(run.MaximumRelativeEnergyDrift))
        {
            yield return new ReportedNumber(
                "energy drift",
                run.MaximumRelativeEnergyDrift.ToString(
                    "G3", System.Globalization.CultureInfo.InvariantCulture),
                "relative",
                null,
                "ACC-4 budget 1e-6");
        }
    }

    /// <summary>
    /// The timeline a sequenced run walked, one entry per phase, in order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Read from the stored phases and nothing else.</b> A phase's width is what the
    /// transport reported at that boundary - the density solver's second moment on a
    /// diffusive leg and the same quantity over the trajectories on the other side of a
    /// conversion - so this is a rendering of the document rather than a second computation
    /// of the packet.
    /// </para>
    /// <para>
    /// <b>A missing width is absent, not zero.</b> A phase that ends with nothing left has
    /// no width, and zero is a real answer for a width: a packet one cell across reports one
    /// and it is a measurement. The two must not print alike, which is the rule the rest of
    /// this surface reached after an undefined Twiss orientation went out as a NaN.
    /// </para>
    /// <para>
    /// <b>One rendering, shared with a checkpoint's.</b> A run that never finished carries
    /// its completed phases in the same record, so both go through <see cref="Rendered"/>
    /// and the page cannot describe a killed run's phase differently from a finished one's.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ReportedPhase> Phases(RunOutcome run)
        => run.Sequence is { } sequence ? [.. sequence.Phases.Select(Rendered)] : [];

    private static ReportedNumber Count(string name, int value)
        => new(
            name,
            value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
            "",
            null,
            null);

    private static ReportedNumber Number(string name, double value, string unit, string format)
        => new(
            name,
            value.ToString(format, System.Globalization.CultureInfo.InvariantCulture),
            unit,
            null,
            null);

    /// <summary>
    /// A quantity stored as a fraction, shown as a percentage - value and interval alike.
    /// </summary>
    /// <remarks>
    /// <b>Both, or neither.</b> Transmission and confinement are stored as fractions and
    /// their intervals are in the same units as their values, so scaling the value and not
    /// the interval prints "0.00 %, 0 to 1" - two numbers about the same quantity in two
    /// different units, side by side, which is the exact ambiguity §9 refuses a model
    /// document for. The first version of this page did that, and it read as a
    /// transmission a hundred times smaller than the run measured.
    /// </remarks>
    private static ReportedNumber Percent(string name, Io.MeasuredJson measured)
        => new(
            name,
            Percentage(measured.Value),
            "%",
            Interval(measured, 100.0),
            measured.Evidence.Kind);

    /// <summary>A bare fraction shown as a percentage, where there is no envelope.</summary>
    private static ReportedNumber Fraction(string name, double fraction)
        => new(name, Percentage(fraction), "%", null, null);

    /// <summary>
    /// A fraction as a percentage, at two decimals unless that would round away a loss.
    /// </summary>
    /// <remarks>
    /// <b>A transmission of 100.00 % has to mean all of them.</b> Two decimals is right for
    /// reading and turns 99.9976 % into "100.00 %", which says every ion arrived when 0.24
    /// of ten thousand did not - and for a diffusive run the density's tail is precisely
    /// where the loss is. So the format widens only where the rounding would land exactly
    /// on nothing or on everything without being there: an ordinary figure stays short, and
    /// the two values that carry a claim of completeness are never claimed falsely.
    /// (<c>einzel run</c> prints one decimal and has the same rounding; matching it would
    /// be matching the wrong thing.)
    /// </remarks>
    private static string Percentage(double fraction)
    {
        var percent = fraction * 100.0;
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        if (percent is > 0.0 and < 100.0
            && Math.Round(percent, 2) is 0.0 or 100.0)
        {
            return percent.ToString("G6", culture);
        }

        return percent.ToString("F2", culture);
    }

    /// <summary>The uncertainty, or nothing where the interval is degenerate.</summary>
    /// <remarks>
    /// A zero-width interval is written as no interval rather than as "+- 0". A residual
    /// of zero is not "no uncertainty", it is one smaller than a comparison of two doubles
    /// can see - the distinction that made an agent refuse to publish
    /// <c>10.180506 +- 0 us</c> and go and measure its own tolerance ladder instead.
    /// </remarks>
    private static string? Interval(Io.MeasuredJson measured, double scale = 1.0)
    {
        var lower = measured.Uncertainty.Lower * scale;
        var upper = measured.Uncertainty.Upper * scale;

        if (!double.IsFinite(lower) || !double.IsFinite(upper) || upper - lower <= 0.0)
        {
            return null;
        }

        var culture = System.Globalization.CultureInfo.InvariantCulture;

        return $"{lower.ToString("G7", culture)} to {upper.ToString("G7", culture)} "
            + $"({measured.Uncertainty.ConfidenceLevel.ToString("P0", culture)})";
    }

    /// <summary>
    /// Every warning on the run, distinct by code, worst first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Gathered from the whole result rather than from one field.</b> A run's warnings
    /// ride on the quantities they qualify, so the flight time carries the field's
    /// provenance and the transmission carries the ensemble's - and a reader wants the set.
    /// GRD-2 is about their travelling; presenting only one field's is how they stop.
    /// </para>
    /// <para>
    /// <b>Distinct by code, and the unsuppressible ones first.</b> GRD-3 makes a validity
    /// violation the class that must never be skimmed, so it may not be below a
    /// housekeeping note in a page somebody reads top to bottom.
    /// </para>
    /// </remarks>
    private static IEnumerable<ValidityWarning> Warnings(RunOutcome run)
    {
        var seen = new Dictionary<string, ValidityWarning>(StringComparer.Ordinal);

        foreach (var warning in Gather(run))
        {
            var restored = new ValidityWarning(
                warning.Code,
                warning.Message,
                // AN UNRECOGNISED SEVERITY IS READ AS THE WORST ONE, not the mildest. The
                // severity is a string on the wire, so a document written by a build that
                // has a severity this one does not know cannot be classified - and the two
                // ways to guess are "quietly demote it" and "keep it loud". GRD-3's whole
                // subject is that a warning the reader must not skim must not be skimmable,
                // so guessing downward would turn an unreadable severity into a silenced
                // one. A false alarm is the cost, and it is the affordable one.
                Enum.TryParse<WarningSeverity>(warning.Severity, out var severity)
                    ? severity
                    : WarningSeverity.ValidityViolation);

            seen.TryAdd(restored.Code, restored);
        }

        return seen.Values
            .OrderBy(w => w.IsSuppressible)
            .ThenBy(w => w.Code, StringComparer.Ordinal);
    }

    private static IEnumerable<Io.WarningJson> Gather(RunOutcome run)
    {
        foreach (var warning in run.FlightTime.Warnings)
        {
            yield return warning;
        }

        if (run.Ensemble is { } ensemble)
        {
            foreach (var warning in ensemble.Transmission.Warnings)
            {
                yield return warning;
            }

            foreach (var warning in ensemble.Confined.Warnings)
            {
                yield return warning;
            }
        }
    }

    /// <summary>Figures already rendered here, so the page can point at them.</summary>
    /// <remarks>
    /// Listed rather than drawn. RND-1 makes rendering an engine capability and
    /// <c>Einzel.Render</c> owns it; a report that re-rendered a section would be a second
    /// caller of that pipeline with its own idea of what the figure should say, and
    /// GRD-12's provenance is stamped on the figure rather than carried alongside it.
    /// </remarks>
    private static IEnumerable<string> Figures(ProjectLayout layout)
    {
        if (!Directory.Exists(layout.Figures))
        {
            yield break;
        }

        var files = Directory.GetFiles(layout.Figures, "*", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);

        foreach (var file in files)
        {
            // DRAWINGS, NOT RENDER SPECS. `figures/` holds both - the spec a person edits
            // and the artifact `einzel render` produced from it - and listing a spec under
            // "figures already rendered here" would say a drawing exists where only the
            // instruction for one does. `einzel project` lists the specs, which is the
            // right place for them.
            if (Path.GetExtension(file).ToLowerInvariant()
                is not (".svg" or ".pdf" or ".png"))
            {
                continue;
            }

            yield return Path.GetRelativePath(layout.Root, file)
                .Replace(Path.DirectorySeparatorChar, '/');
        }
    }
}
