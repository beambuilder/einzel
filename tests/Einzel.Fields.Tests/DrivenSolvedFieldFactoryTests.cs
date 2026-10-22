using System.Globalization;

using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields.Analytic;
using Einzel.Fields.Solved;
using Einzel.Io;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// <see cref="DrivenSolvedField.Create"/>: a host's own basis fields, weighted per channel by a
/// constant and drive terms, give the field a hand-written superposition gives, and the field
/// the model path builds for the same drive.
/// </summary>
/// <remarks>
/// <para>
/// The reference sum is the one the fork's batch driver carried as its own class before the
/// factory existed: per channel <c>direct + amplitude * waveform.At(f * t + phase)</c>, the
/// field skipping a zero weight, the potential not. It is compared with <c>==</c>, because a
/// host that moves from that class to the factory is promised the same numbers, not close ones.
/// </para>
/// </remarks>
public sealed class DrivenSolvedFieldFactoryTests(ITestOutputHelper output)
{
    /// <summary>A static field with a different shape on every axis, so no two channels agree.</summary>
    private sealed class Polynomial(double a, double b, double c) : IElectrostaticField
    {
        public double PotentialAt(in Vec3 position) =>
            (a * position.X * position.X) + (b * position.Y) + (c * position.X * position.Z);

        public Vec3 ElectricFieldAt(in Vec3 position) => new(
            -((2.0 * a * position.X) + (c * position.Z)),
            -b,
            -(c * position.X));
    }

    private static readonly IElectrostaticField[] Channels =
    [
        new Polynomial(3.0e5, 120.0, -7.0e4),
        new Polynomial(-1.1e5, -40.0, 2.0e4),
        new Polynomial(5.0e4, 300.0, 9.0e4),
    ];

    private static readonly Vec3[] Points =
    [
        new(0.0, 0.0, 0.0),
        new(0.0013, -0.0021, 0.0007),
        new(-0.0042, 0.0008, -0.0033),
        new(0.0091, 0.0047, 0.0012),
    ];

    private const double FrequencyHz = 1.1e6;

    /// <summary>Instants across three cycles, none of them on a rectangular wave's edge.</summary>
    private static readonly double[] Times = [0.0, 0.13e-6, 0.37e-6, 0.61e-6, 1.71e-6, 2.93e-6];

    private static readonly Dictionary<string, RfWaveform> Waveforms = new()
    {
        ["sinusoid"] = new RfWaveform.Sinusoid(),
        ["rectangular"] = new RfWaveform.Rectangular(0.5),
    };

    public static TheoryData<string> WaveformNames => [.. Waveforms.Keys];

    /// <summary>One drive term on one clock, in the order the factory sums them.</summary>
    private sealed record Term(int Drive, double Amplitude, double Phase);

    /// <summary>The superposition written out, term by term in the factory's order.</summary>
    private static double Weight(
        double direct, IReadOnlyList<Term> terms, double[] frequencies, RfWaveform[] waveforms, double t)
    {
        var weight = direct;

        foreach (var term in terms)
        {
            weight += term.Amplitude * waveforms[term.Drive].At((frequencies[term.Drive] * t) + term.Phase);
        }

        return weight;
    }

    private static Vec3 ReferenceField(
        double[] direct, Term[][] terms, double[] frequencies, RfWaveform[] waveforms, Vec3 p, double t)
    {
        var e = Vec3.Zero;

        for (var i = 0; i < Channels.Length; i++)
        {
            var weight = Weight(direct[i], terms[i], frequencies, waveforms, t);

            if (weight != 0.0)
            {
                e += Channels[i].ElectricFieldAt(in p) * weight;
            }
        }

        return e;
    }

    private static double ReferencePotential(
        double[] direct, Term[][] terms, double[] frequencies, RfWaveform[] waveforms, Vec3 p, double t)
    {
        var v = 0.0;

        for (var i = 0; i < Channels.Length; i++)
        {
            v += Weight(direct[i], terms[i], frequencies, waveforms, t) * Channels[i].PotentialAt(in p);
        }

        return v;
    }

