using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Transport.Collisions;

namespace Einzel.Transport.Diffusion;

/// <summary>The density at one instant of a run.</summary>
/// <param name="RequestedSeconds">The instant that was asked for.</param>
/// <param name="AtSeconds">The instant it was actually taken at.</param>
/// <param name="Density">The density there.</param>
/// <remarks>
/// <para>
/// Both times, because they are not the same and the difference is not the caller's to
/// guess. A diffusive step is set by a stability limit and cannot be cut to land on a
/// requested instant without changing the step sequence and therefore the answer - so
/// the snapshot is taken at the first step at or after what was asked for, and says so.
/// </para>
/// <para>
/// The gap is a step, which on the shipped models is nanoseconds against transits of
/// hundreds of microseconds. Reporting it anyway costs one field and removes the
/// question.
/// </para>
/// </remarks>
public sealed record DensitySnapshot(
    double RequestedSeconds,
    double AtSeconds,
    DensityField Density);

/// <summary>What a diffusive run did.</summary>
/// <param name="Density">The density at the end.</param>
/// <param name="Steps">Time steps taken.</param>
/// <param name="ElapsedSeconds">Simulated time.</param>
/// <param name="Remaining">Ions still in the domain.</param>
/// <param name="Collected">Ions that left through the collecting boundary.</param>
/// <param name="Lost">Ions that left any other way, by boundary name.</param>
/// <param name="Arrivals">
/// When ions reached the collecting boundary: one bin per time step, in ions.
/// </param>
public sealed record DiffusionResult(
    DensityField Density,
    int Steps,
    double ElapsedSeconds,
    double Remaining,
    double Collected,
    IReadOnlyDictionary<string, double> Lost,
    IReadOnlyList<(double TimeSeconds, double Ions)> Arrivals)
{
    /// <summary>
    /// How many times the density's own self-potential was solved, or zero where space
    /// charge was not modelled.
    /// </summary>
    /// <remarks>
    /// Reported for the same reason the assembly count is. A held packet's self-field is the
    /// same field however long it is held and costs one solve; an eluting one changes as it
    /// goes. A reader deciding whether to believe a long run needs to know how often the
    /// field was actually brought up to date rather than carried.
    /// </remarks>
    public int SelfFieldSolves { get; init; }

    /// <summary>
    /// The largest self-potential anywhere in the tracked region, in volts, or zero where
    /// space charge was not modelled.
    /// </summary>
    /// <remarks>
    /// Against the potential the applied field drops across the same region, this is what says
    /// whether the packet's own charge matters at all - which is a question worth answering
    /// on every run rather than only where it is large, since a reader who sees a number knows
    /// it was asked.
    /// </remarks>
    public double PeakSelfPotentialVolts { get; init; }

    /// <summary>The charge present in the tracked region at the last self-field solve, in coulombs.</summary>
    public double SelfFieldChargeSi { get; init; }

    /// <summary>
    /// How many times the face operator was assembled: once for a field that holds, once
    /// per step for one that changes.
    /// </summary>
    /// <remarks>
    /// Reported because the cost is otherwise invisible to a suite that only checks answers.
    /// A field that is merely held needs one assembly; a caller that hands the solver a field
    /// function for it anyway pays for a ramp it does not have, and nothing about the density
    /// would say so.
    /// </remarks>
    public int Assemblies { get; init; } = 1;

    /// <summary>
    /// How many of those assemblies computed the ponderomotive well over the whole grid,
    /// rather than reusing the one before.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Zero where there is no drive and so no well to compute; one where the field is
    /// fixed, or where a ramp moved only DC and left the oscillating field alone; one
    /// per assembly where the ramp really did move an RF amplitude.
    /// </para>
    /// <para>
    /// Reported for the same reason <see cref="Assemblies"/> is: the well is the
    /// expensive half of a cycle average - two passes over the field at every node
    /// against one pass over the potential - and a saving nothing reports is a saving
    /// nobody can check. It is also the only place a reader can see that the cache did
    /// its job, since by construction the density is unmoved either way.
    /// </para>
    /// </remarks>
    public int WellRebuilds { get; init; }

    /// <summary>The density at each requested instant, in order.</summary>
    /// <remarks>
    /// <para>
    /// Empty unless instants were asked for. A run reports the density it <em>ended</em>
    /// with, which for a model whose ions have all arrived is an empty box - correctly,
    /// and uselessly, because the interesting picture is the packet in flight. This is
    /// what lets one be drawn without shortening the run and losing everything after it.
    /// </para>
    /// <para>
    /// Only instants the run actually reached appear. One past the end, or past the step
    /// budget, is absent rather than filled in with the final state - a density that was
    /// never computed is not the density at that instant, and silently substituting the
    /// last one would make a film of a finished run look like a film of a running one.
    /// </para>
    /// <para>
    /// Each is a full copy of the grid, so a hundred snapshots of a 128 by 32 grid is
    /// about 3 MB and a hundred of a 512 by 512 grid is 200 MB. The caller chooses how
    /// many; nothing here caps it, because a cap would silently drop frames.
    /// </para>
    /// </remarks>
    public IReadOnlyList<DensitySnapshot> Snapshots { get; init; } = [];

    /// <summary>Which time discretisation was used.</summary>
    public StepScheme Scheme { get; init; } = StepScheme.Explicit;

    /// <summary>The step actually taken, in seconds.</summary>
    public double StepSeconds { get; init; }

    /// <summary>
    /// How many times larger the step was than the explicit scheme could have taken.
    /// </summary>
    /// <remarks>
    /// One on the explicit path, by construction. It is the number the implicit path
    /// exists to make large, so it is reported rather than left to be inferred from a
    /// step count.
    /// </remarks>
    public double StepGain { get; init; } = 1.0;

    /// <summary>Gauss-Seidel sweeps over the whole run, zero on the explicit path.</summary>
    public long Sweeps { get; init; }

    /// <summary>
    /// The largest relative change any implicit step's last sweep still made.
    /// </summary>
    /// <remarks>
    /// Positivity survives a partial solve but conservation does not, so this is the
    /// quantity that says how much a reader may trust the ion ledger. Zero on the
    /// explicit path, which has no inner solve to leave unfinished. A sweep change
    /// rather than a residual norm - see <see cref="StepReport"/> for why it is named
    /// that way.
    /// </remarks>
    public double WorstSweepChange { get; init; }
}

