namespace Einzel.Fields;

/// <summary>
/// A field declined to answer at one point - the quadro-logarithmic field on its singular axis,
/// where the potential has no value rather than a large one.
/// </summary>
/// <remarks>
/// <para>
/// An answer about a point, not a defect: a probe crossing such a point reports that row as
/// refused and every other row as usual. It derives from
/// <see cref="ArgumentOutOfRangeException"/>, which is what these refusals have always thrown, so
/// every existing catch still sees it; what it adds is a type narrow enough to catch on its own,
/// so that an argument exception from anywhere else in the field stack - a rejected instant, a
/// defect - is not mistaken for a refused point.
/// </para>
/// </remarks>
public sealed class PointRefusedException : ArgumentOutOfRangeException
{
    /// <summary>Creates the refusal.</summary>
    public PointRefusedException()
    {
    }

    /// <summary>Creates the refusal with a reason.</summary>
    /// <param name="message">Why the point has no value.</param>
    public PointRefusedException(string message)
        : base("position", message)
    {
    }

    /// <summary>Creates the refusal with a reason and a cause.</summary>
    /// <param name="message">Why the point has no value.</param>
    /// <param name="innerException">What caused it.</param>
    public PointRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
