using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

using Einzel.Commands;
using Einzel.Core.Errors;

namespace Einzel.Shell.Presentation;

/// <summary>One point on the path, as a row.</summary>
public sealed class RegimeRow
{
    /// <summary>Creates a row from a sampled point.</summary>
    /// <param name="sample">The point.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sample"/> is null.</exception>
    public RegimeRow(RegimeSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        Time = Fixed(sample.TimeUs, 2);
        Position = Fixed(sample.PositionMm[0], 2);
        Speed = Fixed(sample.SpeedMs, 1);
        Pressure = General(sample.PressureMbar);
        MeanFreePath = General(sample.MeanFreePathMm);
        Knudsen = General(sample.Knudsen);
        PerCycle = sample.CollisionsPerRfCycle is { } cycle ? General(cycle) : "-";
        ReducedField = General(sample.ReducedFieldTd);
        Violations = string.Join(", ", sample.Violations);

        // The highlight §16 asks for. A violated point is not a worse point, it is a point
        // where the description being used does not apply - so it is marked rather than
        // ranked.
        Violated = sample.Violations.Count > 0;
    }

    /// <summary>When, in microseconds.</summary>
    public string Time { get; }

    /// <summary>Where along the axis, in millimetres.</summary>
    public string Position { get; }

    /// <summary>How fast, in metres per second.</summary>
    public string Speed { get; }

    /// <summary>Local pressure, in millibar.</summary>
    public string Pressure { get; }

    /// <summary>Local mean free path, in millimetres.</summary>
    public string MeanFreePath { get; }

    /// <summary>Mean free path over the tightest constriction.</summary>
    public string Knudsen { get; }

    /// <summary>Collisions per drive cycle, or a dash where undriven.</summary>
    public string PerCycle { get; }

    /// <summary>The local field over the local density, in townsend.</summary>
    public string ReducedField { get; }

    /// <summary>Which REG-2 warnings this point earns.</summary>
    public string Violations { get; }

    /// <summary>Whether the selected description fails here.</summary>
    public bool Violated { get; }

    private static string Fixed(double value, int places) =>
        value.ToString("F" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static string General(double value) =>
        double.IsFinite(value) ? value.ToString("G4", CultureInfo.InvariantCulture) : "inf";
}

/// <summary>A stretch of path over which one thing is wrong, as a row.</summary>
public sealed class ExcursionRow
{
    /// <summary>Creates a row from an excursion.</summary>
    /// <param name="excursion">The stretch.</param>
    /// <exception cref="ArgumentNullException"><paramref name="excursion"/> is null.</exception>
    public ExcursionRow(RegimeExcursion excursion)
    {
        ArgumentNullException.ThrowIfNull(excursion);

        Code = excursion.Code;
        Severity = excursion.Severity;
        Message = excursion.Message;

        Where = string.Create(
            CultureInfo.InvariantCulture,
            $"{excursion.FromMm:F1} to {excursion.ToMm:F1} mm "
            + $"({excursion.FromUs:F1} to {excursion.ToUs:F1} us)");
    }

    /// <summary>Which REG-2 warning.</summary>
    public string Code { get; }

    /// <summary>How bad, as GRD-3 grades it.</summary>
    public string Severity { get; }

    /// <summary>Between where and where.</summary>
    public string Where { get; }

    /// <summary>What it says.</summary>
    public string Message { get; }
}

/// <summary>
/// The governing dimensionless numbers along a path, violations highlighted (§16).
/// </summary>
/// <remarks>
/// <para>
/// <b>The excursions are the point, not the table.</b> A run already reports these numbers
/// and does it at the worst place anywhere in the gas, which is the right answer for a
/// warning and tells nobody what to change. What this view adds is <em>where</em>: a
/// funnel whose entrance is at 10 mbar and whose exit is at 0.1 mbar is in two different
/// regimes, and a single verdict describes neither.
/// </para>
/// <para>
/// It computes nothing. Where a regime boundary lies is spec figure 4's, and a window
/// deciding it for itself would be a second copy of that figure to keep in step (UI-1).
/// </para>
/// </remarks>
public sealed class RegimeViewModel : INotifyPropertyChanged
{
    private readonly ShellSession _session;
    private string _status = "not yet inspected";

