using System.Globalization;
using Einzel.Core.Model;
using Einzel.Fields.Analytic;
using Einzel.Fields.Solved;
using Einzel.Io;

namespace Einzel.Fields.Tests;

/// <summary>
/// That a drive's waveform written as an object reaches the solved field as the shape it
/// names: an analytic square wave weighs every channel exactly as the built-in rectangular
/// wave does, in two and three dimensions, and every shape but the sinusoid costs one
/// channel per distinct phase.
/// </summary>
public sealed class WaveformDocumentFieldTests
{
    private const string Square = """{ "type": "analytic", "expression": "1 - 2*floor(2*p)", "breakpoints": [0, 0.5] }""";

    /// <summary>
    /// The analytic square wave and the built-in rectangular wave give the same channels and
    /// the same channel weights at 1000 instants over three cycles, to the bit.
    /// </summary>
    /// <remarks>
    /// Bitwise because both wrap the phase the same way and return exactly plus or minus one.
    /// The rod's tap is at phase 0.3, so a shape mistaken for a sinusoid would split into a cos
    /// and a sin channel and change the count; a builder that ignored the expression would
    /// weigh by a sinusoid and change the weights.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAnalyticSquareWaveWeighsEveryChannelAsTheBuiltInRectangularDoes(bool volume)
    {
        var builtIn = Driven(Model(volume, "\"rectangular\"", [0.0, 0.3]));
        var analytic = Driven(Model(volume, Square, [0.0, 0.3]));

        Assert.Equal(builtIn.ChannelCount, analytic.ChannelCount);

        for (var i = 0; i < 1000; i++)
        {
            var t = i * 3e-6 / 1000.0;

            for (var c = 0; c < builtIn.ChannelCount; c++)
            {
                Assert.Equal(
                    BitConverter.DoubleToInt64Bits(builtIn.WeightAt(c, t)),
                    BitConverter.DoubleToInt64Bits(analytic.WeightAt(c, t)));
            }
        }
    }

    /// <summary>
    /// Three electrodes driven at phases 0, 0.25 and 0.4: a sinusoid is two channels (cos and
    /// sin) and every other shape one per distinct phase, three.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryShapeButTheSinusoidCostsOneChannelPerDistinctPhase(bool volume)
    {
        double[] phases = [0.0, 0.25, 0.4];
        var table = Path.Combine(Path.GetTempPath(), $"einzel-cos-{Guid.NewGuid():N}.csv");
        File.WriteAllText(table, string.Join('\n', Enumerable.Range(0, 16).Select(k =>
            string.Create(CultureInfo.InvariantCulture, $"{k / 16.0},{Math.Cos(2 * Math.PI * k / 16.0)}"))));

        try
        {
            // Every electrode holds 0 V apart from its drive, so there is no DC channel.
            int Count(string waveform) => Driven(Model(volume, waveform, phases)).ChannelCount;

            Assert.Equal(2, Count("\"sinusoid\""));
            Assert.Equal(2, Count("""{ "type": "sinusoid" }"""));
            Assert.Equal(3, Count("\"rectangular\""));
            Assert.Equal(3, Count(Square));
            Assert.Equal(3, Count($$"""{ "type": "tabulated", "file": "{{table.Replace("\\", "/", StringComparison.Ordinal)}}" }"""));
            Assert.Equal(3, Count("""{ "type": "harmonic", "terms": [{ "order": 1, "amplitude": 1 }] }"""));
        }
        finally
        {
            File.Delete(table);
        }
    }

