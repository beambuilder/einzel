using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Transport.Collisions;

namespace Einzel.Transport.Diffusion;

/// <summary>One ion population in a mixture: its identity, how it moves, and where it starts.</summary>
/// <param name="Name">
/// What to call it in the result. Named rather than indexed because the interesting output of a
/// mixture run is per species - which one was displaced, which one was lost - and an index is
/// not something a reader can check against the document they wrote.
/// </param>
/// <param name="Species">Its mass and charge.</param>
/// <param name="Mobility">Its mobility, with whatever field dependence was declared.</param>
/// <param name="Initial">Its starting density, on the grid every species shares.</param>
/// <param name="Field">
/// The field <em>this</em> species feels, where that differs from the one the others do; null to
/// use the run's shared field.
/// </param>
/// <remarks>
/// <b>A per-species field is not a convenience, and a driven mixture cannot work without it.</b>
/// The cycle-averaged well an ion feels in an RF field depends on its charge, its mass and its
/// momentum-transfer rate - <c>PonderomotiveField</c> is constructed from all three - so one RF
/// structure presents a <em>different</em> effective potential to every species in it. That is
/// not an artefact to be normalised away: it is the mechanism by which a funnel or a guide is
/// mass-selective at all. A mixture handed one pseudopotential would have every species feeling
/// the well of whichever one that wrapper was built for, silently, so that case is refused.
/// </remarks>
public sealed record MixtureMember(
    string Name,
    IonSpecies Species,
    Mobility Mobility,
    DensityField Initial,
    IElectrostaticField? Field = null);

/// <summary>What became of one species over a mixture run.</summary>
/// <param name="Name">Which species this is.</param>
/// <param name="Density">Its density at the end.</param>
/// <param name="Population">Real ions still in the tracked region.</param>
/// <param name="Collected">Real ions that reached the detector.</param>
/// <param name="Losses">Every other way it left, by the surface the model author named.</param>
/// <param name="Arrivals">When it arrived and how much, for a transit distribution.</param>
/// <param name="StableStepSeconds">
/// The step <em>this</em> species alone would have taken. Reported because a mixture runs at the
/// shortest one and this is how a reader sees what that cost: a run whose step is set by a fast
/// light ion charges every heavy one for it, and nothing else in the result would say so.
/// </param>
public sealed record MixtureSpeciesResult(
    string Name,
    DensityField Density,
    double Population,
    double Collected,
    IReadOnlyDictionary<string, double> Losses,
    IReadOnlyList<(double AtSeconds, double Ions)> Arrivals,
    double StableStepSeconds);

/// <summary>What a mixture run did.</summary>
/// <param name="Species">Each population, in the order given.</param>
/// <param name="Steps">Shared steps taken.</param>
/// <param name="ElapsedSeconds">How far the run got.</param>
/// <param name="StepSeconds">The shared step, which is the shortest any species needed.</param>
/// <param name="StepSetBy">
/// Which species set that step. The single most useful number for deciding what to change: a
/// mixture that costs ten times what one species would is usually paying for one member.
/// </param>
/// <param name="Scheme">Explicit or implicit.</param>
/// <param name="StepGain">What the step was multiplied by, on the implicit path.</param>
/// <param name="Assemblies">How many times the face operators were rebuilt, per species.</param>
/// <param name="SelfFieldSolves">
/// How many times the shared self-potential was solved, or 0 where none was asked for.
/// </param>
/// <param name="PeakSelfPotentialVolts">The largest self-potential anywhere over the run.</param>
/// <param name="NetChargeSi">
/// The net charge the last solve saw, signed. Signed on purpose: a mixture of opposite
/// polarities can hold a great deal of charge and raise almost no field, and a magnitude would
/// hide exactly that.
/// </param>
/// <param name="SelfFieldReport">The last self-field solve's own convergence record.</param>
public sealed record MixtureResult(
    IReadOnlyList<MixtureSpeciesResult> Species,
    int Steps,
    double ElapsedSeconds,
    double StepSeconds,
    string StepSetBy,
    StepScheme Scheme,
    double StepGain,
    int Assemblies,
    int SelfFieldSolves,
    double PeakSelfPotentialVolts,
    double NetChargeSi,
    SolveReport? SelfFieldReport);

