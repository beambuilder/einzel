using Einzel.Core.Geometry;
using Einzel.Core.Numerics;
using Einzel.Fields;

namespace Einzel.Transport.Integration;

/// <summary>
/// A scalar function of the ion's state whose descending zero crossing ends the
/// integration.
/// </summary>
/// <param name="state">The current state.</param>
/// <returns>
/// A value that is positive while the flight continues and becomes negative once
/// the ion has passed the stopping surface.
/// </returns>
/// <remarks>
/// A signed distance to a detector plane is the usual case. The integrator lands
/// on the zero exactly rather than at the end of whichever step happened to cross
/// it, which matters because the crossing time is the measurement.
/// </remarks>
public delegate double TrajectoryStopFunction(in PhaseState state);

/// <summary>
/// Adaptive-step trajectory integration through a static electric field.
/// </summary>
/// <remarks>
/// <para>
/// The scalar reference implementation. CMP-1: it is never deleted or allowed to
/// rot, because every SIMD or GPU path is tested against it.
/// </para>
/// <para>
/// Five things here are specified rather than chosen, and each addresses a
/// distinct way a flight time goes wrong: Dormand-Prince 5(4) with per-step error
/// control for truncation error, Neumaier compensation for accumulation error in
/// the time total, analytic advance through field-free regions so most of a
/// multi-reflection path contributes no integration error at all, exact landing
/// on declared field discontinuities so no step straddles one, and a forced step
/// cap while decelerating so the turning point is resolved.
/// </para>
/// <para>
/// The inner loop allocates nothing. Spec section 22 lists GC pauses in long runs
/// as a risk that is "preventable with allocation discipline from the start", and
/// <c>AllocationTests</c> asserts that allocation does not grow with step count.
/// </para>
/// </remarks>
public static class TrajectoryIntegrator
{
    // Below this, an analytic drift advance is not worth taking and would risk
    // stalling against a boundary the ion is already sitting on. At 1e-13 m the
    // skipped time is under 1e-17 s for any ion of interest, twelve orders below
    // the ACC-1 budget on a microsecond flight.
    private const double DegenerateDriftDistance = 1e-13;

