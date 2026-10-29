using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Transport;
using Einzel.Transport.Collisions;
using Einzel.Transport.Diffusion;
using Einzel.Transport.Integration;
using Einzel.Transport.Interaction;

namespace Einzel.Commands;

/// <summary>What one phase of a sequenced run did.</summary>
/// <param name="Name">The phase's name.</param>
/// <param name="Mode">The transport mode it ran in.</param>
/// <param name="DurationSeconds">How long it lasted.</param>
/// <param name="EndsAtSeconds">When it ended, on the instrument's timeline.</param>
/// <param name="Population">
/// How many real ions the packet held when the phase ended.
/// </param>
/// <param name="Trajectories">
/// How many trajectories carried them, or zero in a diffusive phase where there are none.
/// </param>
/// <param name="CentroidMm">Where the packet was when the phase ended, in millimetres.</param>
/// <param name="SpreadMm">
/// How wide the packet was when the phase ended - one standard deviation of position along
/// each axis, in millimetres. Absent where there is nothing to take a width over: a single
/// trajectory, or a phase that lost everything.
/// </param>
/// <param name="Converted">
/// Whether the packet was converted into this phase's description at its start.
/// </param>
/// <param name="Arrived">Trajectories that reached the detector during this phase.</param>
/// <param name="Losses">
/// Trajectories that left another way, by the surface the model author named.
/// </param>
/// <param name="Assemblies">
/// In a diffusive phase, how many times the density solver assembled its operator: once
/// for a field that held through the phase, once per step for one that changed. Zero for
/// a trajectory phase, which has no operator. Reported because the cost of a ramp is
/// otherwise invisible, and so is the cost of a hold mistaken for one.
/// </param>
/// <param name="WellRebuilds">
/// How many of those assemblies computed the ponderomotive well over the whole grid
/// rather than reusing the one before. Zero where nothing is driven; one where a ramp
/// moved only DC, which is what an elution scan does; one per assembly where it moved an
/// RF amplitude. The well is the expensive half of a cycle average, so this is the number
/// that says what the ramp actually cost.
/// </param>
/// <param name="SelfFieldSolves">
/// In a diffusive phase asked for a mean field, how many times the density's own charge was
/// solved as a potential. <c>null</c> where none was asked for, and in a trajectory phase,
/// because a count of zero is a real answer - a solver that ran and never needed to refresh -
/// and a reader cannot tell that from its never having been asked.
/// </param>
/// <param name="PeakSelfPotentialVolts">
/// The largest self-potential anywhere on the grid over the phase, or <c>null</c> on the same
/// terms. Reported per phase rather than once for the run because a sequence is where the
/// packet is compressed: a hold and the pulse that follows it hold the same ions at very
/// different densities, and one number for the run would be the larger of the two with
/// nothing saying which phase it belonged to.
/// </param>
/// <remarks>
/// <para>
/// <b>Every trajectory is accounted for within a phase</b>, which ACC-5 requires and
/// which a leg bounded by time makes easy to get wrong: it ends with some still flying,
/// some arrived and some struck, and only the first group is handed on. Keeping just
/// those would make the packet shrink between phases with nothing saying where the rest
/// went.
/// </para>
/// <para>
/// <b>The ledger closes within a phase and not across a conversion</b>, and that is
/// physics rather than bookkeeping. A density is re-sampled into however many
/// trajectories are asked for, so the trajectory count is a numerical choice on the far
/// side of a boundary while the <em>population</em> - the real ions - is what carries
/// across. That is exactly the <c>ions</c> against <c>population</c> distinction the
/// space-charge work already had to draw, met again from the other direction.
/// </para>
/// </remarks>
public sealed record PhaseOutcome(
    string Name,
    string Mode,
    double DurationSeconds,
    double EndsAtSeconds,
    double Population,
    int Trajectories,
    IReadOnlyList<double> CentroidMm,
    IReadOnlyList<double>? SpreadMm,
    bool Converted,
    int Arrived,
    IReadOnlyList<LossChannel> Losses,
    int Assemblies = 0,
    int WellRebuilds = 0,
    int? SelfFieldSolves = null,
    double? PeakSelfPotentialVolts = null);