/// <summary>
/// Several ion populations stepped together through one field, interacting through their own
/// charge (TRN-1/TRN-2 for a mixture).
/// </summary>
/// <remarks>
/// <para>
/// <b>What makes this one problem rather than several.</b> Every coefficient a species needs is
/// its own: its mobility sets its drift, its charge and the gas temperature set its diffusion
/// through the Einstein relation, its thermal voltage sets the Scharfetter-Gummel exponent, and
/// in a driven structure its mass and damping set the well it feels. If the field were its own
/// too, N species would be N independent runs and could be done one after another. They are
/// coupled by exactly one thing - <b>the potential their total charge raises</b> - and that is
/// why this exists and why the self-field is shared while nothing else is.
/// </para>
/// <para>
/// <b>The step is shared, and it has to be.</b> A mutual field between densities evaluated at
/// different times is not a field between anything: if a light species ran ahead on its own
/// longer step, the potential the heavy one drifted in would be the potential of a distribution
/// that no longer existed. This is the same argument <c>PacketIntegrator</c> makes for a
/// space-charged packet of trajectories, met again in the continuum. The consequence is a cost
/// the caller should be able to see rather than infer, so the shortest step and the species that
/// set it are both reported.
/// </para>
/// <para>
/// <b>Written beside <c>DriftDiffusion.Run</c>, and composed from the same pieces.</b> The loop
/// structure here is new; the coefficient sampling, the stability limit, the face assembly and
/// the step are the identical functions the single-species path calls, shared rather than
/// reimplemented. That is deliberate in both directions: the numerics carry validated numbers
/// and must not be duplicated, while <c>Run</c> itself is left untouched so those numbers stand.
/// A one-species mixture with no self-field is asserted <em>bit-identical</em> to <c>Run</c>,
/// which is what keeps the two from drifting apart later.
/// </para>
/// </remarks>
public static class MixtureDiffusion
{
    /// <summary>Steps a mixture of ion populations through a field.</summary>
    /// <param name="members">The populations, at least one.</param>
    /// <param name="field">The field they share, unless a member declares its own.</param>
    /// <param name="gas">The neutral gas, shared: one temperature, one pressure field, one flow.</param>
    /// <param name="untilSeconds">How long to run for.</param>
    /// <param name="edges">What each domain edge does.</param>
    /// <param name="absorbers">Conductors inside the region, which every species is lost to.</param>
    /// <param name="maximumSteps">A ceiling, so a badly conditioned run stops rather than hangs.</param>
    /// <param name="scheme">Explicit, or implicit with a gain.</param>
    /// <param name="stepGain">How far past the stability limit to push, on the implicit path.</param>
    /// <param name="fieldAt">The shared field as a function of time, for a ramp or a sequence.</param>
    /// <param name="selfField">
    /// The mixture's own charge as a potential, or null to leave every species blind to the
    /// others - which is the control that says what the coupling was worth.
    /// </param>
    /// <param name="progress">
    /// Told how far the solve has got while it is still going, or null to run silently.
    /// A run measured in hours has to be able to say something before it ends, which is
    /// what this is for; nothing it is handed reaches the solve, so a watched run and an
    /// unwatched one are bit-identical.
    /// </param>
    /// <returns>What became of each species, and what the run cost.</returns>
    public static MixtureResult Run(
        IReadOnlyList<MixtureMember> members,
        IElectrostaticField field,
        BackgroundGas gas,
        double untilSeconds,
        DriftDiffusion.DomainEdges edges,
        AbsorbingCells? absorbers = null,
        int maximumSteps = 2_000_000,
        StepScheme scheme = StepScheme.Explicit,
        double stepGain = 1.0,
        Func<double, IElectrostaticField>? fieldAt = null,
        DensitySelfField? selfField = null,
        IDensityProgress? progress = null)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(gas);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(untilSeconds);

