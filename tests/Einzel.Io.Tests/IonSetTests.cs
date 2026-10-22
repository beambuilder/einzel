using System.Globalization;
using Einzel.Core.Errors;
using Einzel.Core.Units;
using Einzel.Io.IonLists;
using Einzel.Transport;
using Xunit.Abstractions;

namespace Einzel.Io.Tests;

/// <summary>
/// The seeded generators of an ion list: what each draws, in which order, and that the same
/// seed gives the same rows.
/// </summary>
/// <remarks>
/// Statistical checks use fixed seeds, so each is a deterministic test with a stated
/// tolerance rather than a flaky one: the tolerances are four standard errors for a mean or
/// a variance, and the 0.1 % point of chi-square for a histogram. The golden rows are the
/// expanded <c>.ions.csv</c> text of one seeded set per generator, stored beside this file.
/// </remarks>
public sealed class IonSetTests(ITestOutputHelper output)
{
    private static IReadOnlyList<IonRow> Rows(string sets, string model = IonListTests.Beam)
    {
        var expansion = IonList.Expand(
            IonList.Parse("{ \"schemaVersion\": \"0.1\", \"ionSets\": [ " + sets + " ] }"), IonListTests.Compile(model));
        Assert.True(expansion.IsValid, string.Join("; ", expansion.Errors.Select(e => $"{e.Path}: {e.Constraint}")));
        return expansion.Rows!;
    }

    private static double[] BirthsUs(IReadOnlyList<IonRow> rows) =>
        [.. rows.Select(r => r.BirthTimeSeconds / Quantity.From(1, "us").SiValue)];

    [Fact]
    public void ABirthPhaseGridIsASequenceOnRfPhase()
    {
        // The corpus's eight birth phases: k/8 of a 1.1 MHz cycle, k = 0..7, no draw at all.
        var rows = Rows("""{ "n": 8, "rfPhase": { "sequence": { "first": 0, "step": 0.125 } } }""", IonListTests.Driven);

        Assert.Equal(Enumerable.Range(1, 8), rows.Select(r => r.Id));
        Assert.Equal(
            Enumerable.Range(0, 8).Select(k => BitConverter.DoubleToInt64Bits((0 + (k * 0.125)) * 1.0 / 1.1e6)),
            rows.Select(r => BitConverter.DoubleToInt64Bits(r.BirthTimeSeconds)));
    }

    [Fact]
    public void ALinspaceHitsBothEndsExactly()
    {
        var rows = Rows("""{ "n": 5, "birthTime": { "linspace": { "first": 0, "last": 1 }, "unit": "us" } }""");
        var microsecond = Quantity.From(1, "us").SiValue;

        Assert.Equal(
            Enumerable.Range(0, 5).Select(k => k * 0.25).Select(v => BitConverter.DoubleToInt64Bits(v * microsecond)),
            rows.Select(r => BitConverter.DoubleToInt64Bits(r.BirthTimeSeconds)));
    }

    [Fact]
    public void ConstantsInASetAreTheSameRowsWrittenOut()
    {
        const string Attributes = """
            "massToCharge": { "value": 101, "unit": "Da" }, "chargeNumber": 2,
            "kineticEnergy": { "value": 2, "unit": "eV" }, "birthTime": { "value": 0.1, "unit": "us" },
            "position": { "value": [0, 0.02, 0.05], "unit": "mm" }, "direction": { "value": [3, 0.1, 0.2] }, "tag": "t"
            """;

        var set = Rows("{ \"n\": 3, " + Attributes + " }");
        var written = IonList.Expand(
            IonList.Parse("{ \"schemaVersion\": \"0.1\", \"ions\": [ "
                + string.Join(", ", Enumerable.Range(1, 3).Select(id => $"{{ \"id\": {id}, " + Attributes + " }"))
                + " ] }"),
            IonListTests.Compile(IonListTests.Beam)).Rows!;

        for (var k = 0; k < 3; k++)
        {
            IonListTests.Same(written[k], set[k]);
        }
    }

