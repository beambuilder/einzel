using System.Text.Json.Nodes;
using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Io.IonLists;
using Einzel.Transport;
using Xunit.Abstractions;

namespace Einzel.Io.Tests;

/// <summary>
/// The ion list as a document: rows, their defaults from the model, the <c>cloud</c> set, and
/// the expanded <c>.ions.csv</c> that reproduces a list to the bit.
/// </summary>
/// <remarks>
/// Every equality here is on the bits of the doubles, not within a tolerance. A row that
/// restates the model's ion has to be that ion, and a <c>cloud</c> set has to be the cloud
/// <c>einzel run</c> draws, because the next step flies them and compares against <c>run</c>.
/// </remarks>
public sealed class IonListTests(ITestOutputHelper output)
{
    /// <summary>A beam model with no drive and a non-zero energy offset.</summary>
    private const string Beam = """
        {
          "schemaVersion": "0.14",
          "name": "beam",
          "ion": { "massToCharge": { "value": 622, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [-1, 0.25, 0.5], "unit": "mm" },
            "direction": { "value": [1, 0.1, 0] },
            "accelerationPotential": { "value": 100, "unit": "V" },
            "energyFraction": 0.03
          },
          "detector": { "planePoint": { "value": [50, 0, 0], "unit": "mm" }, "normal": { "value": [-1, 0, 0] } },
          "transport": { "maximumFlightTime": { "value": 10, "unit": "us" } },
          "fields": [ { "type": "uniform", "field": { "value": [1000, 0, 0], "unit": "V/m" } } ]
        }
        """;

    /// <summary>A model with one drive frequency, 1.1 MHz.</summary>
    private const string Driven = """
        {
          "schemaVersion": "0.14",
          "name": "driven",
          "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 0], "unit": "mm" },
            "direction": { "value": [0, 0, 1] },
            "accelerationPotential": { "value": 2, "unit": "V" }
          },
          "detector": { "planePoint": { "value": [0, 0, 50], "unit": "mm" }, "normal": { "value": [0, 0, -1] } },
          "transport": { "maximumFlightTime": { "value": 10, "unit": "us" } },
          "fields": [
            { "type": "idealQuadrupoleRf",
              "directPotential": { "value": 0, "unit": "V" },
              "driveAmplitude": { "value": 100, "unit": "V" },
              "driveFrequency": { "value": 1.1, "unit": "MHz" },
              "inscribedRadius": { "value": 4, "unit": "mm" } }
          ]
        }
        """;