    private static DrivenSolvedField Create(
        double[] direct, Term[][] terms, double[] frequencies, RfWaveform[] waveforms) =>
        DrivenSolvedField.Create(
            Channels,
            direct,
            [.. terms.Select(ts => (IReadOnlyList<WeightTerm>)[.. ts.Select(t => new WeightTerm(t.Drive, t.Amplitude, t.Phase))])],
            frequencies,
            waveforms);

    private void AssertEqualToReference(
        double[] direct, Term[][] terms, double[] frequencies, RfWaveform[] waveforms)
    {
        var field = Create(direct, terms, frequencies, waveforms);
        var compared = 0;

        foreach (var p in Points)
        {
            foreach (var t in Times)
            {
                Assert.Equal(ReferenceField(direct, terms, frequencies, waveforms, p, t), field.ElectricFieldAt(in p, t));
                Assert.Equal(ReferencePotential(direct, terms, frequencies, waveforms, p, t), field.PotentialAt(in p, t));
                compared++;
            }
        }

        output.WriteLine($"{compared} (point, time) pairs equal to the reference, field and potential");
    }

    /// <summary>
    /// The batch driver's shape - one drive, one term per channel, one phase for the ion - is
    /// the reference sum exactly, with a zero amplitude on one channel and a zero direct
    /// weight on another.
    /// </summary>
    /// <remarks>
    /// Mutations in the factory's hand-over to the constructor: dropping each term's
    /// <c>Phase</c>; swapping the direct weight and the amplitude. Each fails here.
    /// </remarks>
    [Theory]
    [MemberData(nameof(WaveformNames))]
    public void OneDriveIsTheReferenceSumExactly(string waveform)
    {
        const double phase = 0.3;

        AssertEqualToReference(
            direct: [-35.0, 0.0, -100.0],
            terms:
            [
                [new Term(0, 80.0, phase)],
                [new Term(0, -80.0, phase)],
                [new Term(0, 0.0, phase)],
            ],
            frequencies: [FrequencyHz],
            waveforms: [Waveforms[waveform]]);
    }

    /// <summary>
    /// Two drives on two clocks, one channel tapping both and one tapping only the second, is
    /// the reference sum exactly.
    /// </summary>
    /// <remarks>
    /// Mutation: the hand-over sending every term to drive 0 (or to the other drive) fails
    /// here, since the two clocks differ in frequency and in waveform.
    /// </remarks>
    [Theory]
    [MemberData(nameof(WaveformNames))]
    public void TwoDrivesAreTheReferenceSumExactly(string waveform)
    {
        AssertEqualToReference(
            direct: [12.0, 0.0, -4.0],
            terms:
            [
                [new Term(0, 50.0, 0.1), new Term(1, 7.0, 0.45)],
                [new Term(1, -20.0, 0.8)],
                [],
            ],
            frequencies: [FrequencyHz, 0.37 * FrequencyHz],
            waveforms: [Waveforms[waveform], new RfWaveform.Rectangular(0.3)]);
    }

    /// <summary>
    /// The time-free arm of a factory-made field answers at t = 0, and nothing else: the
    /// field-seam trap <c>docs/extending.md</c> describes, held to its documented behaviour.
    /// </summary>
    /// <remarks>
    /// Mutation: the time-free <c>ElectricFieldAt</c> or <c>PotentialAt</c> reading any other
    /// instant fails here, because the weights at the points sampled change within a cycle.
    /// </remarks>
    [Fact]
    public void TheTimeFreeArmAnswersAtTimeZero()
    {
        var field = Create(
            [-35.0, 0.0, -100.0],
            [[new Term(0, 80.0, 0.3)], [new Term(0, -80.0, 0.3)], []],
            [FrequencyHz],
            [new RfWaveform.Sinusoid()]);

        foreach (var p in Points[1..])
        {
            Assert.Equal(field.ElectricFieldAt(in p, 0.0), field.ElectricFieldAt(in p));
            Assert.Equal(field.PotentialAt(in p, 0.0), field.PotentialAt(in p));

            // The control: at this point the field does move in time, so equality at zero is
            // a statement about the instant and not about a field that never changes.
            Assert.NotEqual(field.ElectricFieldAt(in p, 0.0), field.ElectricFieldAt(in p, 0.2e-6));
        }
    }

