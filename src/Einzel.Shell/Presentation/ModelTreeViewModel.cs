using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

using Einzel.Commands;
using Einzel.Core.Errors;

namespace Einzel.Shell.Presentation;

/// <summary>One parameter, as a row in the model tree.</summary>
/// <remarks>
/// <para>
/// A view of what <see cref="OutlineCommand"/> reported, and nothing more. This holds no
/// idea of what a parameter means, what its bounds imply, or whether a value is
/// reasonable - all of that is the engine's, reached through the command layer (UI-1).
/// </para>
/// <para>
/// <b>The unit is not decoration.</b> §16 asks for units on every field, and §9's rule
/// that <c>{"energy": 4000}</c> is a validation error exists because unit ambiguity is
/// the commonest source of silent wrongness. A row that showed 7 without saying
/// millimetres would be reintroducing exactly that at the point of entry.
/// </para>
/// </remarks>
public sealed class ParameterRow(ParameterOutline outline)
{
    /// <summary>What the parameter is called.</summary>
    public string Name { get; } = outline.Name;

    /// <summary>Its value, or the expression that derives it.</summary>
    public string Shown { get; } = outline.Expression
        ?? outline.Value?.ToString("G6", CultureInfo.InvariantCulture)
        ?? string.Empty;

    /// <summary>The unit its value is in.</summary>
    public string Unit { get; } = outline.Unit ?? string.Empty;

    /// <summary>The bounds it declares, as a phrase, or empty when it declares none.</summary>
    public string Bounds { get; } = OutlineCommand.BoundsText(outline) ?? string.Empty;

    /// <summary>What it means.</summary>
    public string Description { get; } = outline.Description ?? string.Empty;

    /// <summary>Whether a person may type into it.</summary>
    public bool Editable { get; } = outline.Editable;

    /// <summary>What it currently works out to, with its unit.</summary>
    /// <remarks>
    /// <para>
    /// Shown for a derived parameter especially: a reader wants to see both that the rod
    /// radius <em>is</em> <c>inscribedRadius * rodRatio</c> and what that came to.
    /// </para>
    /// <para>
    /// <b>With the SI unit spelled out rather than the word "SI".</b> §16 asks for units
    /// on every field and GRD-1's habit is that a value never appears without one,
    /// because unit ambiguity is the commonest source of silent wrongness. "0.007 SI"
    /// leaves a reader to work out which SI unit a given row is in, which for a tree
    /// mixing lengths, voltages and dimensionless ratios is exactly the inference the
    /// format exists to remove.
    /// </para>
    /// </remarks>
    public string Resolved { get; } = outline.ResolvedSi is { } si
        ? si.ToString("G6", CultureInfo.InvariantCulture) + SiUnit(outline.Unit)
        : string.Empty;

    /// <summary>The line under the row: a derived parameter's expression, or a free one's bounds.</summary>
    /// <remarks>
    /// <para>
    /// For the cross-platform window, which shows a row as two lines rather than five
    /// columns, so the tree fits beside the viewport. A derived parameter's value cell holds
    /// no number - there is nothing to type there - so this line carries both what it is
    /// over and what that came to, which is the pair a reader needs in order to know which
    /// knob to turn.
    /// </para>
    /// <para>
    /// A free parameter's resolved value is left off it: <c>4 kV</c> beside <c>4000 V</c>
    /// says the same thing twice, and a second line on every row teaches the eye to skip
    /// the line that matters on the derived ones.
    /// </para>
    /// </remarks>
    public string Detail => Editable
        ? Bounds
        : "= " + Shown + (Resolved.Length > 0 ? "  →  " + Resolved : string.Empty);

    /// <summary>What a screen reader says about the row's value box beyond its name.</summary>
    /// <remarks>
    /// <para>
    /// The unit and the bounds, because those are what the label and the line beneath it show
    /// a sighted reader and neither is part of the box. A value read out as "50" without
    /// "millimetres" is the unit ambiguity §9 refuses in a document, met at the keyboard.
    /// </para>
    /// <para>
    /// A ratio's unit is written <c>1</c>, which is right in a document and reads as "in 1"
    /// aloud, so it is said as what it means.
    /// </para>
    /// </remarks>
    public string Help => (Unit.Length > 0 && Unit != "1" ? "in " + Unit : "dimensionless")
        + (Bounds.Length > 0 ? ", " + Bounds : string.Empty);

