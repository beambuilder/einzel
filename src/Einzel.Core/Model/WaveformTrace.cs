using System.Globalization;
using Einzel.Core.Errors;

namespace Einzel.Core.Model;

/// <summary>A measured trace: sample times and values, read from an oscilloscope's text export.</summary>
/// <param name="Times">Sample times in seconds, strictly increasing.</param>
/// <param name="Values">The measured value at each time, in the trace's units.</param>
/// <param name="PreambleLines">How many non-blank lines before the first sample were skipped.</param>
/// <remarks>
/// <para>
/// One row per sample, the time and the value in two of its columns, with a <c>,</c>,
/// <c>;</c> or tab between the fields and a <c>.</c> decimal point. The first row whose two
/// named columns both read as finite numbers is the first sample and decides the separator;
/// every non-blank row before it is preamble (the model, record length and units a scope
/// writes there) and is counted, not read. After it every row is a sample. Fields outside
/// the two named columns are not read, so a Tektronix export, which writes its preamble in
/// columns 1 to 3 beside the data in columns 4 and 5, reads with the columns 4 and 5.
/// </para>
/// <para>
/// Text in, samples out, as <see cref="WaveformTable"/>: reading the file is the caller's.
/// </para>
/// </remarks>
public sealed record WaveformTrace(IReadOnlyList<double> Times, IReadOnlyList<double> Values, int PreambleLines)
{
    private static readonly char[] Separators = [',', ';', '\t'];

    /// <summary>Parses a trace.</summary>
    /// <param name="text">The trace's text.</param>
    /// <param name="path">The trace's path, for the error object.</param>
    /// <param name="timeColumn">The 1-based column of the time.</param>
    /// <param name="valueColumn">The 1-based column of the value.</param>
    /// <param name="timeScale">Seconds per unit of the time column: 1, or 1e-9 for nanoseconds.</param>
    /// <returns>The samples, times in seconds.</returns>
    /// <exception cref="EinzelException">
    /// <c>SCHEMA_INVALID</c>, naming the line: a row after the first sample has fewer than
    /// the named columns or a field there that is not a finite number, a time does not
    /// follow the one before, or there are fewer than two samples.
    /// </exception>
    public static WaveformTrace Parse(string text, string path, int timeColumn, int valueColumn, double timeScale)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeColumn, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(valueColumn, 1);

        var times = new List<double>();
        var values = new List<double>();
        var lines = text.Split('\n');
        var preamble = 0;
        char? separator = null;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();

            if (line.Length == 0)
            {
                continue;
            }

            if (separator is null)
            {
                var found = Array.Find(Separators, s => TryRow(line, s, timeColumn, valueColumn, out _, out _));

                if (found == '\0')
                {
                    preamble++;
                    continue;
                }

                separator = found;
            }

            if (!TryRow(line, separator.Value, timeColumn, valueColumn, out var time, out var value))
            {
                throw Failure(
                    path,
                    string.Create(CultureInfo.InvariantCulture, $"line {index + 1} ('{line}') has no finite time and value in columns {timeColumn} and {valueColumn}"),
                    $"every row after the first sample needs numbers in those columns, separated by '{Name(separator.Value)}' with '.' as the decimal point");
            }

            time *= timeScale;

            if (times.Count > 0 && !(time > times[^1]))
            {
                throw Failure(
                    path,
                    string.Create(CultureInfo.InvariantCulture, $"line {index + 1} has the time {time:R} s, which does not follow {times[^1]:R} s"),
                    "the samples must be in time order with no repeated time; check the columns and the time unit");
            }

            times.Add(time);
            values.Add(value);
        }

        if (times.Count < 2)
        {
            throw Failure(
                path,
                string.Create(CultureInfo.InvariantCulture, $"the trace has {times.Count} samples, and at least 2 are needed"),
                string.Create(CultureInfo.InvariantCulture, $"check that columns {timeColumn} and {valueColumn} hold the time and the value"));
        }

        return new WaveformTrace(times, values, preamble);
    }

    private static bool TryRow(string line, char separator, int timeColumn, int valueColumn, out double time, out double value)
    {
        time = 0.0;
        value = 0.0;
        var fields = line.Split(separator);

        return fields.Length >= Math.Max(timeColumn, valueColumn)
            && double.TryParse(fields[timeColumn - 1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out time)
            && double.TryParse(fields[valueColumn - 1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(time)
            && double.IsFinite(value);
    }

    private static string Name(char separator) => separator == '\t' ? "tab" : separator.ToString();

    private static EinzelException Failure(string path, string constraint, string suggestion) =>
        new(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = path,
            Constraint = constraint,
            Suggestion = suggestion,
        });
}
