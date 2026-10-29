using Einzel.Core.Geometry;

namespace Einzel.Fields;

/// <summary>
/// One element, switched between compiled states by the instrument's timeline.
/// </summary>
/// <remarks>
/// <para>
/// <b>The generic form of what a sequence does to an element.</b> A solved geometry
/// carries its phases inside <c>DrivenSolvedField</c>, because there the phases are
/// re-weightings of channels that are already solved and nothing else changes. An
/// analytic element has no channels to re-weight — a phase simply gives it different
/// numbers — so it needs a switch rather than a weighting, and this is it.
/// </para>
/// <para>
/// Written once here rather than taught to each analytic kind. A uniform field, a
/// half-space and anything added later all switch the same way, and the alternative is a
/// per-phase branch inside every one of them: special cases layered on shared
/// infrastructure, which is the shape a fix takes when it is not deep enough.
/// </para>
/// <para>
/// <b>The last phase holds after the sequence ends</b>, which is the rule the solved path
/// already enforces and is a physics statement rather than a bookkeeping one: an
/// instrument left alone stays where it was put, and a field that switched off would make
/// every ion still in flight suddenly coast.
/// </para>
/// </remarks>
public sealed class SequencedField : ITimeVaryingField
{
    private readonly IReadOnlyList<IElectrostaticField> _states;
    private readonly IReadOnlyList<double> _boundaries;

    // Which state this field is in, held. Null means "read it from the sample time".
    // See AtOperatingPoint: for a sequence the operating point IS the state selection,
    // so holding it is what stops a cycle average near a phase boundary blending two.
    private readonly double? _operatingPoint;

    private SequencedField(
        IReadOnlyList<IElectrostaticField> states,
        IReadOnlyList<double> boundaries,
        double operatingPoint)
    {
        if (!double.IsFinite(operatingPoint))
        {
            throw new ArgumentOutOfRangeException(
                nameof(operatingPoint),
                operatingPoint,
                "an operating point is an instant on the instrument's timeline and must be "
                + "finite. A non-finite one selects the last state and would carry NaN into "
                + "every weight, which this project has four times watched reach a result "
                + "with nothing raised");
        }

        _states = states;
        _boundaries = boundaries;
        _operatingPoint = operatingPoint;
    }

    /// <summary>Wraps one element's per-phase states.</summary>
    /// <param name="states">The element as it stands during each phase, in order.</param>
    /// <param name="boundaries">
    /// The instant each phase ends, cumulative from zero and strictly increasing.
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// There are no states, or the counts disagree, or the boundaries do not increase.
    /// </exception>
    public SequencedField(
        IReadOnlyList<IElectrostaticField> states, IReadOnlyList<double> boundaries)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(boundaries);

        if (states.Count == 0)
        {
            throw new ArgumentException("a sequenced field needs at least one state", nameof(states));
        }

        if (states.Count != boundaries.Count)
        {
            throw new ArgumentException(
                $"a sequenced field needs one boundary per state, and was given "
                + $"{states.Count} states against {boundaries.Count} boundaries",
                nameof(boundaries));
        }

        for (var i = 1; i < boundaries.Count; i++)
        {
            if (boundaries[i] <= boundaries[i - 1])
            {
                throw new ArgumentException(
                    "phase boundaries are cumulative and must increase, and "
                    + $"boundary {i} at {boundaries[i]} s does not exceed "
                    + $"{boundaries[i - 1]} s",
                    nameof(boundaries));
            }
        }

        _states = states;
        _boundaries = boundaries;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A sequence is monochromatic only at a held operating point: without one there is
    /// no single state to take a spectrum of, so a sequence carrying any driven state
    /// reports NaN rather than picking one. All-static states have no oscillation at all,
    /// which is infinity rather than unknown.
    /// </remarks>
    public double MonochromaticPeriodSeconds => _operatingPoint is null
        ? (_states.All(s => s is not ITimeVaryingField) ? double.PositiveInfinity : double.NaN)
        : (At(0) is ITimeVaryingField driven ? driven.MonochromaticPeriodSeconds : double.PositiveInfinity);

