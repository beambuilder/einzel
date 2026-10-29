using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields.Analytic;
using Einzel.Fields.Solved;

namespace Einzel.Fields.Tests;

/// <summary>
/// <see cref="RfWaveform.ConstantBetweenBreakpoints"/> per shape and
/// <see cref="ITimeVaryingField.ShortestSmoothPeriodSeconds"/>: the period the integrator caps on
/// when it lands on edges leaves out every drive that holds still between its edges, and
/// every wrapper forwards it.
/// </summary>
public sealed class SmoothPeriodTests
{
    private const double Megahertz = 1.0e6;

    private sealed class Uniform : IElectrostaticField
    {
        public double PotentialAt(in Vec3 position) => -position.X;

        public Vec3 ElectricFieldAt(in Vec3 position) => Vec3.UnitX;
    }

    private static readonly RfWaveform Square = new RfWaveform.Rectangular(0.3);

    private static readonly RfWaveform Comb = new RfWaveform.Harmonic([new HarmonicTerm(1, 1.0, 0.0), new HarmonicTerm(3, 0.2, 0.0)]);

    /// <summary>One channel per drive, each at amplitude one.</summary>
    private static DrivenSolvedField Solved(double[] frequencies, RfWaveform[] waveforms) =>
        DrivenSolvedField.Create(
            [.. frequencies.Select(_ => (IElectrostaticField)new Uniform())],
            [.. frequencies.Select(_ => 0.0)],
            [.. frequencies.Select((_, k) => (IReadOnlyList<WeightTerm>)[new WeightTerm(k, 1.0, 0.0)])],
            frequencies,
            waveforms);

    private static IdealQuadrupoleRf Quadrupole(RfWaveform waveform) => IdealQuadrupoleRf.FromMathieu(
        0.0, 0.5, Quantity.From(100.0, "u"), Quantity.From(1.0, "e"),
        Quantity.From(1.0, "MHz"), Quantity.From(4.0, "mm"), waveform);

    private static OscillatingUniformField Uniformly(RfWaveform waveform) =>
        OscillatingUniformField.Create(Vec3.UnitX, Quantity.From(1.0, "MHz"), waveform);

    [Fact]
    public void OnlyTheRectangularWaveIsConstantBetweenItsBreakpoints()
    {
        Assert.True(Square.ConstantBetweenBreakpoints);
        Assert.False(new RfWaveform.Sinusoid().ConstantBetweenBreakpoints);
        Assert.False(Comb.ConstantBetweenBreakpoints);
        Assert.False(new RfWaveform.Analytic("1 - 4*abs(p - 0.5)", new Dictionary<string, Quantity>(StringComparer.Ordinal), [0.0, 0.5])
            .ConstantBetweenBreakpoints);
        Assert.False(new RfWaveform.Tabulated([0, 0.2, 0.5, 0.7], [1, -0.4, -1, 0.3]).ConstantBetweenBreakpoints);
    }

    [Fact]
    public void ARectangularDriveAloneHasNoSmoothPeriod()
    {
        ITimeVaryingField[] fields = [Solved([Megahertz], [Square]), Quadrupole(Square), Uniformly(Square)];

        foreach (var field in fields)
        {
            Assert.Equal(double.PositiveInfinity, field.ShortestSmoothPeriodSeconds);
            Assert.True(double.IsFinite(field.ShortestPeriodSeconds), field.GetType().Name);
        }
    }

    [Fact]
    public void ASmoothDriveKeepsItsPeriodAndHarmonicOrders()
    {
        Assert.Equal(1.0 / (3.0 * Megahertz), Solved([Megahertz], [Comb]).ShortestSmoothPeriodSeconds);
        Assert.Equal(1.0 / (3.0 * Megahertz), Uniformly(Comb).ShortestSmoothPeriodSeconds);

        // The quadrupole's period ignores harmonic orders; its smooth period follows it.
        var quadrupole = Quadrupole(Comb);
        Assert.Equal(quadrupole.ShortestPeriodSeconds, quadrupole.ShortestSmoothPeriodSeconds);
    }

    [Fact]
    public void ARectangularAndASinusoidalDriveGiveTheSinusoidsPeriod()
    {
        var field = Solved([3.0 * Megahertz, Megahertz], [Square, new RfWaveform.Sinusoid()]);

        Assert.Equal(1.0 / (3.0 * Megahertz), field.ShortestPeriodSeconds);
        Assert.Equal(1.0 / Megahertz, field.ShortestSmoothPeriodSeconds);
    }

    [Fact]
    public void EveryWrapperForwardsTheSmoothPeriod()
    {
        var square = Quadrupole(Square);
        var sine = Uniformly(new RfWaveform.Sinusoid());

        Assert.Equal(double.PositiveInfinity, new TimeShiftedField(square, 1.0e-7).ShortestSmoothPeriodSeconds);
        Assert.Equal(1.0e-6, new TimeShiftedField(sine, 1.0e-7).ShortestSmoothPeriodSeconds);
        Assert.Equal(
            double.PositiveInfinity,
            Assert.IsAssignableFrom<ITimeVaryingField>(BoundedField.Around(square, Box())).ShortestSmoothPeriodSeconds);
        Assert.Equal(
            1.0e-6,
            Assert.IsAssignableFrom<ITimeVaryingField>(BoundedField.Around(sine, Box())).ShortestSmoothPeriodSeconds);

        Assert.Equal(double.PositiveInfinity, new DrivenSuperposedField([square, FieldFreeSpace.Instance]).ShortestSmoothPeriodSeconds);
        Assert.Equal(1.0e-6, new DrivenSuperposedField([square, sine]).ShortestSmoothPeriodSeconds);

        // Over every state, not the one in force: the flight starts rectangular and switches.
        Assert.Equal(double.PositiveInfinity, new SequencedField([square, FieldFreeSpace.Instance], [1.0e-6, 2.0e-6]).ShortestSmoothPeriodSeconds);
        Assert.Equal(1.0e-6, new SequencedField([square, sine], [1.0e-6, 2.0e-6]).ShortestSmoothPeriodSeconds);
    }

    private static FieldRegion Box() => new(-0.010, 0.010, -0.010, 0.010, -0.010, 0.010, 0.001);
}