/// <summary>Where ions leave the domain, and what that means.</summary>
public enum Escape
{
    /// <summary>Ions are reflected. A wall the model does not care about.</summary>
    Reflecting,

    /// <summary>Ions leave and are counted as collected. The detector or exit.</summary>
    Collecting,

    /// <summary>Ions leave and are counted as lost. A wall they stick to.</summary>
    Absorbing,
}

/// <summary>
/// Transport as an evolving density: drift down the field, diffusion outward.
/// </summary>
/// <remarks>
/// <para>
/// The second half of REG-1, and the description that applies where trajectory
/// integration does not. Above about 10^-2 mbar the collision frequency vastly
/// exceeds everything else in the problem and residence times are of order a
/// millisecond, so integrating collision by collision is not merely slow - each ion
/// has forgotten where it came from long before it arrives, and what survives is a
/// distribution.
/// </para>
/// <para>
/// The flux between two cells uses the Scharfetter-Gummel form, which is the
/// exponentially-fitted upwind scheme and is <em>exact for a potential that varies
/// linearly across the cell</em>. That matters for the same reason cut cells did in
/// the field solver: centred differencing here is not merely less accurate, it
/// oscillates and produces negative densities as soon as drift outruns diffusion,
/// which in a funnel it does everywhere. A negative density is not a small error, it
/// is a quantity that has stopped meaning anything.
/// </para>
/// <para>
/// Explicit in time, with the step taken from the stability limits rather than
/// declared. That is affordable here because the limits are generous at the
/// pressures this mode is for - a funnel at a millibar runs in a few thousand steps
/// - and it avoids a linear solve per step whose convergence would be a second thing
/// to have to trust.
/// </para>
/// </remarks>
public static class DriftDiffusion
{
    /// <summary>Fraction of the stability limit a step actually takes.</summary>
    /// <remarks>
    /// Both limits below are exact thresholds for the linear problem, and taking
    /// them exactly leaves nothing for the nonlinearity of a field-dependent
    /// mobility. Half is the usual margin and costs a factor of two in steps.
    /// </remarks>
    private const double StabilityMargin = 0.5;

