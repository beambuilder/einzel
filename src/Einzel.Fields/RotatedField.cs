using Einzel.Core.Geometry;

namespace Einzel.Fields;

/// <summary>
/// A field rigidly rotated about an axis parallel to y, by a small angle.
/// </summary>
/// <remarks>
/// <para>
/// Built for the converging mirrors of an asymmetric-track analyser, and it is a
/// correctness fix rather than a convenience. Declaring the tilt on the electrodes and
/// solving the tilted geometry in three dimensions does not work at any affordable mesh:
/// the whole signal is the field anisotropy <c>Ez/Ex = tan(alpha)</c>, which for a
/// 200 micron convergence over 350 mm is 2.9e-4, while a second-order solve on a
/// 2.5 mm cell across a 40 mm gap carries around 0.4% of field error - fourteen times the
/// signal. Measured against the closed form below, that route gave 3.54, 0.011 and -0.57
/// of the true deceleration depending only on how wide the gaps between mirror strips
/// were.
/// </para>
/// <para>
/// A rotation is the exact answer instead of an approximated one, because rotations
/// commute with the Laplacian: if the inner field solves Laplace's equation for some
/// geometry, this field solves it exactly for the rotated geometry. Nothing is
/// linearised in the angle, and <c>Ez</c> is <i>constructed</i> from the geometry rather
/// than resolved by differencing a solved field. A shear would have been the obvious
/// spelling and is not equivalent - Laplace is not shear-invariant, and a shear of a
/// mirror pair translates both mirrors the same way, which is no convergence at all.
/// </para>
/// <para>
/// The natural partner is a two-dimensional cross-section, which is invariant along z and
/// so can be rotated with no domain to fall out of. A converging pair is then two
/// elements rotated oppositely, each carrying one mirror at potential with the other
/// grounded - which is the ordinary basis decomposition
/// <c>phi = sum_k V_k psi_k</c> and therefore exact, not an approximation.
/// </para>
/// </remarks>
/// <param name="inner">The field of the unrotated geometry.</param>
/// <param name="halfTurns">
/// The rotation, in half turns, so that 1.0 is 180 degrees. Positive rotates the +x
/// direction toward -z, matching <c>Electrode3D</c>'s tilt convention about y.
/// </param>
/// <param name="centreX">The x coordinate of the rotation axis, in metres.</param>
/// <param name="centreZ">The z coordinate of the rotation axis, in metres.</param>
public sealed class RotatedField(
    IElectrostaticField inner,
    double halfTurns,
    double centreX,
    double centreZ)
    : IElectrostaticField, IConductorBounded
{
    private readonly IElectrostaticField _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly double _cos = double.CosPi(halfTurns);
    private readonly double _sin = double.SinPi(halfTurns);

    // A zero rotation must be exactly a no-op, not merely a small one. Without this,
    // centreX + (x - centreX) does not round-trip to x in floating point, so wrapping an
    // untilted element would move every number this engine has validated in its last
    // bits. Asserted by ZeroRotationIsTheIdentity rather than assumed.
    private readonly bool _rotates = halfTurns != 0.0;

    /// <summary>The rotation this field applies, in half turns.</summary>
    public double HalfTurns { get; } = halfTurns;

    /// <summary>
    /// The tangent of the rotation angle: the field anisotropy the rotation introduces,
    /// and the per-reflection drift impulse divided by twice the ion speed.
    /// </summary>
    public double Slope => _sin / _cos;

    /// <summary>A point in the world, in the unrotated field's frame.</summary>
    /// <param name="p">The point, in metres.</param>
    /// <returns>Where the unrotated field is asked about it.</returns>
    /// <remarks>
    /// Rotate by minus the tilt about the axis. The same convention and the same arithmetic as
    /// Electrode3D.ToLocal, so a rotated field and a tilted box agree about which way a positive
    /// angle turns. Public so that a caller asking whether a point is inside the unrotated
    /// field's domain asks it of the very point this field evaluates there.
    /// </remarks>
    public Vec3 ToLocal(in Vec3 p)
    {
        if (!_rotates)
        {
            return p;
        }

        var dx = p.X - centreX;
        var dz = p.Z - centreZ;
        return new Vec3(
            centreX + (dx * _cos) - (dz * _sin),
            p.Y,
            centreZ + (dx * _sin) + (dz * _cos));
    }

    /// <summary>A point in the unrotated field's frame, placed in the world.</summary>
    /// <param name="p">The point, in metres.</param>
    /// <returns>The world point <see cref="ToLocal"/> maps to <paramref name="p"/>.</returns>
    public Vec3 FromLocal(in Vec3 p)
    {
        if (!_rotates)
        {
            return p;
        }

        var offset = ToWorld(new Vec3(p.X - centreX, p.Y, p.Z - centreZ));
        return new Vec3(centreX + offset.X, offset.Y, centreZ + offset.Z);
    }

    // The inverse rotation, applied to a vector rather than a point.
    private Vec3 ToWorld(in Vec3 v) => !_rotates ? v : new(
        (v.X * _cos) + (v.Z * _sin),
        v.Y,
        (v.Z * _cos) - (v.X * _sin));

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position)
    {
        var local = ToLocal(in position);
        var field = _inner.ElectricFieldAt(in local);
        return ToWorld(in field);
    }

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position)
    {
        var local = ToLocal(in position);
        return _inner.PotentialAt(in local);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Kept rather than surrendered, unlike <c>AxisymmetricField</c>: a rotation maps a
    /// straight line to a straight line, so a run that is field-free in the unrotated
    /// frame is field-free over the same length here.
    /// </remarks>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction)
    {
        var local = ToLocal(in position);
        // A direction is a vector, so it rotates without the centre offset.
        var localDirection = !_rotates ? direction : new Vec3(
            (direction.X * _cos) - (direction.Z * _sin),
            direction.Y,
            (direction.X * _sin) + (direction.Z * _cos));
        return _inner.FieldFreeRunLength(in local, in localDirection);
    }

    /// <inheritdoc/>
    public double SignedDistanceToDiscontinuity(in Vec3 position)
    {
        var local = ToLocal(in position);
        return _inner.SignedDistanceToDiscontinuity(in local);
    }

    /// <inheritdoc/>
    public double ResolutionLength => _inner.ResolutionLength;

    /// <inheritdoc/>
    /// <remarks>
    /// A rotation is rigid, so a distance measured in the unrotated frame is the distance
    /// in the world - no scaling is needed here, which is what makes an ion strike a
    /// rotated electrode at the right place.
    /// </remarks>
    public double SignedDistanceToConductor(in Vec3 position) =>
        _inner is IConductorBounded bounded
            ? bounded.SignedDistanceToConductor(ToLocal(in position))
            : double.PositiveInfinity;

    /// <inheritdoc/>
    public string? ConductorAt(in Vec3 position) =>
        _inner is IConductorBounded bounded ? bounded.ConductorAt(ToLocal(in position)) : null;
}
