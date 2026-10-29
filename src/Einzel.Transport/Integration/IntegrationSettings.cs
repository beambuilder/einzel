namespace Einzel.Transport.Integration;

/// <summary>
/// Tolerances and limits for a trajectory integration.
/// </summary>
/// <remarks>
/// Spec section 11 requires per-step error control "against the run's accuracy
/// class". Stage 1 exposes the tolerances directly; binding them to a declared
/// accuracy class is the job of the run configuration, once models exist.
/// </remarks>
public sealed record IntegrationSettings
{
    /// <summary>
    /// Relative tolerance applied to both position and velocity. The default is
    /// tight enough that the ACC-1 flight-time budget is dominated by neither the
    /// controller nor round-off.
    /// </summary>
    public double RelativeTolerance { get; init; } = 1e-11;

    /// <summary>
    /// Absolute position tolerance, in metres. Sets the floor near a coordinate
    /// zero crossing, where the relative term vanishes.
    /// </summary>
    public double AbsolutePositionTolerance { get; init; } = 1e-13;

    /// <summary>
    /// Absolute velocity tolerance, in metres per second. Its floor matters most
    /// at a turning point, where the speed passes through zero.
    /// </summary>
    public double AbsoluteVelocityTolerance { get; init; } = 1e-9;

    /// <summary>
    /// Initial step size, in seconds. Zero selects a step from the local
    /// deceleration timescale; the controller corrects a poor guess within a few
    /// steps either way.
    /// </summary>
    public double InitialStep { get; init; }

    /// <summary>Largest permitted step, in seconds.</summary>
    public double MaximumStep { get; init; } = double.PositiveInfinity;

    /// <summary>
    /// Smallest permitted step, in seconds. Reaching it ends the run with
    /// <see cref="TrajectoryOutcome.StepSizeUnderflow"/> rather than grinding.
    /// </summary>
    public double MinimumStep { get; init; } = 1e-18;

    /// <summary>
    /// Step cap near a turning point, as a fraction of the time needed to change
    /// the velocity by the ion's characteristic speed. Zero disables the cap.
    /// </summary>
    /// <remarks>
    /// Spec section 11: "Turning points get forced step refinement. The velocity
    /// minimum inside a mirror is where relative timing error is largest and where
    /// position-error controllers under-refine." The mechanism is worth stating
    /// plainly. Near turnaround the ion barely moves, so a controller weighting
    /// position error sees a small change and happily lengthens the step — while
    /// the arrival time, which is what a TOF actually measures, is determined
    /// precisely there. The cap is applied whenever the ion is decelerating,
    /// which in a mirror is the whole penetration.
    /// </remarks>
    public double TurningPointStepFactor { get; init; } = 0.01;

    /// <summary>
    /// Whether to advance exactly through field-free regions instead of
    /// integrating them. Spec section 11 requires this; the switch exists so a
    /// test can measure what it buys.
    /// </summary>
    public bool UseAnalyticDrift { get; init; } = true;

    /// <summary>
    /// How many of the field's own resolution lengths a single step may cover.
    /// Zero disables the cap.
    /// </summary>
    /// <remarks>
    /// See <see cref="Fields.IElectrostaticField.ResolutionLength"/>. Four cells
    /// is loose enough not to dominate the step count in a smooth region and
    /// tight enough that a field feature one cell wide cannot be stepped over.
    /// </remarks>
    public double ResolutionCellsPerStep { get; init; } = 4.0;

    /// <summary>Ceiling on accepted steps, as a runaway guard.</summary>
    public int MaximumSteps { get; init; } = 20_000_000;

    /// <summary>
    /// Wall-clock ceiling on simulated flight, in seconds. Infinite by default;
    /// required when the trajectory has no stop condition.
    /// </summary>
    public double MaximumFlightTime { get; init; } = double.PositiveInfinity;

    /// <summary>Controller safety factor on the proposed step size.</summary>
    public double SafetyFactor { get; init; } = 0.9;

    /// <summary>
    /// Whether a packet flight spreads its applied-field evaluations across cores.
    /// Ignored by single-trajectory flights, which have one member.
    /// </summary>
    /// <remarks>
    /// <see cref="PacketParallelism.Automatic"/> decides from a measurement of the first
    /// stage, because what matters is members times cost-per-sample and only one of those
    /// is known before the run. The other two settings exist because the choice must be
    /// pinnable: the two paths are asserted to agree to the last bit, and an assertion
    /// nothing can arrange is not one.
    /// </remarks>
    public PacketParallelism Members { get; init; } = PacketParallelism.Automatic;

    /// <summary>Largest factor by which one step may grow over its predecessor.</summary>
    public double MaximumStepGrowth { get; init; } = 5.0;

    /// <summary>Smallest factor by which one step may shrink after a rejection.</summary>
    public double MinimumStepShrink { get; init; } = 0.1;