    /// <summary>
    /// Each object shape weighs its one channel by the shape it names: the channel's weight
    /// over three cycles is a fixed multiple of that shape's <c>At</c>, to rounding.
    /// </summary>
    /// <remarks>
    /// The harmonic is not a plain fundamental and the table not a cosine, so a builder that
    /// fell back to a sinusoid for either shape would be off by order one.
    /// </remarks>
    [Fact]
    public void EachObjectShapeWeighsItsChannelByTheShapeItNames()
    {
        var table = Path.Combine(Path.GetTempPath(), $"einzel-tri-{Guid.NewGuid():N}.csv");
        File.WriteAllText(table, "phase,value\n0,1\n0.2,-0.4\n0.5,-1\n0.7,0.3\n");

        try
        {
            (string Document, RfWaveform Shape)[] cases =
            [
                ("""{ "type": "analytic", "expression": "1 - 4*abs(p - 0.5)" }""",
                    new RfWaveform.Analytic("1 - 4*abs(p - 0.5)", new Dictionary<string, Core.Units.Quantity>())),
                ($$"""{ "type": "tabulated", "file": "{{table.Replace("\\", "/", StringComparison.Ordinal)}}", "interpolation": "cubic" }""",
                    new RfWaveform.Tabulated([0, 0.2, 0.5, 0.7], [1, -0.4, -1, 0.3], WaveformInterpolation.Cubic)),
                ("""{ "type": "harmonic", "terms": [{ "order": 1, "amplitude": 1 }, { "order": 3, "amplitude": 0.4, "phase": 0.1 }] }""",
                    new RfWaveform.Harmonic([new HarmonicTerm(1, 1, 0), new HarmonicTerm(3, 0.4, 0.1)])),
            ];

            foreach (var (document, shape) in cases)
            {
                var driven = Driven(Model(false, document, [0.0]));
                Assert.Equal(1, driven.ChannelCount);
                var scale = driven.WeightAt(0, 0.0) / shape.At(0.0);

                for (var i = 0; i < 300; i++)
                {
                    var t = i * 3e-6 / 300.0;
                    Assert.Equal(scale * shape.At(1e6 * t), driven.WeightAt(0, t), 1e-9 * Math.Abs(scale));
                }
            }
        }
        finally
        {
            File.Delete(table);
        }
    }

    private static DrivenSolvedField Driven(string json)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(json));
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => $"{e.Code} {e.Path} {e.Constraint}")));
        var field = FieldAssembly.BuildReported(validation.Model!).Field;
        var driven = field as DrivenSolvedField
            ?? (field as SuperposedField)?.Elements.OfType<DrivenSolvedField>().FirstOrDefault();
        Assert.True(driven is not null, $"no driven field in {field.GetType().Name}");
        return driven;
    }

    /// <summary>One disc (2-D) or box (3-D) per phase, each tapping the one drive at that phase.</summary>
    private static string Model(bool volume, string waveform, double[] phases)
    {
        string Num(double v) => v.ToString(CultureInfo.InvariantCulture);
        string Mm(double v) => $$"""{ "value": {{Num(v)}}, "unit": "mm" }""";
        var drive = $$"""
            "drive": { "frequency": { "value": 1, "unit": "MHz" }, "waveform": {{waveform}} },
            """;
        var electrodes = string.Join(",\n", phases.Select((phase, i) =>
        {
            var x = -6.0 + (6.0 * i);
            var shape = volume
                ? $"\"shape\": \"box\", \"minX\": {Mm(x - 1)}, \"maxX\": {Mm(x + 1)}, \"minY\": {Mm(-1)}, \"maxY\": {Mm(1)}, \"minZ\": {Mm(2)}, \"maxZ\": {Mm(3)}"
                : $"\"shape\": \"disc\", \"centreX\": {Mm(x)}, \"centreY\": {Mm(-4)}, \"radius\": {Mm(1)}";
            return $$"""
                { "name": "e{{i}}", {{shape}},
                  "potential": { "value": 0, "unit": "V" },
                  "driveAmplitude": { "value": 50, "unit": "V" },
                  "drivePhase": { "value": {{Num(phase)}}, "unit": "1" } }
                """;
        }));
        var domain = volume
            ? $"\"minX\": {Mm(-10)}, \"maxX\": {Mm(10)}, \"minY\": {Mm(-5)}, \"maxY\": {Mm(5)}, \"minZ\": {Mm(-5)}, \"maxZ\": {Mm(5)}, \"cellSize\": {Mm(1)},"
            : $"\"minX\": {Mm(-10)}, \"maxX\": {Mm(10)}, \"minY\": {Mm(-10)}, \"maxY\": {Mm(10)}, \"cellSize\": {Mm(0.5)},";

        return $$"""
            {
              "schemaVersion": "0.15",
              "name": "three taps",
              "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
              "source": {
                "position": { "value": [0, 0, 0], "unit": "mm" },
                "direction": { "value": [1, 0, 0] },
                "accelerationPotential": { "value": 10, "unit": "V" }
              },
              "fields": [
                { "type": "{{(volume ? "solved3d" : "solved2d")}}", "{{(volume ? "solve3d" : "solve")}}": {
                    {{drive}}
                    {{domain}}
                    "electrodes": [ {{electrodes}} ] } }
              ],
              "detector": { "planePoint": { "value": [8, 0, 0], "unit": "mm" }, "normal": { "value": [-1, 0, 0] } },
              "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 10, "unit": "us" } }
            }
            """;
    }
}
