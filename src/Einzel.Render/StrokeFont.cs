namespace Einzel.Render;

/// <summary>
/// A small line font, for the few words a raster picture carries: lengths, their units and
/// axis letters.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lines rather than a typeface, and LIC-1 and invariant 1 are both why.</b> A still is drawn
/// on a CI runner with no display and no font server, and a font file shipped with the engine
/// is a license question this project does not want for the sake of a label. Glyphs drawn as
/// strokes through the rasterizer's own line routine need neither, scale to any size, and are
/// antialiased by the same supersampling as everything else in the picture.
/// </para>
/// <para>
/// <b>Deliberately small.</b> Digits, a decimal point, the units a length is written in and the
/// three axis letters - what a scale indicator says. A character outside that set is drawn as an
/// empty box rather than skipped, so a label the font cannot spell shows a gap a reader can see
/// instead of a shorter word that reads as a different length.
/// </para>
/// </remarks>
public static class StrokeFont
{
    /// <summary>The height of a digit, in glyph units; a pixel height is divided by this.</summary>
    private const double CapHeight = 6.0;

    /// <summary>Each glyph: how far it advances, and its strokes as x, y pairs with y up from the baseline.</summary>
    private static readonly Dictionary<char, (double Advance, double[][] Strokes)> Glyphs = new()
    {
        ['0'] = (5, [[0, 1, 0, 5, 1, 6, 3, 6, 4, 5, 4, 1, 3, 0, 1, 0, 0, 1]]),
        ['1'] = (5, [[1, 5, 2, 6, 2, 0], [1, 0, 3, 0]]),
        ['2'] = (5, [[0, 5, 1, 6, 3, 6, 4, 5, 4, 4, 0, 0, 4, 0]]),
        ['3'] = (5, [[0, 5, 1, 6, 3, 6, 4, 5, 4, 4, 3, 3, 4, 2, 4, 1, 3, 0, 1, 0, 0, 1], [1.5, 3, 3, 3]]),
        ['4'] = (5, [[3, 0, 3, 6, 0, 2, 4, 2]]),
        ['5'] = (5, [[4, 6, 0, 6, 0, 3, 3, 3, 4, 2, 4, 1, 3, 0, 1, 0, 0, 1]]),
        ['6'] = (5, [[4, 5, 3, 6, 1, 6, 0, 5, 0, 1, 1, 0, 3, 0, 4, 1, 4, 2, 3, 3, 0, 3]]),
        ['7'] = (5, [[0, 6, 4, 6, 1, 0]]),
        ['8'] = (5, [[1, 3, 0, 4, 0, 5, 1, 6, 3, 6, 4, 5, 4, 4, 3, 3, 1, 3, 0, 2, 0, 1, 1, 0, 3, 0, 4, 1, 4, 2, 3, 3]]),
        ['9'] = (5, [[0, 1, 1, 0, 3, 0, 4, 1, 4, 5, 3, 6, 1, 6, 0, 5, 0, 4, 1, 3, 4, 3]]),
        ['.'] = (2, [[0.5, 0, 0.5, 0.3]]),
        [' '] = (3, []),
        ['m'] = (5, [[0, 0, 0, 4], [0, 3, 1, 4, 1.5, 4, 2, 3, 2, 0], [2, 3, 2.5, 4, 3, 4, 4, 3, 4, 0]]),
        ['n'] = (5, [[0, 0, 0, 4], [0, 3, 1, 4, 3, 4, 4, 3, 4, 0]]),
        ['µ'] = (5, [[0, -2, 0, 4], [0, 1, 1, 0, 3, 0, 4, 1], [4, 4, 4, 0]]),
        ['x'] = (5, [[0, 0, 4, 4], [0, 4, 4, 0]]),
        ['y'] = (5, [[0, 4, 2, 1], [4, 4, 1, -2]]),
        ['z'] = (5, [[0, 4, 4, 4, 0, 0, 4, 0]]),
    };

    /// <summary>What an unknown character is drawn as: a box the size of a digit.</summary>
    private static readonly (double Advance, double[][] Strokes) Missing = (5, [[0, 0, 4, 0, 4, 6, 0, 6, 0, 0]]);

    /// <summary>Whether a character has a glyph of its own.</summary>
    /// <param name="c">The character.</param>
    /// <returns>True when it is drawn as itself rather than as an empty box.</returns>
    public static bool Supports(char c) => Glyphs.ContainsKey(c);

    /// <summary>How wide a line of text is.</summary>
    /// <param name="text">The text.</param>
    /// <param name="heightPx">The height of a digit, in pixels.</param>
    /// <returns>The width, in pixels.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public static double Width(string text, double heightPx)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return 0.0;
        }

        // The last glyph's trailing space is not part of the word.
        var units = text.Sum(c => Glyph(c).Advance) - 1.0;

        return Math.Max(0.0, units) * heightPx / CapHeight;
    }

    /// <summary>The strokes that draw a line of text centered on a point.</summary>
    /// <param name="text">The text.</param>
    /// <param name="centerXPx">The horizontal center, in pixels from the left.</param>
    /// <param name="centerYPx">The vertical center of a digit, in pixels from the top.</param>
    /// <param name="heightPx">The height of a digit, in pixels.</param>
    /// <returns>Polylines as x, y pairs in pixels, rows running down.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public static IReadOnlyList<double[]> Layout(string text, double centerXPx, double centerYPx, double heightPx)
    {
        ArgumentNullException.ThrowIfNull(text);

        var unit = heightPx / CapHeight;
        var x = centerXPx - (0.5 * Width(text, heightPx));
        var baseline = centerYPx + (0.5 * heightPx);

        var strokes = new List<double[]>();

        foreach (var c in text)
        {
            var (advance, glyph) = Glyph(c);

            foreach (var stroke in glyph)
            {
                var placed = new double[stroke.Length];

                for (var i = 0; i + 1 < stroke.Length; i += 2)
                {
                    placed[i] = x + (stroke[i] * unit);
                    placed[i + 1] = baseline - (stroke[i + 1] * unit);
                }

                strokes.Add(placed);
            }

            x += advance * unit;
        }

        return strokes;
    }

    private static (double Advance, double[][] Strokes) Glyph(char c) =>
        Glyphs.TryGetValue(c, out var glyph) ? glyph : Missing;
}
