using System.Text.Json.Nodes;
using Einzel.Commands;
using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Magnetic;
using Einzel.Io;

namespace Einzel.Cli.Tests;

/// <summary>
/// The <c>uniformMagnetic</c> element as a document: its units, what it may and may not be
/// combined with, and that every path which cannot apply v x B refuses a model declaring one
/// by name rather than flying it as though the magnet were absent.
/// </summary>
/// <remarks>
/// The closed-form physics is pinned in <c>Einzel.Transport.Tests.MagneticFieldTests</c> and
/// end to end by the three corpus examples; these pin the format and the refusals.
/// </remarks>
public sealed class MagneticModelTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-magnetic", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static JsonObject Example(string name) => JsonNode.Parse(ExampleModels.Read(name))!.AsObject();

    private static ModelValidation Validate(JsonObject document) =>
        ModelValidator.Validate(ModelJson.Parse(document.ToJsonString()));

    private static JsonObject Magnet(string unit, double z) => JsonNode.Parse(
        $$"""{ "type": "uniformMagnetic", "fluxDensity": { "value": [0, 0, {{z}}], "unit": "{{unit}}" } }""")!
        .AsObject();

    private static void AssertRefusedAt(ModelValidation validation, string path, string contains)
    {
        Assert.False(validation.IsValid);
        Assert.Contains(
            validation.Errors,
            e => e.Path == path && e.Constraint.Contains(contains, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("T", 0.5)]
    [InlineData("mT", 500.0)]
    [InlineData("G", 5000.0)]
    [InlineData("kG", 5.0)]
    public void AFluxDensityReadsInAnyUnitOfFluxDensity(string unit, double value)
    {
        var document = Example("cyclotron");
        document["fields"] = new JsonArray(Magnet(unit, value));

        var validation = Validate(document);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Constraint)));
        Assert.Equal(CompiledFieldKind.UniformMagnetic, validation.Model!.Fields[0].Kind);
        Assert.Equal(0.5, validation.Model!.Fields[0].FluxDensity.Z, 1e-15);
    }

    [Fact]
    public void AFluxDensityInVoltsPerMetreIsRefused()
    {
        var document = Example("cyclotron");
        document["fields"] = new JsonArray(Magnet("V/m", 0.5));

        var validation = Validate(document);

        Assert.False(validation.IsValid);
        Assert.Contains(
            validation.Errors,
            e => e.Path == "/fields/0/fluxDensity" && e.Code == ErrorCodes.UnitsIncompatible);
    }

    [Fact]
    public void AMagneticRegionMayNotCarryAFringe()
    {
        var document = Example("magnetic-sector-180");
        document["fields"]![0]!["region"]!["fringe"] = JsonNode.Parse("""{ "value": 2, "unit": "mm" }""");

        AssertRefusedAt(Validate(document), "/fields/0/region/fringe", "hard edge");
    }

    [Fact]
    public void AMagnetTurnsAnIonRoundButCannotStartOneMoving()
    {
        // Launched away from the detector: accepted with B, refused with B = 0, because a
        // magnet turns an ion and nothing else in the document can.
        var sector = Example("magnetic-sector-180");
        Assert.True(Validate(sector).IsValid);

        sector["fields"]![0]!["fluxDensity"]!["value"] = new JsonArray(0.0, 0.0, 0.0);
        AssertRefusedAt(Validate(sector), "/source/direction", "no field element can turn it around");

        // And at rest in a pure magnet an ion stays at rest: B does no work.
        var resting = Example("cyclotron");
        resting["source"]!["accelerationPotential"]!["value"] = 0.0;
        Assert.False(Validate(resting).IsValid);
    }

    [Fact]
    public void TheDiffusiveModeRefusesAMagnet()
    {
        var document = Example("drift-tube-diffusion");
        document["schemaVersion"] = "0.15";
        document["fields"]!.AsArray().Add(Magnet("T", 0.1));

        AssertRefusedAt(Validate(document), "/fields/1", "diffusive mode");
    }

    [Fact]
    public void ADiffusivePhaseInASequenceRefusesAMagnet()
    {
        var document = Example("cyclotron");
        document["sequence"] = JsonNode.Parse(
            """
            [
              { "name": "thermalise", "duration": { "value": 1, "unit": "us" }, "mode": "diffusion" },
              { "name": "fly", "duration": { "value": 49, "unit": "us" }, "mode": "trajectory" }
            ]
            """);

        AssertRefusedAt(Validate(document), "/fields/0", "diffusive mode");
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("pic")]
    public void SpaceChargeRefusesAMagnet(string method)
    {
        var document = Example("cyclotron");
        document["transport"]!["spaceCharge"] = method;

        AssertRefusedAt(Validate(document), "/fields/0", "space-charge integrator");
    }

    [Fact]
    public void APhaseThatChangesBIsRefused()
    {
        // There is no time-varying B, so a phase that moves one would have to be frozen -
        // the silent half-instrument the model-level timeline exists to prevent.
        var document = Example("cyclotron");
        document["parameters"] = JsonNode.Parse(
            """{ "b": { "value": 0.5, "unit": "T", "description": "the flux density" } }""");
        document["fields"]![0]!["fluxDensity"] = JsonNode.Parse("""{ "expression": ["0", "0", "b"] }""");
        document["sequence"] = JsonNode.Parse(
            """
            [
              { "name": "low", "duration": { "value": 1, "unit": "us" } },
              { "name": "high", "duration": { "value": 49, "unit": "us" },
                "set": { "b": { "value": 0.7, "unit": "T" } } }
            ]
            """);

        AssertRefusedAt(Validate(document), "/fields/0/fluxDensity", "static in this build");

        // The control: the same timeline leaving B alone is fine, and the expression form
        // of B reads.
        document["sequence"]![1]!["set"] = new JsonObject();
        Assert.True(Validate(document).IsValid, string.Join("; ", Validate(document).Errors.Select(e => e.Constraint)));
    }

    [Fact]
    public void EinzelCompareRefusesAMagnetByName()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "cyclotron.json");
        File.WriteAllText(path, ExampleModels.Read("cyclotron"));

        var refused = Assert.Throws<EinzelException>(() => ModeComparison.Execute(path, 4));

        Assert.Equal("/fields/0", refused.Error.Path);
        Assert.Contains("cannot apply a magnetic field", refused.Error.Constraint, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyAModelWithAMagnetIsHandedAMagnetizedField()
    {
        // Every model without a magnet gets exactly the field it always did; that is what
        // keeps every published number bit-identical.
        foreach (var name in new[] { "accelerating-gap", "bounded-accelerating-gap", "free-flight" })
        {
            var plain = FieldAssembly.Build(Validate(Example(name)).Model!);
            Assert.False(plain is IMagneticField, $"{name} was handed a magnetic field");
        }

        var wien = FieldAssembly.Build(Validate(Example("wien-filter")).Model!);
        var magnetic = Assert.IsAssignableFrom<IMagneticField>(wien);
        Assert.Equal(0.5, magnetic.MagneticFieldAt(Core.Geometry.Vec3.Zero).Z, 1e-15);
        Assert.Equal(50000.0, wien.ElectricFieldAt(Core.Geometry.Vec3.Zero).Y, 1e-9);
    }

    [Fact]
    public void ASequencedElectricFieldKeepsItsClockBesideAMagnet()
    {
        // A static magnet beside a switched electric field: the combined field must still
        // answer the time-varying interface, or the integrator would sample the switched
        // field at t = 0 for the whole flight.
        var document = Example("sequenced-uniform");
        document["schemaVersion"] = "0.15";
        document["fields"]!.AsArray().Add(Magnet("mT", 1.0));

        var validation = Validate(document);
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        var field = FieldAssembly.Build(validation.Model!);

        var driven = Assert.IsAssignableFrom<ITimeVaryingField>(field);
        Assert.IsAssignableFrom<IMagneticField>(field);
        Assert.Equal(2e-6, driven.NextSwitchAfter(0.0), 1e-18);
        Assert.Equal(0.0, driven.ElectricFieldAt(Core.Geometry.Vec3.Zero, 1e-6).X);
        Assert.Equal(20000.0, driven.ElectricFieldAt(Core.Geometry.Vec3.Zero, 3e-6).X, 1e-9);
    }

    [Fact]
    public void TwoMagneticElementsAdd()
    {
        var document = Example("cyclotron");
        document["fields"] = new JsonArray(Magnet("T", 0.2), Magnet("mT", 300.0));

        var field = (IMagneticField)FieldAssembly.Build(Validate(document).Model!);

        Assert.Equal(0.5, field.MagneticFieldAt(Core.Geometry.Vec3.Zero).Z, 1e-15);
        Assert.Equal(0.5, field.MaximumFluxDensity, 1e-15);
    }

    [Fact]
    public void TheSchemaCarriesTheElement()
    {
        var schema = ModelSchemaWriter.Write();

        Assert.Contains("fluxDensity", schema, StringComparison.Ordinal);
        Assert.Contains("uniformMagnetic", schema, StringComparison.Ordinal);
        Assert.Contains("\"0.15\"", schema, StringComparison.Ordinal);
    }
}
