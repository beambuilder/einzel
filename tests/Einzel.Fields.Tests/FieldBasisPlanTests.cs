using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields.Solved;
using Einzel.Io;

namespace Einzel.Fields.Tests;

/// <summary>
/// A field basis plan: a DC group leaves the one constant supply and becomes a unit channel
/// weighted by its potential, so fields that differ only in that potential share their solves;
/// the field it builds is the direct field to the solver's convergence; a static element under
/// a plan stays a static field.
/// </summary>
/// <remarks>
/// Every comparison is between two builds made here: the field under a plan against the one
/// the default decomposition builds for the same model.
/// </remarks>
public sealed class FieldBasisPlanTests
{
    /// <summary>Two plates, the upper at <c>U</c> and the lower at -1 V, driven at <c>V</c> and <c>-V</c> unless static.</summary>
    private static string Plates(bool driven) => $$"""
        {
          "schemaVersion": "0.14",
          "name": "plates",
          "parameters": {
            "U": { "value": 2, "unit": "V" },
            "V": { "value": 5, "unit": "V" },
            "L": { "value": -1, "unit": "V" }
          },
          "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0.5, 0], "unit": "mm" },
            "direction": { "value": [0, 0, 1] },
            "accelerationPotential": { "value": 200, "unit": "V" }
          },
          "fields": [{
            "type": "solved2d",
            "solve": {
              {{(driven ? "\"drive\": { \"frequency\": { \"value\": 1, \"unit\": \"MHz\" }, \"waveform\": \"sinusoid\" }," : string.Empty)}}
              "minX": { "value": -6, "unit": "mm" }, "maxX": { "value": 6, "unit": "mm" },
              "minY": { "value": -5, "unit": "mm" }, "maxY": { "value": 5, "unit": "mm" },
              "cellSize": { "value": 0.25, "unit": "mm" },
              "tolerance": 1e-10,
              "electrodes": [
                { "name": "upper", "shape": "rectangle",
                  "minX": { "value": -4, "unit": "mm" }, "maxX": { "value": 4, "unit": "mm" },
                  "minY": { "value": 2.1, "unit": "mm" }, "maxY": { "value": 3.1, "unit": "mm" },
                  "potential": { "expression": "U", "unit": "V" }{{(driven ? ", \"driveAmplitude\": { \"expression\": \"V\", \"unit\": \"V\" }" : string.Empty)}} },
                { "name": "lower", "shape": "rectangle",
                  "minX": { "value": -4, "unit": "mm" }, "maxX": { "value": 4, "unit": "mm" },
                  "minY": { "value": -3.1, "unit": "mm" }, "maxY": { "value": -2.1, "unit": "mm" },
                  "potential": { "expression": "L", "unit": "V" }{{(driven ? ", \"driveAmplitude\": { \"expression\": \"-V\", \"unit\": \"V\" }" : string.Empty)}} }
              ]
            }
          }],
          "detector": { "planePoint": { "value": [0, 0, 20], "unit": "mm" }, "normal": { "value": [0, 0, -1] } },
          "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 40, "unit": "us" } }
        }
        """;

    private static readonly FieldBasisPlan Upper = new(new Dictionary<int, IReadOnlyList<IReadOnlyList<string>>> { [0] = [["upper"]] });

    private static CompiledModel Model(bool driven, double u = 2.0, double lower = -1.0) =>
        ModelValidator.Validate(
            ModelJson.Parse(Plates(driven)),
            new Dictionary<string, Quantity>(StringComparer.Ordinal)
            {
                ["U"] = Quantity.From(u, "V"),
                ["L"] = Quantity.From(lower, "V"),
            }).Model!;

    /// <summary>Points between and beside the plates, where the field is worth comparing.</summary>
    private static IEnumerable<Vec3> Probes() =>
        from x in new[] { -3.5e-3, -1e-3, 0.0, 2e-3, 5e-3 }
        from y in new[] { -1.9e-3, -0.5e-3, 0.0, 1e-3, 1.9e-3, 4e-3 }
        select new Vec3(x, y, 0.0);

