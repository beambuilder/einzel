using Avalonia.Media;
using Einzel.Commands;

namespace Einzel.Shell;

/// <summary>One row of the viewport's legend, ready to bind.</summary>
/// <remarks>
/// <para>
/// <b>A binding adapter and nothing more.</b> What the legend says — which scales exist,
/// what each one's ends are, and which ramp it is on — is
/// <see cref="ViewportPicture.Scales"/>'s decision, taken in the command layer where it can
/// be checked without a window and where <c>einzel render still</c> can reach the same
/// answer. This turns one of those into a label, two numbers and a brush.
/// </para>
/// <para>
/// <b>The swatch is sampled rather than approximated by two stops.</b> Viridis is not a
/// linear interpolation between its ends — that is the whole reason it is used, since its
/// lightness rises monotonically where a two-stop gradient between dark blue and yellow
/// does not — so a legend drawn from the ends alone would show a ramp the picture is not
/// painted with.
/// </para>
/// </remarks>
public sealed class LegendRow
{
    /// <summary>How many stops the swatch gradient is sampled at.</summary>
    /// <remarks>
    /// Enough that the sampling is invisible at the eleven pixels the bar is tall, and few
    /// enough that building one costs nothing. The ramps are smooth, so this is about the
    /// eye rather than about accuracy.
    /// </remarks>
    private const int Stops = 32;

    /// <summary>Builds a row from a scale the picture is drawn on.</summary>
    /// <param name="scale">One of <see cref="ViewportPicture.Scales"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scale"/> is null.</exception>
    public LegendRow(ColorScale scale)
    {
        ArgumentNullException.ThrowIfNull(scale);

        Quantity = scale.Quantity;

        if (scale.IsSingleValue)
        {
            // One value, drawn in the one color every path is painted with: a gradient here
            // would state a range nothing in the picture is drawn over.
            Low = string.Empty;
            High = ScaleText.One(scale.Low) + " " + scale.Unit;
            Swatch = Solid(scale.Diverging);
        }
        else
        {
            var (low, high) = ScaleText.Ends(scale.Low, scale.High);

            Low = low;
            High = high + " " + scale.Unit;
            Swatch = Gradient(scale.Diverging);
        }
    }

    /// <summary>The rows the legend shows for a picture, and none for no picture.</summary>
    /// <param name="outcome">What is on screen, or null when the viewport is empty.</param>
    /// <returns>One row per scale <see cref="ViewportPicture.Scales"/> states.</returns>
    /// <remarks>
    /// The one path every change of picture goes through - a draw, a watched frame, a cleared
    /// viewport - so a legend cannot outlive the picture it described.
    /// </remarks>
    public static IReadOnlyList<LegendRow> RowsFor(ViewportOutcome? outcome) =>
        ViewportPicture.Scales(outcome).Select(s => new LegendRow(s)).ToList();

    /// <summary>What the colors stand for: energy, field, electrodes or potential.</summary>
    public string Quantity { get; }

    /// <summary>The value at the left end of the swatch, with no unit; empty for a single value.</summary>
    public string Low { get; }

    /// <summary>The value at the right end, carrying the unit for the row; the only value of a single-value row.</summary>
    public string High { get; }

    /// <summary>The ramp itself, left to right.</summary>
    public IBrush Swatch { get; }

    private static SolidColorBrush Solid(bool diverging)
    {
        var (r, g, b) = diverging ? ColorRamp.Diverging(0.5) : ColorRamp.At(0.5);

        return new SolidColorBrush(Color.FromRgb(Byte(r), Byte(g), Byte(b)));
    }

    private static LinearGradientBrush Gradient(bool diverging)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0.5, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(1, 0.5, Avalonia.RelativeUnit.Relative),
        };

        for (var s = 0; s < Stops; s++)
        {
            var at = s / (double)(Stops - 1);

            var (r, g, b) = diverging ? ColorRamp.Diverging(at) : ColorRamp.At(at);

            brush.GradientStops.Add(new GradientStop(
                Color.FromRgb(Byte(r), Byte(g), Byte(b)), at));
        }

        return brush;
    }

    private static byte Byte(double channel) =>
        (byte)Math.Clamp(Math.Round(channel * 255.0), 0.0, 255.0);
}
