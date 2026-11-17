using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Transport.Integration;

namespace Einzel.Transport.Interaction;

/// <summary>What became of one macroparticle in a packet.</summary>
/// <param name="Outcome">How its flight ended.</param>
/// <param name="FlightTimeSeconds">When, if it reached the stopping surface.</param>
/// <param name="FinalState">Where it was and where it was going when it stopped.</param>
/// <param name="StruckSurface">Which electrode absorbed it, if one did.</param>
public readonly record struct PacketMember(
    TrajectoryOutcome Outcome,
    double FlightTimeSeconds,
    PhaseState FinalState,
    string? StruckSurface);

/// <summary>What a self-consistent packet flight produced.</summary>
/// <param name="Members">One entry per macroparticle, in launch order.</param>
/// <param name="Steps">Steps the whole packet took.</param>
/// <param name="RejectedSteps">Steps the error controller threw away.</param>
/// <param name="MaximumInteractionImbalance">
/// The largest relative imbalance of the mutual force, over every stage of every
/// step. Zero when the ions were flown independently.
/// </param>
/// <remarks>
/// <b>On the imbalance, and on the invariant it is not.</b> Newton's third law
/// makes the mutual accelerations sum to zero, so this is a running check that the
/// pairwise sum stayed balanced for the whole flight - including as members are
/// absorbed and drop out of it, which is where an indexing error would show.
/// <para>
/// It is deliberately <em>not</em> the packet's total momentum. That is conserved
/// only in free flight with nothing absorbed: an applied field is an external
/// force and a detector removes momentum along with the ion carrying it, so a
/// momentum drift measured across a real flight is dominated by two effects that
/// are not errors. Asserting on it would have been asserting that mirrors do not
/// reflect.
/// </para>
/// </remarks>
public sealed record PacketResult(
    IReadOnlyList<PacketMember> Members,
    int Steps,
    int RejectedSteps,
    double MaximumInteractionImbalance)
{
    /// <summary>
    /// Real collisions applied across the packet, summed over every macroparticle;
    /// zero for a flight in vacuum.
    /// </summary>
    public int Collisions { get; init; }
}

/// <summary>
/// Advances a whole packet together, so the ions can push on each other.
/// </summary>
/// <remarks>
/// <para>
/// The engine's ordinary loop flies one ion to its detector, then the next. That
/// is exactly right when the ions do not interact and structurally incapable of
/// space charge when they do: ion 1 has already landed before ion 2 is launched,
/// so there is no instant at which both exist. Modelling the mutual force means
/// inverting the loop — advance every ion one step, recompute their shared field,
/// repeat — which is what this does and why it is a separate integrator rather
/// than a flag on the existing one.
/// </para>
/// <para>
/// <b>Written beside the single-ion path, not by generalising it.</b>
/// <see cref="TrajectoryIntegrator"/> carries every validated number in this
/// engine, and refactoring a numerical core known to be right in order to add a
/// case next to it is how those numbers get quietly lost. The same choice was made
/// for the three-dimensional solver, for the same reason.
/// </para>
/// <para>
/// <b>The step is shared, and it has to be.</b> An adaptive controller per ion
/// would have each of them at a different instant, and a mutual force computed
/// between ions at different times is not a force between anything. So the error
/// norm is the worst over the packet and every macroparticle takes the same step —
/// which costs accuracy on the easy ions and is the price of the interaction being
/// meaningful at all.
/// </para>
/// <para>
/// <b>Dormand-Prince, with the interaction evaluated at every stage.</b> The seven
/// stages sample the field at seven different states, and the mutual part of that
/// field depends on where the <em>other</em> ions are at that same stage. Freezing
/// the interaction across a step and only refreshing it between steps is the
/// cheaper thing and it drops the method to first order in the part that matters,
/// so the stages are evaluated over the whole packet at once.
/// </para>
/// </remarks>
public static class PacketIntegrator
{
    /// <summary>
    /// Below this many macroparticles the applied-field loop runs serially whatever it
    /// costs, because starting a parallel loop over a handful of items cannot pay.
    /// </summary>
    private const int MinimumParallelMembers = 16;

    /// <summary>
    /// A first stage slower than this is worth spreading across cores. Starting a parallel
    /// loop costs some tens of microseconds, so the threshold is several times that: a
    /// solved volume is far above it and an analytic formula far below.
    /// </summary>
    private const double ParallelStageSeconds = 150e-6;