    /// <summary>
    /// Under a plan a DC group is a channel of its own at one volt, so two models that differ
    /// only in the group's potential have the same keys, where by default each is a DC pattern
    /// and a solve of its own.
    /// </summary>
    /// <remarks>
    /// Mutations: a group's DC fed into the constant supply, and the group's supply not told
    /// apart from the constant one, each fail here.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnderAPlanADcGroupIsAUnitChannelAndTwoPotentialsOfItShareTheirKeys(bool driven)
    {
        var two = Model(driven, 2.0);
        var five = Model(driven, 5.0);

        Assert.NotEqual(SolveHash.Of(two)[0].ChannelHashes, SolveHash.Of(five)[0].ChannelHashes);
        Assert.Equal(SolveHash.Of(two, Upper)[0].ChannelHashes, SolveHash.Of(five, Upper)[0].ChannelHashes);

        // The group alone at one volt is one of the channels; the lower plate is the other DC one.
        var holdings = GeometryBuilder.Holdings(two.Fields[0].Solve!, Upper.GroupsOf(0));
        Assert.Contains(holdings, holding => holding.SequenceEqual([1.0, 0.0]));
        Assert.Contains(holdings, holding => holding[0] == 0.0 && holding[1] != 0.0);
        Assert.Equal(driven ? 3 : 2, holdings.Count);
    }

    /// <summary>
    /// The field a plan builds is the direct field to the solver's convergence, driven and
    /// static, and a static one stays a static field: time-free, as its one channel was.
    /// </summary>
    /// <remarks>
    /// Mutations: a static group weighted one instead of its potential, the grouped electrodes
    /// left in the fixed channel too, and a weighted field that drops a weight, each fail here.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFieldUnderAPlanIsTheDirectFieldToTheSolversConvergence(bool driven)
    {
        var model = Model(driven, 7.0);
        var direct = FieldAssembly.Build(model);
        var planned = FieldAssembly.Build(model, null, Upper);

        if (driven)
        {
            var a = Assert.IsType<DrivenSolvedField>(direct);
            var b = Assert.IsType<DrivenSolvedField>(planned);
            Assert.Equal(3, b.ChannelCount);

            foreach (var t in new[] { 0.0, 0.31e-6, 0.77e-6 })
            {
                foreach (var probe in Probes())
                {
                    Assert.Equal(a.PotentialAt(probe, t), b.PotentialAt(probe, t), 1e-6);
                }
            }

            return;
        }

        var weighted = Assert.IsType<WeightedSolvedField>(planned);
        Assert.False(planned is ITimeVaryingField);
        Assert.Equal([1.0, 7.0], weighted.Weights);

        foreach (var probe in Probes())
        {
            Assert.Equal(direct.PotentialAt(probe), planned.PotentialAt(probe), 1e-6);
            Assert.True((direct.ElectricFieldAt(probe) - planned.ElectricFieldAt(probe)).Length < 1e-3);
        }

        Assert.Equal(direct.ResolutionLength, planned.ResolutionLength);
        Assert.Equal(
            ((IConductorBounded)direct).ConductorAt(new Vec3(0.0, 2.6e-3, 0.0)),
            ((IConductorBounded)planned).ConductorAt(new Vec3(0.0, 2.6e-3, 0.0)));
    }

    /// <summary>
    /// A static group whose electrodes do not hold one potential cannot be a unit channel and
    /// stays in the fixed one, so the element is the default single channel; and with nothing
    /// outside the groups holding a potential there is no fixed channel to solve.
    /// </summary>
    /// <remarks>Mutations: a mixed group made a unit channel, and an all-zero fixed channel solved anyway, each fail here.</remarks>
    [Fact]
    public void AStaticPlanSolvesOnlyChannelsThatCarrySomething()
    {
        var both = new FieldBasisPlan(new Dictionary<int, IReadOnlyList<IReadOnlyList<string>>> { [0] = [["upper", "lower"]] });
        var mixed = Model(driven: false).Fields[0].Solve!;
        Assert.Equal(GeometryBuilder.Holdings(mixed), GeometryBuilder.Holdings(mixed, both.GroupsOf(0)));

        var grounded = Model(driven: false, 3.0, 0.0).Fields[0].Solve!;
        Assert.Equal([[1.0, 0.0]], GeometryBuilder.Holdings(grounded, Upper.GroupsOf(0)));
    }

