using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields.Analytic;

namespace Einzel.Fields.Solved;

/// <summary>
/// A solved geometry whose electrodes are driven, presented as a field in time.
/// </summary>
/// <remarks>
/// <para>
/// RF on real geometry costs almost nothing beyond the static case, because
/// <em>basis superposition already was the mechanism</em>. The field is linear in
/// the applied potentials, so solving once per independent channel at unit
/// potential and then making the weights functions of time is RF, with nothing
/// re-solved and no time-stepping of the Poisson equation at all.
/// </para>
/// <para>
/// Channels rather than electrodes. SYM-1 makes the point in passing - "a 200-ring
/// funnel driven in two RF phases needs two RF basis fields plus a DC gradient, not
/// 200 basis solutions" - and it is the target this decomposition reaches, for any
/// number of rings.
/// </para>
/// <para>
/// Grouping is by <em>spatial pattern</em>, not by time dependence, and that is
/// what makes it minimal. Every electrode's potential is first split into the
/// supplies feeding it - one constant, one per distinct drive phase - so a resistor
/// chain down a funnel is a single supply however many distinct voltages it holds,
/// because what makes a supply one supply is that its electrodes move
/// <em>together</em>. Then supplies whose applied potentials are exactly
/// proportional share a solve and carry a weight each: a quadrupole run with DC has
/// a steady supply and an oscillating one, both putting the x pair up and the y
/// pair down by the same relative amounts, so the whole filter is still one solve.
/// </para>
/// </remarks>
public sealed class DrivenSolvedField : ITimeVaryingField, IConductorBounded
{
    private readonly IElectrostaticField[] _channels;
    private readonly double[] _direct;
    private readonly WeightTerm[][] _harmonics;
    private readonly double[] _frequencies;
    private readonly RfWaveform[] _waveforms;

    // One entry per stage, holding that stage's weights and when it ends. Empty
    // for a geometry held in one state for the whole run.
    private readonly double[] _boundaries;
    private readonly double[][] _stageDirect;
    private readonly WeightTerm[][][] _stageHarmonics;

    // A stage that ramps carries its end weights too, and its weights at any instant are
    // the linear interpolation between start and end over its own duration. Null per
    // stage where the stage holds.
    private readonly double[]?[] _stageEndDirect;
    private readonly WeightTerm[][]?[] _stageEndHarmonics;

    // Where the sequence has got to, held. Null means "read it from the sample time",
    // which is what an integrator wants; a value means "this is the operating point",
    // which is what a cycle average wants. See <see cref="AtOperatingPoint"/>.
    private readonly double? _operatingPoint;

    // Every distinct (drive, breakpoint, tap phase) the field can cross, over every stage.
    // Built once from the terms; see `NextEdgeAfter`.
    private readonly EdgeTap[] _edgeTaps;

    /// <summary>The same field with its operating point held at an instant.</summary>
    private DrivenSolvedField(DrivenSolvedField source, double operatingPoint)
    {
        // FINITE, not merely non-negative. `Math.Clamp(NaN, 0, 1)` is NaN, so a non-finite
        // operating point makes the ramp fraction NaN, every channel weight NaN, and every
        // potential and field this returns NaN - with nothing thrown and nothing warned.
        // This project has four times watched a non-finite double reach a result silently,
        // and once taken a whole --json document down at the serialiser after the run had
        // succeeded, which is why this constructor checks its instant and the public factory
        // `Create` checks every weight it is handed. The internal constructor the builders
        // call checks nothing: its inputs come from a model the validator has already passed.
        if (!double.IsFinite(operatingPoint))
        {
            throw new ArgumentOutOfRangeException(
                nameof(operatingPoint),
                operatingPoint,
                "an operating point is an instant on the instrument's timeline and must be "
                + "finite. A non-finite one would carry NaN into every channel weight");
        }

        // The arrays are never mutated after construction, so they are shared rather
        // than copied: this is built once per assembly of a ramped diffusive step and
        // copying a solved channel set per step would cost more than the saving.
        _channels = source._channels;
        _direct = source._direct;
        _harmonics = source._harmonics;
        _frequencies = source._frequencies;
        _waveforms = source._waveforms;
        _boundaries = source._boundaries;
        _stageDirect = source._stageDirect;
        _stageHarmonics = source._stageHarmonics;
        _stageEndDirect = source._stageEndDirect;
        _stageEndHarmonics = source._stageEndHarmonics;
        _edgeTaps = source._edgeTaps;
        _operatingPoint = operatingPoint;
    }