    /// <summary>Flies a packet, with the ions pushing on each other.</summary>
    /// <param name="launch">Starting state of each macroparticle.</param>
    /// <param name="species">The ion's mass and charge.</param>
    /// <param name="field">The applied field.</param>
    /// <param name="interaction">The mutual force, or null to fly them independently.</param>
    /// <param name="settings">Tolerances and ceilings.</param>
    /// <param name="stopWhenNegative">The stopping surface.</param>
    /// <param name="collisions">
    /// One gas sampler per macroparticle, or null for a flight in vacuum. Each
    /// macroparticle collides on its own schedule; what is shared is the step, which
    /// is cut to land on whichever collision comes next across the packet.
    /// </param>
    /// <returns>What became of each macroparticle.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="launch"/> is empty, or <paramref name="collisions"/> does not
    /// have one sampler per macroparticle.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A collision is an instant, so it lands the way it lands on a single
    /// trajectory: the time is known and the step is cut to it. With a packet the
    /// cut is to the earliest scheduled collision of any active member, so a dense
    /// gas costs steps in proportion to the packet's total collision rate. That is
    /// the honest price of a shared step; the alternative - applying each collision
    /// at the end of whatever step contained it - would put every scattered velocity
    /// a fraction of a step from where it happened, and the mutual force that this
    /// integrator exists to compute is evaluated at those positions.
    /// </para>
    /// <para>
    /// A macroparticle standing for many ions scatters them all together. The
    /// kinematics are those of one ion against one neutral, so the velocity
    /// distribution the packet relaxes to is the ion's, sampled at as many points as
    /// there are macroparticles rather than as there are ions. The drag and the
    /// diffusion are right; the fluctuations are those of the smaller sample.
    /// </para>
    /// </remarks>
    public static PacketResult Fly(
        IReadOnlyList<PhaseState> launch,
        IonSpecies species,
        IElectrostaticField field,
        ISelfField? interaction,
        IntegrationSettings settings,
        TrajectoryStopFunction stopWhenNegative,
        IReadOnlyList<Collisions.CollisionSampler>? collisions = null)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(stopWhenNegative);

        if (launch.Count == 0)
        {
            throw new ArgumentException("a packet needs at least one macroparticle", nameof(launch));
        }

        // B is applied by single-ion trajectory integration alone; dropping it here would
        // fly the model as though the magnet were absent, with nothing to say so.
        Einzel.Fields.Magnetic.MagneticRefusal.ThrowIfMagnetic(field, "the space-charge packet integrator");

        if (collisions is not null && collisions.Count != launch.Count)
        {
            throw new ArgumentException(
                $"a packet of {launch.Count} needs one gas sampler per macroparticle, not {collisions.Count}",
                nameof(collisions));
        }

        var count = launch.Count;
        var chargeToMass = species.ChargeSi / species.MassSi;
        var bounded = field as IConductorBounded;

        var state = new PhaseState[count];
        var active = new bool[count];
        var outcome = new TrajectoryOutcome[count];
        var flightTime = new double[count];
        var struck = new string?[count];

        for (var k = 0; k < count; k++)
        {
            state[k] = launch[k];
            active[k] = true;
            outcome[k] = TrajectoryOutcome.MaximumFlightTimeReached;
        }

        var time = 0.0;
        var steps = 0;
        var rejected = 0;
        var imbalance = 0.0;
        var collided = 0;

        if (collisions is not null)
        {
            for (var k = 0; k < count; k++)
            {
                collisions[k].Start(0.0, state[k].Velocity.Length);
            }
        }

        // The applied field's own resolution caps the step for the same reason it
        // does on a single trajectory: a gridded field cannot be sampled coarser
        // than it is stored, however smooth the answer looks.
        var resolutionCap = field.ResolutionLength > 0.0
            ? field.ResolutionLength / Math.Max(SpeedScale(state), double.Epsilon)
            : double.PositiveInfinity;

        var scratch = new Vec3[count];
        var trial = new PhaseState[count];
        var derivative = new PhaseDerivative[count];
        var work = new Workspace(count, derivative);