/// <summary>What a run across a changing transport mode did.</summary>
/// <param name="Phases">Each phase, in order.</param>
/// <param name="Conversions">How many mode boundaries the packet crossed.</param>
/// <param name="Warnings">
/// Everything the run and its conversions had to say, per GRD-2.
/// </param>
/// <param name="Arrived">Real ions that reached the detector, over every phase.</param>
/// <param name="Losses">
/// Every other way ions left, by named surface, in real ions.
/// </param>
/// <param name="Arrivals">
/// When ions reached the detector during the diffusive legs, on the instrument's clock, one
/// entry per step that collected anything. For a mobility analyser this is the spectrum.
/// Trajectory legs count their arrivals but record no instants here.
/// </param>
/// <remarks>
/// In <em>ions</em> rather than trajectories, because a conversion re-samples and the
/// trajectory count on the far side of one is a numerical choice. Weighted by what each
/// trajectory stood for at the phase it left in, so the totals are comparable across a
/// boundary where the counts are not.
/// </remarks>
public sealed record SequencedOutcome(
    IReadOnlyList<PhaseOutcome> Phases,
    int Conversions,
    IReadOnlyList<ValidityWarning> Warnings,
    double Arrived,
    IReadOnlyList<WeightedLoss> Losses,
    IReadOnlyList<(double TimeSeconds, double Ions)> Arrivals)
{
    /// <summary>
    /// The density the run ended with, where its last phase was a diffusive one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>So that a sequenced packet can be looked at rather than only summarised.</b> A
    /// centroid and a standard deviation are two numbers about a shape, and the questions a
    /// sequenced diffusive run raises are about the shape: whether a packet that will not
    /// elute is held against a barrier, or spread, or in two places. The wholly diffusive
    /// path has written its density since RND-8's argument was answered for it, on the
    /// grounds that a mode whose principal result cannot be looked at in any form is worse
    /// served by silence than by a figure.
    /// </para>
    /// <para>
    /// Null where the run ended in the trajectory description, because then there is no
    /// density - which is a different statement from an empty one, and the two must not
    /// both read as a box with nothing in it.
    /// </para>
    /// </remarks>
    public DensityField? FinalDensity { get; init; }

    /// <summary>The density at each instant the caller asked for, on the run's own clock.</summary>
    /// <remarks>
    /// <para>
    /// <b>An animation is the reason this exists.</b> A film of a packet parking and being
    /// released needs the density at each frame's instant, and the only way to get one used
    /// to be to call the wholly diffusive solver - which runs the model's declared mode over
    /// its whole flight and never looks at the sequence. So a sequenced model rendered as an
    /// unsequenced one, silently: the ramp that releases the packet never ran, and the film
    /// showed it sitting where it was parked.
    /// </para>
    /// <para>
    /// <b>Instants are on the instrument's clock, not the leg's.</b> Each diffusive phase is
    /// handed the subset falling inside it, shifted to its own start, and the global instant
    /// is restored on the way out - the same shift <see cref="TimeShiftedField"/> makes for a
    /// trajectory leg, and for the same reason.
    /// </para>
    /// <para>
    /// An instant landing in a <b>trajectory</b> phase yields nothing, because a trajectory
    /// phase has trajectories and not a density. That is a shortfall the caller can see and
    /// must decide about; filling it with the nearest density would draw a packet somewhere
    /// it never was.
    /// </para>
    /// </remarks>
    public IReadOnlyList<DensitySnapshot> Snapshots { get; init; } = [];

    /// <summary>Phases the model declares, where the run walked fewer of them.</summary>
    /// <remarks>
    /// <b>So that a short walk cannot read as a whole instrument.</b> A caller that asked
    /// for a phase limit knows it did; anything downstream reading the outcome does not,
    /// and a three-phase timeline reported as one finished phase is a different instrument
    /// from one that stopped after its first. Equal to <c>Phases.Count</c> on an ordinary
    /// run, so the comparison is the question rather than a flag somebody has to remember.
    /// </remarks>
    public int PhasesDeclared { get; init; }
}

/// <summary>Ions lost one way, in real ions rather than in trajectories.</summary>
/// <param name="Surface">Where they went, named as the model author named it.</param>
/// <param name="Ions">How many real ions.</param>
public sealed record WeightedLoss(string Surface, double Ions);

