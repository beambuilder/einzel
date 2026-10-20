using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

using Einzel.Commands;

namespace Einzel.Shell;

/// <summary>
/// The scale indicator, drawn over the viewport as ordinary lines.
/// </summary>
/// <remarks>
/// <para>
/// <b>It decides nothing.</b> The round length, which arms show, where everything sits and the
/// glyphs of its text come from <see cref="ScaleIndicator.For"/> and
/// <see cref="ScaleIndicator.Strokes"/>, laid out from the matrix the viewport is drawing with
/// and the picture it is drawing - the same calls <c>einzel render still</c> makes, so a still
/// and the window draw the same ruler, stroke for stroke, in the same corner.
/// </para>
/// <para>
/// <b>Text as strokes, not as a font.</b> The window first set the label and the letters in its
/// own font from the layout's numbers. The layout's text height is a digit's height and a font
/// is sized by its em, which is taller, so every label came out two thirds the size of the
/// still's; and scaled up to match, the font ran a little wider than the box the layout had
/// reserved, which it measures from the still's glyphs. A look at the live window caught the
/// first; a review of the fix caught the second. Drawing the still's own strokes ends both.
/// </para>
/// <para>
/// <b>Over the GL control rather than inside it</b>, because an annotation is not part of the
/// scene: nothing in the instrument may hide it. It takes no pointer input, so it never stands
/// between a reader and the picture.
/// </para>
/// </remarks>
public sealed class ScaleOverlay : Control
{
    private readonly SceneView _view;

    /// <summary>Draws a scale indicator over a viewport.</summary>
    /// <param name="view">The viewport whose camera the ruler measures.</param>
    public ScaleOverlay(SceneView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        _view = view;
        IsHitTestVisible = false;

        // The framing can change on the render thread - a watched packet grows the frame - and
        // a control may only be invalidated on the UI thread.
        _view.FramingChanged += (_, _) => Dispatcher.UIThread.Post(InvalidateVisual);
    }

    /// <inheritdoc />
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        // The ruler is sized from the picture's smaller side, so a resize changes its length.
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;

        if (!(width > 0.0) || !(height > 0.0) || _view.CurrentMatrix(width / height) is not { } matrix)
        {
            return;
        }

        if (ScaleIndicator.For(matrix, width, height, ScaleIndicator.TextHeightFor(width, height), picture: _view.Picture) is not { } ruler)
        {
            return;
        }

        var (r, g, b) = ScaleIndicator.Ink;
        var brush = new SolidColorBrush(Color.FromRgb(Byte(r), Byte(g), Byte(b)));
        Pen? pen = null;

        foreach (var stroke in ruler.Strokes())
        {
            // Round ends and joins, as the still's rasterizer strokes: everything within half a
            // width of the centerline.
            if (pen is null || pen.Thickness != stroke.WidthPx)
            {
                pen = new Pen(brush, stroke.WidthPx, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            }

            var points = stroke.PointsPx;

            for (var i = 2; i + 1 < points.Count; i += 2)
            {
                context.DrawLine(pen, new Point(points[i - 2], points[i - 1]), new Point(points[i], points[i + 1]));
            }
        }
    }

    private static byte Byte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);
}
