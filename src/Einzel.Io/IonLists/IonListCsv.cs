using System.Globalization;
using System.Text;
using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Transport;

namespace Einzel.Io.IonLists;

/// <summary>
/// The expanded form of an ion list: one line per ion, every number in SI, written so that
/// reading it back gives the same rows to the bit.
/// </summary>
/// <remarks>
/// The reproducibility anchor. A seeded set expands through <see cref="Random"/> and the
/// platform's maths library, which nothing promises to be the same on every machine; the
/// expanded rows, written with round-trip (<c>R</c>) formatting, are. The expanded
/// <c>.ions.csv</c> reproduces the list anywhere.
/// <para>
/// A list with parameter overrides adds one column per overridden parameter after the
/// eleven, <c>p:&lt;name&gt;</c>, holding the SI value or nothing for a row that does not
/// override it. A list without overrides has exactly the eleven.
/// </para>
/// </remarks>
public static class IonListCsv
{
    /// <summary>The header line, naming each column and its unit.</summary>
    public const string Header =
        "id,tag,mass_kg,charge_C,x_m,y_m,z_m,vx_m_per_s,vy_m_per_s,vz_m_per_s,birth_time_s";

    private static readonly string[] Columns = Header.Split(',');

    /// <summary>The prefix of a parameter-override column.</summary>
    public const string ParameterPrefix = "p:";

    /// <summary>Writes rows as CSV text, <c>\n</c>-terminated, culture-invariant.</summary>
    /// <param name="rows">The rows.</param>
    /// <returns>The text.</returns>
    /// <exception cref="EinzelException">A tag is not one CSV field.</exception>
    public static string Write(IEnumerable<IonRow> rows) => Write([.. rows], [], []);

    /// <summary>Writes rows with their parameter overrides as CSV text.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="groups">Each row's index into <paramref name="sets"/>; empty when <paramref name="sets"/> is.</param>
    /// <param name="sets">The distinct override sets (<see cref="IonListExpansion.OverrideSets"/>).</param>
    /// <returns>The text: the eleven columns, then one <c>p:</c> column per overridden parameter in name order.</returns>
    /// <exception cref="EinzelException">A tag is not one CSV field.</exception>
    public static string Write(IReadOnlyList<IonRow> rows, IReadOnlyList<int> groups, IReadOnlyList<IonOverrideSet> sets)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(sets);

        var names = ParameterNames(sets);
        var text = new StringBuilder(Header);

        foreach (var name in names)
        {
            text.Append(',').Append(ParameterPrefix).Append(name);
        }

        text.Append('\n');

