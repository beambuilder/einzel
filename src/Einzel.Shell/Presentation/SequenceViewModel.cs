using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

using Einzel.Commands;
using Einzel.Core.Errors;

namespace Einzel.Shell.Presentation;

/// <summary>One phase, as a row on the timeline.</summary>
public sealed class PhaseRow
{
    /// <summary>Creates a row from a declared phase.</summary>
    /// <param name="phase">The phase.</param>
    /// <param name="totalUs">The whole sequence's length, for the bar.</param>
    /// <exception cref="ArgumentNullException"><paramref name="phase"/> is null.</exception>
    public PhaseRow(SequencePhase phase, double totalUs)
    {
        ArgumentNullException.ThrowIfNull(phase);

        Name = phase.Name;
        Mode = phase.Mode;
        ModeChanged = phase.ModeChanged;

        Span = string.Create(
            CultureInfo.InvariantCulture,
            $"{phase.StartsAtUs:G6} to {phase.EndsAtUs:G6} us ({phase.DurationUs:G4})");

        // What the phase moves, which is the information a timeline carries that a table of
        // settings does not - a sequenced instrument repeats most of its state from one
        // phase to the next, so the two rows that change are the ones worth reading.
        Changes = phase.ChangedCount == 0
            ? "holds"
            : string.Join(
                ", ",
                phase.Electrodes
                    .Where(e => e.Changed)
                    .Select(Setting));

        // Proportional to duration, so the eye reads the schedule rather than the row
        // count. A hold of a microsecond beside a flight of a hundred is the shape of a
        // pulsed extraction and a table of equal rows hides it.
        Width = totalUs > 0.0 ? Math.Max(2.0, 320.0 * phase.DurationUs / totalUs) : 2.0;
    }

    /// <summary>One electrode's new setting, as a phrase.</summary>
    /// <remarks>
    /// The drive is shown only where there is one, because a zero beside every DC value on
    /// a static instrument is a column of noughts a reader learns to skip - and then misses
    /// the one row where it is not nought.
    /// </remarks>
    private static string Setting(PhaseElectrode electrode)
    {
        var drive = electrode.DriveAmplitudeVolts != 0.0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $" / {electrode.DriveAmplitudeVolts:G4} V drive")
            : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{electrode.Name} -> {electrode.PotentialVolts:G4} V{drive}");
    }

    /// <summary>What the phase is for.</summary>
    public string Name { get; }

    /// <summary>When it runs.</summary>
    public string Span { get; }

    /// <summary>The transport mode it is described in.</summary>
    public string Mode { get; }

    /// <summary>Whether that differs from the phase before (SEQ-1's boundary).</summary>
    public bool ModeChanged { get; }

    /// <summary>What it moves, or that it holds.</summary>
    public string Changes { get; }

    /// <summary>How wide to draw its bar, in device units.</summary>
    public double Width { get; }
}

/// <summary>
/// The instrument as a timed state machine (§16's sequence editor).
/// </summary>
/// <remarks>
/// <para>
/// It edits nothing yet - it shows the declared timeline. A sequence is a block in the
/// model document, so editing one is editing the document, which goes through the same
/// journal every other change does; what is missing is the input surface rather than the
/// path underneath it.
/// </para>
/// <para>
/// The timeline is <see cref="SequenceCommand"/>'s, because which phases exist and what
/// each holds is compiled from the document and UI-1 puts that outside the shell.
/// </para>
/// </remarks>
public sealed class SequenceViewModel : INotifyPropertyChanged
{
    private readonly ShellSession _session;
    private string _status = "not yet read";

    /// <summary>Opens the editor over a session.</summary>
    /// <param name="session">The session, which owns the model.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public SequenceViewModel(ShellSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The phases, in order.</summary>
    public ObservableCollection<PhaseRow> Phases { get; } = [];

    /// <summary>What the reader needs alongside the timeline (GRD-2).</summary>
    public ObservableCollection<string> Warnings { get; } = [];

    /// <summary>What the editor is showing.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            Changed(nameof(Status));
        }
    }

    /// <summary>Re-reads the declared timeline.</summary>
    /// <returns>Whether the model declares a sequence.</returns>
    public bool Refresh()
    {
        SequenceOutcome outcome;

        try
        {
            outcome = _session.Sequence();
        }
        catch (EinzelException refusal)
        {
            Phases.Clear();
            Warnings.Clear();

            Status = refusal.Error.Constraint
                + (refusal.Error.Suggestion is { } how ? $" - {how}" : string.Empty);

            return false;
        }

        Phases.Clear();

        foreach (var phase in outcome.Phases)
        {
            Phases.Add(new PhaseRow(phase, outcome.TotalUs));
        }

        Warnings.Clear();

        foreach (var warning in outcome.Warnings)
        {
            Warnings.Add($"{warning.Code}: {warning.Message}");
        }

        Status = outcome.Sequenced
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{outcome.Phases.Count} phases over {outcome.TotalUs:G6} us")
            : "no sequence - see the note below";

        return outcome.Sequenced;
    }

    private void Changed(string property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