    private static CompiledModel Compile(string json)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(json));
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Constraint)));
        return validation.Model!;
    }

    private static IonListExpansion Expand(string ions, string model = Beam) =>
        IonList.Expand(IonList.Parse(ions), Compile(model));

    private static IReadOnlyList<IonRow> Rows(string ions, string model = Beam)
    {
        var expansion = Expand(ions, model);
        Assert.True(expansion.IsValid, string.Join("; ", expansion.Errors.Select(e => $"{e.Path}: {e.Constraint}")));
        return expansion.Rows!;
    }

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);

    /// <summary>Asserts two rows equal to the bit, field by field.</summary>
    internal static void Same(IonRow expected, IonRow actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Tag, actual.Tag);

        double[] Of(IonRow row) =>
        [
            row.Species.MassSi, row.Species.ChargeSi,
            row.Launch.Position.X, row.Launch.Position.Y, row.Launch.Position.Z,
            row.Launch.Velocity.X, row.Launch.Velocity.Y, row.Launch.Velocity.Z,
            row.BirthTimeSeconds,
        ];

        Assert.Equal(Of(expected).Select(Bits), Of(actual).Select(Bits));
    }

    /// <summary>The launch <c>run</c> and <c>FlyCloud</c> give the model's own ion.</summary>
    private static IonRow ModelsOwn(CompiledModel model, int id) => new(
        id,
        IonSpecies.FromModel(model),
        new PhaseState(model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi()),
        0.0,
        string.Empty);

    [Fact]
    public void ARowWithOnlyAnIdIsTheModelsOwnIon()
    {
        var rows = Rows("""{ "schemaVersion": "0.1", "ions": [ { "id": 7 } ] }""");

        // Species, position, direction and the energy with its 3 % offset, all from the model.
        Same(ModelsOwn(Compile(Beam), 7), Assert.Single(rows));
    }

    [Fact]
    public void ARowRestatingTheModelsIonAndEnergyIsThatIon()
    {
        var model = Compile(Beam.Replace("\"energyFraction\": 0.03", "\"energyFraction\": 0", StringComparison.Ordinal));

        var rows = Rows("""
            { "schemaVersion": "0.1", "ions": [ {
                "id": 1, "massToCharge": { "value": 622, "unit": "Da" }, "chargeNumber": 1,
                "position": { "value": [-1, 0.25, 0.5], "unit": "mm" }, "direction": { "value": [1, 0.1, 0] },
                "accelerationPotential": { "value": 100, "unit": "V" } } ] }
            """, Beam.Replace("\"energyFraction\": 0.03", "\"energyFraction\": 0", StringComparison.Ordinal));

        Same(ModelsOwn(model, 1), Assert.Single(rows));
    }

    [Fact]
    public void StatedAttributesReplaceTheModelsAndTheEnergyOffsetIsNotApplied()
    {
        var rows = Rows("""
            { "schemaVersion": "0.1", "ions": [
              { "id": 1, "massToCharge": { "value": 250, "unit": "Da" }, "chargeNumber": -2,
                "position": { "value": [1, 2, 3], "unit": "mm" }, "direction": { "value": [0, 3, 4] },
                "kineticEnergy": { "value": 3, "unit": "eV" }, "birthTime": { "value": 2, "unit": "us" },
                "tag": "probe" },
              { "id": 2, "accelerationPotential": { "value": 50, "unit": "V" } },
              { "id": 3, "chargeNumber": 2 } ] }
            """);

        var model = Compile(Beam);
        var mass = Quantity.From(250, "Da").SiValue * 2;

        Assert.Equal(Bits(mass), Bits(rows[0].Species.MassSi));
        Assert.Equal(Bits(Quantity.From(-2, "e").SiValue), Bits(rows[0].Species.ChargeSi));
        Assert.Equal(Bits(Quantity.From(2, "mm").SiValue), Bits(rows[0].Launch.Position.Y));
        Assert.Equal(Bits(new Vec3(0, 3, 4).Normalized().Z * Math.Sqrt(2.0 * Quantity.From(3, "eV").SiValue / mass)), Bits(rows[0].Launch.Velocity.Z));
        Assert.Equal(0.0, rows[0].Launch.Velocity.X);
        Assert.Equal(Bits(Quantity.From(2, "us").SiValue), Bits(rows[0].BirthTimeSeconds));
        Assert.Equal("probe", rows[0].Tag);

        // A stated potential is the whole energy: the model's 3 % offset belongs to its own.
        var speed = Math.Sqrt(2.0 * (model.ChargeSi * 50.0) / model.MassSi);
        Assert.Equal(Bits((model.SourceDirection * speed).Y), Bits(rows[1].Launch.Velocity.Y));
        Assert.Equal(Bits((model.SourceDirection * speed).X), Bits(rows[1].Launch.Velocity.X));
        Assert.Equal(Bits(model.SourcePosition.X), Bits(rows[1].Launch.Position.X));

        // A charge alone keeps the model's mass-to-charge, so the mass doubles.
        Assert.Equal(Bits(Quantity.From(622, "Da").SiValue * 2), Bits(rows[2].Species.MassSi));
    }

    [Fact]
    public void AnRfPhaseIsABirthTimeInCyclesOfTheOneDrive()
    {
        var rows = Rows("""
            { "schemaVersion": "0.1", "ions": [ { "id": 1, "rfPhase": 0.75 }, { "id": 2, "rfPhase": 0 } ] }
            """, Driven);

        var frequency = Compile(Driven).Fields[0].DriveFrequencySi;

        Assert.Equal(1.1e6, frequency);
        Assert.Equal(Bits(0.75 / frequency), Bits(rows[0].BirthTimeSeconds));
        Assert.Equal(0.0, rows[1].BirthTimeSeconds);
    }

    [Fact]
    public void SetIdsContinueAfterEveryIdBeforeThem()
    {
        var rows = Rows("""
            { "schemaVersion": "0.1",
              "ions": [ { "id": 40 }, { "id": 3 } ],
              "ionSets": [ { "cloud": { "ions": 2, "transverseSpread": { "value": 1, "unit": "um" } } },
                           { "cloud": { "seed": 2 }, "n": 3, "tag": "b" },
                           { "cloud": { }, "firstId": 0 } ] }
            """);

        Assert.Equal([40, 3, 41, 42, 43, 44, 45, 0], rows.Select(r => r.Id));
        Assert.Equal(["", "", "", "", "b", "b", "b", ""], rows.Select(r => r.Tag));
    }

    public static TheoryData<string, string, string, string> Refusals => new()
    {
        // fragment of the document after the schema version, model, code, pointer
        { """ "ions": [ { "tag": "x" } ] """, Beam, ErrorCodes.SchemaInvalid, "/ions/0/id" },
        { """ "ions": [ { "id": -1 } ] """, Beam, ErrorCodes.ValueOutOfBounds, "/ions/0/id" },
        { """ "ions": [ { "id": 1 }, { "id": 1 } ] """, Beam, ErrorCodes.SchemaInvalid, "/ions/1/id" },
        { """ "ions": [ { "id": 1, "direction": { "value": [0, 0, 0] } } ] """, Beam, ErrorCodes.ValueOutOfBounds, "/ions/0/direction" },
        { """ "ions": [ { "id": 1, "position": { "value": [0, 0], "unit": "mm" } } ] """, Beam, ErrorCodes.SchemaInvalid, "/ions/0/position" },
        { """ "ions": [ { "id": 1, "kineticEnergy": { "value": 1, "unit": "eV" }, "accelerationPotential": { "value": 1, "unit": "V" } } ] """, Beam, ErrorCodes.SchemaInvalid, "/ions/0/accelerationPotential" },
        { """ "ions": [ { "id": 1, "kineticEnergy": { "value": -1, "unit": "eV" } } ] """, Beam, ErrorCodes.ValueOutOfBounds, "/ions/0/kineticEnergy" },
        { """ "ions": [ { "id": 1, "kineticEnergy": { "value": 1, "unit": "V" } } ] """, Beam, ErrorCodes.UnitsIncompatible, "/ions/0/kineticEnergy" },
        { """ "ions": [ { "id": 1, "chargeNumber": 0 } ] """, Beam, ErrorCodes.ValueOutOfBounds, "/ions/0/chargeNumber" },
        { """ "ions": [ { "id": 1, "massToCharge": { "value": 0, "unit": "Da" } } ] """, Beam, ErrorCodes.ValueOutOfBounds, "/ions/0/massToCharge" },
        { """ "ions": [ { "id": 1, "birthTime": { "value": -1, "unit": "ns" } } ] """, Beam, ErrorCodes.ValueOutOfBounds, "/ions/0/birthTime" },
        { """ "ions": [ { "id": 1, "rfPhase": 0.5 } ] """, Beam, ErrorCodes.SchemaInvalid, "/ions/0/rfPhase" },
        { """ "ions": [ { "id": 1, "rfPhase": 0.5, "birthTime": { "value": 1, "unit": "ns" } } ] """, Driven, ErrorCodes.SchemaInvalid, "/ions/0/rfPhase" },
        { """ "ions": [ { "id": 1, "rfPhase": -0.5 } ] """, Driven, ErrorCodes.ValueOutOfBounds, "/ions/0/rfPhase" },
        { """ "ions": [ { "id": 1, "tag": "a,b" } ] """, Beam, ErrorCodes.SchemaInvalid, "/ions/0/tag" },
        { """ "ions": [ ] """, Beam, ErrorCodes.SchemaInvalid, "/" },
        { """ "ions": [ { "id": 1 } ], "maxParallelism": 0 """, Beam, ErrorCodes.ValueOutOfBounds, "/maxParallelism" },
        { """ "ionSets": [ { "n": 3 } ] """, Beam, ErrorCodes.SchemaInvalid, "/ionSets/0" },
        { """ "ionSets": [ { "cloud": { "ions": 4 }, "n": 3 } ] """, Beam, ErrorCodes.SchemaInvalid, "/ionSets/0/n" },
        { """ "ionSets": [ { "cloud": { }, "n": 0 } ] """, Beam, ErrorCodes.ValueOutOfBounds, "/ionSets/0/n" },
        { """ "ionSets": [ { "cloud": { "population": 5 } } ] """, Beam, ErrorCodes.SchemaInvalid, "/ionSets/0/cloud/population" },
        { """ "ionSets": [ { "cloud": { "divergence": { "value": 95, "unit": "deg" } } } ] """, Beam, ErrorCodes.ValueOutOfBounds, "/ionSets/0/cloud/divergence" },
        { """ "ionSets": [ { "cloud": { "temperature": { "value": 300, "unit": "mm" } } } ] """, Beam, ErrorCodes.UnitsIncompatible, "/ionSets/0/cloud/temperature" },
        { """ "ions": [ { "id": 2 } ], "ionSets": [ { "cloud": { "ions": 3 }, "firstId": 0 } ] """, Beam, ErrorCodes.SchemaInvalid, "/ionSets/0/firstId" },
        { """ "ionSets": [ { "cloud": { }, "firstId": -4 } ] """, Beam, ErrorCodes.ValueOutOfBounds, "/ionSets/0/firstId" },
        { """ "ionSets": [ { "cloud": { }, "tag": "a\"b" } ] """, Beam, ErrorCodes.SchemaInvalid, "/ionSets/0/tag" },
        { """ "ions": [ null ] """, Beam, ErrorCodes.SchemaInvalid, "/ions/0" },
        { """ "ions": [ { "id": 1 } ], "ionSets": [ null ] """, Beam, ErrorCodes.SchemaInvalid, "/ionSets/0" },

        // Beam's maximumFlightTime is 10 us, so its clock ends at 1000 x 10 us = 10 ms.
        { """ "ions": [ { "id": 1, "birthTime": { "value": 11, "unit": "ms" } } ] """, Beam, ErrorCodes.ValueOutOfBounds, "/ions/0/birthTime" },
        { """ "ions": [ { "id": 1, "rfPhase": 12100 } ] """, Driven, ErrorCodes.ValueOutOfBounds, "/ions/0/rfPhase" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void EachMistakeIsRefusedAtItsPointer(string fragment, string model, string code, string at)
    {
        var expansion = Expand("{ \"schemaVersion\": \"0.1\", " + fragment + " }", model);

        Assert.False(expansion.IsValid);
        Assert.Null(expansion.Rows);
        var error = Assert.Single(expansion.Errors);
        output.WriteLine($"{error.Code} {error.Path}: {error.Constraint}");
        Assert.Equal((code, at), (error.Code, error.Path));
    }

    [Theory]
    [InlineData("""{ "ions": [ { "id": 1 } ] }""")]
    [InlineData("""{ "schemaVersion": "0.2", "ions": [ { "id": 1 } ] }""")]
    public void TheSchemaVersionIsRequiredAndChecked(string json)
    {
        var error = Assert.Single(Expand(json).Errors);
        Assert.Equal((ErrorCodes.SchemaInvalid, "/schemaVersion"), (error.Code, error.Path));
    }

    /// <summary>
    /// A mistake in the document's own keys does not hide the rows' mistakes: one pass
    /// lists them all. Only a model the list cannot launch into stops the rows being read.
    /// </summary>
    [Fact]
    public void ADocumentMistakeDoesNotHideTheRowMistakes()
    {
        var expansion = Expand("""
            { "schemaVersion": "0.2", "maxParallelism": 0,
              "ions": [ { "id": -1 }, { "id": 2, "tag": "a,b" }, { "id": 3, "birthTime": { "value": 5, "unit": "ms" } } ] }
            """);

        Assert.Equal(
            ["/schemaVersion", "/maxParallelism", "/ions/0/id", "/ions/1/tag"],
            expansion.Errors.Select(e => e.Path));
    }

    /// <summary>
    /// <c>rfPhase</c> counts every drive frequency the model declares, on every kind of element
    /// that carries one: an ideal quadrupole, a solved 2D or 3D drive, an analytic element a
    /// sequence phase re-compiles, and the same frequency on two elements counted once.
    /// </summary>
    [Theory]
    [MemberData(nameof(DriveCounts))]
    public void AnRfPhaseCountsEveryDeclaredDriveFrequency(string kind, string json, double hertz, int count)
    {
        var model = Compile(json) is var compiled && compiled.Cloud.IsCloud
            ? compiled with { Cloud = new IonCloudSettings() }
            : compiled;
        var expansion = IonList.Expand(
            IonList.Parse("""{ "schemaVersion": "0.1", "ions": [ { "id": 1, "rfPhase": 0.25 } ] }"""), model);

        output.WriteLine($"{kind}: {string.Join("; ", expansion.Errors.Select(e => e.Constraint))}");

        if (count == 1)
        {
            Assert.True(expansion.IsValid, kind);
            Assert.Equal(Bits(0.25 / hertz), Bits(expansion.Rows![0].BirthTimeSeconds));
        }
        else
        {
            var error = Assert.Single(expansion.Errors);
            Assert.Equal((ErrorCodes.SchemaInvalid, "/ions/0/rfPhase"), (error.Code, error.Path));
            Assert.Contains($"the model has {count}", error.Constraint, StringComparison.Ordinal);
        }
    }

    public static TheoryData<string, string, double, int> DriveCounts()
    {
        var examples = Path.Combine(RepositoryRoot(), "src", "Einzel.Commands", "Examples");
        string Example(string name) => File.ReadAllText(Path.Combine(examples, name + ".json"));

        // A drive on the shipped 3D parallel-plate gap; validated, never solved.
        var gap = JsonNode.Parse(Example("parallel-plate-gap-3d"))!;
        var volume = gap["fields"]![0]!["solve3d"]!;
        volume["drive"] = JsonNode.Parse("""{ "frequency": { "value": 2.5, "unit": "MHz" }, "waveform": "sinusoid" }""");
        volume["electrodes"]![0]!["driveAmplitude"] = JsonNode.Parse("""{ "value": 1, "unit": "V" }""");

        // A sequence phase that retunes the ideal quadrupole: compiled once per phase.
        var retuned = JsonNode.Parse(Driven)!;
        retuned["parameters"] = JsonNode.Parse("""{ "f": { "value": 1.1, "unit": "MHz" } }""");
        retuned["fields"]![0]!["driveFrequency"] = JsonNode.Parse("""{ "expression": "f", "unit": "MHz" }""");
        retuned["sequence"] = JsonNode.Parse("""
            [ { "name": "low", "duration": { "value": 2, "unit": "us" } },
              { "name": "high", "duration": { "value": 8, "unit": "us" }, "set": { "f": { "value": 2.2, "unit": "MHz" } } } ]
            """);

        var twice = JsonNode.Parse(Driven)!;
        twice["fields"]!.AsArray().Add(twice["fields"]![0]!.DeepClone());

        return new()
        {
            { "ideal quadrupole", Driven, 1.1e6, 1 },
            { "solved 2D, one drive", Example("quadrupole-rf-stable"), 1e6, 1 },
            { "solved 3D, one drive", gap.ToJsonString(), 2.5e6, 1 },
            { "solved 2D, two drives", Example("linear-ion-trap-held"), 0, 2 },
            { "retuned by a phase", retuned.ToJsonString(), 0, 2 },
            { "one frequency on two elements", twice.ToJsonString(), 1.1e6, 1 },
            { "no drive", Beam, 0, 0 },
        };
    }

    [Theory]
    [InlineData("""{ "schemaVersion": "0.1", "ions": [ { "id": 1, "energy": 2 } ] }""", "/ions/0/energy")]
    [InlineData("""{ "schemaVersion": "0.1", "ions": [ ], "ionSet": [ ] }""", "/ionSet")]
    [InlineData("""{ "schemaVersion": "0.1", "ionSets": [ { "cloud": { "spread": 1 } } ] }""", "/ionSets/0/cloud/spread")]
    public void AnUnknownKeyIsRefusedWithItsPointer(string json, string at)
    {
        var failure = Assert.Throws<EinzelException>(() => IonList.Parse(json));

        output.WriteLine($"{failure.Error.Path}: {failure.Error.Constraint}");
        Assert.Equal((ErrorCodes.SchemaInvalid, at), (failure.Error.Code, failure.Error.Path));
    }

    [Fact]
    public void AModelThatCannotSupplyOneIonOrOneLaunchIsRefused()
    {
        var model = Compile(Beam);
        var list = IonList.Parse("""{ "schemaVersion": "0.1", "ions": [ { "id": 1 } ] }""");

        var clouded = model with { Cloud = new IonCloudSettings { Ions = 4 } };
        var mixture = model with { Species = [Species("a"), Species("b")] };
        var unnamed = model with { Source = model.Source with { Ion = null } };

        // A CSV defaults nothing from the model and is refused into the same models.
        var directory = Directory.CreateTempSubdirectory("einzel-ions-");
        var csv = Path.Combine(directory.FullName, "one.ions.csv");
        File.WriteAllText(csv, IonListCsv.Write(Rows("""{ "schemaVersion": "0.1", "ions": [ { "id": 1 } ] }""")));

        try
        {
            foreach (var refused in new[] { clouded, mixture, unnamed })
            {
                var error = Assert.Single(IonList.Expand(list, refused).Errors);
                output.WriteLine(error.Constraint);
                Assert.Equal((ErrorCodes.SchemaInvalid, "/"), (error.Code, error.Path));

                var read = IonList.Load(csv, refused);
                Assert.Null(read.Rows);
                Assert.Equal((ErrorCodes.SchemaInvalid, "/"), (Assert.Single(read.Errors).Code, read.Errors[0].Path));
            }

            Assert.True(IonList.Load(csv, model).IsValid);
        }
        finally
        {
            directory.Delete(recursive: true);
        }

        static CompiledSpecies Species(string name) =>
            new(name, 1e-25, 1.602176634e-19, new CompiledMobility(1e-4, 0.0, 0.0, false), 10);
    }

    /// <summary>
    /// A <c>cloud</c> set is the cloud <c>einzel run</c> draws for the same block, ion for ion,
    /// on every shipped example that declares one and on one that uses every spread.
    /// </summary>
    [Fact]
    public void ACloudSetDrawsTheCloudRunDraws()
    {
        var examples = Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot(), "src", "Einzel.Commands", "Examples"), "*.json")
            .Where(p => !p.EndsWith(".test.json", StringComparison.Ordinal))
            .Select(p => (Path.GetFileName(p), File.ReadAllText(p)))
            .Where(e => JsonNode.Parse(e.Item2)!["source"]?["cloud"] is not null)
            .Append(("every-spread", Beam.Replace(
                "\"energyFraction\": 0.03",
                """
                "energyFraction": 0.03,
                "cloud": { "ions": 500, "seed": 77, "temperature": { "value": 500, "unit": "K" },
                  "transverseSpread": { "value": 0.1, "unit": "mm" }, "longitudinalSpread": { "value": 20, "unit": "um" },
                  "energyFractionSpread": 0.02, "divergence": { "value": 3, "unit": "deg" } }
                """,
                StringComparison.Ordinal)))
            .ToList();

        var compared = 0;

        foreach (var (name, json) in examples)
        {
            var withCloud = Compile(json);
            var node = JsonNode.Parse(json)!;
            var cloud = node["source"]!["cloud"]!.DeepClone().AsObject();

            // A population sizes a packet for space charge and is refused in a list.
            cloud.Remove("population");

            // The same model with its cloud taken away, which is what an ion list launches into.
            var withoutCloud = withCloud with { Cloud = new IonCloudSettings() };
            var list = IonList.Parse(
                new JsonObject
                {
                    ["schemaVersion"] = "0.1",
                    ["ionSets"] = new JsonArray(new JsonObject { ["cloud"] = cloud, ["tag"] = "c" }),
                }.ToJsonString());

            var expansion = IonList.Expand(list, withoutCloud);
            Assert.True(expansion.IsValid, name + ": " + string.Join("; ", expansion.Errors.Select(e => e.Constraint)));

            // FiguresOfMerit.FlyCloud, verbatim: LaunchAt(model, 0), FromModel, the declared direction.
            var nominal = new PhaseState(
                withCloud.SourcePosition,
                withCloud.SourceDirection * (withCloud.LaunchSpeedSi() * Math.Sqrt(1.0 + 0.0)));
            var species = IonSpecies.FromModel(withCloud);
            var drawn = IonCloud.Draw(in nominal, species, withCloud.Cloud, withCloud.SourceDirection);

            Assert.Equal(drawn.Length, expansion.Rows!.Count);
            Assert.Equal([(long)withCloud.Cloud.Seed], expansion.Seeds);

            for (var k = 0; k < drawn.Length; k++)
            {
                Same(new IonRow(k + 1, species, drawn[k], 0.0, "c"), expansion.Rows[k]);
            }

            output.WriteLine($"{name,-36} {drawn.Length,5} ions, seed {withCloud.Cloud.Seed}: identical");
            compared++;
        }

        Assert.True(compared >= 15, $"only {compared} clouds were compared");
    }

    [Fact]
    public void NOnACloudSetTakesThePrefixOfTheLargerDraw()
    {
        const string Cloud = """ "cloud": { "seed": 9, "temperature": { "value": 300, "unit": "K" } } """;

        var small = Rows("{ \"schemaVersion\": \"0.1\", \"ionSets\": [ { " + Cloud + ", \"n\": 5 } ] }");
        var large = Rows("{ \"schemaVersion\": \"0.1\", \"ionSets\": [ { " + Cloud + ", \"n\": 50 } ] }");

        for (var k = 0; k < small.Count; k++)
        {
            Same(large[k], small[k]);
        }
    }

    private const string Mixed = """
        { "schemaVersion": "0.1", "maxParallelism": 3,
          "ions": [
            { "id": 11, "rfPhase": 0.125, "tag": "phase1" },
            { "id": 12, "massToCharge": { "value": 1234.5678, "unit": "Da" }, "chargeNumber": -3,
              "position": { "value": [0.1, -0.3, 1e-7], "unit": "mm" }, "direction": { "value": [1, 1, 1] },
              "kineticEnergy": { "value": 0.333, "unit": "eV" }, "birthTime": { "value": 1.7, "unit": "us" } },
            { "id": 13, "kineticEnergy": { "value": 0, "unit": "eV" } } ],
          "ionSets": [ { "cloud": { "ions": 40, "seed": 3, "temperature": { "value": 300, "unit": "K" },
                                    "transverseSpread": { "value": 20, "unit": "um" }, "energyFractionSpread": 0.01 } } ] }
        """;

    [Fact]
    public void TheCsvReadsBackToTheSameRowsToTheBit()
    {
        var rows = Rows(Mixed, Driven);
        var text = IonListCsv.Write(rows);

        output.WriteLine(string.Join('\n', text.Split('\n').Take(4)));

        var read = IonListCsv.Read(text);
        Assert.True(read.IsValid, string.Join("; ", read.Errors.Select(e => $"{e.Path}: {e.Constraint}")));
        Assert.Equal(rows.Count, read.Rows!.Count);

        for (var k = 0; k < rows.Count; k++)
        {
            Same(rows[k], read.Rows[k]);
        }

        Assert.Equal(text, IonListCsv.Write(read.Rows));
        Assert.StartsWith(IonListCsv.Header + "\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
    }

    [Fact]
    public void ACsvIsAnIonListInputAndJsonIsTheOther()
    {
        var directory = Directory.CreateTempSubdirectory("einzel-ions-");

        try
        {
            var model = Compile(Driven);
            var json = Path.Combine(directory.FullName, "set.ions.json");
            var csv = Path.Combine(directory.FullName, "set.ions.csv");

            File.WriteAllText(json, Mixed);

            var fromJson = IonList.Load(json, model);
            Assert.True(fromJson.IsValid);
            Assert.Equal(3, fromJson.MaxParallelism);
            Assert.Equal([3L], fromJson.Seeds);

            File.WriteAllText(csv, IonListCsv.Write(fromJson.Rows!));

            var fromCsv = IonList.Load(csv, model);
            Assert.True(fromCsv.IsValid);
            Assert.Equal(fromJson.Rows!.Count, fromCsv.Rows!.Count);

            for (var k = 0; k < fromCsv.Rows.Count; k++)
            {
                Same(fromJson.Rows[k], fromCsv.Rows[k]);
            }

            File.WriteAllText(json, """{ "schemaVersion": "0.1", "ions": [ { "id": 1, "speed": 1 } ] }""");
            Assert.Equal("/ions/0/speed", Assert.Single(IonList.Load(json, model).Errors).Path);

            // A CSV row born beyond the model's clock (10 us x 1000) is refused at its row and column.
            File.WriteAllText(csv, IonListCsv.Header + "\n1,,1e-25,1.6e-19,0,0,0,0,0,0,0.005\n2,,1e-25,1.6e-19,0,0,0,0,0,0,0.02\n");
            var late = Assert.Single(IonList.Load(csv, model).Errors);
            Assert.Equal((ErrorCodes.ValueOutOfBounds, "/1/birth_time_s"), (late.Code, late.Path));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    public static TheoryData<string, string> CsvRefusals => new()
    {
        { "id,tag,mass\n1,,1,1,0,0,0,0,0,0,0\n", "/" },
        { IonListCsv.Header + "\n", "/" },
        { IonListCsv.Header + "\n1,,1e-25,1.6e-19,0,0,0,0,0,0\n", "/0" },
        { IonListCsv.Header + "\n-1,,1e-25,1.6e-19,0,0,0,0,0,0,0\n", "/0/id" },
        { IonListCsv.Header + "\n1,,1e-25,1.6e-19,0,0,0,0,0,0,0\n1,,1e-25,1.6e-19,0,0,0,0,0,0,0\n", "/1/id" },
        { IonListCsv.Header + "\n1,,0,1.6e-19,0,0,0,0,0,0,0\n", "/0/mass_kg" },
        { IonListCsv.Header + "\n1,,1e-25,0,0,0,0,0,0,0,0\n", "/0/charge_C" },
        { IonListCsv.Header + "\n1,,1e-25,1.6e-19,NaN,0,0,0,0,0,0\n", "/0/x_m" },
        { IonListCsv.Header + "\n1,,1e-25,1.6e-19,0,0,0,0,Infinity,0,0\n", "/0/vy_m_per_s" },
        { IonListCsv.Header + "\n1,,1e-25,1.6e-19,0,0,0,0,0,0,-1e-9\n", "/0/birth_time_s" },
        { IonListCsv.Header + "\n1,\"q\",1e-25,1.6e-19,0,0,0,0,0,0,0\n", "/0/tag" },
    };

    [Theory]
    [MemberData(nameof(CsvRefusals))]
    public void EachCsvMistakeIsRefusedAtItsRowAndColumn(string csv, string at)
    {
        var read = IonListCsv.Read(csv);

        Assert.Null(read.Rows);
        var error = Assert.Single(read.Errors);
        output.WriteLine($"{error.Path}: {error.Constraint}");
        Assert.Equal(at, error.Path);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SPEC.md")))
            {
                return directory.FullName;
            }
        }

        throw new FileNotFoundException("SPEC.md is not above the test binary");
    }
}
