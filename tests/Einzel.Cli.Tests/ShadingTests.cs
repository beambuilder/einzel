using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>What color an electrode is drawn in, checked without a viewport.</summary>
public sealed class ShadingTests(ITestOutputHelper output)
{
    /// <summary>
    /// Antiphase rods land at opposite ends of the ramp, which is the one thing a driven
    /// structure's drawing is read for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the defect, and it is a regression the port introduced.</b> The first
    /// version took <c>Math.Abs</c> of the drive amplitude and recovered its sign from the DC
    /// potential - which is exactly zero for a purely driven electrode, so the ternary
    /// substituted +1 and every rod of a quadrupole came back at +500 V and the same
    /// saturated red. The WPF shell this was ported from computed
    /// <c>PotentialVolts + DriveAmplitudeVolts</c>, which is correct, so the regression was
    /// introduced in carrying it across.
    /// </para>
    /// <para>
    /// The assertion is that the two are on OPPOSITE SIDES of earth rather than merely
    /// unequal: a version that scaled them differently but kept both positive would pass
    /// "not equal" and still draw a quadrupole as four rods doing the same thing.
    /// </para>
    /// </remarks>
    [Fact]
    public void AntiphaseDrivenRodsAreDrawnAtOppositeEnds()
    {
        // A quadrupole as shipped: no DC at all, and the pairing entirely in the drive.
        ConductorSurface[] rods =
        [
            Rod("rodXPlus", dc: 0.0, drive: +500.0),
            Rod("rodXMinus", dc: 0.0, drive: +500.0),
            Rod("rodYPlus", dc: 0.0, drive: -500.0),
            Rod("rodYMinus", dc: 0.0, drive: -500.0),
        ];

        var span = Shading.Span(rods);
        var fractions = rods.Select(rod => Shading.Fraction(rod, span)).ToArray();

        output.WriteLine($"span {span:F1} V; fractions "
            + string.Join(", ", rods.Zip(fractions, (rod, f) => $"{rod.Name} {f:F3}")));

        Assert.Equal(1.0, fractions[0], 12);
        Assert.Equal(1.0, fractions[1], 12);
        Assert.Equal(0.0, fractions[2], 12);
        Assert.Equal(0.0, fractions[3], 12);
    }

    /// <summary>Earth is drawn as earth, whatever else is in the scene.</summary>
    /// <remarks>
    /// The neutral is the value a reader looks for first, so it must land at the middle of
    /// the ramp rather than at the middle of the observed range - a lens holding 0 and 500 V
    /// would otherwise paint its earthed tube a quarter of the way along.
    /// </remarks>
    [Fact]
    public void AnEarthedElectrodeSitsAtTheNeutral()
    {
        ConductorSurface[] lens =
        [
            Rod("entrance", dc: 0.0, drive: 0.0),
            Rod("centre", dc: 500.0, drive: 0.0),
            Rod("exit", dc: 0.0, drive: 0.0),
        ];

        var span = Shading.Span(lens);

        Assert.Equal(0.5, Shading.Fraction(lens[0], span), 12);
        Assert.Equal(1.0, Shading.Fraction(lens[1], span), 12);
        Assert.Equal(0.5, Shading.Fraction(lens[2], span), 12);
    }

    /// <summary>A DC offset and a drive on one electrode both count.</summary>
    /// <remarks>
    /// A funnel's rings carry a DC chain and an RF tap at once, so neither alone describes
    /// what the ring reaches - and the span has to be taken over the same sum, or the
    /// electrode that set the scale would be clamped by it.
    /// </remarks>
    [Fact]
    public void ADcChainAndADriveAreBothOnTheScale()
    {
        ConductorSurface[] rings =
        [
            Rod("ring-0", dc: 30.0, drive: +200.0),
            Rod("ring-1", dc: 30.0, drive: -200.0),
        ];

        var span = Shading.Span(rings);

        Assert.Equal(230.0, Shading.Peak(rings[0]), 12);
        Assert.Equal(-170.0, Shading.Peak(rings[1]), 12);
        Assert.Equal(230.0, span, 12);

        // Both inside the scale, and on opposite sides of the neutral, because the chain
        // does not lift the antiphase ring above earth.
        Assert.True(Shading.Fraction(rings[0], span) > 0.5);
        Assert.True(Shading.Fraction(rings[1], span) < 0.5);
    }

    /// <summary>A scene with nothing in it gives a usable scale rather than a division.</summary>
    [Fact]
    public void AnEmptySceneStillHasASpan()
    {
        Assert.Equal(1.0, Shading.Span([]), 12);

        // And a scene of nothing but earth, where the widest excursion really is zero.
        Assert.Equal(1.0, Shading.Span([Rod("housing", 0.0, 0.0)]), 12);
        Assert.Equal(0.5, Shading.Fraction(Rod("housing", 0.0, 0.0), 0.0), 12);
    }

    private static ConductorSurface Rod(string name, double dc, double drive) =>
        new(name, dc, drive, VerticesMm: [], Normals: [], Triangles: []);
}
