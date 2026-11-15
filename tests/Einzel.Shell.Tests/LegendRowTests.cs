using Avalonia.Media;

using Einzel.Commands;
using Einzel.Shell;

using Xunit.Abstractions;

namespace Einzel.Shell.Tests;

/// <summary>
/// The legend's rows as a person reads them, checked without a window.
/// </summary>
/// <remarks>
/// What each row says is <see cref="ViewportPicture.Scales"/>'s decision and is checked
/// where that lives. What is left here is turning one into a label, two numbers and a
/// swatch — which sounds like nothing and is where the one defect a live window found was.
/// </remarks>
public sealed class LegendRowTests(ITestOutputHelper output)
{
    /// <summary>A legend number is never written in exponent form.</summary>
    /// <remarks>
    /// <para>
    /// <b>Found by opening the window, not by a test.</b> The obvious spelling is a general
    /// format at three significant figures, and it writes a one-kilovolt beam's energy as
    /// <c>1E+03</c> — which is correct, is what the first version shipped, and is nothing a
    /// person reads a scale by. The shipped einzel lens is exactly that case.
    /// </para>
    /// <para>
    /// The small values are here because a fixed format would print them as zero, and a
    /// legend whose two ends both read <c>0</c> says the picture has no range at all.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(1000.0, "1000")]
    [InlineData(-7360.0, "-7360")]
    [InlineData(514.0, "514")]
    [InlineData(0.0, "0")]
    [InlineData(2.5, "2.50")]
    // Not a midpoint: .NET rounds those to even, so -0.125 gives -0.12 and the case would
    // be about the rounding mode rather than about the format.
    [InlineData(-0.126, "-0.13")]
    public void ANumberOnAScaleIsWrittenOutRatherThanInExponentForm(double value, string shown)
    {
        var row = new LegendRow(new ColorScale("energy", "eV", value, value, Diverging: false));

        output.WriteLine($"{value} -> {row.High}");

        // Equal ends are one value, stated once, on the right.
        Assert.Equal(shown + " eV", row.High);
        Assert.DoesNotContain("E", row.High.Replace("eV", string.Empty), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A value too small for a fixed format still carries its magnitude.</summary>
    /// <remarks>
    /// The one place an exponent is right: <c>0.00</c> for a hundredth of a volt would be a
    /// scale claiming no range, which is a worse answer than a hard-to-read one.
    /// </remarks>
    [Fact]
    public void AVerySmallValueKeepsItsMagnitude()
    {
        var row = new LegendRow(new ColorScale("potential", "V", -1.2e-5, 1.2e-5, Diverging: true));

        output.WriteLine($"{row.Low} to {row.High}");

        Assert.NotEqual("0", row.Low);
        Assert.NotEqual("0.00", row.Low);
        Assert.NotEqual(row.Low, row.High.Replace(" V", string.Empty));
    }

    /// <summary>A single-value scale is one value on one swatch color, not a gradient.</summary>
    [Fact]
    public void ASingleValueIsOneSolidSwatch()
    {
        var row = new LegendRow(new ColorScale("energy", "eV", 4000.0, 4000.0, Diverging: false));

        Assert.Equal(string.Empty, row.Low);
        Assert.Equal("4000 eV", row.High);

        var solid = Assert.IsType<SolidColorBrush>(row.Swatch);
        var (r, g, b) = ColorRamp.At(0.5);

        Assert.Equal(Color.FromRgb((byte)Math.Round(r * 255.0), (byte)Math.Round(g * 255.0), (byte)Math.Round(b * 255.0)), solid.Color);
    }

    /// <summary>Ends that differ by less than a unit still print differently.</summary>
    [Fact]
    public void NearlyEqualEndsStayDistinguishable()
    {
        var row = new LegendRow(new ColorScale("energy", "eV", 4000.0, 4000.05, Diverging: false));

        Assert.Equal("4000", row.Low);
        Assert.Equal("4000.05 eV", row.High);
    }

    /// <summary>The legend is empty for no picture, so it cannot outlive one.</summary>
    [Fact]
    public void NoPictureHasNoLegendRows() => Assert.Empty(LegendRow.RowsFor(null));

    /// <summary>The unit travels with the row, on the end a reader finishes at.</summary>
    /// <remarks>
    /// GRD-1's rule in the one place the API cannot enforce it: a swatch is a quantity drawn
    /// as a colour, and a number beside it with no unit is exactly the bare scalar the rest
    /// of this surface refuses to hand out.
    /// </remarks>
    [Fact]
    public void TheUnitIsOnTheRow()
    {
        var row = new LegendRow(new ColorScale("energy", "eV", 900.0, 2000.0, Diverging: false));

        Assert.Equal("900", row.Low);
        Assert.Equal("2000 eV", row.High);
        Assert.Equal("energy", row.Quantity);
    }

    /// <summary>The swatch is the ramp itself, not a fade between its ends.</summary>
    /// <remarks>
    /// <para>
    /// <b>Viridis is not linear between its ends, and that is the whole reason it is used.</b>
    /// Its lightness rises monotonically where a two-stop gradient from dark blue to yellow
    /// does not, so a legend drawn from the ends alone would show a ramp the picture is not
    /// painted with — and would show it directly beside the picture.
    /// </para>
    /// <para>
    /// The assertion is that a stop in the middle is <b>far</b> from the average of the ends,
    /// rather than merely different: a two-stop gradient sampled anywhere lands exactly on
    /// that average, so any real distance is the whole finding.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSwatchSamplesTheRampRatherThanFadingBetweenItsEnds()
    {
        var row = new LegendRow(new ColorScale("energy", "eV", 0.0, 1.0, Diverging: false));

        var brush = Assert.IsType<LinearGradientBrush>(row.Swatch);

        Assert.True(brush.GradientStops.Count > 2, $"{brush.GradientStops.Count} stops");

        var first = brush.GradientStops[0].Color;
        var last = brush.GradientStops[^1].Color;
        var middle = brush.GradientStops.MinBy(s => Math.Abs(s.Offset - 0.5))!.Color;

        var faded = (
            R: (first.R + last.R) / 2.0,
            G: (first.G + last.G) / 2.0,
            B: (first.B + last.B) / 2.0);

        var apart = Math.Sqrt(
            Math.Pow(middle.R - faded.R, 2.0)
            + Math.Pow(middle.G - faded.G, 2.0)
            + Math.Pow(middle.B - faded.B, 2.0));

        output.WriteLine($"middle {middle} against a two-stop fade {faded}: {apart:F1} apart");

        Assert.True(apart > 40.0, $"the middle stop is only {apart:F1} from a plain fade");
    }

    /// <summary>The two ramps are different ramps.</summary>
    /// <remarks>
    /// The control for the rest: a row that ignored <see cref="ColorScale.Diverging"/> would
    /// pass every assertion above and paint a potential scale in viridis, so an earthed
    /// electrode would be drawn in the colour the legend gives to the middle of nothing.
    /// </remarks>
    [Fact]
    public void ADivergingScaleGetsTheDivergingRamp()
    {
        var sequential = Middle(new LegendRow(new ColorScale("energy", "eV", 0.0, 1.0, false)));
        var diverging = Middle(new LegendRow(new ColorScale("potential", "V", -1.0, 1.0, true)));

        output.WriteLine($"sequential {sequential}, diverging {diverging}");

        Assert.NotEqual(sequential, diverging);
    }

    private static Color Middle(LegendRow row)
    {
        var brush = (LinearGradientBrush)row.Swatch;

        return brush.GradientStops.MinBy(s => Math.Abs(s.Offset - 0.5))!.Color;
    }
}