        // Time the first stage and decide from it whether to use the machine. A tricubic
        // gather over a solved volume costs a thousand times an analytic formula, so the
        // member count alone cannot say whether spreading the loop across cores pays for
        // the twenty microseconds or so it costs to start one.
        var began = System.Diagnostics.Stopwatch.GetTimestamp();
        Derivatives(
            state, active, field, chargeToMass, interaction, time, scratch, work.Positions,
            derivative, parallel: false, ref imbalance);
        var stageSeconds = System.Diagnostics.Stopwatch.GetElapsedTime(began).TotalSeconds;

        work.Parallel = settings.Members switch
        {
            PacketParallelism.Serial => false,
            PacketParallelism.Parallel => Environment.ProcessorCount > 1,
            _ => count >= MinimumParallelMembers
                && Environment.ProcessorCount > 1
                && stageSeconds > ParallelStageSeconds,
        };

        var step = Math.Min(
            Math.Min(InitialStep(settings, derivative, state), resolutionCap), settings.MaximumStep);

        while (AnyActive(active) && time < settings.MaximumFlightTime && steps < settings.MaximumSteps)
        {
            step = Math.Min(step, Math.Min(resolutionCap, settings.MaximumFlightTime - time));
            step = Math.Min(step, ToNearestDiscontinuity(state, active, field));

            if (collisions is not null)
            {
                // The earliest collision of any active member is the next event the
                // packet has to land on. One closer than the integrator can resolve is
                // applied here rather than stepped to, so a dense gas presents as a
                // physical rate and not as step-size underflow.
                var toEvent = ToNextCollision(collisions, active, time);

                if (toEvent < settings.MinimumStep)
                {
                    collided += ApplyDueCollisions(collisions, state, active, time, settings.MinimumStep);
                    Derivatives(
                        state, active, field, chargeToMass, interaction, time, scratch, work.Positions,
                        derivative, work.Parallel, ref imbalance);
                    continue;
                }

                step = Math.Min(step, toEvent);
            }

            var error = Advance(
                state, active, derivative, step, field, chargeToMass, interaction, time,
                scratch, trial, work, out var trialDerivative, ref imbalance);

            var norm = ErrorNorm(error, trial, active, settings);

            if (norm > 1.0 && step > settings.MinimumStep)
            {
                rejected++;
                step = Math.Max(settings.MinimumStep, step * Math.Max(0.2, 0.9 * Math.Pow(norm, -0.2)));
                continue;
            }

            // Landing on the stopping surface: each macroparticle crosses at its
            // own instant inside the step, so the crossing is bracketed per ion
            // rather than for the packet. What is shared is the step, not the exit.
            for (var k = 0; k < count; k++)
            {
                if (!active[k])
                {
                    continue;
                }

                var before = stopWhenNegative(in state[k]);
                var after = stopWhenNegative(in trial[k]);

                if (before > 0.0 && after <= 0.0)
                {
                    var fraction = before / (before - after);

                    active[k] = false;
                    outcome[k] = TrajectoryOutcome.StopConditionMet;
                    flightTime[k] = time + (fraction * step);
                    state[k] = Interpolate(state[k], trial[k], fraction);
                    continue;
                }

                if (bounded is not null && bounded.SignedDistanceToConductor(trial[k].Position) < 0.0)
                {
                    active[k] = false;
                    outcome[k] = TrajectoryOutcome.StruckElectrode;
                    flightTime[k] = time + step;
                    state[k] = trial[k];
                    struck[k] = bounded.ConductorAt(trial[k].Position);
                    continue;
                }

                state[k] = trial[k];
                derivative[k] = trialDerivative[k];
            }

            time += step;
            steps++;

            if (collisions is not null)
            {
                // The step was cut to land on the earliest collision, so whichever
                // members are due scatter now, at the positions they have actually
                // reached, before the derivatives are rebuilt from the new velocities.
                collided += ApplyDueCollisions(collisions, state, active, time, settings.MinimumStep);
            }

            step = Math.Min(
                Math.Min(resolutionCap, settings.MaximumStep),
                step * Math.Clamp(0.9 * Math.Pow(Math.Max(norm, 1e-10), -0.2), 0.2, 5.0));

            // Every macroparticle that left took its charge with it, so the
            // interaction has changed and the derivatives are stale.
            Derivatives(
                state, active, field, chargeToMass, interaction, time, scratch, work.Positions,
                derivative, work.Parallel, ref imbalance);
        }

