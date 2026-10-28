using System.Globalization;
using Einzel.Core.Errors;

namespace Einzel.Core.Model;

/// <summary>How a tabulated waveform joins its samples.</summary>
public enum WaveformInterpolation
{
    /// <summary>Straight lines between samples; the error falls as the square of the spacing.</summary>
    Linear,

    /// <summary>The periodic cubic spline; the error falls as the fourth power of the spacing.</summary>
    Cubic,
}

/// <summary>One period of a drive waveform, read from <c>phase,value</c> text.</summary>
/// <param name="Phases">Sample phases in cycles, strictly ascending in [0, 1).</param>
/// <param name="Values">The drive at each phase, relative to the amplitude.</param>
/// <remarks>
/// <para>
/// The format is the least a spreadsheet or an oscilloscope export can be made to write:
/// an optional header line, then one <c>phase,value</c> row per sample, with a <c>.</c>
/// decimal point and a comma between the two. A first line with no digit in it is a
/// header; any other first line is data and must read as two numbers, so a typo in the
/// first sample is refused rather than taken for a header. The table is one
/// period and is closed by the reader of it: the sample after the last is the first, one
/// cycle on, so the first row is not repeated at phase one.
/// </para>
/// <para>
/// Text in, samples out. Reading the file is the caller's, so the parse is the same for a
/// file, a test and anything that generates a table in memory, and a failure is reported
/// at the pointer of whatever named the file.
/// </para>
/// <para>
/// Equal when the samples are, element by element, so two reads of the same text compare
/// equal however the lists were built.
/// </para>
/// </remarks>
public sealed record WaveformTable(IReadOnlyList<double> Phases, IReadOnlyList<double> Values)
{
    /// <summary>Parses a table.</summary>
    /// <param name="text">The table's text.</param>
    /// <param name="path">JSON Pointer to whatever named the table, for the error object.</param>
    /// <returns>The samples.</returns>
    /// <exception cref="EinzelException">
    /// A row is not two numbers, a phase is outside [0, 1) or out of order, a value is not
    /// finite, or there are fewer than three rows.
    /// </exception>
    public static WaveformTable Parse(string text, string path)
    {
        ArgumentNullException.ThrowIfNull(text);

        var phases = new List<double>();
        var values = new List<double>();
        var lines = text.Split('\n');
        var seenFirst = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            var number = index + 1;

            if (line.Length == 0)
            {
                continue;
            }

            if (!seenFirst)
            {
                seenFirst = true;

                if (!line.Any(char.IsDigit))
                {
                    continue;
                }
            }

            var isRow = TryRow(line, out var phase, out var value);

            if (!isRow)
            {
                throw Failure(
                    path,
                    $"line {number} ('{line}') is not a phase and a value",
                    "write one 'phase,value' row per sample, with '.' as the decimal point");
            }

            if (!(phase >= 0.0 && phase < 1.0))
            {
                throw Failure(
                    path,
                    $"line {number} has the phase {phase:R}, outside [0, 1)",
                    "give phases in cycles; the table is one period, and phase 1 is phase 0 again");
            }

            if (phases.Count > 0 && !(phase > phases[^1]))
            {
                throw Failure(
                    path,
                    $"line {number} has the phase {phase:R}, which does not follow {phases[^1]:R}",
                    "sort the rows by phase and remove repeated phases");
            }

            if (!double.IsFinite(value))
            {
                throw Failure(
                    path,
                    $"line {number} has the value {value}, which is not a finite number",
                    "a drive must be finite at every sample; replace or remove the row");
            }

            phases.Add(phase);
            values.Add(value);
        }

        if (phases.Count < 3)
        {
            throw Failure(
                path,
                $"the table has {phases.Count} rows, and a period needs at least 3",
                "sample the period at three phases or more; two samples cannot tell a shape from a line");
        }

        return new WaveformTable(phases, values);
    }

    /// <summary>Equal when the phases and the values are, element by element.</summary>
    /// <param name="other">The other table.</param>
    /// <returns>Whether the two tables hold the same samples.</returns>
    public bool Equals(WaveformTable? other) =>
        other is not null && Phases.SequenceEqual(other.Phases) && Values.SequenceEqual(other.Values);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(Phases.Count, Phases.Count > 0 ? Values[0] : 0.0, Phases.Count > 0 ? Values[^1] : 0.0);

    private static bool TryRow(string line, out double phase, out double value)
    {
        phase = 0.0;
        value = 0.0;
        var fields = line.Split(',');

        return fields.Length == 2
            && double.TryParse(fields[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out phase)
            && double.TryParse(fields[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static EinzelException Failure(string path, string constraint, string suggestion) =>
        new(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = path,
            Constraint = constraint,
            Suggestion = suggestion,
        });
}
