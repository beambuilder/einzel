using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Io;

namespace Einzel.Io.Tests;

/// <summary>
/// That a name is an electrode's identity within its solve: an unnamed one is named after
/// its place in the list, and two sharing one are refused.
/// </summary>
/// <remarks>
/// <para>
/// Every unnamed electrode used to be called <c>electrode</c>, and nothing stopped two in
/// one solve sharing a name. A driven or sequenced geometry builds its channels from a map
/// keyed by name, so the later of two same-named electrodes set the potential of both, and
/// an ion lost on either was itemized as striking one surface (ACC-5). The end-to-end
/// consequence is pinned in <c>Einzel.Cli.Tests.ElectrodeNameTests</c>; these pin the rule.
/// </para>
/// <para>
/// <b>Within one solve, and only there.</b> The Astral's two mirror solves each declare all
/// twenty-two boards under the same names, at the potentials one mirror or the other holds,
/// and that is right: it is the same metal, and an ion striking it struck one surface.
/// </para>
/// </remarks>
public sealed class ElectrodeNameValidationTests
{
    private const string Head =
        """
        {
          "schemaVersion": "0.14",
          "name": "names",
          "parameters": {
            "plateVolts": { "value": 2, "unit": "V" },
            "start": { "value": -10, "unit": "mm" },
            "pitch": { "value": 3, "unit": "mm" },
            "width": { "value": 1, "unit": "mm" }
          },
          "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 0], "unit": "mm" },
            "direction": { "value": [0, 0, 1] },
            "accelerationPotential": { "value": 100, "unit": "V" }
          },
        """;

    private const string Tail =
        """
          "detector": { "planePoint": { "value": [0, 0, 50], "unit": "mm" }, "normal": { "value": [0, 0, -1] } },
          "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 50, "unit": "us" } }
        }
        """;

    /// <summary>
    /// Unnamed electrodes are named after their place in the list, so the name matches the
    /// path an error gives and does not move when another electrode gains a name.
    /// </summary>
    [Fact]
    public void AnUnnamedElectrodeIsNamedAfterItsPlaceInTheList()
    {
        var solve = Compile(Plane(Plate(null, Upper, "plateVolts"), Plate("middle", Middle, "0"), Plate(null, Lower, "-plateVolts")));

        Assert.Equal(["electrode-0", "middle", "electrode-2"], solve.Electrodes.Select(e => e.Name));
    }

    /// <summary>The copies of an unnamed repeated electrode take its positional name as their base.</summary>
    [Fact]
    public void AnUnnamedRepeatIsNamedAfterItsPlaceInTheList()
    {
        var solve = Compile(Plane(Plate("upper", Upper, "plateVolts"), Repeated(null, 3, Lower)));

        Assert.Equal(["upper", "electrode-1-0", "electrode-1-1", "electrode-1-2"], solve.Electrodes.Select(e => e.Name));
    }

    /// <summary>
    /// A stage re-compiles every electrode over its own parameters, and must reach the same
    /// names, or a stage's channels would be keyed differently from the base state's.
    /// </summary>
    [Fact]
    public void AStageNamesUnnamedElectrodesAsTheBaseStateDoes()
    {
        var sequence =
            """
              "sequence": [
                { "name": "deflect", "duration": { "value": 20, "unit": "us" },
                  "set": { "plateVolts": { "value": 4, "unit": "V" } } }
              ],
            """;

        var solve = Compile(Plane(Plate(null, Upper, "plateVolts"), Plate(null, Lower, "-plateVolts")), sequence);

        Assert.Single(solve.Stages);
        Assert.Equal(solve.Electrodes.Select(e => e.Name), solve.Stages[0].Electrodes.Select(e => e.Name));
        Assert.Equal(4.0, solve.Stages[0].Electrodes[0].Potential, 12);
    }

