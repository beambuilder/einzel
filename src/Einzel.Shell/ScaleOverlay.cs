using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

using Einzel.Commands;

namespace Einzel.Shell;

/// <summary>
/// The scale indicator, drawn over the viewport as ordinary lines and text.
/// </summary>
/// <remarks>
/// <para>
/// <b>It decides nothing.</b> The round length, which arms show and where everything sits come
/// from <see cref="ScaleIndicator.For"/>, laid out from the matrix the viewport is drawing with
/// and the picture it is drawing - the same call <c>einzel render still</c> makes, so a still and
/// the window put the same ruler in the same corner.
/// </para>
/// <para>
/// <b>Over the GL control rather than inside it</b>, because Avalonia draws text and the shader
/// does not, and an annotation is not part of the scene: nothing in the instrument may hide it.
/// It takes no pointer input, so it never stands between a reader and the picture.
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
        var pen = new Pen(brush, ScaleIndicator.LineWidthPx);
        var origin = new Point(ruler.OriginPx.X, ruler.OriginPx.Y);

        foreach (var arm in ruler.Arms)
        {
            context.DrawLine(pen, origin, new Point(arm.TipPx.X, arm.TipPx.Y));
            Text(context, arm.Axis, arm.LetterPx, 0.8 * ruler.TextPx, brush);
        }

        Text(context, ruler.Label, ruler.LabelPx, ruler.TextPx, brush);
    }

    /// <summary>A line of text centered on a point, as the layout placed it.</summary>
    private static void Text(DrawingContext context, string text, (double X, double Y) center, double size, IBrush brush)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            size,
            brush);

        context.DrawText(formatted, new Point(center.X - (formatted.Width / 2.0), center.Y - (formatted.Height / 2.0)));
    }

    private static byte Byte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);
}
