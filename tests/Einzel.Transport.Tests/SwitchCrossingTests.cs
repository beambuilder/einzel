using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Transport;
using Einzel.Transport.Integration;

using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// Localising a step-size underflow at a sequencer switch.
/// </summary>
/// <remarks>
/// <para>
/// A pulsed-extraction model - plates at zero for a hold, then at voltage - underflows at
/// exactly the switch instant, with the ion wherever it had got to. It is independent of
/// the ion's speed: at rest, creeping and moving all fail the same way at the same time.
/// </para>
/// <para>
/// <c>SequencerTests</c> crosses a plus-and-minus 200 V switch without trouble, and
/// differs from the failing case in two ways at once: it builds the field straight from
/// <c>GeometryBuilder</c> rather than through <c>FieldAssembly</c>, and it integrates to a
/// flight-time ceiling with no stopping surface. These tests separate the two.
/// </para>
/// </remarks>
public sealed class SwitchCrossingTests(ITestOutputHelper output)
{
    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    private static CompiledElectrode Plate(double volts) => new()
    {
        Name = "pusher",
        Shape = ElectrodeShape.Rectangle,
        MinX = -0.02,
        MinY = -0.006,
        MaxX = 0.02,
        MaxY = -0.005,
        Potential = volts,
    };

    private static CompiledSolvedField Geometry(double volts, IReadOnlyList<CompiledStage> stages) =>
        new()
        {
            MinX = -0.03,
            MinY = -0.016,
            MaxX = 0.03,
            MaxY = 0.016,
            CellSize = 0.001,
            Tolerance = 1e-10,
            Electrodes = [Plate(volts)],
            Stages = stages,
        };

    /// <summary>Crossing a switch with no stopping surface, as SequencerTests does.</summary>
    [Fact]
    public void ASwitchIsCrossedWithNoStoppingSurface()
    {
        var run = Fly(stop: null, seconds: 4e-6);

        output.WriteLine($"{run.Outcome} after {run.AcceptedSteps} steps");

        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, run.Outcome);
    }

    /// <summary>
    /// The same crossing, with a stopping surface the ion has not yet reached.
    /// </summary>
    /// <remarks>
    /// If this underflows and the one above does not, the stopping surface is implicated
    /// and the sequencer itself is not - which would make the failing model's detector,
    /// rather than its stages, the thing to look at.
    /// </remarks>
    [Fact]
    public void ASwitchIsCrossedWithAStoppingSurfaceAhead()
    {
        // A plane well past anywhere the ion reaches in four microseconds, so it is
        // present and never met.
        var run = Fly(
            stop: (in PhaseState s) => 0.5 - s.Position.Y,
            seconds: 4e-6);

        output.WriteLine($"{run.Outcome} after {run.AcceptedSteps} steps");

        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, run.Outcome);
    }

    /// <summary>
    /// The same crossing with a facing pair held at plus and minus half the voltage.
    /// </summary>
    /// <remarks>
    /// The next narrowest difference between the failing model and the passing tests
    /// above. Two electrodes that are exact negatives share one basis solve, so the
    /// channel decomposition has a different shape here than it does for a single plate -
    /// and in the first stage both of them are at zero, which is a pattern of nothing.
    /// </remarks>
    [Fact]
    public void ASwitchIsCrossedByAFacingPair()
    {
        var solve = new CompiledSolvedField
        {
            MinX = -0.03,
            MinY = -0.016,
            MaxX = 0.03,
            MaxY = 0.016,
            CellSize = 0.001,
            Tolerance = 1e-10,
            Electrodes = [Pair(0.0, "lower"), Pair(0.0, "upper")],
            Stages =
            [
                new CompiledStage("hold", 1e-6, [Pair(0.0, "lower"), Pair(0.0, "upper")]),
                new CompiledStage(
                    "push", 3e-6, [Pair(100.0, "lower"), Pair(-100.0, "upper")]),
            ],
        };

        var field = GeometryBuilder.Build(solve).Field;
        var run = TrajectoryIntegrator.Integrate(
            new PhaseState(Vec3.Zero, Vec3.Zero),
            Peptide,
            field,
            new IntegrationSettings { RelativeTolerance = 1e-11, MaximumFlightTime = 4e-6 });
        var cap = TrajectoryIntegrator.Integrate(
            new PhaseState(Vec3.Zero, Vec3.Zero),
            Peptide,
            field,
            new IntegrationSettings { RelativeTolerance = 1e-11, MaximumFlightTime = 4e-6, LandOnEdges = false });

        output.WriteLine($"{run.Outcome} after {run.AcceptedSteps} steps at {run.FlightTimeSeconds:R} s; cap {cap.Outcome} after {cap.AcceptedSteps} steps at {cap.FlightTimeSeconds:R} s");

        // It crosses the switch and is then pushed into the upper plate, which is what a
        // pair at plus and minus a hundred volts does to an ion on the axis between them.
        // What is asserted is the crossing, not the ending: this test exists to eliminate
        // the facing pair as the cause of the underflow, and StruckElectrode after 47
        // steps under the default (112 under cap, whose derivative carried across the
        // switch costs rejections and regrowth) eliminates it as thoroughly as reaching the
        // ceiling would. Both rules strike the same plate at the same instant.
        Assert.NotEqual(TrajectoryOutcome.StepSizeUnderflow, run.Outcome);
        Assert.True(run.AcceptedSteps > 20, $"only {run.AcceptedSteps} steps");
        Assert.Equal(cap.Outcome, run.Outcome);
        Assert.Equal(cap.FlightTimeSeconds, run.FlightTimeSeconds, cap.FlightTimeSeconds * 1e-6);
    }

    private static CompiledElectrode Pair(double volts, string name) => new()
    {
        Name = name,
        Shape = ElectrodeShape.Rectangle,
        MinX = -0.02,
        MinY = name == "lower" ? -0.006 : 0.005,
        MaxX = 0.02,
        MaxY = name == "lower" ? -0.005 : 0.006,
        Potential = volts,
    };

    private static TrajectoryResult Fly(TrajectoryStopFunction? stop, double seconds)
    {
        // Zero for the first microsecond, then 200 V - the shape the failing model has,
        // rather than the plus-and-minus reversal SequencerTests uses.
        var solve = Geometry(
            0.0,
            [
                new CompiledStage("hold", 1e-6, [Plate(0.0)]),
                new CompiledStage("push", 3e-6, [Plate(200.0)]),
            ]);

        return TrajectoryIntegrator.Integrate(
            new PhaseState(Vec3.Zero, Vec3.Zero),
            Peptide,
            GeometryBuilder.Build(solve).Field,
            new IntegrationSettings { RelativeTolerance = 1e-11, MaximumFlightTime = seconds },
            stop);
    }
}