    /// <summary>Opens the inspector over a session.</summary>
    /// <param name="session">The session, which owns the model.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public RegimeViewModel(ShellSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The path, in flight order.</summary>
    public ObservableCollection<RegimeRow> Samples { get; } = [];

    /// <summary>Where the selected description does not hold.</summary>
    public ObservableCollection<ExcursionRow> Excursions { get; } = [];

    /// <summary>What applies to the profile as a whole (GRD-2).</summary>
    public ObservableCollection<string> Warnings { get; } = [];

    /// <summary>What the inspector is showing.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            Changed(nameof(Status));
        }
    }

    /// <summary>Walks the path and reads the regime at each step.</summary>
    /// <returns>Whether there was a path to walk.</returns>
    public bool Refresh()
    {
        try
        {
            return Show(_session.Regime());
        }
        catch (EinzelException refusal)
        {
            return Refused(refusal);
        }
    }

    /// <summary>Walks the path in the background and reads the regime when it is done.</summary>
    /// <returns>Whether there was a path to walk.</returns>
    /// <remarks>
    /// The lists are filled on the caller's thread once the walk returns, so a window awaiting
    /// this on its UI thread never has a collection changed under it from another.
    /// </remarks>
    public async Task<bool> RefreshAsync()
    {
        try
        {
            return Show(await _session.RegimeAsync().ConfigureAwait(true));
        }
        catch (EinzelException refusal)
        {
            return Refused(refusal);
        }
    }

    private bool Refused(EinzelException refusal)
    {
        Samples.Clear();
        Excursions.Clear();
        Warnings.Clear();

        Status = refusal.Error.Constraint
            + (refusal.Error.Suggestion is { } how ? $" - {how}" : string.Empty);

        return false;
    }

    private bool Show(RegimeProfile profile)
    {
        Samples.Clear();

        foreach (var sample in profile.Samples)
        {
            Samples.Add(new RegimeRow(sample));
        }

        Excursions.Clear();

        foreach (var excursion in profile.Excursions)
        {
            Excursions.Add(new ExcursionRow(excursion));
        }

        Warnings.Clear();

        foreach (var warning in profile.Warnings)
        {
            Warnings.Add($"{warning.Code}: {warning.Message}");
        }

        Status = Describe(profile);

        return profile.Samples.Count > 0;
    }

    /// <summary>What the profile says, in a phrase.</summary>
    /// <remarks>
    /// An empty profile is a statement rather than an absence, and which statement matters:
    /// a vacuum has no regime numbers, and a diffusive model has no path to report them
    /// along. Both are on the record as warnings; this says which without paraphrasing
    /// them.
    /// </remarks>
    private static string Describe(RegimeProfile profile)
    {
        if (profile.Samples.Count == 0)
        {
            return "nothing to report along - see the note below";
        }

        var violated = profile.Samples.Count(s => s.Violations.Count > 0);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{profile.Samples.Count} points, Knudsen against a {Length(profile.ApertureMm)} mm "
            + $"constriction - {violated} outside validity in "
            + $"{profile.Excursions.Count} stretch(es)");
    }

    /// <summary>A length in millimetres, without exponent notation for ordinary sizes.</summary>
    /// <remarks>
    /// "1E+03 mm" is a correct rendering of a metre and an unreadable one. G-format reaches
    /// for an exponent at four significant figures, which is exactly the range instrument
    /// dimensions live in.
    /// </remarks>
    private static string Length(double millimetres) =>
        millimetres.ToString(
            Math.Abs(millimetres) is >= 0.01 and < 100_000.0 ? "0.###" : "G3",
            CultureInfo.InvariantCulture);

    private void Changed(string property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