    /// <summary>
    /// A sequence keeps the default decomposition under a plan: each stage is weighed against
    /// its own patterns, and a plan is about one operating point's DC.
    /// </summary>
    /// <remarks>Mutation: the plan applied to a sequenced element (its stages then find no channel and the build throws) fails here.</remarks>
    [Fact]
    public void ASequenceKeepsTheDefaultDecomposition()
    {
        static CompiledElectrode Plate(string name, double minY, double potential) => new()
        {
            Name = name, Shape = ElectrodeShape.Rectangle,
            MinX = -4e-3, MinY = minY, MaxX = 4e-3, MaxY = minY + 1e-3, Potential = potential,
        };

        var first = new CompiledStage("first", 10e-6, [Plate("plate", 2e-3, 60.0), Plate("other", -3e-3, 20.0)]);
        var second = new CompiledStage("second", 10e-6, [Plate("plate", 2e-3, 0.0), Plate("other", -3e-3, 20.0)]);
        var sequence = new CompiledSolvedField
        {
            MinX = -10e-3, MinY = -10e-3, MaxX = 10e-3, MaxY = 10e-3, CellSize = 0.5e-3, Tolerance = 1e-10,
            Electrodes = first.Electrodes,
            Stages = [first, second],
        };
        IReadOnlyList<IReadOnlyList<string>> plate = [["plate"]];

        Assert.Equal(GeometryBuilder.Holdings(sequence), GeometryBuilder.Holdings(sequence, plate));
        Assert.IsType<DrivenSolvedField>(GeometryBuilder.Build(sequence, null, plate).Field);
    }

    /// <summary>
    /// A static volume under a plan is a weighted sum of a fixed channel and a unit channel per
    /// group, and its potential is the direct one's to the solver's convergence.
    /// </summary>
    /// <remarks>Mutation: the volume builder ignoring the plan, or weighting a group by one, fails here.</remarks>
    [Fact]
    public void AStaticVolumeUnderAPlanIsTheDirectFieldToTheSolversConvergence()
    {
        var geometry = Volume();
        IReadOnlyList<IReadOnlyList<string>> slab = [["slab"]];

        Assert.Equal([[0.0, -20.0], [1.0, 0.0]], GeometryBuilder3D.Holdings(geometry, slab));

        var (direct, _) = GeometryBuilder3D.BuildField(geometry);
        var (planned, _) = GeometryBuilder3D.BuildField(geometry, null, slab);

        Assert.Equal([1.0, 100.0], Assert.IsType<WeightedSolvedField>(planned).Weights);

        foreach (var z in new[] { -0.0015, 0.0, 0.0005, 0.002 })
        {
            var probe = new Vec3(0.0005, -0.0003, z);
            Assert.Equal(direct.PotentialAt(probe), planned.PotentialAt(probe), 1e-5);
        }
    }

    /// <summary>A small static volume: a slab at 100 V above a tube at -20 V.</summary>
    private static Geometry3D Volume()
    {
        CompiledElectrode3D[] electrodes =
        [
            new()
            {
                Name = "slab", Shape = Electrode3DShape.Box,
                MinX = -0.002, MinY = -0.002, MinZ = 0.001, MaxX = 0.002, MaxY = 0.002, MaxZ = 0.0015,
                Potential = 100.0,
            },
            new()
            {
                Name = "tube", Shape = Electrode3DShape.Cylinder,
                CentreX = 0.0, CentreY = 0.0, CentreZ = 0.0, Radius = 0.001, Axis = CylinderAxis.Z,
                Lower = -0.003, Upper = -0.002, Potential = -20.0,
            },
        ];
        return new Geometry3D(-0.004, -0.004, -0.004, 0.004, 0.004, 0.004, 0.0005, electrodes, 1e-10);
    }

