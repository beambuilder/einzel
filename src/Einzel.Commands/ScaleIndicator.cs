using System.Globalization;

using Einzel.Render;

namespace Einzel.Commands;

/// <summary>One arm of a scale indicator.</summary>
/// <param name="Axis">The model axis the arm lies along: <c>x</c>, <c>y</c> or <c>z</c>.</param>
/// <param name="TipPx">Where the arm ends, in pixels from the picture's top left.</param>
/// <param name="LetterPx">Where the axis letter is centered, just beyond the tip.</param>
public sealed record ScaleArm(string Axis, (double X, double Y) TipPx, (double X, double Y) LetterPx);

/// <summary>
/// A ruler in the corner of a picture: one arm along each model axis, all the same round
/// length, drawn with the picture's own projection.
/// </summary>
/// <param name="OriginPx">Where the arms meet, in pixels from the picture's top left.</param>
/// <param name="Arms">The arms that are drawn; an arm pointing at the viewer is left out.</param>
/// <param name="LengthMm">The true length of every arm, in millimeters.</param>
/// <param name="Label">That length as a reader sees it, with its unit.</param>
/// <param name="LabelPx">Where the label is centered.</param>
/// <param name="TextPx">The text height the layout was made for, in pixels.</param>
/// <param name="Corner">Which corner it sits in: <c>lower left</c>, <c>lower right</c>,
/// <c>upper left</c> or <c>upper right</c>.</param>
/// <remarks>
/// <para>
/// <b>A ruler in the scene rather than a bar on the screen, and the angled view is why.</b> In
/// the side, top and front views the camera looks straight down a model axis, so a flat bar is
/// exact for every length on the page. In the iso view each model axis is foreshortened by a
/// different amount - at the shipped angles x by 0.87, y by 0.91 and z by 0.63 - so a flat bar
/// would state one scale for three. Arms along the model's own axes, projected by the same
/// matrix as the electrodes, shorten exactly as the instrument does, and in a named view the
/// same construction is an ordinary L-shaped scale bar.
/// </para>
/// <para>
/// <b>In a corner, which the projection allows, and the corner the instrument covers least.</b>
/// The camera is orthographic, so a segment along a model axis has the same length on screen
/// wherever it is placed, and the ruler can sit away from the instrument without being any less
/// true. But the camera fits the instrument to the picture with only a few percent to spare, so
/// no corner is clear by construction - a plate seen face-on fills all four. So each corner is
/// scored by how much of the drawn instrument it would cover, and the least-covered one wins,
/// lower left on a tie. <b>Where every corner is covered the ruler is drawn over the
/// instrument</b>, as an annotation on top of it: it is not guaranteed to stay clear, only to
/// stay as clear as the picture allows.
/// </para>
/// <para>
/// <b>Left out when it does not fit.</b> A ruler whose arms, letters and label cannot all sit
/// inside the picture (above a tainted picture's hatched band) is not laid out at all, rather
/// than drawn partly off the edge; a thumbnail a few dozen pixels across gets no ruler.
/// </para>
/// <para>
/// <b>One length for every arm.</b> Per-axis lengths would carry more information and read
/// worse: arms of 200, 10 and 200 mm side by side invite the reader to compare arms that are
/// not comparable. With one length, the arms' different screen lengths <em>are</em> the
/// foreshortening, which is what an angled view hides.
/// </para>
/// <para>
/// <b>Decided here, for the window and for <c>einzel render still</c> alike</b>, like every
/// other decision about the picture (<see cref="ViewportPicture"/>): the round length, which
/// arms show and where they sit. Each consumer only turns lines and text into its own pixels.
/// </para>
/// </remarks>
public sealed record ScaleIndicator(
    (double X, double Y) OriginPx,
    IReadOnlyList<ScaleArm> Arms,
    double LengthMm,
    string Label,
    (double X, double Y) LabelPx,
    double TextPx,
    string Corner)
{
    /// <summary>The color the ruler and its labels are drawn in: a near-black that reads on white.</summary>
    public static readonly (double R, double G, double B) Ink = (0.20, 0.20, 0.22);

    /// <summary>The width of an arm, in pixels: the width the picture's own lines are drawn at.</summary>
    public const double LineWidthPx = 1.5;

    /// <summary>How long the longest arm may be, as a fraction of the picture's smaller side.</summary>
    public const double TargetFraction = 0.16;

    /// <summary>
    /// How short an arm may be, against the longest, before it is taken to point at the viewer
    /// and left out.
    /// </summary>
    /// <remarks>
    /// A quarter. Straight down an axis the arm is exactly zero and drawing a dot labeled with
    /// a length would be a claim about nothing; at the shipped iso angles the shortest arm is
    /// 0.69 of the longest, so every arm shows there.
    /// </remarks>
    public const double HiddenBelow = 0.25;

    /// <summary>The label height a picture of this size gets.</summary>
    /// <param name="widthPx">The picture's width, in pixels.</param>
    /// <param name="heightPx">The picture's height, in pixels.</param>
    /// <returns>2.6 percent of the smaller side, and never under 12 pixels.</returns>
    /// <remarks>
    /// One rule for the window and the still, so a still of a window-sized picture carries the
    /// same label the window does.
    /// </remarks>
    public static double TextHeightFor(double widthPx, double heightPx) =>
        Math.Max(12.0, Math.Round(0.026 * Math.Min(widthPx, heightPx)));

    /// <summary>Lays out a scale indicator for a picture.</summary>
    /// <param name="matrix">Column-major 4x4 from millimeters to clip space, as the picture is drawn with.</param>
    /// <param name="widthPx">The picture's width, in pixels.</param>
    /// <param name="heightPx">The picture's height, in pixels.</param>
    /// <param name="textPx">The height of the label text, in pixels.</param>
    /// <param name="reserveBottomPx">Space at the bottom edge the ruler must stay clear of.</param>
    /// <param name="picture">
    /// The picture the ruler is drawn over, to choose the corner it covers least of; null
    /// puts it in the lower left.
    /// </param>
    /// <returns>
    /// The indicator, or null where the projection has no finite scale or the ruler does not fit
    /// inside the picture.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="matrix"/> is null.</exception>
    /// <exception cref="ArgumentException">The matrix is not 4x4.</exception>
    /// <remarks>
    /// The matrix alone decides the scale, because it is the thing the picture was drawn with:
    /// a ruler computed from anything else would be a second account of the camera, and the
    /// two would disagree the first time the framing changed.
    /// </remarks>
    public static ScaleIndicator? For(
        IReadOnlyList<double> matrix,
        double widthPx,
        double heightPx,
        double textPx = 12.0,
        double reserveBottomPx = 0.0,
        IReadOnlyList<PictureLayer>? picture = null)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        if (matrix.Count != 16)
        {
            throw new ArgumentException("a projection is a 4x4 matrix", nameof(matrix));
        }

        if (!(widthPx > 0.0) || !(heightPx > 0.0))
        {
            return null;
        }

        // Screen pixels per millimeter along each model axis: clip x spans the width over
        // two units, and clip y runs up where a picture's rows run down.
        (string Axis, double X, double Y)[] perMm =
        [
            ("x", matrix[0] * widthPx / 2.0, -matrix[1] * heightPx / 2.0),
            ("y", matrix[4] * widthPx / 2.0, -matrix[5] * heightPx / 2.0),
            ("z", matrix[8] * widthPx / 2.0, -matrix[9] * heightPx / 2.0),
        ];

        var longest = perMm.Max(a => Math.Sqrt((a.X * a.X) + (a.Y * a.Y)));

        if (!(longest > 0.0) || !double.IsFinite(longest))
        {
            return null;
        }

        var lengthMm = RoundDown(TargetFraction * Math.Min(widthPx, heightPx) / longest);

        var shown = perMm
            .Where(a => Math.Sqrt((a.X * a.X) + (a.Y * a.Y)) >= HiddenBelow * longest)
            .Select(a => (a.Axis, X: a.X * lengthMm, Y: a.Y * lengthMm))
            .ToList();

        // Where each axis letter sits, just beyond its tip along the arm, relative to where
        // the arms meet.
        var letterPx = 0.8 * textPx;
        var letters = shown
            .Select(a =>
            {
                var length = Math.Sqrt((a.X * a.X) + (a.Y * a.Y));
                var reach = length > 0.0 ? (length + letterPx) / length : 1.0;

                return (X: a.X * reach, Y: a.Y * reach);
            })
            .ToList();

        // The ruler's own box relative to where the arms meet - letters and label included - so
        // the whole of it can be put in a corner whichever way the arms point: in the front view
        // +z runs to the left, and a ruler anchored at its origin would draw that arm off the
        // picture. The letters count because in the angled view the x arm dips below the origin,
        // and a label placed under the arms alone sat beside that arm's letter and read as
        // "10 mm x". The label's width is the still's own font's, which is the wider of the two
        // consumers' fonts.
        var half = 0.5 * letterPx;
        var left = Math.Min(Math.Min(0.0, shown.Min(a => a.X)), letters.Min(l => l.X) - half);
        var right = Math.Max(Math.Max(0.0, shown.Max(a => a.X)), letters.Max(l => l.X) + half);
        var top = Math.Min(Math.Min(0.0, shown.Min(a => a.Y)), letters.Min(l => l.Y) - half);
        var bottom = Math.Max(Math.Max(0.0, shown.Max(a => a.Y)), letters.Max(l => l.Y) + half);

        var label = Format(lengthMm);
        var center = (left + right) / 2.0;
        var labelHalf = 0.5 * StrokeFont.Width(label, textPx);
        var labelY = bottom + (0.9 * textPx);

        var boxLeft = Math.Min(left, center - labelHalf);
        var boxRight = Math.Max(right, center + labelHalf);
        var boxBottom = bottom + (1.6 * textPx);

        var margin = Math.Max(12.0, 0.04 * Math.Min(widthPx, heightPx));
        var usable = heightPx - reserveBottomPx;

        // Every corner holds the same box, so one test settles whether any of them can.
        if ((boxRight - boxLeft) + (2.0 * margin) > widthPx || (boxBottom - top) + (2.0 * margin) > usable)
        {
            return null;
        }

        // Where the arms meet for each corner, in the order a tie goes to.
        (string Corner, double X, double Y)[] corners =
        [
            ("lower left", margin - boxLeft, usable - margin - boxBottom),
            ("lower right", widthPx - margin - boxRight, usable - margin - boxBottom),
            ("upper left", margin - boxLeft, margin - top),
            ("upper right", widthPx - margin - boxRight, margin - top),
        ];

        var best = 0;

        if (picture is not null)
        {
            var covered = Covered(
                picture,
                matrix,
                widthPx,
                heightPx,
                [.. corners.Select(c => (c.X + boxLeft, c.Y + top, c.X + boxRight, c.Y + boxBottom))]);

            // Strictly less, so a tie keeps the earlier corner.
            for (var i = 1; i < corners.Length; i++)
            {
                if (covered[i] < covered[best])
                {
                    best = i;
                }
            }
        }

        var chosen = corners[best];
        var (originX, originY) = (chosen.X, chosen.Y);

        var arms = shown
            .Zip(letters, (a, l) => new ScaleArm(
                a.Axis,
                (originX + a.X, originY + a.Y),
                (originX + l.X, originY + l.Y)))
            .ToList();

        return new ScaleIndicator(
            (originX, originY),
            arms,
            lengthMm,
            label,
            (originX + center, originY + labelY),
            textPx,
            chosen.Corner);
    }

    /// <summary>How much of the drawn instrument each candidate box would cover, in square pixels.</summary>
    /// <remarks>
    /// <para>
    /// <b>Triangle by triangle and segment by segment</b>, each by its own bounding box on screen.
    /// A box per mesh would be far too generous for a curved electrode or a long diagonal flight,
    /// whose box covers the picture while the thing itself covers a strip of it; a box per
    /// triangle overstates by at most a factor of two and costs one pass over the vertices.
    /// </para>
    /// <para>
    /// <b>The field is left out.</b> The equipotentials are drawn over the whole region the
    /// field was sampled on, so every corner is covered by them and avoiding them would choose
    /// nothing; they are context for the instrument rather than part of it.
    /// </para>
    /// </remarks>
    private static double[] Covered(
        IReadOnlyList<PictureLayer> picture,
        IReadOnlyList<double> m,
        double widthPx,
        double heightPx,
        (double Left, double Top, double Right, double Bottom)[] boxes)
    {
        var covered = new double[boxes.Length];

        (double X, double Y) Screen(IReadOnlyList<double> points, int i) =>
            (((m[0] * points[i]) + (m[4] * points[i + 1]) + (m[8] * points[i + 2]) + m[12] + 1.0) * 0.5 * widthPx,
             (1.0 - ((m[1] * points[i]) + (m[5] * points[i + 1]) + (m[9] * points[i + 2]) + m[13])) * 0.5 * heightPx);

        void Add(double minX, double minY, double maxX, double maxY)
        {
            for (var b = 0; b < boxes.Length; b++)
            {
                var w = Math.Min(maxX, boxes[b].Right) - Math.Max(minX, boxes[b].Left);
                var h = Math.Min(maxY, boxes[b].Bottom) - Math.Max(minY, boxes[b].Top);

                if (w > 0.0 && h > 0.0)
                {
                    covered[b] += w * h;
                }
            }
        }

        foreach (var layer in picture)
        {
            if (layer.Name == "field")
            {
                continue;
            }

            foreach (var mesh in layer.Meshes)
            {
                var screen = new (double X, double Y)[mesh.VerticesMm.Count / 3];

                for (var v = 0; v < screen.Length; v++)
                {
                    screen[v] = Screen(mesh.VerticesMm, 3 * v);
                }

                for (var t = 0; t + 2 < mesh.Triangles.Count; t += 3)
                {
                    var (a, b, c) = (screen[mesh.Triangles[t]], screen[mesh.Triangles[t + 1]], screen[mesh.Triangles[t + 2]]);

                    Add(
                        Math.Min(a.X, Math.Min(b.X, c.X)),
                        Math.Min(a.Y, Math.Min(b.Y, c.Y)),
                        Math.Max(a.X, Math.Max(b.X, c.X)),
                        Math.Max(a.Y, Math.Max(b.Y, c.Y)));
                }
            }

            // A line is drawn a line-width wide, so a segment along a pixel row still covers it.
            var halfWidth = 0.5 * LineWidthPx;

            foreach (var line in layer.Lines)
            {
                for (var i = 0; i + 5 < line.PointsMm.Count; i += 3)
                {
                    var (a, b) = (Screen(line.PointsMm, i), Screen(line.PointsMm, i + 3));

                    Add(
                        Math.Min(a.X, b.X) - halfWidth,
                        Math.Min(a.Y, b.Y) - halfWidth,
                        Math.Max(a.X, b.X) + halfWidth,
                        Math.Max(a.Y, b.Y) + halfWidth);
                }
            }
        }

        return covered;
    }

    /// <summary>The largest 1, 2 or 5 times a power of ten not above a length.</summary>
    /// <param name="mm">The length, in millimeters; positive.</param>
    /// <returns>A round length, in millimeters.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The length is not positive.</exception>
    /// <remarks>
    /// Down rather than to the nearest, so the ruler never outgrows the room it was sized for.
    /// A ruler of 23.7 mm would be arithmetically correct and useless - the same rule the
    /// vector section's scale bar has always followed.
    /// </remarks>
    public static double RoundDown(double mm)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mm);

        var decade = Math.Pow(10.0, Math.Floor(Math.Log10(mm)));
        var leading = mm / decade;

        // The logarithm of a power of ten can land a hair either side of the integer, which
        // puts the leading digit at 9.9999999 or 10.0000001 rather than at 1.
        if (leading >= 10.0 - 1e-9)
        {
            decade *= 10.0;
            leading /= 10.0;
        }
        else if (leading < 1.0 - 1e-9)
        {
            decade /= 10.0;
            leading *= 10.0;
        }

        // A rounding error must not drop a length that IS round a whole step: 200 mm arrives
        // as 199.99999999999997 often enough to matter.
        var step = leading >= 5.0 - 1e-9 ? 5.0 : leading >= 2.0 - 1e-9 ? 2.0 : 1.0;

        return step * decade;
    }

    /// <summary>A length as a reader wants it: in the unit that keeps the number small.</summary>
    /// <param name="mm">The length, in millimeters.</param>
    /// <returns>For example <c>200 mm</c>, <c>500 µm</c>, <c>2 m</c>.</returns>
    public static string Format(double mm)
    {
        var (value, unit) = mm switch
        {
            >= 1000.0 => (mm / 1000.0, "m"),
            >= 1.0 => (mm, "mm"),
            >= 1e-3 => (mm * 1e3, "µm"),
            _ => (mm * 1e6, "nm"),
        };

        // Fixed rather than general: "G3" writes a thousand as 1E+03, and a ruler label is read
        // by eye and drawn in a font that has no E.
        return value.ToString("0.###", CultureInfo.InvariantCulture) + " " + unit;
    }
}
