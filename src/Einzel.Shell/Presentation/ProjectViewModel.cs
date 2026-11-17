using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

using Einzel.Commands;
using Einzel.Core.Errors;

namespace Einzel.Shell.Presentation;

/// <summary>One model in the project, as a row.</summary>
public sealed class ProjectRow
{
    /// <summary>Creates a row from a model's state.</summary>
    /// <param name="model">The model.</param>
    /// <param name="open">Whether it is the one the window has open.</param>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    public ProjectRow(ProjectModel model, bool open)
    {
        ArgumentNullException.ThrowIfNull(model);

        Path = model.Path;
        Open = open;

        // Four states, kept apart because each calls for something different: invalid is a
        // thing to fix, stale is a thing to re-run, never-run is where every model starts,
        // and current is the only one that needs nothing.
        (State, Severity) =
            !model.Valid ? ("invalid", "bad")
            : !model.Ran ? ("not run", "neutral")
            : model.Current ? ("current", "good")
            : ("stale", "warn");

        Detail = model.Problem
            ?? (model.Drift.Count > 0 ? string.Join("; ", model.Drift) : null)
            ?? (model.Notes.Count > 0 ? string.Join("; ", model.Notes) : null)
            ?? (model.Ran ? "the stored result still stands" : "no stored result");

        // The mode only where it is not the default, since a column reading "trajectory"
        // on every row is one a reader learns to skip - and then misses the diffusive one.
        Mode = model.TransportMode is { } mode && mode != "trajectory" ? mode : string.Empty;
    }

    /// <summary>Where it is, relative to the project root.</summary>
    public string Path { get; }

    /// <summary>Whether its state is a thing to fix.</summary>
    /// <remarks>
    /// The severity as yes-or-no questions, which is what a style selector can ask. Four
    /// fixed colours keyed on the state word would paint "not run" like a problem, and it is
    /// where every model starts.
    /// </remarks>
    public bool Bad => Severity == "bad";

    /// <summary>Whether its state is a thing to re-run.</summary>
    public bool Warn => Severity == "warn";

    /// <summary>Whether its state needs nothing.</summary>
    public bool Good => Severity == "good";

    /// <summary>Whether the window has this one open.</summary>
    public bool Open { get; }

    /// <summary>invalid, not run, stale or current.</summary>
    public string State { get; }

    /// <summary>How the state should read: bad, warn, neutral or good.</summary>
    public string Severity { get; }

    /// <summary>Why, in one line.</summary>
    public string Detail { get; }

    /// <summary>The transport mode, where it is not the default.</summary>
    public string Mode { get; }
}

/// <summary>
/// The project the open model belongs to, and the state of each part of it (§16).
/// </summary>
/// <remarks>
/// <para>
/// §16 asks for model-drift and engine-drift state, which <c>einzel verify</c> computes
/// and separates: an edited model or a changed solver-behaviour version invalidates a
/// result, while a different engine build with identical numerics does not.
/// </para>
/// <para>
/// <b>What verify cannot answer is what has never been run.</b> It walks the manifests, so
/// a model with no result is reported by neither its success nor its failure - and that is
/// the state most models in a working project are in. <see cref="ProjectCommand"/> adds it.
/// </para>
/// <para>
/// The root is found from the open model by the command layer, not here: UI-1 puts project
/// layout outside the shell along with the rest of the file format, and a window that knew
/// where <c>models/</c> sits would grow its own idea of what a project is.
/// </para>
/// </remarks>
public sealed class ProjectViewModel : INotifyPropertyChanged
{
    private readonly ShellSession _session;
    private string _status = "not yet read";

    /// <summary>Opens the view over a session.</summary>
    /// <param name="session">The session, which owns the model.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public ProjectViewModel(ShellSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The models, ordered by path.</summary>
    public ObservableCollection<ProjectRow> Models { get; } = [];

    /// <summary>Studies, figures, tests and extensions, as lines.</summary>
    public ObservableCollection<string> Contents { get; } = [];

    /// <summary>What the reader needs alongside it (GRD-2).</summary>
    public ObservableCollection<string> Warnings { get; } = [];

    /// <summary>What the view is showing.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            Changed(nameof(Status));
        }
    }

    /// <summary>Re-reads the project.</summary>
    /// <returns>Whether anything in it needs attention.</returns>
    public bool Refresh()
    {
        ProjectOutcome outcome;

        try
        {
            outcome = _session.Project();
        }
        catch (EinzelException refusal)
        {
            Models.Clear();
            Contents.Clear();
            Warnings.Clear();

            // AGT-3's error is already a recovery instruction, so it is shown rather than
            // reworded.
            Status = refusal.Error.Constraint
                + (refusal.Error.Suggestion is { } how ? $" - {how}" : string.Empty);

            return false;
        }

        var open = _session.Journal.ModelPath;

        Models.Clear();

        foreach (var model in outcome.Models)
        {
            Models.Add(new ProjectRow(
                model,
                string.Equals(
                    System.IO.Path.GetFullPath(System.IO.Path.Combine(outcome.Root, model.Path)),
                    System.IO.Path.GetFullPath(open),
                    StringComparison.OrdinalIgnoreCase)));
        }

        Contents.Clear();

        foreach (var (label, items) in new (string, IReadOnlyList<string>)[]
        {
            ("studies", outcome.Studies),
            ("figures", outcome.Figures),
            ("tests", outcome.Tests),
            ("extensions", outcome.ExtensionNames),
        })
        {
            if (items.Count > 0)
            {
                Contents.Add($"{items.Count} {label}: {string.Join(", ", items)}");
            }
        }

        foreach (var orphan in outcome.Orphans)
        {
            Contents.Add($"orphaned result: {orphan.Manifest}");
        }

        Warnings.Clear();

        foreach (var warning in outcome.Warnings)
        {
            Warnings.Add($"[{warning.Severity}] {warning.Code}: {warning.Message}");
        }

        var wrong = outcome.Models.Count(m => !m.Valid) + outcome.Drifted;

        Status = string.Create(
            CultureInfo.InvariantCulture,
            $"{outcome.Root} - {outcome.Models.Count} models, {outcome.NeverRun} never run, "
            + $"{outcome.Drifted} stale");

        return wrong == 0;
    }

    private void Changed(string property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
