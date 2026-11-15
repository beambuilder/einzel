using Einzel.Commands;
using Einzel.Core.Results;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The two colour scales every picture is drawn with, and the degenerate range that would
/// paint a bundle NaN (§16).
/// </summary>
/// <remarks>
/// <para>
/// <b>These came across when the WPF shell was deleted, and finding that they had to is the
/// point.</b> `ColorRamp` is what the cross-platform viewport and every `einzel render still`
/// colour from, and it had no tests at all: the only ones were in `Einzel.Wpf.Tests`, against
/// the shell's own copy of the same table. The copy's doc comments cite measured contrast
/// figures that nothing checked, and the live one would have kept those comments with the
/// tests gone.
/// </para>
/// <para>
/// The two tables were identical in value when they were separated, so these pass unchanged
/// against this one - which is what says the ramp the window draws with is the ramp that was
/// measured.
/// </para>
/// </remarks>
public sealed class ColorRampTests(ITestOutputHelper output)
{
    /// <summary>Every colour on both ramps is legible against the viewport's ground.</summary>
    /// <remarks>
    /// <para>
    /// <b>The property that was missing, and it is not a style preference.</b> Viridis spans
    /// dark to light by construction, so on a dark ground its low end is a line you cannot
    /// see - and the ions it hides are the slow ones at a turning point, which are the
    /// interesting ones. Measured across grounds from #101010 to #D0D0D0 the worst contrast
    /// anywhere on the unlifted ramp never rises above 1.25, so <em>no</em> background is the
    /// fix; lifting the ramp off the ground is.
    /// </para>
    /// <para>
    /// Asserted as a WCAG contrast ratio because that is a published measure of exactly this -
    /// whether two colours are distinguishable - rather than a threshold invented to fit. 3.0
    /// is the large-text floor, which is the right comparison for a line.
    /// </para>
    /// </remarks>
    [Fact]
    public void BothRampsAreLegibleAgainstTheViewportGround()
    {
        // The viewport's own background, read from where it is declared rather than restated
        // here. A copy would let the ground move without this test noticing, which is exactly
        // the pairing the test exists to enforce.
        var ground = ColorRamp.Ground;

        static double Channel(double v) =>
            v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

        static double Relative((double R, double G, double B) c) =>
            (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));

        double Contrast((double R, double G, double B) c)
        {
            var (high, low) = (Relative(c), Relative(ground));

            (high, low) = high >= low ? (high, low) : (low, high);

            return (high + 0.05) / (low + 0.05);
        }

        var worstEnergy = double.MaxValue;
        var worstPotential = double.MaxValue;

        for (var i = 0; i <= 100; i++)
        {
            worstEnergy = Math.Min(worstEnergy, Contrast(ColorRamp.At(i / 100.0)));
            worstPotential = Math.Min(worstPotential, Contrast(ColorRamp.Diverging(i / 100.0)));
        }

        output.WriteLine($"worst contrast on the energy ramp    {worstEnergy:F2}");
        output.WriteLine($"worst contrast on the potential ramp {worstPotential:F2}");