        for (var k = 0; k < rows.Count; k++)
        {
            var row = rows[k];
            var (p, v) = (row.Launch.Position, row.Launch.Velocity);

            text.Append(string.Create(
                CultureInfo.InvariantCulture,
                $"{row.Id},{IonList.Tag(row.Tag, "/tag")},{row.Species.MassSi:R},{row.Species.ChargeSi:R},"
                + $"{p.X:R},{p.Y:R},{p.Z:R},{v.X:R},{v.Y:R},{v.Z:R},{row.BirthTimeSeconds:R}"));

            foreach (var name in names)
            {
                text.Append(',');

                if (sets[groups[k]].Values.TryGetValue(name, out var value))
                {
                    text.Append(value.ToString("R", CultureInfo.InvariantCulture));
                }
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Every parameter any set overrides, in ordinal order: the <c>p:</c> columns.</summary>
    /// <param name="sets">The override sets.</param>
    /// <returns>The names.</returns>
    public static IReadOnlyList<string> ParameterNames(IReadOnlyList<IonOverrideSet> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);

        return [.. sets.SelectMany(set => set.Values.Keys).Distinct().Order(StringComparer.Ordinal)];
    }

    /// <summary>Reads rows written by <see cref="Write(IEnumerable{IonRow})"/>, or a hand-written file of the same shape.</summary>
    /// <param name="text">The CSV text.</param>
    /// <returns>
    /// The rows, or the errors, each addressed as <c>/row/column</c> with rows counted from 0
    /// after the header.
    /// </returns>
    public static IonListExpansion Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();

        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var errors = new List<EinzelError>();

        var header = lines.Count == 0 ? [] : lines[0].Split(',');
        var names = header.Skip(Columns.Length).Select(column => column.StartsWith(ParameterPrefix, StringComparison.Ordinal)
            ? column[ParameterPrefix.Length..] : string.Empty).ToList();

        if (lines.Count == 0 || !header.Take(Columns.Length).SequenceEqual(Columns)
            || names.Any(name => name.Length == 0) || names.Distinct().Count() != names.Count)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/",
                Constraint = "an ion-list CSV starts with the header " + Header
                    + ", followed by one distinct p:<name> column per overridden parameter",
            });

            return new IonListExpansion(null, errors);
        }

        if (lines.Count == 1)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/",
                Constraint = "an ion list declares at least one ion",
            });
        }

        var rows = new List<IonRow>(lines.Count - 1);
        var overrides = new List<IonOverrideSet>(lines.Count - 1);
        var ids = new HashSet<int>();

        for (var k = 0; k + 1 < lines.Count; k++)
        {
            try
            {
                var fields = lines[k + 1].Split(',');
                var row = Row(fields, $"/{k}", header.Length);
                IonList.Require(ids.Add(row.Id), ErrorCodes.SchemaInvalid, $"/{k}/id", $"id {row.Id} is used twice");
                rows.Add(row);
                overrides.Add(Overrides(fields, names, $"/{k}"));
            }
            catch (EinzelException failure)
            {
                errors.Add(failure.Error);
            }
        }

        var (sets, groups) = IonOverrideSet.Group(overrides);

        return new IonListExpansion(errors.Count > 0 ? null : rows, errors) { OverrideSets = sets, RowGroups = groups };
    }

    /// <summary>A row's overrides from its <c>p:</c> cells; an empty cell overrides nothing.</summary>
    private static IonOverrideSet Overrides(string[] fields, List<string> names, string path)
    {
        var values = new SortedDictionary<string, double>(StringComparer.Ordinal);

        for (var c = 0; c < names.Count; c++)
        {
            var cell = fields[Columns.Length + c];

            if (cell.Length > 0)
            {
                var parsed = double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out var value);
                IonList.Require(parsed && double.IsFinite(value), ErrorCodes.ValueOutOfBounds,
                    $"{path}/{ParameterPrefix}{names[c]}", "a finite number in SI, or nothing");

                // + 0.0 turns -0 into 0, so the two are one group, as in a JSON list.
                values[names[c]] = value + 0.0;
            }
        }

        return new IonOverrideSet(values, null, path);
    }

    private static IonRow Row(string[] fields, string path, int columns)
    {
        IonList.Require(fields.Length == columns, ErrorCodes.SchemaInvalid, path,
            $"a row has {columns} fields");

        double Number(int column)
        {
            var parsed = double.TryParse(fields[column], NumberStyles.Float, CultureInfo.InvariantCulture, out var value);
            IonList.Require(parsed && double.IsFinite(value), ErrorCodes.ValueOutOfBounds,
                $"{path}/{Columns[column]}", "a finite number in SI");
            return value;
        }

        IonList.Require(
            int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id),
            ErrorCodes.ValueOutOfBounds, path + "/id", "an id is a non-negative integer");

        var (mass, charge, birth) = (Number(2), Number(3), Number(10));

        IonList.Require(mass > 0.0, ErrorCodes.ValueOutOfBounds, path + "/mass_kg", "a mass is positive");
        IonList.Require(charge != 0.0, ErrorCodes.ValueOutOfBounds, path + "/charge_C", "an ion has charge");
        IonList.Require(birth >= 0.0, ErrorCodes.ValueOutOfBounds, path + "/birth_time_s",
            "a birth time cannot be negative");

        return new IonRow(
            id,
            IonSpecies.Create(Quantity.Si(mass, Dimension.MassDimension), Quantity.Si(charge, Dimension.Charge)),
            new PhaseState(
                new Vec3(Number(4), Number(5), Number(6)),
                new Vec3(Number(7), Number(8), Number(9))),
            birth,
            IonList.Tag(fields[1], path));
    }
}