    // ------------------------------------------------------------------------------------
    // Superposition against the model path.
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// A 20 mm square at 0.5 mm with a plate and a rod; the plate's and the rod's potentials,
    /// and the rod's drive, substituted.
    /// </summary>
    private static string Model(string plate, string rod, string drive) => $$"""
        {
          "schemaVersion": "0.14",
          "name": "plate and rod",
          "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 10, "unit": "V" }
          },
          "fields": [
            { "type": "solved2d", "solve": {
                {{drive}}
                "minX": { "value": -10, "unit": "mm" }, "maxX": { "value": 10, "unit": "mm" },
                "minY": { "value": -10, "unit": "mm" }, "maxY": { "value": 10, "unit": "mm" },
                "cellSize": { "value": 0.5, "unit": "mm" },
                "electrodes": [
                  { "name": "plate", "shape": "rectangle",
                    "minX": { "value": -4, "unit": "mm" }, "maxX": { "value": 4, "unit": "mm" },
                    "minY": { "value": 2, "unit": "mm" }, "maxY": { "value": 3, "unit": "mm" },
                    {{plate}} },
                  { "name": "rod", "shape": "disc",
                    "centreX": { "value": 0, "unit": "mm" }, "centreY": { "value": -4, "unit": "mm" },
                    "radius": { "value": 1, "unit": "mm" },
                    {{rod}} }
                ] } }
          ],
          "detector": { "planePoint": { "value": [8, 0, 0], "unit": "mm" }, "normal": { "value": [-1, 0, 0] } },
          "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 10, "unit": "us" } }
        }
        """;

    private static string Volts(double value) =>
        $$"""{ "value": {{value.ToString(CultureInfo.InvariantCulture)}}, "unit": "V" }""";

