using Einzel.Core.Geometry;
using Einzel.Core.Model;

namespace Einzel.Fields.Analytic;

/// <summary>
/// An analytic field confined to a box, contributing nothing outside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>An analytic field has no extent, because a formula does not.</b> That is harmless
/// while such a field is an idealisation of a whole instrument — a uniform field, a
/// retarding half-space — and stops being harmless the moment one is an exact statement of
/// a real device sitting <i>next to</i> another. A quadro-logarithmic potential grows as z
/// squared, so an orbital analyser declared beside the trap that injects it puts an
/// enormous field across that trap, and the two instruments cannot be composed even though
/// superposition is exact and the sequencer can express the handover.
/// </para>
/// <para>
/// <b>The boundary is a field discontinuity, and the potential does not generally match
/// across it.</b> A box is not an equipotential of anything interesting, so an ion crossing
/// one gains or loses the potential it left. That is not hidden: the assembly measures the
/// largest potential anywhere on the boundary and reports it as a non-suppressible
/// violation, in volts, because it is exactly the energy error per crossing. Placing the
/// box where the field has decayed is what makes it small, and a region is a statement
/// about where an idealisation applies rather than a conductor.
/// </para>
/// <para>
/// Wrapping rather than teaching every analytic field about bounds — the precedent
/// <c>AxisymmetricField</c> and <c>PonderomotiveField</c> set, and for the
/// reason this project has recorded twice: the transport core carries every validated
/// number here, and refactoring it to add a case beside one is how those get quietly lost.
/// </para>
/// </remarks>
public class BoundedField : IElectrostaticField
{
    private readonly IElectrostaticField _inner;

    /// <summary>Wraps a field so that it is silent outside a box.</summary>
    /// <param name="inner">The field to bound.</param>
    /// <param name="region">The box, in metres.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    protected BoundedField(IElectrostaticField inner, FieldRegion region)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(region);

        _inner = inner;
        Region = region;
    }

    /// <summary>The box outside which this element contributes nothing.</summary>
    public FieldRegion Region { get; }

    /// <summary>The field this one bounds.</summary>
    protected IElectrostaticField Inner => _inner;

    /// <summary>
    /// Wraps a field so it is silent outside a box, keeping any time dependence.
    /// </summary>
    /// <param name="inner">The field to bound.</param>
    /// <param name="region">The box, in metres.</param>
    /// <returns>A bounded field of the same kind as the one given.</returns>
    /// <remarks>
    /// <b>The driven case is chosen by what the field IS, not by what the caller asks
    /// for.</b> A driven field also answers the time-free interface, at whatever instant it
    /// happens to be handed — and this project has now found that same defect five times,
    /// most recently in <c>SuperposedField</c>, where summing a driven member silently
    /// produced a snapshot of the RF at the top of its cycle. Returning a plain
    /// <see cref="BoundedField"/> around a driven field would be the sixth.
    /// </remarks>
    public static BoundedField Around(IElectrostaticField inner, FieldRegion region) =>
        inner is ITimeVaryingField driven
            ? new DrivenBoundedField(driven, region)
            : new BoundedField(inner, region);

    /// <inheritdoc />
    public Vec3 ElectricFieldAt(in Vec3 position)
    {
        var scale = Region.Scale(in position);

        if (scale <= 0.0)
        {
            return Vec3.Zero;
        }

        var field = _inner.ElectricFieldAt(in position);

        if (scale >= 1.0)
        {
            // Deep inside, or no fringe: the element itself, to the bit.
            return field;
        }

        // E = -grad(s phi) = s E - phi grad s: the potential's own gradient through the
        // fringe, so the bounded field is the gradient of the bounded potential.
        return (field * scale) - (Region.ScaleGradient(in position) * _inner.PotentialAt(in position));
    }

    /// <inheritdoc />
    public double PotentialAt(in Vec3 position)
    {
        var scale = Region.Scale(in position);
        return scale <= 0.0 ? 0.0 : scale * _inner.PotentialAt(in position);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Zero, which means "no guarantee", rather than the inner field's answer. Outside the
    /// box the field really is zero and a long analytic drift would be exact — but only as
    /// far as the boundary, and a run length is a promise about the <i>whole</i> run. The
    /// same reason <c>AxisymmetricField</c> gives one up.
    /// </remarks>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => 0.0;

    /// <inheritdoc />
    /// <remarks>
    /// The box surface, so the integrator brackets its zero and lands exactly on it — the
    /// same first-class event a declared field discontinuity already is (spec section 11).
    /// The inner field's own discontinuity, if it has one, is reported only inside: outside
    /// the box this element is not there, so it has no surfaces out there either.
    /// </remarks>
    public double SignedDistanceToDiscontinuity(in Vec3 position)
    {
        var toBoundary = Region.SignedDistance(in position);

        if (!Region.Contains(in position))
        {
            return toBoundary;
        }

        var inner = _inner.SignedDistanceToDiscontinuity(in position);

        return double.IsPositiveInfinity(inner)
            ? toBoundary
            : Math.Abs(inner) < Math.Abs(toBoundary) ? inner : toBoundary;
    }

    /// <inheritdoc />
    public double ResolutionLength => _inner.ResolutionLength;
}