    /// <summary>Two plates given one name are refused, at the second one's name.</summary>
    [Fact]
    public void TwoElectrodesSharingANameAreRefused()
    {
        var errors = Errors(Plane(Plate("plate", Upper, "plateVolts"), Plate("plate", Lower, "-plateVolts")));

        var error = Assert.Single(errors);
        Assert.Equal(ErrorCodes.SchemaInvalid, error.Code);
        Assert.Equal("/fields/0/solve/electrodes/1/name", error.Path);
        Assert.Contains("electrodes 0 and 1 in this solve are both called 'plate'", error.Constraint, StringComparison.Ordinal);
    }

    /// <summary>
    /// A repeat's copies collide with an explicit name as readily as two declarations do,
    /// because both are names in the one solve.
    /// </summary>
    [Fact]
    public void ARepeatCopyCollidingWithANamedElectrodeIsRefused()
    {
        var errors = Errors(Plane(Repeated("ring", 3, Upper), Plate("ring-2", Lower, "-plateVolts")));

        var error = Assert.Single(errors);
        Assert.Equal("/fields/0/solve/electrodes/1/name", error.Path);
        Assert.Contains("both called 'ring-2'", error.Constraint, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two repeats sharing a base name are one mistake, and are reported as one rather than
    /// once for every copy.
    /// </summary>
    [Fact]
    public void TwoRepeatsSharingABaseNameAreOneComplaint()
    {
        var errors = Errors(Plane(Repeated("ring", 5, Upper), Repeated("ring", 5, Lower)));

        var error = Assert.Single(errors);
        Assert.Contains("both called 'ring-0', and 4 more of their names collide", error.Constraint, StringComparison.Ordinal);
    }

    /// <summary>The same rule in a volume, through the same function.</summary>
    [Fact]
    public void TwoBoxesSharingANameAreRefused()
    {
        var errors = Errors(Volume("\"name\": \"plate\",", "\"name\": \"plate\","));

        var error = Assert.Single(errors);
        Assert.Equal("/fields/0/solve3d/electrodes/1/name", error.Path);
        Assert.Contains("both called 'plate'", error.Constraint, StringComparison.Ordinal);
    }

    /// <summary>Unnamed boxes are named apart, as unnamed plates are.</summary>
    [Fact]
    public void UnnamedBoxesAreNamedApart()
    {
        var model = ModelValidator.Validate(ModelJson.Parse(Head + Volume(string.Empty, string.Empty) + Tail));

        Assert.Empty(model.Errors);
        Assert.Equal(["electrode-0", "electrode-1"], model.Model!.Fields[0].Solve3D!.Electrodes.Select(e => e.Name));
    }

    /// <summary>
    /// Two field elements may name the same metal, which is what the Astral does: its two
    /// mirror solves each declare every board, at one mirror's potentials or the other's.
    /// </summary>
    [Fact]
    public void TwoElementsMayShareANameBecauseTheyMayShareTheMetal()
    {
        var one = Plane(Plate("plate", Upper, "plateVolts"), Plate("other", Lower, "-plateVolts"));
        var two = Plane(Plate("plate", Upper, "0"), Plate("other", Lower, "plateVolts"));

        var json = Head + "\"fields\": [" + Element(one) + ", " + Element(two) + "]," + Tail;
        var model = ModelValidator.Validate(ModelJson.Parse(json));

        Assert.Empty(model.Errors);
    }

    private static CompiledSolvedField Compile(string solve, string sequence = "")
    {
        var model = ModelValidator.Validate(ModelJson.Parse(Head + "\"fields\": [" + Element(solve) + "]," + sequence + Tail));

        Assert.True(model.Errors.Count == 0, string.Join("; ", model.Errors.Select(e => $"{e.Path}: {e.Constraint}")));

        return model.Model!.Fields[0].Solve!;
    }

    private static IReadOnlyList<EinzelError> Errors(string solveOrFields) =>
        ModelValidator.Validate(ModelJson.Parse(
            Head + (solveOrFields.TrimStart().StartsWith("\"fields\"", StringComparison.Ordinal)
                ? solveOrFields
                : "\"fields\": [" + Element(solveOrFields) + "],") + Tail)).Errors;

    private static string Element(string solve) => $$"""{ "type": "solved2d", "solve": {{solve}} }""";

    /// <summary>A grounded box holding the given electrodes.</summary>
    private static string Plane(params string[] electrodes) =>
        $$"""
        {
          "minX": { "value": -12, "unit": "mm" }, "maxX": { "value": 12, "unit": "mm" },
          "minY": { "value": -5, "unit": "mm" }, "maxY": { "value": 5, "unit": "mm" },
          "cellSize": { "value": 0.5, "unit": "mm" },
          "electrodes": [ {{string.Join(", ", electrodes)}} ]
        }
        """;

    /// <summary>A band one millimeter thick above the beam.</summary>
    private static readonly (string Min, string Max) Upper = ("2.1", "3.1");

    /// <summary>A band half a millimeter thick between the beam and the lower band.</summary>
    private static readonly (string Min, string Max) Middle = ("-1.6", "-1.1");

    /// <summary>A band one millimeter thick below the beam.</summary>
    private static readonly (string Min, string Max) Lower = ("-3.1", "-2.1");

    /// <summary>A plate across the box, in one of the three bands.</summary>
    private static string Plate(string? name, (string Min, string Max) band, string potential) =>
        $$"""
        { {{(name is null ? string.Empty : $"\"name\": \"{name}\",")}} "shape": "rectangle",
          "minX": { "value": -8, "unit": "mm" }, "maxX": { "value": 8, "unit": "mm" },
          "minY": { "value": {{band.Min}}, "unit": "mm" }, "maxY": { "value": {{band.Max}}, "unit": "mm" },
          "potential": { "expression": "{{potential}}", "unit": "V" } }
        """;

    /// <summary>A row of short plates along x in one band, a millimeter wide on a three-millimeter pitch.</summary>
    private static string Repeated(string? name, int count, (string Min, string Max) band) =>
        $$"""
        { {{(name is null ? string.Empty : $"\"name\": \"{name}\",")}} "shape": "rectangle",
          "repeat": { "count": { "value": {{count}}, "unit": "1" }, "index": "k" },
          "minX": { "expression": "start + pitch * k", "unit": "mm" },
          "maxX": { "expression": "start + pitch * k + width", "unit": "mm" },
          "minY": { "value": {{band.Min}}, "unit": "mm" }, "maxY": { "value": {{band.Max}}, "unit": "mm" },
          "potential": { "value": 1, "unit": "V" } }
        """;

    /// <summary>Two boxes in a grounded volume, each with the given name property or none.</summary>
    private static string Volume(string lower, string upper) =>
        $$"""
          "fields": [{
            "type": "solved3d",
            "solve3d": {
              "minX": { "value": -6, "unit": "mm" }, "maxX": { "value": 6, "unit": "mm" },
              "minY": { "value": -6, "unit": "mm" }, "maxY": { "value": 6, "unit": "mm" },
              "minZ": { "value": -6, "unit": "mm" }, "maxZ": { "value": 6, "unit": "mm" },
              "cellSize": { "value": 1, "unit": "mm" },
              "electrodes": [
                { {{lower}} "shape": "box",
                  "minX": { "value": -4, "unit": "mm" }, "maxX": { "value": 4, "unit": "mm" },
                  "minY": { "value": -4, "unit": "mm" }, "maxY": { "value": -3, "unit": "mm" },
                  "minZ": { "value": -4, "unit": "mm" }, "maxZ": { "value": 4, "unit": "mm" },
                  "potential": { "value": -2, "unit": "V" } },
                { {{upper}} "shape": "box",
                  "minX": { "value": -4, "unit": "mm" }, "maxX": { "value": 4, "unit": "mm" },
                  "minY": { "value": 3, "unit": "mm" }, "maxY": { "value": 4, "unit": "mm" },
                  "minZ": { "value": -4, "unit": "mm" }, "maxZ": { "value": 4, "unit": "mm" },
                  "potential": { "value": 2, "unit": "V" } }
              ]
            }
          }],
        """;
}
