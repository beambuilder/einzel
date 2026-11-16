using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Einzel.Commands;
using Einzel.Core.Errors;

namespace Einzel.Shell.Presentation;

/// <summary>One extension, as the manager shows it.</summary>
/// <remarks>
/// Every field is a string built here rather than a value bound and formatted in XAML, for
/// the reason the other panes follow: what a reader sees is decided in one place that a
/// test can read, not in a template.
/// </remarks>
public sealed class ExtensionRow
{
    /// <summary>Builds a row from a listing entry.</summary>
    /// <param name="entry">The entry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
    public ExtensionRow(ExtensionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Name = entry.Name;
        Version = entry.Version;
        Kind = entry.Kind;
        Trust = entry.Trust;
        Description = entry.Description ?? string.Empty;
        Directory = entry.Directory;

        // LIC-2, and the decision `einzel ext list` already made: an undeclared licence
        // says so rather than being blank. The extension somebody most needs to ask about
        // must not be the one whose line is shortest, and a reader cannot recompute this
        // one for themselves the way they could recompute a version.
        Licence = entry.Licence ?? "NOT DECLARED";
        LicenceDeclared = entry.Licence is not null;

        Incompatibility = entry.Incompatibility ?? string.Empty;
        Usable = entry.Incompatibility is null;
    }

    /// <summary>The extension's name.</summary>
    public string Name { get; }

    /// <summary>Its version.</summary>
    public string Version { get; }

    /// <summary>What it extends - an objective, a geometry, and so on.</summary>
    public string Kind { get; }

    /// <summary>Sandboxed or trusted. The most consequential field on the row.</summary>
    public string Trust { get; }

    /// <summary>Its declared licence, or <c>NOT DECLARED</c>.</summary>
    public string Licence { get; }

    /// <summary>Whether the licence was declared, so the view can mark the ones that were not.</summary>
    public bool LicenceDeclared { get; }

    /// <summary>What the manifest says it does.</summary>
    public string Description { get; }

    /// <summary>Where it lives.</summary>
    public string Directory { get; }

    /// <summary>Why this engine cannot run it, or empty.</summary>
    public string Incompatibility { get; }

    /// <summary>Whether this engine can run it.</summary>
    public bool Usable { get; }
}

/// <summary>
/// The extension manager (§16), and the half of LIC-2 the engine could not satisfy on its
/// own: extensions carry their own licences, and this is what surfaces them.
/// </summary>
/// <remarks>
/// <para>
/// It shows what somebody deciding whether to run third-party code needs before they run
/// it: the licence, the trust level, and <b>what the sandbox does not enforce</b>. That
/// last is not an aside. <c>einzel ext list</c> prints the containment gaps on stderr every
/// single time, because a containment measure claimed and not applied is worse than one
/// absent and known to be - and a window that put them behind a button would be the shell
/// quietly dropping a safety statement the command layer refuses to drop.
/// </para>
/// <para>
/// UI-1: no file-format knowledge here. The listing comes from <c>ExtensionCommand</c>
/// through the session, which is the same call <c>einzel ext list</c> makes, so the window
/// and the command cannot come to disagree about what is installed.
/// </para>
/// </remarks>
public sealed class ExtensionsViewModel : INotifyPropertyChanged
{
    private readonly ShellSession _session;
    private string _status = "not yet read";
    private string _interpreter = string.Empty;

    /// <summary>Opens the manager over a session.</summary>
    /// <param name="session">The session, which knows where the project is.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public ExtensionsViewModel(ShellSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The installed extensions, in the order the command lists them.</summary>
    public ObservableCollection<ExtensionRow> Extensions { get; } = [];

    /// <summary>What the sandbox does not enforce. Never empty while anything is sandboxed.</summary>
    public ObservableCollection<string> Unenforced { get; } = [];

    /// <summary>What the manager is showing.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            Changed(nameof(Status));
        }
    }

    /// <summary>The interpreter that would run a Python extension, and whether it is vendored.</summary>
    public string Interpreter
    {
        get => _interpreter;
        private set
        {
            _interpreter = value;
            Changed(nameof(Interpreter));
        }
    }

    /// <summary>How many listed extensions did not declare a licence.</summary>
    public int Undeclared => Extensions.Count(e => !e.LicenceDeclared);

    /// <summary>Re-reads the project's extensions.</summary>
    /// <returns>Whether anything is installed.</returns>
    public bool Refresh()
    {
        ExtensionListOutcome? outcome;

        try
        {
            outcome = _session.Extensions();
        }
        catch (EinzelException refusal)
        {
            Clear();

            Status = refusal.Error.Constraint
                + (refusal.Error.Suggestion is { } how ? $" - {how}" : string.Empty);

            return false;
        }

        Clear();

        if (outcome is null)
        {
            // Not the same as having none, and worth saying differently: there is nowhere
            // for an extension to live, so nothing is missing.
            Status = "this model is not inside a project, so there is no extensions folder to read";
            return false;
        }

        foreach (var entry in outcome.Extensions)
        {
            Extensions.Add(new ExtensionRow(entry));
        }

        foreach (var gap in outcome.UnenforcedContainment)
        {
            Unenforced.Add(gap);
        }

        Interpreter = outcome.Interpreter is { } found
            ? $"{found} (discovered, not vendored - EXT-6 wants one shipped)"
            : "none found - a Python extension cannot run here";

        Changed(nameof(Undeclared));

        if (Extensions.Count == 0)
        {
            Status = "no extensions installed - einzel ext register scaffolds one that runs";
            return false;
        }

        var undeclared = Undeclared;
        var sandboxed = Extensions.Count(
            e => string.Equals(e.Trust, "sandboxed", StringComparison.OrdinalIgnoreCase));
        var plural = Extensions.Count == 1 ? string.Empty : "s";
        var licences = undeclared > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{undeclared} with no declared licence")
            : "every licence declared";

        Status = string.Create(
            CultureInfo.InvariantCulture,
            $"{Extensions.Count} extension{plural}, {sandboxed} sandboxed, {licences}");

        return true;
    }

    private void Clear()
    {
        Extensions.Clear();
        Unenforced.Clear();
        Interpreter = string.Empty;
        Changed(nameof(Undeclared));
    }

    private void Changed(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