    /// <summary>Integrates one ion until the stop condition or a limit is reached.</summary>
    /// <param name="initialState">Starting position and velocity, in SI.</param>
    /// <param name="species">The ion's mass and charge.</param>
    /// <param name="field">The field to integrate through.</param>
    /// <param name="settings">Tolerances and limits.</param>
    /// <param name="stopWhenNegative">
    /// Optional stopping surface. When null, <see cref="IntegrationSettings.MaximumFlightTime"/>
    /// must be finite.
    /// </param>
    /// <param name="recorder">
    /// Optional trajectory sampler for rendering and export (TRJ-1). Supplying
    /// one makes the run allocate in proportion to the sample count; omitting it
    /// keeps the inner loop allocation-free.
    /// </param>
    /// <param name="collisions">
    /// Optional gas sampler. Supplying one makes the flight collisional; omitting
    /// it is a flight in vacuum, and the arithmetic of a vacuum flight is unchanged
    /// to the last bit by this parameter existing.
    /// </param>
    /// <returns>The trajectory outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> or <paramref name="settings"/> is null.</exception>
    /// <exception cref="ArgumentException">The run is unbounded: no stop condition and no flight-time ceiling.</exception>
    public static TrajectoryResult Integrate(
        PhaseState initialState,
        IonSpecies species,
        IElectrostaticField field,
        IntegrationSettings settings,
        TrajectoryStopFunction? stopWhenNegative = null,
        TrajectoryRecorder? recorder = null,
        Collisions.CollisionSampler? collisions = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(settings);

        if (stopWhenNegative is null && double.IsPositiveInfinity(settings.MaximumFlightTime))
        {
            throw new ArgumentException(
                "an integration needs either a stop condition or a finite MaximumFlightTime",
                nameof(settings));
        }

        // Hoisted out of the loop: constructing this per step would allocate a
        // closure per step, which is exactly what the allocation discipline in
        // spec section 11 forbids.
        var boundarySurface = BoundarySurfaceFor(field);

        // Conductors, when the field has any. Expressed as a stopping surface
        // because that is exactly what an electrode is - the ion stops on it - and
        // reusing the machinery means it lands on the surface rather than a step
        // short of it or a step inside it.
        //
        // Sound because a gridded field already caps the step at its own cell
        // spacing, so a step cannot arc into an electrode and back out again
        // between two samples: the chord and the arc differ by far less than an
        // electrode is thick.
        var conductors = field as IConductorBounded;

        TrajectoryStopFunction? conductorSurface = conductors is null
            ? null
            : (in PhaseState state) =>
            {
                var position = state.Position;
                return conductors.SignedDistanceToConductor(in position);
            };

        var chargeToMass = species.ChargeToMassSi;
        var state = initialState;

        // The first collision is scheduled from the launch state. A sampler that
        // scheduled lazily at the first step would miss an ion that struck an
        // electrode inside its own mean free path.
        collisions?.Start(0.0, initialState.Velocity.Length);

        // A source inside an electrode has nowhere to go, and reporting it as a
        // zero-length flight would look like an instrument that loses everything
        // rather than a model that puts its source in the metal.
        if (conductorSurface is not null && conductorSurface(in state) < 0.0)
        {
            var launchPoint = state.Position;

            return new TrajectoryResult
            {
                FinalState = state,
                FlightTimeSeconds = 0.0,
                TimeCompensation = 0.0,
                Outcome = TrajectoryOutcome.StruckElectrode,
                StruckSurface = conductors!.ConductorAt(in launchPoint),
                AcceptedSteps = 0,
                RejectedSteps = 0,
                FieldEvaluations = 0,
                AnalyticDriftDistance = 0.0,

                // NaN, for the same reason as below and more strongly: this ion never
                // flew, so its drift is not merely unmeasured, it is meaningless. Zero
                // would be the best possible value of a diagnostic that had no chance to
                // say anything.
                MaximumRelativeEnergyDrift = double.NaN,
            };
        }

        var time = default(CompensatedSum);
        long fieldEvaluations = 0;
        var analyticDistance = 0.0;
        var accepted = 0;
        var rejected = 0;

        var initialEnergy = TotalEnergy(in state, species, field, ref fieldEvaluations);
        var energyScale = Math.Abs(initialEnergy);

        // NOT ZERO WHEN THERE IS NOTHING TO MEASURE AGAINST. An ion launched at rest at
        // zero potential has a total energy of exactly zero, so there is no scale to form
        // a relative drift against and the tracking below returns without doing anything -
        // which used to leave this at its initial 0.0 and print "energy drift 0.00E+000
        // relative (ACC-4 budget 1e-6)". That reads as four orders inside budget and means
        // NOT MEASURED, which is the more dangerous of the two by far.
        //
        // Every at-rest launch here was affected: the accelerating gap, the sequenced
        // extraction, the Paul trap, the rectilinear trap. Absent rather than zero is the
        // rule this project already applies to an undefined Twiss orientation and to a peak
        // width with fewer than two arrivals, and NaN is what the driven branch below
        // already uses for the same reason.
        var maximumEnergyDrift = energyScale > 0.0 ? 0.0 : double.NaN;

        // The speed the ion would have at zero potential. In a mirror this is the
        // drift-region speed, which is the natural yardstick for the turning-point
        // cap because it does not collapse as the ion slows.
        var characteristicSpeed = energyScale > 0.0
            ? Math.Sqrt(2.0 * energyScale / species.MassSi)
            : state.Speed;

        recorder?.Offer(0.0, in state, force: true);

        var derivative = DormandPrince54.Derivative(in state, field, chargeToMass, time.Total);
        fieldEvaluations++;

        // A step may not outrun the field's own resolution. Computed once: the
        // resolution is a property of the field, not of where the ion is.
        var resolutionStep = settings.ResolutionCellsPerStep > 0.0 && characteristicSpeed > 0.0
            ? settings.ResolutionCellsPerStep * field.ResolutionLength / characteristicSpeed
            : double.PositiveInfinity;

        // A step may not outrun the drive either, and the failure is the same one
        // in a different variable. An embedded error estimate compares two
        // solutions of the problem it was given; if every stage of a step lands on
        // the same phase of the cycle both agree and the step is accepted as
        // accurate. It was accurate for the field it was shown. It was not shown
        // the field.
        var driven = field as ITimeVaryingField;

        var periodStep = driven is not null
            ? driven.ShortestPeriodSeconds / StepsPerRfPeriod
            : double.PositiveInfinity;

        // Computed once, since neither of these changes mid-flight.
        //
        // The turning-point cap is off for a driven field because an oscillating
        // ion is at a velocity minimum twice per cycle, so the cap fires
        // continuously and the step collapses. A collisional flight has the same
        // shape for a different reason: an ion thermalising in gas spends its whole
        // life near a velocity minimum, and it is briefly at one after every
        // collision that reverses it.
        //
        // The symptom is not a slow run, it is a wrong outcome. An ion drifting in
        // 1 mbar of nitrogen underflowed after eight steps and 32 ns of a 300 us
        // flight, and reported StepSizeUnderflow - a numerical failure standing in
        // for ordinary physics. The measured evidence in section 11's own findings
        // is that the cap does not help and slightly hurts even where it does fire
        // harmlessly.
        var stepSettings = driven is null && collisions is null
            ? settings
            : settings with { TurningPointStepFactor = 0.0 };

        var step = Math.Min(
            Math.Min(InitialStep(settings, in derivative, characteristicSpeed), resolutionStep), periodStep);
        var outcome = TrajectoryOutcome.MaximumStepsExceeded;
        string? struckSurface = null;

        // A stopping surface is not armed until the flight has been on its
        // positive side. An ion launched exactly on the surface — which is the
        // normal case when one leg of a periodic flight ends where the next
        // begins — would otherwise either stop immediately or, if round-off put
        // it a hair past, never stop at all.
        var armed = stopWhenNegative is null || stopWhenNegative(in state) > 0.0;

        while (accepted < settings.MaximumSteps)
        {
            if (!armed && stopWhenNegative!(in state) > 0.0)
            {
                armed = true;
            }

            var activeStop = armed ? stopWhenNegative : null;

            if (settings.UseAnalyticDrift
                && TryAnalyticDrift(
                    ref state, ref time, field, settings, activeStop,
                    recorder, collisions, ref analyticDistance, out var stoppedOnDrift))
            {
                derivative = DormandPrince54.Derivative(in state, field, chargeToMass, time.Total);
                fieldEvaluations++;

                if (stoppedOnDrift)
                {
                    outcome = TrajectoryOutcome.StopConditionMet;
                    break;
                }

                if (time.Total >= settings.MaximumFlightTime)
                {
                    outcome = TrajectoryOutcome.MaximumFlightTimeReached;
                    break;
                }

                continue;
            }

            // The turning-point cap does not apply to a driven field, and applying
            // it is fatal rather than merely wasteful. It exists because a
            // position-error controller can under-refine at an isolated velocity
            // minimum, such as an ion turning in a mirror. An ion in an
            // oscillating field is at a velocity minimum twice per cycle: turning
            // points are its normal condition, not an event, so the cap fires
            // continuously and drives the step to underflow. Measured: nineteen
            // steps and a quarter of one cycle before the integration gave up.
            step = ApplyStepCaps(
                step, stepSettings, in state, in derivative, characteristicSpeed, resolutionStep);
            step = Math.Min(Math.Min(step, settings.MaximumFlightTime - time.Total), periodStep);

            // A sequencer switches state at known times, and a step that spans one
            // averages two different fields into a single answer. Unlike a boundary
            // in space this needs no root-find - the time is known - so the step is
            // simply cut to land on it. The next step then starts in the new state,
            // with the derivative at the end of the step, which was sampled at the switch
            // and so is already the new state's.
            //
            // Under LandOnEdges the step is also cut to a waveform's edge, and every
            // landing - edge or switch - follows stricter rules: the two end stages sample
            // one ulp before the instant, which is the near side by the field's contract;
            // on acceptance the time is set to the instant exactly, not to a compensated
            // sum that may fall an ulp short of it, and the derivative is evaluated there,
            // on the far side. An instant closer than the minimum step is taken in place,
            // as a collision is, so that two edges an ulp apart cannot underflow the step.
            var landing = false;
            var landAt = double.PositiveInfinity;

            if (driven is not null && settings.LandOnEdges)
            {
                landAt = Math.Min(driven.NextSwitchAfter(time.Total), driven.NextEdgeAfter(time.Total));

                if (double.IsFinite(landAt))
                {
                    var toLanding = landAt - time.Total;

                    if (toLanding < settings.MinimumStep || landAt <= Math.BitIncrement(time.Total))
                    {
                        time = default;
                        time.Add(landAt);
                        derivative = DormandPrince54.Derivative(in state, field, chargeToMass, time.Total);
                        fieldEvaluations++;
                        continue;
                    }

                    if (toLanding <= step)
                    {
                        step = toLanding;
                        landing = true;
                    }
                }
            }
            else if (driven is not null)
            {
                var switchAt = driven.NextSwitchAfter(time.Total);

                if (double.IsFinite(switchAt))
                {
                    step = Math.Min(step, switchAt - time.Total);
                }
            }

            // A collision is an instant, so it is the same kind of event as a
            // sequencer switch and lands the same way: the time is known, so the
            // step is cut to it rather than root-found. A step that spanned one
            // would average the velocity before and after a discontinuity that is
            // the entire physics being modelled.
            var collisionDue = false;

            if (collisions is not null && double.IsFinite(collisions.NextEventSeconds))
            {
                var toEvent = collisions.NextEventSeconds - time.Total;

                if (toEvent < settings.MinimumStep)
                {
                    // Closer than the integrator can resolve. Applying it here
                    // rather than cutting the step to nothing keeps a dense gas from
                    // presenting as step-size underflow, which would look like a
                    // numerical failure and is a physical rate.
                    var here = state.Velocity;
                    var at = state.Position;

                    if (collisions.Collide(time.Total, in at, ref here))
                    {
                        state = new PhaseState(state.Position, here);
                        derivative = DormandPrince54.Derivative(in state, field, chargeToMass, time.Total);
                        fieldEvaluations++;
                    }

                    continue;
                }

                if (toEvent <= step)
                {
                    step = toEvent;
                    collisionDue = true;
                }
            }

            // A collision due strictly earlier cut the step short of the landing, and the
            // step no longer ends on the instant.
            if (landing && step < landAt - time.Total)
            {
                landing = false;
            }

            if (step < settings.MinimumStep)
            {
                outcome = time.Total >= settings.MaximumFlightTime
                    ? TrajectoryOutcome.MaximumFlightTimeReached
                    : TrajectoryOutcome.StepSizeUnderflow;
                break;
            }

            DormandPrince54.Step(
                in state, in derivative, step, field, chargeToMass,
                out var candidate, out var errorPosition, out var errorVelocity, out var candidateDerivative,
                time.Total, landing ? Math.BitDecrement(landAt) : double.NaN);
            fieldEvaluations += 6;

            var error = ErrorNorm(in state, in candidate, in errorPosition, in errorVelocity, settings);

            if (error > 1.0)
            {
                rejected++;
                step *= Math.Max(settings.MinimumStepShrink, settings.SafetyFactor * Math.Pow(error, -0.2));
                continue;
            }

            // The step is accurate enough. Three surfaces can still cut it short,
            // and the earliest wins: the field's own discontinuity, which the ion
            // must land on so that no step straddles it; an electrode, which
            // absorbs it; and the stopping surface, which ends the flight.
            var boundaryStep = BoundaryLandingStep(
                in state, in derivative, in candidate, step, field, chargeToMass, time.Total,
                boundarySurface, ref fieldEvaluations);

            var stopStep = StopLandingStep(
                in state, in derivative, in candidate, step, field, chargeToMass, time.Total,
                activeStop, ref fieldEvaluations);

            var conductorStep = StopLandingStep(
                in state, in derivative, in candidate, step, field, chargeToMass, time.Total,
                conductorSurface, ref fieldEvaluations);

            // Ordered ahead of the detector because an ion that hits metal on the
            // way to a detector did not reach it. Behind the field boundary,
            // because an electrode cannot be on the far side of a discontinuity
            // the ion has not crossed yet.
            if (double.IsFinite(conductorStep) && conductorStep <= boundaryStep
                && !(double.IsFinite(stopStep) && stopStep < conductorStep))
            {
                DormandPrince54.Step(
                    in state, in derivative, conductorStep, field, chargeToMass,
                    out var absorbed, out _, out _, out _, time.Total);
                fieldEvaluations += 6;

                time.Add(conductorStep);
                state = absorbed;
                accepted++;
                recorder?.Offer(time.Total, in state, force: true);

                var impact = state.Position;
                struckSurface = conductors!.ConductorAt(in impact);
                outcome = TrajectoryOutcome.StruckElectrode;
                break;
            }

            if (double.IsFinite(stopStep) && stopStep <= boundaryStep)
            {
                DormandPrince54.Step(
                    in state, in derivative, stopStep, field, chargeToMass,
                    out var landed, out _, out _, out _, time.Total);
                fieldEvaluations += 6;

                time.Add(stopStep);
                state = landed;
                accepted++;
                recorder?.Offer(time.Total, in state, force: true);
                TrackEnergyDrift(
                    in state, species, field, initialEnergy, energyScale, ref maximumEnergyDrift, ref fieldEvaluations);
                outcome = TrajectoryOutcome.StopConditionMet;
                break;
            }

            if (double.IsFinite(boundaryStep))
            {
                DormandPrince54.Step(
                    in state, in derivative, boundaryStep, field, chargeToMass,
                    out var onBoundary, out _, out _, out _, time.Total);
                fieldEvaluations += 6;

                time.Add(boundaryStep);
                state = onBoundary;
                accepted++;
                recorder?.Offer(time.Total, in state, force: true);
                TrackEnergyDrift(
                    in state, species, field, initialEnergy, energyScale, ref maximumEnergyDrift, ref fieldEvaluations);

                // The field on the far side is a different function, so the
                // cached derivative is stale.
                derivative = DormandPrince54.Derivative(in state, field, chargeToMass, time.Total);
                fieldEvaluations++;
                continue;
            }

            time.Add(step);
            state = candidate;
            derivative = candidateDerivative;

            if (landing)
            {
                // The end-of-step derivative was sampled on the near side of the jump.
                time = default;
                time.Add(landAt);
                derivative = DormandPrince54.Derivative(in state, field, chargeToMass, time.Total);
                fieldEvaluations++;
            }

            accepted++;
            recorder?.Offer(time.Total, in state, force: false);

            TrackEnergyDrift(
                in state, species, field, initialEnergy, energyScale, ref maximumEnergyDrift, ref fieldEvaluations);

            // Only on the plain full step. A step cut short by an electrode, a
            // detector or a field discontinuity did not reach the collision, and
            // applying it there would scatter an ion that had already stopped.
            if (collisionDue)
            {
                var velocity = state.Velocity;
                var at = state.Position;

                if (collisions!.Collide(time.Total, in at, ref velocity))
                {
                    state = new PhaseState(state.Position, velocity);

                    // The velocity is discontinuous, so every cached derivative and
                    // the step estimate built from it are stale.
                    derivative = DormandPrince54.Derivative(in state, field, chargeToMass, time.Total);
                    fieldEvaluations++;
                    recorder?.Offer(time.Total, in state, force: true);
                }
            }

            if (time.Total >= settings.MaximumFlightTime)
            {
                outcome = TrajectoryOutcome.MaximumFlightTimeReached;
                break;
            }

            var growth = error > 0.0
                ? settings.SafetyFactor * Math.Pow(error, -0.2)
                : settings.MaximumStepGrowth;

            step *= Math.Clamp(growth, settings.MinimumStepShrink, settings.MaximumStepGrowth);
        }

        return new TrajectoryResult
        {
            FinalState = state,
            FlightTimeSeconds = time.Total,
            TimeCompensation = time.Compensation,
            Outcome = outcome,
            StruckSurface = struckSurface,
            AcceptedSteps = accepted,
            RejectedSteps = rejected,
            FieldEvaluations = fieldEvaluations,
            AnalyticDriftDistance = analyticDistance,
            MaximumRelativeEnergyDrift = maximumEnergyDrift,
        };
    }

