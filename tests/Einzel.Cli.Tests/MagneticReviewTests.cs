using System.Text.Json;
using System.Text.Json.Nodes;
using Einzel.Commands;
using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Io;

namespace Einzel.Cli.Tests;

/// <summary>
/// A magnet declared where nothing reads it, refused where it is declared; refusals pointing at
/// the document's own element with the regime class they belong to; and the probe and the
/// composite export reporting B rather than printing nothing inside a sector.
/// </summary>
public sealed class MagneticReviewTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-magnetic-review", Guid.NewGuid().ToString("N"));

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

    private static JsonObject Magnet(double z) => JsonNode.Parse(
        $$"""{ "type": "uniformMagnetic", "fluxDensity": { "value": [0, 0, {{z}}], "unit": "T" } }""")!
        .AsObject();

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            return (Program.Main(args), stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    private string Write(string name, JsonObject document)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"{name}.json");
        File.WriteAllText(path, document.ToJsonString());
        return path;
    }

    [Fact]
    public void AFluxDensityOnAnElementThatDoesNotReadItIsRefused()
    {
        // A Wien filter written as one element: it validated and flew with no magnet.
        var document = Example("wien-filter");
        var electric = document["fields"]!.AsArray().First(f => (string?)f!["type"] == "uniform")!.AsObject();
        electric["fluxDensity"] = JsonNode.Parse("""{ "value": [0, 0, 0.5], "unit": "T" }""");
        document["fields"] = new JsonArray(electric.DeepClone());

        var validation = Validate(document);

        Assert.False(validation.IsValid);
        var error = Assert.Single(validation.Errors, e => e.Path == "/fields/0/fluxDensity");
        Assert.Equal(ErrorCodes.SchemaInvalid, error.Code);
        Assert.Contains("uniformMagnetic", error.Constraint, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("capPotential", """{ "value": 100, "unit": "V" }""")]
    [InlineData("curvature", """{ "value": 1, "unit": "V/m^2" }""")]
    [InlineData("driveFrequency", """{ "value": 1, "unit": "MHz" }""")]
    public void AnyKindSpecificPropertyOnTheWrongKindIsRefused(string property, string value)
    {
        var document = Example("accelerating-gap");
        document["fields"]![0]![property] = JsonNode.Parse(value);

        var validation = Validate(document);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Path == $"/fields/0/{property}" && e.Code == ErrorCodes.SchemaInvalid);
    }

    [Fact]
    public void ARefusalNamesTheMagnetsOwnIndexWhenAnEarlierElementFailed()
    {
        // fields[0] has a typo and does not compile; the magnet is fields[1]. Indexed by the
        // compiled list, the diffusive refusal was reported at /fields/0.
        var document = Example("drift-tube-diffusion");
        document["schemaVersion"] = "0.15";
        var broken = document["fields"]![0]!.DeepClone().AsObject();
        broken["type"] = "unifrom";
        document["fields"] = new JsonArray(broken, Magnet(0.1));

        var validation = Validate(document);

        var refusal = Assert.Single(validation.Errors, e => e.Constraint.Contains("diffusive mode", StringComparison.Ordinal));
        Assert.Equal("/fields/1", refusal.Path);
    }

    [Fact]
    public void ATransportModeThatCannotHonourBIsARegimeRefusalWithItsExitCode()
    {
        var diffusive = Example("drift-tube-diffusion");
        diffusive["schemaVersion"] = "0.15";
        diffusive["fields"]!.AsArray().Add(Magnet(0.1));
        Assert.Contains(Validate(diffusive).Errors, e => e.Path == "/fields/1" && e.Code == ErrorCodes.RegimeInvalid);

        var charged = Example("cyclotron");
        charged["transport"]!["spaceCharge"] = "direct";
        Assert.Contains(Validate(charged).Errors, e => e.Path == "/fields/0" && e.Code == ErrorCodes.RegimeInvalid);

        // CLI-3: the regime class has its own exit code, and validate and run both use it.
        var path = Write("diffusive-magnet", diffusive);
        Assert.Equal(2, Run("validate", path).ExitCode);
        Assert.Equal(2, Run("run", path).ExitCode);

        // A malformed document stays a schema error.
        var fringed = Example("magnetic-sector-180");
        fringed["fields"]![0]!["region"]!["fringe"] = JsonNode.Parse("""{ "value": 2, "unit": "mm" }""");
        Assert.Contains(Validate(fringed).Errors, e => e.Code == ErrorCodes.SchemaInvalid);
    }

    [Fact]
    public void EinzelCompareRefusesAMagnetAsARegimeRefusal()
    {
        var path = Write("cyclotron", Example("cyclotron"));

        var refused = Assert.Throws<EinzelException>(() => ModeComparison.Execute(path, 4));

        Assert.Equal(ErrorCodes.RegimeInvalid, refused.Error.Code);
    }

    [Fact]
    public void TheProbeReportsBInsideASector()
    {
        var path = Write("sector", Example("magnetic-sector-180"));

        var (exitCode, stdout, stderr) = Run("probe", path, "--at-mm", "100,0,0", "--json");
        Assert.True(exitCode == 0, stderr);

        using var document = JsonDocument.Parse(stdout);
        var sample = document.RootElement.GetProperty("samples")[0];
        var b = sample.GetProperty("fluxDensityT").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        Assert.Equal([0.0, 0.0, 0.5], b);

        // Outside the box, B is zero and still reported.
        var (_, outside, _) = Run("probe", path, "--at-mm", "-5,0,0", "--json");
        using var outsideDocument = JsonDocument.Parse(outside);
        var zero = outsideDocument.RootElement.GetProperty("samples")[0].GetProperty("fluxDensityT")
            .EnumerateArray().Select(e => e.GetDouble()).ToArray();
        Assert.Equal([0.0, 0.0, 0.0], zero);

        // The human form and the CSV carry it, with the unit in the column name.
        var (_, human, _) = Run("probe", path, "--at-mm", "100,0,0");
        Assert.Contains("Bz_T", human, StringComparison.Ordinal);

        var csv = Path.Combine(_root, "sector.csv");
        Assert.Equal(0, Run("probe", path, "--line-mm", "-5,0,0", "--to-mm", "100,0,0", "--samples", "3", "--csv", csv).ExitCode);
        var lines = File.ReadAllLines(csv).Where(l => !l.StartsWith('#')).ToArray();
        Assert.Equal("x_mm,y_mm,z_mm,potential_V,Ex_V_per_m,Ey_V_per_m,Ez_V_per_m,Bx_T,By_T,Bz_T,status,conductor", lines[0]);
        Assert.Equal("0,0,0.5", string.Join(",", lines[^1].Split(',')[7..10]));
    }

    [Fact]
    public void AModelWithNoMagnetProbesExactlyAsBefore()
    {
        var path = Write("gap", Example("accelerating-gap"));

        var (_, json, _) = Run("probe", path, "--at-mm", "1,0,0", "--json");
        Assert.DoesNotContain("fluxDensity", json, StringComparison.Ordinal);

        var (_, human, _) = Run("probe", path, "--at-mm", "1,0,0");
        Assert.DoesNotContain("B", human.Split('\n')[0], StringComparison.Ordinal);

        var csv = Path.Combine(_root, "gap.csv");
        Assert.Equal(0, Run("probe", path, "--at-mm", "1,0,0", "--csv", csv).ExitCode);
        Assert.Contains(
            "x_mm,y_mm,z_mm,potential_V,Ex_V_per_m,Ey_V_per_m,Ez_V_per_m,status,conductor\n",
            File.ReadAllText(csv).ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheCompositeExportWritesBAndSaysSo()
    {
        var path = Write("sector", Example("magnetic-sector-180"));

        var (exitCode, stdout, stderr) = Run(
            "export", path, "--composite", "--project", _root,
            "--box-mm", "-10,-10,-10,10,10,10", "--step-mm", "10", "--json");
        Assert.True(exitCode == 0, stderr);

        using var document = JsonDocument.Parse(stdout);
        var vti = File.ReadAllText(document.RootElement.GetProperty("artifacts")[0].GetString()!);

        Assert.Contains("Name=\"B_T\"", vti, StringComparison.Ordinal);
        Assert.Contains("B_T the magnetic flux density in tesla", vti, StringComparison.Ordinal);

        // Three nodes along x: -10 mm (outside the magnet), 0 (on its face), 10 (inside).
        var block = vti[vti.IndexOf("Name=\"B_T\"", StringComparison.Ordinal)..];
        block = block[(block.IndexOf('>') + 1)..block.IndexOf("</DataArray>", StringComparison.Ordinal)];
        var rows = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("0 0 0", rows[0]);
        Assert.Equal("0 0 0.5", rows[2]);

        // And a model without a magnet writes no such array.
        var gap = Write("gap", Example("accelerating-gap"));
        var (_, plainOut, plainErr) = Run(
            "export", gap, "--composite", "--project", _root,
            "--box-mm", "-1,-1,-1,1,1,1", "--step-mm", "1", "--json");
        using var plainDocument = JsonDocument.Parse(plainOut);
        var plain = File.ReadAllText(plainDocument.RootElement.GetProperty("artifacts")[0].GetString()!);
        Assert.DoesNotContain("B_T", plain, StringComparison.Ordinal);
        Assert.True(plainErr.Length >= 0);
    }
}