    /// <summary>The SI unit a declared unit reduces to, as a suffix.</summary>
    /// <remarks>
    /// Asked of the unit registry rather than mapped here, because which SI unit a
    /// dimension has is format knowledge and UI-1 puts that outside the shell. A unit the
    /// registry does not know reduces to nothing, and the value is then shown bare rather
    /// than with a guess.
    /// </remarks>
    private static string SiUnit(string? declared)
    {
        if (string.IsNullOrWhiteSpace(declared))
        {
            return string.Empty;
        }

        var symbol = OutlineCommand.SiUnitOf(declared);

        return string.IsNullOrEmpty(symbol) ? string.Empty : " " + symbol;
    }
}

/// <summary>What became of a value typed into the model tree.</summary>
public enum ParameterEdit
{
    /// <summary>The text is what the row already shows, so nothing was done.</summary>
    Unchanged,

    /// <summary>The edit went through the journal and the tree was re-read.</summary>
    Applied,

    /// <summary>Nothing was applied, and <see cref="ModelTreeViewModel.Status"/> says why.</summary>
    Refused,
}

/// <summary>
/// The model tree: what the model declares, editable, validated as it is typed (§16).
/// </summary>
/// <remarks>
/// <para>
/// <b>It knows no file format.</b> UI-1 puts format knowledge outside the shell, so the
/// tree is whatever <c>einzel outline</c> reported and an edit is whatever
/// <c>OutlineCommand.WithParameter</c> produced. A window that parsed the document to
/// build a tree would be growing its own idea of what a model is, and the two would come
/// to disagree.
/// </para>
/// <para>
/// <b>Every edit goes through the journal</b>, not to the file, so a change made here is
/// undoable by an agent connected to the same session and vice versa (MCP-1, GRD-9).
/// </para>
/// <para>
/// <b>Validation is live and never blocks.</b> A parameter typed out of bounds leaves the
/// tree standing with the error against it, because a tree that vanished when the value
/// went invalid would disappear exactly when it is most needed. That is the same
/// taint-never-block rule the rest of the platform follows, applied to input.
/// </para>
/// </remarks>
public sealed class ModelTreeViewModel : INotifyPropertyChanged
{
    private readonly ShellSession _session;
    private string _status = string.Empty;
    private bool _valid;

    /// <summary>Opens the tree over a session.</summary>
    /// <param name="session">The session, which owns the journal and the model.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public ModelTreeViewModel(ShellSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;

        Refresh();
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The parameters, in document order.</summary>
    public ObservableCollection<ParameterRow> Parameters { get; } = [];

    /// <summary>What the journal says has happened, one line per entry.</summary>
    public ObservableCollection<string> Journal { get; } = [];

    /// <summary>
    /// Every action this session has taken, as the command line that would repeat it.
    /// </summary>
    /// <remarks>
    /// Amendment 25 in the window: a person can read what they just did as something
    /// they could have typed, and hand it to an agent unchanged.
    /// </remarks>
    public ObservableCollection<string> Commands { get; } = [];

    /// <summary>Whether the model validates as it stands.</summary>
    public bool Valid
    {
        get => _valid;
        private set
        {
            _valid = value;
            Changed(nameof(Valid));
        }
    }

