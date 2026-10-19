using Einzel.Commands;
using Einzel.Render;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The scale indicator: a ruler whose arms lie along the model's axes and are projected with
/// the picture's own matrix.
/// </summary>
/// <remarks>
/// <para>
/// <b>The expected lengths are arithmetic the ruler had no part in.</b> A picture's pixels per
/// millimeter follow from the framing's own rule - the box's half height, or half width over
/// the aspect, whichever binds, with the margin - and an arm's foreshortening in the angled
/// view follows from the two view angles alone. Asserting against a length computed from the
/// same matrix the ruler reads would check that the ruler agrees with itself.
/// </para>
/// </remarks>
public sealed class ScaleIndicatorTests(ITestOutputHelper output)
{
    private const int Width = 900;
    private const int Height = 560;

    /// <summary>A box a hundred millimeters long, twenty high and ten deep, centered on the origin.</summary>
    private static readonly (double X, double Y, double Z)[] Box =
    [
        (-50.0, -10.0, -5.0), (50.0, -10.0, -5.0), (-50.0, 10.0, -5.0), (50.0, 10.0, -5.0),
        (-50.0, -10.0, 5.0), (50.0, -10.0, 5.0), (-50.0, 10.0, 5.0), (50.0, 10.0, 5.0),
    ];

    /// <summary>
    /// In the side view the ruler is an L: an x arm to the right and a y arm upward, each the
    /// round length at the picture's own scale, and no z arm, because z points at the viewer.
    /// </summary>
    [Fact]
    public void InTheSideViewTheRulerIsAnLAtThePicturesOwnScale()
    {
        var ruler = For("side");

        // The framing's rule: 1.08 times whichever of the half height and the half width over
        // the aspect binds. Here the half width does: 50 / (900/560) = 31.1 against 10.
        var halfY = 1.08 * Math.Max(10.0, 50.0 / ((double)Width / Height));
        var pixelsPerMm = Height / (2.0 * halfY);

        // 0.16 of the smaller side is 89.6 px, or 10.75 mm at this scale, which rounds down
        // to 10.
        Assert.Equal(10.0, ruler.LengthMm, 12);
        Assert.Equal("10 mm", ruler.Label);

        Assert.Equal(["x", "y"], ruler.Arms.Select(a => a.Axis).ToArray());

        var x = Arm(ruler, "x");
        var y = Arm(ruler, "y");

        Assert.Equal(10.0 * pixelsPerMm, x.Dx, 9);
        Assert.Equal(0.0, x.Dy, 9);
        Assert.Equal(0.0, y.Dx, 9);
        Assert.Equal(-10.0 * pixelsPerMm, y.Dy, 9);   // up the picture is down its rows
    }

    /// <summary>
    /// In the angled view every arm is shortened by exactly the amount the view shortens that
    /// model axis - the thing a flat scale bar could not say.
    /// </summary>
    /// <remarks>
    /// Turning by the azimuth and tilting by the elevation carries a unit length along x to a
    /// screen length of sqrt(cos^2 a + sin^2 e sin^2 a), along y to cos e, and along z to
    /// sqrt(sin^2 a + sin^2 e cos^2 a).
    /// </remarks>
    [Fact]
    public void InTheAngledViewEachArmIsForeshortenedAsItsAxisIs()
    {
        var (azimuth, elevation) = ViewportPicture.Views["iso"];
        var ruler = For("iso");

        var a = azimuth * Math.PI / 180.0;
        var e = elevation * Math.PI / 180.0;

        var expected = new Dictionary<string, double>
        {
            ["x"] = Math.Sqrt((Math.Cos(a) * Math.Cos(a)) + (Math.Sin(e) * Math.Sin(e) * Math.Sin(a) * Math.Sin(a))),
            ["y"] = Math.Cos(e),
            ["z"] = Math.Sqrt((Math.Sin(a) * Math.Sin(a)) + (Math.Sin(e) * Math.Sin(e) * Math.Cos(a) * Math.Cos(a))),
        };

        Assert.Equal(["x", "y", "z"], ruler.Arms.Select(arm => arm.Axis).ToArray());

        var scale = PixelsPerMm("iso");

        foreach (var (axis, factor) in expected)
        {
            var arm = Arm(ruler, axis);
            var length = Math.Sqrt((arm.Dx * arm.Dx) + (arm.Dy * arm.Dy));

            Assert.Equal(factor, length / (ruler.LengthMm * scale), 9);
        }
    }

