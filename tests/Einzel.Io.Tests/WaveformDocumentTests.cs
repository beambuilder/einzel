using System.Globalization;
using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Units;

namespace Einzel.Io.Tests;

/// <summary>
/// That a drive's <c>waveform</c> reads as a name or as an object, compiles each object
/// form to the drive it describes, and refuses everything else at the pointer of the key
/// at fault.
/// </summary>
public sealed class WaveformDocumentTests
{
    private const string Pointer = "/fields/0/solve/drive";

    /// <summary>The short form writes back as a name and the object form as an object, both equal to what was read.</summary>
    [Theory]
    [InlineData("\"rectangular\"", "\"waveform\": \"rectangular\"")]
    [InlineData("""{ "type": "analytic", "expression": "1 - 2*floor(2*p)", "breakpoints": [0, 0.5] }""", "\"expression\": \"1 - 2*floor(2*p)\"")]
    [InlineData("""{ "type": "tabulated", "file": "drive.csv", "interpolation": "cubic" }""", "\"interpolation\": \"cubic\"")]
    [InlineData("""{ "type": "harmonic", "terms": [{ "order": 1, "amplitude": 1, "phase": 0.25 }, { "order": 3, "amplitude": 0.3 }] }""", "\"order\": 3")]
    public void BothSpellingsRoundTripThroughTheSerialiserAndCompareByValue(string waveform, string written)
    {
        var first = ModelJson.Parse(Model(waveform));
        var text = ModelJson.Write(first);
        var second = ModelJson.Parse(text);

        Assert.Contains(written, text, StringComparison.Ordinal);
        Assert.Equal(Drive(first).Waveform, Drive(second).Waveform);
        Assert.Equal(Drive(first), Drive(second));
    }

    /// <summary>Documents and compiled drives that differ only inside a list compare unequal.</summary>
    [Fact]
    public void ShapesDifferingOnlyInsideAListCompareUnequal()
    {
        WaveformDocument a = new() { Type = "harmonic", Terms = [new() { Order = 1, Amplitude = 1 }], Breakpoints = [0.1] };

        Assert.Equal(a, a with { Terms = [new() { Order = 1, Amplitude = 1 }], Breakpoints = [0.1] });
        Assert.NotEqual(a, a with { Terms = [new() { Order = 2, Amplitude = 1 }] });
        Assert.NotEqual(a, a with { Breakpoints = [0.2] });

        CompiledDrive d = new(1e6, DriveWaveform.Harmonic, 0.5) { Terms = [new(1, 1, 0)], Breakpoints = [0.1] };

        Assert.Equal(d, d with { Terms = [new(1, 1, 0)], Breakpoints = [0.1] });
        Assert.NotEqual(d, d with { Terms = [new(1, 1, 0.5)] });
        Assert.NotEqual(d, d with { Breakpoints = [0.2] });
        Assert.NotEqual(d, d with { Table = new WaveformTable([0, 0.5, 0.7], [1, -1, 0]) });
    }

    /// <summary>An unknown key inside the object is refused as one anywhere else is, pointing at the waveform.</summary>
    [Fact]
    public void AnUnknownKeyInTheWaveformObjectIsRefused()
    {
        var failure = Assert.Throws<EinzelException>(() =>
            ModelJson.Parse(Model("""{ "type": "analytic", "expresion": "1" }""")));

        Assert.Equal(ErrorCodes.SchemaInvalid, failure.Error.Code);
        Assert.Equal($"{Pointer}/waveform", failure.Error.Path);
        Assert.Equal("'expresion' is not part of a waveform object and could not be mapped", failure.Error.Constraint);
        Assert.Contains("not part of the model format", failure.Error.Suggestion, StringComparison.Ordinal);
    }

