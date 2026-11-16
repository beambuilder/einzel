using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

using Einzel.Commands;
using Einzel.Core.Errors;

namespace Einzel.Shell.Presentation;

/// <summary>One figure, as a row under its class.</summary>
/// <remarks>
/// <b>Every part of the envelope is a column, not a tooltip.</b> §16 says results carry
/// uncertainty and warnings alongside the value and <em>never behind a disclosure
/// control</em>, which is the one requirement in that section most easily violated by
/// somebody who has not read §4 — a value is small and an envelope is bulky, and putting
/// the bulk behind a chevron is the natural thing to do and the wrong one.
/// </remarks>
public sealed class FigureRow
{
    /// <summary>Creates a row from what the command reported.</summary>
    /// <param name="figure">The figure.</param>
    /// <exception cref="ArgumentNullException"><paramref name="figure"/> is null.</exception>
    public FigureRow(ReportedFigure figure)
    {
        ArgumentNullException.ThrowIfNull(figure);

        Name = figure.Name;
        Description = figure.Description;

        if (figure.Measured is not { } measured)
        {
            // Absent with a reason, never a zero that reads as a measurement.
            Value = "-";
            Uncertainty = figure.Absent ?? "not reported";
            Evidence = string.Empty;
            Warnings = string.Empty;
            Present = false;

            return;
        }

        Present = true;
        Value = Number(measured.Value) + " " + measured.Unit;

        Uncertainty = string.Create(
            CultureInfo.InvariantCulture,
            $"[{Number(measured.Lower)}, {Number(measured.Upper)}] "
            + $"at {measured.ConfidenceLevel:P0}");

        Evidence = measured.Evidence;
        Warnings = string.Join(", ", measured.Warnings.Select(w => w.Code));
    }

    /// <summary>Its name in the figure-of-merit registry.</summary>
    public string Name { get; }

    /// <summary>Its magnitude with its unit, or a dash where there is none.</summary>
    public string Value { get; }

    /// <summary>The interval, or why the figure is absent.</summary>
    public string Uncertainty { get; }

    /// <summary>What stands behind the value.</summary>
    public string Evidence { get; }

    /// <summary>The codes of any warnings on it.</summary>
    public string Warnings { get; }

    /// <summary>Whether there is a measurement here at all.</summary>
    public bool Present { get; }

    /// <summary>What it measures.</summary>
    public string Description { get; }

    /// <summary>A magnitude in as few characters as carry it.</summary>
    private static string Number(double value) =>
        value.ToString(
            Math.Abs(value) is > 1e-3 and < 1e6 ? "G6" : "G3", CultureInfo.InvariantCulture);
}

/// <summary>
/// Results by accuracy class, with the envelope alongside the value (§16).
/// </summary>
/// <remarks>
/// <para>
/// The grouping is <see cref="ResultsCommand"/>'s, because which class a figure is in is
/// §12's taxonomy and UI-1 puts that outside the shell. What is here is layout — and the
/// layout is the requirement: uncertainty and warnings beside the value, never behind a
/// disclosure control.
/// </para>
/// <para>
/// <b>A preview is marked as one.</b> GRD-5 taints the preview tier permanently, and a
/// preview number that looks like a run number is the failure the tier exists to prevent.
/// </para>
/// </remarks>
public sealed class ResultsViewModel : INotifyPropertyChanged
{
    private readonly ShellSession _session;
    private string _status = "not yet run";
    private bool _tainted;

    /// <summary>Opens the results view over a session.</summary>
    /// <param name="session">The session, which owns the model.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public ResultsViewModel(ShellSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The figures, flattened with a header row per class.</summary>
    public ObservableCollection<object> Rows { get; } = [];

    /// <summary>What applies to the results as a whole (GRD-2).</summary>
    public ObservableCollection<string> Warnings { get; } = [];

    /// <summary>Whether these numbers are from the preview tier, and so tainted.</summary>
    public bool Tainted
    {
        get => _tainted;
        private set
        {
            _tainted = value;
            Changed(nameof(Tainted));
        }
    }

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

    /// <summary>Runs the model and reads its figures.</summary>
    /// <param name="preview">Whether to use the cheap, permanently marked tier.</param>
    /// <returns>Whether anything was reported.</returns>
    public bool Refresh(bool preview)
    {
        try
        {
            return Show(_session.Results(preview));
        }
        catch (EinzelException refusal)
        {
            return Refused(refusal);
        }
    }

    /// <summary>Runs the model in the background and reads its figures when it is done.</summary>
    /// <param name="preview">Whether to use the cheap, permanently marked tier.</param>
    /// <returns>Whether anything was reported.</returns>
    /// <remarks>
    /// The lists are filled on the caller's thread once the run returns, so a window awaiting
    /// this on its UI thread never has a collection changed under it from another.
    /// </remarks>
    public async Task<bool> RefreshAsync(bool preview)
    {
        try
        {
            return Show(await _session.ResultsAsync(preview).ConfigureAwait(true));
        }
        catch (EinzelException refusal)
        {
            return Refused(refusal);
        }
    }

    private bool Refused(EinzelException refusal)
    {
        Rows.Clear();
        Warnings.Clear();
        Tainted = false;

        Status = refusal.Error.Constraint
            + (refusal.Error.Suggestion is { } how ? $" - {how}" : string.Empty);

        return false;
    }

    private bool Show(ResultsOutcome outcome)
    {
        Rows.Clear();

        foreach (var group in outcome.Classes)
        {
            Rows.Add(new FigureClassHeader(group.Class, group.What));

            foreach (var figure in group.Figures)
            {
                Rows.Add(new FigureRow(figure));
            }
        }

        Warnings.Clear();

        foreach (var warning in outcome.Warnings)
        {
            Warnings.Add($"{warning.Code}: {warning.Message}");
        }

        Tainted = outcome.Preview;

        var reported = outcome.Classes.SelectMany(c => c.Figures).Count(f => f.Measured is not null);

        var total = outcome.Classes.Sum(c => c.Figures.Count);
        var tier = outcome.Preview ? " - PREVIEW TIER, not a run" : string.Empty;

        Status = string.Create(
            CultureInfo.InvariantCulture, $"{reported} of {total} figures reported{tier}");

        return reported > 0;
    }

    private void Changed(string property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

/// <summary>A class heading in the results list.</summary>
/// <param name="Class">The class, as §12 names it.</param>
/// <param name="What">What a figure of this class is <em>of</em>.</param>
public sealed record FigureClassHeader(string Class, string What);