/// <summary>
/// A run whose phases are not all in the same transport description (SEQ-1).
/// </summary>
/// <remarks>
/// <para>
/// <b>The instrument this exists for is ordinary.</b> Ions are collected and thermalised
/// in a gas-filled trap, where the right description is a density, and then extracted
/// into vacuum and flown, where it is trajectories. The two modes have been peers since
/// REG-1's seam was built; what was missing was a run that could hold both.
/// </para>
/// <para>
/// <b>Each phase is an ordinary run of its own mode</b>, over the phase's duration, and
/// the orchestration is the boundaries between them. A trajectory leg starting part-way
/// along the timeline is flown against a <see cref="TimeShiftedField"/>, so the
/// integrator - which always starts at t = 0 - sees the instrument at the instant the leg
/// actually begins, and nothing inside it knows a sequence exists.
/// </para>
/// <para>
/// <b>A boundary where the mode changes is where SEQ-1's uncertainty lives.</b> Going one
/// way discards the velocity distribution entirely, because a density has nowhere to
/// hold one; going the other invents it, because a density says nothing about how fast
/// anything is moving. Both are non-suppressible on the result.
/// </para>
/// </remarks>
public static class SequencedRun
{
    /// <summary>Runs a model whose phases change transport mode.</summary>
    /// <param name="model">The model, whose phases carry the modes.</param>
    /// <param name="field">The assembled field.</param>
    /// <param name="gas">The gas, resolved so an imported field reaches both modes.</param>
    /// <param name="progress">
    /// Told which phase is running and what each finished one measured, or null to run
    /// silently. A phase that has finished is a measurement, so handing it over is what
    /// lets a killed run leave the phases that completed behind it.
    /// </param>
    /// <param name="snapshotSeconds">
    /// Instants on the instrument's own clock at which to keep a copy of the density,
    /// ascending, or null for none. Each is served by the diffusive phase containing it;
    /// one landing in a trajectory phase yields nothing, since there is no density there.
    /// </param>
    /// <param name="phaseLimit">
    /// Stop after this many phases, or null to walk the whole timeline.
    /// </param>
    /// <remarks>
    /// <b>Phases rather than an instant, and that is the load-bearing part of the
    /// signature.</b> The obvious spelling is "run until t", and it is wrong here: a
    /// phase's ramp interpolates over the phase's own declared duration, so stopping a
    /// phase early by shortening it makes every parameter it ramps arrive at its end value
    /// early. That is not a short answer, it is a wrong one, and it would validate, solve
    /// and run. A boundary needs no truncation, so nothing about a phase changes when the
    /// walk stops after it.
    /// </remarks>
    /// <returns>What each phase did, and what the conversions cost.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="EinzelException">The model cannot be run this way.</exception>
    public static SequencedOutcome Execute(
        CompiledModel model,
        IElectrostaticField field,
        BackgroundGas gas,
        IRunProgress? progress = null,
        IReadOnlyList<double>? snapshotSeconds = null,
        int? phaseLimit = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(gas);

        if (model.Phases.Count == 0)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/sequence",
                Constraint = "a sequenced run needs a sequence, and this model declares none",
                Suggestion = "add a \"sequence\", or run the model in its declared transport "
                    + "mode with einzel run",
            });
        }

        var species = IonSpecies.FromModel(model);
        var warnings = new List<ValidityWarning>();
        var outcomes = new List<PhaseOutcome>(model.Phases.Count);
        var conversions = 0;

        // Every ion leaves the packet exactly once, and these are where it goes - in
        // real ions, because a conversion re-samples and a trajectory count on the far
        // side of one is a numerical choice rather than a quantity of anything.
        var arrivedTotal = 0.0;
        var lostTotal = new Dictionary<string, double>(StringComparer.Ordinal);
        var arrivals = new List<(double TimeSeconds, double Ions)>();
        var snapshots = new List<DensitySnapshot>(snapshotSeconds?.Count ?? 0);

        // What one trajectory stands for. It starts as the declared population spread
        // over the launched cloud, and a conversion back from a density re-derives it
        // from the population that survived.
        var perTrajectory = Population(model) / Math.Max(model.Cloud.Ions, 1);

        var detectorPoint = model.DetectorPoint;
        var detectorNormal = model.DetectorNormal;
        TrajectoryStopFunction detector =
            (in PhaseState state) => Vec3.Dot(state.Position - detectorPoint, detectorNormal);

        // The packet, in whichever description the current phase uses. Exactly one of
        // these is live at a time - that is what a transport mode *is* - and the
        // conversion at a boundary is what swaps which.
        PhaseState[]? states = null;
        DensityField? density = null;

        var started = 0.0;

        for (var i = 0; i < model.Phases.Count; i++)
        {
            var phase = model.Phases[i];
            var trajectory = string.Equals(phase.Mode, "trajectory", StringComparison.Ordinal);
            var converted = false;

            // ACC-5: arrival or absorption can exhaust the packet before a mode change.
            // Keep the remaining phases and the ledger, but never re-seed or convert zero ions.
            //
            // BEFORE the phase is announced, and the completed phase is still reported to
            // the watcher: a skipped phase is one a killed run should be found having
            // finished, so it belongs in the checkpoint like any other, and announcing
            // entry to a phase that will not step would give a watcher an ETA for no work.
            // `SpreadMm` is null rather than empty - there is no packet to take a width
            // over, which is the same absent-not-zero rule the dash in the phase table
            // follows.
            if (states is { Length: 0 } || (density is not null && density.Population() == 0.0))
            {
                outcomes.Add(new PhaseOutcome(
                    phase.Name, phase.Mode, phase.DurationSeconds, phase.EndsAtSeconds,
                    0.0, 0, [], null, false, 0, []));
                started = phase.EndsAtSeconds;
                progress?.Completed(outcomes[^1]);
                continue;
            }

            // Which phase of how many, so a watcher can say "5 of 8" - a thing the
            // transport stepping one leg has no way to know.
            progress?.Entering(
                phase.Name, i + 1, model.Phases.Count, phase.DurationSeconds);

            // Enter the phase in its own description, converting if the packet is in
            // the other one. The first phase has nothing to convert from: it starts
            // from the source.
            if (trajectory && states is null)
            {
                if (density is null)
                {
                    states = IonCloud.Draw(
                        Launch(model), species, model.Cloud, model.SourceDirection);
                }
                else
                {
                    var back = PacketConversion.ToTrajectories(
                        density,
                        Math.Max(model.Cloud.Ions, 1),
                        species,
                        gas,
                        Instant(field, started),
                        Mobility(model, gas, species),
                        model.Cloud.Seed);

                    states = back.States;
                    perTrajectory = back.PopulationPerIon;
                    warnings.AddRange(back.Warnings);
                    density = null;
                    converted = true;
                    conversions++;
                }
            }
            else if (!trajectory && density is null)
            {
                if (states is null)
                {
                    // A trap-then-extract instrument starts in the trap, so the first
                    // phase being diffusive is the ordinary case rather than a corner.
                    // Seeded by the same function `einzel run` uses for a wholly
                    // diffusive model - one implementation, because `run` and `test`
                    // once computed one flight time two ways and disagreed by 1.3e-10.
                    density = DiffusionRun.Seed(
                        model, DiffusionRun.GridFor(model), Cylindrical(model));
                }
                else
                {
                    var forward = PacketConversion.ToDensity(
                        states, states.Length * perTrajectory,
                        DiffusionRun.GridFor(model), Cylindrical(model));

                    density = forward.Density;
                    warnings.AddRange(forward.Warnings);
                    states = null;
                    converted = true;
                    conversions++;
                }
            }

            // Run the phase in its own mode, for its own duration.
            if (trajectory)
            {
                var (flying, arrived, lost) = Fly(
                    // Subtract the same absolute instants as TimeShiftedField does.
                    // The declared duration can differ by a few ulps and otherwise
                    // leaves an unrepresentable sliver after the last switch.
                    states!, species, field, detector, started, phase.EndsAtSeconds - started, model,
                    gas, perTrajectory, outcomes.Count, warnings);

                states = flying;
                arrivedTotal += arrived * perTrajectory;

                foreach (var (channel, count) in lost)
                {
                    lostTotal[channel] =
                        lostTotal.GetValueOrDefault(channel) + (count * perTrajectory);
                }

                outcomes.Add(new PhaseOutcome(
                    phase.Name, phase.Mode, phase.DurationSeconds, phase.EndsAtSeconds,
                    states.Length * perTrajectory, states.Length,
                    Centroid(states), Spread(states), converted,
                    arrived,
                    [.. lost.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => new LossChannel(pair.Key, pair.Value))]));
            }
            else
            {
                // The instants this phase is responsible for, shifted onto its own clock.
                //
                // Half-open at the END, so an instant landing exactly on a boundary is served
                // by the phase that BEGINS there rather than by the one closing there - the
                // same convention `StageAt` uses, and the two must agree or a frame on a
                // boundary is drawn in the operating point of the phase before it.
                //
                // The LAST phase carries no upper bound at all, which is not the same rule
                // relaxed. Phase ends are accumulated durations, so the declared end of an
                // 80 us run is 80 us only up to rounding - and an animation forces its final
                // frame onto exactly the declared end, which is precisely the instant a
                // comparison against the accumulated one drops. Whether the run actually
                // reached an instant is decided below by the run, which is the thing that
                // knows, rather than here by arithmetic on the durations.
                var last = i == model.Phases.Count - 1;
                var mine = snapshotSeconds is null
                    ? null
                    : snapshotSeconds
                        .Where(at => at >= started && (last || at < phase.EndsAtSeconds))
                        // Snapped to the start, and that is not defensive rounding. A phase
                        // start is an accumulated sum of durations, so an instant asked for
                        // exactly at a boundary lands a few zeptoseconds AFTER it - positive,
                        // so a clamp at zero does nothing. The solver serves an instant at or
                        // before its launch from the density it was handed and anything past
                        // that only at the first step at or after, so on a leg of one or two
                        // steps that one bit dated the packet a whole phase forward: a frame
                        // asked for at the end of the hold was drawn after the push.
                        .Select(at => at - started <= Math.Abs(phase.DurationSeconds) * 1e-9
                            ? 0.0
                            : at - started)
                        .ToList();

                var diffused = Diffuse(
                    density!, model, field, gas, species, started, phase, warnings, progress,
                    mine is { Count: > 0 } ? mine : null);
                density = diffused.Density;

                foreach (var shot in diffused.Snapshots)
                {
                    snapshots.Add(shot with
                    {
                        RequestedSeconds = started + shot.RequestedSeconds,
                        AtSeconds = started + shot.AtSeconds,
                    });
                }

                // An instant asked for at the very END of the phase is served by the density
                // the leg finished with. The solver records at the first step AT OR AFTER
                // what was asked for, and a step sequence set by a stability limit lands a
                // few ulps short of the declared duration far more often than it lands on
                // it - so the instant that matters most to an animation, which forces its
                // last frame onto the end of the flight, was the one reliably dropped.
                //
                // Only where the leg actually reached it. A leg that stopped early because
                // its packet was exhausted has no density at an instant it never ran to,
                // and handing over the last one it had would date a packet forward.
                if (mine is { Count: > 0 } && diffused.Snapshots.Count < mine.Count)
                {
                    var served = diffused.Snapshots.Select(s => s.RequestedSeconds).ToHashSet();

                    foreach (var at in mine.Where(at => !served.Contains(at)
                        && at <= diffused.ElapsedSeconds
                            + (Math.Abs(phase.DurationSeconds) * 1e-9)))
                    {
                        snapshots.Add(new DensitySnapshot(
                            started + at,
                            started + diffused.ElapsedSeconds,
                            diffused.Density.Clone()));
                    }
                }

                // The diffusive leg's own ledger used to be dropped here - `Diffuse`
                // returned the density and nothing else - so a sequenced diffusive run
                // reported no arrivals, no collected count and no named losses. For a
                // mobility analyser the arrivals ARE the result: the elution spectrum is
                // when ions reached the exit, against the ramp. Offset to the instrument's
                // clock, since the leg's own starts at zero.
                arrivedTotal += diffused.Collected;

                foreach (var (where, ions) in diffused.Lost)
                {
                    lostTotal[where] = lostTotal.GetValueOrDefault(where) + ions;
                }

                foreach (var (at, ions) in diffused.Arrivals)
                {
                    arrivals.Add((started + at, ions));
                }

                var (cx, cy) = density.Centroid();
                var (sx, sy) = density.Spread();

                // A density's losses are the solver's own ledger, in ions rather than
                // in counts, and folding them into a trajectory tally would add two
                // different quantities. Reported as the population that survives the
                // phase instead, which is what the next conversion will carry.
                outcomes.Add(new PhaseOutcome(
                    phase.Name, phase.Mode, phase.DurationSeconds, phase.EndsAtSeconds,
                    density.Population(), 0, [cx * 1e3, cy * 1e3],
                    density.Population() > 0.0 ? [sx * 1e3, sy * 1e3] : null,
                    converted, 0, [],
                    diffused.Assemblies, diffused.WellRebuilds,
                    // Asked of the model rather than inferred from the result, and the same
                    // predicate the wholly diffusive path uses. A count of zero is a real
                    // answer - a field that never needed refreshing - so "was one asked for"
                    // is the question, and reading it off a value of zero would answer a
                    // different one.
                    model.ModelsMeanField ? diffused.SelfFieldSolves : null,
                    model.ModelsMeanField ? diffused.PeakSelfPotentialVolts : null));
            }

            // A FINISHED PHASE IS A MEASUREMENT. Handed over whole, so a run killed in
            // the sixth phase leaves the five that finished on disk rather than losing
            // them with the process - which for a study that splits a hold into phases to
            // read a relaxation curve is most of the answer.
            progress?.Completed(outcomes[^1]);

            started = phase.EndsAtSeconds;

            // After the phase is whole, never inside one. See the remarks on the parameter:
            // a phase shortened is a phase whose ramps finish early.
            if (phaseLimit is { } limit && outcomes.Count >= limit)
            {
                break;
            }
        }

        if (conversions > 0)
        {
            warnings.Add(new ValidityWarning(
                "transport.mode-changed-in-sequence",
                $"the packet crossed {conversions} boundary where the transport mode "
                + "changed. Each crossing is a change of description rather than of "
                + "instrument, and neither direction is lossless: read the conversion "
                + "warnings above before comparing anything across one.",
                WarningSeverity.ValidityViolation));
        }

        return new SequencedOutcome(
            outcomes,
            conversions,
            warnings,
            arrivedTotal,
            [.. lostTotal.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new WeightedLoss(pair.Key, pair.Value))],
            arrivals)
        {
            // Whichever description the packet is in at the end. Exactly one of the two is
            // live at a time - that is what a transport mode is - so a null here says the
            // run finished as trajectories rather than that its density was empty.
            FinalDensity = density,
            Snapshots = snapshots,
            PhasesDeclared = model.Phases.Count,
        };
    }

    /// <summary>The field as a leg starting part-way along the timeline sees it.</summary>
    /// <remarks>
    /// The integrator starts at t = 0, so a leg beginning at 100 us has to be handed an
    /// instrument shifted by 100 us rather than a start time it has nowhere to put. A
    /// static field needs no shift and is passed through, which keeps an unsequenced
    /// element bit-identical.
    /// </remarks>
    /// <summary>
    /// Where a phase is asked whether it changes the field: the packet's own centre, and
    /// the four corners of the tracked region half a cell in, so a change anywhere the
    /// density can reach is seen.
    /// </summary>
    private static IEnumerable<Vec3> Probes(DensityField density, Grid2D grid)
    {
        var (cx, cy) = density.Centroid();
        yield return new Vec3(cx, cy, 0.0);

        var dx = 0.5 * grid.SpacingX;
        var dy = 0.5 * grid.SpacingY;
        yield return new Vec3(grid.OriginX + dx, grid.OriginY + dy, 0.0);
        yield return new Vec3(grid.X(grid.CountX - 1) - dx, grid.OriginY + dy, 0.0);
        yield return new Vec3(grid.OriginX + dx, grid.Y(grid.CountY - 1) - dy, 0.0);
        yield return new Vec3(grid.X(grid.CountX - 1) - dx, grid.Y(grid.CountY - 1) - dy, 0.0);
    }

    /// <summary>Whether two potentials differ by more than round-off.</summary>
    private static bool Changed(double before, double after) =>
        Math.Abs(after - before) > 1e-9 * Math.Max(1.0, Math.Abs(before));

    /// <summary>
    /// The potential a slow ion in the gas feels at a point at an instant: the cycle
    /// average where the field is driven, the field itself where it is not.
    /// </summary>
    private static double Felt(
        IElectrostaticField field,
        double atSeconds,
        in Vec3 probe,
        IonSpecies species,
        Mobility mobility,
        BackgroundGas gas)
    {
        var at = Instant(field, atSeconds);
        _ = DiffusionRun.Effective(ref at, species, mobility, gas, atSeconds);
        return at.PotentialAt(in probe);
    }

    private static IElectrostaticField Instant(IElectrostaticField field, double atSeconds) =>
        field is ITimeVaryingField driven && atSeconds > 0.0
            ? new TimeShiftedField(driven, atSeconds)
            : field;

    private static PhaseState Launch(CompiledModel model) =>
        new(model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

    /// <summary>How many real ions the packet holds.</summary>
    /// <remarks>
    /// `population` when declared, and the trajectory count otherwise - the conservative
    /// reading the space-charge screen already established, so a dense packet is never
    /// silently sparse.
    /// </remarks>
    private static double Population(CompiledModel model) =>
        model.Cloud.Population is { } declared and > 0
            ? declared
            : Math.Max(model.Cloud.Ions, 1);

    private static bool Cylindrical(CompiledModel model) =>
        model.Fields.Any(f => f.Solve?.Symmetry == SolveSymmetry.Cylindrical);

    /// <summary>The mobility this run drifts at.</summary>
    /// <remarks>
    /// <b>`Derived` is the part that is easy to drop.</b> A mobility the document derived
    /// from a cross section carries a stored zero-field value that is not the one to use -
    /// the derivation has to be redone against the gas actually resolved for this run.
    /// A first version of this helper read the stored value unconditionally, which is a
    /// different mobility from the one `einzel run` uses on the same model.
    /// </remarks>
    private static Mobility Mobility(
        CompiledModel model, BackgroundGas gas, IonSpecies species) =>
        model.Mobility is { Derived: false } declared
            ? new Mobility(declared.ZeroFieldSi, declared.Alpha, declared.ValidToTownsend)
            : Transport.Diffusion.Mobility.FromCrossSection(gas, species);

    private static double[] Centroid(PhaseState[] states)
    {
        if (states.Length == 0)
        {
            return [0.0, 0.0];
        }

        var x = states.Average(s => s.Position.X);
        var y = states.Average(s => s.Position.Y);

        return [x * 1e3, y * 1e3];
    }

    /// <summary>
    /// How wide the packet is, as one standard deviation of position along each axis.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The same quantity the density solver reports, computed from the other
    /// description.</b> SEQ-1's own subject is that position is the one thing both
    /// descriptions carry, so a width is comparable across a conversion boundary where a
    /// velocity distribution is not - which is what makes it worth reporting in one field
    /// on both sides rather than in two fields named differently.
    /// </para>
    /// <para>
    /// <b>Absent below two members</b>, because a standard deviation over one point is
    /// zero and zero is a real width. The rule the rest of this surface reached after four
    /// non-finite doubles took a serialiser down: an undefined measurement is missing
    /// rather than nought.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<double>? Spread(PhaseState[] states)
    {
        if (states.Length < 2)
        {
            return null;
        }

        var meanX = states.Average(s => s.Position.X);
        var meanY = states.Average(s => s.Position.Y);

        var varianceX = states.Average(s => (s.Position.X - meanX) * (s.Position.X - meanX));
        var varianceY = states.Average(s => (s.Position.Y - meanY) * (s.Position.Y - meanY));

        return [Math.Sqrt(varianceX) * 1e3, Math.Sqrt(varianceY) * 1e3];
    }

    /// <summary>Flies the packet for one phase, and keeps whatever is still in flight.</summary>
    /// <remarks>
    /// Bounded by the phase rather than by a detector: what the next phase needs is where
    /// the packet is when this one ends. An ion that leaves - reaching a detector or
    /// striking metal - simply is not in the list handed on, which is the same accounting
    /// the ensemble figures already use.
    /// </remarks>
    private static (PhaseState[] Flying, int Arrived, Dictionary<string, int> Lost) Fly(
        PhaseState[] states,
        IonSpecies species,
        IElectrostaticField field,
        TrajectoryStopFunction detector,
        double startedAt,
        double durationSeconds,
        CompiledModel model,
        BackgroundGas gas,
        double perTrajectory,
        int phaseIndex,
        List<ValidityWarning> warnings)
    {
        var seen = Instant(field, startedAt);

        var settings = new IntegrationSettings
        {
            RelativeTolerance = model.RelativeTolerance,
            LandOnEdges = model.LandOnEdges,
            MaximumFlightTime = durationSeconds,
        };

        var flying = new List<PhaseState>(states.Length);
        var lost = new Dictionary<string, int>(StringComparer.Ordinal);
        var arrived = 0;

        // Separate reproducible streams per member and leg; restarting the same stream
        // at every boundary would correlate the collisions of successive phases.
        var samplers = gas.IsPresent
            ? Enumerable.Range(0, states.Length).Select(i => new CollisionSampler(
                gas, species.MassSi, species.ChargeSi,
                unchecked(model.Gas.Seed + i + phaseIndex * 1_000_003))).ToArray()
            : null;
        IReadOnlyList<PacketMember> members;
        if (model.ModelsSpaceCharge && states.Length > 1)
        {
            var interaction = FiguresOfMerit.PacketInteraction(
                model, states, species, perTrajectory * states.Length);
            members = PacketIntegrator.Fly(states, species, seen, interaction, settings,
                detector, samplers).Members;
            var smoothing = interaction is CoulombInteraction direct
                ? FormattableString.Invariant($"Plummer softening {direct.SofteningLengthSi:G6} m")
                : "a grid-smoothed self-field";
            // ONCE FOR THE RUN, not once per phase. The caveat is about the method
            // rather than about this phase, so a sixteen-stage staircase would otherwise
            // emit sixteen copies of the same paragraph and bury the warnings that differ -
            // which is the defect this project already fixed once, when a malformed stage
            // was reported per field element and turned one typo into a wall. The phase
            // that first paid it is still named, because that is where a reader looks.
            // The collision count below stays per phase: it is a different number each
            // time and is the reason anybody wants it.
            if (!warnings.Any(w => w.Code == "spacecharge.sequenced-packet"))
            {
                warnings.Add(new ValidityWarning("spacecharge.sequenced-packet",
                    FormattableString.Invariant($"from phase {phaseIndex + 1}: {model.SpaceChargeMode} advances {states.Length} macroparticles together, each representing {perTrajectory:G6} ions, with {smoothing}. ")
                    + "The self-field is rebuilt at every phase boundary; shared steps do not guarantee exact landing on each member's field discontinuities. "
                    + "Collisions, when present, are sampled once per macroparticle, not per represented ion",
                    WarningSeverity.Qualified));
            }
        }
        else
        {
            var individual = new PacketMember[states.Length];
            for (var i = 0; i < states.Length; i++)
            {
                var result = TrajectoryIntegrator.Integrate(states[i], species, seen,
                    settings, detector, collisions: samplers?[i]);
                individual[i] = new PacketMember(result.Outcome, result.FlightTimeSeconds,
                    result.FinalState, result.StruckSurface);
            }
            members = individual;
        }

        if (samplers is not null)
        {
            warnings.AddRange(FlightTimeStudy.CollisionWarnings(
                samplers.Any(s => s.BoundExceeded), samplers.Any(s => s.SampledOutsideDensity),
                samplers.Any(s => s.SampledOutsideFlow)));
            warnings.Add(new ValidityWarning("collisions.sequence-leg",
                $"trajectory phase {phaseIndex + 1}: {samplers.Sum(s => s.Collisions)} collisions "
                + $"across {states.Length} trajectories", WarningSeverity.Provenance));
        }

        foreach (var flight in members)
        {
            if (!flight.Outcome.Completed())
            {
                throw new EinzelException(new EinzelError
                {
                    Code = ErrorCodes.ConvergenceFailed,
                    Path = "/sequence",
                    Constraint = $"trajectory leg stopped with {flight.Outcome} at {flight.FlightTimeSeconds:G17} s of {durationSeconds:G17} s; it cannot hand an unfinished packet to the next phase",
                    Suggestion = "inspect the transport settings and the field near the stopped ion",
                });
            }

            // Three outcomes, and all three are accounted for. An ion that ran the whole
            // phase is handed to the next one; one that reached the detector arrived; one
            // that left any other way is itemised by the surface the model author named.
            //
            // ACC-5 wants every launched ion to appear exactly once, and a leg bounded by
            // time is where that is easiest to get wrong: keeping only the survivors makes
            // the packet shrink between phases with nothing saying where it went.
            switch (flight.Outcome)
            {
                case TrajectoryOutcome.MaximumFlightTimeReached:
                    flying.Add(flight.FinalState);
                    break;

                case TrajectoryOutcome.StopConditionMet:
                    arrived++;
                    break;

                default:
                    var channel = flight.Outcome == TrajectoryOutcome.StruckElectrode
                        ? flight.StruckSurface ?? "an electrode"
                        : flight.Outcome.ToString();

                    lost[channel] = lost.GetValueOrDefault(channel) + 1;
                    break;
            }
        }

        return ([.. flying], arrived, lost);
    }

    /// <summary>Steps the density for one phase.</summary>
    /// <remarks>
    /// A phase that changes the field - a ramp - hands the solver the field as a function
    /// of time, and it re-samples every step. A phase that holds keeps the assemble-once
    /// path, which is bit-identical to what it was. Which one applies is decided by asking
    /// the field whether it differs between the phase's two ends at a probe point, rather
    /// than by asking the phase whether it declares a ramp: the field is what the solver
    /// steps through, and a phase could in principle change it some other way.
    /// </remarks>
    private static DiffusionResult Diffuse(
        DensityField density,
        CompiledModel model,
        IElectrostaticField field,
        BackgroundGas gas,
        IonSpecies species,
        double startedAt,
        CompiledPhase phase,
        List<ValidityWarning> warnings,
        IRunProgress? progress,
        IReadOnlyList<double>? snapshotSeconds = null)
    {
        var grid = DiffusionRun.GridFor(model);

        // The electrodes absorb during a diffusive phase exactly as they do in a wholly
        // diffusive run. Leaving them out would let a density pass through metal, which
        // is the defect that made every diffusive transmission an upper bound with
        // nothing saying so.
        var (absorbers, _) = DiffusionRun.Absorb(model, grid, density);

        var mobility = Mobility(model, gas, species);

        // A driven geometry has no static field to step a density through, and the
        // time-free interface would answer with the RF at this phase's first instant -
        // a field that exists for no length of time. What a slow ion in a gas feels is
        // the cycle average, and `Effective` is the same wrapper the wholly diffusive
        // path uses.
        //
        // Stepping through a snapshot is what this did before, which is the FIFTH time
        // in this project a time-varying quantity reached through a time-free interface
        // has answered at an arbitrary instant rather than failing.
        var seen = Instant(field, startedAt);
        var effective = DiffusionRun.Effective(ref seen, species, mobility, gas, startedAt);

        // What the wrapper did rides out on the result, as it does on a wholly diffusive
        // run. This leg discarded it - the return of Effective was assigned to nothing -
        // so a sequenced diffusive phase that was cycle-averaged over a one-second cycle
        // of a DC ramp said nothing about having been averaged at all. Evidence about a
        // computation's own quality, dropped at a seam, again.
        if (effective is not null)
        {
            foreach (var warning in DiffusionRun.EffectiveFieldWarnings(effective, grid))
            {
                if (!warnings.Any(w => w.Code == warning.Code))
                {
                    warnings.Add(warning);
                }
            }
        }

        // Does the field change over this phase? Asked of the field at the phase's two
        // ends - the last instant INSIDE the phase, not the boundary. A staged field
        // switches to the next phase's weights at the boundary itself (its stage lookup
        // takes t < boundary), so sampling exactly there reads the phase after this one,
        // and a held phase followed by a step looked like a change: the solver then
        // rebuilt its operator every step for a ramp that was not there. Found in review,
        // and it would have been found by the assembly count - which is why that count
        // now rides out per phase.
        //
        // Asked at several points, because a phase can leave the packet's own centre
        // alone and move the field elsewhere; and to a tolerance, because two evaluations
        // of one unchanged field at different absolute times can differ in the last bits.
        //
        // And asked of what the density actually steps through - the cycle average where
        // there is a drive - not of the instantaneous field. An RF that is merely held
        // differs from itself at any two instants that are not a whole number of cycles
        // apart, so the instantaneous potential would call every RF hold a change and the
        // solver would re-assemble its operator every step: sixteen cycle samples at every
        // node, for a ramp that does not exist. The density would be right and nothing
        // would say what it cost.
        Func<double, IElectrostaticField>? fieldAt = null;

        if (field is ITimeVaryingField)
        {
            // ONE ULP inside the end, which is now all that is needed. The period-wide
            // step-back was here because a cycle average taken an ulp inside a phase used
            // to sample the period that FOLLOWS it and so averaged across the boundary
            // into the next phase, calling a held RF a change - 7 assemblies for a 7-step
            // hold when the guarding test ran on the RF template. Holding the operating
            // point fixes that at the root: the average now samples one state whatever
            // window it opens, so there is nothing to step back from.
            //
            // Stepping back a whole period is not merely redundant, it is wrong in a way
            // that got worse the faster the ramp: it probes a DIFFERENT operating point
            // from the phase's end, so on an elution scan the comparison was against a
            // point short of the end and under-detected the change it exists to find.
            //
            // The ulp is still needed, and for a different reason: `StageAt(end)` selects
            // the state that BEGINS at that boundary, so probing exactly at the end asks
            // about the next phase.
            var end = startedAt + phase.DurationSeconds;
            var inside = Math.BitDecrement(end);

            if (Probes(density, grid).Any(probe => Changed(
                    Felt(field, startedAt, in probe, species, mobility, gas),
                    Felt(field, inside, in probe, species, mobility, gas))))
            {
                fieldAt = elapsed =>
                {
                    var now = Instant(field, startedAt + elapsed);
                    _ = DiffusionRun.Effective(ref now, species, mobility, gas, startedAt + elapsed);
                    return now;
                };
            }
        }

        // The same step scheme the wholly diffusive path honours. This leg used to step
        // explicitly whatever the model declared, which made a millisecond ramp cost
        // what a millisecond of explicit stepping costs.
        var scheme = model.DensityStep.IsImplicit
            ? StepScheme.Implicit
            : StepScheme.Explicit;

        // The same self-field the wholly diffusive path builds, from the same helper. A
        // capability wired into one of the two paths and not the other is how this project
        // has four times produced a run that answers while leaving out the physics it was
        // asked for.
        var edges = DiffusionRun.EdgesFor(model, grid);
        var selfField = DiffusionRun.SelfFieldFor(model, grid, absorbers, edges, species);

        var result = DriftDiffusion.Run(
            density,
            seen,
            gas,
            mobility,
            species,
            phase.DurationSeconds,
            edges,
            absorbers,
            scheme: scheme,
            stepGain: model.DensityStep.IsImplicit ? model.DensityStep.Gain : 1.0,
            fieldAt: fieldAt,
            selfField: selfField,
            snapshotSeconds: snapshotSeconds,
            progress: progress);

        if (selfField is not null)
        {
            foreach (var warning in DiffusionRun.SelfFieldWarnings(selfField, result, gas))
            {
                if (!warnings.Any(w => w.Code == warning.Code))
                {
                    warnings.Add(warning);
                }
            }
        }

        return result;
    }
}
