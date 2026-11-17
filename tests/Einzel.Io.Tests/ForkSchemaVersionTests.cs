using Einzel.Core.Errors;
using Einzel.Core.Model;

namespace Einzel.Io.Tests;

/// <summary>
/// The fork's schema number: upstream's number, then <c>-esibd.N</c> for the fork's N
/// additions to the model format on top of it. Every number either tree has written still
/// reads, and the number this build writes is the marked one.
/// </summary>
/// <remarks>
/// "0.15" was written by two trees for two things: upstream's magnetic field and, earlier,
/// the fork's waveform objects. "0.16" was the fork's alone (<c>transport.edges</c>). Both
/// must read whichever feature they carry, which they do because a version is a member of a
/// list and no feature is switched by it; this pins that.
/// </remarks>
public sealed class ForkSchemaVersionTests
{
    private const string Magnet =
        """{ "type": "uniformMagnetic", "fluxDensity": { "value": [0.0, 0.0, 0.1], "unit": "T" } }""";

    private const string Sinusoid = "\"sinusoid\"";

    private const string WaveformObject = """{ "type": "rectangular", "dutyCycle": 0.4 }""";

    /// <summary>The number this build writes, and the one a new document carries.</summary>
    [Fact]
    public void ThisBuildWritesTheForkMarkedNumber()
    {
        Assert.Equal("0.15-esibd.2", ModelSchema.CurrentVersion);
        Assert.Equal("0.15-esibd.2", new ModelDocument().SchemaVersion);

        // As plain text: a search of a written file for the number finds it.
        var written = ModelJson.Write(
            ModelJson.Parse(Model("0.14", Sinusoid, null, null)) with { SchemaVersion = ModelSchema.CurrentVersion });

        Assert.Contains("\"schemaVersion\": \"0.15-esibd.2\"", written, StringComparison.Ordinal);
        Assert.True(ModelValidator.Validate(ModelJson.Parse(written)).IsValid);
    }

    /// <summary>
    /// Every number a file may carry reads, with the features that were written under it: a
    /// plain 0.14 document, an upstream 0.15 magnet, a fork 0.15 waveform object, a fork 0.16
    /// <c>transport.edges</c>, and the current number with all three.
    /// </summary>
    /// <param name="version">The version the document declares.</param>
    /// <param name="waveformObject">Whether the drive's waveform is the object form.</param>
    /// <param name="magnet">Whether the document declares a uniform magnetic field.</param>
    /// <param name="edges">The <c>transport.edges</c> value, or null for no key.</param>
    [Theory]
    [InlineData("0.14", false, false, null)]
    [InlineData("0.15", false, true, null)]
    [InlineData("0.15", true, false, null)]
    [InlineData("0.16", true, false, "cap")]
    [InlineData("0.15-esibd.2", true, true, "cap")]
    public void EveryNumberEitherTreeHasWrittenStillReads(string version, bool waveformObject, bool magnet, string? edges)
    {
        var json = Model(version, waveformObject ? WaveformObject : Sinusoid, magnet ? Magnet : null, edges);
        var validation = ModelValidator.Validate(ModelJson.Parse(json));

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => $"{e.Path} {e.Constraint}")));
        Assert.Equal(edges is null, validation.Model!.LandOnEdges);
    }

    /// <summary>
    /// The control: a number nobody wrote is refused, including the ones the scheme could
    /// produce later and a bare suffix. Membership, not a parse of the text.
    /// </summary>
    /// <param name="version">A version this build does not read.</param>
    [Theory]
    [InlineData("0.17")]
    [InlineData("0.15-esibd.1")]
    [InlineData("0.15-esibd.3")]
    [InlineData("0.16-esibd.2")]
    [InlineData("0.15-esibd")]
    [InlineData("0.15 ")]
    public void ANumberNobodyWroteIsRefused(string version)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(Model(version, Sinusoid, null, null)));

        var error = Assert.Single(validation.Errors);
        Assert.Equal(ErrorCodes.SchemaInvalid, error.Code);
        Assert.Equal("/schemaVersion", error.Path);
        Assert.Contains("0.15-esibd.2", error.Suggestion, StringComparison.Ordinal);
    }

    private static string Model(string version, string waveform, string? magnet, string? edges) => $$"""
        {
          "schemaVersion": "{{version}}",
          "name": "versions",
          "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 10, "unit": "V" }
          },
          "fields": [
            { "type": "solved2d", "solve": {
                "drive": { "frequency": { "value": 1, "unit": "MHz" }, "waveform": {{waveform}} },
                "minX": { "value": -10, "unit": "mm" }, "maxX": { "value": 10, "unit": "mm" },
                "minY": { "value": -10, "unit": "mm" }, "maxY": { "value": 10, "unit": "mm" },
                "cellSize": { "value": 0.5, "unit": "mm" },
                "electrodes": [
                  { "name": "rod", "shape": "disc",
                    "centreX": { "value": 0, "unit": "mm" }, "centreY": { "value": -4, "unit": "mm" },
                    "radius": { "value": 1, "unit": "mm" },
                    "potential": { "value": 0, "unit": "V" }, "driveAmplitude": { "value": 50, "unit": "V" } }
                ] } }{{(magnet is null ? "" : ", " + magnet)}}
          ],
          "detector": { "planePoint": { "value": [8, 0, 0], "unit": "mm" }, "normal": { "value": [-1, 0, 0] } },
          "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 10, "unit": "us" }{{(edges is null ? "" : $", \"edges\": \"{edges}\"")}} }
        }
        """;
}