        var members = new PacketMember[count];

        for (var k = 0; k < count; k++)
        {
            if (active[k])
            {
                outcome[k] = steps >= settings.MaximumSteps
                    ? TrajectoryOutcome.MaximumStepsExceeded
                    : TrajectoryOutcome.MaximumFlightTimeReached;

                flightTime[k] = time;
            }

            members[k] = new PacketMember(outcome[k], flightTime[k], state[k], struck[k]);
        }

        return new PacketResult(members, steps, rejected, imbalance) { Collisions = collided };
    }

    /// <summary>Seconds from now to the earliest scheduled collision of any active member.</summary>
    private static double ToNextCollision(
        IReadOnlyList<Collisions.CollisionSampler> collisions, bool[] active, double time)
    {
        var earliest = double.PositiveInfinity;

        for (var k = 0; k < active.Length; k++)
        {
            if (active[k] && collisions[k].NextEventSeconds < earliest)
            {
                earliest = collisions[k].NextEventSeconds;
            }
        }

        return earliest - time;
    }

    /// <summary>
    /// Scatters every active member whose collision is due at this instant, and
    /// returns how many actually scattered.
    /// </summary>
    /// <remarks>
    /// Due means within half a minimum step of now: the step was cut to the event's
    /// own time, so the two agree to rounding, and a tolerance of one ulp would let
    /// an event fall between two steps and be landed on again next time round with
    /// the packet a step further along.
    /// </remarks>
    private static int ApplyDueCollisions(
        IReadOnlyList<Collisions.CollisionSampler> collisions,
        PhaseState[] state,
        bool[] active,
        double time,
        double minimumStep)
    {
        var scattered = 0;
        var horizon = time + (0.5 * minimumStep);

        for (var k = 0; k < active.Length; k++)
        {
            if (!active[k])
            {
                continue;
            }

            var sampler = collisions[k];

            while (sampler.NextEventSeconds <= horizon)
            {
                var velocity = state[k].Velocity;
                var position = state[k].Position;

                if (sampler.Collide(time, in position, ref velocity))
                {
                    state[k] = new PhaseState(position, velocity);
                    scattered++;
                }
            }
        }

        return scattered;
    }

    /// <summary>
    /// Every buffer a step needs, allocated once for a flight.
    /// </summary>
    /// <remarks>
    /// CMP-1 asks that the inner loop allocate nothing, so that a garbage collection
    /// cannot interrupt a run. This one allocated twenty-two arrays per step - six stage
    /// derivatives, a staged state, an error vector, a position copy per stage, and the
    /// weight lists each stage was called with - which for a packet of a few hundred over
    /// a few hundred thousand steps is a great deal of garbage for no reason.
    /// <para>
    /// The stage weight lists are constant, not merely poolable: which derivative buffer
    /// each Runge-Kutta stage reads is fixed by the tableau, and the buffers themselves
    /// are stable across steps, so the lists are built once here and never rebuilt.
    /// </para>
    /// </remarks>
    private sealed class Workspace
    {
        public Workspace(int count, PhaseDerivative[] k1)
        {
            K2 = new PhaseDerivative[count];
            K3 = new PhaseDerivative[count];
            K4 = new PhaseDerivative[count];
            K5 = new PhaseDerivative[count];
            K6 = new PhaseDerivative[count];
            K7 = new PhaseDerivative[count];
            Staged = new PhaseState[count];
            Error = new Vec3[2 * count];
            Positions = new Vec3[count];

            Terms2 = [(DormandPrince54.A21, k1)];
            Terms3 = [(DormandPrince54.A31, k1), (DormandPrince54.A32, K2)];
            Terms4 = [(DormandPrince54.A41, k1), (DormandPrince54.A42, K2), (DormandPrince54.A43, K3)];
            Terms5 = [(DormandPrince54.A51, k1), (DormandPrince54.A52, K2), (DormandPrince54.A53, K3), (DormandPrince54.A54, K4)];
            Terms6 = [(DormandPrince54.A61, k1), (DormandPrince54.A62, K2), (DormandPrince54.A63, K3), (DormandPrince54.A64, K4), (DormandPrince54.A65, K5)];
            TermsResult = [(DormandPrince54.B1, k1), (DormandPrince54.B3, K3), (DormandPrince54.B4, K4), (DormandPrince54.B5, K5), (DormandPrince54.B6, K6)];
            TermsError =
            [
                (DormandPrince54.E1, k1), (DormandPrince54.E3, K3), (DormandPrince54.E4, K4),
                (DormandPrince54.E5, K5), (DormandPrince54.E6, K6), (DormandPrince54.E7, K7),
            ];
        }

        public PhaseDerivative[] K2 { get; }

        public PhaseDerivative[] K3 { get; }

        public PhaseDerivative[] K4 { get; }

        public PhaseDerivative[] K5 { get; }

        public PhaseDerivative[] K6 { get; }

        public PhaseDerivative[] K7 { get; }

        public PhaseState[] Staged { get; }

        public Vec3[] Error { get; }

        public Vec3[] Positions { get; }

        /// <summary>
        /// Whether the applied-field evaluations are spread across cores. Set once, from a
        /// measurement of the first stage rather than from the member count: what decides
        /// it is members times cost-per-sample, and only one of those is known in advance.
        /// </summary>
        public bool Parallel { get; set; }

        public (double Weight, PhaseDerivative[] Derivative)[] Terms2 { get; }

        public (double Weight, PhaseDerivative[] Derivative)[] Terms3 { get; }

        public (double Weight, PhaseDerivative[] Derivative)[] Terms4 { get; }

        public (double Weight, PhaseDerivative[] Derivative)[] Terms5 { get; }

        public (double Weight, PhaseDerivative[] Derivative)[] Terms6 { get; }

        public (double Weight, PhaseDerivative[] Derivative)[] TermsResult { get; }

        public (double Weight, PhaseDerivative[] Derivative)[] TermsError { get; }
    }

    /// <summary>One Dormand-Prince step over the whole packet.</summary>
    /// <remarks>
    /// The tableau is the same one <see cref="TrajectoryIntegrator"/> uses, applied
    /// a stage at a time across the packet instead of a step at a time down one
    /// trajectory. Written out here rather than shared, because the shared version
    /// evaluates the field for one state and this has to evaluate it for all of
    /// them before any of them can move.
    /// </remarks>
    private static Vec3[] Advance(
        PhaseState[] state,
        bool[] active,
        PhaseDerivative[] k1,
        double step,
        IElectrostaticField field,
        double chargeToMass,
        ISelfField? interaction,
        double time,
        Vec3[] scratch,
        PhaseState[] result,
        Workspace work,
        out PhaseDerivative[] resultDerivative,
        ref double imbalance)
    {
        var count = state.Length;
        var staged = work.Staged;
        var parallel = work.Parallel;

        Stage(state, active, staged, step, work.Terms2);
        Derivatives(staged, active, field, chargeToMass, interaction, time + (DormandPrince54.C2 * step), scratch, work.Positions, work.K2, parallel, ref imbalance);

        Stage(state, active, staged, step, work.Terms3);
        Derivatives(staged, active, field, chargeToMass, interaction, time + (DormandPrince54.C3 * step), scratch, work.Positions, work.K3, parallel, ref imbalance);

        Stage(state, active, staged, step, work.Terms4);
        Derivatives(staged, active, field, chargeToMass, interaction, time + (DormandPrince54.C4 * step), scratch, work.Positions, work.K4, parallel, ref imbalance);

        Stage(state, active, staged, step, work.Terms5);
        Derivatives(staged, active, field, chargeToMass, interaction, time + (DormandPrince54.C5 * step), scratch, work.Positions, work.K5, parallel, ref imbalance);

        Stage(state, active, staged, step, work.Terms6);
        Derivatives(
            staged, active, field, chargeToMass, interaction, time + step, scratch, work.Positions, work.K6, parallel, ref imbalance);

        Stage(state, active, result, step, work.TermsResult);
        Derivatives(
            result, active, field, chargeToMass, interaction, time + step, scratch, work.Positions, work.K7, parallel, ref imbalance);

        Stage(state, active, staged, step, work.TermsError);

        var error = work.Error;

        for (var k = 0; k < count; k++)
        {
            error[k] = result[k].Position - staged[k].Position;
            error[count + k] = result[k].Velocity - staged[k].Velocity;
        }

        resultDerivative = work.K7;

        return error;
    }

    private static void Stage(
        PhaseState[] origin,
        bool[] active,
        PhaseState[] target,
        double step,
        (double Weight, PhaseDerivative[] Derivative)[] terms)
    {
        for (var k = 0; k < origin.Length; k++)
        {
            if (!active[k])
            {
                target[k] = origin[k];
                continue;
            }

            var position = origin[k].Position;
            var velocity = origin[k].Velocity;

            foreach (var (weight, derivative) in terms)
            {
                position += derivative[k].Velocity * (step * weight);
                velocity += derivative[k].Acceleration * (step * weight);
            }

            target[k] = new PhaseState(position, velocity);
        }
    }

    /// <summary>Velocity and acceleration for every member: applied field plus mutual force.</summary>
    /// <remarks>
    /// <para>
    /// The applied-field loop is where a packet run on a solved geometry spends its time,
    /// and every member's evaluation is independent: it reads a field that holds no
    /// mutable state and writes only its own slot. So it may be spread across cores, and
    /// the result is <b>bit-identical however many run it</b> - nothing is summed across
    /// members, so no rounding order can change. That is asserted rather than assumed.
    /// </para>
    /// <para>
    /// The mutual force is not parallelised here. It is a single call over the whole
    /// packet that already vectorises internally, and splitting a symmetric sum across
    /// threads would make the accumulation order depend on the thread count.
    /// </para>
    /// </remarks>
    private static void Derivatives(
        PhaseState[] state,
        bool[] active,
        IElectrostaticField field,
        double chargeToMass,
        ISelfField? interaction,
        double time,
        Vec3[] scratch,
        Vec3[] positions,
        PhaseDerivative[] into,
        bool parallel,
        ref double imbalance)
    {
        for (var k = 0; k < state.Length; k++)
        {
            scratch[k] = default;
        }

        if (interaction is not null)
        {
            for (var k = 0; k < state.Length; k++)
            {
                positions[k] = state[k].Position;
            }

            interaction.Accumulate(positions, active, scratch);

            // Newton's third law, checked rather than assumed, at every stage of
            // every step. It costs one pass over an array against a sum that is
            // already quadratic, and it is the one statement about this sum that is
            // exactly true no matter what the applied field is doing.
            var total = default(Vec3);
            var magnitude = 0.0;

            for (var k = 0; k < state.Length; k++)
            {
                total += scratch[k];
                magnitude += scratch[k].Length;
            }

            if (magnitude > 0.0)
            {
                imbalance = Math.Max(imbalance, total.Length / magnitude);
            }
        }

        if (parallel)
        {
            AppliedInParallel(state, active, field, chargeToMass, time, scratch, into);
            return;
        }

        // The serial loop is written out rather than shared with the parallel branch, and
        // this method contains no lambda at all. Both are deliberate. A lambda anywhere in
        // a method allocates its display class when the method is *entered*, not when the
        // branch holding it is taken: the compiler hoists every captured variable up front.
        // With seven captures that is 72 bytes on every one of the seven stages of every
        // step - 504 bytes a step, measured, and invariant with the member count, which is
        // what gave it away - in the loop CMP-1 asks to allocate nothing. So the lambda
        // lives in its own method, where it costs one closure per call on a path that only
        // runs when the call is worth hundreds of microseconds.
        for (var k = 0; k < state.Length; k++)
        {
            if (!active[k])
            {
                into[k] = default;
                continue;
            }

            var applied = DormandPrince54.Derivative(in state[k], field, chargeToMass, time);

            into[k] = new PhaseDerivative(applied.Velocity, applied.Acceleration + scratch[k]);
        }
    }

    /// <summary>The applied-field loop, spread across cores.</summary>
    /// <remarks>
    /// Its own method so that <see cref="Derivatives"/> holds no lambda and so allocates
    /// no closure on the serial path. See the comment there.
    /// </remarks>
    private static void AppliedInParallel(
        PhaseState[] state,
        bool[] active,
        IElectrostaticField field,
        double chargeToMass,
        double time,
        Vec3[] scratch,
        PhaseDerivative[] into) =>
        System.Threading.Tasks.Parallel.For(0, state.Length, k =>
        {
            if (!active[k])
            {
                into[k] = default;
                return;
            }

            var applied = DormandPrince54.Derivative(in state[k], field, chargeToMass, time);

            into[k] = new PhaseDerivative(applied.Velocity, applied.Acceleration + scratch[k]);
        });

    private static double ErrorNorm(
        Vec3[] error, PhaseState[] trial, bool[] active, IntegrationSettings settings)
    {
        var count = trial.Length;
        var worst = 0.0;

        for (var k = 0; k < count; k++)
        {
            if (!active[k])
            {
                continue;
            }

            var positionScale = settings.AbsolutePositionTolerance
                + (settings.RelativeTolerance * trial[k].Position.Length);

            var velocityScale = settings.AbsoluteVelocityTolerance
                + (settings.RelativeTolerance * trial[k].Velocity.Length);

            worst = Math.Max(worst, error[k].Length / Math.Max(positionScale, double.Epsilon));
            worst = Math.Max(worst, error[count + k].Length / Math.Max(velocityScale, double.Epsilon));
        }

        return worst;
    }

    private static PhaseState Interpolate(in PhaseState from, in PhaseState to, double fraction) =>
        new(
            from.Position + ((to.Position - from.Position) * fraction),
            from.Velocity + ((to.Velocity - from.Velocity) * fraction));

    private static bool AnyActive(bool[] active)
    {
        foreach (var flag in active)
        {
            if (flag)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// How long until the first macroparticle reaches a declared field jump.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two problems, one cap. A field-free region has no acceleration, so the
    /// embedded error estimate correctly reports that an enormous step was accurate
    /// for a straight line and the packet sails through the next electrode without
    /// ever sampling it - the same failure the single-ion path meets and solves with
    /// its own resolution cap. And Dormand-Prince stage 4 carries a coefficient of
    /// -56/15, so a stage can fall outside the step interval and land on the wrong
    /// side of a jump even when both endpoints are inside it.
    /// </para>
    /// <para>
    /// The single-ion path lands on the jump exactly. This one cannot: a shared step
    /// cannot land exactly on a surface that each macroparticle reaches at its own
    /// instant. Stopping short of the first arrival is the honest weaker guarantee -
    /// every stage stays on the side it started - and it is why this integrator is
    /// documented as the reference method for space charge rather than as a
    /// replacement for the one that carries the engine's accuracy numbers.
    /// </para>
    /// </remarks>
    private static double ToNearestDiscontinuity(
        PhaseState[] state, bool[] active, IElectrostaticField field)
    {
        // An ion sitting exactly on the surface would cap the step at zero and the
        // integrator would never move again. A picometre is far below the finest
        // geometry this engine can represent - grids are micrometres at best - so
        // treating an ion this close as already across costs nothing and breaks the
        // deadlock.
        const double AlreadyThere = 1e-12;

        var soonest = double.PositiveInfinity;

        for (var k = 0; k < state.Length; k++)
        {
            if (!active[k])
            {
                continue;
            }

            var distance = Math.Abs(field.SignedDistanceToDiscontinuity(state[k].Position));

            if (!double.IsFinite(distance) || distance <= AlreadyThere)
            {
                continue;
            }

            var speed = state[k].Velocity.Length;

            if (speed > 0.0)
            {
                soonest = Math.Min(soonest, distance / speed);
            }
        }

        return soonest;
    }

    /// <summary>A first step, when the caller did not name one.</summary>
    /// <remarks>
    /// The same heuristic the single-ion path uses - a thousandth of the time the
    /// field would take to change the packet's speed appreciably - taken over the
    /// worst macroparticle, because the step is shared and the worst one sets it.
    /// A packet with no acceleration gets a harmless nanosecond, which the
    /// controller grows within a few steps.
    /// </remarks>
    private static double InitialStep(
        IntegrationSettings settings, PhaseDerivative[] derivative, PhaseState[] state)
    {
        if (settings.InitialStep > 0.0)
        {
            return settings.InitialStep;
        }

        var speed = SpeedScale(state);
        var shortest = double.PositiveInfinity;

        foreach (var one in derivative)
        {
            var acceleration = one.Acceleration.Length;

            if (acceleration > 0.0 && speed > 0.0)
            {
                shortest = Math.Min(shortest, 1e-3 * speed / acceleration);
            }
        }

        return double.IsFinite(shortest) ? shortest : 1e-9;
    }

    private static double SpeedScale(PhaseState[] state)
    {
        var fastest = 0.0;

        foreach (var one in state)
        {
            fastest = Math.Max(fastest, one.Velocity.Length);
        }

        return fastest;
    }
}
