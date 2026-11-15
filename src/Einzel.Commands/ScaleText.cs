using System.Globalization;

namespace Einzel.Commands;

/// <summary>How the ends of a color scale are written so a reader can tell them apart.</summary>
/// <remarks>
/// <para>
/// <b>The two ends are formatted together.</b> Choosing digits from each value alone prints
/// a 4 keV beam with a few hundredths of an electronvolt of spread as <c>4000 ... 4000</c>
/// across a full gradient, and a +/-12.5 V scale as <c>-13 ... 13</c>. The digits are chosen
/// from the span between the ends instead: three significant figures of it, so the ends
/// differ in print whenever they differ and neither is rounded past the other.
/// </para>
/// <para>
/// Here rather than in the window because it decides what a reader can learn from the scale,
/// and a still that draws a legend must say the same thing. Invariant culture, because the
/// number sits beside a model document that writes a decimal point.
/// </para>
/// </remarks>
public static class ScaleText
{
    private const int MostDecimals = 12;

    /// <summary>Writes both ends of a scale with enough digits to tell them apart.</summary>
    /// <param name="low">The value at the low end.</param>
    /// <param name="high">The value at the high end.</param>
    /// <returns>The two texts, with no unit. Equal ends give equal texts.</returns>
    public static (string Low, string High) Ends(double low, double high)
    {
        var span = Math.Abs(high - low);

        if (span == 0.0 || double.IsNaN(span) || double.IsInfinity(span))
        {
            return (One(low), One(high));
        }

        var decimals = Math.Clamp(3 - (int)Math.Ceiling(Math.Log10(span)), 0, MostDecimals);

        return (Fixed(low, decimals), Fixed(high, decimals));
    }

    /// <summary>Writes one value, as for a scale whose ends are the same.</summary>
    /// <param name="value">The value.</param>
    /// <returns>Never in exponent form except where a fixed format would print zero.</returns>
    public static string One(double value)
    {
        var size = Math.Abs(value);

        var format = size == 0.0 ? "0"
            : size >= 10.0 ? "F0"
            : size >= 0.1 ? "F2"
            : "G3";

        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string Fixed(double value, int decimals)
    {
        var text = value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        if (text.Contains('.', StringComparison.Ordinal))
        {
            text = text.TrimEnd('0').TrimEnd('.');
        }

        return text == "-0" ? "0" : text;
    }
}