    /// <summary>What validation had to say, or the model's name when it is happy.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            Changed(nameof(Status));
        }
    }

    /// <summary>Whether there is an edit that still stands, to be reversed.</summary>
    /// <remarks>
    /// The journal's answer rather than a count kept here, because an agent on the same
    /// session can make or reverse an edit between two of the window's.
    /// </remarks>
    public bool CanUndo => _session.Journal.CanUndo;

    /// <summary>Takes a value as a person typed it, and applies it if it says something.</summary>
    /// <param name="row">The row it was typed into.</param>
    /// <param name="text">What is in the box.</param>
    /// <returns>What became of it; <see cref="Status"/> says why when it was refused.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="row"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>Here rather than in the window, so it is tested where the window cannot be.</b>
    /// Every rule in it is one a person meets at the keyboard, and each has a way to be
    /// wrong that a test can see and a screenshot cannot: a box left as it was must record
    /// nothing, or tabbing through the tree writes a journal entry per row it passes.
    /// </para>
    /// <para>
    /// <b>Parsed invariantly, whatever the machine's locale</b>, because the document being
    /// edited is invariant: <c>1,5</c> is refused rather than read as fifteen, which is what
    /// a parser told to accept group separators would make of it.
    /// </para>
    /// <para>
    /// A derived parameter is refused by name before anything is parsed. Its value is its
    /// expression's, so there is nothing to set, and saying which parameters it is over is
    /// more use than saying the box is read-only.
    /// </para>
    /// </remarks>
    public ParameterEdit Commit(ParameterRow row, string? text)
    {
        ArgumentNullException.ThrowIfNull(row);

        var typed = text?.Trim() ?? string.Empty;

        if (string.Equals(typed, row.Shown, StringComparison.Ordinal))
        {
            return ParameterEdit.Unchanged;
        }

        if (!row.Editable)
        {
            Status = $"{row.Name} is derived, so its value is its expression's - "
                + $"set one of the parameters in {row.Shown}";

            return ParameterEdit.Refused;
        }

        if (!double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            Status = $"'{typed}' is not a number. {row.Name} is in "
                + (row.Unit.Length > 0 ? row.Unit : "no unit")
                + ", so write the magnitude alone, with a point for the decimal";

            return ParameterEdit.Refused;
        }

        return Set(row.Name, value) ? ParameterEdit.Applied : ParameterEdit.Refused;
    }

    /// <summary>Sets one parameter and re-reads everything.</summary>
    /// <param name="name">Which parameter.</param>
    /// <param name="value">Its new magnitude, in its own declared unit.</param>
    /// <returns>Whether the edit was applied.</returns>
    /// <remarks>
    /// <para>
    /// A refused edit is reported and not applied, which is the journal's rule rather
    /// than the window's: in a shared session an invalid document is not one party's
    /// problem, because the other party's next action is against whatever is on disk.
    /// </para>
    /// <para>
    /// An edit that is merely <em>out of bounds</em> is a different thing and does apply
    /// - the model stays readable with the error against it. What is refused is a
    /// document that would not parse or that names a parameter the model does not have.
    /// </para>
    /// </remarks>
    public bool Set(string name, double value)
    {
        try
        {
            var edited = OutlineCommand.WithParameter(_session.Journal.ModelPath, name, value);

            _session.Edit(
                string.Create(CultureInfo.InvariantCulture, $"set {name} to {value:G6}"),
                edited);
        }
        catch (EinzelException refusal)
        {
            // AGT-3's error is already a recovery instruction, so the window shows it
            // rather than writing its own.
            Status = refusal.Error.Constraint
                + (refusal.Error.Suggestion is { } how ? $" - {how}" : string.Empty);

            return false;
        }

        Refresh();

        return true;
    }

    /// <summary>Reverses the most recent edit that still stands.</summary>
    /// <returns>Whether there was one to reverse.</returns>
    public bool Undo()
    {
        try
        {
            _session.Undo();
        }
        catch (EinzelException nothing)
        {
            Status = nothing.Error.Constraint;

            return false;
        }

        Refresh();

        return true;
    }

    /// <summary>Re-reads the model, the journal, and the commands.</summary>
    /// <remarks>
    /// Called after every action rather than incrementally patched. A tree is tens of
    /// rows and a person edits one at a time, so the cost is nothing and the alternative
    /// is two representations of one document that can disagree - which is the failure
    /// this whole design is arranged to avoid.
    /// </remarks>
    public void Refresh()
    {
        var outline = OutlineCommand.Execute(_session.Journal.ModelPath);

        Parameters.Clear();

        foreach (var parameter in outline.Parameters)
        {
            Parameters.Add(new ParameterRow(parameter));
        }

        RefreshHistory();

        Valid = outline.Valid;

        Status = outline.Valid
            ? $"{outline.Name ?? "(unnamed)"} - schema {outline.SchemaVersion}, "
                + $"{outline.Parameters.Count} parameters"
            : string.Join(
                "; ", outline.Errors.Select(e => $"{e.Path}: {e.Constraint}"));
    }

    /// <summary>Re-reads the journal and the commands, and leaves the parameters alone.</summary>
    /// <remarks>
    /// For an action that records a command without changing the document - a redraw, a
    /// watch. Rebuilding the parameter rows then would take the keyboard out of whichever
    /// box a person is typing in, for no change to anything that box shows.
    /// </remarks>
    public void RefreshHistory()
    {
        Journal.Clear();

        foreach (var line in _session.Journal.Lines())
        {
            Journal.Add(line);
        }

        Commands.Clear();

        foreach (var action in _session.Actions)
        {
            Commands.Add(action.Command);
        }

        Changed(nameof(CanUndo));
    }

    private void Changed(string property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