    /// <summary>
    /// Whichever way the arms point, the whole ruler - arms, letters and label - is inside the
    /// picture and clear of the band a tainted picture carries along its bottom.
    /// </summary>
    /// <param name="view">A named view.</param>
    /// <remarks>
    /// The front view is the one that needed it: +z runs to the LEFT there, so a ruler anchored
    /// at the point its arms meet would draw that arm off the picture.
    /// </remarks>
    [Theory]
    [InlineData("iso")]
    [InlineData("side")]
    [InlineData("top")]
    [InlineData("front")]
    public void TheRulerIsInsideThePictureAndAboveTheBand(string view)
    {
        const double Band = 40.0;
        var (azimuth, elevation) = ViewportPicture.Views[view];
        var matrix = Framing.Around(Box, azimuth, elevation).Matrix((double)Width / Height);
        var ruler = ScaleIndicator.For(matrix, Width, Height, 14.0, Band)!;

        var points = new List<(double X, double Y)> { ruler.OriginPx, ruler.LabelPx };
        points.AddRange(ruler.Arms.Select(arm => arm.TipPx));
        points.AddRange(ruler.Arms.Select(arm => arm.LetterPx));

        foreach (var (x, y) in points)
        {
            Assert.InRange(x, 0.0, Width);
            Assert.InRange(y, 0.0, Height - Band);
        }

        if (view == "front")
        {
            // The case the layout exists for: an arm pointing left, kept on the picture.
            Assert.True(Arm(ruler, "z").Dx < 0.0, "+z runs left in the front view");
        }
    }

    /// <summary>In each straight-on view, the axis pointing at the viewer has no arm.</summary>
    /// <param name="view">A named view.</param>
    /// <param name="hidden">The axis it looks down.</param>
    [Theory]
    [InlineData("side", "z")]
    [InlineData("top", "y")]
    [InlineData("front", "x")]
    public void TheAxisPointingAtTheViewerHasNoArm(string view, string hidden)
    {
        var ruler = For(view);

        Assert.Equal(2, ruler.Arms.Count);
        Assert.DoesNotContain(ruler.Arms, arm => arm.Axis == hidden);
    }

