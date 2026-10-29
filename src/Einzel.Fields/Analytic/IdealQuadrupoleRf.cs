using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;

namespace Einzel.Fields.Analytic;

/// <summary>
/// The ideal two-dimensional quadrupole field, driven.
/// </summary>
/// <remarks>
/// <para>
/// Phi(x, y, t) = (U + V cos(omega t)) (x^2 - y^2) / r0^2, the hyperbolic field a
/// real quadrupole approximates with round rods. Exactly quadrupolar by
/// construction, which is the point: it separates whether the RF <em>path</em> is
/// right from whether a particular set of rods produces a good field, and those
/// are different questions with different failure modes.
/// </para>
/// <para>
/// The equation of motion it produces is the Mathieu equation, whose stability is
/// known in closed form. Spec section 19 calls recovering the a-q diagram "the
/// best single test that the RF path is correct", and this is the field that makes
/// that test about the integrator rather than about a mesh.
/// </para>
/// <para>
/// Two dimensional and infinite along one axis, which is what a mass filter's
/// cross-section is. An ion drifts along the axis at whatever speed it was given
/// and the transverse motion does not care. The axis is z unless another is
/// given; a device whose beam runs along x - every axisymmetric tunnel here - puts
/// the quadrupole across x, and the transverse pair follows the cyclic order so
/// the potential is always <c>drive (u^2 - v^2) / r0^2</c> in that pair.
/// </para>
/// </remarks>
public sealed class IdealQuadrupoleRf : ITimeVaryingField
{
    private readonly double _inscribedRadiusSquared;
    private readonly double[] _breakpoints;

    private IdealQuadrupoleRf(
        double directVolts,
        double amplitudeVolts,
        double angularFrequency,
        double inscribedRadius,
        RfWaveform waveform,
        CylinderAxis axis)
    {
        DirectVolts = directVolts;
        AmplitudeVolts = amplitudeVolts;
        AngularFrequency = angularFrequency;
        InscribedRadiusM = inscribedRadius;
        Waveform = waveform;
        Axis = axis;
        _inscribedRadiusSquared = inscribedRadius * inscribedRadius;
        _breakpoints = [.. waveform.Breakpoints];
    }

    /// <summary>The shape of the drive. A sinusoid unless another is given.</summary>
    public RfWaveform Waveform { get; }