    [Fact]
    public void UniformAndGaussianHaveTheirMoments()
    {
        const int N = 10_000;

        var uniform = BirthsUs(Rows($$"""{ "n": {{N}}, "seed": 11, "birthTime": { "uniform": { "min": 1, "max": 3 }, "unit": "us" } }"""));
        var gaussian = BirthsUs(Rows($$"""{ "n": {{N}}, "seed": 12, "birthTime": { "gaussian": { "mean": 5, "sigma": 0.5 }, "unit": "us" } }"""));

        var (uMean, uVariance) = Moments(uniform);
        var (gMean, gVariance) = Moments(gaussian);

        output.WriteLine($"uniform  mean {uMean:F5} (2), variance {uVariance:F5} ({1.0 / 3.0:F5})");
        output.WriteLine($"gaussian mean {gMean:F5} (5), variance {gVariance:F5} (0.25)");

        Assert.All(uniform, t => Assert.InRange(t, 1.0, 3.0));
        Assert.InRange(uMean, 2.0 - (4 * 0.5774 / 100), 2.0 + (4 * 0.5774 / 100));
        Assert.InRange(uVariance / (1.0 / 3.0), 0.96, 1.04);
        Assert.InRange(gMean, 5.0 - (4 * 0.5 / 100), 5.0 + (4 * 0.5 / 100));
        Assert.InRange(gVariance / 0.25, 0.94, 1.06);
    }

    [Fact]
    public void ADiscIsUniformInArea()
    {
        const int N = 10_000;
        var rows = Rows($$"""
            { "n": {{N}}, "seed": 21,
              "position": { "disc": { "centre": [0, 0, 1], "normal": [0, 0, 2], "radius": 2 }, "unit": "mm" } }
            """);

        var mm = Quantity.From(1, "mm").SiValue;
        var squared = rows.Select(r => ((r.Launch.Position.X * r.Launch.Position.X) + (r.Launch.Position.Y * r.Launch.Position.Y)) / (4 * mm * mm)).ToArray();
        var azimuth = rows.Select(r => (Math.Atan2(r.Launch.Position.Y, r.Launch.Position.X) + Math.PI) / (2 * Math.PI)).ToArray();

        Assert.All(rows, r => Assert.Equal(mm, r.Launch.Position.Z, 1e-18));
        Assert.All(squared, s => Assert.InRange(s, 0.0, 1.0));

        // Uniform in area means r^2 uniform: ten equal-area annuli, and eight equal sectors.
        AssertUniform(squared, 10, "r^2");
        AssertUniform(azimuth, 8, "azimuth");
    }

    [Fact]
    public void AFilledConeIsUniformInSolidAngleAndASurfaceConeIsItsEdge()
    {
        const int N = 10_000;
        var cosMax = Math.Cos(10 * Math.PI / 180);

        IReadOnlyList<IonRow> Cone(bool fill) => Rows($$"""
            { "n": {{N}}, "seed": 31,
              "direction": { "cone": { "axis": [0, 0, 5], "halfAngle": { "value": 10, "unit": "deg" }, "fill": {{(fill ? "true" : "false")}} } } }
            """);

        double Cos(IonRow r) => r.Launch.Velocity.Z / r.Launch.Velocity.Length;

        var filled = Cone(fill: true);
        var cosines = filled.Select(Cos).ToArray();

        Assert.All(cosines, c => Assert.InRange(c, cosMax - 1e-12, 1.0 + 1e-12));

        // Uniform in solid angle means cos(theta) uniform between the edge and the axis.
        AssertUniform([.. cosines.Select(c => Math.Clamp((1.0 - c) / (1.0 - cosMax), 0.0, 1.0 - 1e-15))], 10, "cos theta");

        var surface = Cone(fill: false);

        Assert.All(surface, r => Assert.Equal(cosMax, Cos(r), 1e-12));
        AssertUniform([.. surface.Select(r => (Math.Atan2(r.Launch.Velocity.Y, r.Launch.Velocity.X) + Math.PI) / (2 * Math.PI))], 8, "azimuth");
    }