    /// <summary>
    /// Whether a step is cut to land on every edge a driven field declares
    /// (<see cref="Einzel.Fields.ITimeVaryingField.NextEdgeAfter"/>) as well as on every
    /// stage switch. False by default, which is the integrator of every earlier version
    /// to the bit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When true, the step is cut to the earlier of the next switch and the next edge.
    /// The landing step samples its two end stages one ulp before the instant, which the
    /// field's contract places on the near side; on acceptance the time is set to the
    /// instant exactly and the derivative is evaluated there, on the far side, so the
    /// end-of-step derivative is not reused across the jump. An instant closer than
    /// <see cref="MinimumStep"/> is taken in place: the time is set to it and the
    /// derivative re-evaluated without a step, which counts as neither an accepted nor a
    /// rejected step.
    /// </para>
    /// <para>
    /// Set from <c>transport.edges</c>: <c>land</c> is true, <c>cap</c> or an absent key
    /// false.
    /// </para>
    /// </remarks>
    public bool LandOnEdges { get; init; }
}

/// <summary>Why an integration stopped.</summary>
public enum TrajectoryOutcome
{
    /// <summary>The stop condition was met and the final state sits on it.</summary>
    StopConditionMet,

    /// <summary>The flight-time ceiling was reached.</summary>
    MaximumFlightTimeReached,

    /// <summary>The step ceiling was reached. The result is incomplete.</summary>
    MaximumStepsExceeded,

    /// <summary>
    /// The controller demanded a step below the floor. Usually a discontinuity
    /// the field did not declare, or a tolerance tighter than round-off allows.
    /// </summary>
    StepSizeUnderflow,

    /// <summary>
    /// The ion struck an electrode and was absorbed.
    /// </summary>
    /// <remarks>
    /// A real outcome rather than a failure: this is what an aperture is for, and
    /// what makes a transmission figure mean something. The surface it struck is
    /// named on the result, because ACC-5 asks for losses itemised by surface and
    /// refuses a bare percentage.
    /// </remarks>
    StruckElectrode,
}

/// <summary>Whether an integration reached a conclusion, or gave up short of one.</summary>
public static class TrajectoryOutcomes
{
    /// <summary>Whether the integration computed what it was asked to.</summary>
    /// <param name="outcome">The outcome to classify.</param>
    /// <returns>True when the run finished; false when the integrator gave up.</returns>
    /// <remarks>
    /// <para>
    /// <b>The distinction is whether the ENGINE finished, not whether the instrument
    /// performed.</b> An ion that strikes an electrode, or that is still being held when
    /// the declared hold ends, is a <i>result</i> — the integration did exactly what the
    /// document asked and the figures say what became of the ion. An integration that
    /// underflowed its step floor, or ran out of its step budget, did not: its numbers stop
    /// part way and nothing downstream can tell how far off they are.
    /// </para>
    /// <para>
    /// This exists because the two were conflated, and the conflation was measurable:
    /// <b>six of the thirty-seven shipped examples exited with a failure code while
    /// behaving exactly as designed</b> — three traps and thermalisations that end at their
    /// declared hold, and three deliberate losses that are the control halves of pairs.
    /// A rule that calls a sixth of the reference corpus broken is measuring the wrong
    /// thing.
    /// </para>
    /// <para>
    /// <see cref="TrajectoryOutcome.StruckElectrode"/>'s own remarks have said "a real
    /// outcome rather than a failure" since electrodes learned to absorb. The enum
    /// documented the principle and the exit code contradicted it.
    /// </para>
    /// <para>
    /// A switch with every case named and a throw for the rest, rather than a list of the
    /// successful ones: a list is a proxy for the question and has had to be widened twice
    /// already, once for diffusive runs and once for sequenced ones. A new outcome now
    /// fails to compile rather than being silently classified as a failure.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The outcome is not a known one.</exception>
    public static bool Completed(this TrajectoryOutcome outcome) => outcome switch
    {
        // The ion reached the surface it was flown at.
        TrajectoryOutcome.StopConditionMet => true,

        // The declared flight time elapsed. For a beamline that means the ion never
        // arrived, which the transmission and the itemised losses say; for a trap or a
        // thermalisation it is the intended end of the run.
        TrajectoryOutcome.MaximumFlightTimeReached => true,

        // What an aperture is for, and what makes a transmission figure mean anything.
        TrajectoryOutcome.StruckElectrode => true,

        // The integrator gave up. Both of these leave a trajectory that stops part way
        // for a numerical reason, with no way to say how wrong it is.
        TrajectoryOutcome.MaximumStepsExceeded => false,
        TrajectoryOutcome.StepSizeUnderflow => false,

        _ => throw new ArgumentOutOfRangeException(
            nameof(outcome),
            outcome,
            "a new trajectory outcome has to say whether it is a completed run or a "
            + "failure to compute one; it cannot default to either"),
    };
}

/// <summary>How a packet flight uses the machine's cores.</summary>
public enum PacketParallelism
{
    /// <summary>Decide from a measurement of the first stage.</summary>
    Automatic,

    /// <summary>One core, always. Every member is evaluated in index order.</summary>
    Serial,

    /// <summary>Spread the applied-field evaluations across cores, whatever they cost.</summary>
    Parallel,
}
