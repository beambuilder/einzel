using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Fields.Analytic;

namespace Einzel.Commands;

/// <summary>A harmonic series fitted to a sampled period, as <c>einzel waveform fit</c> reports it.</summary>
/// <param name="Table">The table that was fitted, as an absolute path.</param>
/// <param name="Samples">How many samples it holds.</param>
/// <param name="Orders">The highest order fitted.</param>
/// <param name="Waveform">
/// The series as a model's <c>harmonic</c> waveform object, every order from zero up, ready
/// to paste into a drive.
/// </param>
/// <param name="ResidualRms">The root-mean-square of sample minus series, relative to the drive amplitude.</param>
/// <param name="ResidualMax">The largest magnitude of sample minus series.</param>
public sealed record WaveformFitOutcome(
    string Table,
    int Samples,
    int Orders,
    WaveformDocument Waveform,
    double ResidualRms,
    double ResidualMax);

/// <summary>Fits a harmonic series to a tabulated period.</summary>
/// <remarks>
/// The table is read by the same rules as a model's <c>tabulated</c> waveform file, so a
/// file a model accepts is a file this fits, and a malformed one is refused with the same
/// error naming the line.
/// </remarks>
public static class WaveformFitCommand
{
    /// <summary>Reads a table and fits orders 0 to <paramref name="orders"/> to it.</summary>
    /// <param name="path">The <c>phase,value</c> file.</param>
    /// <param name="orders">The highest order to fit; zero or more.</param>
    /// <returns>The series and its residual.</returns>
    /// <exception cref="EinzelException">
    /// The table cannot be read or is malformed (<c>SCHEMA_INVALID</c>, as a model's table
    /// file is reported), or its samples do not determine the orders asked for: fewer than
    /// <c>2·orders + 1</c> of them, or phases too clustered to separate the orders
    /// (<c>VALUE_OUT_OF_BOUNDS</c>).
    /// </exception>
    public static WaveformFitOutcome Execute(string path, int orders)
    {
        var full = Path.GetFullPath(path);
        string text;

        try
        {
            text = File.ReadAllText(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = path,
                Constraint = $"the waveform table '{path}' could not be read: {ex.Message}",
                Observed = new ObservedValue(0.0, path),
                Suggestion = "check that the file exists and is readable",
            });
        }

        var table = WaveformTable.Parse(text, path);
        var count = table.Phases.Count;

        // In long: an order count near int.MaxValue would overflow 2N + 1 into a small number.
        if (count < (2L * orders) + 1)
        {
            throw Undetermined(
                path,
                $"orders 0 to {orders} are {(2L * orders) + 1} coefficients, and the table has {count} samples",
                $"fit at most {(count - 1) / 2} orders, or sample the period more finely");
        }

        HarmonicFit fit;

        try
        {
            fit = RfWaveform.Harmonic.Fit(table.Phases, table.Values, orders);
        }
        catch (ArgumentException ex)
        {
            // Enough samples, but bunched into a part of the period: a valid table (a trace
            // that covers a tenth of a cycle, say) whose phases do not tell the orders apart.
            throw Undetermined(
                path,
                $"the samples do not separate {orders} orders: {ex.Message}",
                "fit fewer orders, or sample a longer part of the period");
        }

        var waveform = new WaveformDocument
        {
            Type = "harmonic",
            Terms = [.. fit.Waveform.Terms.Select(t =>
                new HarmonicTermDocument { Order = t.Order, Amplitude = t.Amplitude, Phase = t.Phase })],
        };

        return new WaveformFitOutcome(full, count, orders, waveform, fit.ResidualRms, fit.ResidualMax);
    }

    private static EinzelException Undetermined(string path, string constraint, string suggestion) =>
        new(new EinzelError
        {
            Code = ErrorCodes.ValueOutOfBounds,
            Path = path,
            Constraint = constraint,
            Suggestion = suggestion,
        });
}