/// <summary>A driven analytic field confined to a box.</summary>
/// <remarks>
/// Separate from <see cref="BoundedField"/> so that a driven field wrapped in a region
/// still answers the time-varying interface. Reached through
/// <see cref="BoundedField.Around"/> rather than constructed directly, so the choice is
/// made by what the inner field is.
/// </remarks>
public sealed class DrivenBoundedField : BoundedField, ITimeVaryingField
{
    private readonly ITimeVaryingField _driven;

    internal DrivenBoundedField(ITimeVaryingField inner, FieldRegion region)
        : base(inner, region) => _driven = inner;

    /// <inheritdoc />
    public double ShortestPeriodSeconds => _driven.ShortestPeriodSeconds;

    /// <inheritdoc />
    public double ShortestSmoothPeriodSeconds => _driven.ShortestSmoothPeriodSeconds;

    /// <inheritdoc/>
    public double MonochromaticPeriodSeconds => _driven.MonochromaticPeriodSeconds;

    /// <inheritdoc/>
    public bool HasSameOscillationAs(ITimeVaryingField other) =>
        other is DrivenBoundedField bounded && Region == bounded.Region
        && _driven.HasSameOscillationAs(bounded._driven);

    /// <inheritdoc />
    public double OscillatingResolutionLength => _driven.OscillatingResolutionLength;

    /// <inheritdoc/>
    /// <remarks>
    /// Forwarded: the region scales the field by a factor of position alone, so it moves
    /// no instant in time. Every inner this wraps today is analytic and answers infinity.
    /// </remarks>
    public double NextSwitchAfter(double timeSeconds) => _driven.NextSwitchAfter(timeSeconds);

    /// <inheritdoc/>
    /// <remarks>Forwarded, as the switches are.</remarks>
    public double NextEdgeAfter(double timeSeconds) => _driven.NextEdgeAfter(timeSeconds);

    /// <inheritdoc/>
    /// <remarks>
    /// The region scales a potential by a factor of position alone, so it commutes with an
    /// average over time and can be applied to the mean rather than to every sample.
    /// </remarks>
    public double CycleMeanPotentialAt(
        in Vec3 position, double fromSeconds, double periodSeconds, int samples)
    {
        var scale = Region.Scale(in position);

        return scale <= 0.0
            ? 0.0
            : scale * _driven.CycleMeanPotentialAt(
                in position, fromSeconds, periodSeconds, samples);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A region is a shape and holds nothing of its own, so this is the inner field's answer
    /// re-wrapped - and this instance where the inner field had nothing to hold.
    /// </remarks>
    public ITimeVaryingField AtOperatingPoint(double timeSeconds)
    {
        var held = _driven.AtOperatingPoint(timeSeconds);

        // Through `Around` rather than the constructor, because this class's own remarks
        // say the wrapper kind is chosen by inspecting the inner field and that the choice
        // lives in one place. Equivalent today - `held` is always time-varying, so `Around`
        // picks this type - and it stays equivalent if `Around` ever gains a case.
        return ReferenceEquals(held, _driven)
            ? this
            : (ITimeVaryingField)Around(held, Region);
    }

    /// <inheritdoc />
    public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds)
    {
        var scale = Region.Scale(in position);

        if (scale <= 0.0)
        {
            return Vec3.Zero;
        }

        var field = _driven.ElectricFieldAt(in position, timeSeconds);

        if (scale >= 1.0)
        {
            return field;
        }

        return (field * scale) - (Region.ScaleGradient(in position) * _driven.PotentialAt(in position, timeSeconds));
    }

    /// <inheritdoc />
    public double PotentialAt(in Vec3 position, double timeSeconds)
    {
        var scale = Region.Scale(in position);
        return scale <= 0.0 ? 0.0 : scale * _driven.PotentialAt(in position, timeSeconds);
    }
}
