namespace Einzel.Commands;

/// <summary>Where a conductor sits on the potential scale.</summary>
/// <remarks>
/// <para>
/// <b>Separate from the viewport so it can be checked without one.</b> Deciding what color
/// an electrode is drawn in needs no GL context, no window and no platform, and leaving it
/// inside the shell's GL control put it behind all three - which is why the defect below
/// lived in a viewport nothing could test.
/// </para>
/// <para>
/// <b>In the command layer rather than the shell, because two things now draw with it.</b>
/// The window and <c>einzel render still</c> must color one model the same way, and UI-1
/// forbids the shell from producing render output - so the decision lives where both can
/// reach it and neither owns it.
/// </para>
/// </remarks>
public static class Shading
{
    /// <summary>The extreme of what a conductor holds, keeping the drive's own sign.</summary>
    /// <param name="conductor">The conductor as the command layer described it.</param>
    /// <returns>Volts, signed about earth.</returns>
    /// <remarks>
    /// <para>
    /// <b>The drive amplitude is signed, and the sign is the whole point.</b> A quadrupole's
    /// two rod pairs are exact negatives - that is what makes them one basis channel - so a
    /// tap of +500 V and one of -500 V are antiphase, and a drawing that loses the
    /// distinction paints all four rods identically. Which is the single most informative
    /// thing about a driven structure's picture.
    /// </para>
    /// <para>
    /// <b>A first version took the absolute value and recovered the sign from the DC</b>,
    /// which is zero for a purely driven electrode - so every rod came back at +500 V. That
    /// is the sixth appearance of reading a driven electrode through a quantity that is not
    /// its drive, and the first to survive into a viewport.
    /// </para>
    /// <para>
    /// Adding is right rather than merely convenient: the potential swings over
    /// <c>DC +/- amplitude</c>, and with a signed amplitude the sum is the end of that swing
    /// furthest from earth where the DC and the drive push the same way, and the end nearest
    /// it otherwise. Earth stays earth either way, which is the value a reader looks for
    /// first.
    /// </para>
    /// </remarks>
    public static double Peak(ConductorSurface conductor)
    {
        ArgumentNullException.ThrowIfNull(conductor);

        return conductor.PotentialVolts + conductor.DriveAmplitudeVolts;
    }

    /// <summary>The widest excursion from earth over the conductors given, with no fallback.</summary>
    /// <param name="conductors">The conductors that are drawn.</param>
    /// <returns>Volts, zero where every conductor is at earth or there are none.</returns>
    /// <remarks>
    /// The one statement of "the metal's span is the largest <c>|Peak|</c>". <see cref="Span"/>
    /// paints with it and the legend states it, so the two cannot drift: the paint needs a
    /// nonzero divisor and the legend must not invent a range, which is the only way they
    /// differ and is <see cref="Span"/>'s fallback alone.
    /// </remarks>
    public static double Widest(IReadOnlyList<ConductorSurface> conductors)
    {
        ArgumentNullException.ThrowIfNull(conductors);

        var widest = 0.0;

        foreach (var conductor in conductors)
        {
            widest = Math.Max(widest, Math.Abs(Peak(conductor)));
        }

        return widest;
    }

    /// <summary>The widest excursion from earth anywhere in a scene, as a divisor.</summary>
    /// <param name="conductors">The conductors that are drawn.</param>
    /// <returns>Volts, always positive, and one where there is nothing to measure.</returns>
    /// <remarks>
    /// Taken over the same <see cref="Peak"/> the fraction is computed from, so the scale
    /// cannot be narrower than the values put on it - a span built from a different
    /// expression would clamp the very electrodes that set it. The fallback of one volt
    /// exists so the fraction cannot divide by zero; it is not a range to state.
    /// </remarks>
    public static double Span(IReadOnlyList<ConductorSurface> conductors)
    {
        var widest = Widest(conductors);

        return widest > 0.0 ? widest : 1.0;
    }

    /// <summary>Where a conductor falls on a diverging ramp, from zero to one.</summary>
    /// <param name="conductor">The conductor to place.</param>
    /// <param name="span">The widest excursion from earth in the scene.</param>
    /// <returns>Zero at the most negative, a half at earth, one at the most positive.</returns>
    /// <remarks>
    /// <b>Symmetric about earth rather than stretched across the observed range.</b> A lens
    /// holding 0 and 500 V would otherwise put the neutral color at 250, and an earthed tube
    /// would be painted like a genuinely negative one - earth being the value a reader looks
    /// for first.
    /// </remarks>
    public static double Fraction(ConductorSurface conductor, double span) =>
        span > 0.0 ? 0.5 + (0.5 * Math.Clamp(Peak(conductor) / span, -1.0, 1.0)) : 0.5;
}