    [Fact]
    public void AHemisphereIsTheWidestCone()
    {
        var rows = Rows("""{ "n": 200, "seed": 4, "direction": { "cone": { "axis": [1, 0, 0], "halfAngle": { "value": 90, "unit": "deg" } } } }""");

        Assert.All(rows, r => Assert.True(r.Launch.Velocity.X >= -1e-9));
    }

    [Fact]
    public void RaisingNKeepsTheIonsAlreadyDrawn()
    {
        static string Set(int n) => $$"""
            { "n": {{n}}, "seed": 5, "tag": "p",
              "massToCharge": { "gaussian": { "mean": 100, "sigma": 1 }, "unit": "Da" },
              "kineticEnergy": { "uniform": { "min": 1, "max": 3 }, "unit": "eV" },
              "rfPhase": { "sequence": { "first": 0, "step": 0.125 } },
              "position": { "x": { "value": 0 }, "y": { "gaussian": { "mean": 0, "sigma": 0.05 } }, "z": { "uniform": { "min": -0.1, "max": 0.1 } }, "unit": "mm" },
              "direction": { "cone": { "axis": [1, 0, 0], "halfAngle": { "value": 3, "unit": "deg" } } } }
            """;

        var few = Rows(Set(10), IonListTests.Driven);
        var many = Rows(Set(40), IonListTests.Driven);

        for (var k = 0; k < few.Count; k++)
        {
            IonListTests.Same(many[k], few[k]);
        }
    }

    /// <summary>
    /// The expansion records each set's seed in document order: a cloud set's, then a
    /// generated set's; a set that draws nothing at random and states no seed records none.
    /// </summary>
    [Fact]
    public void EachSetsSeedIsRecordedInDocumentOrder()
    {
        var expansion = IonList.Expand(
            IonList.Parse("""
                { "schemaVersion": "0.1",
                  "ions": [ { "id": 1 } ],
                  "ionSets": [
                    { "cloud": { "ions": 4, "seed": 3, "temperature": { "value": 300, "unit": "K" } } },
                    { "n": 2, "birthTime": { "sequence": { "first": 0, "step": 1 }, "unit": "us" } },
                    { "n": 3, "seed": 9, "kineticEnergy": { "uniform": { "min": 1, "max": 2 }, "unit": "eV" } } ] }
                """),
            IonListTests.Compile(IonListTests.Beam));

        Assert.True(expansion.IsValid);
        Assert.Equal(10, expansion.Rows!.Count);
        Assert.Equal([3L, 9L], expansion.Seeds);
    }

    /// <summary>
    /// The edges of two generators: a <c>linspace</c> of one ion is its <c>first</c>, and a
    /// <c>uniform</c> of no width is its <c>min</c>; both are legal sets.
    /// </summary>
    [Fact]
    public void ALinspaceOfOneIsItsFirstAndAUniformOfNoWidthIsItsMin()
    {
        var microsecond = Quantity.From(1, "us").SiValue;
        var one = Rows("""{ "n": 1, "birthTime": { "linspace": { "first": 2, "last": 5 }, "unit": "us" } }""");
        var flat = Rows("""{ "n": 3, "seed": 8, "birthTime": { "uniform": { "min": 1.5, "max": 1.5 }, "unit": "us" } }""");

        Assert.Equal(BitConverter.DoubleToInt64Bits(2 * microsecond), BitConverter.DoubleToInt64Bits(Assert.Single(one).BirthTimeSeconds));
        Assert.All(flat, r => Assert.Equal(BitConverter.DoubleToInt64Bits(1.5 * microsecond), BitConverter.DoubleToInt64Bits(r.BirthTimeSeconds)));
    }