    /// <inheritdoc/>
    public bool HasSameOscillationAs(ITimeVaryingField other)
    {
        if (other is not SequencedField sequence || _operatingPoint is null || sequence._operatingPoint is null)
            return ReferenceEquals(this, other);
        var a = At(0) as ITimeVaryingField;
        var b = sequence.At(0) as ITimeVaryingField;
        return a is null ? b is null : b is not null && a.HasSameOscillationAs(b);
    }

    /// <summary>The state that holds at an instant.</summary>
    /// <remarks>
    /// A time exactly on a boundary belongs to the phase that is starting, not the one
    /// that is ending — the integrator lands exactly on switch instants by design, so
    /// which side of the comparison it falls on is a real decision rather than a
    /// tie-break. Starting is the right one: the switch has happened.
    /// </remarks>
    /// <param name="timeSeconds">The instant, on the instrument's timeline.</param>
    /// <returns>The state in force there.</returns>
    private IElectrostaticField At(double timeSeconds)
    {
        timeSeconds = _operatingPoint ?? timeSeconds;

        for (var i = 0; i < _boundaries.Count; i++)
        {
            if (timeSeconds < _boundaries[i])
            {
                return _states[i];
            }
        }

        return _states[^1];
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>The time reaches the state.</b> This used to select the state by time and then
    /// read it through the TIME-FREE accessor, so a driven state's oscillation was pinned
    /// at whatever that answers - the eighth appearance in this project of a time-varying
    /// quantity reached through a time-free interface answering at an arbitrary instant.
    /// It bit in both transport modes: an integrator flying through a sequenced driven
    /// analytic element saw its RF held still rather than oscillating.
    /// </para>
    /// <para>
    /// WHICH state is a question about the sequence and is asked at the operating point;
    /// what that state is doing is a question about the drive and is asked at the sample
    /// time. Those are the same instant for an integrator and deliberately are not for a
    /// cycle average.
    /// </para>
    /// </remarks>
    public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds)
    {
        var state = At(timeSeconds);

        return state is ITimeVaryingField driven
            ? driven.ElectricFieldAt(in position, timeSeconds)
            : state.ElectricFieldAt(in position);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// As <see cref="ElectricFieldAt(in Vec3, double)"/>: the state is chosen at the
    /// operating point and then sampled at the given time.
    /// </remarks>
    public double PotentialAt(in Vec3 position, double timeSeconds)
    {
        var state = At(timeSeconds);

        return state is ITimeVaryingField driven
            ? driven.PotentialAt(in position, timeSeconds)
            : state.PotentialAt(in position);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The state is chosen at the operating point, as everywhere else here, and then asked
    /// for its own window mean. A static state contributes its potential once.
    /// </remarks>
    public double CycleMeanPotentialAt(
        in Vec3 position, double fromSeconds, double periodSeconds, int samples)
    {
        var state = At(fromSeconds);

        return state is ITimeVaryingField driven
            ? driven.CycleMeanPotentialAt(in position, fromSeconds, periodSeconds, samples)
            : state.PotentialAt(in position);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Held state by state as well as at the top: the selection is pinned so a window
    /// near a phase boundary cannot blend two states, and each state is asked to hold its
    /// own operating point so a ramped solve nested inside a sequence is held too.
    /// Returns this instance for a single-state sequence with nothing nested to hold,
    /// since pinning a selection that has only one answer changes nothing.
    /// </remarks>
    public ITimeVaryingField AtOperatingPoint(double timeSeconds)
    {
        // VALIDATED BEFORE THE FAST PATH, not in the constructor the fast path skips.
        // Raised by review: an instant is an instant whether or not this field has anything
        // to hold with it, and a guard that fires only on the path that allocates makes
        // whether a caller's NaN is caught depend on the shape of the model. A single-state sequence took the fast path and never reached it.
        if (!double.IsFinite(timeSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeSeconds),
                timeSeconds,
                "an operating point is an instant on the instrument's timeline and must be "
                + "finite");
        }

        IElectrostaticField[]? held = null;

        for (var i = 0; i < _states.Count; i++)
        {
            if (_states[i] is not ITimeVaryingField driven)
            {
                continue;
            }

            var one = driven.AtOperatingPoint(timeSeconds);

            if (ReferenceEquals(one, driven))
            {
                continue;
            }

            held ??= [.. _states];
            held[i] = one;
        }

        return held is null && _states.Count == 1
            ? this
            : new SequencedField(held ?? _states, _boundaries, timeSeconds);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A time-free caller gets the first phase, which is the instrument as it starts.
    /// This is the interface a driven field answers at an arbitrary instant without
    /// failing — the defect this project has now found four times — so a caller that
    /// reaches an element through it gets a stated instant rather than an accidental one.
    /// </remarks>
    public Vec3 ElectricFieldAt(in Vec3 position) =>
        (_operatingPoint is null ? _states[0] : At(0.0)).ElectricFieldAt(position);

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position) =>
        (_operatingPoint is null ? _states[0] : At(0.0)).PotentialAt(position);

    /// <inheritdoc/>
    public double ResolutionLength => _states.Min(s => s.ResolutionLength);

    /// <inheritdoc/>
    /// <remarks>A sequence of static states has nothing oscillating in it, and says so.</remarks>
    public double OscillatingResolutionLength => _states
        .OfType<ITimeVaryingField>()
        .Where(s => double.IsFinite(s.ShortestPeriodSeconds))
        .Select(s => s.OscillatingResolutionLength)
        .DefaultIfEmpty(double.PositiveInfinity)
        .Min();

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The shortest period any STATE declares, and infinity when none does - which is the
    /// static case, and the value this used to return unconditionally on the stated
    /// assumption that "a sequence of static states has nothing oscillating in it".
    /// <c>FieldAssembly.Sequenced</c> wraps whatever the per-phase build returns, so a
    /// driven analytic element with phases is a sequence of DRIVEN states and that
    /// assumption does not hold.
    /// </para>
    /// <para>
    /// Reporting infinity there cost two things, both silent. The diffusive path's
    /// <c>Effective</c> takes an early return on a non-finite period, so such a field was
    /// never cycle-averaged at all; and step control had no drive period to cap against.
    /// This mirrors <see cref="OscillatingResolutionLength"/>, which already asked the
    /// states rather than assuming.
    /// </para>
    /// <para>
    /// The sequence's own switches are not a period, and are still reported through
    /// <see cref="NextSwitchAfter"/>.
    /// </para>
    /// </remarks>
    public double ShortestPeriodSeconds => _states
        .OfType<ITimeVaryingField>()
        .Select(s => s.ShortestPeriodSeconds)
        .Where(double.IsFinite)
        .DefaultIfEmpty(double.PositiveInfinity)
        .Min();

    /// <inheritdoc/>
    /// <remarks>
    /// The integrator refuses to step past this, so a switch needs no root-find: unlike a
    /// boundary in space, the time is known in advance.
    /// </remarks>
    public double NextSwitchAfter(double timeSeconds)
    {
        // Indexed, not foreach: the boundaries are an IReadOnlyList, whose enumerator is
        // boxed, and this runs once per step and once per edge query.
        for (var i = 0; i < _boundaries.Count; i++)
        {
            if (_boundaries[i] > timeSeconds)
            {
                return _boundaries[i];
            }
        }

        return double.PositiveInfinity;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The edges of the state in force, up to the next boundary and not including it: the
    /// boundary is a switch, reported by <see cref="NextSwitchAfter"/>, and the edges after
    /// it belong to the state that starts there, which answers once the time has reached
    /// it. A held operating point never leaves its state, so its edges are not cut.
    /// </remarks>
    public double NextEdgeAfter(double timeSeconds)
    {
        if (At(timeSeconds) is not ITimeVaryingField driven)
        {
            return double.PositiveInfinity;
        }

        var edge = driven.NextEdgeAfter(timeSeconds);
        var boundary = _operatingPoint is null ? NextSwitchAfter(timeSeconds) : double.PositiveInfinity;

        return edge < boundary ? edge : double.PositiveInfinity;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Taken from the first state. Every phase of one element is the same geometry with
    /// different numbers on it — the validator refuses a phase that moves metal — so the
    /// discontinuity surfaces are the same throughout.
    /// </remarks>
    public double SignedDistanceToDiscontinuity(in Vec3 position) =>
        _states[0].SignedDistanceToDiscontinuity(position);

    /// <inheritdoc/>
    /// <remarks>
    /// Zero rather than the first state's answer. A field-free run length is a promise
    /// about a whole straight segment, and a switch part-way along it would break that
    /// promise silently; the analytic drift is an optimisation, so giving it up is a cost
    /// in speed and never in accuracy.
    /// </remarks>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => 0.0;
}
