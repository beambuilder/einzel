using Einzel.Core.Geometry;

namespace Einzel.Fields;

/// <summary>
/// A field that changes with time: the RF path.
/// </summary>
/// <remarks>
/// <para>
/// Kept as a separate interface rather than a time argument added to
/// <see cref="IElectrostaticField"/>, because the distinction is real and worth
/// keeping visible. A static field conserves energy and can be sampled in any
/// order; a time-varying one does neither. Code that has checked for this
/// interface knows which world it is in, and code that has not keeps the fast path
/// unchanged.
/// </para>
/// <para>
/// The inherited time-free members sample at t = 0. That is a real field - the
/// instantaneous one at the start of the cycle - so the inherited contract is
/// honoured rather than stubbed, and a caller that ignores time gets a definite
/// answer instead of an arbitrary one.
/// </para>
/// <para>
/// Almost all of RF costs nothing to build here, because the electric field is
/// linear in the applied potentials: solve once per electrode at unit potential
/// and any voltage set is a weighted sum of the results. Making those weights
/// functions of time <em>is</em> radio frequency, with nothing re-solved. The
/// basis machinery that makes it free has been in place since the field solver.
/// </para>
/// </remarks>
public interface ITimeVaryingField : IElectrostaticField
{
    /// <summary>The electric field vector at a point and an instant.</summary>
    /// <param name="position">The point, in metres.</param>
    /// <param name="timeSeconds">The instant, in seconds from the launch.</param>
    /// <returns>The field vector, in volts per metre.</returns>
    Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds);

    /// <summary>The electric potential at a point and an instant.</summary>
    /// <param name="position">The point, in metres.</param>
    /// <param name="timeSeconds">The instant, in seconds from the launch.</param>
    /// <returns>The potential, in volts.</returns>
    double PotentialAt(in Vec3 position, double timeSeconds);

    /// <summary>
    /// The shortest period in the drive, in seconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What stops the step controller outrunning the field. A gridded field
    /// carries no information below its node spacing and reports
    /// <see cref="IElectrostaticField.ResolutionLength"/> so a step cannot skip
    /// over it; a driven field carries none below its period, and the failure
    /// looks identical.
    /// </para>
    /// <para>
    /// It is worth being explicit about why an error estimator will not catch it.
    /// An embedded estimate compares two Runge-Kutta solutions of the same
    /// problem, and if every stage of a step happens to sample the same phase of
    /// the cycle both solutions agree and the step is accepted as accurate. It was
    /// accurate, for the field the step was shown. It was not shown the field.
    /// </para>
    /// </remarks>
    double ShortestPeriodSeconds { get; }

    /// <summary>
    /// Period of the single sinusoidal frequency at this operating point; infinity for
    /// no oscillation, NaN for an unknown or non-monochromatic spectrum. Unlike the
    /// shortest period, this licenses a single-frequency pseudopotential (REG-2).
    /// </summary>
    double MonochromaticPeriodSeconds => double.NaN;

    /// <summary>Whether the immutable oscillating fields are identical, ignoring DC.</summary>
    /// <param name="other">Another held operating point.</param>
    /// <returns>True only when equality follows from the field's definition, not spatial probes.</returns>
    bool HasSameOscillationAs(ITimeVaryingField other) => false;

    /// <summary>
    /// When the field next changes discontinuously, after a given instant.
    /// </summary>
    /// <param name="timeSeconds">The instant to look forward from.</param>
    /// <returns>The time of the next switch, or positive infinity when there is none.</returns>
    /// <remarks>
    /// <para>
    /// A sequencer switches state at known times, and a Runge-Kutta step that spans
    /// one averages two different fields into a single answer. Unlike a boundary in
    /// space this needs no root-find at all - the time is known in advance - so the
    /// integrator simply refuses to take a step past it and lands on it exactly.
    /// </para>
    /// <para>
    /// Infinity for a field driven by a continuous waveform, however fast: a
    /// sinusoid has no discontinuity. The jumps and kinks a waveform declares within
    /// its cycle are not switches: they are reported by <see cref="NextEdgeAfter"/>,
    /// and the integrator lands on them when a model asks for it, which a later change adds.
    /// </para>
    /// </remarks>
    double NextSwitchAfter(double timeSeconds) => double.PositiveInfinity;

    /// <summary>
    /// When the field next crosses a breakpoint its waveforms declare, after a given instant.
    /// </summary>
    /// <param name="timeSeconds">The instant to look forward from.</param>
    /// <returns>
    /// The smallest double <c>t*</c> greater than <paramref name="timeSeconds"/> at which this
    /// field's own evaluation is on the post-edge side of a declared waveform breakpoint, or
    /// positive infinity when no declared breakpoint lies ahead.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Exact to the double, not the nominal <c>(n + b - phase) / f</c>: evaluating the field at
    /// <c>Math.BitDecrement(t*)</c> gives the pre-edge value and at <c>t*</c> the post-edge one.
    /// The field computes its phase in doubles, and at the nominal instant that phase can
    /// still be an ulp or two short of the breakpoint, so a step ending there would see the
    /// old level. The crossing is judged on the phase alone, so it holds for every shape: each
    /// wraps its phase as <c>phase - Math.Floor(phase)</c> before comparing it.
    /// </para>
    /// <para>
    /// Distinct from <see cref="NextSwitchAfter"/>: a switch changes the state, an edge is a
    /// point within a periodic drive's cycle. Nothing reads this yet.
    /// </para>
    /// </remarks>
    double NextEdgeAfter(double timeSeconds) => double.PositiveInfinity;

    /// <summary>
    /// The mesh the <em>oscillating</em> part of the field is known on: the finest
    /// resolution among the members that vary in time, or infinity where every oscillating
    /// member is analytic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pseudopotential averages the oscillating field over the ion's quiver, and that
    /// average describes something only if the oscillating field is roughly linear across
    /// the excursion. For a solved RF that is a question about its cell; for an analytic
    /// one there is no mesh to exceed and the validity is the adiabatic one instead. A
    /// static member superposed with the RF does not enter: a DC field averaged over a
    /// small excursion is the DC field at the mean position, whatever cell it was solved
    /// on. Asking the whole field's <see cref="IElectrostaticField.ResolutionLength"/>
    /// reported a solved DC gradient's cell as the RF's, and an analytic confinement as
    /// unresolved.
    /// </para>
    /// <para>
    /// Defaults to the field's own resolution, which is the conservative reading: a
    /// wrapper that does not say otherwise is taken to be as coarse as it says it is.
    /// </para>
    /// </remarks>
    double OscillatingResolutionLength => ResolutionLength;

    /// <summary>
    /// The mean of the potential over one window, which is what a slow ion in a gas feels.
    /// </summary>
    /// <param name="position">Where to evaluate.</param>
    /// <param name="fromSeconds">When the window opens, on this field's own clock.</param>
    /// <param name="periodSeconds">How long the window is.</param>
    /// <param name="samples">How many samples the fallback takes.</param>
    /// <returns>The mean potential over that window, in volts.</returns>
    /// <remarks>
    /// <para>
    /// <b>This exists because sampling it is the dominant cost of a driven diffusive run.</b>
    /// The mean was taken by evaluating the composite potential at <paramref name="samples"/>
    /// instants for every node of the density grid, every step - on the shipped TIMS front end
    /// that is sixteen superpositions of bicubic interpolations across 33,345 nodes, about
    /// half a million field evaluations per step.
    /// </para>
    /// <para>
    /// It does not have to be sampled at all where the weights are known. A solved field's
    /// potential is <c>sum_k w_k(t) * phi_k(x)</c> - linear in the weights, which is what
    /// basis superposition means - so its mean over a window is
    /// <c>sum_k mean(w_k) * phi_k(x)</c>: <b>one evaluation per channel, and no time
    /// sampling</b>. Each weight is a constant plus harmonic terms, and
    /// <c>RfWaveform.Mean</c> gives a waveform's own cycle mean in closed form - zero
    /// for a sinusoid, <c>2*duty - 1</c> for a rectangular wave.
    /// </para>
    /// <para>
    /// <b>The condition is that every drive completes a whole number of cycles in the
    /// window.</b> The window is one period of the SHORTEST period present, so that holds
    /// when every generator shares a frequency and fails when one is slower - and where it
    /// fails, the sampled average is itself only an approximation of that generator's mean.
    /// An implementation that cannot satisfy it falls back to
    /// <see cref="SampledCycleMean"/>, which is what every implementation did before.
    /// </para>
    /// </remarks>
    double CycleMeanPotentialAt(
        in Vec3 position, double fromSeconds, double periodSeconds, int samples) =>
        SampledCycleMean(this, in position, fromSeconds, periodSeconds, samples);

    /// <summary>The sampled window mean: what every implementation used to do.</summary>
    /// <param name="field">The field to sample.</param>
    /// <param name="position">Where to evaluate.</param>
    /// <param name="fromSeconds">When the window opens.</param>
    /// <param name="periodSeconds">How long the window is.</param>
    /// <param name="samples">How many samples to take across it.</param>
    /// <returns>The mean potential over the window, in volts.</returns>
    static double SampledCycleMean(
        ITimeVaryingField field,
        in Vec3 position,
        double fromSeconds,
        double periodSeconds,
        int samples)
    {
        ArgumentNullException.ThrowIfNull(field);

        var total = 0.0;

        for (var s = 0; s < samples; s++)
        {
            total += field.PotentialAt(in position, fromSeconds + (periodSeconds * s / samples));
        }

        return total / samples;
    }

    /// <summary>
    /// The same field with any NON-OSCILLATORY time dependence held at the given instant,
    /// leaving the oscillation a function of time as before.
    /// </summary>
    /// <param name="timeSeconds">The instant to hold the operating point at.</param>
    /// <returns>The field at that operating point; the same instance where nothing varies.</returns>
    /// <remarks>
    /// <para>
    /// <b>A pseudopotential is defined at an operating point.</b> Averaging over a cycle asks
    /// what an ion feels from a field that repeats, and a ramp does not repeat - so if a
    /// ramp is still advancing inside the averaging window, the drift enters the mean square
    /// of the "oscillating" field and is indistinguishable there from quiver.
    /// </para>
    /// <para>
    /// It is not small and it is not only a bias. The covariance between a linear drift of
    /// rate r and a sinusoid of amplitude A sampled N times across a period is
    /// <c>(A r T/N)[-cos(phi0) + cot(pi/N) sin(phi0)]</c>, so it carries the phase the window
    /// opens at - and a diffusive step is set by a stability limit, never by the drive, so
    /// every assembly opens somewhere else in the cycle. On the shipped TIMS analyser that
    /// swing is 1.9e-5, which is what made its well cache rebuild at every one of thirty
    /// assemblies and save nothing. Measured against its closed form in
    /// <c>PonderomotiveRampLeakTests</c>.
    /// </para>
    /// <para>
    /// Neither obvious alternative works, and both are rejected by arithmetic rather than by
    /// trying them. Sampling the cycle more finely does not converge it away: as N grows
    /// <c>cot(pi/N) -&gt; N/pi</c>, so the swing tends to <c>4rT/(pi A)</c> and stops depending
    /// on N. And detrending the window rather than de-meaning it removes <c>6/(N^2-1)</c> of
    /// the well - 2.4 per cent at N = 16 - because the least-squares slope of a sinusoid
    /// sampled over one period is not zero.
    /// </para>
    /// <para>
    /// The default returns <c>this</c>, which is right for every field whose time dependence
    /// IS the oscillation. <b>Every implementer is listed here because the default decides
    /// for all of them at once, and the one that needed to disagree got no compiler error:</b>
    /// <c>DrivenSolvedField</c> holds its stage and ramp fraction;
    /// <c>SequencedField</c> holds its state selection (it was missed on the first pass, and
    /// a staged analytic element inside a driven superposition kept blending two states);
    /// <c>TimeShiftedField</c>, <c>DrivenBoundedField</c> and <c>DrivenSuperposedField</c>
    /// pass it to what they wrap; <c>OscillatingUniformField</c> and
    /// <c>IdealQuadrupoleRf</c> are pure oscillations and take the default.
    /// </para>
    /// <para>
    /// A new implementer belongs in that list, with its reason. A new member on this
    /// interface should be added without a default, or with the same enumeration done first.
    /// </para>
    /// </remarks>
    ITimeVaryingField AtOperatingPoint(double timeSeconds)
    {
        // THE GUARD LIVES IN THE DEFAULT, which is the only place that reaches every
        // composition. Put in the two implementations that STORE the instant, it missed the
        // ordinary case: an unsequenced model's solved element carries no drive, so it is not
        // time-varying at all, and the composition is made entirely of implementations that
        // pass the instant along or ignore it. Nothing validated, and a caller's NaN was
        // caught or not depending on whether the model happened to have a sequence in it.
        //
        // Here it covers everything, because an implementer either validates for itself -
        // SequencedField and DrivenSolvedField do, since they hold the value - or delegates
        // to something that eventually reaches a leaf, and every leaf takes this default.
        if (!double.IsFinite(timeSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeSeconds),
                timeSeconds,
                "an operating point is an instant on the instrument's timeline and must be "
                + "finite");
        }

        return this;
    }
}