    /// <summary>
    /// One seeded set per generator, expanded to CSV text and compared with its golden rows
    /// (<c>Golden/ion-sets/*.csv</c>, recorded on Windows).
    /// </summary>
    /// <remarks>
    /// <c>uniform</c>, <c>sequence</c> and <c>linspace</c> use only IEEE addition,
    /// multiplication and division on <see cref="Random"/>'s doubles, so their text is equal
    /// on every runner. The other five call <c>Math.Log</c>, <c>Math.Sqrt</c>, <c>Math.Cos</c>
    /// or <c>Math.Sin</c>, which the platform's maths library computes: their text is equal on
    /// Windows, and elsewhere every number agrees within 1e-12 relative.
    /// </remarks>
    [Theory]
    [InlineData("uniform", true, """{ "n": 50, "seed": 1, "kineticEnergy": { "uniform": { "min": 1, "max": 3 }, "unit": "eV" } }""")]
    [InlineData("gaussian", false, """{ "n": 50, "seed": 2, "massToCharge": { "gaussian": { "mean": 600, "sigma": 20 }, "unit": "Da" } }""")]
    [InlineData("sequence", true, """{ "n": 50, "birthTime": { "sequence": { "first": 0.5, "step": 0.1 }, "unit": "us" } }""")]
    [InlineData("linspace", true, """{ "n": 50, "accelerationPotential": { "linspace": { "first": 90, "last": 110 }, "unit": "V" } }""")]
    [InlineData("disc", false, """{ "n": 50, "seed": 3, "position": { "disc": { "centre": [-1, 0, 0], "normal": [1, 1, 0], "radius": 0.5 }, "unit": "mm" } }""")]
    [InlineData("cone", false, """{ "n": 50, "seed": 4, "direction": { "cone": { "axis": [1, 0.1, 0], "halfAngle": { "value": 5, "unit": "deg" } } } }""")]
    [InlineData("cone-surface", false, """{ "n": 50, "seed": 5, "direction": { "cone": { "axis": [1, 0, 0], "halfAngle": { "value": 2, "unit": "deg" }, "fill": false } } }""")]
    [InlineData("per-axis", false, """{ "n": 50, "seed": 6, "position": { "x": { "value": -1 }, "y": { "gaussian": { "mean": 0, "sigma": 0.1 } }, "z": { "uniform": { "min": -0.2, "max": 0.2 } }, "unit": "mm" } }""")]
    public void ASeededSetExpandsToItsGoldenRows(string generator, bool libraryFree, string set)
    {
        var text = IonListCsv.Write(Rows(set));
        var golden = File.ReadAllText(
            Path.Combine(IonListTests.RepositoryRoot(), "tests", "Einzel.Io.Tests", "Golden", "ion-sets", generator + ".csv"))
            .ReplaceLineEndings("\n");

        if (libraryFree || OperatingSystem.IsWindows())
        {
            Assert.Equal(golden, text);
            return;
        }

        string[][] Cells(string csv) => [.. csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(','))];
        var (expected, actual) = (Cells(golden), Cells(text));
        var worst = 0.0;

        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected[0], actual[0]);

        for (var row = 1; row < expected.Length; row++)
        {
            Assert.Equal(expected[row][..2], actual[row][..2]);

            for (var column = 2; column < expected[row].Length; column++)
            {
                var (e, a) = (Number(expected[row][column]), Number(actual[row][column]));
                worst = Math.Max(worst, e == a ? 0.0 : Math.Abs(a - e) / Math.Max(Math.Abs(e), Math.Abs(a)));
            }
        }

        output.WriteLine($"{generator}: largest relative difference {worst:G3}");
        Assert.True(worst <= 1e-12, $"{generator}: a number differs by {worst:G3} relative");

        static double Number(string cell) => double.Parse(cell, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    public static TheoryData<string, string, string> Refusals => new()
    {
        { """{ }""", ErrorCodes.SchemaInvalid, "/ionSets/0/n" },
        { """{ "n": 0 }""", ErrorCodes.ValueOutOfBounds, "/ionSets/0/n" },
        { """{ "n": 2, "birthTime": { "uniform": { "min": 0, "max": 1 }, "unit": "us" } }""", ErrorCodes.SchemaInvalid, "/ionSets/0/seed" },
        { """{ "n": 2, "seed": 1, "birthTime": { "gaussian": { "mean": 1, "sigma": -0.1 }, "unit": "us" } }""", ErrorCodes.ValueOutOfBounds, "/ionSets/0/birthTime/gaussian/sigma" },
        { """{ "n": 2, "seed": 1, "birthTime": { "uniform": { "min": 2, "max": 1 }, "unit": "us" } }""", ErrorCodes.ValueOutOfBounds, "/ionSets/0/birthTime/uniform/max" },
        { """{ "n": 2, "birthTime": { "value": 1, "sequence": { "first": 0, "step": 1 }, "unit": "us" } }""", ErrorCodes.SchemaInvalid, "/ionSets/0/birthTime" },
        { """{ "n": 2, "birthTime": { "value": 1 } }""", ErrorCodes.UnitsRequired, "/ionSets/0/birthTime/unit" },
        { """{ "n": 2, "birthTime": { "value": 1, "unit": "mm" } }""", ErrorCodes.UnitsIncompatible, "/ionSets/0/birthTime/unit" },
        { """{ "n": 2, "rfPhase": { "value": 0.5, "unit": "1" } }""", ErrorCodes.SchemaInvalid, "/ionSets/0/rfPhase/unit" },
        { """{ "n": 20, "seed": 1, "massToCharge": { "gaussian": { "mean": 1, "sigma": 5 }, "unit": "Da" } }""", ErrorCodes.ValueOutOfBounds, "/ionSets/0/massToCharge" },
        { """{ "n": 2, "seed": 1, "position": { "disc": { "centre": [0, 0, 0], "normal": [1, 0, 0], "radius": 0 }, "unit": "mm" } }""", ErrorCodes.ValueOutOfBounds, "/ionSets/0/position/disc/radius" },
        { """{ "n": 2, "seed": 1, "position": { "disc": { "centre": [0, 0, 0], "normal": [0, 0, 0], "radius": 1 }, "unit": "mm" } }""", ErrorCodes.ValueOutOfBounds, "/ionSets/0/position/disc/normal" },
        { """{ "n": 2, "position": { "x": { "value": 0 }, "y": { "value": 0 }, "unit": "mm" } }""", ErrorCodes.SchemaInvalid, "/ionSets/0/position" },
        { """{ "n": 2, "position": { "value": [0, 0, 0] } }""", ErrorCodes.UnitsRequired, "/ionSets/0/position/unit" },
        { """{ "n": 2, "seed": 1, "direction": { "cone": { "axis": [1, 0, 0], "halfAngle": { "value": 0, "unit": "deg" } } } }""", ErrorCodes.ValueOutOfBounds, "/ionSets/0/direction/cone/halfAngle" },
        { """{ "n": 2, "seed": 1, "direction": { "cone": { "axis": [1, 0, 0], "halfAngle": { "value": 91, "unit": "deg" } } } }""", ErrorCodes.ValueOutOfBounds, "/ionSets/0/direction/cone/halfAngle" },
        { """{ "n": 2, "seed": 1, "direction": { "value": [1, 0, 0], "cone": { "axis": [1, 0, 0], "halfAngle": { "value": 1, "unit": "deg" } } } }""", ErrorCodes.SchemaInvalid, "/ionSets/0/direction" },
        { """{ "n": 2, "direction": { "value": [0, 0, 0] } }""", ErrorCodes.ValueOutOfBounds, "/ionSets/0/direction/value" },
        { """{ "n": 2, "position": { "disc": { "centre": [0, 0, 0], "normal": [1, 0, 0], "radius": 1 }, "unit": "mm" } }""", ErrorCodes.SchemaInvalid, "/ionSets/0/seed" },
        { """{ "n": 2, "direction": { "cone": { "axis": [1, 0, 0], "halfAngle": { "value": 1, "unit": "deg" } } } }""", ErrorCodes.SchemaInvalid, "/ionSets/0/seed" },
        { """{ "n": 2, "seed": 1, "direction": { "cone": { "axis": [1, 0, 0], "halfAngle": null } } }""", ErrorCodes.SchemaInvalid, "/ionSets/0/direction/cone/halfAngle" },
        { """{ "n": 2, "position": { "x": { "value": 1e306 }, "y": { "value": 0 }, "z": { "value": 0 }, "unit": "km" } }""", ErrorCodes.ValueOutOfBounds, "/ionSets/0/position" },
        { """{ "n": 2, "birthTime": { "value": 20, "unit": "ms" } }""", ErrorCodes.ValueOutOfBounds, "/ionSets/0/birthTime" },
        { """{ "cloud": { }, "seed": 3 }""", ErrorCodes.SchemaInvalid, "/ionSets/0/cloud" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void EachGeneratorMistakeIsRefusedAtItsPointer(string set, string code, string at)
    {
        var expansion = IonList.Expand(
            IonList.Parse("{ \"schemaVersion\": \"0.1\", \"ionSets\": [ " + set + " ] }"), IonListTests.Compile(IonListTests.Beam));

        Assert.Null(expansion.Rows);
        var error = Assert.Single(expansion.Errors);
        output.WriteLine($"{error.Code} {error.Path}: {error.Constraint}");
        Assert.Equal((code, at), (error.Code, error.Path));
    }

    [Theory]
    [InlineData("""{ "n": 2, "kineticEnergy": { "cone": { "axis": [1, 0, 0], "halfAngle": { "value": 1, "unit": "deg" } } } }""", "/ionSets/0/kineticEnergy/cone")]
    [InlineData("""{ "n": 2, "direction": { "disc": { } } }""", "/ionSets/0/direction/disc")]
    [InlineData("""{ "n": 2, "birthTime": { "uniform": { "min": 1 }, "unit": "us" } }""", "/ionSets/0/birthTime/uniform")]
    public void AGeneratorWhereItDoesNotBelongIsRefusedOnReading(string set, string at)
    {
        var failure = Assert.Throws<EinzelException>(
            () => IonList.Parse("{ \"schemaVersion\": \"0.1\", \"ionSets\": [ " + set + " ] }"));

        output.WriteLine($"{failure.Error.Path}: {failure.Error.Constraint}");
        Assert.Equal((ErrorCodes.SchemaInvalid, at), (failure.Error.Code, failure.Error.Path));
    }

    private static (double Mean, double Variance) Moments(double[] values)
    {
        var mean = values.Average();
        return (mean, values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1));
    }

    /// <summary>Chi-square of equal bins on [0, 1) against the 0.1 % point for its degrees of freedom.</summary>
    private void AssertUniform(double[] values, int bins, string what)
    {
        var counts = new int[bins];

        foreach (var value in values)
        {
            counts[Math.Min(bins - 1, (int)(value * bins))]++;
        }

        var expected = values.Length / (double)bins;
        var chi = counts.Sum(c => (c - expected) * (c - expected) / expected);
        var limit = bins switch { 8 => 24.32, 10 => 27.88, _ => throw new ArgumentOutOfRangeException(nameof(bins)) };

        output.WriteLine($"{what}: chi-square {chi:F2} over {bins} bins (0.1 % point {limit})");
        Assert.True(chi < limit, $"{what} is not uniform: chi-square {chi:F2} over {bins} bins");
    }
}