        if (members.Count == 0)
        {
            throw new ArgumentException("a mixture needs at least one species", nameof(members));
        }

        // B is applied by single-ion trajectory integration alone; dropping it here would
        // fly the model as though the magnet were absent, with nothing to say so.
        // The time-dependent field is refused where it is first built, below, rather than by
        // building it an extra time here only to test its type.
        Einzel.Fields.Magnetic.MagneticRefusal.ThrowIfMagnetic(field, "the mixture drift-diffusion solve");

        // Names carry the result, so two species answering to one name would make the output
        // ambiguous in the one dimension it exists to resolve.
        var duplicate = members.GroupBy(m => m.Name, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"two species are both called '{duplicate.Key}': a mixture's result is reported by "
                + "name, so the names have to be distinct",
                nameof(members));
        }

        // Refused rather than run: every species must feel the well built for it, and a shared
        // pseudopotential would give them all the one built for whichever species that wrapper
        // was constructed from - a wrong answer with nothing to distinguish it from a right one.
        foreach (var member in members)
        {
            var seen = member.Field ?? field;

            if (seen is PonderomotiveField && member.Field is null && members.Count > 1)
            {
                throw new ArgumentException(
                    $"'{member.Name}' would feel a cycle-averaged well built for another species: the "
                    + "pseudopotential depends on charge, mass and momentum-transfer rate, so a driven "
                    + "mixture needs one PonderomotiveField per species, passed as that member's own "
                    + $"{nameof(MixtureMember.Field)}",
                    nameof(members));
            }
        }

        // The explicit scheme is bounded by its own stability limit, so a gain has nowhere to go.
        // Refused rather than ignored, for the reason the single-species path gives: a caller who
        // asked for a longer step and silently got the short one concludes the scheme is slow.
        if (scheme == StepScheme.Explicit && stepGain != 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stepGain),
                stepGain,
                "the explicit scheme is bounded by its own stability limit, so it cannot take a "
                + "longer step. Ask for StepScheme.Implicit to use a gain.");
        }

        absorbers ??= AbsorbingCells.None;

        var grid = members[0].Initial.Grid;
        var cylindrical = members[0].Initial.Cylindrical;

        foreach (var member in members)
        {
            ArgumentNullException.ThrowIfNull(member.Initial);

            if (member.Initial.Grid.CountX != grid.CountX || member.Initial.Grid.CountY != grid.CountY)
            {
                throw new ArgumentException(
                    $"'{member.Name}' is on a different grid: every species in a mixture shares one, "
                    + "because they share one potential",
                    nameof(members));
            }

            if (member.Initial.Cylindrical != cylindrical)
            {
                throw new ArgumentException(
                    $"'{member.Name}' disagrees with the others about whether the grid is cylindrical, "
                    + "which is a disagreement about what a cell's volume is",
                    nameof(members));
            }
        }

        var state = members.Select(m => new Walker(m, gas, cylindrical)).ToArray();
        var charges = state.Select(w => w.Member.Species.ChargeSi).ToArray();

        // Re-read from the walkers every time rather than captured once. `Commit` swaps each
        // walker's density with its scratch buffer, so a list of the ORIGINAL references holds
        // whichever buffer the walker is not currently using - which after the first swap is
        // the previous step's density. It would alternate between right and one step stale,
        // and every test here would still pass: the bit-identity control has no self-field,
        // the blind control has none by construction, and the charged one asserts a direction
        // that a one-step-stale field still gets right.
        var densities = new DensityField[state.Length];

        void Rebind()
        {
            for (var s = 0; s < state.Length; s++)
            {
                densities[s] = state[s].Density;
            }
        }

        Rebind();

        // The total charge before anything is sampled, so that every species' drift, its
        // Scharfetter-Gummel potential and its stability limit all come from one field rather
        // than from several that agree by construction.
        selfField?.Refresh(densities, charges);

        var at = fieldAt?.Invoke(0.0) ?? field;
        Einzel.Fields.Magnetic.MagneticRefusal.ThrowIfMagnetic(at, "the mixture drift-diffusion solve");

        foreach (var walker in state)
        {
            walker.Sample(at, gas, grid, cylindrical, selfField);
        }

        var assemblies = 1;
        var time = 0.0;
        var steps = 0;

        var (step, setBy) = Shortest(state, stepGain);

        foreach (var walker in state)
        {
            walker.AssembleFaces(grid, edges, absorbers);
        }

        while (time < untilSeconds && steps < maximumSteps)
        {
            // Either the applied field moved, or the mixture's own charge did. The second is a
            // re-sample even with a fixed applied field: a population dense enough to matter
            // changes the field every species is stepped through as it goes, including the
            // others'. That cross term is the whole point of running them together.
            Rebind();

            var selfMoved = steps > 0 && (selfField?.Refresh(densities, charges) ?? false);

            if ((fieldAt is not null || selfMoved) && steps > 0)
            {
                var now = fieldAt?.Invoke(time) ?? at;

                foreach (var walker in state)
                {
                    walker.Sample(now, gas, grid, cylindrical, selfField);
                    walker.AssembleFaces(grid, edges, absorbers);
                }

                (step, setBy) = Shortest(state, stepGain);
                assemblies++;
            }

            var dt = Math.Min(step, untilSeconds - time);

            // Every species advances over the same interval, from the same field, before any of
            // them advances again.
            foreach (var walker in state)
            {
                walker.Advance(absorbers, scheme, dt, time + dt);
            }

            // Swapped only after all of them have stepped: a species advanced onto the new
            // densities of those before it in the list would be reading a field half a step old
            // for some members and current for others, and the list order would then be physics.
            foreach (var walker in state)
            {
                walker.Commit();
            }

            time += dt;
            steps++;

            // WHERE IT HAS GOT TO, WHILE IT IS STILL GOING - per species, because in a
            // mixture the interesting thing is how far apart two populations have moved
            // and a total would hide exactly that. Read only: nothing here reaches the
            // solve, so a run being watched and one not are bit-identical.
            if (progress is not null && progress.Wants(steps, time))
            {
                progress.Reached(new DensityProgressReport(
                    steps,
                    time,
                    untilSeconds,
                    dt,
                    state.Sum(w => w.Collected),
                    [.. state.Select(w =>
                        new DensityProgressSpecies(w.Member.Name, w.Density))]));
            }
        }

        return new MixtureResult(
            [.. state.Select(w => w.Result())],
            steps,
            time,
            step,
            setBy,
            scheme,
            stepGain,
            assemblies,
            selfField?.Solves ?? 0,
            selfField?.PeakVolts ?? 0.0,
            selfField?.ChargeSi ?? 0.0,
            selfField?.Report);
    }

    /// <summary>The shortest step any species needs, and which one needs it.</summary>
    /// <remarks>
    /// The minimum rather than an average or the first: a step stable for one species and not
    /// another is unstable, and the explicit scheme's failure mode is a negative density rather
    /// than a large error.
    /// </remarks>
    private static (double Step, string SetBy) Shortest(Walker[] state, double stepGain)
    {
        var tightest = state[0];

        foreach (var walker in state)
        {
            if (walker.Stable < tightest.Stable)
            {
                tightest = walker;
            }
        }

        return (tightest.Stable * stepGain, tightest.Member.Name);
    }

    /// <summary>
    /// One species' own coefficients, operator, scratch buffer and ledger: everything about it
    /// that is not shared.
    /// </summary>
    private sealed class Walker
    {
        private readonly int _sign;
        private readonly double _thermal;
        private readonly double _number;
        private readonly Dictionary<string, double> _lost = new(StringComparer.Ordinal);
        private readonly List<(double, double)> _arrivals = [];

        private DensityField _next;
        private PonderomotiveWellCache? _well;
        private double[] _driftX = [];
        private double[] _driftY = [];
        private double[] _diffusion = [];
        private double[] _potential = [];
        private double[] _gasX = [];
        private double[] _gasY = [];
        private FaceCoefficients? _faces;
        private double _collected;

        internal Walker(MixtureMember member, BackgroundGas gas, bool cylindrical)
        {
            Member = member;
            Density = member.Initial.Clone();
            _next = new DensityField(member.Initial.Grid, cylindrical);

            _sign = Math.Sign(member.Species.ChargeSi);

            // The density the declared mobility belongs to; a pressure field grades away from it.
            _number = gas.NumberDensitySi;

            // Per species, because it carries the charge: a doubly charged ion feels half the
            // Scharfetter-Gummel exponent per volt of the same potential.
            _thermal = BackgroundGas.BoltzmannSi * gas.TemperatureK / member.Species.ChargeSi;
        }

        internal MixtureMember Member { get; }

        internal DensityField Density { get; private set; }

        /// <summary>Real ions of this species collected so far.</summary>
        /// <remarks>
        /// Exposed for the progress observer, which reports mid-run and so cannot wait for
        /// <c>Result()</c>. Read-only: a watcher that could write into a walker would be a
        /// second author of the run.
        /// </remarks>
        internal double Collected => _collected;

        internal double Stable { get; private set; }

        internal void Sample(
            IElectrostaticField shared,
            BackgroundGas gas,
            Grid2D grid,
            bool cylindrical,
            DensitySelfField? selfField)
        {
            // The species' own field where it has one - which for a driven mixture is its own
            // pseudopotential - and the shared one otherwise.
            var seen = Member.Field ?? shared;

            // One cycle-averaged well per species, kept across re-samples. The well is a
            // property of the APPLIED field, and a re-sample driven by the mixture's own charge
            // leaves that untouched - so rebuilding it at sixteen samples a node over the whole
            // grid, once per species per refresh, is the dominant cost of a driven mean-field
            // run and buys nothing. `Refresh` probes first and rebuilds only if the drive
            // really moved, so a ramped field still gets the right answer.
            if (seen is PonderomotiveField pondered)
            {
                if (_well is null)
                {
                    _well = new PonderomotiveWellCache(grid, pondered);
                }
                else
                {
                    _well.Refresh(pondered);
                }
            }
            else
            {
                _well = null;
            }

            (_driftX, _driftY, _diffusion, _potential, _gasX, _gasY) = DriftDiffusion.SampleCoefficients(
                grid, seen, gas, Member.Mobility, Member.Species, _sign, _number, cylindrical,
                well: _well, selfField: selfField,
                reuse: _driftX.Length == 0 ? null : (_driftX, _driftY, _diffusion, _potential, _gasX, _gasY));

            Stable = DriftDiffusion.StableStep(
                grid, _driftX, _driftY, _gasX, _gasY, _diffusion, Density.LargestRadialWeight());
        }

        internal void AssembleFaces(Grid2D grid, DriftDiffusion.DomainEdges edges, AbsorbingCells absorbers) =>
            _faces = FaceCoefficients.Assemble(
                Density, grid, _driftX, _driftY, _gasX, _gasY, _diffusion, _potential, _thermal,
                edges, absorbers, reuse: _faces);

        internal void Advance(AbsorbingCells absorbers, StepScheme scheme, double dt, double endsAt)
        {
            var leaving = DensityStepper.Advance(Density, _next, _faces!, absorbers, scheme, dt);

            _collected += leaving.Collected;

            if (leaving.Collected > 0.0)
            {
                _arrivals.Add((endsAt, leaving.Collected));
            }

            foreach (var (where, ions) in leaving.Absorbed)
            {
                _lost[where] = _lost.GetValueOrDefault(where) + ions;
            }
        }

        internal void Commit() => (Density, _next) = (_next, Density);

        internal MixtureSpeciesResult Result() => new(
            Member.Name, Density, Density.Population(), _collected, _lost, _arrivals, Stable);
    }
}
