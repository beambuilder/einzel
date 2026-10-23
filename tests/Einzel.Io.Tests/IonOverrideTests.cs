using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Io.IonLists;
using Xunit.Abstractions;

namespace Einzel.Io.Tests;

/// <summary>
/// Parameter overrides in an ion list: what may be overridden, how rows are grouped by them,
/// and the <c>p:</c> columns that carry them through the expanded <c>.ions.csv</c>.
/// </summary>
/// <remarks>
/// The rows themselves do not change: an override is a statement about the field a row is
/// flown through, so every test that groups rows also checks that the rows are the rows the
/// same list gives without overrides.
/// </remarks>
public sealed class IonOverrideTests(ITestOutputHelper output)
{
    /// <summary>
    /// The beam model with a free potential, a free phase, a free energy and temperature, and a
    /// bounded potential derived from the first.
    /// </summary>
    private static readonly string Model = IonListTests.Beam.Replace(
        "\"name\": \"beam\",",
        """
        "name": "beam",
          "parameters": {
            "U": { "value": 10, "unit": "V", "minimum": -100, "maximum": 100 },
            "phase": { "value": 0.25, "unit": "1" },
            "Ek": { "value": 2, "unit": "eV" },
            "T": { "value": 10, "unit": "K" },
            "W": { "expression": "2 * U", "unit": "V", "maximum": 150 }
          },
        """,
        StringComparison.Ordinal);

    private static IonListExpansion Expand(string ions) =>
        IonList.Expand(IonList.Parse(ions), IonListTests.Compile(Model));

