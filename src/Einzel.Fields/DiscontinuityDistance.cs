namespace Einzel.Fields;

/// <summary>
/// The one rule for composing the signed distances of several declared discontinuities into
/// one: the magnitude of the nearest, and the sign the product of every member's sign.
/// </summary>
/// <remarks>
/// <para>
/// <b>The sign is a product, not the sign of whichever surface is nearer.</b> The integrator
/// detects a crossing as a change of sign between the start and the end of a step, so the
/// composed sign must flip exactly when one member's sign flips. A product does that. Taking
/// the nearer member's sign does not: an ion one millimetre inside a bounded magnet and five in
/// front of a mirror plane reads -1 mm (the magnet's face is nearer), and half a millimetre
/// past the plane it reads -0.5 mm (now the plane is nearer) - the same sign, so the exact
/// landing on the mirror is skipped; and where the nearer surface changes from one the ion is
/// inside to one it is outside, the sign flips with nothing crossed and a bisection is spent on
/// a surface that is not there.
/// </para>
/// <para>
/// Shared by every composition rather than copied, because two copies of a sign rule are how
/// one composition came to disagree with the others. The known limit is
/// <see cref="SuperposedField"/>'s: a step crossing two surfaces at once returns the sign to
/// where it started.
/// </para>
/// </remarks>
public static class DiscontinuityDistance
{
    /// <summary>
    /// Folds one more member's signed distance into a running composition.
    /// </summary>
    /// <param name="composed">
    /// The composition so far; positive infinity when no member has a discontinuity yet.
    /// </param>
    /// <param name="member">The next member's signed distance; non-finite means none.</param>
    /// <returns>The composition including <paramref name="member"/>.</returns>
    public static double Combine(double composed, double member)
    {
        if (!double.IsFinite(member))
        {
            return composed;
        }

        if (!double.IsFinite(composed))
        {
            return member;
        }

        var sign = (composed < 0.0 ? -1 : 1) * (member < 0.0 ? -1 : 1);

        return Math.Min(Math.Abs(composed), Math.Abs(member)) * sign;
    }
}
