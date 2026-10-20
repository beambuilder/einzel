using System.Text.Json;

namespace Einzel.Cli.Tests;

/// <summary>
/// That two electrodes in one driven or sequenced solve hold their own potentials, whatever
/// they are called.
/// </summary>
/// <remarks>
/// <para>
/// A name was optional, every unnamed electrode was called <c>electrode</c>, and nothing
/// required two electrodes in one solve to be called different things. The channel
/// decomposition a driven or sequenced geometry is solved through keys each supply's
/// coefficients by name, so two plates at +2 V and -2 V became one entry holding -2 V and
/// both were solved at -2 V. The deflector they make was switched off: an ion that the same
/// model with named plates bends 0.595 mm flew straight, on a document that validated,
/// solved, converged and exited 0. A static geometry was right, because it builds its mask
/// electrode by electrode - which is why the defect needed a drive or a sequence to show.
/// </para>
/// <para>
/// Every comparison here is between two documents that differ only in what the electrodes
/// are called, so the field the named one builds is the reference and nothing is a
/// tolerance: the two must fly the same ion to the last bit. The deflection itself is the
/// control - two runs that both lost the plates would agree too.
/// </para>
/// </remarks>
public sealed class ElectrodeNameTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-tests", Guid.NewGuid().ToString("N"));

    /// <summary>A one-microsecond flight through a ±2 V deflector bends the ion this far, and more.</summary>
    /// <remarks>Measured at 0.595 mm in the plane and 0.178 mm in the volume.</remarks>
    private const double DeflectedMm = 0.1;

    public ElectrodeNameTests() => Directory.CreateDirectory(Path.Combine(_root, "models"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Two unnamed plates in a driven cross-section are solved at their own potentials.</summary>
    [Fact]
    public void TwoUnnamedPlatesInADrivenCrossSectionHoldTheirOwnPotentials()
    {
        var named = FinalPosition(Plane(upper: "upper", lower: "lower"), "named");
        var unnamed = FinalPosition(Plane(upper: null, lower: null), "unnamed");

        Assert.True(
            Math.Abs(named[1]) > DeflectedMm,
            $"the named deflector must bend the ion, and bent it {named[1]:G6} mm");
        Assert.Equal(named, unnamed);
    }

    /// <summary>The same in a volume, whose channels go through the same decomposition.</summary>
    [Fact]
    public void TwoUnnamedPlatesInADrivenVolumeHoldTheirOwnPotentials()
    {
        var named = FinalPosition(Volume(upper: "upper", lower: "lower"), "named-3d");
        var unnamed = FinalPosition(Volume(upper: null, lower: null), "unnamed-3d");

        Assert.True(
            Math.Abs(named[1]) > DeflectedMm,
            $"the named deflector must bend the ion, and bent it {named[1]:G6} mm");
        Assert.Equal(named, unnamed);
    }

    /// <summary>
    /// A sequenced cross-section with no drive at all reaches the decomposition too, so it
    /// had the defect too.
    /// </summary>
    /// <remarks>
    /// The plates hold nothing until the phase sets them, so the element is genuinely moved
    /// by the sequence and is solved channel by channel: an element no phase moves is static
    /// and would pass this test with the defect in place.
    /// </remarks>
    [Fact]
    public void TwoUnnamedPlatesInASequencedCrossSectionHoldTheirOwnPotentials()
    {
        var named = FinalPosition(Plane(upper: "upper", lower: "lower", sequenced: true), "named-seq");
        var unnamed = FinalPosition(Plane(upper: null, lower: null, sequenced: true), "unnamed-seq");

        Assert.True(
            Math.Abs(named[1]) > DeflectedMm,
            $"the named deflector must bend the ion, and bent it {named[1]:G6} mm");
        Assert.Equal(named, unnamed);
    }

    /// <summary>Two plates given one name are refused, at the second one's name.</summary>
    [Fact]
    public void TwoPlatesSharingANameAreRefused()
    {
        var path = Write(Plane(upper: "plate", lower: "plate"), "twins");
        var (exit, stdout, _) = Run("validate", path, "--json");

        Assert.Equal(1, exit);

        using var result = JsonDocument.Parse(stdout);
        var errors = result.RootElement.GetProperty("errors");

        Assert.Equal(1, errors.GetArrayLength());
        Assert.Equal("/fields/0/solve/electrodes/1/name", errors[0].GetProperty("path").GetString());
        Assert.Contains("both called 'plate'", errors[0].GetProperty("constraint").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Two taps on one generator at one phase are one tap of their sum, and not the second
    /// alone.
    /// </summary>
    /// <remarks>
    /// The same assignment that let the later of two same-named electrodes win also let the
    /// later of two taps win, because both resolve into one supply under one name: 100 V and
    /// 50 V on one generator flew exactly as 50 V alone. Not to the bit against the sum, since
    /// 100 cos + 50 cos is not 150 cos in floating point; the control is the last tap alone,
    /// which the defect reproduced exactly.
    /// </remarks>
    [Fact]
    public void TwoTapsOnOneGeneratorAreTheirSum()
    {
        static string Taps(params string[] volts) =>
            "\"taps\": [" + string.Join(", ", volts.Select(v =>
                $$"""{ "amplitude": { "value": {{v}}, "unit": "V" }, "phase": { "value": 0.1, "unit": "1" } }""")) + "],";

        var both = FinalPosition(Plane("upper", "lower", rfTaps: Taps("100", "50")), "two-taps");
        var sum = FinalPosition(Plane("upper", "lower", rfTaps: Taps("150")), "sum-tap");
        var last = FinalPosition(Plane("upper", "lower", rfTaps: Taps("50")), "last-tap");

        var againstSum = Distance(both, sum);
        var againstLast = Distance(both, last);

        Assert.True(againstLast > 1e-3, $"the RF must move the ion measurably, and moved it {againstLast:G4} mm");
        Assert.True(againstSum < 1e-9, $"two taps flew {againstSum:G4} mm from one tap of their sum");
    }

    private double[] FinalPosition(string modelJson, string tag)
    {
        var path = Write(modelJson, tag);
        var (exit, stdout, stderr) = Run("run", path, "--json");

        Assert.True(exit == 0, $"{tag}: exit {exit}: {stderr}");

        using var result = JsonDocument.Parse(stdout);
        var final = result.RootElement.GetProperty("finalPositionMm");
        return [final[0].GetDouble(), final[1].GetDouble(), final[2].GetDouble()];
    }

    private string Write(string modelJson, string tag)
    {
        var path = Path.Combine(_root, "models", $"{tag}.json");
        File.WriteAllText(path, modelJson);
        return path;
    }

    private static double Distance(double[] a, double[] b) =>
        Math.Sqrt(((a[0] - b[0]) * (a[0] - b[0])) + ((a[1] - b[1]) * (a[1] - b[1])) + ((a[2] - b[2]) * (a[2] - b[2])));

    /// <summary>A plate's name property, or nothing for an unnamed one.</summary>
    private static string NameOf(string? name) => name is null ? string.Empty : $"\"name\": \"{name}\",";

    /// <summary>
    /// Two plates either side of the beam at +2 V and -2 V, and a small driven disc well clear
    /// of it so the geometry is solved through the channel decomposition.
    /// </summary>
    /// <param name="upper">The upper plate's name, or null for none.</param>
    /// <param name="lower">The lower plate's name, or null for none.</param>
    /// <param name="sequenced">
    /// Whether the plates are switched on by a sequence phase instead, with no drive at all.
    /// </param>
    /// <param name="rfTaps">How the disc taps the drive, if not one 1 V tap.</param>
    private static string Plane(string? upper, string? lower, bool sequenced = false, string? rfTaps = null)
    {
        var drive = sequenced
            ? string.Empty
            : """ "drive": { "frequency": { "value": 1, "unit": "MHz" }, "waveform": "sinusoid" }, """;

        var rf = sequenced
            ? string.Empty
            : rfTaps ?? """ "driveAmplitude": { "value": 1, "unit": "V" }, """;

        var sequence = sequenced
            ? """
              "sequence": [
                { "name": "deflect", "duration": { "value": 20, "unit": "us" },
                  "set": { "plateVolts": { "value": 2, "unit": "V" } } }
              ],
              """
            : string.Empty;

        return $$"""
        {
          "schemaVersion": "0.14",
          "name": "deflector",
          "parameters": {
            "plateVolts": { "value": {{(sequenced ? "0" : "2")}}, "unit": "V" }
          },
          "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 0], "unit": "mm" },
            "direction": { "value": [0, 0, 1] },
            "accelerationPotential": { "value": 1000, "unit": "V" }
          },
          "fields": [{
            "type": "solved2d",
            "solve": {
              {{drive}}
              "minX": { "value": -12, "unit": "mm" }, "maxX": { "value": 12, "unit": "mm" },
              "minY": { "value": -5, "unit": "mm" }, "maxY": { "value": 5, "unit": "mm" },
              "cellSize": { "value": 0.25, "unit": "mm" },
              "electrodes": [
                { {{NameOf(upper)}} "shape": "rectangle",
                  "minX": { "value": -8, "unit": "mm" }, "maxX": { "value": 8, "unit": "mm" },
                  "minY": { "value": 2.1, "unit": "mm" }, "maxY": { "value": 3.1, "unit": "mm" },
                  "potential": { "expression": "plateVolts", "unit": "V" } },
                { {{NameOf(lower)}} "shape": "rectangle",
                  "minX": { "value": -8, "unit": "mm" }, "maxX": { "value": 8, "unit": "mm" },
                  "minY": { "value": -3.1, "unit": "mm" }, "maxY": { "value": -2.1, "unit": "mm" },
                  "potential": { "expression": "-plateVolts", "unit": "V" } },
                { "name": "rf", "shape": "disc",
                  "centreX": { "value": 10.6, "unit": "mm" }, "centreY": { "value": 0.1, "unit": "mm" },
                  "radius": { "value": 0.5, "unit": "mm" },
                  {{rf}}
                  "potential": { "value": 0, "unit": "V" } }
              ]
            }
          }],
          {{sequence}}
          "detector": { "planePoint": { "value": [0, 0, 50], "unit": "mm" }, "normal": { "value": [0, 0, -1] } },
          "transport": { "mode": "trajectory", "relativeTolerance": 1e-9, "maximumFlightTime": { "value": 20, "unit": "us" } }
        }
        """;
    }

    /// <summary>The same deflector as boxes in a volume, the plates running the length of the flight.</summary>
    private static string Volume(string? upper, string? lower)
    {
        static string Box(string name, string y0, string y1, string potential) =>
            $$"""
            { {{name}} "shape": "box",
              "minX": { "value": -4.05, "unit": "mm" }, "maxX": { "value": 4.05, "unit": "mm" },
              "minY": { "value": {{y0}}, "unit": "mm" }, "maxY": { "value": {{y1}}, "unit": "mm" },
              "minZ": { "value": 2.03, "unit": "mm" }, "maxZ": { "value": 28.03, "unit": "mm" },
              "potential": { "value": {{potential}}, "unit": "V" } }
            """;

        return $$"""
        {
          "schemaVersion": "0.14",
          "name": "deflector-3d",
          "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 1.3], "unit": "mm" },
            "direction": { "value": [0, 0, 1] },
            "accelerationPotential": { "value": 1000, "unit": "V" }
          },
          "fields": [{
            "type": "solved3d",
            "solve3d": {
              "drive": { "frequency": { "value": 1, "unit": "MHz" }, "waveform": "sinusoid" },
              "minX": { "value": -6.1, "unit": "mm" }, "maxX": { "value": 6.1, "unit": "mm" },
              "minY": { "value": -5.1, "unit": "mm" }, "maxY": { "value": 5.1, "unit": "mm" },
              "minZ": { "value": -0.1, "unit": "mm" }, "maxZ": { "value": 30.1, "unit": "mm" },
              "cellSize": { "value": 0.5, "unit": "mm" },
              "electrodes": [
                {{Box(NameOf(upper), "2.07", "3.07", "2")}},
                {{Box(NameOf(lower), "-3.07", "-2.07", "-2")}},
                { "name": "rf", "shape": "box",
                  "minX": { "value": 4.83, "unit": "mm" }, "maxX": { "value": 5.33, "unit": "mm" },
                  "minY": { "value": -0.27, "unit": "mm" }, "maxY": { "value": 0.23, "unit": "mm" },
                  "minZ": { "value": 2.03, "unit": "mm" }, "maxZ": { "value": 28.03, "unit": "mm" },
                  "driveAmplitude": { "value": 1, "unit": "V" },
                  "potential": { "value": 0, "unit": "V" } }
              ]
            }
          }],
          "detector": { "planePoint": { "value": [0, 0, 29], "unit": "mm" }, "normal": { "value": [0, 0, -1] } },
          "transport": { "mode": "trajectory", "relativeTolerance": 1e-9, "maximumFlightTime": { "value": 20, "unit": "us" } }
        }
        """;
    }

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
            var exitCode = Program.Main(args);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }
}