    private static IonListExpansion Valid(string ions)
    {
        var expansion = Expand(ions);
        Assert.True(expansion.IsValid, string.Join("; ", expansion.Errors.Select(e => $"{e.Path}: {e.Constraint}")));
        return expansion;
    }

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);

    private static string Key(IonOverrideSet set) => string.Join(';', set.Values.Select(p => $"{p.Key}={Bits(p.Value)}"));

    public static TheoryData<string, string, string> Refusals => new()
    {
        // overrides of row 0 (and set 0), code, pointer
        { """ "ions": [ { "id": 1, "overrides": { "X": { "value": 1, "unit": "V" } } } ] """, ErrorCodes.SchemaInvalid, "/ions/0/overrides/X" },
        { """ "ions": [ { "id": 1, "overrides": { "W": { "value": 1, "unit": "V" } } } ] """, ErrorCodes.SchemaInvalid, "/ions/0/overrides/W" },
        { """ "ions": [ { "id": 1, "overrides": { "U": { "value": 1, "unit": "mm" } } } ] """, ErrorCodes.UnitsIncompatible, "/ions/0/overrides/U" },
        { """ "ions": [ { "id": 1, "overrides": { "U": { "value": 0, "unit": "V", "expression": "phase * 1 V" } } } ] """, ErrorCodes.SchemaInvalid, "/ions/0/overrides/U" },
        { """ "ions": [ { "id": 1, "overrides": { "U": { "value": 1 } } } ] """, ErrorCodes.UnitsRequired, "/ions/0/overrides/U" },
        { """ "ions": [ { "id": 1, "overrides": { "U": { "value": 101, "unit": "V" } } } ] """, ErrorCodes.ValueOutOfBounds, "/ions/0/overrides/U" },
        { """ "ions": [ { "id": 1, "overrides": { "phase": { "value": 0.5, "unit": "V" } } } ] """, ErrorCodes.UnitsIncompatible, "/ions/0/overrides/phase" },
        { """ "ionSets": [ { "n": 2, "overrides": { "X": { "value": 1, "unit": "V" } } } ] """, ErrorCodes.SchemaInvalid, "/ionSets/0/overrides/X" },
        { """ "ionSets": [ { "cloud": { }, "overrides": { "U": { "value": 1, "unit": "kg" } } } ] """, ErrorCodes.UnitsIncompatible, "/ionSets/0/overrides/U" },
        { """ "ions": [ { "id": 1, "overrides": { "U": { "value": 80, "unit": "V" } } } ] """, ErrorCodes.ValueOutOfBounds, "/ions/0/overrides" },
        { """ "ionSets": [ { "n": 1, "overrides": { "U": { "value": 80, "unit": "V" } } } ] """, ErrorCodes.ValueOutOfBounds, "/ionSets/0/overrides" },
    };

    /// <summary>
    /// What <c>ParameterScan.Bind</c> refuses in a scan, an override refuses at its own pointer:
    /// a name the model does not declare, a derived parameter, the wrong dimension, a value
    /// outside the bounds. An expression and a missing unit are refused too, and a value that
    /// takes a derived parameter outside its bounds is refused at the row's or set's overrides.
    /// </summary>
    [Theory]
    [MemberData(nameof(Refusals))]
    public void EachOverrideMistakeIsRefusedAtItsPointer(string fragment, string code, string at)
    {
        var expansion = Expand("{ \"schemaVersion\": \"0.1\", " + fragment + " }");

        Assert.Null(expansion.Rows);
        var error = Assert.Single(expansion.Errors);
        output.WriteLine($"{error.Code} {error.Path}: {error.Constraint}");
        Assert.Equal((code, at), (error.Code, error.Path));
    }

    /// <summary>
    /// A key written twice is refused rather than the later one kept, which is what the
    /// model's own parser does: an override stated twice would fly one of two numbers silently.
    /// </summary>
    [Fact]
    public void AnOverrideWrittenTwiceIsRefused()
    {
        var failure = Assert.Throws<EinzelException>(() => IonList.Parse("""
            { "schemaVersion": "0.1", "ions": [ { "id": 1, "overrides": {
                "U": { "value": 1, "unit": "V" }, "U": { "value": 2, "unit": "V" } } } ] }
            """));

        output.WriteLine($"{failure.Error.Path}: {failure.Error.Constraint}");
        Assert.Equal(ErrorCodes.SchemaInvalid, failure.Error.Code);
        Assert.StartsWith("/ions/0/overrides", failure.Error.Path, StringComparison.Ordinal);
    }

    /// <summary>
    /// Any key written twice is refused in the list's own words, not the serializer's, and
    /// at the key: <c>id</c> as much as an override.
    /// </summary>
    [Fact]
    public void AnyKeyWrittenTwiceIsRefusedAtTheKey()
    {
        var failure = Assert.Throws<EinzelException>(() => IonList.Parse(
            """{ "schemaVersion": "0.1", "ions": [ { "id": 1, "id": 2 } ] }"""));

        output.WriteLine($"{failure.Error.Path}: {failure.Error.Constraint} / {failure.Error.Suggestion}");
        Assert.Equal((ErrorCodes.SchemaInvalid, "/ions/0/id", "a key written twice"),
            (failure.Error.Code, failure.Error.Path, failure.Error.Constraint));
        Assert.Contains("write each key once", failure.Error.Suggestion, StringComparison.Ordinal);

        // Any other parse failure keeps the serializer's message and the unknown-key advice.
        var unknown = Assert.Throws<EinzelException>(() => IonList.Parse(
            """{ "schemaVersion": "0.1", "ions": [ { "id": 1, "energy": 2 } ] }"""));
        Assert.Contains("unknown one is refused", unknown.Error.Suggestion, StringComparison.Ordinal);
    }

    /// <summary>
    /// A row's or set's own expressions are evaluated with its overrides layered on the model's
    /// parameters: a row that overrides <c>Ek</c> and states its energy as <c>Ek</c> is, to the
    /// bit, the row that states the overridden energy as a number, and a cloud set whose
    /// temperature is <c>T</c> draws what the set stating the overridden temperature draws.
    /// A row without overrides still reads the model's value.
    /// </summary>
    [Fact]
    public void ARowsExpressionsSeeItsOwnOverrides()
    {
        var rows = Valid("""
            { "schemaVersion": "0.1", "ions": [
              { "id": 1, "kineticEnergy": { "expression": "Ek", "unit": "eV" }, "overrides": { "Ek": { "value": 50, "unit": "eV" } } },
              { "id": 2, "kineticEnergy": { "value": 50, "unit": "eV" } },
              { "id": 3, "kineticEnergy": { "expression": "Ek", "unit": "eV" } },
              { "id": 4, "kineticEnergy": { "value": 2, "unit": "eV" } } ],
              "ionSets": [
              { "firstId": 10, "cloud": { "ions": 3, "seed": 5, "temperature": { "expression": "T", "unit": "K" } },
                "overrides": { "T": { "value": 300, "unit": "K" } } },
              { "firstId": 20, "cloud": { "ions": 3, "seed": 5, "temperature": { "value": 300, "unit": "K" } } } ] }
            """).Rows!;

        IonListTests.Same(rows[1], rows[0] with { Id = 2 });
        IonListTests.Same(rows[3], rows[2] with { Id = 4 });
        Assert.NotEqual(Bits(rows[0].Launch.Velocity.X), Bits(rows[2].Launch.Velocity.X));

        for (var k = 0; k < 3; k++)
        {
            IonListTests.Same(rows[7 + k], rows[4 + k] with { Id = 20 + k });
        }
    }

    /// <summary>
    /// <c>-0</c> and <c>0</c> are one value, so one group and one field, in a JSON list and in
    /// a CSV.
    /// </summary>
    [Fact]
    public void MinusZeroAndZeroAreOneGroup()
    {
        var expansion = Valid("""
            { "schemaVersion": "0.1", "ions": [
              { "id": 1, "overrides": { "U": { "value": 0.0, "unit": "V" } } },
              { "id": 2, "overrides": { "U": { "value": -0.0, "unit": "V" } } } ] }
            """);

        Assert.Equal([0, 0], expansion.RowGroups);
        Assert.Equal(Bits(0.0), Bits(expansion.OverrideSets[0].Values["U"]));

        const string Row = ",,1e-25,1.6e-19,0,0,0,0,0,0,0,";
        var read = IonListCsv.Read(IonListCsv.Header + ",p:U\n1" + Row + "-0\n2" + Row + "0\n");
        Assert.True(read.IsValid);
        Assert.Equal([0, 0], read.RowGroups);
        Assert.Equal(Bits(0.0), Bits(read.OverrideSets[0].Values["U"]));
    }

    /// <summary>
    /// The expansion records which rows took their birth from an <c>rfPhase</c>, explicit or
    /// generated, so a flight can tell which rows a changed drive frequency would move; a CSV
    /// states birth times and records none.
    /// </summary>
    [Fact]
    public void TheRowsBornByAnRfPhaseAreRecorded()
    {
        var expansion = IonList.Expand(IonList.Parse("""
            { "schemaVersion": "0.1", "ions": [
              { "id": 1, "rfPhase": 0.25 },
              { "id": 2, "birthTime": { "value": 1, "unit": "us" } },
              { "id": 3 } ],
              "ionSets": [
              { "n": 2, "rfPhase": { "sequence": { "first": 0, "step": 0.5 } } },
              { "n": 2, "birthTime": { "linspace": { "first": 0, "last": 1 }, "unit": "us" } } ] }
            """), IonListTests.Compile(IonListTests.Driven));

        Assert.True(expansion.IsValid, string.Join("; ", expansion.Errors.Select(e => $"{e.Path}: {e.Constraint}")));
        Assert.Equal([0, 3, 4], expansion.RfPhaseRows);

        var read = IonListCsv.Read(IonListCsv.Write(expansion.Rows!));
        Assert.True(read.IsValid);
        Assert.Empty(read.RfPhaseRows);
    }

    /// <summary>
    /// Rows are grouped by the bits of their SI values: <c>1.0 V</c>, <c>1 V</c> and
    /// <c>1000 mV</c> are one group, the key order inside an object does not matter, and the
    /// groups are numbered in the order their first row appears. A row without overrides is
    /// a group of its own; the rows are the ones the list gives without any overrides.
    /// </summary>
    [Fact]
    public void RowsAreGroupedByTheBitsOfTheirOverridesInOrderOfAppearance()
    {
        var expansion = Valid("""
            { "schemaVersion": "0.1", "ions": [
              { "id": 1, "overrides": { "U": { "value": 1.0, "unit": "V" } } },
              { "id": 2 },
              { "id": 3, "overrides": { "U": { "value": 1, "unit": "V" } } },
              { "id": 4, "overrides": { "U": { "value": 1000, "unit": "mV" } } },
              { "id": 5, "overrides": { "U": { "value": 2, "unit": "V" }, "phase": { "value": 0.5, "unit": "1" } } },
              { "id": 6, "overrides": { "phase": { "value": 0.5, "unit": "1" }, "U": { "value": 2, "unit": "V" } } },
              { "id": 7, "overrides": { } },
              { "id": 8, "overrides": { "U": { "value": 10, "unit": "V" } } } ] }
            """);

        Assert.Equal([0, 1, 0, 0, 2, 2, 1, 3], expansion.RowGroups);
        Assert.Equal(4, expansion.OverrideSets.Count);

        Assert.Equal(["U"], expansion.OverrideSets[0].Values.Keys);
        Assert.Equal(Bits(1.0), Bits(expansion.OverrideSets[0].Values["U"]));
        Assert.Equal("V", expansion.OverrideSets[0].Written!["U"].Unit);
        Assert.Equal("/ions/0/overrides", expansion.OverrideSets[0].DeclaredAt);
        Assert.Empty(expansion.OverrideSets[1].Values);
        Assert.Equal(["U", "phase"], expansion.OverrideSets[2].Values.Keys);
        Assert.Equal(Bits(Quantity.From(10, "V").SiValue), Bits(expansion.OverrideSets[3].Values["U"]));

        var plain = Valid("""{ "schemaVersion": "0.1", "ions": [ { "id": 1 }, { "id": 2 }, { "id": 3 }, { "id": 4 }, { "id": 5 }, { "id": 6 }, { "id": 7 }, { "id": 8 } ] }""");

        Assert.Empty(plain.OverrideSets);
        Assert.Empty(plain.RowGroups);

        for (var k = 0; k < plain.Rows!.Count; k++)
        {
            IonListTests.Same(plain.Rows[k], expansion.Rows![k]);
        }
    }

    /// <summary>
    /// A set's override is one constant for every row of the set, generated or drawn from a
    /// cloud, and draws nothing: the rows are the rows the same sets give without it.
    /// </summary>
    [Fact]
    public void ASetsOverrideHoldsForEveryRowOfItAndDrawsNothing()
    {
        const string Sets = """
            { "schemaVersion": "0.1", "ions": [ { "id": 1 } ], "ionSets": [
              { "n": 3, "seed": 4, "kineticEnergy": { "uniform": { "min": 1, "max": 2 }, "unit": "eV" } OVERRIDE },
              { "cloud": { "ions": 4, "seed": 5, "temperature": { "value": 300, "unit": "K" } } OVERRIDE } ] }
            """;

        var with = Valid(Sets.Replace("OVERRIDE", """, "overrides": { "U": { "value": -3, "unit": "V" } }""", StringComparison.Ordinal));
        var without = Valid(Sets.Replace("OVERRIDE", string.Empty, StringComparison.Ordinal));

        Assert.Equal([0, 1, 1, 1, 1, 1, 1, 1], with.RowGroups);
        Assert.Equal("/ionSets/0/overrides", with.OverrideSets[1].DeclaredAt);
        Assert.Equal(Bits(-3.0), Bits(with.OverrideSets[1].Values["U"]));
        Assert.Equal(without.Seeds, with.Seeds);

        for (var k = 0; k < without.Rows!.Count; k++)
        {
            IonListTests.Same(without.Rows[k], with.Rows![k]);
        }
    }

    /// <summary>
    /// The expanded CSV carries the overrides as <c>p:</c> columns in SI, one per parameter
    /// in name order, empty where a row does not override it; read back it gives the same
    /// rows and the same groups, and written again the same bytes.
    /// </summary>
    [Fact]
    public void TheCsvCarriesTheOverridesAsParameterColumns()
    {
        var expansion = Valid("""
            { "schemaVersion": "0.1", "ions": [
              { "id": 1, "overrides": { "phase": { "value": 0.123456789, "unit": "1" } } },
              { "id": 2 },
              { "id": 3, "overrides": { "U": { "value": 0.05, "unit": "kV" } } },
              { "id": 4, "overrides": { "U": { "value": 50, "unit": "V" } } } ] }
            """);

        var text = IonListCsv.Write(expansion.Rows!, expansion.RowGroups, expansion.OverrideSets);
        output.WriteLine(text);

        var lines = text.Split('\n');
        Assert.Equal(IonListCsv.Header + ",p:U,p:phase", lines[0]);
        Assert.EndsWith(",,0.123456789", lines[1], StringComparison.Ordinal);
        Assert.EndsWith(",,", lines[2], StringComparison.Ordinal);
        Assert.EndsWith(",50,", lines[3], StringComparison.Ordinal);

        var read = IonListCsv.Read(text);
        Assert.True(read.IsValid, string.Join("; ", read.Errors.Select(e => $"{e.Path}: {e.Constraint}")));
        Assert.Equal(expansion.RowGroups, read.RowGroups);
        Assert.Equal(expansion.OverrideSets.Select(Key), read.OverrideSets.Select(Key));
        Assert.Null(read.OverrideSets[0].Written);
        Assert.Equal(text, IonListCsv.Write(read.Rows!, read.RowGroups, read.OverrideSets));

        for (var k = 0; k < read.Rows!.Count; k++)
        {
            IonListTests.Same(expansion.Rows![k], read.Rows[k]);
        }

        // Without overrides the eleven columns alone, the bytes the list wrote before overrides existed.
        var plain = IonListCsv.Read(string.Join('\n', lines.Select(line => string.Join(',', line.Split(',').Take(11)))));
        Assert.True(plain.IsValid);
        Assert.Empty(plain.OverrideSets);
        Assert.Equal(IonListCsv.Write(plain.Rows!), IonListCsv.Write(plain.Rows!, [], []));
    }

    public static TheoryData<string, string> CsvRefusals => new()
    {
        { IonListCsv.Header + ",p:\n1,,1e-25,1.6e-19,0,0,0,0,0,0,0,1\n", "/" },
        { IonListCsv.Header + ",p:U,p:U\n1,,1e-25,1.6e-19,0,0,0,0,0,0,0,1,1\n", "/" },
        { IonListCsv.Header + ",U\n1,,1e-25,1.6e-19,0,0,0,0,0,0,0,1\n", "/" },
        { IonListCsv.Header + ",p:U\n1,,1e-25,1.6e-19,0,0,0,0,0,0,0\n", "/0" },
        { IonListCsv.Header + ",p:U\n1,,1e-25,1.6e-19,0,0,0,0,0,0,0,NaN\n", "/0/p:U" },
        { IonListCsv.Header + ",p:U\n1,,1e-25,1.6e-19,0,0,0,0,0,0,0,1\n2,,1e-25,1.6e-19,0,0,0,0,0,0,0,Infinity\n", "/1/p:U" },
    };

    /// <summary>A <c>p:</c> header that names nothing or a name twice, a short row, a non-finite value.</summary>
    [Theory]
    [MemberData(nameof(CsvRefusals))]
    public void EachCsvOverrideMistakeIsRefusedAtItsRowAndColumn(string csv, string at)
    {
        var read = IonListCsv.Read(csv);

        Assert.Null(read.Rows);
        var error = Assert.Single(read.Errors);
        output.WriteLine($"{error.Path}: {error.Constraint}");
        Assert.Equal(at, error.Path);
    }

    /// <summary>
    /// A CSV states SI and no units, so it is checked against the model when it is loaded:
    /// a column naming no parameter, or a derived one, or a value outside the bounds, is refused
    /// at the first row of its group and its column.
    /// </summary>
    [Fact]
    public void ACsvsParameterColumnsAreCheckedAgainstTheModel()
    {
        var directory = Directory.CreateTempSubdirectory("einzel-overrides-");

        try
        {
            var model = IonListTests.Compile(Model);
            var csv = Path.Combine(directory.FullName, "list.ions.csv");
            const string Row = ",,1e-25,1.6e-19,0,0,0,0,0,0,0,";

            File.WriteAllText(csv, IonListCsv.Header + ",p:U,p:X\n1" + Row + "5,\n2" + Row + "5,\n3" + Row + "5,7\n");
            var unknown = Assert.Single(IonList.Load(csv, model).Errors);
            Assert.Equal((ErrorCodes.SchemaInvalid, "/2/p:X"), (unknown.Code, unknown.Path));

            File.WriteAllText(csv, IonListCsv.Header + ",p:W\n1" + Row + "5\n");
            Assert.Equal("/0/p:W", Assert.Single(IonList.Load(csv, model).Errors).Path);

            File.WriteAllText(csv, IonListCsv.Header + ",p:U\n1" + Row + "500\n");
            var bounds = Assert.Single(IonList.Load(csv, model).Errors);
            Assert.Equal((ErrorCodes.ValueOutOfBounds, "/0/p:U"), (bounds.Code, bounds.Path));

            File.WriteAllText(csv, IonListCsv.Header + ",p:U\n1" + Row + "5\n2" + Row + "\n");
            var loaded = IonList.Load(csv, model);
            Assert.True(loaded.IsValid);
            Assert.Equal([0, 1], loaded.RowGroups);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