    /// <summary>Evolves a density until a time, or until the ions have gone.</summary>
    /// <param name="initial">The starting density.</param>
    /// <param name="field">The electrostatic field driving the drift.</param>
    /// <param name="gas">The gas, for temperature and number density.</param>
    /// <param name="mobility">The declared mobility (TRN-1).</param>
    /// <param name="species">The ion, for its charge sign.</param>
    /// <param name="untilSeconds">How long to run.</param>
    /// <param name="edges">What happens at each domain edge.</param>
    /// <param name="absorbers">
    /// Interior cells that swallow whatever reaches them, named by surface, or null
    /// where the tracked region has no geometry in it.
    /// </param>
    /// <param name="maximumSteps">A runaway guard.</param>
    /// <param name="scheme">
    /// Which time discretisation to use. The explicit one is bounded by the faster of
    /// diffusion and Courant; the implicit one has no stability bound at all.
    /// </param>
    /// <param name="stepGain">
    /// How many times the explicit stability limit to step, for the implicit scheme.
    /// </param>
    /// <param name="snapshotSeconds">
    /// Instants to record the density at, in seconds and in order, or null for none.
    /// Each is taken at the first step at or after it, and reports both times.
    /// </param>
    /// <param name="fieldAt">
    /// The field as a function of the simulated time, for a phase during which it changes -
    /// a ramp. Null for the ordinary run, where the field is fixed and the operator is
    /// assembled once. When given, the drift is re-sampled and the face operator rebuilt at
    /// every step, and the stability limit recomputed with it.
    /// </param>
    /// <param name="selfField">
    /// The density's own charge, coupled back into the field it is stepped through, or null
    /// for a run in which the ions do not push on each other.
    /// </param>
    /// <param name="progress">
    /// Told how far the solve has got while it is still going, or null to run silently.
    /// A run measured in hours has to be able to say something before it ends, which is
    /// what this is for; nothing it is handed reaches the solve, so a watched run and an
    /// unwatched one are bit-identical.
    /// </param>
    /// <returns>What happened.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The duration is not positive.</exception>
    public static DiffusionResult Run(
        DensityField initial,
        IElectrostaticField field,
        BackgroundGas gas,
        Mobility mobility,
        IonSpecies species,
        double untilSeconds,
        DomainEdges edges,
        AbsorbingCells? absorbers = null,
        int maximumSteps = 2_000_000,
        StepScheme scheme = StepScheme.Explicit,
        double stepGain = 1.0,
        IReadOnlyList<double>? snapshotSeconds = null,
        Func<double, IElectrostaticField>? fieldAt = null,
        DensitySelfField? selfField = null,
        IDensityProgress? progress = null)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(gas);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(untilSeconds);

        absorbers ??= AbsorbingCells.None;

        var grid = initial.Grid;
        var density = initial.Clone();
        var next = new DensityField(grid, initial.Cylindrical);

        var sign = Math.Sign(species.ChargeSi);

        // The density the declared mobility belongs to. A pressure field grades the
        // gas away from it, and mobility goes as the reciprocal of density, so this
        // is the reference the scaling is against rather than the value used.
        var number = gas.NumberDensitySi;

        var at = fieldAt?.Invoke(0.0) ?? field;

        // B is applied by single-ion trajectory integration alone; dropping it here would
        // fly the model as though the magnet were absent, with nothing to say so.
        Einzel.Fields.Magnetic.MagneticRefusal.ThrowIfMagnetic(field, "the drift-diffusion solve");
        Einzel.Fields.Magnetic.MagneticRefusal.ThrowIfMagnetic(at, "the drift-diffusion solve");

        // The well is the expensive half of a cycle average and, in an elution scan, the
        // half that cannot have changed: the ramp walks the DC gradient down and holds
        // every RF amplitude. Kept per node across the steps of a ramp and checked at a
        // spread of probes at each one, so a document that really does ramp an amplitude
        // gets the right answer without the saving. Only on the ramped path - a fixed
        // field assembles once and has nothing to save.
        // Built whenever the field is a cycle average, not only when it is ramped. The reason
        // it was gated on `fieldAt` is that a fixed field assembles once and has nothing to
        // save - which stopped being true when the density's own charge started forcing
        // re-samples. A self-field re-sample leaves the APPLIED field exactly where it was, so
        // the well is unchanged by construction and rebuilding it is pure waste: at sixteen
        // samples per node over the whole grid, per species, it is the dominant cost of a
        // driven mean-field run and it buys nothing.
        var wellCache = at is PonderomotiveField effective
            ? new PonderomotiveWellCache(grid, effective)
            : null;

        // One well computation for a fixed driven field, none where there is no drive.
        var wellRebuilds = wellCache?.Rebuilds ?? (at is PonderomotiveField ? 1 : 0);

        // Sampled once when the field is fixed, which it is for every run except a
        // ramped phase of a sequence. When `fieldAt` is given the field is a function of
        // time and these are re-sampled every step inside the loop below - the comment
        // that stood here said a sequenced run "would need this inside the loop, and does
        // not exist yet", and a TIMS elution ramp is that run.
        // The density's own charge, before anything is sampled from the field: the applied
        // field and the self-field are added into one total per node, so that the drift, the
        // potential the flux is built from and the stability limit all come from the same
        // field rather than from two that agree by construction.
        selfField?.Refresh(density);

