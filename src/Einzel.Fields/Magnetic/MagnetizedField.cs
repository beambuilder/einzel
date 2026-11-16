using Einzel.Core.Geometry;

namespace Einzel.Fields.Magnetic;

/// <summary>
/// An electric field and a magnetic field carried as one object, so the single-ion
/// integrator sees both.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why one object rather than a second argument to the integrator.</b> Every flight in
/// this engine is built from what <see cref="FieldAssembly"/> returns, and there are more
/// than a dozen of them. A B handed in beside the field would have to be threaded through
/// every one, and the one somebody forgot would fly straight through the magnet with nothing
/// to say so. Carried on the field, every consumer that hands it to
/// <c>TrajectoryIntegrator</c> honours B without being changed, and every consumer that
/// cannot honour it can ask <c>field is IMagneticField</c> and refuse.
/// </para>
/// <para>
/// <b>Built only by the assembly, and only when the model declares a magnetic element</b>,
/// so a model without one is handed exactly the electric field it always was - the same
/// object, flown bit-identically.
/// </para>
/// <para>
/// Two classes, chosen by <see cref="Around"/> from what the electric part is, for the reason
/// <see cref="Analytic.BoundedField"/> has two: a wrapper that answered only
/// <see cref="IElectrostaticField"/> around a driven field would make the integrator sample
/// the RF at t = 0, which is the "time-varying quantity reached through a time-free
/// interface" defect this project has met six times.
/// </para>
/// </remarks>
public class MagnetizedField : IElectrostaticField, IMagneticField, IConductorBounded
{
    private readonly IElectrostaticField _electric;
    private readonly IMagneticField _magnetic;

    /// <summary>Combines an electric and a magnetic field.</summary>
    /// <param name="electric">The electric part.</param>
    /// <param name="magnetic">The magnetic part.</param>
    protected MagnetizedField(IElectrostaticField electric, IMagneticField magnetic)
    {
        ArgumentNullException.ThrowIfNull(electric);
        ArgumentNullException.ThrowIfNull(magnetic);

        if (electric is IMagneticField)
        {
            throw new ArgumentException(
                "the electric part already carries a magnetic field; superpose the magnetic "
                + "elements instead of nesting them", nameof(electric));
        }

        _electric = electric;
        _magnetic = magnetic;
    }

    /// <summary>The electric part.</summary>
    public IElectrostaticField Electric => _electric;

    /// <summary>The magnetic part.</summary>
    public IMagneticField Magnetic => _magnetic;

    /// <summary>
    /// Combines the two, choosing the driven form when the electric part is time-varying.
    /// </summary>
    /// <param name="electric">The electric part.</param>
    /// <param name="magnetic">The magnetic part.</param>
    /// <returns>The combined field.</returns>
    public static MagnetizedField Around(IElectrostaticField electric, IMagneticField magnetic) =>
        electric is ITimeVaryingField driven
            ? new DrivenMagnetizedField(driven, magnetic)
            : new MagnetizedField(electric, magnetic);

    /// <inheritdoc />
    public Vec3 ElectricFieldAt(in Vec3 position) => _electric.ElectricFieldAt(in position);

    /// <inheritdoc />
    public double PotentialAt(in Vec3 position) => _electric.PotentialAt(in position);

    /// <inheritdoc />
    /// <remarks>
    /// <b>The shorter of the electric run and the distance to the nearest magnetic field.</b>
    /// The analytic drift assumes zero acceleration means a straight line, which holds for E
    /// alone and fails the moment B acts: an ion with no electric force on it in a magnet is
    /// on a circle, not a line. So a drift is offered only up to where the ion would enter a
    /// magnetic field, and not at all inside one.
    /// </remarks>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) =>
        Math.Min(
            _electric.FieldFreeRunLength(in position, in direction),
            _magnetic.MagnetFreeRunLength(in position, in direction));

    /// <inheritdoc />
    /// <remarks>
    /// The electric discontinuities and the magnetic ones composed by
    /// <see cref="DiscontinuityDistance.Combine"/> - the nearest's magnitude and the product of
    /// the signs, the rule <see cref="SuperposedField"/> uses - so an ion lands on a magnet's
    /// edge exactly as it lands on an electric region's face, and crossing either flips the
    /// sign whichever happens to be nearer.
    /// </remarks>
    public double SignedDistanceToDiscontinuity(in Vec3 position) =>
        DiscontinuityDistance.Combine(
            _electric.SignedDistanceToDiscontinuity(in position),
            _magnetic.SignedDistanceToMagneticDiscontinuity(in position));