    private static TrajectoryStopFunction BoundarySurfaceFor(IElectrostaticField field) =>
        (in PhaseState state) =>
        {
            var position = state.Position;
            return field.SignedDistanceToDiscontinuity(in position);
        };

    /// <summary>
    /// How finely a step must resolve the drive.
    /// </summary>
    /// <remarks>
    /// Twenty steps to a cycle. The integrator is fifth order, so this is far
    /// more than accuracy alone would need; it is set by the fact that the error
    /// estimator cannot see a phase it never sampled, which no order of accuracy
    /// repairs.
    /// </remarks>
    private const double StepsPerRfPeriod = 20.0;

    private static double BoundaryLandingStep(
        in PhaseState state,
        in PhaseDerivative derivative,
        in PhaseState candidate,
        double step,
        IElectrostaticField field,
        double chargeToMass,
        double timeSeconds,
        TrajectoryStopFunction boundarySurface,
        ref long fieldEvaluations)
    {
        var before = boundarySurface(in state);
        fieldEvaluations++;

        // Smooth field, or the ion is sitting exactly on the boundary having just
        // landed on it. In the second case it is leaving, and the next step will
        // see a non-zero value.
        if (!double.IsFinite(before) || before == 0.0)
        {
            return double.PositiveInfinity;
        }

        var after = boundarySurface(in candidate);
        fieldEvaluations++;

        if (!double.IsFinite(after) || Math.Sign(after) == Math.Sign(before))
        {
            return double.PositiveInfinity;
        }

        return LandingStep(
            in state, in derivative, step, field, chargeToMass, boundarySurface, before, timeSeconds,
            ref fieldEvaluations);
    }