        Assert.True(worstEnergy > 3.0, $"the energy ramp reaches {worstEnergy:F2}");
        Assert.True(worstPotential > 3.0, $"the potential ramp reaches {worstPotential:F2}");
    }

    /// <summary>The ramp is ordered and stays inside the unit cube.</summary>
    /// <remarks>
    /// <para>
    /// <b>Monotone lightness is the property the choice was made for.</b> A rainbow ramp - the
    /// default almost everywhere - has non-monotone lightness, so it invents boundaries where
    /// the data is smooth and collapses under the commonest colour vision deficiencies.
    /// Asserting it here is what stops the ramp being "improved" into one.
    /// </para>
    /// <para>
    /// Luminance by the Rec. 709 weights, which is what an eye does rather than what a mean of
    /// the channels does.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRampRisesInLightnessThroughout()
    {
        var previous = double.NegativeInfinity;

        for (var i = 0; i <= 64; i++)
        {
            var (r, g, b) = ColorRamp.At(i / 64.0);

            Assert.All(new[] { r, g, b }, c => Assert.InRange(c, 0.0, 1.0));

            var luminance = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);

            Assert.True(
                luminance > previous,
                $"lightness fell at {i / 64.0:F3}: {luminance:F4} after {previous:F4}");

            previous = luminance;
        }

        output.WriteLine($"lightness rises to {previous:F4} over 65 samples");

        // Out of range is clamped rather than refused: the scale is a display decision and a
        // value past its end is still a value to draw.
        Assert.Equal(ColorRamp.At(0.0), ColorRamp.At(-5.0));
        Assert.Equal(ColorRamp.At(1.0), ColorRamp.At(5.0));
    }

    /// <summary>A bundle of one energy is drawn in a colour, not divided by nothing (§16).</summary>
    /// <remarks>
    /// <para>
    /// A packet whose ions all carry the same energy has a range of zero width, and that is
    /// not an exotic case: it is a monoenergetic beam in a field-free drift, the simplest
    /// model anyone writes. A scale that divided by the width would paint the whole bundle
    /// NaN, which is the same family as the four non-finite doubles that took the JSON surface
    /// down one at a time.
    /// </para>
    /// <para>
    /// <b>Half rather than zero or one</b>, because there is no top or bottom of a scale with
    /// no width, and either end would read as a statement about the energy that is not being
    /// made. The control is a bundle that does span a range, whose two paths must then differ
    /// - without it this passes against a composition that paints everything one colour.
    /// </para>
    /// </remarks>
    [Fact]
    public void ABundleOfOneEnergyIsDrawnInAColour()
    {
        var flat = Composed(Scene(4000.0, 4000.0));

        Assert.Equal(2, flat.Count);

        foreach (var line in flat)
        {
            Assert.All(
                new[] { line.R, line.G, line.B },
                c => Assert.True(double.IsFinite(c), "a degenerate range painted the bundle NaN"));
        }

        var (r, g, b) = ColorRamp.At(0.5);

        output.WriteLine($"one-energy bundle drawn at ({flat[0].R:F3}, {flat[0].G:F3}, {flat[0].B:F3})");

        Assert.Equal((r, g, b), (flat[0].R, flat[0].G, flat[0].B));
        Assert.Equal((flat[0].R, flat[0].G, flat[0].B), (flat[1].R, flat[1].G, flat[1].B));

        // The control: a real range colours its two ends differently, so the equality above is
        // the degenerate rule rather than a composition that cannot tell paths apart.
        var spread = Composed(Scene(1000.0, 4000.0));

        Assert.NotEqual((spread[0].R, spread[0].G, spread[0].B), (spread[1].R, spread[1].G, spread[1].B));
    }

    private static IReadOnlyList<PictureLine> Composed(ViewportOutcome scene) =>
        ViewportPicture.Compose(scene).Single(layer => layer.Name == "paths").Lines;

    /// <summary>Two straight paths, one at each energy, and nothing else drawn.</summary>
    private static ViewportOutcome Scene(double lowEv, double highEv) =>
        new(
            ModelPath: "probe.json",
            Trajectories: [Path(lowEv), Path(highEv)],
            ProducesTrajectories: true,
            LowestEnergyEv: lowEv,
            HighestEnergyEv: highEv,
            Conductors: [],
            Equipotentials: [],
            LowestPotentialVolts: null,
            HighestPotentialVolts: null,
            Density: [],
            PeakDensityPerCubicMetre: null,
            DensityAtUs: null,
            Ends: null,
            Warnings: Array.Empty<ValidityWarning>());

    private static TrajectoryPath Path(double energyEv) =>
        new([[0.0, 0.0, 0.0], [10.0, 0.0, 0.0]], [energyEv, energyEv], "arrived");
}