    private static IElectrostaticField Build(string json)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(json));
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => $"{e.Code} {e.Path} {e.Constraint}")));
        return FieldAssembly.BuildReported(validation.Model!).Field;
    }

    /// <summary>
    /// Two static unit-potential basis models of one geometry, weighted through the factory
    /// with a driven model's voltages, give the field the model path builds for that driven
    /// model - to the solver's tolerance, since the two are different solves of one linear
    /// problem.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the claim a solve-once, fly-many host rests on: that its own basis split and
    /// <c>einzel run</c>'s channel decomposition are the same physics. A sinusoid reaches the
    /// model path's weights as two quadrature terms rather than as a phase, and a rectangular
    /// wave as the phase itself, so both are checked.
    /// </para>
    /// <para>
    /// Mutations in the factory's hand-over: dropping the phase, or swapping direct weight and
    /// amplitude, each move the potential by volts against an agreement of 3e-10 V (1e-11 of
    /// the field) measured on this geometry.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(WaveformNames))]
    public void BasisFieldsThroughTheFactoryAreTheDrivenModel(string waveform)
    {
        const double plateDc = 30.0, rodDc = -20.0, rodAmplitude = 50.0, phase = 0.3;
        var drive = $$"""
            "drive": { "frequency": { "value": 1.1, "unit": "MHz" }, "waveform": "{{waveform}}", "dutyCycle": 0.5 },
            """;

        var driven = (ITimeVaryingField)Build(Model(
            $"\"potential\": {Volts(plateDc)}",
            $"\"potential\": {Volts(rodDc)}, \"driveAmplitude\": {Volts(rodAmplitude)}, \"drivePhase\": {{ \"value\": {phase.ToString(CultureInfo.InvariantCulture)}, \"unit\": \"1\" }}",
            drive));

        IElectrostaticField[] bases =
        [
            Build(Model($"\"potential\": {Volts(1.0)}", $"\"potential\": {Volts(0.0)}", "")),
            Build(Model($"\"potential\": {Volts(0.0)}", $"\"potential\": {Volts(1.0)}", "")),
        ];

        var weighted = DrivenSolvedField.Create(
            bases,
            [plateDc, rodDc],
            [[new WeightTerm(0, 0.0, phase)], [new WeightTerm(0, rodAmplitude, phase)]],
            [1.1e6],
            [Waveforms[waveform]]);

        Vec3[] points =
        [
            new(0.0, 0.0, 0.0),
            new(0.0021, -0.0013, 0.0),
            new(-0.0057, 0.0061, 0.0),
            new(0.0033, -0.0071, 0.0),
            new(-0.0018, 0.0042, 0.0),
        ];

        var worstPotential = 0.0;
        var worstField = 0.0;
        var largestField = 0.0;

        foreach (var p in points)
        {
            foreach (var t in Times)
            {
                worstPotential = Math.Max(worstPotential, Math.Abs(weighted.PotentialAt(in p, t) - driven.PotentialAt(in p, t)));
                worstField = Math.Max(worstField, (weighted.ElectricFieldAt(in p, t) - driven.ElectricFieldAt(in p, t)).Length);
                largestField = Math.Max(largestField, driven.ElectricFieldAt(in p, t).Length);
            }
        }

        output.WriteLine(
            $"{waveform}: worst |dV| {worstPotential:E2} V, worst |dE| {worstField:E2} V/m "
            + $"against |E| up to {largestField:E2} V/m");

        // A 70 V drive on a 1e-12 solve: a sub-nanovolt disagreement is two solves of one
        // problem, and a dropped phase or swapped weight is tens of volts.
        Assert.True(worstPotential < 1e-6, $"potential disagrees by {worstPotential:E2} V");
        Assert.True(worstField < 1e-6 * largestField, $"field disagrees by {worstField:E2} V/m");
    }

    // ------------------------------------------------------------------------------------
    // Refusals.
    // ------------------------------------------------------------------------------------

    private sealed record Arguments(
        IReadOnlyList<IElectrostaticField> Channels,
        IReadOnlyList<double> Direct,
        IReadOnlyList<IReadOnlyList<WeightTerm>> Harmonics,
        IReadOnlyList<double> Frequencies,
        IReadOnlyList<RfWaveform> Waveforms)
    {
        public DrivenSolvedField Create() =>
            DrivenSolvedField.Create(Channels, Direct, Harmonics, Frequencies, Waveforms);
    }

    /// <summary>Two channels, one drive, every value legal.</summary>
    private static Arguments Legal() => new(
        Channels[..2],
        [10.0, -10.0],
        [[new WeightTerm(0, 50.0, 0.25)], [new WeightTerm(0, -50.0, 0.25)]],
        [FrequencyHz],
        [new RfWaveform.Sinusoid()]);

    private static readonly Dictionary<string, (Func<Arguments> Arguments, Type Exception, string Parameter)> Refusals = new()
    {
        ["channels null"] = (() => Legal() with { Channels = null! }, typeof(ArgumentNullException), "channels"),
        ["direct null"] = (() => Legal() with { Direct = null! }, typeof(ArgumentNullException), "direct"),
        ["harmonics null"] = (() => Legal() with { Harmonics = null! }, typeof(ArgumentNullException), "harmonics"),
        ["frequencies null"] = (() => Legal() with { Frequencies = null! }, typeof(ArgumentNullException), "frequenciesHz"),
        ["waveforms null"] = (() => Legal() with { Waveforms = null! }, typeof(ArgumentNullException), "waveforms"),
        ["no channels"] = (() => Legal() with { Channels = [], Direct = [], Harmonics = [] }, typeof(ArgumentException), "channels"),
        ["a channel null"] = (() => Legal() with { Channels = [Channels[0], null!] }, typeof(ArgumentException), "channels"),
        ["a channel time-varying"] = (() => Legal() with { Channels = [Channels[0], Legal().Create()] }, typeof(ArgumentException), "channels"),
        ["direct count"] = (() => Legal() with { Direct = [10.0] }, typeof(ArgumentException), "direct"),
        ["harmonics count"] = (() => Legal() with { Harmonics = [[]] }, typeof(ArgumentException), "harmonics"),
        ["a term list null"] = (() => Legal() with { Harmonics = [[], null!] }, typeof(ArgumentException), "harmonics"),
        ["waveform count"] = (() => Legal() with { Waveforms = [] }, typeof(ArgumentException), "waveforms"),
        ["a waveform null"] = (() => Legal() with { Waveforms = [null!] }, typeof(ArgumentException), "waveforms"),
        ["a term and no drive"] = (() => Legal() with { Frequencies = [], Waveforms = [] }, typeof(ArgumentException), "frequenciesHz"),
        ["drive negative"] = (() => Legal() with { Harmonics = [[], [new WeightTerm(-1, 5.0, 0.0)]] }, typeof(ArgumentOutOfRangeException), "harmonics"),
        ["drive past the last"] = (() => Legal() with { Harmonics = [[], [new WeightTerm(1, 5.0, 0.0)]] }, typeof(ArgumentOutOfRangeException), "harmonics"),
        ["direct NaN"] = (() => Legal() with { Direct = [10.0, double.NaN] }, typeof(ArgumentOutOfRangeException), "direct"),
        ["direct infinite"] = (() => Legal() with { Direct = [double.NegativeInfinity, 0.0] }, typeof(ArgumentOutOfRangeException), "direct"),
        ["amplitude NaN"] = (() => Legal() with { Harmonics = [[], [new WeightTerm(0, double.NaN, 0.0)]] }, typeof(ArgumentOutOfRangeException), "harmonics"),
        ["phase infinite"] = (() => Legal() with { Harmonics = [[new WeightTerm(0, 5.0, double.PositiveInfinity)], []] }, typeof(ArgumentOutOfRangeException), "harmonics"),
        ["frequency zero"] = (() => Legal() with { Frequencies = [0.0] }, typeof(ArgumentOutOfRangeException), "frequenciesHz"),
        ["frequency negative"] = (() => Legal() with { Frequencies = [-1.0e6] }, typeof(ArgumentOutOfRangeException), "frequenciesHz"),
        ["frequency NaN"] = (() => Legal() with { Frequencies = [double.NaN] }, typeof(ArgumentOutOfRangeException), "frequenciesHz"),
        ["frequency infinite"] = (() => Legal() with { Frequencies = [double.PositiveInfinity] }, typeof(ArgumentOutOfRangeException), "frequenciesHz"),
    };

    public static TheoryData<string> RefusalNames => [.. Refusals.Keys];

    /// <summary>
    /// Each malformed argument is refused with the exception its kind calls for, naming the
    /// argument at fault.
    /// </summary>
    /// <remarks>
    /// Mutation: removing any one check. A count or a null that slips through fails later and
    /// elsewhere (an index or null-reference exception, or none at all); a drive index out of
    /// range or a non-finite weight is accepted silently, and the row expecting the refusal
    /// fails.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RefusalNames))]
    public void AMalformedArgumentIsRefusedByName(string name)
    {
        var (arguments, exception, parameter) = Refusals[name];

        var thrown = Assert.Throws(exception, () => arguments().Create());
        var argument = Assert.IsAssignableFrom<ArgumentException>(thrown);

        output.WriteLine(argument.Message);

        Assert.Equal(parameter, argument.ParamName);
        Assert.Contains($"'{parameter}'", argument.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The controls for the refusals: the legal arguments build, phases outside [0, 1) are
    /// legal (a phase is in cycles, so -0.2 and 1.3 are ordinary), and a field with no drive at
    /// all - empty term lists, no frequencies, no waveforms - builds and is static.
    /// </summary>
    /// <remarks>Mutation: refusing a phase outside [0, 1) fails this control.</remarks>
    [Fact]
    public void LegalArgumentsAndAFieldWithNoDriveBuild()
    {
        var driven = Legal().Create();
        var outsideOneCycle = (Legal() with
        {
            Harmonics = [[new WeightTerm(0, 50.0, -0.2)], [new WeightTerm(0, -50.0, 1.3)]],
        }).Create();
        var undriven = DrivenSolvedField.Create(Channels[..2], [10.0, -10.0], [[], []], [], []);
        var p = Points[1];

        Assert.Equal(2, driven.ChannelCount);
        Assert.Equal(2, outsideOneCycle.ChannelCount);
        Assert.Equal(double.PositiveInfinity, undriven.ShortestPeriodSeconds);
        Assert.Equal(undriven.PotentialAt(in p, 0.0), undriven.PotentialAt(in p, 1.3e-6));
        Assert.Equal(
            (10.0 * Channels[0].PotentialAt(in p)) + (-10.0 * Channels[1].PotentialAt(in p)),
            undriven.PotentialAt(in p, 0.7e-6));
    }
}