    private static double StopLandingStep(
        in PhaseState state,
        in PhaseDerivative derivative,
        in PhaseState candidate,
        double step,
        IElectrostaticField field,
        double chargeToMass,
        double timeSeconds,
        TrajectoryStopFunction? stopWhenNegative,
        ref long fieldEvaluations)
    {
        if (stopWhenNegative is null)
        {
            return double.PositiveInfinity;
        }

        var before = stopWhenNegative(in state);

        if (before <= 0.0 || stopWhenNegative(in candidate) > 0.0)
        {
            return double.PositiveInfinity;
        }

        return LandingStep(
            in state, in derivative, step, field, chargeToMass, stopWhenNegative, before, timeSeconds,
            ref fieldEvaluations);
    }

    /// <summary>
    /// Finds the step size at which a surface function crosses zero, by bisection
    /// on real Runge-Kutta steps.
    /// </summary>
    /// <remarks>
    /// Bisection rather than a secant method: each probe is a full step from the
    /// same starting state, so the landing carries the integrator's own accuracy
    /// rather than an interpolant's, and bisection cannot be defeated by the
    /// curvature near a turning point. It costs about fifty probes per landing,
    /// which against two landings per flight is not worth optimising.
    /// </remarks>
    private static double LandingStep(
        in PhaseState start,
        in PhaseDerivative startDerivative,
        double bracketingStep,
        IElectrostaticField field,
        double chargeToMass,
        TrajectoryStopFunction surface,
        double valueAtStart,
        double timeSeconds,
        ref long fieldEvaluations)
    {
        var low = 0.0;
        var high = bracketingStep;
        var startSign = Math.Sign(valueAtStart);

        for (var i = 0; i < 80; i++)
        {
            var mid = 0.5 * (low + high);

            if (mid <= low || mid >= high)
            {
                break;
            }

            DormandPrince54.Step(
                in start, in startDerivative, mid, field, chargeToMass,
                out var probe, out _, out _, out _, timeSeconds);
            fieldEvaluations += 6;

            var value = surface(in probe);

            if (value == 0.0)
            {
                return mid;
            }

            if (Math.Sign(value) == startSign)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return high;
    }

    private static double InitialStep(
        IntegrationSettings settings, in PhaseDerivative derivative, double characteristicSpeed)
    {
        if (settings.InitialStep > 0.0)
        {
            return settings.InitialStep;
        }

        var acceleration = derivative.Acceleration.Length;

        // Field-free at the start: analytic drift will carry the ion to the first
        // boundary, so the guess only has to be harmless.
        var guess = acceleration > 0.0 && characteristicSpeed > 0.0
            ? 1e-3 * characteristicSpeed / acceleration
            : 1e-9;

        return Math.Min(guess, settings.MaximumStep);
    }

    private static double ApplyStepCaps(
        double step,
        IntegrationSettings settings,
        in PhaseState state,
        in PhaseDerivative derivative,
        double characteristicSpeed,
        double resolutionStep)
    {
        step = Math.Min(step, Math.Min(settings.MaximumStep, resolutionStep));

        if (settings.TurningPointStepFactor <= 0.0)
        {
            return step;
        }

        var acceleration = derivative.Acceleration.Length;

        if (acceleration <= 0.0)
        {
            return step;
        }

        // Only while decelerating. Accelerating away from a mirror is the
        // well-behaved half of the trajectory and does not need the cap.
        if (Vec3.Dot(state.Velocity, derivative.Acceleration) >= 0.0)
        {
            return step;
        }

        return Math.Min(step, settings.TurningPointStepFactor * characteristicSpeed / acceleration);
    }

    private static bool TryAnalyticDrift(
        ref PhaseState state,
        ref CompensatedSum time,
        IElectrostaticField field,
        IntegrationSettings settings,
        TrajectoryStopFunction? stopWhenNegative,
        TrajectoryRecorder? recorder,
        Collisions.CollisionSampler? collisions,
        ref double analyticDistance,
        out bool stopped)
    {
        stopped = false;

        var speed = state.Speed;

        if (speed <= 0.0)
        {
            return false;
        }

        var direction = state.Velocity / speed;
        var position = state.Position;
        var run = field.FieldFreeRunLength(in position, in direction);

        if (run <= DegenerateDriftDistance)
        {
            return false;
        }

        var remaining = settings.MaximumFlightTime - time.Total;

        // A scheduled collision bounds the drift as surely as the flight-time
        // ceiling does. Left out, an analytic advance jumps straight over it - and
        // this is the path a long field-free flight in a thin gas takes, which is
        // exactly the residual-gas case the hard-sphere model exists for.
        //
        // Bounding rather than disabling, because between two collisions the motion
        // really is a straight line: the analytic advance is not an approximation
        // that a gas invalidates, it is the exact solution over a shorter interval.
        if (collisions is not null && double.IsFinite(collisions.NextEventSeconds))
        {
            remaining = Math.Min(remaining, collisions.NextEventSeconds - time.Total);
        }

        if (remaining <= 0.0)
        {
            return false;
        }

        if (!double.IsPositiveInfinity(remaining))
        {
            run = Math.Min(run, remaining * speed);
        }

        if (double.IsPositiveInfinity(run))
        {
            if (stopWhenNegative is null)
            {
                // Field-free forever, no ceiling, and the stopping surface is not
                // yet armed. Integrating is always correct, only slower.
                return false;
            }

            // Unbounded field-free flight with no ceiling. Bracket against the
            // stopping surface instead.
            run = BracketUnboundedDrift(in state, in direction, speed, stopWhenNegative);
        }

        if (stopWhenNegative is not null)
        {
            var atEnd = Drift(in state, in direction, run);

            if (stopWhenNegative(in atEnd) <= 0.0)
            {
                // Straight-line motion, so the crossing distance can be found to
                // machine precision without a single field evaluation.
                run = BisectDrift(in state, in direction, run, stopWhenNegative);
                stopped = true;
            }
        }

        // Both ends of a straight segment, so a figure keeps its endpoints even
        // when the whole drift is a single advance.
        recorder?.Offer(time.Total, in state, force: true);

        state = Drift(in state, in direction, run);
        time.Add(run / speed);
        analyticDistance += run;

        recorder?.Offer(time.Total, in state, force: true);
        return true;
    }

    private static PhaseState Drift(in PhaseState state, in Vec3 direction, double distance) =>
        new(state.Position + (direction * distance), state.Velocity);

    private static double BracketUnboundedDrift(
        in PhaseState state, in Vec3 direction, double speed, TrajectoryStopFunction stop)
    {
        // Grow geometrically until the surface is passed. Field-free and straight,
        // so this costs arithmetic only.
        var run = Math.Max(1.0, speed * 1e-9);

        for (var i = 0; i < 200; i++)
        {
            var probe = Drift(in state, in direction, run);

            if (stop(in probe) <= 0.0)
            {
                return run;
            }

            run *= 2.0;
        }

        return run;
    }

    private static double BisectDrift(
        in PhaseState state, in Vec3 direction, double upper, TrajectoryStopFunction stop)
    {
        var low = 0.0;
        var high = upper;

        // Straight-line geometry: 80 halvings takes the bracket to the last bit of
        // a double regardless of the starting span.
        for (var i = 0; i < 80; i++)
        {
            var mid = 0.5 * (low + high);

            if (mid <= low || mid >= high)
            {
                break;
            }

            var probe = Drift(in state, in direction, mid);

            if (stop(in probe) <= 0.0)
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return high;
    }

    private static double ErrorNorm(
        in PhaseState state,
        in PhaseState candidate,
        in Vec3 errorPosition,
        in Vec3 errorVelocity,
        IntegrationSettings settings)
    {
        var sum = 0.0;
        var atolX = settings.AbsolutePositionTolerance;
        var atolV = settings.AbsoluteVelocityTolerance;
        var rtol = settings.RelativeTolerance;

        sum += Component(errorPosition.X, state.Position.X, candidate.Position.X, atolX, rtol);
        sum += Component(errorPosition.Y, state.Position.Y, candidate.Position.Y, atolX, rtol);
        sum += Component(errorPosition.Z, state.Position.Z, candidate.Position.Z, atolX, rtol);
        sum += Component(errorVelocity.X, state.Velocity.X, candidate.Velocity.X, atolV, rtol);
        sum += Component(errorVelocity.Y, state.Velocity.Y, candidate.Velocity.Y, atolV, rtol);
        sum += Component(errorVelocity.Z, state.Velocity.Z, candidate.Velocity.Z, atolV, rtol);

        return Math.Sqrt(sum / 6.0);

        static double Component(double error, double before, double after, double absolute, double relative)
        {
            var scale = absolute + (relative * Math.Max(Math.Abs(before), Math.Abs(after)));
            var scaled = error / scale;
            return scaled * scaled;
        }
    }

    private static double TotalEnergy(
        in PhaseState state, IonSpecies species, IElectrostaticField field, ref long fieldEvaluations)
    {
        var position = state.Position;
        fieldEvaluations++;
        return (0.5 * species.MassSi * state.Velocity.LengthSquared)
            + (species.ChargeSi * field.PotentialAt(in position));
    }

    /// <summary>
    /// Tracks the energy-conservation diagnostic, where there is one to track.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ACC-4 uses energy drift as a cheap check that the integrator is behaving,
    /// and it has caught real bugs. It rests entirely on the field being static:
    /// a conservative field does no net work, so any change in total energy is
    /// the integrator's error and nothing else.
    /// </para>
    /// <para>
    /// A driven field does work on the ion, deliberately and continuously - that
    /// is what it is for. There is no conserved quantity to compare against, so
    /// the drift is reported as not-a-number rather than as a large number.
    /// Reporting the change against the t = 0 potential would produce a figure
    /// that looks like a diagnostic, moves when the physics moves, and means
    /// nothing.
    /// </para>
    /// <para>
    /// What replaces it for RF is refinement: <c>FlightTimeStudy</c> integrates at
    /// three tolerances and reports the observed order, which tests the same thing
    /// without needing an invariant.
    /// </para>
    /// </remarks>
    private static void TrackEnergyDrift(
        in PhaseState state,
        IonSpecies species,
        IElectrostaticField field,
        double initialEnergy,
        double energyScale,
        ref double maximumDrift,
        ref long fieldEvaluations)
    {
        if (field is ITimeVaryingField)
        {
            maximumDrift = double.NaN;
            return;
        }

        if (energyScale <= 0.0)
        {
            return;
        }

        var energy = TotalEnergy(in state, species, field, ref fieldEvaluations);
        var drift = Math.Abs(energy - initialEnergy) / energyScale;

        if (drift > maximumDrift)
        {
            maximumDrift = drift;
        }
    }
}
