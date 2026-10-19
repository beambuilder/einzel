using Einzel.Render;

namespace Einzel.Render.Tests;

/// <summary>
/// The line font a still's labels are drawn in, and the overlay that draws them over the
/// picture.
/// </summary>
public sealed class StrokeFontTests
{
    private static readonly (double R, double G, double B) White = (1.0, 1.0, 1.0);

    private static readonly RasterLighting Lit = new((_, _, _) => 1.0, 1.0);

    /// <summary>A line of text is laid out centered on the point it is given, at the height it is given.</summary>
    [Fact]
    public void TextIsCenteredWhereItIsPutAndAsTallAsAsked()
    {
        var strokes = StrokeFont.Layout("200", 100.0, 50.0, 12.0);

        var xs = strokes.SelectMany(s => s.Where((_, i) => i % 2 == 0)).ToList();
        var ys = strokes.SelectMany(s => s.Where((_, i) => i % 2 == 1)).ToList();

        // Digits span the full height, so their box is exactly the requested one.
        Assert.Equal(100.0, (xs.Min() + xs.Max()) / 2.0, 9);
        Assert.Equal(50.0, (ys.Min() + ys.Max()) / 2.0, 9);
        Assert.Equal(12.0, ys.Max() - ys.Min(), 9);
        Assert.Equal(StrokeFont.Width("200", 12.0), xs.Max() - xs.Min(), 9);
    }

    /// <summary>Width is proportional to height, so a label keeps its shape at any size.</summary>
    [Fact]
    public void WidthScalesWithHeight() =>
        Assert.Equal(2.0 * StrokeFont.Width("500 µm", 10.0), StrokeFont.Width("500 µm", 20.0), 12);

    /// <summary>
    /// A character the font cannot spell is drawn as a box, not left out - so a label with a
    /// missing glyph shows a gap rather than reading as a shorter, different length.
    /// </summary>
    [Fact]
    public void AnUnknownCharacterIsABoxNotAGap()
    {
        Assert.False(StrokeFont.Supports('Q'));

        var box = StrokeFont.Layout("Q", 0.0, 0.0, 6.0);

        Assert.Single(box);
        Assert.Equal(StrokeFont.Width("0", 6.0), StrokeFont.Width("Q", 6.0), 12);
    }

    /// <summary>
    /// The overlay is drawn over the scene and hidden by nothing: an annotation is not part of
    /// the instrument.
    /// </summary>
    /// <remarks>
    /// The square is opaque, writes depth and sits nearest the camera, so anything tested
    /// against depth would lose to it. The overlay's stroke crosses it and must still be there.
    /// </remarks>
    [Fact]
    public void TheOverlayIsDrawnOverTheSceneAndHiddenByNothing()
    {
        var square = new RasterMesh(
            [-10, -10, 5, 10, -10, 5, 10, 10, 5, -10, 10, 5],
            [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1],
            [0, 1, 2, 0, 2, 3],
            1.0, 0.0, 0.0);

        var overlay = new RasterOverlay(
            [new OverlayStroke([0.0, 20.0, 40.0, 20.0], 0.0, 0.0, 1.0)],
            [],
            2.0);

        var rgb = Rasterizer.Draw(
            [new RasterLayer([square], [], 1.0, true)],
            Orthographic(10.0),
            Lit,
            White,
            40,
            40,
            supersample: 1,
            overlay: overlay);

        Assert.Equal((0, 0, 255), At(rgb, 40, 20, 20));   // on the stroke, over the square
        Assert.Equal((255, 0, 0), At(rgb, 40, 20, 10));   // the square, away from the stroke
    }

    /// <summary>Text in the overlay puts ink where the label is and nowhere else.</summary>
    [Fact]
    public void OverlayTextInksItsOwnBox()
    {
        var overlay = new RasterOverlay([], [new OverlayText("10 mm", 50.0, 20.0, 12.0, 0.0, 0.0, 0.0)], 1.5);

        var rgb = Rasterizer.Draw([], Orthographic(1.0), Lit, White, 100, 40, overlay: overlay);

        var half = StrokeFont.Width("10 mm", 12.0) / 2.0;
        int inked = 0, stray = 0;

        for (var y = 0; y < 40; y++)
        {
            for (var x = 0; x < 100; x++)
            {
                if (At(rgb, 100, x, y) == (255, 255, 255))
                {
                    continue;
                }

                var inside = x >= 50 - half - 2 && x <= 50 + half + 2 && y >= 12 && y <= 28;

                inked += inside ? 1 : 0;
                stray += inside ? 0 : 1;
            }
        }

        Assert.True(inked > 40, $"only {inked} pixels of the label were inked");
        Assert.Equal(0, stray);
    }

    private static double[] Orthographic(double half) =>
    [
        1.0 / half, 0, 0, 0,
        0, 1.0 / half, 0, 0,
        0, 0, -1.0 / half, 0,
        0, 0, 0, 1,
    ];

    private static (int R, int G, int B) At(byte[] rgb, int width, int x, int y)
    {
        var i = 3 * ((y * width) + x);
        return (rgb[i], rgb[i + 1], rgb[i + 2]);
    }
}
