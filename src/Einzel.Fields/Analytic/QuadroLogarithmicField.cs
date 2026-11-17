using Einzel.Core.Geometry;
using Einzel.Core.Units;

namespace Einzel.Fields;

/// <summary>
/// The quadro-logarithmic field: a harmonic axial well superposed on a logarithmic
/// radial one.
/// </summary>
/// <remarks>
/// <para>
/// <c>U(r, z) = (k/2)(z^2 - r^2/2) + (k/2) Rm^2 ln(r/Rm)</c>, which satisfies Laplace
/// exactly — the quadratic part contributes <c>-k</c> to the radial Laplacian and <c>+k</c>
/// to the axial one, and the logarithm is harmonic on its own, so the whole thing sums to
/// zero at every point off the axis.
/// </para>
/// <para>
/// <b>Named for its mathematics rather than for the instrument built from it</b>, following
/// <see cref="HalfSpaceUniformField"/> and for the same reason: architecture invariant 2
/// keeps device names above <c>Einzel.Library</c>. An orbital trap is an arrangement of
/// this field, not something the engine knows about.
/// </para>
/// <para>
/// <b>The property the field exists for is that the axial motion is exactly harmonic and
/// exactly decoupled.</b> <c>dU/dz = k z</c> with no <c>r</c> in it, so an ion oscillates
/// axially at <c>omega = sqrt(q k / m)</c> whatever its radius, whatever its angular
/// momentum, and whatever its axial amplitude. That independence is not an approximation
/// and is the whole basis of measuring mass by frequency: everything else about the ion's
/// motion can vary and the frequency does not.
/// </para>
/// <para>
/// <b>The radial field vanishes at Rm</b> and points inward inside it, so bound orbits live
/// at <c>r &lt; Rm</c>. Outside it an ion is pushed away, which is not a defect of the
/// formula but the reason a real trap has an outer electrode there.
/// </para>
/// <para>
/// <b>The axis is a singularity</b>, as it must be: a logarithm has no value at zero and a
/// field that pulls inward at every radius has to come from somewhere. Physically the
/// central electrode occupies that region. Sampling on the axis is refused rather than
/// clamped, because a clamped value would be a number with no physics behind it and would
/// let an ion be launched somewhere it cannot be.
/// </para>
/// </remarks>
public sealed class QuadroLogarithmicField : IElectrostaticField
{
    private readonly double _curvatureSi;
    private readonly double _characteristicRadiusSi;
    private readonly Vec3 _centre;

    private QuadroLogarithmicField(double curvatureSi, double characteristicRadiusSi, Vec3 centre)
    {
        _curvatureSi = curvatureSi;
        _characteristicRadiusSi = characteristicRadiusSi;
        _centre = centre;
    }

    /// <summary>Creates the field.</summary>
    /// <param name="curvature">
    /// The axial field curvature <c>k</c>, of dimension volts per metre squared. It sets the
    /// axial frequency and nothing else: <c>omega = sqrt(q k / m)</c>.
    /// </param>
    /// <param name="characteristicRadius">
    /// The radius <c>Rm</c> at which the radial field vanishes. Bound orbits are inside it.
    /// </param>
    /// <param name="centre">
    /// The point the axial well is centred on, on the axis of symmetry. The axis runs along
    /// x, matching the convention every other field here uses for the optical axis.
    /// </param>
    /// <returns>The field.</returns>
    /// <exception cref="Core.Errors.EinzelException">A quantity has the wrong dimension.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A quantity is not positive.</exception>
    public static QuadroLogarithmicField Create(
        Quantity curvature, Quantity characteristicRadius, Vec3 centre)
    {
        var k = curvature.In("V/m^2");
        var radius = characteristicRadius.In("m");

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);

        return new QuadroLogarithmicField(k, radius, centre);
    }

    /// <summary>The axial field curvature, in volts per metre squared.</summary>
    public double CurvatureSi => _curvatureSi;

    /// <summary>The radius at which the radial field vanishes, in metres.</summary>
    public double CharacteristicRadiusSi => _characteristicRadiusSi;

    /// <summary>
    /// The axial angular frequency an ion of this charge-to-mass ratio oscillates at.
    /// </summary>
    /// <param name="chargeSi">Charge, in coulombs.</param>
    /// <param name="massSi">Mass, in kilograms.</param>
    /// <returns>Angular frequency, in radians per second.</returns>
    /// <remarks>
    /// Offered so a caller can check a measured frequency against the closed form without
    /// rederiving it, and so the one number the field is characterised by is reachable
    /// rather than implicit. It contains no radius and no amplitude, which is the point.
    /// </remarks>
    public double AxialAngularFrequency(double chargeSi, double massSi) =>
        Math.Sqrt(chargeSi * _curvatureSi / massSi);

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position)
    {
        var (axial, radial, unit) = Cylindrical(in position);

        // E = -grad U. Axially that is -k z, which is the harmonic restoring force and
        // carries no radial dependence at all. Radially it is k(r/2 - Rm^2/2r), which
        // vanishes at Rm, pulls inward inside it and pushes outward beyond.
        var radialField =
            _curvatureSi
            * ((radial / 2.0) - (_characteristicRadiusSi * _characteristicRadiusSi / (2.0 * radial)));

        return new Vec3(-_curvatureSi * axial, 0.0, 0.0)
            + (unit * radialField);
    }

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position)
    {
        var (axial, radial, _) = Cylindrical(in position);

        return (_curvatureSi / 2.0)
            * ((axial * axial) - (radial * radial / 2.0)
                + (_characteristicRadiusSi * _characteristicRadiusSi
                    * Math.Log(radial / _characteristicRadiusSi)));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The field varies everywhere, so there is no run to take analytically. Zero rather
    /// than a guess: the integrator treats it as "ask me again after a step".
    /// </remarks>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => 0.0;

    /// <summary>Axial offset, radius, and the outward radial unit vector.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The point is on the axis, where the logarithm has no value.
    /// </exception>
    private (double Axial, double Radial, Vec3 Outward) Cylindrical(in Vec3 position)
    {
        var offset = position - _centre;
        var radial = Math.Sqrt((offset.Y * offset.Y) + (offset.Z * offset.Z));

        if (!(radial > 0.0))
        {
            throw new PointRefusedException(
                "the quadro-logarithmic field is singular on its axis, where the central "
                + "electrode is: a point there has no potential rather than a large one");
        }

        return (offset.X, radial, new Vec3(0.0, offset.Y / radial, offset.Z / radial));
    }
}