    /// <summary>
    /// A model with a volume element is assembled and keyed under the plan as the volume
    /// builder builds it: the box at one volt beside the guard at its potential.
    /// </summary>
    /// <remarks>Mutations: the assembly or the keys ignoring the plan for a volume each fail here.</remarks>
    [Fact]
    public void AVolumeModelIsAssembledAndKeyedUnderThePlan()
    {
        var model = ModelValidator.Validate(ModelJson.Parse("""
            {
              "schemaVersion": "0.14",
              "name": "two boxes",
              "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
              "source": {
                "position": { "value": [0, 0, -2], "unit": "mm" },
                "direction": { "value": [0, 0, 1] },
                "accelerationPotential": { "value": 100, "unit": "V" }
              },
              "fields": [
                { "type": "solved3d", "solve3d": {
                  "minX": { "value": -3, "unit": "mm" }, "maxX": { "value": 3, "unit": "mm" },
                  "minY": { "value": -3, "unit": "mm" }, "maxY": { "value": 3, "unit": "mm" },
                  "minZ": { "value": -3, "unit": "mm" }, "maxZ": { "value": 3, "unit": "mm" },
                  "cellSize": { "value": 0.5, "unit": "mm" },
                  "electrodes": [
                    { "name": "box", "shape": "box",
                      "minX": { "value": 1, "unit": "mm" }, "maxX": { "value": 2, "unit": "mm" },
                      "minY": { "value": -1, "unit": "mm" }, "maxY": { "value": 1, "unit": "mm" },
                      "minZ": { "value": -1, "unit": "mm" }, "maxZ": { "value": 1, "unit": "mm" },
                      "potential": { "value": 10, "unit": "V" } },
                    { "name": "guard", "shape": "box",
                      "minX": { "value": -2, "unit": "mm" }, "maxX": { "value": -1, "unit": "mm" },
                      "minY": { "value": -1, "unit": "mm" }, "maxY": { "value": 1, "unit": "mm" },
                      "minZ": { "value": -1, "unit": "mm" }, "maxZ": { "value": 1, "unit": "mm" },
                      "potential": { "value": 5, "unit": "V" } }
                  ] } }
              ],
              "detector": { "planePoint": { "value": [0, 0, 2], "unit": "mm" }, "normal": { "value": [0, 0, -1] } },
              "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 20, "unit": "us" } }
            }
            """)).Model!;
        var plan = new FieldBasisPlan(new Dictionary<int, IReadOnlyList<IReadOnlyList<string>>> { [0] = [["box"]] });

        var weighted = Assert.IsType<WeightedSolvedField>(FieldAssembly.BuildReported(model, null, plan).Field);
        Assert.Equal([1.0, 10.0], weighted.Weights);

        var geometry = SolveHash.Of(model)[0].GeometryHash;
        Assert.Equal(
            [SolveHash.Channel(geometry, [0.0, 5.0]), SolveHash.Channel(geometry, [1.0, 0.0])],
            SolveHash.Of(model, plan)[0].ChannelHashes);
    }

    /// <summary>
    /// A plan's groups are disjoint and name only what the element declares: an electrode named
    /// twice, an empty group or a negative element is refused when the plan is made, and an
    /// electrode or an element the model does not have when it is used - by the keys and by the
    /// build, in the plane and in the volume - before anything is solved.
    /// </summary>
    /// <remarks>
    /// Without the first refusal a static element counted an electrode named in two groups once
    /// per group, 1.9 V off on these plates, and the driven one kept the last group; an unknown
    /// name was ignored. Mutations: each refusal left out (the constructor's three, the names in
    /// the plane's keys and build and the volume's keys and build, the element in the keys and
    /// the assembly) fails here.
    /// </remarks>
    [Fact]
    public void APlansGroupsAreDisjointAndNameOnlyWhatTheElementDeclares()
    {
        static FieldBasisPlan Plan(int element, params IReadOnlyList<string>[] groups) =>
            new(new Dictionary<int, IReadOnlyList<IReadOnlyList<string>>> { [element] = groups });

        Assert.Contains("'upper'", Assert.Throws<ArgumentException>(() => Plan(0, ["upper"], ["upper"])).Message, StringComparison.Ordinal);
        Assert.Contains("'upper'", Assert.Throws<ArgumentException>(() => Plan(0, ["upper", "lower", "upper"])).Message, StringComparison.Ordinal);
        Assert.Contains("empty group", Assert.Throws<ArgumentException>(() => Plan(0, ["upper"], [])).Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => Plan(-1, ["upper"]));

        var ghost = Plan(0, ["upper"], ["nosuch"]);
        var plates = Model(driven: false);
        Assert.Contains("'nosuch'", Assert.Throws<ArgumentException>(() => SolveHash.Of(plates, ghost)).Message, StringComparison.Ordinal);
        Assert.Contains("'nosuch'", Assert.Throws<ArgumentException>(() => FieldAssembly.BuildReported(plates, null, ghost)).Message, StringComparison.Ordinal);

        IReadOnlyList<IReadOnlyList<string>> stray = [["slab"], ["nosuch"]];
        Assert.Throws<ArgumentException>(() => GeometryBuilder3D.Holdings(Volume(), stray));
        Assert.Throws<ArgumentException>(() => GeometryBuilder3D.BuildField(Volume(), null, stray));

        var elsewhere = Plan(1, ["upper"]);
        Assert.Contains("field element 1", Assert.Throws<ArgumentException>(() => SolveHash.Of(plates, elsewhere)).Message, StringComparison.Ordinal);
        Assert.Contains("field element 1", Assert.Throws<ArgumentException>(() => FieldAssembly.BuildReported(plates, null, elsewhere)).Message, StringComparison.Ordinal);
    }
}