    internal DrivenSolvedField(
        IReadOnlyList<IElectrostaticField> channels,
        IReadOnlyList<double> direct,
        IReadOnlyList<IReadOnlyList<WeightTerm>> harmonics,
        IReadOnlyList<double> frequenciesHz,
        IReadOnlyList<RfWaveform> waveforms,
        IReadOnlyList<double>? boundaries = null,
        IReadOnlyList<IReadOnlyList<double>>? stageDirect = null,
        IReadOnlyList<IReadOnlyList<IReadOnlyList<WeightTerm>>>? stageHarmonics = null,
        IReadOnlyList<IReadOnlyList<double>?>? stageEndDirect = null,
        IReadOnlyList<IReadOnlyList<IReadOnlyList<WeightTerm>>?>? stageEndHarmonics = null)
    {
        _channels = [.. channels];
        _direct = [.. direct];
        _harmonics = [.. harmonics.Select(h => h.ToArray())];
        _frequencies = [.. frequenciesHz];
        _waveforms = [.. waveforms];

        _boundaries = boundaries is null ? [] : [.. boundaries];
        _stageDirect = stageDirect is null ? [] : [.. stageDirect.Select(d => d.ToArray())];

        _stageHarmonics = stageHarmonics is null
            ? []
            : [.. stageHarmonics.Select(stage => stage.Select(h => h.ToArray()).ToArray())];

        _stageEndDirect = stageEndDirect is null
            ? new double[]?[_boundaries.Length]
            : [.. stageEndDirect.Select(d => d?.ToArray())];
        _stageEndHarmonics = stageEndHarmonics is null
            ? new WeightTerm[][]?[_boundaries.Length]
            : [.. stageEndHarmonics.Select(stage => stage?.Select(h => h.ToArray()).ToArray())];

        _edgeTaps = EdgeSchedule();
    }

    /// <summary>
    /// A driven field over basis fields a host has solved itself: each channel weighted by a
    /// constant plus a sum of drive terms, with no sequence.
    /// </summary>
    /// <param name="channels">
    /// The basis fields, one per channel, all static and on the same geometry. The first also
    /// answers for the conductors and the domain edge.
    /// </param>
    /// <param name="direct">The constant part of each channel's weight, in volts.</param>
    /// <param name="harmonics">
    /// Each channel's drive terms: amplitude in volts, the drive whose clock it follows, and its
    /// phase in cycles of that drive. A channel with no drive takes an empty list.
    /// </param>
    /// <param name="frequenciesHz">Each drive's frequency, in hertz.</param>
    /// <param name="waveforms">Each drive's waveform, in the order of the frequencies.</param>
    /// <returns>
    /// The field whose potential at time t is the sum over channels of
    /// <c>(direct + sum of Amplitude * waveform.At(f * t + Phase)) * channel</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is what <c>einzel run</c> builds for a driven solved element, without the solve and
    /// without the decomposition into channels: the caller chooses the basis fields and their
    /// weights. A solve-once, fly-many host builds its basis fields once with
    /// <see cref="FieldAssembly.BuildReported(CompiledModel, ISolveStore)"/>, and then one field per ion from here with that
    /// ion's voltages and phase. The channels are shared, not copied, and every field involved
    /// is immutable, so many of these may sample the same channels from many threads.
    /// </para>
    /// <para>
    /// The time-free <see cref="ElectricFieldAt(in Vec3)"/> and <see cref="PotentialAt(in Vec3)"/>
    /// answer at t = 0, as for every driven field; an integrator reads the timed overloads.
    /// </para>
    /// <para>
    /// The arguments are checked here, unlike on the path from a model, because they come from
    /// a caller rather than from a validated document: a non-finite weight would otherwise
    /// carry NaN into every potential with nothing raised, and a term naming a drive that does
    /// not exist would be skipped without a word.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">A list argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// There are no channels, a channel or a channel's term list or a waveform is null, a channel
    /// varies in time (a channel must be static, because the weighting reads its time-free arm),
    /// the counts disagree, or a drive term exists and no drive is given.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A direct weight, amplitude or phase is not finite, a term names a drive outside the
    /// frequencies given, or a frequency is not finite and positive.
    /// </exception>
    public static DrivenSolvedField Create(
        IReadOnlyList<IElectrostaticField> channels,
        IReadOnlyList<double> direct,
        IReadOnlyList<IReadOnlyList<WeightTerm>> harmonics,
        IReadOnlyList<double> frequenciesHz,
        IReadOnlyList<RfWaveform> waveforms)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(direct);
        ArgumentNullException.ThrowIfNull(harmonics);
        ArgumentNullException.ThrowIfNull(frequenciesHz);
        ArgumentNullException.ThrowIfNull(waveforms);

