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
/// </remarks>
public static class IonListCsv
{
    /// <summary>The header line, naming each column and its unit.</summary>
    public const string Header =
        "id,tag,mass_kg,charge_C,x_m,y_m,z_m,vx_m_per_s,vy_m_per_s,vz_m_per_s,birth_time_s";

    private static readonly string[] Columns = Header.Split(',');

    /// <summary>Writes rows as CSV text, <c>\n</c>-terminated, culture-invariant.</summary>
    /// <param name="rows">The rows.</param>
    /// <returns>The text.</returns>
    /// <exception cref="EinzelException">A tag is not one CSV field.</exception>
    public static string Write(IEnumerable<IonRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var text = new StringBuilder(Header).Append('\n');

        foreach (var row in rows)
        {
            var (p, v) = (row.Launch.Position, row.Launch.Velocity);

            text.Append(string.Create(
                CultureInfo.InvariantCulture,
                $"{row.Id},{IonList.Tag(row.Tag, "/tag")},{row.Species.MassSi:R},{row.Species.ChargeSi:R},"
                + $"{p.X:R},{p.Y:R},{p.Z:R},{v.X:R},{v.Y:R},{v.Z:R},{row.BirthTimeSeconds:R}\n"));
        }

        return text.ToString();
    }

    /// <summary>Reads rows written by <see cref="Write"/>, or a hand-written file of the same shape.</summary>
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

        if (lines.Count == 0 || lines[0] != Header)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/",
                Constraint = "an ion-list CSV starts with the header " + Header,
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
        var ids = new HashSet<int>();

        for (var k = 0; k + 1 < lines.Count; k++)
        {
            try
            {
                var row = Row(lines[k + 1].Split(','), $"/{k}");
                IonList.Require(ids.Add(row.Id), ErrorCodes.SchemaInvalid, $"/{k}/id", $"id {row.Id} is used twice");
                rows.Add(row);
            }
            catch (EinzelException failure)
            {
                errors.Add(failure.Error);
            }
        }

        return new IonListExpansion(errors.Count > 0 ? null : rows, errors);
    }

    private static IonRow Row(string[] fields, string path)
    {
        IonList.Require(fields.Length == Columns.Length, ErrorCodes.SchemaInvalid, path,
            $"a row has {Columns.Length} fields");

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