    /// <summary>
    /// A waveform that is neither a name nor an object is refused at the waveform, naming
    /// what it is. Such a document was refused before the object form too, with the
    /// serialiser's own text; the code and the pointer are unchanged.
    /// </summary>
    [Theory]
    [InlineData("3", "a number")]
    [InlineData("true", "a boolean")]
    [InlineData("[\"sinusoid\"]", "an array")]
    public void AWaveformThatIsNeitherANameNorAnObjectIsRefusedNamingItsKind(string waveform, string kind)
    {
        var failure = Assert.Throws<EinzelException>(() => ModelJson.Parse(Model(waveform)));

        Assert.Equal(ErrorCodes.SchemaInvalid, failure.Error.Code);
        Assert.Equal($"{Pointer}/waveform", failure.Error.Path);
        Assert.EndsWith($"not {kind}", failure.Error.Constraint, StringComparison.Ordinal);
    }

    /// <summary>
    /// A volume geometry's drives are refused at their own pointer, and a document read
    /// from a string finds its table in the working directory, joined linearly when the
    /// document does not say how.
    /// </summary>
    [Fact]
    public void AVolumeDriveIsRefusedAtItsPointerAndAStringDocumentReadsFromTheWorkingDirectory()
    {
        var name = $"einzel-cwd-{Guid.NewGuid():N}.csv";
        var volume = Volume($$"""{ "type": "tabulated", "file": "{{name}}" }""");

        var missing = ModelValidator.Validate(ModelJson.Parse(volume)).Errors[0];
        Assert.Equal("/fields/0/solve3d/drives/1/waveform/file", missing.Path);

        File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), name), "0,1\n0.5,-1\n0.75,0\n");

        try
        {
            var validation = ModelValidator.Validate(ModelJson.Parse(volume));
            Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => $"{e.Path} {e.Constraint}")));
            var drive = validation.Model!.Fields[0].Solve3D!.Drives[1];
            Assert.Equal(WaveformInterpolation.Linear, drive.Interpolation);
            Assert.Equal([0.0, 0.5, 0.75], drive.Table!.Phases);
        }
        finally
        {
            File.Delete(Path.Combine(Directory.GetCurrentDirectory(), name));
        }
    }

    /// <summary>A small volume geometry with two drives, the second shaped by <paramref name="waveform"/>.</summary>
    internal static string Volume(string waveform) => $$"""
        {
          "schemaVersion": "0.15",
          "name": "volume",
          "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 0], "unit": "mm" },
            "direction": { "value": [0, 0, 1] },
            "accelerationPotential": { "value": 10, "unit": "V" }
          },
          "fields": [
            { "type": "solved3d", "solve3d": {
                "drives": [
                  { "name": "main", "frequency": { "value": 1, "unit": "MHz" } },
                  { "name": "aux", "frequency": { "value": 0.1, "unit": "MHz" }, "waveform": {{waveform}} }
                ],
                "minX": { "value": -6, "unit": "mm" }, "maxX": { "value": 6, "unit": "mm" },
                "minY": { "value": -6, "unit": "mm" }, "maxY": { "value": 6, "unit": "mm" },
                "minZ": { "value": -6, "unit": "mm" }, "maxZ": { "value": 6, "unit": "mm" },
                "cellSize": { "value": 1, "unit": "mm" },
                "electrodes": [
                  { "name": "plate", "shape": "box",
                    "minX": { "value": -4, "unit": "mm" }, "maxX": { "value": 4, "unit": "mm" },
                    "minY": { "value": -4, "unit": "mm" }, "maxY": { "value": 4, "unit": "mm" },
                    "minZ": { "value": -4, "unit": "mm" }, "maxZ": { "value": -3, "unit": "mm" },
                    "taps": [ { "drive": "main", "amplitude": { "value": 50, "unit": "V" } },
                              { "drive": "aux", "amplitude": { "value": 5, "unit": "V" } } ],
                    "potential": { "value": 0, "unit": "V" } }
                ] } }
          ],
          "detector": { "planePoint": { "value": [0, 0, 5], "unit": "mm" }, "normal": { "value": [0, 0, -1] } },
          "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 10, "unit": "us" } }
        }
        """;

    /// <summary>Every refusal of the waveform grammar, with its pointer and code.</summary>
    [Theory]
    [InlineData("\"square\"", "", "/waveform", ErrorCodes.SchemaInvalid)]
    [InlineData("\"rectangular\"", "\"dutyCycle\": 1.2,", "/dutyCycle", ErrorCodes.ValueOutOfBounds)]
    [InlineData("""{ "type": "triangle" }""", "", "/waveform/type", ErrorCodes.SchemaInvalid)]
    [InlineData("""{ "type": "analytic", "expression": "1 - 2*floor(2*q)" }""", "", "/waveform/expression", ErrorCodes.SchemaInvalid)]
    [InlineData("""{ "type": "analytic", "expression": "gap" }""", "", "/waveform/expression", ErrorCodes.SchemaInvalid)]
    [InlineData("""{ "type": "analytic", "expression": "log(p - 0.5)" }""", "", "/waveform/expression", ErrorCodes.SchemaInvalid)]
    [InlineData("""{ "type": "analytic", "expression": "1/(p - 0.3)", "breakpoints": [0.3] }""", "", "/waveform/expression", ErrorCodes.SchemaInvalid)]
    [InlineData("""{ "type": "analytic" }""", "", "/waveform/expression", ErrorCodes.SchemaInvalid)]
    [InlineData("""{ "type": "analytic", "expression": "1", "breakpoints": [0.5, 0.2] }""", "", "/waveform/breakpoints/1", ErrorCodes.ValueOutOfBounds)]
    [InlineData("""{ "type": "analytic", "expression": "1", "breakpoints": [1] }""", "", "/waveform/breakpoints/0", ErrorCodes.ValueOutOfBounds)]
    [InlineData("""{ "type": "analytic", "expression": "1", "file": "x.csv" }""", "", "/waveform/file", ErrorCodes.SchemaInvalid)]
    [InlineData("""{ "type": "analytic", "expression": "1", "dutyCycle": 0.5 }""", "", "/waveform/dutyCycle", ErrorCodes.SchemaInvalid)]
    [InlineData("""{ "type": "analytic", "expression": "1" }""", "\"dutyCycle\": 0.5,", "/dutyCycle", ErrorCodes.SchemaInvalid)]
    [InlineData("""{ "type": "tabulated", "file": "absent.csv" }""", "", "/waveform/file", ErrorCodes.SchemaInvalid)]
    [InlineData("""{ "type": "tabulated" }""", "", "/waveform/file", ErrorCodes.SchemaInvalid)]
    [InlineData("""{ "type": "tabulated", "file": "drive.csv", "interpolation": "spline" }""", "", "/waveform/interpolation", ErrorCodes.SchemaInvalid)]
    [InlineData("""{ "type": "harmonic", "terms": [] }""", "", "/waveform/terms", ErrorCodes.SchemaInvalid)]
    [InlineData("""{ "type": "harmonic", "terms": [{ "order": -1, "amplitude": 1 }] }""", "", "/waveform/terms/0/order", ErrorCodes.ValueOutOfBounds)]
    [InlineData("""{ "type": "harmonic", "terms": [{ "order": 1 }] }""", "", "/waveform/terms/0/amplitude", ErrorCodes.ValueOutOfBounds)]
    [InlineData("""{ "type": "harmonic", "terms": [{ "order": 1, "amplitude": 1 }], "breakpoints": [0] }""", "", "/waveform/breakpoints", ErrorCodes.SchemaInvalid)]
    [InlineData("""{ "type": "rectangular", "dutyCycle": 0 }""", "", "/waveform/dutyCycle", ErrorCodes.ValueOutOfBounds)]
    [InlineData("""{ "type": "rectangular", "dutyCycle": 0.4 }""", "\"dutyCycle\": 0.4,", "/dutyCycle", ErrorCodes.SchemaInvalid)]
    public void EachRefusalPointsAtTheKeyAtFault(string waveform, string beside, string suffix, string code)
    {
        var directory = Directory.CreateTempSubdirectory("einzel-waveform-").FullName;

        try
        {
            File.WriteAllText(Path.Combine(directory, "drive.csv"), "0,1\n0.5,-1\n0.75,0\n");
            var document = ModelJson.Parse(Model(waveform, beside, parameters: """ "gap": { "value": 2, "unit": "mm" } """));
            var errors = ModelValidator.Validate(document, null, directory).Errors;

            var error = errors[0]; // the electrode tapping the refused drive follows
            Assert.Equal(code, error.Code);
            Assert.Equal(Pointer + suffix, error.Path);
            Assert.False(string.IsNullOrWhiteSpace(error.Suggestion));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A parameter named <c>p</c> would be shadowed by the phase, so an analytic drive refuses it.</summary>
    [Fact]
    public void AParameterNamedPIsRefusedByAnAnalyticDrive()
    {
        var document = ModelJson.Parse(Model(
            """{ "type": "analytic", "expression": "sinPi(2*p)" }""", parameters: """ "p": { "value": 1, "unit": "1" } """));

        var error = ModelValidator.Validate(document).Errors[0];

        Assert.Equal($"{Pointer}/waveform/expression", error.Path);
        Assert.Contains("shadows", error.Constraint, StringComparison.Ordinal);
    }

    /// <summary>A malformed table is refused at the file, naming the line.</summary>
    [Fact]
    public void AMalformedTableIsRefusedAtTheFileNamingTheLine()
    {
        var directory = Directory.CreateTempSubdirectory("einzel-waveform-").FullName;

        try
        {
            File.WriteAllText(Path.Combine(directory, "drive.csv"), "phase,value\n0,1\n0.5;-1\n");
            var errors = ModelValidator.Validate(
                ModelJson.Parse(Model("""{ "type": "tabulated", "file": "drive.csv" }""")), null, directory).Errors;

            var error = errors[0]; // the electrode tapping the refused drive follows
            Assert.Equal($"{Pointer}/waveform/file", error.Path);
            Assert.Contains("line 3", error.Constraint, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A 0.14 document's short form and a 0.15 document's object form compile to the same
    /// drive; the version string is the only difference between the two documents' meaning.
    /// </summary>
    [Theory]
    [InlineData("\"rectangular\"", "\"dutyCycle\": 0.5,", """{ "type": "rectangular", "dutyCycle": 0.5 }""")]
    [InlineData("\"rectangular\"", "\"dutyCycle\": 0.3,", """{ "type": "rectangular" }""")]
    [InlineData("\"sinusoid\"", "", """{ "type": "sinusoid" }""")]
    public void AShortFormIn014CompilesToTheSameDriveAsTheObjectFormIn015(string name, string beside, string shape)
    {
        var old = Compiled(Model(name, beside, version: "0.14"));
        var current = Compiled(Model(shape, shape.Contains("dutyCycle", StringComparison.Ordinal) ? "" : beside, version: "0.15"));

        Assert.Equal(old, current);
    }

    /// <summary>An analytic drive binds the parameters it names, with the values an override gives them.</summary>
    [Fact]
    public void AnAnalyticDriveBindsTheParametersItNames()
    {
        var document = ModelJson.Parse(Model(
            """{ "type": "analytic", "expression": "level * sinPi(2*p)" }""",
            parameters: """ "level": { "value": 0.5, "unit": "1" }, "unused": { "value": 3, "unit": "1" } """));

        var plain = Compiled(document, null);
        var overridden = Compiled(document, new Dictionary<string, Quantity>(StringComparer.Ordinal) { ["level"] = Quantity.Number(0.7) });

        Assert.Equal(DriveWaveform.Analytic, plain.Waveform);
        Assert.Equal(["level"], plain.Bindings!.Keys);
        Assert.Equal(0.5, plain.Bindings["level"].SiValue);
        Assert.Equal(0.7, overridden.Bindings!["level"].SiValue);
        Assert.NotEqual(plain, overridden);
    }

    /// <summary>A table resolves against the model's directory and its samples, path and interpolation reach the drive.</summary>
    [Fact]
    public void ATableIsReadRelativeToTheModelDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("einzel-waveform-").FullName;

        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "tables"));
            File.WriteAllText(Path.Combine(directory, "tables", "drive.csv"), "phase,value\n0,1\n0.25,0\n0.5,-1\n0.75,0\n");
            var document = ModelJson.Parse(Model("""{ "type": "tabulated", "file": "tables/drive.csv", "interpolation": "cubic", "breakpoints": [0.5] }"""));

            var drive = Compiled(document, null, directory);

            Assert.Equal(DriveWaveform.Tabulated, drive.Waveform);
            Assert.Equal("tables/drive.csv", drive.TablePath);
            Assert.Equal(WaveformInterpolation.Cubic, drive.Interpolation);
            Assert.Equal([0.0, 0.25, 0.5, 0.75], drive.Table!.Phases);
            Assert.Equal([1.0, 0.0, -1.0, 0.0], drive.Table.Values);
            Assert.Equal([0.5], drive.Breakpoints);

            // Elsewhere the same relative path names nothing.
            Assert.False(ModelValidator.Validate(document, null, Path.Combine(directory, "tables")).IsValid);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The second of two drives is refused at its own index.</summary>
    [Fact]
    public void ARefusalInADrivesListPointsAtThatDrive()
    {
        var json = Model("", drive: """
            "drives": [
              { "name": "a", "frequency": { "value": 1, "unit": "MHz" } },
              { "name": "b", "frequency": { "value": 1, "unit": "MHz" }, "waveform": { "type": "harmonic", "terms": [] } }
            ],
            """);

        var error = ModelValidator.Validate(ModelJson.Parse(json)).Errors[0];

        Assert.Equal("/fields/0/solve/drives/1/waveform/terms", error.Path);
    }

    private static DriveDocument Drive(ModelDocument document) => document.Fields![0].Solve!.Drive!;

    private static CompiledDrive Compiled(string json) => Compiled(ModelJson.Parse(json), null);

    private static CompiledDrive Compiled(
        ModelDocument document, IReadOnlyDictionary<string, Quantity>? overrides, string? directory = null)
    {
        var validation = ModelValidator.Validate(document, overrides, directory);
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => $"{e.Path} {e.Constraint}")));
        return Assert.Single(validation.Model!.Fields[0].Solve!.Drives);
    }

    private static string Model(
        string waveform, string beside = "", string parameters = "", string version = "0.15", string? drive = null) => $$"""
        {
          "schemaVersion": "{{version}}",
          "name": "waveform",
          "parameters": { {{parameters}} },
          "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 10, "unit": "V" }
          },
          "fields": [
            { "type": "solved2d", "solve": {
                {{drive ?? $"\"drive\": {{ \"frequency\": {{ \"value\": 1, \"unit\": \"MHz\" }}, {beside} \"waveform\": {waveform} }},"}}
                "minX": { "value": -10, "unit": "mm" }, "maxX": { "value": 10, "unit": "mm" },
                "minY": { "value": -10, "unit": "mm" }, "maxY": { "value": 10, "unit": "mm" },
                "cellSize": { "value": 0.5, "unit": "mm" },
                "electrodes": [
                  { "name": "rod", "shape": "disc",
                    "centreX": { "value": 0, "unit": "mm" }, "centreY": { "value": -4, "unit": "mm" },
                    "radius": { "value": 1, "unit": "mm" },
                    "potential": { "value": 0, "unit": "V" }, "driveAmplitude": { "value": 50, "unit": "V" } }
                ] } }
          ],
          "detector": { "planePoint": { "value": [8, 0, 0], "unit": "mm" }, "normal": { "value": [-1, 0, 0] } },
          "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 10, "unit": "us" } }
        }
        """;
}