        if (channels.Count == 0)
        {
            throw new ArgumentException("a driven field needs at least one channel", nameof(channels));
        }

        for (var c = 0; c < channels.Count; c++)
        {
            if (channels[c] is null)
            {
                throw new ArgumentException($"channel {c} is null", nameof(channels));
            }

            if (channels[c] is ITimeVaryingField)
            {
                throw new ArgumentException(
                    $"channel {c} varies in time, and a channel must be static: the weighting reads "
                    + "its time-free arm, which would freeze it at t = 0",
                    nameof(channels));
            }
        }

        if (direct.Count != channels.Count)
        {
            throw new ArgumentException(
                $"a driven field needs one direct weight per channel, and was given {direct.Count} "
                + $"against {channels.Count} channels",
                nameof(direct));
        }

        if (harmonics.Count != channels.Count)
        {
            throw new ArgumentException(
                $"a driven field needs one term list per channel, and was given {harmonics.Count} "
                + $"against {channels.Count} channels; a channel with no drive takes an empty list",
                nameof(harmonics));
        }

        if (waveforms.Count != frequenciesHz.Count)
        {
            throw new ArgumentException(
                $"every drive needs a frequency and a waveform, and was given {frequenciesHz.Count} "
                + $"frequencies against {waveforms.Count} waveforms",
                nameof(waveforms));
        }

        for (var k = 0; k < frequenciesHz.Count; k++)
        {
            if (!double.IsFinite(frequenciesHz[k]) || frequenciesHz[k] <= 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(frequenciesHz),
                    frequenciesHz[k],
                    $"drive {k}'s frequency must be finite and positive");
            }

