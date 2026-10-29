using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields.Analytic;
using Einzel.Fields.Solved;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// The edge schedule of a staged solved field is the union over its stages, built once.
/// </summary>
/// <remarks>
/// A tap whose amplitude is exactly zero in one stage and not in the next declares its edges
/// from the start, held or ramped: the schedule is decided on the compiled terms, so a zero
/// lands on nothing and any other amplitude on every edge, and it is not narrowed to the
/// stage in force (a landing where nothing jumps costs a step and changes no answer).
/// </remarks>
public sealed class StagedEdgeScheduleTests(ITestOutputHelper output)
{
    private static CompiledElectrode Plate(params CompiledTap[] taps) =>
        new()
        {
            Name = "plate", Shape = ElectrodeShape.Rectangle,
            MinX = -4.0e-3, MinY = -1.0e-3, MaxX = 4.0e-3, MaxY = 1.0e-3,
            Potential = 0.0, Taps = taps,
        };

    private static DrivenSolvedField Build(CompiledStage[] stages)
    {
        var (field, _) = GeometryBuilder.Build(new CompiledSolvedField
        {
            MinX = -10.0e-3, MinY = -10.0e-3, MaxX = 10.0e-3, MaxY = 10.0e-3,
            CellSize = 0.5e-3, Tolerance = 1e-10,
            Electrodes = [stages[0].Electrodes[0]],
            Drives = [new CompiledDrive(1.0e6, DriveWaveform.Rectangular, 0.3)],
            Stages = stages,
        });

        return Assert.IsType<DrivenSolvedField>(field);
    }

    private static CompiledTap Tap(double amplitude) => new(0, amplitude, 0.0);

    private sealed class Uniform : IElectrostaticField
    {
        public double PotentialAt(in Vec3 position) => -position.X;

        public Vec3 ElectricFieldAt(in Vec3 position) => Vec3.UnitX;
    }

    /// <summary>
    /// The constructor's own contract, below the builder: the builder's base terms already
    /// carry every pattern a stage uses, so only this shows the stages are read at all.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheScheduleReadsEveryStageAndRampEnd(bool ramped)
    {
        WeightTerm Term(double amplitude) => new(0, amplitude, 0.25);
        IReadOnlyList<IReadOnlyList<WeightTerm>> Channel(double amplitude) => [[Term(amplitude)]];

        var field = new DrivenSolvedField(
            [new Uniform()], [0.0], Channel(0.0), [1.0e6], [new RfWaveform.Rectangular(0.3)],
            [10.0e-6, 20.0e-6],
            [[0.0], [0.0]],
            [Channel(0.0), Channel(ramped ? 0.0 : 100.0)],
            ramped ? [null, [0.0]] : null,
            ramped ? [null, Channel(100.0)] : null);

        // Phase 0.25 puts the falling edge 0.05 us into each period.
        var first = field.NextEdgeAfter(0.0);
        output.WriteLine($"ramped {ramped}: first edge {first:R} s");
        Assert.True(Math.Abs(first - 0.05e-6) <= 1.0e-21, $"first edge {first:R} s");
    }

    /// <summary>
    /// With stages every instant is evaluated on a stage's terms, so a base tap that every
    /// stage holds at zero is never read and lands on nothing.
    /// </summary>
    [Fact]
    public void ABaseTapEveryStageSilencesDeclaresNothing()
    {
        IReadOnlyList<IReadOnlyList<WeightTerm>> Channel(double amplitude) => [[new WeightTerm(0, amplitude, 0.25)]];

        var field = new DrivenSolvedField(
            [new Uniform()], [0.0], Channel(100.0), [1.0e6], [new RfWaveform.Rectangular(0.3)],
            [10.0e-6, 20.0e-6],
            [[0.0], [0.0]],
            [Channel(0.0), Channel(0.0)]);

        Assert.Equal(double.PositiveInfinity, field.NextEdgeAfter(0.0));
        Assert.Equal(0.0, field.WeightAt(0, 5.0e-6));
        Assert.Equal(0.0, field.WeightAt(0, 25.0e-6));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATapThatStartsInALaterStageDeclaresItsEdgesFromTheStart(bool ramped)
    {
        var silent = new CompiledStage("silent", 10.0e-6, [Plate(Tap(0.0))]);
        var loud = ramped
            ? new CompiledStage("loud", 10.0e-6, [Plate(Tap(0.0))]) { EndElectrodes = [Plate(Tap(100.0))] }
            : new CompiledStage("loud", 10.0e-6, [Plate(Tap(100.0))]);

        var field = Build([silent, loud]);
        var first = field.NextEdgeAfter(0.0);
        var later = field.NextEdgeAfter(12.0e-6);

        output.WriteLine($"ramped {ramped}: first edge {first:R} s, after 12 us {later:R} s");
        Assert.True(Math.Abs(first - 0.3e-6) <= 1.0e-21, $"first edge {first:R} s");
        Assert.True(Math.Abs(later - 12.3e-6) <= 1.0e-20, $"edge after 12 us {later:R} s");

        if (!ramped)
        {
            var probe = new Vec3(0.0, 2.0e-3, 0.0);
            Assert.True(
                field.PotentialAt(probe, Math.BitDecrement(later)) * field.PotentialAt(probe, later) < 0.0,
                "the held stage's tap flips at the instant reported");
        }
    }

    [Fact]
    public void ATapSilentInEveryStageDeclaresNothing()
    {
        var field = Build(
        [
            new CompiledStage("one", 10.0e-6, [Plate(Tap(0.0))]),
            new CompiledStage("two", 10.0e-6, [Plate(Tap(0.0))]),
        ]);

        Assert.Equal(double.PositiveInfinity, field.NextEdgeAfter(0.0));
    }
}