        var (driftX, driftY, diffusion, potential, gasX, gasY) = SampleCoefficients(
            grid, at, gas, mobility, species, sign, number, initial.Cylindrical, wellCache,
            selfField);

        // The thermal voltage, which is what turns a potential difference across a
        // face into the exponent Scharfetter-Gummel needs.
        var thermal = BackgroundGas.BoltzmannSi * gas.TemperatureK / species.ChargeSi;

        var stable = StableStep(
            grid, driftX, driftY, gasX, gasY, diffusion, density.LargestRadialWeight());

        // The explicit scheme cannot exceed its stability limit; the implicit one has
        // none, so what bounds it is accuracy and the caller says how far to push.
        // Refusing a gain on the explicit path rather than ignoring it, because a
        // caller who asked for a longer step and silently got the short one would
        // conclude the scheme is slow rather than that the request went nowhere.
        if (scheme == StepScheme.Explicit && stepGain != 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stepGain),
                stepGain,
                "the explicit scheme is bounded by its own stability limit, so it cannot "
                + "take a longer step. Ask for StepScheme.Implicit to use a gain.");
        }

        var step = stable * stepGain;

        // The operator is assembled once: everything that decides a face coefficient -
        // the mesh, the mobility, the field, the gas - is fixed for the whole run. The
        // explicit path used to recompute two exponentials per face per step, which a
        // driven funnel pays about a million times over.
        var faces = FaceCoefficients.Assemble(
            density, grid, driftX, driftY, gasX, gasY, diffusion, potential, thermal,
            edges, absorbers);

        var arrivals = new List<(double, double)>();
        var lost = new Dictionary<string, double>(StringComparer.Ordinal);

        var collected = 0.0;
        var time = 0.0;
        var steps = 0;
        var assemblies = 1;
        var sweeps = 0L;
        var worstChange = 0.0;

        var snapshots = new List<DensitySnapshot>(snapshotSeconds?.Count ?? 0);
        var pending = 0;

        // An instant at or before the launch is the initial density, which no step
        // produces. Taken here so that a caller asking for t = 0 gets the packet as it
        // was seeded rather than as it was after one step.
        while (pending < (snapshotSeconds?.Count ?? 0) && snapshotSeconds![pending] <= time)
        {
            snapshots.Add(new DensitySnapshot(snapshotSeconds[pending], time, density.Clone()));
            pending++;
        }

        while (time < untilSeconds && steps < maximumSteps)
        {
            // A time-varying field: re-sample the drift at THIS instant and rebuild the
            // face operator on it. The step is recomputed too, because a rising ramp
            // shortens the drift-limited step and a step chosen at t = 0 would then be
            // unstable - a falling ramp only ever loosens it, which is why this was not
            // noticed on the first ramp tried. The cost is one assembly per step, which
            // is what the assemble-once path was built to avoid; here it is the price of
            // the field actually changing, and it is paid only when it does.
            // Either the applied field has moved, or the density has moved far enough that
            // its own field has. The second is a re-sample even when the applied field is
            // fixed, which is why this is not simply the ramped path: a packet whose charge
            // matters changes the field it is stepped through as it goes.
            var selfMoved = steps > 0 && (selfField?.Refresh(density) ?? false);

            if ((fieldAt is not null || selfMoved) && steps > 0)
            {
                var now = fieldAt?.Invoke(time) ?? at;

                // The cache holds the well and the field of the moment supplies the
                // direct term, so it has to be pointed at this step's field before
                // anything is read through it. `Refresh` probes first and rebuilds only
                // if the well has moved.
                if (wellCache is not null && now is PonderomotiveField pondered)
                {
                    wellCache.Refresh(pondered);
                    wellRebuilds = wellCache.Rebuilds;
                }
                else if (fieldAt is not null)
                {
                    // A field that stopped being a cycle average part way through a
                    // phase cannot happen today, and dropping the cache rather than
                    // reading a well through a field it was not built from is the
                    // reading that stays correct if it ever does. Only where the APPLIED
                    // field moved: a re-sample driven by the density's own charge leaves
                    // the drive exactly where it was, so the well it holds is still the
                    // well of the field being read.
                    wellCache = null;
                }

                (driftX, driftY, diffusion, potential, gasX, gasY) = SampleCoefficients(
                    grid, now, gas, mobility, species, sign, number, initial.Cylindrical,
                    wellCache, selfField, (driftX, driftY, diffusion, potential, gasX, gasY));

                stable = StableStep(
                    grid, driftX, driftY, gasX, gasY, diffusion, density.LargestRadialWeight());
                step = stable * stepGain;

                faces = FaceCoefficients.Assemble(
                    density, grid, driftX, driftY, gasX, gasY, diffusion, potential, thermal,
                    edges, absorbers, reuse: faces);
                assemblies++;
            }

            var dt = Math.Min(step, untilSeconds - time);

            var leaving = DensityStepper.Advance(
                density, next, faces, absorbers, scheme, dt);

            (density, next) = (next, density);

            time += dt;
            steps++;

            sweeps += leaving.Sweeps;
            worstChange = Math.Max(worstChange, leaving.SweepChange);

            collected += leaving.Collected;

            if (leaving.Collected > 0.0)
            {
                arrivals.Add((time, leaving.Collected));
            }

            foreach (var (where, ions) in leaving.Absorbed)
            {
                lost[where] = lost.GetValueOrDefault(where) + ions;
            }

            // At the first step at or after each requested instant. The step is set by a
            // stability limit and cutting it to land exactly would change the step
            // sequence and so the answer, which is a high price for an offset of one
            // step - so the instant actually taken is reported instead.
            while (pending < (snapshotSeconds?.Count ?? 0) && snapshotSeconds![pending] <= time)
            {
                snapshots.Add(new DensitySnapshot(snapshotSeconds[pending], time, density.Clone()));
                pending++;
            }

            // WHERE IT HAS GOT TO, WHILE IT IS STILL GOING. Read only: nothing here
            // reaches the solve, so a run being watched and one not are bit-identical.
            // The moments a consumer wants are full passes over the grid, which is why
            // it is asked whether it wants them before they are computed.
            if (progress is not null && progress.Wants(steps, time))
            {
                progress.Reached(new DensityProgressReport(
                    steps, time, untilSeconds, dt, collected, [new DensityProgressSpecies(string.Empty, density)]));
            }
        }

        return new DiffusionResult(
            density, steps, time, density.Population(), collected, lost, arrivals)
        {
            Snapshots = snapshots,
            Scheme = scheme,
            StepSeconds = step,
            StepGain = stepGain,
            Sweeps = sweeps,
            WorstSweepChange = worstChange,
            SelfFieldSolves = selfField?.Solves ?? 0,
            PeakSelfPotentialVolts = selfField?.PeakVolts ?? 0.0,
            SelfFieldChargeSi = selfField?.ChargeSi ?? 0.0,
            Assemblies = assemblies,
            WellRebuilds = wellRebuilds,
        };
    }

    /// <summary>What happens at each edge of the domain.</summary>
    /// <param name="MinX">The lower x edge.</param>
    /// <param name="MaxX">The upper x edge.</param>
    /// <param name="MinY">The lower y edge, which is the axis in a cylindrical solve.</param>
    /// <param name="MaxY">The upper y edge.</param>
    public readonly record struct DomainEdges(
        Escape MinX = Escape.Absorbing,
        Escape MaxX = Escape.Collecting,
        Escape MinY = Escape.Reflecting,
        Escape MaxY = Escape.Absorbing);

    /// <summary>
    /// The largest step both stability limits allow.
    /// </summary>
    /// <remarks>
    /// Two separate conditions, and which one binds says what the run is doing.
    /// Diffusion binds as h squared over D, so refining the mesh costs quadratically;
    /// drift binds as h over v, only linearly. A run whose step is set by diffusion
    /// is one where the mesh, not the physics, is the expense.
    /// </remarks>
    /// <summary>
    /// The step a run will take, from the mesh and the coefficients alone.
    /// </summary>
    /// <param name="grid">The grid the density is tracked on.</param>
    /// <param name="diffusionSi">The diffusion coefficient, in square metres per second.</param>
    /// <param name="fastestCrossingRateSi">
    /// The largest value of |vx|/hx + |vy|/hy anywhere, in reciprocal seconds, or
    /// zero when the field is not known. A <em>rate</em> rather than a speed,
    /// because that is what the Courant condition is on: an axial drift on an
    /// anisotropic mesh crosses a cell at a different rate from a diagonal one of
    /// the same speed, and quoting a speed loses which.
    /// </param>
    /// <param name="largestRadialWeight">
    /// The largest conservative face weight anywhere on the grid, from
    /// <see cref="DensityField.LargestRadialWeight"/>. One in the plane, four on the
    /// axis of a cylindrical solve.
    /// </param>
    /// <returns>The step, and which limit set it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Public because a cost estimate needs it before the run rather than after, and
    /// GRD-8 gates on a number that has to be available without doing the work.
    /// </para>
    /// <para>
    /// The diffusion limit is knowable without solving anything - D comes from the
    /// mobility and the temperature, and the mesh is declared - while the drift limit
    /// needs the field. So an estimate that has not solved the field can bound the
    /// step from above and say so, which is the right direction: an estimate that
    /// runs under is worse than one that runs over.
    /// </para>
    /// </remarks>
    public static (double Seconds, string Limit) StepFor(
        Fields.Solved.Grid2D grid,
        double diffusionSi,
        double fastestCrossingRateSi = 0.0,
        double largestRadialWeight = 1.0)
    {
        ArgumentNullException.ThrowIfNull(grid);

        // A weighted radial face scales the outward coefficient with it, so the
        // largest weight anywhere scales the limit. One in the plane; four on the
        // axis of a cylindrical solve, where the cell is a disc rather than a ring.
        // Taking the step from the unweighted rate there is stepping four times too
        // far, at the one place a funnel puts most of its ions.
        var weight = Math.Max(1.0, largestRadialWeight);

        var inverseSquares = (1.0 / (grid.SpacingX * grid.SpacingX))
            + (weight / (grid.SpacingY * grid.SpacingY));

        var byDiffusion = diffusionSi > 0.0
            ? 1.0 / (2.0 * diffusionSi * inverseSquares)
            : double.PositiveInfinity;

        var byDrift = fastestCrossingRateSi > 0.0
            ? 1.0 / (weight * fastestCrossingRateSi)
            : double.PositiveInfinity;

        if (double.IsPositiveInfinity(byDiffusion) && double.IsPositiveInfinity(byDrift))
        {
            return (1e-6, "neither: nothing is moving");
        }

        return byDiffusion <= byDrift
            ? (StabilityMargin * byDiffusion, "diffusion")
            : (StabilityMargin * byDrift, "drift");
    }

    internal static double StableStep(
        Fields.Solved.Grid2D grid,
        double[] driftX,
        double[] driftY,
        double[] gasX,
        double[] gasY,
        double[] diffusion,
        double largestRadialWeight)
    {
        var fastest = 0.0;
        var widest = 0.0;

        for (var k = 0; k < diffusion.Length; k++)
        {
            // The Courant condition is on how fast the ions actually move, which is
            // the field drift and the gas carrying them added. Taking it from the
            // field alone would let a fast gas outrun the step in a model whose own
            // field is weak - which is precisely the funnel case, where the gas is
            // half the mechanism.
            fastest = Math.Max(
                fastest, CrossingRate(grid, driftX[k] + gasX[k], driftY[k] + gasY[k]));

            widest = Math.Max(widest, diffusion[k]);
        }

        // The same function the cost estimate calls, so the two cannot disagree
        // about what a run will do. An estimate computed by a second implementation
        // of the step rule is an estimate of that implementation.
        return StepFor(grid, widest, fastest, largestRadialWeight).Seconds;
    }

    /// <summary>How fast a drift crosses a cell, in reciprocal seconds.</summary>
    /// <param name="grid">The grid.</param>
    /// <param name="driftXSi">Drift along x, in metres per second.</param>
    /// <param name="driftYSi">Drift along y, in metres per second.</param>
    /// <returns>The Courant rate.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    public static double CrossingRate(Fields.Solved.Grid2D grid, double driftXSi, double driftYSi)
    {
        ArgumentNullException.ThrowIfNull(grid);

        return (Math.Abs(driftXSi) / grid.SpacingX) + (Math.Abs(driftYSi) / grid.SpacingY);
    }

    /// <summary>
    /// Minus the gradient of a per-node potential, by central differences on the grid, with
    /// one-sided differences at an edge.
    /// </summary>
    /// <remarks>
    /// One-sided rather than reflected at the edges: what the edge condition is belongs to the
    /// solve that produced the potential, and guessing at it here would be a second, quieter
    /// statement of it. A one-sided difference is first-order where a central one is second,
    /// which at the wall of a bore is where the density is smallest.
    /// </remarks>
    private static Vec3 SelfGradient(
        Fields.Solved.Grid2D grid, ReadOnlySpan<double> potential, int i, int j)
    {
        var k = (j * grid.CountX) + i;

        var left = i > 0 ? potential[k - 1] : potential[k];
        var right = i + 1 < grid.CountX ? potential[k + 1] : potential[k];
        var spanX = ((i > 0 ? 1 : 0) + (i + 1 < grid.CountX ? 1 : 0)) * grid.SpacingX;

        var below = j > 0 ? potential[k - grid.CountX] : potential[k];
        var above = j + 1 < grid.CountY ? potential[k + grid.CountX] : potential[k];
        var spanY = ((j > 0 ? 1 : 0) + (j + 1 < grid.CountY ? 1 : 0)) * grid.SpacingY;

        return new Vec3(
            spanX > 0.0 ? -(right - left) / spanX : 0.0,
            spanY > 0.0 ? -(above - below) / spanY : 0.0,
            0.0);
    }

    internal static (double[] DriftX, double[] DriftY, double[] Diffusion, double[] Potential, double[] GasX, double[] GasY) SampleCoefficients(
        Fields.Solved.Grid2D grid,
        IElectrostaticField field,
        BackgroundGas gas,
        Mobility mobility,
        IonSpecies species,
        int sign,
        double number,
        bool cylindrical,
        PonderomotiveWellCache? well = null,
        DensitySelfField? selfField = null,
        (double[] DriftX, double[] DriftY, double[] Diffusion, double[] Potential, double[] GasX, double[] GasY)? reuse = null)
    {
        var count = grid.CountX * grid.CountY;

        // CHECKED, as `FaceCoefficients.Assemble` checks its own scratch object. A
        // workspace sized for another grid would index past the end at best, and where it
        // is longer than this grid needs it would leave the tail holding another run's
        // values for a later full-length read to consume. Every cell of every array below
        // is written unconditionally - there is no `continue` in the sampling loop - so
        // matching lengths are the whole of what reuse requires here, which is why these
        // are not cleared the way the face operator's are.
        if (reuse is { } scratch
            && (scratch.DriftX.Length != count || scratch.DriftY.Length != count
                || scratch.Diffusion.Length != count || scratch.Potential.Length != count
                || scratch.GasX.Length != count || scratch.GasY.Length != count))
        {
            throw new ArgumentException(
                "the scratch coefficient arrays must each be "
                + $"{count} long for this grid", nameof(reuse));
        }

        var driftX = reuse?.DriftX ?? new double[count];
        var driftY = reuse?.DriftY ?? new double[count];
        var diffusion = reuse?.Diffusion ?? new double[count];
        var potential = reuse?.Potential ?? new double[count];
        var gasX = reuse?.GasX ?? new double[count];
        var gasY = reuse?.GasY ?? new double[count];

        // SPREAD ACROSS CORES, and the results do not depend on how many.
        //
        // Every node writes only its own slot in the six arrays and nothing is summed
        // across nodes, so this is bit-identical however the rows are divided - which is
        // the property that makes it safe rather than a numerical trade. What it reads is
        // read-only for the duration: the well cache's `Total` is a lookup plus a field
        // evaluation and only `Refresh` writes it, the self-potential is a finished array,
        // and neither the gas nor the mobility accumulates anything while being sampled.
        //
        // Worth it because this is the expensive half of a driven diffusive step: on the
        // shipped TIMS front end an assembly is about 99 ms of a 26 s probe run, and most
        // of that is here - seven cycle-mean potential evaluations per node, each a
        // superposition over the solved channels.
        if (count >= ParallelNodeThreshold)
        {
            SampleRowsInParallel(
                grid, field, gas, mobility, species, sign, number, well, selfField,
                driftX, driftY, diffusion, potential, gasX, gasY);
        }
        else
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                SampleRow(
                    j, grid, field, gas, mobility, species, sign, number, well, selfField,
                    driftX, driftY, diffusion, potential, gasX, gasY);
            }
        }


        // On the axis of a cylindrical solve there is no radial direction, so a
        // radial drift there is a discretisation artefact rather than a velocity.
        // The same argument applies to the gas: a neutral flow with a radial
        // component on the axis is describing a jet emerging from the axis itself.
        if (cylindrical)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                driftY[i] = 0.0;
                gasY[i] = 0.0;
            }
        }

        return (driftX, driftY, diffusion, potential, gasX, gasY);
    }


    /// <summary>
    /// How many density nodes make spreading the coefficient sweep across cores worth its
    /// dispatch, measured rather than chosen: a row of the shipped front end's 513-node grid
    /// is over a millisecond of work, which is three orders above the cost of handing it to
    /// a thread, while a 32 by 16 test grid is not.
    /// </summary>
    /// <remarks>
    /// Verified by being forced to <b>1</b> for a whole suite run, so every diffusive test took
    /// the parallel path: the Boltzmann equilibrium still held to 8.9e-16, the ion ledger to
    /// 100.0000 per cent and the corpus drift tube to L/(mu E). That found one defect and it
    /// was in a test's own counter rather than in the physics - a non-atomic increment losing
    /// counts once the loop it instrumented was threaded.
    /// </remarks>
    private const int ParallelNodeThreshold = 4096;

    /// <summary>One row of the coefficient sweep.</summary>
    /// <remarks>
    /// <para>
    /// Its own method, with the parameters named as the enclosing locals were, so the body
    /// moved out of the loop <b>verbatim</b>. A numerical core is the wrong place to retype
    /// arithmetic that is already known to be right, and a long parameter list is the cheaper
    /// price - the same trade the 3-D solver made by being written beside the 2-D one rather
    /// than by generalising it.
    /// </para>
    /// <para>
    /// And a method rather than a local function or a lambda, because a lambda's captures are
    /// hoisted when the method is <i>entered</i> and not when its branch is taken - so writing
    /// this inline would allocate a closure on the serial path that never uses it. That cost
    /// 504 bytes a step in <c>PacketIntegrator</c> before it was found; see the remarks on
    /// <c>AppliedInParallel</c> there.
    /// </para>
    /// </remarks>
    private static void SampleRow(
        int j,
        Fields.Solved.Grid2D grid,
        IElectrostaticField field,
        BackgroundGas gas,
        Mobility mobility,
        IonSpecies species,
        int sign,
        double number,
        PonderomotiveWellCache? well,
        DensitySelfField? selfField,
        double[] driftX,
        double[] driftY,
        double[] diffusion,
        double[] potential,
        double[] gasX,
        double[] gasY)
    {

            for (var i = 0; i < grid.CountX; i++)
            {
                var k = (j * grid.CountX) + i;

                var point = new Vec3(grid.X(i), grid.Y(j), 0.0);

                // Through the cache where there is one, which reads the well it already
                // holds and recomputes only the direct term. The arithmetic is the
                // field's own, in the field's own order, so a run with a cache and a run
                // without one are the same numbers to the bit.
                var electric = well is null
                    ? field.ElectricFieldAt(in point)
                    : well.ElectricFieldAt(k, in point);

                // The density's own contribution, added to the applied field before the
                // mobility is taken so both come from one total field. Its gradient is a
                // central difference on the density's own grid, which is the mesh the
                // self-potential was solved on - differencing it any finer would be
                // differencing the interpolation between nodes rather than the field.
                if (selfField is not null)
                {
                    electric += SelfGradient(grid, selfField.Potential, i, j);
                }

                var strength = Math.Sqrt((electric.X * electric.X) + (electric.Y * electric.Y));

                // Sampled per node, like the flow below and for the same reason. The
                // mobility carries two separate density dependences and this moves
                // both: it goes as 1/n outright, and its field expansion is in E/n.
                // Where no pressure field is declared this is the model's own
                // density at every node, the ratio is exactly one, and the result is
                // bit-identical to what it was.
                var here = gas.NumberDensityAt(in point);
                var local = mobility.At(strength, here, number);

                driftX[k] = sign * local * electric.X;
                driftY[k] = sign * local * electric.Y;

                diffusion[k] = Mobility.DiffusionSi(gas.TemperatureK, species.ChargeSi, local);

                potential[k] = well is null
                    ? field.PotentialAt(in point)
                    : well.PotentialAt(k, in point);

                // And into the potential the flux is built from. Scharfetter-Gummel takes a
                // difference across a face, so a per-node scalar added here keeps the flux
                // antisymmetric between two cells and the scheme exactly conservative.
                if (selfField is not null)
                {
                    potential[k] += selfField.Potential[k];
                }

                // Sampled per node rather than taken once, even though only a
                // uniform flow can be declared today. A flow field is what GAS-1
                // asks for above 1e-2 mbar and the face averaging below is only
                // correct if the velocity is known at both nodes of a face.
                var flow = gas.VelocityAt(in point);

                gasX[k] = flow.X;
                gasY[k] = flow.Y;
            }
        
    }

    /// <summary>The coefficient sweep, one row per work item.</summary>
    /// <remarks>
    /// Rows rather than nodes, so each work item walks memory the way the serial loop did and
    /// the six arrays are written in contiguous runs. See the remarks on
    /// <see cref="SampleRow"/> for why this is a separate method.
    /// </remarks>
    private static void SampleRowsInParallel(
        Fields.Solved.Grid2D grid,
        IElectrostaticField field,
        BackgroundGas gas,
        Mobility mobility,
        IonSpecies species,
        int sign,
        double number,
        PonderomotiveWellCache? well,
        DensitySelfField? selfField,
        double[] driftX,
        double[] driftY,
        double[] diffusion,
        double[] potential,
        double[] gasX,
        double[] gasY) =>
        System.Threading.Tasks.Parallel.For(0, grid.CountY, j =>
            SampleRow(
                j, grid, field, gas, mobility, species, sign, number, well, selfField,
                driftX, driftY, diffusion, potential, gasX, gasY));
}