    /// <summary>A ruler is a round length: 1, 2 or 5 times a power of ten, never above what was asked.</summary>
    /// <param name="asked">The length the room allows, in millimeters.</param>
    /// <param name="round">The round length expected.</param>
    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(1.99, 1.0)]
    [InlineData(2.0, 2.0)]
    [InlineData(4.99, 2.0)]
    [InlineData(5.0, 5.0)]
    [InlineData(9.99, 5.0)]
    [InlineData(10.0, 10.0)]
    [InlineData(1000.0, 1000.0)]
    [InlineData(0.0023, 0.002)]
    [InlineData(199.99999999999997, 200.0)]
    [InlineData(0.30000000000000004, 0.2)]
    public void ARulerIsARoundLength(double asked, double round) =>
        Assert.Equal(round, ScaleIndicator.RoundDown(asked), 12);

    /// <summary>A length is written in the unit that keeps its number small.</summary>
    /// <param name="mm">The length, in millimeters.</param>
    /// <param name="written">How it reads.</param>
    [Theory]
    [InlineData(200.0, "200 mm")]
    [InlineData(1.0, "1 mm")]
    [InlineData(0.5, "500 µm")]
    [InlineData(0.002, "2 µm")]
    [InlineData(5e-4, "500 nm")]
    [InlineData(2000.0, "2 m")]
    public void ALengthIsWrittenInTheUnitThatKeepsItSmall(double mm, string written) =>
        Assert.Equal(written, ScaleIndicator.Format(mm));

    /// <summary>
    /// The raster's line font can spell every label the ruler can produce, from nanometers to
    /// kilometers, and every axis letter.
    /// </summary>
    /// <remarks>
    /// The font draws an unknown character as an empty box rather than skipping it, so a gap
    /// would at least be visible - but a label with a box in it is still a broken label, and
    /// this is where it would be caught.
    /// </remarks>
    [Fact]
    public void TheStillsFontSpellsEveryLabelTheRulerCanWrite()
    {
        var labels = new List<string> { "x", "y", "z" };

        for (var exponent = -6; exponent <= 6; exponent++)
        {
            foreach (var step in (double[])[1.0, 2.0, 5.0])
            {
                labels.Add(ScaleIndicator.Format(step * Math.Pow(10.0, exponent)));
            }
        }

        foreach (var label in labels)
        {
            Assert.All(label, c => Assert.True(StrokeFont.Supports(c), $"'{c}' in \"{label}\" has no glyph"));
        }
    }

    /// <summary>
    /// The ruler takes the corner the drawn instrument covers least, and lower left only when
    /// that is as good as any.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Found by review: the camera fits the instrument to the picture with only a few percent to
    /// spare, so a ruler always put in the lower left landed on an electrode in a plate-filling
    /// view while the documentation said it never would. In the side view the screen's axes are
    /// the model's, so a slab is placed over chosen quarters of the picture directly.
    /// </para>
    /// <para>
    /// The control is the same picture with nothing in it, which must keep the old lower left -
    /// and a slab covering the whole picture, where every corner is covered alike and the ruler
    /// is drawn over the instrument in the lower left: it can only be as clear as the picture
    /// allows.
    /// </para>
    /// </remarks>
    /// <param name="lowX">The slab's least x, in millimeters.</param>
    /// <param name="lowY">The slab's least y, in millimeters.</param>
    /// <param name="highX">The slab's greatest x, in millimeters.</param>
    /// <param name="highY">The slab's greatest y, in millimeters.</param>
    /// <param name="corner">Where the ruler should go.</param>
    [Theory]
    [InlineData(0.0, 0.0, 0.0, 0.0, "lower left")]
    [InlineData(-100.0, -100.0, 0.0, 0.0, "lower right")]
    [InlineData(-100.0, -100.0, 100.0, 0.0, "upper left")]
    [InlineData(-100.0, -100.0, 100.0, 100.0, "lower left")]
    public void TheRulerTakesTheCornerTheInstrumentCoversLeast(
        double lowX, double lowY, double highX, double highY, string corner)
    {
        var (azimuth, elevation) = ViewportPicture.Views["side"];
        var matrix = Framing.Around(Box, azimuth, elevation).Matrix((double)Width / Height);

        PictureMesh[] meshes = highX > lowX
            ? [new PictureMesh(
                [lowX, lowY, 0.0, highX, lowY, 0.0, highX, highY, 0.0, lowX, highY, 0.0],
                [0.0, 0.0, 1.0, 0.0, 0.0, 1.0, 0.0, 0.0, 1.0, 0.0, 0.0, 1.0],
                [0, 1, 2, 0, 2, 3],
                0.5, 0.5, 0.5)]
            : [];

        PictureLayer[] picture = [new PictureLayer("conductors", meshes, [], 1.0, WritesDepth: true)];

        var ruler = ScaleIndicator.For(matrix, Width, Height, 14.0, 0.0, picture)!;

        output.WriteLine($"slab x {lowX} to {highX}, y {lowY} to {highY} mm: ruler in the {ruler.Corner}");

        Assert.Equal(corner, ruler.Corner);

        // Where a corner was clear, the ruler is clear of the slab - every point of it outside
        // the slab's own rectangle on screen.
        if (highX > lowX && !(highX >= 100.0 && highY >= 100.0))
        {
            var (x0, y0) = Pixel(matrix, lowX, highY);
            var (x1, y1) = Pixel(matrix, highX, lowY);

            foreach (var (x, y) in Points(ruler))
            {
                Assert.False(
                    x > x0 && x < x1 && y > y0 && y < y1,
                    $"the ruler in the {ruler.Corner} reaches ({x:F0}, {y:F0}), inside the slab");
            }
        }
    }

    /// <summary>
    /// At every supported size and in every named view, the ruler - arms, letters and label -
    /// is wholly inside the picture and above the hatched band, or it is not drawn at all.
    /// </summary>
    /// <remarks>
    /// Found by review: at 32 pixels a side the minimum text height and margin put the origin
    /// four pixels above the picture, both arms clipped away, while the still's JSON still
    /// reported a ruler. Sixteen pixels is the smallest still the verb accepts.
    /// </remarks>
    /// <param name="view">A named view.</param>
    /// <param name="size">The picture's width and height, in pixels.</param>
    [Theory]
    [InlineData("side", 16)]
    [InlineData("side", 32)]
    [InlineData("side", 64)]
    [InlineData("side", 128)]
    [InlineData("iso", 16)]
    [InlineData("iso", 48)]
    [InlineData("iso", 96)]
    [InlineData("front", 24)]
    [InlineData("front", 80)]
    [InlineData("top", 40)]
    [InlineData("top", 200)]
    public void ARulerThatDoesNotFitIsNotDrawn(string view, int size)
    {
        var (azimuth, elevation) = ViewportPicture.Views[view];
        var matrix = Framing.Around(Box, azimuth, elevation).Matrix(1.0);
        var band = Rasterizer.HatchHeight(size);

        var ruler = ScaleIndicator.For(matrix, size, size, ScaleIndicator.TextHeightFor(size, size), band);

        output.WriteLine($"{view} at {size} px: {(ruler is null ? "no ruler" : $"{ruler.Label} in the {ruler.Corner}")}");

        if (ruler is null)
        {
            return;
        }

        foreach (var (x, y) in Points(ruler))
        {
            Assert.InRange(x, 0.0, size);
            Assert.InRange(y, 0.0, size - band);
        }
    }

    /// <summary>The review's own case: a 32-pixel side view has no room for a ruler, so has none.</summary>
    [Fact]
    public void AThirtyTwoPixelPictureHasNoRuler()
    {
        var (azimuth, elevation) = ViewportPicture.Views["side"];
        var matrix = Framing.Around(Box, azimuth, elevation).Matrix(1.0);

        Assert.Null(ScaleIndicator.For(matrix, 32, 32, ScaleIndicator.TextHeightFor(32, 32)));
        Assert.NotNull(ScaleIndicator.For(matrix, Width, Height, ScaleIndicator.TextHeightFor(Width, Height)));
    }

    /// <summary>A projection with no finite scale gets no ruler rather than a ruler of infinity.</summary>
    [Fact]
    public void AProjectionWithNoScaleGetsNoRuler()
    {
        Assert.Null(ScaleIndicator.For(new double[16], Width, Height));
        Assert.Null(ScaleIndicator.For(Framing.Identity().Select(v => (double)v).ToArray(), 0.0, Height));
    }

    private static ScaleIndicator For(string view)
    {
        var (azimuth, elevation) = ViewportPicture.Views[view];
        var matrix = Framing.Around(Box, azimuth, elevation).Matrix((double)Width / Height);

        return ScaleIndicator.For(matrix, Width, Height)!;
    }

    /// <summary>Pixels per millimeter in the plane of the screen, by the framing's own rule.</summary>
    private static double PixelsPerMm(string view)
    {
        var (azimuth, elevation) = ViewportPicture.Views[view];
        var framing = Framing.Around(Box, azimuth, elevation);
        var halfY = 1.08 * Math.Max(framing.HalfHeightMm, framing.HalfWidthMm / ((double)Width / Height));

        return Height / (2.0 * halfY);
    }

    /// <summary>Where a model point in the z = 0 plane lands, in pixels from the top left.</summary>
    private static (double X, double Y) Pixel(double[] m, double x, double y) =>
        (((m[0] * x) + (m[4] * y) + m[12] + 1.0) * 0.5 * Width,
         (1.0 - ((m[1] * x) + (m[5] * y) + m[13])) * 0.5 * Height);

    /// <summary>
    /// Every point the ruler reaches: where the arms meet, their tips, the extent of each letter,
    /// and the corners of the label as the still's font sets it.
    /// </summary>
    private static List<(double X, double Y)> Points(ScaleIndicator ruler)
    {
        var points = new List<(double X, double Y)> { ruler.OriginPx };
        var letter = 0.4 * ruler.TextPx;

        foreach (var arm in ruler.Arms)
        {
            points.Add(arm.TipPx);
            points.Add((arm.LetterPx.X - letter, arm.LetterPx.Y - letter));
            points.Add((arm.LetterPx.X + letter, arm.LetterPx.Y + letter));
        }

        var labelHalf = 0.5 * StrokeFont.Width(ruler.Label, ruler.TextPx);
        points.Add((ruler.LabelPx.X - labelHalf, ruler.LabelPx.Y - (0.5 * ruler.TextPx)));
        points.Add((ruler.LabelPx.X + labelHalf, ruler.LabelPx.Y + (0.5 * ruler.TextPx)));

        return points;
    }

    private static (double Dx, double Dy) Arm(ScaleIndicator ruler, string axis)
    {
        var arm = ruler.Arms.Single(a => a.Axis == axis);

        return (arm.TipPx.X - ruler.OriginPx.X, arm.TipPx.Y - ruler.OriginPx.Y);
    }
}