            if (waveforms[k] is null)
            {
                throw new ArgumentException($"drive {k}'s waveform is null", nameof(waveforms));
            }
        }

        for (var c = 0; c < channels.Count; c++)
        {
            if (!double.IsFinite(direct[c]))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(direct),
                    direct[c],
                    $"channel {c}'s direct weight must be finite; it would carry NaN into every potential");
            }

            var terms = harmonics[c]
                ?? throw new ArgumentException(
                    $"channel {c}'s term list is null; a channel with no drive takes an empty list",
                    nameof(harmonics));

            for (var k = 0; k < terms.Count; k++)
            {
                var term = terms[k];

                if (frequenciesHz.Count == 0)
                {
                    throw new ArgumentException(
                        $"channel {c} has a drive term and no drive was given", nameof(frequenciesHz));
                }

                if (term.Drive < 0 || term.Drive >= frequenciesHz.Count)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(harmonics),
                        term.Drive,
                        $"channel {c}'s term {k} names drive {term.Drive}, and the drives are 0 to "
                        + $"{frequenciesHz.Count - 1}; such a term would be skipped without a word");
                }

                if (!double.IsFinite(term.Amplitude) || !double.IsFinite(term.Phase))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(harmonics),
                        term,
                        $"channel {c}'s term {k} must have a finite amplitude and phase");
                }
            }
        }

        return new DrivenSolvedField(channels, direct, harmonics, frequenciesHz, waveforms);
    }

    /// <summary>Whether any stage ramps rather than holds.</summary>
    public bool HasRamp => _stageEndDirect.Any(d => d is not null);

    // The same weights used by Weight(), evaluated at the held operating point.
    //
    // MEMOISED, because the answer cannot change for the lifetime of this instance: the
    // operating point and every array it reads are immutable after construction. The
    // callers are the cache's own validity check and the spectrum, both of which run once
    // per channel per diffusive step - so projecting a fresh array each time put an
    // allocation per channel into the path that exists to remove work from that step
    // (CMP-1). Built on first use rather than in the constructor, since a field that is
    // never asked for its spectrum should not pay for one.
    //
    // Unsynchronised deliberately. This type is sampled concurrently by the diffusive
    // coefficient sweep, but through `Weight` and the field accessors rather than through
    // here; and if a future caller did race, both racers would compute the same array from
    // the same immutable inputs, so the loser's work is discarded rather than a wrong
    // value published - reference assignment cannot tear. A lock would make the hot path
    // pay for a hazard that has no wrong outcome.
    private WeightTerm[][]? _rfTerms;

    private WeightTerm[] RfTermsAt(int channel)
    {
        var memo = _rfTerms ??= new WeightTerm[_channels.Length][];

        if (memo[channel] is { } held)
        {
            return held;
        }

        return memo[channel] = RfTerms(channel);
    }

    private WeightTerm[] RfTerms(int channel)
    {
        var setting = _operatingPoint ?? 0.0;
        var stage = _boundaries.Length == 0 ? -1 : StageAt(setting);
        var terms = stage < 0 ? _harmonics[channel] : _stageHarmonics[stage][channel];
        if (stage < 0 || _stageEndHarmonics[stage] is not { } ends) return terms;
        var start = stage == 0 ? 0.0 : _boundaries[stage - 1];
        var fraction = Math.Clamp((setting - start) / (_boundaries[stage] - start), 0.0, 1.0);
        return [.. terms.Select((term, k) => term with
        {
            Amplitude = term.Amplitude + (ends[channel][k].Amplitude - term.Amplitude) * fraction,
        })];
    }

    /// <inheritdoc/>
    public double MonochromaticPeriodSeconds
    {
        get
        {
            if (_boundaries.Length > 0 && _operatingPoint is null) return double.NaN;
            var frequency = 0.0;
            for (var c = 0; c < _channels.Length; c++)
            {
                foreach (var term in RfTermsAt(c))
                {
                    if (term.Amplitude == 0 || term.Drive < 0 || term.Drive >= _frequencies.Length) continue;
                    var next = _frequencies[term.Drive] * _waveforms[term.Drive].SingleHarmonicOrder;
                    if (next == 0) continue;
                    if (double.IsNaN(next) || (frequency != 0 && frequency != next)) return double.NaN;
                    frequency = next;
                }
            }
            return frequency == 0 ? double.PositiveInfinity : 1.0 / frequency;
        }
    }

    /// <inheritdoc/>
    public bool HasSameOscillationAs(ITimeVaryingField other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is not DrivenSolvedField field || !ReferenceEquals(_channels, field._channels)
            || !ReferenceEquals(_frequencies, field._frequencies)
            || !ReferenceEquals(_waveforms, field._waveforms)
            || (_boundaries.Length > 0 && (_operatingPoint is null || field._operatingPoint is null))) return false;
        for (var c = 0; c < _channels.Length; c++)
        {
            if (!RfTermsAt(c).AsSpan().SequenceEqual(field.RfTermsAt(c))) return false;
        }
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// A geometry with no sequence has one operating point already and returns itself, so
    /// every unsequenced model is bit-identical to what it was.
    /// </para>
    /// <para>
    /// A sequenced one holds both the stage and the ramp fraction, and keeps the waveform
    /// phases on real time. Both halves matter: a window straddling a stage boundary would
    /// otherwise average two operating points, which is the reason
    /// <c>SequencedRun</c> probes a period inside a phase's end rather than at it.
    /// </para>
    /// </remarks>
    public ITimeVaryingField AtOperatingPoint(double timeSeconds)
    {
        // VALIDATED BEFORE THE FAST PATH, not in the constructor the fast path skips.
        // Raised by review: an instant is an instant whether or not this field has anything
        // to hold with it, and a guard that fires only on the path that allocates makes
        // whether a caller's NaN is caught depend on the shape of the model. A geometry with no sequence took the fast path and never reached it.
        if (!double.IsFinite(timeSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeSeconds),
                timeSeconds,
                "an operating point is an instant on the instrument's timeline and must be "
                + "finite");
        }

        return _boundaries.Length == 0 ? this : new DrivenSolvedField(this, timeSeconds);
    }

    /// <summary>How many stages the sequence has. Zero for a geometry held in one state.</summary>
    public int StageCount => _boundaries.Length;

    /// <summary>When the sequence finishes, in seconds. Infinity when there is none.</summary>
    public double SequenceEndsAt => _boundaries.Length == 0 ? double.PositiveInfinity : _boundaries[^1];

    /// <summary>
    /// Which stage is running at an instant.
    /// </summary>
    /// <remarks>
    /// After the last one ends the last one continues to hold, rather than the
    /// field switching off. A sequence describes what the instrument does, and an
    /// instrument left alone stays where it was put - and a field that vanished at
    /// the end of the declared sequence would make every ion still in flight
    /// suddenly coast, which is a physics change disguised as a bookkeeping one.
    /// </remarks>
    private int StageAt(double timeSeconds)
    {
        for (var k = 0; k < _boundaries.Length; k++)
        {
            if (timeSeconds < _boundaries[k])
            {
                return k;
            }
        }

        return _boundaries.Length - 1;
    }

    /// <inheritdoc/>
    public double NextSwitchAfter(double timeSeconds)
    {
        foreach (var boundary in _boundaries)
        {
            if (boundary > timeSeconds)
            {
                return boundary;
            }
        }

        return double.PositiveInfinity;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The earliest crossing over every (drive, breakpoint, tap phase) the field holds, each
    /// judged on the phase <c>(f * t) + phase</c> exactly as the weights compute it. The phase
    /// alone decides the side of an edge, whatever the shape, because every shape wraps its
    /// phase as <c>phase - Math.Floor(phase)</c> before comparing it.
    /// </para>
    /// <para>
    /// The schedule is the union over every stage, built once, and not narrowed to the stage
    /// in force: a tap that exists only in a later stage reports its edges from the start.
    /// Landing on an instant where nothing jumps costs a step and changes no answer, and
    /// choosing per stage would make the schedule depend on the operating point as well.
    /// A term whose amplitude is exactly zero in a stage, at the start and the end of a ramp,
    /// contributes nothing there and is left out; any non-zero amplitude, however small,
    /// puts its tap's edges in. The decision is on the compiled term, not on the field it
    /// makes.
    /// </para>
    /// </remarks>
    public double NextEdgeAfter(double timeSeconds)
    {
        var next = double.PositiveInfinity;

        foreach (var tap in _edgeTaps)
        {
            var frequency = _frequencies[tap.Drive];
            var clock = new DriveClock(frequency, tap.Phase);
            next = Math.Min(next, EdgeInstants.Next(clock, frequency, tap.Phase, tap.Breakpoint, timeSeconds));
        }

        return next;
    }

    private EdgeTap[] EdgeSchedule()
    {
        var taps = new HashSet<EdgeTap>();

        void Add(WeightTerm[][]? channels, WeightTerm[][]? ends)
        {
            if (channels is null)
            {
                return;
            }

            for (var c = 0; c < channels.Length; c++)
            {
                for (var k = 0; k < channels[c].Length; k++)
                {
                    var term = channels[c][k];
                    var end = ends?[c][k].Amplitude ?? 0.0;

                    if (term.Drive < 0 || term.Drive >= _frequencies.Length || (term.Amplitude == 0.0 && end == 0.0))
                    {
                        continue;
                    }

                    foreach (var breakpoint in _waveforms[term.Drive].Breakpoints)
                    {
                        taps.Add(new EdgeTap(term.Drive, breakpoint, term.Phase));
                    }
                }
            }
        }

        Add(_harmonics, null);
        for (var s = 0; s < _stageHarmonics.Length; s++)
        {
            Add(_stageHarmonics[s], s < _stageEndHarmonics.Length ? _stageEndHarmonics[s] : null);
        }

        return [.. taps];
    }

    /// <summary>A breakpoint of a drive's waveform, seen through one tap's phase.</summary>
    private readonly record struct EdgeTap(int Drive, double Breakpoint, double Phase);

    /// <summary>
    /// The primary drive frequency, in hertz - the fastest of them.
    /// </summary>
    /// <remarks>
    /// A geometry may carry several generators at once: a trap's main RF alongside
    /// a supplementary excitation, or a guide's confining RF alongside a travelling
    /// wave. Where a single number is wanted - a collisions-per-cycle figure, a step
    /// cap - it is the fastest, because that is the timescale the field carries
    /// information on.
    /// </remarks>
    public double FrequencyHz => _frequencies.Length == 0 ? 1.0 : _frequencies.Max();

    /// <summary>Every drive frequency, in hertz, in declaration order.</summary>
    public IReadOnlyList<double> FrequenciesHz => _frequencies;

    /// <summary>How many basis solves the geometry reduced to.</summary>
    /// <remarks>
    /// Reported because it is the number that decides what a driven geometry costs,
    /// and because a template author who expected two and got two hundred has
    /// written a document whose electrodes do not share their time dependence.
    /// </remarks>
    public int ChannelCount => _channels.Length;

    /// <inheritdoc/>
    /// <remarks>
    /// The shortest period any drive carries, and for a harmonic waveform the period
    /// of its highest term rather than of its fundamental - a comb reaching order 120
    /// carries information a hundred and twenty times faster than its own repeat
    /// rate, and a controller told only the fundamental would step over every one of
    /// those oscillations while its error estimator agreed the step was accurate.
    /// </remarks>
    public double ShortestPeriodSeconds
    {
        get
        {
            var shortest = double.PositiveInfinity;

            for (var k = 0; k < _frequencies.Length; k++)
            {
                var highest = 1;

                if (k < _waveforms.Length && _waveforms[k] is RfWaveform.Harmonic harmonic)
                {
                    foreach (var term in harmonic.Terms)
                    {
                        highest = Math.Max(highest, term.Order);
                    }
                }

                shortest = Math.Min(shortest, 1.0 / (_frequencies[k] * highest));
            }

            // Infinite when nothing oscillates. This returned a placeholder of one second
            // for a geometry with stages and no drive, and every reader took it at its
            // word: the regime report called it a 1 Hz drive, and the diffusive path's
            // pseudopotential wrapper - which gates on a finite period, because a ramp
            // is not an oscillation - saw a finite period and cycle-averaged a DC ramp
            // over a one-second cycle, sweeping 97 per cent of a trapped density out of a
            // TIMS tunnel that was holding it. The integrator's step cap and the regime
            // report both already treat infinity as "no drive", which is what it is.
            return shortest;
        }
    }

    /// <inheritdoc/>
    public double ResolutionLength => _channels[0].ResolutionLength;

    /// <summary>The potential a channel holds at an instant, in volts.</summary>
    /// <param name="channel">Channel index.</param>
    /// <param name="timeSeconds">The instant.</param>
    /// <returns>The potential.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No such channel.</exception>
    public double WeightAt(int channel, double timeSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(channel);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(channel, _channels.Length);

        return Weight(channel, timeSeconds);
    }

    private double Weight(int channel, double timeSeconds)
    {
        // WHICH state the geometry is in, as against WHERE IN THE CYCLE the drive is.
        // Those are the same instant for an integrator and must not be for a cycle
        // average: see the remarks on `AtOperatingPoint`. The waveform phases below stay
        // on `timeSeconds`.
        var setting = _operatingPoint ?? timeSeconds;

        var stage = _boundaries.Length == 0 ? -1 : StageAt(setting);

        var direct = stage < 0 ? _direct[channel] : _stageDirect[stage][channel];
        var harmonics = stage < 0 ? _harmonics[channel] : _stageHarmonics[stage][channel];

        if (stage >= 0 && _stageEndDirect[stage] is { } endDirect)
        {
            // A ramp: interpolate every weight linearly over the stage's own duration.
            // Past the last boundary the fraction is clamped to one, so the last stage
            // holds the value its ramp reached, as a held last stage holds its value.
            var start = stage == 0 ? 0.0 : _boundaries[stage - 1];
            var fraction = Math.Clamp((setting - start) / (_boundaries[stage] - start), 0.0, 1.0);
            var endHarmonics = _stageEndHarmonics[stage]![channel];

            var ramped = direct + ((endDirect[channel] - direct) * fraction);
            for (var k = 0; k < harmonics.Length; k++)
            {
                var term = harmonics[k];
                var drive = term.Drive;
                if (drive < 0 || drive >= _frequencies.Length)
                {
                    continue;
                }

                var amplitude = term.Amplitude + ((endHarmonics[k].Amplitude - term.Amplitude) * fraction);
                ramped += amplitude * _waveforms[drive].At((_frequencies[drive] * timeSeconds) + term.Phase);
            }

            return ramped;
        }

        var total = direct;

        // More than one term because two supplies can share a spatial pattern: a
        // quadrupole's DC and RF put the same electrodes up and down by the same
        // relative amounts, so they are one solved field carrying two weights. Each
        // term names the clock its phase is measured on, which is what lets one
        // solved pattern be driven by two generators at different frequencies.
        foreach (var term in harmonics)
        {
            var drive = term.Drive;

            if (drive < 0 || drive >= _frequencies.Length)
            {
                continue;
            }

            total += term.Amplitude * _waveforms[drive].At((_frequencies[drive] * timeSeconds) + term.Phase);
        }

        return total;
    }

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds)
    {
        var total = Vec3.Zero;

        for (var k = 0; k < _channels.Length; k++)
        {
            var weight = Weight(k, timeSeconds);

            if (weight != 0.0)
            {
                total += _channels[k].ElectricFieldAt(in position) * weight;
            }
        }

        return total;
    }

    /// <inheritdoc/>
    /// <inheritdoc/>
    public double CycleMeanPotentialAt(
        in Vec3 position, double fromSeconds, double periodSeconds, int samples)
    {
        // The stage and the ramp fraction have to be constant across the window, or the mean
        // of the weights is not a function of one setting. They are when the operating point
        // is held - which is what the diffusive path does - and trivially when there are no
        // stages at all. Otherwise sample, as before.
        if (_operatingPoint is null && _boundaries.Length > 0)
        {
            return ITimeVaryingField.SampledCycleMean(
                this, in position, fromSeconds, periodSeconds, samples);
        }

        // ONE PASS AND NO ARRAY. A first version held the channel means in a
        // `new double[_channels.Length]` so it could bail out before evaluating any channel
        // potential - and this is called seven times per density node per step (once for the
        // potential, six for the central differences of the field), which on the shipped
        // front end is billions of allocations over a run, against CMP-1's "the inner loop
        // allocates nothing". Bailing out mid-accumulation costs at most a few wasted
        // channel evaluations on a path that re-evaluates everything anyway, and in practice
        // nothing: the condition is a property of the frequencies and the window, so it is
        // the same answer for every channel.
        var total = 0.0;

        for (var k = 0; k < _channels.Length; k++)
        {
            if (CycleMeanWeight(k, fromSeconds, periodSeconds) is not { } mean)
            {
                return ITimeVaryingField.SampledCycleMean(
                    this, in position, fromSeconds, periodSeconds, samples);
            }

            // Skipped at zero, in channel order, so the sum is the same arithmetic in the
            // same sequence as `PotentialAt` does it - which is what keeps an unsequenced
            // model bit-identical.
            if (mean != 0.0)
            {
                total += _channels[k].PotentialAt(in position) * mean;
            }
        }

        return total;
    }

    /// <summary>
    /// The mean of a channel's weight over a window, in closed form, or null where the
    /// window is not a whole number of cycles for every drive the channel taps.
    /// </summary>
    private double? CycleMeanWeight(int channel, double fromSeconds, double periodSeconds)
    {
        var setting = _operatingPoint ?? fromSeconds;

        var stage = _boundaries.Length == 0 ? -1 : StageAt(setting);

        var direct = stage < 0 ? _direct[channel] : _stageDirect[stage][channel];
        var harmonics = stage < 0 ? _harmonics[channel] : _stageHarmonics[stage][channel];

        var fraction = 0.0;
        WeightTerm[]? endHarmonics = null;

        if (stage >= 0 && _stageEndDirect[stage] is { } endDirect)
        {
            var start = stage == 0 ? 0.0 : _boundaries[stage - 1];
            fraction = Math.Clamp((setting - start) / (_boundaries[stage] - start), 0.0, 1.0);
            endHarmonics = _stageEndHarmonics[stage]![channel];
            direct += (endDirect[channel] - direct) * fraction;
        }

        var total = direct;

        for (var k = 0; k < harmonics.Length; k++)
        {
            var term = harmonics[k];
            var drive = term.Drive;

            if (drive < 0 || drive >= _frequencies.Length)
            {
                continue;
            }

            // A whole number of cycles, or the waveform's own mean is not this window's mean.
            var cycles = _frequencies[drive] * periodSeconds;
            var whole = Math.Round(cycles);

            if (whole < 1.0 || Math.Abs(cycles - whole) > 1e-9 * Math.Max(1.0, whole))
            {
                return null;
            }

            var amplitude = endHarmonics is null
                ? term.Amplitude
                : term.Amplitude + ((endHarmonics[k].Amplitude - term.Amplitude) * fraction);

            total += amplitude * _waveforms[drive].Mean;
        }

        return total;
    }

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position, double timeSeconds)
    {
        var total = 0.0;

        for (var k = 0; k < _channels.Length; k++)
        {
            var weight = Weight(k, timeSeconds);

            if (weight != 0.0)
            {
                total += _channels[k].PotentialAt(in position) * weight;
            }
        }

        return total;
    }

    /// <inheritdoc/>
    /// <remarks>The field at the start of the cycle.</remarks>
    public Vec3 ElectricFieldAt(in Vec3 position) => ElectricFieldAt(in position, 0.0);

    /// <inheritdoc/>
    /// <remarks>The potential at the start of the cycle.</remarks>
    public double PotentialAt(in Vec3 position) => PotentialAt(in position, 0.0);

    /// <inheritdoc/>
    /// <remarks>
    /// Zero, always. Every channel shares one grid, so a region that is field-free
    /// in one is field-free in all - but only at this instant, and the guarantee
    /// this returns is about a whole run. A driven field has no field-free regions
    /// that stay that way.
    /// </remarks>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => 0.0;

    /// <inheritdoc/>
    public double SignedDistanceToDiscontinuity(in Vec3 position) =>
        _channels[0].SignedDistanceToDiscontinuity(in position);

    /// <inheritdoc/>
    /// <remarks>
    /// Every channel is the same geometry at different potentials, so the metal is
    /// in the same place in all of them and the first is as good as any.
    /// </remarks>
    public double SignedDistanceToConductor(in Vec3 position) =>
        _channels[0] is IConductorBounded bounded
            ? bounded.SignedDistanceToConductor(in position)
            : double.PositiveInfinity;

    /// <inheritdoc/>
    public string? ConductorAt(in Vec3 position) =>
        _channels[0] is IConductorBounded bounded ? bounded.ConductorAt(in position) : null;
}