    /// <summary>Creates a driven quadrupole.</summary>
    /// <param name="direct">The DC component applied to the x pair.</param>
    /// <param name="amplitude">The RF amplitude, zero to peak.</param>
    /// <param name="frequency">The drive frequency.</param>
    /// <param name="inscribedRadius">Axis to nearest electrode surface, conventionally r0.</param>
    /// <param name="waveform">The drive shape. A sinusoid when omitted.</param>
    /// <param name="axis">The axis the field is invariant along. Z when omitted.</param>
    /// <returns>The field.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The frequency or the radius is not positive.</exception>
    public static IdealQuadrupoleRf Create(
        Quantity direct,
        Quantity amplitude,
        Quantity frequency,
        Quantity inscribedRadius,
        RfWaveform? waveform = null,
        CylinderAxis axis = CylinderAxis.Z)
    {
        var hertz = frequency.In("Hz");
        var radius = inscribedRadius.In("m");

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hertz);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);

        return new IdealQuadrupoleRf(
            direct.In("V"),
            amplitude.In("V"),
            2.0 * Math.PI * hertz,
            radius,
            waveform ?? new RfWaveform.Sinusoid(),
            axis);
    }

    /// <summary>
    /// Creates the quadrupole that puts an ion at a stated point of the stability
    /// diagram.
    /// </summary>
    /// <param name="a">The Mathieu a parameter, which the DC sets.</param>
    /// <param name="q">The Mathieu q parameter, which the RF sets.</param>
    /// <param name="mass">The ion's mass.</param>
    /// <param name="charge">The ion's charge.</param>
    /// <param name="frequency">The drive frequency.</param>
    /// <param name="inscribedRadius">Axis to nearest electrode surface.</param>
    /// <param name="waveform">The drive shape. A sinusoid when omitted.</param>
    /// <param name="axis">The axis the field is invariant along. Z when omitted.</param>
    /// <returns>The field.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The frequency or the radius is not positive.</exception>
    /// <remarks>
    /// <para>
    /// a = 8 z e U / (m omega^2 r0^2) and q = 4 z e V / (m omega^2 r0^2), the
    /// standard mapping. Provided in this direction because the stability diagram
    /// is drawn in a and q and a test that scans them should say so, rather than
    /// scanning volts and converting in a comment.
    /// </para>
    /// <para>
    /// It also puts the mapping in one place. Every published quadrupole result is
    /// quoted in a and q, so an error in the conversion would make every
    /// comparison wrong by the same factor and each one would look self-consistent.
    /// </para>
    /// </remarks>
    public static IdealQuadrupoleRf FromMathieu(
        double a,
        double q,
        Quantity mass,
        Quantity charge,
        Quantity frequency,
        Quantity inscribedRadius,
        RfWaveform? waveform = null,
        CylinderAxis axis = CylinderAxis.Z)
    {
        var hertz = frequency.In("Hz");
        var radius = inscribedRadius.In("m");

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hertz);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);

        // Mass and charge rather than an IonSpecies: that type lives in the
        // transport layer, which sits above this one, and a field reaching upward
        // for it would invert the dependency for the sake of a constructor.
        var omega = 2.0 * Math.PI * hertz;
        var scale = mass.In("kg") * omega * omega * radius * radius / Math.Abs(charge.In("C"));

        return new IdealQuadrupoleRf(
            a * scale / 8.0, q * scale / 4.0, omega, radius, waveform ?? new RfWaveform.Sinusoid(), axis);
    }

    /// <summary>The DC component on the x pair, in volts.</summary>
    public double DirectVolts { get; }

    /// <summary>The RF amplitude, zero to peak, in volts.</summary>
    public double AmplitudeVolts { get; }

    /// <summary>The angular drive frequency, in radians per second.</summary>
    public double AngularFrequency { get; }

    /// <summary>Axis to nearest electrode surface, in metres.</summary>
    public double InscribedRadiusM { get; }

    /// <summary>The axis the field is invariant along.</summary>
    public CylinderAxis Axis { get; }

    /// <summary>The transverse pair (u, v) at a point, in the cyclic order after the axis.</summary>
    /// <remarks>
    /// z gives (x, y), so an undeclared axis computes exactly what it did before; x gives
    /// (y, z); y gives (z, x). A permutation rather than a rotation, so nothing is lost to
    /// rounding: a point on the axis has u = v = 0 to the bit.
    /// </remarks>
    private (double U, double V) Transverse(in Vec3 position) => Axis switch
    {
        CylinderAxis.X => (position.Y, position.Z),
        CylinderAxis.Y => (position.Z, position.X),
        _ => (position.X, position.Y),
    };

    /// <summary>A transverse field (E_u, E_v) put back into world coordinates; nothing along the axis.</summary>
    private Vec3 FromTransverse(double u, double v) => Axis switch
    {
        CylinderAxis.X => new Vec3(0.0, u, v),
        CylinderAxis.Y => new Vec3(v, 0.0, u),
        _ => new Vec3(u, v, 0.0),
    };

    /// <inheritdoc/>
    public double ShortestPeriodSeconds => 2.0 * Math.PI / AngularFrequency;

    /// <inheritdoc/>
    public double MonochromaticPeriodSeconds => AmplitudeVolts == 0.0
        ? double.PositiveInfinity : ShortestPeriodSeconds / Waveform.SingleHarmonicOrder;

    /// <inheritdoc/>
    public bool HasSameOscillationAs(ITimeVaryingField other) => other is IdealQuadrupoleRf field
        && AmplitudeVolts == field.AmplitudeVolts && AngularFrequency == field.AngularFrequency
        && InscribedRadiusM == field.InscribedRadiusM && Waveform == field.Waveform && Axis == field.Axis;

    /// <inheritdoc/>
    /// <remarks>Never: an analytic drive runs one way for the whole flight.</remarks>
    public double NextSwitchAfter(double timeSeconds) => double.PositiveInfinity;

    /// <inheritdoc/>
    /// <remarks>
    /// Judged on the phase <see cref="DriveAt"/> computes, <c>omega t / (2 pi)</c>, which is not
    /// <c>f t</c> in doubles; that arithmetic is kept rather than changed, because changing it
    /// would move the bits of every ideal-quadrupole result.
    /// </remarks>
    public double NextEdgeAfter(double timeSeconds)
    {
        var clock = new AngularClock(AngularFrequency);
        var frequency = AngularFrequency / (2.0 * Math.PI);
        var next = double.PositiveInfinity;

        foreach (var breakpoint in _breakpoints)
        {
            next = Math.Min(next, EdgeInstants.Next(clock, frequency, 0.0, breakpoint, timeSeconds));
        }

        return next;
    }

    /// <inheritdoc/>
    /// <remarks>An analytic field is defined everywhere and resolves everything.</remarks>
    public double ResolutionLength => double.PositiveInfinity;

    /// <summary>The potential applied to the x pair at an instant, in volts.</summary>
    /// <param name="timeSeconds">The instant.</param>
    /// <returns>The potential.</returns>
    public double DriveAt(double timeSeconds) =>
        DirectVolts + (AmplitudeVolts * Waveform.At(AngularFrequency * timeSeconds / (2.0 * Math.PI)));

    /// <summary>
    /// The Mathieu a a given drive produces, including any the waveform's own mean
    /// contributes.
    /// </summary>
    /// <param name="mass">The ion's mass.</param>
    /// <param name="charge">The ion's charge.</param>
    /// <returns>The a parameter.</returns>
    /// <remarks>
    /// A rectangular wave off half duty carries a mean of 2d - 1, which enters the
    /// equation of motion exactly where a DC offset would. Reporting it here keeps
    /// the two sources of a in one place: a digital filter with no DC supply still
    /// has an a, and it is set by switching times.
    /// </remarks>
    public double MathieuA(Quantity mass, Quantity charge)
    {
        var scale = mass.In("kg") * AngularFrequency * AngularFrequency
            * InscribedRadiusM * InscribedRadiusM / Math.Abs(charge.In("C"));

        return 8.0 * (DirectVolts + (AmplitudeVolts * Waveform.Mean)) / scale;
    }

    /// <summary>The Mathieu q a given drive produces.</summary>
    /// <param name="mass">The ion's mass.</param>
    /// <param name="charge">The ion's charge.</param>
    /// <returns>The q parameter.</returns>
    public double MathieuQ(Quantity mass, Quantity charge)
    {
        var scale = mass.In("kg") * AngularFrequency * AngularFrequency
            * InscribedRadiusM * InscribedRadiusM / Math.Abs(charge.In("C"));

        return 4.0 * AmplitudeVolts / scale;
    }

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds)
    {
        // E = -grad Phi, and Phi = drive (u^2 - v^2) / r0^2 in the transverse pair.
        var (u, v) = Transverse(in position);
        var scale = 2.0 * DriveAt(timeSeconds) / _inscribedRadiusSquared;
        return FromTransverse(-scale * u, scale * v);
    }

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position, double timeSeconds)
    {
        var (u, v) = Transverse(in position);
        return DriveAt(timeSeconds) * ((u * u) - (v * v)) / _inscribedRadiusSquared;
    }

    /// <inheritdoc/>
    /// <remarks>The instantaneous field at the start of the cycle.</remarks>
    public Vec3 ElectricFieldAt(in Vec3 position) => ElectricFieldAt(in position, 0.0);

    /// <inheritdoc/>
    /// <remarks>The instantaneous potential at the start of the cycle.</remarks>
    public double PotentialAt(in Vec3 position) => PotentialAt(in position, 0.0);
}