    /// <inheritdoc />
    /// <remarks>The magnetic surfaces alone: the electric ones are not jumps in B.</remarks>
    public double SignedDistanceToMagneticDiscontinuity(in Vec3 position) =>
        _magnetic.SignedDistanceToMagneticDiscontinuity(in position);

    /// <inheritdoc />
    public double ResolutionLength => _electric.ResolutionLength;

    /// <inheritdoc />
    public Vec3 MagneticFieldAt(in Vec3 position) => _magnetic.MagneticFieldAt(in position);

    /// <inheritdoc />
    public Vec3 MagneticFieldAt(in Vec3 position, in Vec3 side) =>
        _magnetic.MagneticFieldAt(in position, in side);

    /// <inheritdoc />
    public double MaximumFluxDensity => _magnetic.MaximumFluxDensity;

    /// <inheritdoc />
    public double MagnetFreeRunLength(in Vec3 position, in Vec3 direction) =>
        _magnetic.MagnetFreeRunLength(in position, in direction);

    /// <inheritdoc />
    public double MagnetHalfwayLength(in Vec3 position, in Vec3 direction) =>
        _magnetic.MagnetHalfwayLength(in position, in direction);

    /// <inheritdoc />
    public double SignedDistanceToConductor(in Vec3 position) => _electric is IConductorBounded bounded
        ? bounded.SignedDistanceToConductor(in position)
        : double.PositiveInfinity;

    /// <inheritdoc />
    public string? ConductorAt(in Vec3 position) => _electric is IConductorBounded bounded
        ? bounded.ConductorAt(in position)
        : null;
}

/// <summary>A magnetized field whose electric part is driven.</summary>
/// <remarks>
/// Forwards every member of <see cref="ITimeVaryingField"/>, including the ones with
/// defaults, because a default answered by this wrapper rather than the inner field is a
/// wrong answer that compiles.
/// </remarks>
public sealed class DrivenMagnetizedField : MagnetizedField, ITimeVaryingField
{
    private readonly ITimeVaryingField _driven;

    /// <summary>The electric part, as the driven field it is.</summary>
    internal ITimeVaryingField Driven => _driven;

    internal DrivenMagnetizedField(ITimeVaryingField electric, IMagneticField magnetic)
        : base(electric, magnetic) => _driven = electric;

    /// <inheritdoc />
    public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds) =>
        _driven.ElectricFieldAt(in position, timeSeconds);

    /// <inheritdoc />
    public double PotentialAt(in Vec3 position, double timeSeconds) =>
        _driven.PotentialAt(in position, timeSeconds);

    /// <inheritdoc />
    public double ShortestPeriodSeconds => _driven.ShortestPeriodSeconds;

    /// <inheritdoc />
    public double MonochromaticPeriodSeconds => _driven.MonochromaticPeriodSeconds;

    /// <inheritdoc />
    public bool HasSameOscillationAs(ITimeVaryingField other) =>
        other is DrivenMagnetizedField magnetized
        && ReferenceEquals(Magnetic, magnetized.Magnetic)
        && _driven.HasSameOscillationAs(magnetized._driven);

    /// <inheritdoc />
    public double NextSwitchAfter(double timeSeconds) => _driven.NextSwitchAfter(timeSeconds);

    /// <inheritdoc />
    /// <remarks>
    /// The inner field's edges, unchanged: B has no clock. Without this line the interface's
    /// default answers "no edge" and a rectangular drive beside a magnet is flown without
    /// landing on its edges, although <c>edges: land</c> is what the model asked for.
    /// </remarks>
    public double NextEdgeAfter(double timeSeconds) => _driven.NextEdgeAfter(timeSeconds);

    /// <inheritdoc />
    public double OscillatingResolutionLength => _driven.OscillatingResolutionLength;

    /// <inheritdoc />
    public double CycleMeanPotentialAt(
        in Vec3 position, double fromSeconds, double periodSeconds, int samples) =>
        _driven.CycleMeanPotentialAt(in position, fromSeconds, periodSeconds, samples);

    /// <inheritdoc />
    public ITimeVaryingField AtOperatingPoint(double timeSeconds)
    {
        var held = _driven.AtOperatingPoint(timeSeconds);

        return ReferenceEquals(held, _driven) ? this : (ITimeVaryingField)Around(held, Magnetic);
    }
}
