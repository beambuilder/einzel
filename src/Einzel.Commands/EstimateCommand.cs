using Einzel.Core.Model;
using Einzel.Fields.Solved;

namespace Einzel.Commands;

/// <summary>What one field element will cost to solve.</summary>
public sealed record ElementEstimate
{
    /// <summary>Which field element of the model this is.</summary>
    public required int Index { get; init; }

    /// <summary>The field type.</summary>
    public required string Type { get; init; }

    /// <summary>Node counts, x then y.</summary>
    public required IReadOnlyList<int> Nodes { get; init; }

    /// <summary>Total nodes.</summary>
    /// <remarks>
    /// A long rather than an int, because a volume grid can hold more nodes than an int
    /// counts and truncating that would report a huge solve as a small one - which is
    /// the failure this whole estimate exists to prevent.
    /// </remarks>
    public required long NodeCount { get; init; }

    /// <summary>The most this element's solve holds live at once, in mebibytes.</summary>
    /// <remarks>
    /// The solver's own statement of what it allocates (<c>SolveMemoryModel</c>), measured
    /// against real builds: about 150 bytes a node for a volume solve on the Galerkin
    /// hierarchy, 51 on the rediscretized one, 153 for a plane solve, plus 8 a node for each
    /// basis channel solved before the last. It was 48 for all three, which put the Astral at
    /// a 1 mm cell at 3.1 GiB where it needed 15.3, and needs 9.5 now that the cut links hold
    /// their cut arms alone.
    /// </remarks>
    public required double MemoryMiB { get; init; }

    /// <summary>Estimated seconds to solve.</summary>
    public required double Seconds { get; init; }

    /// <summary>The cell size the document asked for, in metres.</summary>
    public double RequestedCell { get; init; }

    /// <summary>The cell size each axis actually got, in metres.</summary>
    /// <remarks>
    /// <b>Never coarser than requested, and often much finer.</b> Each axis rounds its own
    /// interval count up to a power of two, so a domain whose extents are not near powers
    /// of two is meshed finer than asked on every axis - and the node count is the product
    /// of three such roundings. On a 635 x 48 x 350 mm analyser at a requested 1 mm that is
    /// 0.62 x 0.75 x 0.68 mm and <b>3.2x the nodes</b>, silently.
    /// </remarks>
    public IReadOnlyList<double> Spacing { get; init; } = [];
}

/// <summary>What a study multiplies its model's cost by.</summary>
/// <remarks>
/// A study is the operation somebody actually plans a multi-day run against, and
/// <c>estimate</c> took a model - so the number it gave was the cost of one evaluation
/// out of a search that declares hundreds. The multiplier needs no pilot and no run: a
/// study file <b>states</b> its own scan points, draw count or optimiser budget.
/// </remarks>
public sealed record StudyEstimate
{
    /// <summary>The study file, as an absolute path.</summary>
    public required string StudyPath { get; init; }

    /// <summary>Which of the four drivers this study runs.</summary>
    public required string Kind { get; init; }

    /// <summary>How many times the figure of merit is evaluated.</summary>
    public required int Evaluations { get; init; }

    /// <summary>
    /// Whether that count is a ceiling the search may stop short of, or a number it will
    /// certainly reach.
    /// </summary>
    /// <remarks>
    /// A scan computes every point it declares; an optimiser stops when it converges and
    /// a bisection when its bracket closes. Reporting a ceiling as a certainty would
    /// overstate a search that usually converges early, and reporting a certainty as a
    /// ceiling would let somebody plan for less work than there is.
    /// </remarks>
    public required bool EvaluationsAreACeiling { get; init; }

    /// <summary>How many trajectories one evaluation flies.</summary>
    public required int Members { get; init; }

    /// <summary>What one evaluation costs, in seconds.</summary>
    public required double SecondsPerEvaluation { get; init; }

    /// <summary>How the evaluation count was arrived at.</summary>
    public required string Basis { get; init; }
}

/// <summary>What a model will cost to run.</summary>
public sealed record EstimateOutcome
{
    /// <summary>The model file, as an absolute path.</summary>
    public required string ModelPath { get; init; }

    /// <summary>One entry per field element, in document order.</summary>
    public required IReadOnlyList<ElementEstimate> Elements { get; init; }

    /// <summary>Estimated total seconds.</summary>
    public required double Seconds { get; init; }

    /// <summary>Estimated peak live memory, in mebibytes.</summary>
    /// <remarks>
    /// <para>
    /// <b>What has to fit, not what the process will show.</b> Elements are built in order and
    /// each keeps its solved potentials, so this is the largest of what every earlier element
    /// retains plus what the next one holds while it solves.
    /// </para>
    /// <para>
    /// The collector frees a large array lazily when memory is plentiful, so the working set
    /// of a real run is higher - 1.6x on the Astral at 4 mm - and it gives that back under
    /// pressure: the same solve completed under a hard heap limit 7 per cent above this.
    /// </para>
    /// </remarks>
    public required double MemoryMiB { get; init; }

    /// <summary>Whether this exceeds the cost threshold and should be confirmed.</summary>
    public required bool AboveThreshold { get; init; }

    /// <summary>What the threshold is, in seconds.</summary>
    public required double ThresholdSeconds { get; init; }

    /// <summary>How the estimate was arrived at, so it can be argued with.</summary>
    public required string Basis { get; init; }

    /// <summary>
    /// What one trajectory costs, of <see cref="Seconds"/>.
    /// </summary>
    /// <remarks>
    /// Split out because a study flies many trajectories through <em>one</em> solve, so
    /// the two terms scale differently and a total cannot be multiplied as a whole.
    /// </remarks>
    public double TrajectorySeconds { get; init; }

    /// <summary>The study this costs, or null when a model was costed directly.</summary>
    public StudyEstimate? Study { get; init; }

    /// <summary>
    /// How far the repeated pilots spread, as a fraction of the cheapest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>How firm the number is, which was measured and then discarded.</b> GRD-1 asks that
    /// a quantitative result carry a measure of its own uncertainty, and an estimate somebody
    /// plans a multi-day run against is such a result - the more so since it stopped being a
    /// quoted constant and started being measured on the machine that will do the work.
    /// </para>
    /// <para>
    /// Zero means nothing was repeated, not that the number is exact: a documented rate, an
    /// uncalibrated estimate, or a pilot so expensive that the repeat budget allowed one
    /// attempt all report zero, and the basis says which. Taking the cheapest of several is
    /// what makes this a floor rather than a confidence interval - it is the observed spread
    /// of runs on an otherwise idle machine, and says nothing about a loaded one.
    /// </para>
    /// </remarks>
    public double PilotSpread { get; init; }
}

/// <summary>
/// Estimates what a model costs before running it.
/// </summary>
/// <remarks>
/// <para>
/// GRD-8 gates operations above a cost threshold. Gating needs a number to gate
/// on, and it has to be available without doing the work - which rules out
/// anything measured and leaves a model of the cost.
/// </para>
/// <para>
/// The model here is deliberately crude and deliberately explicit about being
/// crude: multigrid work is proportional to node count, with a constant measured
/// on this codebase's own solves. It reports the basis it used alongside the
/// number so a caller can see it is an estimate and not a measurement. An
/// estimate presented with the same confidence as a result is worse than no
/// estimate, and this is the same argument GRD-1 makes about bare numbers.
/// </para>
/// <para>
/// It is honest about what it does not cover: trajectory integration cost depends
/// on the path, which depends on the field, which is the thing not yet solved.
/// </para>
/// </remarks>
public static class EstimateCommand
{
    /// <summary>
    /// Seconds per million nodes for a converged multigrid solve.
    /// </summary>
    /// <remarks>
    /// Measured on the shipped templates: a 129 by 129 quadrupole with four
    /// interior rods solves in roughly 210 ms, which is about 12 s per million
    /// nodes; the mirror pair's 513 by 33 boundary-value geometry is faster per
    /// node. The larger figure is used, because an estimate that runs under is
    /// worse than one that runs over.
    /// </remarks>
    private const double SecondsPerMegaNode = 13.0;

    /// <summary>
    /// The same figure for a volume solve, when nothing better has been measured.
    /// </summary>
    /// <remarks>
    /// A volume cycle carries a 27-point stencil where a plane carries five, and its
    /// coarse levels are built by Galerkin rather than rediscretised. Measured at 65 s per
    /// million nodes on the shipped segmented quadrupole - five times the plane rate, which
    /// is why quoting the plane's figure for a volume was reported as a floor and read as
    /// an estimate.
    /// </remarks>
    private const double VolumeSecondsPerMegaNode = 65.0;

    /// <summary>How much coarser the calibration pilot is than the solve it stands for.</summary>
    /// <remarks>
    /// Two in each direction, so a plane pilot is a quarter of the nodes and a volume pilot
    /// an eighth. Extrapolating on node count is sound because multigrid cycle counts are
    /// grid-independent - measured at 8/7/7/7 from 32 to 256 intervals - so the work per
    /// node is what stays fixed as the mesh changes, and that is the thing being measured.
    /// </remarks>
    private const double PilotCoarsening = 2.0;

    /// <summary>Below this the pilot measured scheduling noise rather than the solve.</summary>
    private const double PilotFloorMs = 15.0;

    /// <summary>How much of the declared flight a trajectory pilot actually flies.</summary>
    /// <remarks>
    /// A twentieth, which is enough to leave the launch transient behind on the models
    /// here and cheap enough to run inside an estimate. If the ion finishes inside it -
    /// arrives, or strikes something - there is nothing to extrapolate and the pilot IS
    /// the answer.
    /// </remarks>
    private const double PilotFlightFraction = 0.05;

    /// <summary>A bound on the pilot itself, so an estimate cannot become the run.</summary>
    private const int PilotStepCeiling = 400_000;

    /// <summary>Above this, GRD-8 asks for confirmation rather than proceeding.</summary>
    /// <remarks>
    /// <para>
    /// The default. GRD-8's own wording is "a configurable cost threshold", and a gate whose
    /// number cannot be moved is not a gate, it is an opinion: somebody who knows their study
    /// is worth an hour has no way to say so, and the observed response was to shrink the
    /// study instead. So <see cref="Execute"/> and <see cref="ForStudy"/> each take it.
    /// </para>
    /// <para>
    /// <b>A parameter, not a setting.</b> It was a public mutable static, which <c>--threshold</c>
    /// moved and nothing put back - so in any process running more than one estimate, the
    /// next one passed or refused on a number nobody had given it. A test asking for 1e12
    /// made another test's cost gate pass. Passed in, it cannot outlive the call it was meant
    /// for, and there is nothing for a caller to remember to restore.
    /// </para>
    /// </remarks>
    public const double ThresholdSeconds = 30.0;

    /// <summary>
    /// Cell updates per second for a diffusive step, in millions.
    /// </summary>
    /// <remarks>
    /// Measured on this codebase: 2.4 to 3.0 million across grids from 129 by 33 to
    /// 257 by 129. The low figure is used, for the same reason the solve estimate
    /// uses its high one - an estimate that runs under is worse than one that runs
    /// over, because the first is discovered by waiting.
    /// </remarks>
    private const double MegaCellsPerSecond = 2.4;

    /// <summary>What a diffusive run holds per density node, measured.</summary>
    /// <remarks>
    /// <para>
    /// 248 and 259 bytes a node on the drift-tube example at 513 x 129 and 1025 x 257, a slope
    /// of 263 between them, live-set sampled with the solved field excluded. This used to be
    /// 16 - "two density fields, current and next" - which is a sixteenth of it.
    /// </para>
    /// <para>
    /// 216 bytes a node are named arrays: the seed, the density and the next step, 24; six
    /// coefficients, 48; the face operator, four faces of four doubles and two flags plus an
    /// outward rate, 144. The rest is not attributed, which is why the figure is the
    /// measurement rather than that sum - an estimate that runs under is discovered by the run
    /// failing. An explicit run with no self-field; a mean-field solve adds its own grids.
    /// </para>
    /// <para>
    /// <b>Per species, all of it.</b> A mixture gives every species its own walker - its
    /// density, next step and seed, its coefficients, its face operator and its well - and
    /// measured on a 513 by 129 grid one species held 248.5 bytes a node and each species added
    /// 247.6 and then 248.3: nothing of this is shared. Charged once whatever the species
    /// count, a three-species run was billed a third of what it holds.
    /// </para>
    /// </remarks>
    private const double DiffusiveBytesPerNode = 263.0;

    /// <summary>The ponderomotive well cache, seven arrays over the density grid.</summary>
    /// <remarks>Built only when the run steps through a cycle average.</remarks>
    private const double WellCacheBytesPerNode = 7.0 * sizeof(double);

    /// <summary>
    /// Gauss-Seidel sweeps an implicit density step is assumed to take.
    /// </summary>
    /// <remarks>
    /// Measured at 3.0 on the shipped funnel at gains from 4 to 256 and 4.9 at 1024,
    /// and it is <em>not</em> a property of the gain: what decides it is how far past
    /// the diffusion limit the step lands, and on a problem already at that limit it
    /// reaches 88.7. So this is the figure for the case the implicit scheme exists for
    /// - a drift-limited driven structure - and the basis says as much rather than
    /// implying the number is general.
    /// </remarks>
    private const double SweepsPerStep = 3.0;

    /// <summary>How many times to repeat a pilot before taking the cheapest.</summary>
    private const int PilotAttempts = 5;

    /// <summary>Repeats a pilot while repeating is cheap, and reports the cheapest run.</summary>
    /// <remarks>
    /// <para>
    /// <b>The cheapest, because the work is a floor.</b> A single timing of a short pilot is
    /// mostly jitting and scheduling: the plane rate on the shipped mirror pair swung between
    /// 14.9 and 29.0 s per million nodes run to run, a factor of two straight into the
    /// estimate. The same statistic, for the same reason, as
    /// <c>AllocationDoesNotGrowWithStepCount</c>.
    /// </para>
    /// <para>
    /// <b>Self-limiting in the right direction.</b> The repeat stops once the pilot has cost
    /// about a second, so a short pilot - the noisy one - is repeated and cheap, while a long
    /// one is already well measured and is not repeated at all. That keeps the estimate from
    /// becoming a run.
    /// </para>
    /// </remarks>
    private static double CheapestMilliseconds(Action work) => Repeat(work).Cheapest;

    /// <summary>Repeats a pilot and reports both the cheapest run and how far they spread.</summary>
    /// <param name="work">The pilot.</param>
    /// <returns>The cheapest run in milliseconds, and the dearest as a fraction of it.</returns>
    /// <remarks>
    /// <b>The spread is known here and was being thrown away.</b> GRD-1 asks that a
    /// quantitative result carry a measure of how firm it is, and an estimate somebody plans
    /// a multi-day run against is exactly such a result - the more so now that it is measured
    /// on the machine rather than quoted from a constant. A single attempt reports a spread
    /// of zero, which is the honest answer: nothing was repeated, so nothing is known about
    /// the variation.
    /// </remarks>
    private static (double Cheapest, double Spread) Repeat(Action work)
    {
        var best = double.PositiveInfinity;
        var worst = 0.0;
        var spent = 0.0;
        var attempts = 0;

        for (var attempt = 0; attempt < PilotAttempts && spent < PilotRepeatBudgetMs; attempt++)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            work();
            clock.Stop();

            spent += clock.Elapsed.TotalMilliseconds;
            best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
            worst = Math.Max(worst, clock.Elapsed.TotalMilliseconds);
            attempts++;
        }

        return (best, attempts > 1 && best > 0.0 ? (worst - best) / best : 0.0);
    }

    /// <summary>How long the repeats may cost in total before settling for what is measured.</summary>
    private const double PilotRepeatBudgetMs = 1000.0;

    /// <summary>What the integration costs, measured by flying a little of it.</summary>
    /// <param name="model">The validated model.</param>
    /// <param name="coarsening">How much coarser the pilot field is than the real one.</param>
    /// <param name="windowSeconds">How much flight to fly, or null for the pilot fraction.</param>
    /// <returns>Estimated seconds for the whole flight, and how that was arrived at.</returns>
    /// <remarks>
    /// <para>
    /// <b>This term used to be left out entirely</b>, on the argument that its cost "depends
    /// on the path, which depends on the field this has not solved yet". That is true and it
    /// is not a reason to report nothing: for a multi-reflection analyser the flight is the
    /// dominant cost, and an estimate that silently omits the dominant term is worse than no
    /// estimate. GRD-8 asks for a number available without doing the work, not for a number
    /// covering only the part that was easy to predict.
    /// </para>
    /// <para>
    /// So it is <b>measured rather than modelled</b>: fly a twentieth of the declared flight
    /// and scale by how much of it was covered. Step size is chosen by the controller from
    /// the field the ion is actually in, so no formula predicts it - but the ion will tell
    /// you if you ask it for a moment.
    /// </para>
    /// <para>
    /// <b>Flown through a coarsened field, which biases the answer downward.</b> A gridded
    /// field caps the step by its own cell size, so a coarser field permits longer steps and
    /// fewer of them wherever the integration is resolution-limited rather than
    /// tolerance-limited. The caveat is stated rather than corrected by a factor, because
    /// which of the two limits binds depends on the model.
    /// </para>
    /// </remarks>
    private static (double Seconds, string How) PilotFlight(
        CompiledModel model, double coarsening, double? windowSeconds = null)
    {
        try
        {
            var coarse = model with
            {
                Fields = [.. model.Fields.Select(f => f with
                {
                    Solve = f.Solve is null ? null : f.Solve with { CellSize = f.Solve.CellSize * coarsening },
                    Solve3D = f.Solve3D is null ? null : f.Solve3D with { CellSize = f.Solve3D.CellSize * coarsening },
                })],
            };

            var (field, _) = Fields.FieldAssembly.BuildReported(coarse);

            var species = Transport.IonSpecies.FromModel(coarse);

            var launch = new Transport.PhaseState(
                coarse.SourcePosition, coarse.SourceDirection * coarse.LaunchSpeedSi());

            var point = coarse.DetectorPoint;
            var normal = coarse.DetectorNormal;

            var window = windowSeconds ?? (coarse.MaximumFlightTimeSi * PilotFlightFraction);

            var settings = new Transport.Integration.IntegrationSettings
            {
                RelativeTolerance = coarse.RelativeTolerance,
                MaximumFlightTime = window,
                MaximumSteps = PilotStepCeiling,
            };

            // A DECLARED GAS TAKES PART. Without this the pilot flew in vacuum and the
            // basis still said "the whole flight, measured", so a model at 1e-2 mbar was
            // costed by a flight that schedules none of the thousands of collisions the
            // real one does - and the estimate ran under, which is the direction
            // Amendment 33 exists to prevent. The same silent substitution RunCommand's
            // own comment warns against, and that this repo already fixed once for the
            // regime inspector.
            //
            // A fresh sampler per attempt, because CheapestMilliseconds repeats the flight
            // and a shared one would let the second attempt continue the first's draws -
            // a different trajectory, timed as though it were the same one.
            var gas = DiffusionRun.GasFor(coarse);

            Transport.Integration.TrajectoryResult flown = default!;

            var pilotSeconds = CheapestMilliseconds(
                () => flown = Transport.Integration.TrajectoryIntegrator.Integrate(
                    launch,
                    species,
                    field,
                    settings,
                    (in Transport.PhaseState state) =>
                        Core.Geometry.Vec3.Dot(state.Position - point, normal),
                    collisions: gas.IsPresent
                        ? new Transport.Collisions.CollisionSampler(
                            gas, species.MassSi, species.ChargeSi, coarse.Gas.Seed)
                        : null))
                / 1000.0;
            var covered = flown.FlightTimeSeconds;

            // Arrived or absorbed inside the window: the flight is over, so this is not an
            // extrapolation at all - it is the measurement.
            if (flown.Outcome is Transport.Integration.TrajectoryOutcome.StopConditionMet
                or Transport.Integration.TrajectoryOutcome.StruckElectrode)
            {
                return (
                    pilotSeconds,
                    $"the whole flight, measured: it ended as {flown.Outcome} after "
                    + $"{covered * 1e6:G4} us and {flown.AcceptedSteps:N0} steps");
            }

            if (covered <= 0.0)
            {
                return (0.0, "not measured: the pilot flight covered no time at all");
            }

            var scale = coarse.MaximumFlightTimeSi / covered;

            return (
                pilotSeconds * scale,
                $"{flown.AcceptedSteps:N0} steps over {covered * 1e6:G4} us took "
                + $"{pilotSeconds * 1000.0:F0} ms, scaled to the declared "
                + $"{coarse.MaximumFlightTimeSi * 1e6:G4} us. Flown through a field solved at "
                + $"{coarsening:G2}x the cell size, so this is a FLOOR wherever the step is "
                + "capped by resolution rather than by tolerance");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return (0.0, $"not measured: a pilot flight could not be flown ({exception.GetType().Name})");
        }
    }

    /// <summary>How the documented rate describes itself when nothing was measured.</summary>
    private static string Documented(double rate) =>
        $"{rate:G3} s per million nodes, the documented rate for this engine's own machine - "
        + "nothing was measured here, so treat it as a scale rather than a time";


    /// <summary>What a solve costs per node ON THIS MACHINE, measured rather than quoted.</summary>
    /// <param name="pilot">A function that solves a coarsened copy and returns its node count.</param>
    /// <param name="fallback">The documented rate, used when the pilot is too quick to time.</param>
    /// <returns>The rate in seconds per million nodes, and how it was arrived at.</returns>
    /// <remarks>
    /// <para>
    /// <b>A hardcoded rate is a statement about the machine it was measured on.</b> This one
    /// was 13 s per million nodes, taken from the shipped templates on a developer box, and
    /// it is what an estimate on somebody else's hardware would have been quoting. GRD-8
    /// wants a number available without doing the work; it does not want a number about the
    /// wrong computer.
    /// </para>
    /// <para>
    /// <b>Calibrated on the model's own geometry, coarsened</b>, rather than on a fabricated
    /// pilot. The rate is not a property of the solver alone: a boundary-value problem
    /// converges faster per node than one with interior electrodes, which is exactly the
    /// spread the old constant papered over by taking the larger figure. Coarsening the real
    /// geometry keeps the electrodes, the boundary conditions and the difficulty, so what is
    /// measured is this solve on this machine.
    /// </para>
    /// <para>
    /// A pilot too quick to time measures the scheduler, so below a floor the documented
    /// constant is used and the basis line says which it was.
    /// </para>
    /// </remarks>
    private static (double Rate, string How, double Spread) CalibrateRate(
        Func<long> pilot, double fallback)
    {
        try
        {
            // CHEAPEST OF SEVERAL, and only while several are cheap. A single timing of a
            // short pilot is mostly jitting and scheduling: the plane rate on the shipped
            // mirror pair swung between 14.9 and 29.0 s per million nodes run to run, a
            // factor of two straight into the estimate. The work is a floor, so the
            // minimum is the right statistic - the same argument, and the same statistic,
            // as `AllocationDoesNotGrowWithStepCount`.
            //
            // The repeat stops once the pilot has cost about a second, which is
            // self-limiting in the right direction: a short pilot is the noisy one and is
            // cheap to repeat, and a long one is already well measured and would be
            // expensive to.
            var nodes = 0L;
            var (milliseconds, spread) = Repeat(() => nodes = pilot());

            if (nodes <= 0 || milliseconds < PilotFloorMs)
            {
                return (
                    fallback,
                    $"{fallback:G3} s per million nodes, the documented rate: a pilot solve of "
                    + $"{nodes:N0} nodes took {milliseconds:F0} ms, too little to time",
                    0.0);
            }

            var rate = milliseconds / 1000.0 / (nodes / 1e6);

            return (
                rate,
                $"{rate:G3} s per million nodes, measured on this machine by solving this "
                + $"geometry at {PilotCoarsening:G2}x the cell size - {nodes:N0} nodes in "
                + $"{milliseconds:F0} ms, repeats spreading {spread:P0} - and extrapolated on "
                + "node count, which holds because multigrid cycle counts are grid-independent",
                spread);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A coarsened geometry can fail where the real one would not: an electrode too
            // small to be represented at twice the cell size is refused by the same guard
            // that protects a real solve. That is the pilot being unrepresentative, not the
            // model being wrong, so it falls back rather than failing the estimate.
            return (
                fallback,
                $"{fallback:G3} s per million nodes, the documented rate: this geometry does "
                + "not survive being coarsened for a pilot, so nothing was measured here",
                0.0);
        }
    }


    /// <summary>Estimates a model's cost.</summary>
    /// <param name="modelPath">Path to the model file.</param>
    /// <returns>The estimate.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is null or blank.</exception>
    /// <exception cref="Core.Errors.EinzelException">The model does not validate.</exception>
    /// <param name="calibrate">
    /// Whether to measure this machine by solving a coarsened copy of the model's own
    /// geometry, rather than quoting the documented rates. On by default: an estimate is
    /// worth having about the computer that will do the work. Turned off, the verb keeps
    /// PERF-8's cold-start budget, which a pilot solve does not.
    /// </param>
    /// <param name="threshold">
    /// Seconds above which the estimate says the operation should be confirmed.
    /// </param>
    /// <param name="availableMemoryBytes">
    /// What the process may use, for the comparison the basis makes with the model's peak; this
    /// machine's when omitted. A parameter for the reason <paramref name="threshold"/> is one:
    /// so the comparison can be checked without a machine that is short of memory.
    /// </param>
    public static EstimateOutcome Execute(
        string modelPath,
        bool calibrate = true,
        double threshold = ThresholdSeconds,
        long? availableMemoryBytes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        var absolute = Path.GetFullPath(modelPath);
        var document = Io.ModelJson.Parse(File.ReadAllText(absolute));
        var validation = ModelValidator.Validate(
            document, null, Path.GetDirectoryName(absolute));

        if (!validation.IsValid)
        {
            throw new Core.Errors.EinzelException(validation.Errors);
        }

        var model = validation.Model!;
        var elements = new List<ElementEstimate>();
        var seconds = 0.0;
        var memory = 0.0;

        // Elements are built in document order and each one's solved potentials stay live
        // while the next is solving, so the model's peak is not the largest element's alone.
        var retained = 0.0;
        (int Index, SolveMemory Memory)? largest = null;

        // Measured once, and only for the kinds this model contains: a plane-only model
        // must not pay for a volume pilot it will never use.
        var firstPlane = model.Fields.FirstOrDefault(f => f.Solve is not null)?.Solve;
        var firstVolume = model.Fields.FirstOrDefault(f => f.Solve3D is not null)?.Solve3D;

        var planeRate = firstPlane is null || !calibrate
            ? (SecondsPerMegaNode, Documented(SecondsPerMegaNode), 0.0)
            : CalibrateRate(
                () =>
                {
                    var coarse = firstPlane with { CellSize = firstPlane.CellSize * PilotCoarsening };
                    var pilot = Fields.Solved.GeometryBuilder.BuildGrid(coarse);
                    _ = Fields.Solved.GeometryBuilder.Build(coarse);
                    return pilot.NodeCount;
                },
                SecondsPerMegaNode);

        var volumeRate = firstVolume is null || !calibrate
            ? (VolumeSecondsPerMegaNode, Documented(VolumeSecondsPerMegaNode), 0.0)
            : CalibrateRate(
                () =>
                {
                    var geometry = new Fields.Solved.Geometry3D(
                        firstVolume.MinX, firstVolume.MinY, firstVolume.MinZ,
                        firstVolume.MaxX, firstVolume.MaxY, firstVolume.MaxZ,
                        firstVolume.CellSize * PilotCoarsening,
                        firstVolume.Electrodes,
                        firstVolume.Tolerance)
                    {
                        Drives = firstVolume.Drives,
                        Stages = firstVolume.Stages,

                        // THE SAME BOUNDARY-VALUE PROBLEM AS THE RUN. A Neumann face
                        // constrains fewer nodes and converges differently, so a pilot
                        // that quietly grounded every face would measure the rate of a
                        // problem nobody asked about. The plane pilot above escapes this
                        // by construction - it is `firstPlane with { CellSize = ... }`,
                        // which carries every other field forward - while this one is
                        // copied field by field and so drops whatever was added last.
                        Faces = Fields.Solved.Geometry3D.FacesOf(firstVolume.Faces),
                        ReflectAboutX = firstVolume.ReflectAboutX,
                    };

                    var pilot = Fields.Solved.GeometryBuilder3D.BuildGrid(geometry);
                    _ = Fields.Solved.GeometryBuilder3D.BuildField(geometry);
                    return pilot.NodeCount;
                },
                VolumeSecondsPerMegaNode);
        // The flight, measured rather than omitted. Only a trajectory run has one: a
        // diffusive run's cost is the density stepping, which is estimated below from the
        // stability limits and needs no pilot.
        var trajectory = calibrate && model.TransportMode != "diffusion"
            ? PilotFlight(model, PilotCoarsening)
            : (Seconds: 0.0, How: string.Empty);

        // A CLOUD FLIES ONCE PER ION THROUGH ONE SOLVED FIELD, so the flight is paid as many
        // times as there are ions and the solve is paid once. Costing a single trajectory
        // was silently short by the ion count - the shipped rectilinear trap declares 2000
        // of them - which is the same defect this command already had for studies, where
        // the number was short by the evaluation count.
        //
        // Not multiplied for a diffusive run, which steps a density and produces no
        // trajectories at all, nor for a space-charge run, which advances the whole packet
        // in lockstep so its members are not independent flights. In both the model's own
        // cost already IS the whole run.
        var members = model.Cloud.IsCloud && !model.ModelsSpaceCharge
            && model.TransportMode != "diffusion"
                ? Math.Max(1, model.Cloud.Ions)
                : 1;

        seconds += members * trajectory.Seconds;

        var basis = string.Join(
            " ",
            new[]
            {
                firstPlane is null ? string.Empty : "Plane solves: " + planeRate.Item2 + ".",
                firstVolume is null ? string.Empty : "Volume solves: " + volumeRate.Item2 + ".",
                trajectory.How.Length > 0 ? "Trajectory: " + trajectory.How + "." : string.Empty,
            }.Where(part => part.Length > 0));

        for (var index = 0; index < model.Fields.Count; index++)
        {
            var element = model.Fields[index];

            // A volume solve is the most expensive thing a model can ask for and was
            // costed at zero, because this loop tested `Solve` and never `Solve3D` - so
            // `estimate` reported "0.00 s" for the one element whose cost the gate exists
            // to gate on. GRD-8 wants a number available without doing the work, and a
            // number that is always nought is worse than none: it reads as an answer.
            if (element.Solve3D is { } volume)
            {
                // The whole geometry, drives and stages included, though nothing here is
                // solved: BuildGrid is arithmetic on the box, and the memory statement needs
                // the channel count, which is a decomposition of the drives and the stages. A
                // geometry copied without them reads as one channel whatever it declares.
                var declared = new Fields.Solved.Geometry3D(
                    volume.MinX, volume.MinY, volume.MinZ,
                    volume.MaxX, volume.MaxY, volume.MaxZ,
                    volume.CellSize,
                    volume.Electrodes,
                    volume.Tolerance)
                {
                    Drives = volume.Drives,
                    Stages = volume.Stages,
                    Faces = Fields.Solved.Geometry3D.FacesOf(volume.Faces),
                    ReflectAboutX = volume.ReflectAboutX,
                };

                var space = Fields.Solved.GeometryBuilder3D.BuildGrid(declared);

                var volumeNodes = space.NodeCount;

                // The same rate as a plane solve, which is a floor rather than an
                // estimate: a volume cycle carries a 27-point stencil where a plane
                // carries five, and its coarse levels are built by Galerkin rather than
                // rediscretised. Said in the basis line rather than fudged by a factor
                // nobody measured.
                var volumeSeconds = volumeRate.Item1 * volumeNodes / 1e6;

                // THE SOLVER'S OWN STATEMENT OF WHAT IT HOLDS, not a constant here. This was
                // 48 bytes a node - three fields and some restriction buffers - and a volume
                // solve held five times that: the conductor mask and cut links storing all
                // six arms of every node were 105 bytes before a field was allocated, and the
                // Galerkin hierarchy then stores the fine operator and forms its first coarse
                // level from it. Measured at 241.8 bytes a node on the Astral then, and 150.8
                // with the cut links stored over their cut arms; the Astral at a 1 mm cell was
                // billed 3.1 GiB and needs about 9.5.
                var volumePlan = Fields.Solved.GeometryBuilder3D.Memory(declared);
                var volumeMemory = volumePlan.PeakBytes / Mebibyte;

                memory = Math.Max(memory, (retained + volumePlan.PeakBytes) / Mebibyte);
                retained += volumePlan.RetainedBytes;

                if (largest is null || volumePlan.PeakBytes > largest.Value.Memory.PeakBytes)
                {
                    largest = (index, volumePlan);
                }

                elements.Add(new ElementEstimate
                {
                    Index = index,
                    Type = element.Kind.ToString(),
                    Nodes = [space.CountX, space.CountY, space.CountZ],
                    NodeCount = volumeNodes,
                    MemoryMiB = volumeMemory,
                    Seconds = volumeSeconds,
                    RequestedCell = volume.CellSize,
                    Spacing = [space.SpacingX, space.SpacingY, space.SpacingZ],
                });

                seconds += volumeSeconds;

                continue;
            }

            if (element.Solve is null)
            {
                elements.Add(new ElementEstimate
                {
                    Index = index,
                    Type = element.Kind.ToString(),
                    Nodes = [],
                    NodeCount = 0,
                    MemoryMiB = 0.0,
                    Seconds = 0.0,
                });

                continue;
            }

            // Building the grid is arithmetic on the declared box, so asking it
            // how big it will be costs nothing and beats estimating the estimate.
            var grid = GeometryBuilder.BuildGrid(element.Solve);
            var nodes = grid.NodeCount;
            var elementSeconds = planeRate.Item1 * nodes / 1e6;

            // Three times the 48 bytes a node this used to assume, for the same reason as a
            // volume: the mask and four cut-link arms were not in it, nor the coarse levels,
            // which a plane V-cycle rebuilds from the geometry and holds all at once at the
            // bottom of the V. Measured at 143 to 150 bytes a node.
            var planePlan = GeometryBuilder.Memory(element.Solve);
            var elementMemory = planePlan.PeakBytes / Mebibyte;

            memory = Math.Max(memory, (retained + planePlan.PeakBytes) / Mebibyte);
            retained += planePlan.RetainedBytes;

            if (largest is null || planePlan.PeakBytes > largest.Value.Memory.PeakBytes)
            {
                largest = (index, planePlan);
            }

            elements.Add(new ElementEstimate
            {
                Index = index,
                Type = element.Kind.ToString(),
                Nodes = [grid.CountX, grid.CountY],
                NodeCount = nodes,
                MemoryMiB = elementMemory,
                Seconds = elementSeconds,
                RequestedCell = element.Solve.CellSize,
                Spacing = [grid.SpacingX, grid.SpacingY],
            });

            seconds += elementSeconds;
        }

        // A diffusive run is a time-stepped solve over a whole grid, and its step is
        // set by stability rather than declared - so unlike trajectory integration
        // its cost is knowable before the run, and unlike a field solve it is the
        // dominant term rather than a preamble.
        if (model.TransportMode == "diffusion")
        {
            var (diffusiveSeconds, diffusiveMemory, diffusiveBasis) = Diffusive(model, Path.GetDirectoryName(absolute) ?? Directory.GetCurrentDirectory());

            seconds += diffusiveSeconds;

            // On top of the solved fields, which the density is stepped through and which
            // stay live for the whole run.
            memory = Math.Max(memory, (retained / Mebibyte) + diffusiveMemory);
            basis = diffusiveBasis;
        }

        var firmness = Math.Max(planeRate.Item3, volumeRate.Item3);

        if (firmness > 0.0)
        {
            basis = basis + string.Create(
                Inv,
                $" Repeating the pilots on this machine spread {firmness:P0} of the cheapest, "
                + $"which is how firm this number is - on an idle machine, and a floor rather "
                + $"than a confidence interval.");
        }

        if (members > 1)
        {
            basis = basis + string.Create(
                Inv,
                $" The source declares a cloud of {members} ions, which fly independently "
                + $"through one solved field - so the solve is paid once and the flight "
                + $"{members} times.");
        }

        var mesh = MeshNote(elements);

        if (mesh.Length > 0)
        {
            basis = basis + " " + mesh;
        }

        var held = MemoryNote(largest, elements);

        if (held.Length > 0)
        {
            basis = basis + " " + held;
        }

        var fits = CapacityNote(memory, availableMemoryBytes ?? GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);

        if (fits.Length > 0)
        {
            basis = basis + " " + fits;
        }

        if (model.ModelsSpaceCharge)
        {
            var (chargeSeconds, chargeBasis) = SelfField(model);

            seconds += chargeSeconds;
            basis = basis + " " + chargeBasis;
        }

        return new EstimateOutcome
        {
            ModelPath = absolute,
            Elements = elements,
            Seconds = seconds,
            MemoryMiB = memory,
            AboveThreshold = seconds > threshold,
            ThresholdSeconds = threshold,
            Basis = basis,
            TrajectorySeconds = trajectory.Seconds,
            PilotSpread = Math.Max(planeRate.Item3, volumeRate.Item3),
        };
    }

    /// <summary>Below this many evaluations, sampling the range costs too much of the run.</summary>
    /// <remarks>
    /// From the arithmetic rather than by taste. A sample is one solve and one flight, and
    /// an evaluation is one solve and <c>members</c> flights, so three samples cost roughly
    /// one and a half evaluations. Against twenty that is about 7 per cent of the run, and
    /// against the hundreds a real optimisation declares it is under one.
    /// </remarks>
    private const int ExtremeSamplingThreshold = 20;

    /// <summary>The parameter values a study will actually visit the extremes of.</summary>
    /// <remarks>
    /// <para>
    /// A scan and a boundary search declare a range; an optimisation declares a box. A
    /// tolerance sweep declares neither - its channels perturb <em>around</em> the nominal
    /// by a tolerance, so the nominal is already the right place to measure and there is
    /// nothing to sample.
    /// </para>
    /// <para>
    /// One variable at a time for an optimisation rather than every corner, because corners
    /// are exponential in the variable count and the estimate must stay small against the
    /// study. Capped for the same reason.
    /// </para>
    /// </remarks>
    private static List<IReadOnlyDictionary<string, Core.Units.Quantity>> Extremes(
        StudyDocument study)
    {
        static IReadOnlyDictionary<string, Core.Units.Quantity> At(
            string parameter, double value, string unit) =>
            new Dictionary<string, Core.Units.Quantity>(StringComparer.Ordinal)
            {
                [parameter] = Core.Units.Quantity.From(value, unit),
            };

        if (study.Scan is { Parameter: { } scanned, Unit: { } scanUnit })
        {
            return [At(scanned, study.Scan.From, scanUnit), At(scanned, study.Scan.To, scanUnit)];
        }

        if (study.Boundary is { Parameter: { } bounded, Unit: { } boundUnit })
        {
            return [At(bounded, study.Boundary.From, boundUnit), At(bounded, study.Boundary.To, boundUnit)];
        }

        if (study.Variables is { Count: > 0 } variables)
        {
            var samples = new List<IReadOnlyDictionary<string, Core.Units.Quantity>>();

            foreach (var variable in variables.Take(MaximumSampledVariables))
            {
                if (variable is { Parameter: { } name, Minimum: { } low, Maximum: { } high, Unit: { } unit })
                {
                    samples.Add(At(name, low, unit));
                    samples.Add(At(name, high, unit));
                }
            }

            return samples;
        }

        return [];
    }

    /// <summary>How many of an optimisation's variables to sample the extremes of.</summary>
    private const int MaximumSampledVariables = 3;

    /// <summary>The nominal, expressed as a sample that overrides nothing.</summary>
    private static readonly Dictionary<string, Core.Units.Quantity> EmptyOverrides =
        new(StringComparer.Ordinal);

    /// <summary>The mean pilot flight over the nominal and the values a study will visit.</summary>
    /// <remarks>
    /// <b>The mean rather than the worst</b>, because a study visits its whole range and
    /// pays the average of it - reporting the worst would gate a job on a cost it never
    /// incurs. The spread is reported alongside, since a range that varies several-fold is
    /// something the reader should know about whatever the mean is.
    /// </remarks>
    private static (double Flight, string Spread) SampledFlight(
        Core.Model.ModelDocument document,
        string? directory,
        double nominal,
        List<IReadOnlyDictionary<string, Core.Units.Quantity>> samples)
    {
        // No early return on an empty list: the nominal is measured the same way whether or
        // not there are extremes to go with it, which is what keeps the flight term one
        // quantity at every study length.
        var flights = new List<double>();

        // THE WHOLE DECLARED FLIGHT, not the pilot fraction, and the nominal is resampled
        // the same way so every sample is the same measurement. A fraction of a flight has
        // to be extrapolated, and the only length available to extrapolate against is the
        // declared *maximum* - which is a ceiling rather than an expectation. The nominal
        // ion happened to arrive inside the fraction and the extremes did not, so they were
        // scaled by the whole ceiling and the estimate came out 3.4x over. A study samples
        // few points against many evaluations, so it can afford the real thing.
        foreach (var overrides in samples.Prepend(EmptyOverrides))
        {
            // A sample outside the parameter's declared bounds is refused by validation,
            // which is the model being right rather than the estimate being wrong - so it
            // is skipped and the others still inform the mean.
            var validation = ModelValidator.Validate(document, overrides, directory);

            if (validation.Model is { } compiled)
            {
                // AT THE REAL CELL SIZE, not the pilot's coarsening. Coarsening exists to
                // make the *solve* cheap when it is the only measurement being taken; here
                // a solve is paid per sample regardless, and a flight through a coarser
                // field takes a different number of steps - which is precisely the quantity
                // being measured. Sampling is already gated on the study being long enough
                // to absorb this.
                var (seconds, _) = PilotFlight(compiled, 1.0, compiled.MaximumFlightTimeSi);

                if (seconds > 0.0)
                {
                    flights.Add(seconds);
                }
            }
        }

        if (flights.Count == 0)
        {
            // Not even the nominal could be flown - fall back to what the model estimate
            // measured, which says in its own basis how it was arrived at.
            return (nominal, string.Empty);
        }

        var mean = flights.Average();

        if (flights.Count == 1)
        {
            // The nominal alone, measured the same way the sampled case measures it. There
            // is no range to report a spread across, and the caller says why.
            return (mean, string.Empty);
        }

        var ratio = flights.Max() / Math.Max(flights.Min(), double.Epsilon);

        return (
            mean,
            $" The flight was sampled at {flights.Count} points across the study's own range "
            + $"and averaged, because a study that varies the geometry varies its own cost: "
            + $"here the dearest point was {ratio:F1}x the cheapest.");
    }


    /// <summary>Costs a study: what its model costs, times what its driver declares.</summary>
    /// <param name="studyPath">Path to the study file.</param>
    /// <param name="calibrate">Whether to measure this machine rather than quote a constant.</param>
    /// <param name="threshold">
    /// Seconds above which the estimate says the study should be confirmed.
    /// </param>
    /// <returns>The estimate, carrying the model's own breakdown and the study multiplier.</returns>
    /// <exception cref="ArgumentException"><paramref name="studyPath"/> is null or blank.</exception>
    /// <exception cref="Core.Errors.EinzelException">The study or its model is invalid.</exception>
    /// <remarks>
    /// <para>
    /// <b>The multiplier needs no pilot.</b> Every driver declares its own extent - a scan
    /// its points, a sweep its draws, an optimiser and a bisection their evaluation
    /// ceilings - so this is arithmetic over numbers already written in the file, which is
    /// what makes it free where the per-evaluation cost is measured.
    /// </para>
    /// <para>
    /// <b>One solve, many trajectories.</b> An evaluation solves the field once and flies
    /// every ensemble member through it, so the two terms are multiplied separately: a
    /// study is <c>evaluations x (solve + members x flight)</c>, not <c>evaluations x</c>
    /// the model's total. Costing it the second way overstates a nine-member figure by
    /// most of eight solves per evaluation.
    /// </para>
    /// </remarks>
    public static EstimateOutcome ForStudy(
        string studyPath, bool calibrate = true, double threshold = ThresholdSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studyPath);

        var (study, modelPath, absolute) = StudyCommand.Load(studyPath);
        var model = Execute(modelPath, calibrate, threshold);

        var (kind, evaluations, ceiling, how) = Extent(study);

        // Whichever ensemble the figure will actually fly. A declared cloud wins,
        // because that is what the figures fly when there is one - the study's own
        // ion count is the fallback the deterministic energy sweep uses.
        var document = Io.ModelJson.Parse(File.ReadAllText(modelPath));
        var directory = Path.GetDirectoryName(modelPath);

        // EVERY DRIVER RE-COMPILES THE DOCUMENT PER EVALUATION, because that is how an
        // override reaches the geometry - the parameter surface is re-resolved and every
        // expression re-expanded. It is not free and it is not part of the solve, so a
        // study costed as solves and flights alone runs under: on the shipped mirror pair
        // that term was 1.44x of the whole estimate.
        //
        // Cheapest of three, because the property is a floor and the runtime charges
        // one-off costs to whichever window they fire in.
        // ONE REPEAT POLICY, and the compilation it produced is the one that is kept.
        // A hand-rolled loop here meant two policies - this one always ran three times with
        // no budget while `CheapestMilliseconds` repeats up to five within a second - and a
        // fifth Validate whose only purpose was to hand back a result the last timed
        // iteration already had. For a model with an imported gas or pressure field every
        // one of those resolves and reads that file from disk.
        ModelValidation validation = default!;

        var compile = CheapestMilliseconds(
            () => validation = ModelValidator.Validate(document, null, directory)) / 1000.0;

        // HOW MANY FLIGHTS AN EVALUATION COSTS IS THE FIGURE OF MERIT'S ANSWER, not the
        // study's ion count. This charged the ion count for every figure, which is right
        // for an ensemble and wrong for the rest: `flightTime` is one ion down a three-rung
        // convergence ladder, so a tolerance sweep of the shipped reflectron was costed at
        // 29 s against a measured 1.08 s. The gate then called it expensive and an agent
        // shrank its study to get past a number that was wrong - the gate degrading the
        // work it exists to protect.
        var members = FiguresOfMerit.FlightsPerEvaluation(
            study.FigureOfMerit,
            study.Ions,
            validation.Model is { Cloud.IsCloud: true } compiled ? compiled.Cloud.Ions : null);

        // WHETHER AN EVALUATION IS A PACKET OR AN ENSEMBLE OF INDEPENDENT IONS, and the
        // two cost completely differently.
        //
        // The ordinary case flies `members` independent ions through one solved field, so
        // the solve is paid once and the flight `members` times. But a diffusive run steps
        // a density and a space-charge run advances the whole packet in lockstep: in both,
        // what `Execute` already costed IS one whole evaluation, flights included. Adding
        // `members x flight` on top of those double-counts the very work they describe -
        // and for a diffusive model the flights do not exist at all, since that mode
        // produces no trajectories (TRN-2, RND-8).
        var wholeRun = validation.Model?.ModelsSpaceCharge == true
            || validation.Model?.TransportMode == "diffusion";

        // SAMPLED WHERE THE STUDY WILL GO, not only at the nominal. A study that varies
        // the geometry varies its own cost, and on the shipped mirror pair a separation
        // scan ran 2.2x dearer at one end than at the other - so an estimate taken at the
        // declared values alone came out 0.57x of the real scan, which is the direction
        // that matters. Almost all of that variation is the flight (the solve's node count
        // moved 4 per cent across the same range), so it is the flight that is resampled.
        //
        // THE THRESHOLD GATES THE EXTREMES, NOT THE MEASUREMENT. Both paths go through
        // SampledFlight, which always flies the nominal at the full window and the real cell
        // size, so the flight term means one thing at every study length. Gating the whole
        // measurement instead made it a coarsened extrapolation below the threshold and a
        // full-fidelity mean above it - so a 19-point and a 21-point scan over one model
        // were costed in different currencies and the per-evaluation figure jumped at a
        // boundary that has nothing to do with the model.
        //
        // What the threshold still buys is the extremes: two extra pilots are worth spending
        // against hundreds of evaluations and not against a handful. The nominal costs one
        // evaluation's worth of measurement, which a study of any length can absorb.
        var (flight, spread) = wholeRun
            ? (0.0, string.Empty)
            : SampledFlight(
                document,
                directory,
                model.TrajectorySeconds,
                evaluations >= ExtremeSamplingThreshold ? Extremes(study) : []);

        var perEvaluation = wholeRun
            ? compile + model.Seconds
            : compile + (model.Seconds - model.TrajectorySeconds) + (members * flight);

        var seconds = evaluations * perEvaluation;

        var each = wholeRun
            ? "is one whole run of the model - a density stepped, or a packet advanced in "
            + "lockstep - so its flights are already inside the figure above and are not "
            + "counted again"
            : $"solves once and flies {members} "
            + $"trajector{(members == 1 ? "y" : "ies")} through that one field";

        var basis = model.Basis
            + $" This is a study: {how} Each evaluation re-compiles the document "
            + $"({compile * 1000.0:F0} ms, measured) and {each}, so it costs "
            + $"{perEvaluation:F2} s, and {evaluations} of them cost {Duration(seconds)}."
            + spread
            + (ceiling
                ? " That count is a ceiling the search may stop short of."
                : " Every one of those evaluations is computed.")
            + Unsampled(wholeRun, spread, evaluations, Extremes(study).Count)
            + " Process start and just-in-time compilation are not included - a fixed cost, "
            + "which is negligible for a long study and is not for a short one.";

        return model with
        {
            Seconds = seconds,
            AboveThreshold = seconds > threshold,
            Basis = basis,

            // The flight the arithmetic above actually used, not the nominal pilot it
            // started from. Leaving the nominal here made the record contradict itself:
            // a caller deriving the solve term as Seconds/Evaluations - Members x this
            // got a number that did not reconcile, silently, because both fields look
            // equally authoritative.
            TrajectorySeconds = wholeRun ? 0.0 : flight,
            Study = new StudyEstimate
            {
                StudyPath = absolute,
                Kind = kind,
                Evaluations = evaluations,
                EvaluationsAreACeiling = ceiling,
                Members = members,
                SecondsPerEvaluation = perEvaluation,
                Basis = how,
            },
        };
    }

    /// <summary>How many evaluations a study's declared driver will make.</summary>
    /// <remarks>
    /// Read from the file rather than from the driver, deliberately: the point of an
    /// estimate is to be available <em>before</em> the work, and every one of these
    /// numbers is stated in the document.
    /// </remarks>
    private static (string Kind, int Evaluations, bool Ceiling, string How) Extent(
        StudyDocument study)
    {
        if (study.Scan is { } scan)
        {
            return ("scan", Math.Max(scan.Points, 1), false,
                $"a scan of {scan.Points} points over '{scan.Parameter}'.");
        }

        if (study.Boundary is { } boundary)
        {
            // A bisection's real cost is about log2 of the bracket over the resolution,
            // plus the confirmation walk - but what it will not exceed is its own
            // declared budget, and a ceiling is the honest thing for a plan to hold.
            return ("boundary", Math.Max(boundary.MaximumEvaluations, 1), true,
                $"a boundary search along '{boundary.Parameter}', budgeted at "
                + $"{boundary.MaximumEvaluations} evaluations - a bisection usually needs "
                + "about eleven plus its confirmation walk, so this is an upper bound.");
        }

        if (study.Variables is { Count: > 0 })
        {
            return ("optimisation", Math.Max(study.MaximumEvaluations, 1), true,
                $"an optimisation over {study.Variables.Count} variables by "
                + $"{study.Algorithm}, budgeted at {study.MaximumEvaluations} evaluations.");
        }

        var channels = study.Channels?.Count ?? 0;

        // Attribution is two more evaluations per channel, at that channel's extremes.
        var attribution = study.OneAtATime ? 2 * channels : 0;

        return ("sweep", Math.Max(study.Draws + attribution, 1), false,
            $"a tolerance sweep of {study.Draws} draws over {channels} "
            + $"channel{(channels == 1 ? string.Empty : "s")}"
            + (attribution > 0
                ? $", plus {attribution} more for one-at-a-time attribution."
                : "."));
    }

    /// <summary>Why the study's range was not sampled, when it was not.</summary>
    /// <remarks>
    /// Three different reasons, and the first version of this said the same wrong thing for
    /// two of them. A 504-evaluation tolerance sweep is not "too short to be worth sampling":
    /// its channels perturb <em>around</em> the nominal, so the nominal is already the right
    /// place to measure and there is nothing to sample. Saying otherwise invites somebody to
    /// lengthen a study that would gain nothing from it.
    /// </remarks>
    private static string Unsampled(bool wholeRun, string spread, int evaluations, int samples)
    {
        if (spread.Length > 0)
        {
            return string.Empty;
        }

        if (wholeRun)
        {
            return " The flight was not sampled across the range, because an evaluation here "
                + "is one whole run and has no separable flight term.";
        }

        if (samples == 0)
        {
            return " The flight was not sampled across a range, because this study declares "
                + "none - its draws perturb around the model's declared values, which is "
                + "where the flight was measured.";
        }

        return string.Create(
            Inv,
            $" Costed at the model's declared parameter values: a study that varies the "
            + $"geometry varies its own cost, and at {evaluations} evaluations this one is "
            + $"too short to spend {samples} extra pilots sampling the range.");
    }

    /// <summary>Seconds as something a person can plan against.</summary>
    /// <param name="seconds">The duration, in seconds.</param>
    /// <returns>The duration in whatever unit a reader can act on.</returns>
    /// <remarks>
    /// GRD-8's number is read to decide whether to start the work now or overnight, and
    /// "108,000 s" does not answer that question while "1 day 6 h" does. Public because the
    /// CLI prints the same quantity beside the basis sentence that contains it, and two
    /// spellings of one duration on one page is a reader's problem before it is a
    /// maintenance one.
    /// </remarks>
    public static string Duration(double seconds) => seconds switch
    {
        < 90.0 => $"{seconds:F0} s",
        < 5400.0 => $"{seconds / 60.0:F0} min",
        < 172800.0 => $"{seconds / 3600.0:F1} h",
        _ => $"{seconds / 86400.0:F1} days",
    };

    /// <summary>Says when a grid is much finer than the document asked for, and what to ask instead.</summary>
    /// <remarks>
    /// <para>
    /// <b>The cost of a mesh is a step function of the cell size.</b> Each axis rounds its
    /// own interval count up to a power of two, so a request landing just over a power of
    /// two pays double on that axis - and the node count is the product of three such
    /// roundings. A 635 x 48 x 350 mm analyser at a requested 1 mm gets 1025 x 65 x 513
    /// nodes at 0.62 x 0.75 x 0.68 mm: <b>34.2 M where the request implies 10.7 M</b>.
    /// </para>
    /// <para>
    /// <b>That is not waste - it is a finer mesh than was asked for</b>, and the distinction
    /// matters because nothing here is wrong. What somebody planning a multi-day run needs
    /// is the other half: on that analyser, asking for <b>1.5 mm instead of 1.0 costs 7.8x
    /// less</b>, because all three axes drop together. A rule of thumb cannot find that, so
    /// the candidate is evaluated with the same arithmetic the grid uses.
    /// </para>
    /// </remarks>
    private static string MeshNote(IReadOnlyList<ElementEstimate> elements)
    {
        // The largest solve in the model, since that is the one worth tuning.
        ElementEstimate? worst = null;

        foreach (var element in elements)
        {
            if (element.Spacing.Count > 0 && (worst is null || element.NodeCount > worst.NodeCount))
            {
                worst = element;
            }
        }

        if (worst is null || worst.RequestedCell <= 0.0 || worst.Spacing.Count != worst.Nodes.Count)
        {
            return string.Empty;
        }

        // A rounding of less than about a fifth is not worth a sentence.
        if (worst.Spacing.Min() > 0.8 * worst.RequestedCell)
        {
            return string.Empty;
        }

        // The extent each axis actually spans: its spacing times its interval count.
        var extents = new double[worst.Spacing.Count];

        for (var axis = 0; axis < extents.Length; axis++)
        {
            extents[axis] = worst.Spacing[axis] * (worst.Nodes[axis] - 1);
        }

        // Every request at which some axis drops a power of two, evaluated exactly rather
        // than reasoned about: the boundary is where extent / h is a power of two, and a
        // candidate sitting on it lands the wrong side by a rounding. The margin is what a
        // first version left out, which made the suggestion a no-op - 1.24 mm was offered
        // against a 1 mm request and produced the identical mesh.
        //
        // The cheapest candidate wins rather than the nearest, and it is bounded: spacings
        // differ by at most 2:1 by construction, so the coarsest candidate is at most twice
        // the largest spacing and no axis can drop more than a couple of powers. The
        // resulting node count and ratio are both reported, so the trade is on the page
        // rather than implied.
        var best = worst.NodeCount;
        var at = 0.0;

        foreach (var spacing in worst.Spacing)
        {
            var candidate = 2.0 * spacing * (1.0 + 1e-9);

            // A volume asks the rule its grid is built by, rather than this file's own copy of
            // it: that copy allowed one interval an axis where the grid allows no fewer than
            // two, so a thin axis was costed at two nodes where the grid builds three, and the
            // promised saving came out half again too large.
            var nodes = extents.Length == 3
                ? Core.Numerics.VolumeMesh.Nodes(extents[0], extents[1], extents[2], candidate)
                : NodesAt(extents, candidate);

            if (nodes < best)
            {
                best = nodes;
                at = candidate;
            }
        }

        var achieved = string.Join(
            " x ", worst.Spacing.Select(m => (m * 1e3).ToString("G3", Inv)));

        var note = string.Create(
            Inv,
            $"The mesh is {achieved} mm against a requested {worst.RequestedCell * 1e3:G3} mm, "
            + $"because each axis rounds its interval count up to a power of two - finer than "
            + $"asked for on every axis, never coarser, with the node count the product of "
            + $"three such roundings.");

        if (at <= 0.0 || best >= worst.NodeCount)
        {
            return note;
        }

        return note + string.Create(
            Inv,
            $" Cost is therefore a step function of the cell size: asking for "
            + $"{at * 1e3:G3} mm would give {best / 1e6:G3} M nodes against "
            + $"{worst.NodeCount / 1e6:G3} M, which is {(double)worst.NodeCount / best:F1}x less.");
    }

    /// <summary>Bytes in a mebibyte, the unit the estimate reports memory in.</summary>
    private const double Mebibyte = 1024.0 * 1024.0;

    /// <summary>A byte count, spelled the way the table beside it spells it.</summary>
    private static string Size(double bytes) => MemorySize(bytes / Mebibyte);

    /// <summary>A memory figure in mebibytes, as something a person sizes a machine against.</summary>
    /// <param name="mebibytes">The figure, in mebibytes - the unit the estimate reports it in.</param>
    /// <returns>Mebibytes to one decimal below a gibibyte, gibibytes to one decimal above.</returns>
    /// <remarks>
    /// Public for the reason <see cref="Duration"/> is: the CLI prints the same quantity beside
    /// the basis sentence that contains it, and two spellings of one number on one page is a
    /// reader's problem. "15630.5 MiB" answers "will this fit in 16 GB" less directly than
    /// "15.3 GiB" does.
    /// </remarks>
    public static string MemorySize(double mebibytes) => mebibytes switch
    {
        < 1024.0 => string.Create(Inv, $"{mebibytes:F1} MiB"),
        _ => string.Create(Inv, $"{mebibytes / 1024.0:F1} GiB"),
    };

    /// <summary>Says what the largest solve holds and why.</summary>
    /// <param name="largest">The element whose solve holds the most, and what it holds.</param>
    /// <param name="elements">The per-element estimates, for the element's kind.</param>
    /// <returns>A sentence for the basis line, or empty when nothing is solved.</returns>
    /// <remarks>
    /// <para>
    /// <b>The basis of the memory number, stated for the same reason the time's is.</b> This
    /// figure was 48 bytes a node with nothing on the page to say so, and it was a fifth of
    /// what a volume solve holds. A number somebody sizes a machine against has to say what it
    /// counted. Whether the peak fits this machine is <see cref="CapacityNote"/>'s.
    /// </para>
    /// </remarks>
    private static string MemoryNote(
        (int Index, SolveMemory Memory)? largest,
        IReadOnlyList<ElementEstimate> elements)
    {
        if (largest is not { } found || found.Memory.Nodes <= 0)
        {
            return string.Empty;
        }

        var plan = found.Memory;
        var volume = elements.FirstOrDefault(e => e.Index == found.Index)?.Nodes.Count == 3;
        var perNode = plan.PeakBytes / plan.Nodes;

        var hierarchy = !volume
            ? string.Empty
            : plan.Galerkin
                ? " on the Galerkin multigrid hierarchy"
                : " on the rediscretized multigrid hierarchy";

        var channels = plan.Channels > 1
            ? string.Create(
                Inv, $" with {plan.Channels} basis channels solved one at a time, each keeping its potential")
            : string.Empty;

        var parts = volume && plan.Galerkin
            ? ", and the stored operator while its first coarse level is formed"
            : ", and the coarse levels";

        var note = string.Create(
                Inv,
                $"Memory: field {found.Index} holds {Size(plan.PeakBytes)} at its peak, {perNode:F0} bytes a node")
            + hierarchy
            + channels
            + " - the conductor mask and its cut links, three fields"
            + parts
            + ", counted from what the solver allocates and measured against real builds. That is "
            + "the live set, which is what has to fit; a run's working set is higher on a machine "
            + "with memory to spare, because the collector frees large arrays lazily.";

        return note;
    }

    /// <summary>Says whether the model's peak fits what this process may use, when it is close.</summary>
    /// <param name="peakMiB">The model's peak, in mebibytes.</param>
    /// <param name="available">What the process may use, in bytes; nothing is said when unknown.</param>
    /// <returns>A sentence for the basis line, or empty when the peak is under half of it.</returns>
    /// <remarks>
    /// <para>
    /// <b>Only when it matters</b> - above half of what the process may use - so an ordinary
    /// estimate carries no statement about the hardware, and one that is about to run out of
    /// memory says so before the run rather than twenty minutes in.
    /// </para>
    /// <para>
    /// <b>Asked of the model's peak, whatever holds it.</b> This was the tail of the solved-field
    /// explanation, so a model with nothing solved - a diffusive run through an analytic field,
    /// which holds 263 bytes a density node and more for every species - never reached it, and
    /// a run that would not fit was priced without a word.
    /// </para>
    /// </remarks>
    private static string CapacityNote(double peakMiB, long available)
    {
        var peak = peakMiB * Mebibyte;

        if (available <= 0 || peak <= 0.5 * available)
        {
            return string.Empty;
        }

        var share = peak / available;

        return string.Create(
                Inv, $"Memory: the model's peak is {share:P0} of the {Size(available)} this process may use on this machine")
            + (share >= 1.0 ? ", so it will not fit here." : ", which leaves little room.");
    }

    /// <summary>How many nodes a requested cell size produces over a plane's extents.</summary>
    /// <remarks>
    /// <para>
    /// The grid's rule, restated: each axis takes the interval count that covers its extent at
    /// the requested size, rounded up to a power of two, and the node count is one more than
    /// that per axis.
    /// </para>
    /// <para>
    /// <b>Planes only.</b> A volume asks <c>VolumeMesh</c>, which is the rule its grid is built
    /// by. This restatement still allows one interval an axis where the plane grid allows no
    /// fewer than four, so it too can be wrong on a thin axis; bringing the plane under one
    /// shared rule belongs with the plane's own node limit, which does not exist yet.
    /// </para>
    /// </remarks>
    private static long NodesAt(IReadOnlyList<double> extents, double cell)
    {
        var nodes = 1L;

        foreach (var extent in extents)
        {
            var needed = Math.Max(1.0, Math.Ceiling(extent / cell));
            var intervals = (long)Math.Pow(2.0, Math.Ceiling(Math.Log2(needed)));

            nodes *= intervals + 1;
        }

        return nodes;
    }

    /// <summary>Invariant formatting, because a basis line travels.</summary>
    private static System.Globalization.CultureInfo Inv =>
        System.Globalization.CultureInfo.InvariantCulture;


    /// <summary>What the mutual force will cost, by whichever method computes it.</summary>
    /// <remarks>
    /// <para>
    /// GRD-8 gates an operation above a cost threshold and needs a number to gate
    /// on without doing the work. Direct space charge is the first thing here whose
    /// cost is <em>quadratic</em> in a number a user types: raising a cloud from 150
    /// trajectories to 2,000 is a factor of 178, and 87 seconds becomes four hours.
    /// A linear intuition is exactly wrong, so the basis says so in words.
    /// </para>
    /// <para>
    /// <b>Particle-in-cell is linear in that same number and is not simply cheaper.</b>
    /// It pays for one Poisson solve per refresh whatever the cloud, so below the
    /// crossing the reference method is faster - and an estimate that reported the
    /// asymptotics would recommend the approximation exactly where it loses. Both are
    /// costed in the same currency, pair-equivalents a stage, so their ratio at a
    /// given cloud is something the basis can state rather than imply.
    /// </para>
    /// <para>
    /// The step count is the one thing here that is not knowable in advance - it is
    /// whatever the adaptive controller decides - so it is taken from the flight
    /// time and a step scale, and the estimate is an order of magnitude rather than
    /// the exact number the diffusive estimate can give.
    /// </para>
    /// </remarks>
    private static (double Seconds, string Basis) SelfField(CompiledModel model)
    {
        var trajectories = Math.Max(model.Cloud.Ions, 2);
        var pairs = 0.5 * trajectories * (trajectories - 1.0);
        var steps = EstimatedSteps;

        // Seven Dormand-Prince stages, each evaluating the mutual force.
        //
        // Measured on this codebase: 150 trajectories through the rectilinear trap
        // took 87 s over roughly eleven thousand steps, which is 8.7e8 pair
        // evaluations. Rounded to one figure, because it is a rate on one machine.
        double Cost(double workPerStage) => workPerStage * StagesPerStep * steps / PairsPerSecond;

        var nodes = model.SpaceChargeGrid?.Nodes ?? 32;

        // Two terms, because the node count is now something a document sets and the
        // one it scales is the one that dominates. The gather is linear in the cloud;
        // the solve is cubic in the node count and does not see the cloud at all.
        var gridWork =
            (PicGatherPerTrajectory * trajectories)
            + (PicSolveWork * Math.Pow(nodes / (double)PicCalibrationNodes, 3.0));

        if (!string.Equals(model.SpaceChargeMode, "pic", StringComparison.Ordinal))
        {
            var basis =
                $"Space charge is summed over every pair: {trajectories:N0} trajectories give {pairs:N0} "
                + $"pairs, {StagesPerStep} stages a step and about {steps:N0} steps, at {PairsPerSecond:G2} "
                + "pair evaluations a second. THE COST IS QUADRATIC IN THE TRAJECTORY COUNT, so doubling "
                + "the cloud quadruples this - the linear intuition is exactly wrong here. Lower "
                + "\"ions\" and raise \"population\" to keep the packet's charge while computing fewer "
                + "of them."
                + (trajectories > PicCrossingTrajectories
                    ? $" Above about {PicCrossingTrajectories:N0} trajectories the grid method is the "
                        + $"cheaper one - here by about {pairs / gridWork:N1}x - so \"spaceCharge\": "
                        + "\"pic\" is worth considering, bearing in mind that this one is the reference "
                        + "it was validated against."
                    : string.Empty);

            return (Cost(pairs), basis);
        }

        // Linear in the cloud and cubic in the node count, calibrated on two things
        // actually measured: the two methods cross near 850 macroparticles at 32 nodes,
        // and the work there splits about 43 to 57 between solving and gathering.
        // Anchoring on measurements rather than on a fitted rate keeps the number a
        // reader acts on exact.
        var picBasis =
            $"Space charge is deposited onto a grid and solved: {trajectories:N0} trajectories, "
            + $"{StagesPerStep} stages a step and about {steps:N0} steps. THE COST IS LINEAR IN THE "
            + "TRAJECTORY COUNT AND CUBIC IN THE NODE COUNT, so doubling the cloud doubles the "
            + "gather while the solve is paid for whatever the cloud - which is why it is not "
            + "simply cheaper than the sum: at the default "
            + $"{PicCalibrationNodes} nodes the two methods cross near "
            + $"{PicCrossingTrajectories:N0} trajectories"
            + (gridWork > pairs
                ? $", and here the pairwise sum is about {gridWork / pairs:N1}x faster. "
                    + $"\"spaceCharge\": \"direct\" is also the reference method."
                : $", and this cloud is above it by about {pairs / gridWork:N1}x.")
            + (nodes == PicCalibrationNodes
                ? string.Empty
                : $" THE SOLVE IS CUBIC IN THE NODE COUNT: {nodes} nodes rather than "
                    + $"{PicCalibrationNodes} is "
                    + $"{Math.Pow(nodes / (double)PicCalibrationNodes, 3.0):N1}x that part of the "
                    + "work, and a finer grid is not a better answer here - accuracy has an optimum "
                    + "near one cell per mean macroparticle spacing.");

        return (Cost(gridWork), picBasis);
    }

    /// <summary>Dormand-Prince stages that each evaluate the mutual force.</summary>
    private const int StagesPerStep = 7;

    /// <summary>Pair evaluations a second, measured on this codebase's own runs.</summary>
    private const double PairsPerSecond = 1e7;

    /// <summary>Where the grid method starts beating the pairwise sum.</summary>
    /// <remarks>
    /// Measured rather than derived: 0.16x at 250 macroparticles, 0.42x at 500, 1.21x
    /// at 1,000, 3.21x at 2,000. It is the number worth stating because it is the one
    /// a reader acts on - quoting the asymptotics alone would recommend the
    /// approximation exactly where it loses.
    /// </remarks>
    private const double PicCrossingTrajectories = 850.0;

    /// <summary>The node count the crossing was measured at.</summary>
    private const int PicCalibrationNodes = 32;

    /// <summary>
    /// How the grid method's work splits between gathering and solving, at the
    /// calibration node count.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured by running the same packet at 16, 32, 64 and 128 nodes and at 200 and
    /// 800 macroparticles - eight points, two knobs. A two-term fit of
    /// <c>solves x c_solve x nodes^3 + calls x c_gather x N</c> lands within 12% at
    /// every point except the smallest grid, where a per-solve overhead the model does
    /// not carry becomes visible. At 32 nodes and the crossing cloud that split is
    /// about 43% solve to 57% gather.
    /// </para>
    /// <para>
    /// It matters because the two scale with different things: 200 macroparticles took
    /// <b>0.99 s at 16 nodes and 124 s at 128</b>, a factor of 126 for a knob a
    /// document can now set. An estimate blind to it would gate on a number missing
    /// its dominant term.
    /// </para>
    /// </remarks>
    private const double PicSolveFraction = 0.43;

    /// <summary>
    /// The solve's work at <see cref="PicCalibrationNodes"/>, in pair-equivalents.
    /// </summary>
    private const double PicSolveWork =
        PicSolveFraction * 0.5 * PicCrossingTrajectories * (PicCrossingTrajectories - 1.0);

    /// <summary>
    /// The gather's work per trajectory, in the pairwise sum's own currency.
    /// </summary>
    /// <remarks>
    /// Both constants are pinned by one measured fact and one measured ratio: at
    /// <see cref="PicCrossingTrajectories"/> and <see cref="PicCalibrationNodes"/> the
    /// two methods cost the same, and <see cref="PicSolveFraction"/> says how that
    /// total divides. Costing both in pair-equivalents is what lets the estimate
    /// compare them at all, and ties the model to something measured rather than to a
    /// rate that was fitted.
    /// </remarks>
    private const double PicGatherPerTrajectory =
        (1.0 - PicSolveFraction) * 0.5 * (PicCrossingTrajectories - 1.0);

    /// <summary>
    /// Steps a packet flight takes, as an order of magnitude.
    /// </summary>
    /// <remarks>
    /// The one quantity here that is not knowable before the run: it is whatever the
    /// adaptive controller decides. Ten thousand is what the shipped templates take.
    /// </remarks>
    private const double EstimatedSteps = 1e4;

    /// <summary>
    /// What a diffusive run will cost, from the mesh and the mobility alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The step is bounded from above by the diffusion limit, which needs no field:
    /// D comes from the mobility and the temperature, and the mesh is declared. The
    /// drift limit needs the field, which is the thing not solved yet, so the real
    /// step can only be smaller and the real cost only larger - which is stated
    /// rather than left for someone to discover by waiting.
    /// </para>
    /// <para>
    /// The direction of the pressure dependence is the part worth reading. D goes as
    /// one over pressure, so a <em>thinner</em> gas diffuses faster, needs a smaller
    /// step, and costs more. That is the opposite of the event-driven mode, where a
    /// thinner gas means fewer collisions, and the opposite of most people's
    /// intuition about which regime is expensive.
    /// </para>
    /// </remarks>
    private static (double Seconds, double MemoryMiB, string Basis) Diffusive(
        CompiledModel model, string modelDirectory)
    {
        var grid = DiffusionRun.GridFor(model);

        // Resolved, because an imported pressure field changes the mobility and so
        // the stability limit, which is the whole number this function reports. An
        // estimate taken in a gas the model does not declare is an estimate of a
        // different run - and GRD-8 exists to be relied on before the work is done.
        var gas = Io.GasFlowImport.Resolve(model.Gas, modelDirectory);
        var species = Transport.IonSpecies.FromModel(model);

        var declaredMobility = model.Mobility is { Derived: false } given
            ? new Transport.Diffusion.Mobility(
                given.ZeroFieldSi, given.Alpha, given.ValidToTownsend)
            : Transport.Diffusion.Mobility.FromCrossSection(gas, species);

        // At the thinnest gas ON THIS GRID, when the density varies. Mobility goes
        // as the reciprocal of density, so both stability limits - the Courant one on
        // the drift and the diffusive one - are set where there is least to collide
        // with, and the run finds that worst case from its own per-node arrays.
        //
        // Over the tracked grid rather than over the whole imported field, and the
        // difference is not academic: a field solved on a larger box may be thinner
        // somewhere no ion is tracked through, and taking its minimum over-predicted
        // a shipped case by 50% - 2,252 steps against 1,502 - because the field ran to
        // 0.5 mbar while the grid only reached 0.75. GRD-8's claim is that estimate
        // and run agree exactly, so the two have to be asking about the same region.
        //
        // At the declared density this is ZeroFieldSi exactly - the ratio is 1.0 and
        // a zero field makes the expansion term one - so an ungraded model is
        // bit-identical to what it was and skips the sweep entirely.
        var thinnestOnGrid = gas.NumberDensitySi;

        if (gas.IsGraded)
        {
            thinnestOnGrid = double.PositiveInfinity;

            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    thinnestOnGrid = Math.Min(
                        thinnestOnGrid,
                        gas.NumberDensityAt(new Core.Geometry.Vec3(grid.X(i), grid.Y(j), 0.0)));
                }
            }
        }

        var mobility = declaredMobility.At(0.0, thinnestOnGrid, gas.NumberDensitySi);

        var diffusion = Transport.Diffusion.Mobility.DiffusionSi(
            gas.TemperatureK, species.ChargeSi, mobility);

        // THE DRIFT LIMIT IS INCLUDED, AND THE FIELD IS SOLVED TO GET IT.
        //
        // This used to be evaluated only where every element was analytic, on the argument
        // that "solving the field to estimate the cost of the run defeats the purpose of
        // estimating". That is sound in general and wrong for this mode, by four orders: on
        // the shipped TIMS front end it reported **8 s against an actual 197,000**, because
        // the drift limit is not a refinement here - it IS the cost. The ramped phase steps
        // at about 56 ns where the diffusion limit alone allows 1.26e-4 s, a factor of 2,236.
        //
        // The trade only looks bad against the estimate's own runtime. Against what is being
        // estimated it is free, and the reason is structural rather than empirical: **a
        // diffusive run cannot be cheaper than its own solve**, since it must solve the same
        // field and then step through it thousands of times. So solving here costs at most
        // the run's unavoidable floor - 1.81 s of 197,000 on that model - and the estimate
        // reports what it spent so the reader can see the price.
        var solveWatch = System.Diagnostics.Stopwatch.StartNew();
        var (built, _) = Fields.FieldAssembly.BuildReported(model);
        solveWatch.Stop();

        var sign = Math.Sign(species.ChargeSi);
        var fastestDrift = 0.0;

        // THE EFFECTIVE FIELD, which is what the run drifts a density through. A driven
        // geometry reached through the time-free interface answers at an arbitrary instant -
        // the peak of the RF rather than its cycle average - and this sampled it that way,
        // which is the ninth appearance of that defect here and the first in the cost gate.
        // `Effective` is the same wrapper `DiffusionRun` installs, so the two now sample one
        // field; where there is no drive it returns the field unchanged.
        //
        // At every phase boundary as well as at zero. The potential is linear in the channel
        // weights and a ramp is linear in time, so a phase's extremes are at its ends and the
        // fastest drift anywhere in the sequence is at one of them. Sampling only t = 0 would
        // have been right for a sequence that ramps DOWN and an under-estimate for one that
        // ramps up, which is the unsafe direction for a gate.
        var instants = new List<double> { 0.0 };

        foreach (var phase in model.Phases)
        {
            instants.Add(phase.EndsAtSeconds);
        }

        // Whether the run will step through a cycle average, which is also whether it builds
        // the ponderomotive well cache - seven arrays over the density grid.
        var averaged = false;

        foreach (var atSeconds in instants)
        {
            var field = built;
            averaged |= DiffusionRun.Effective(ref field, species, declaredMobility, gas, atSeconds) is not null;

            // Every node where the gas is graded, every other one where it is not. The run
            // samples all of them; a stride is harmless on a field that is smooth between
            // nodes, and the mobility is what varies fastest where the gas does.
            var stride = gas.IsGraded ? 1 : 2;

            for (var j = 0; j < grid.CountY; j += stride)
            {
                for (var i = 0; i < grid.CountX; i += stride)
                {
                    var point = new Core.Geometry.Vec3(grid.X(i), grid.Y(j), 0.0);
                    var electric = field.ElectricFieldAt(in point);

                    var local = gas.IsGraded
                        ? declaredMobility.At(
                            0.0, gas.NumberDensityAt(in point), gas.NumberDensitySi)
                        : mobility;

                    fastestDrift = Math.Max(
                        fastestDrift,
                        Transport.Diffusion.DriftDiffusion.CrossingRate(
                            grid, sign * local * electric.X, sign * local * electric.Y));
                }
            }
        }

        // The same weight the run will use, from the same function, so the estimate
        // and the run cannot disagree about what a step is. On the axis of a
        // cylindrical solve it is four, and an estimate that assumed a plane would
        // report a quarter of the steps.
        var cylindrical = model.Fields.Any(
            f => f.Solve?.Symmetry == Core.Model.SolveSymmetry.Cylindrical);

        var weight = new Transport.Diffusion.DensityField(grid, cylindrical).LargestRadialWeight();

        var (stable, limit) = Transport.Diffusion.DriftDiffusion.StepFor(
            grid, diffusion, fastestDrift, weight);

        // The implicit scheme steps past the stability limit and pays Gauss-Seidel
        // sweeps for it, so both halves have to enter the estimate or it is an
        // estimate of a run nobody asked for. Ignoring the gain would over-state the
        // cost by that factor - the safe direction, but GRD-8 gates on this number and
        // a gate that refuses a run costing a minute is as wrong as one that waves
        // through an hour.
        var implicitly = model.DensityStep.IsImplicit;

        var step = implicitly ? stable * model.DensityStep.Gain : stable;

        var steps = Math.Max(1.0, Math.Ceiling(model.MaximumFlightTimeSi / step));

        // Measured at 3.0 on the shipped funnel across gains from 4 to 256, rising to
        // 4.9 at 1024. A sweep costs about what an explicit step costs - it is the
        // same pass over the same coefficients - so this is the multiplier on the work
        // and not a small correction.
        var sweeps = implicitly ? SweepsPerStep : 1.0;

        var cells = steps * sweeps * grid.NodeCount;

        var seconds = cells / (MegaCellsPerSecond * 1e6);

        // Measured, not the two density fields this used to count. A step holds the seed, the
        // density and the next one, six stored coefficients a node and the face operator -
        // four faces of four doubles and two flags, which is Amendment 49's workspace - and
        // the run holds more than those name.
        // Once per declared species: each steps its own density through its own coefficients,
        // face operator and well, and nothing of the working set is shared between them.
        var speciesCount = Math.Max(1, model.Species.Count);

        var memory = grid.NodeCount * speciesCount
            * (DiffusiveBytesPerNode + (averaged ? WellCacheBytesPerNode : 0.0))
            / Mebibyte;

        return (
            seconds,
            memory,
            $"diffusive run: {grid.CountX} by {grid.CountY} nodes"
            + (speciesCount > 1
                ? $", {speciesCount} species each holding its own density and coefficients over all of them"
                : string.Empty)
            + $", a stability-limited step of "
            + $"{stable:G3} s set by {limit}, "
            + (implicitly
                ? $"stepped implicitly at {model.DensityStep.Gain:N0}x that - {step:G3} s - so "
                    + $"about {steps:N0} steps at an assumed {sweeps:F1} Gauss-Seidel sweeps each "
                    + "over "
                : $"so about {steps:N0} steps over ")
            + $"{model.MaximumFlightTimeSi * 1e6:G4} us, at {MegaCellsPerSecond:G2} million cell "
            + "updates per second measured on this codebase. "
            + $"Both stability limits are included. The field was solved to get the drift one, "
            + $"which cost {solveWatch.Elapsed.TotalSeconds:F2} s of this estimate - a diffusive "
            + "run cannot be cheaper than its own solve, so that is bounded by the run's floor, "
            + "and omitting it used to make this number four orders low on a ramped model. "
            + $"Sampled at {instants.Count} instant(s) - zero and every phase boundary - through "
            + "the cycle-averaged field the run drifts through, not the instantaneous one."
            + (implicitly
                ? " The sweep count is the one quantity here that is not knowable in advance - it "
                    + "depends on how far past the DIFFUSION limit the step lands, not on the gain "
                    + "itself - so this is an order of magnitude where the explicit estimate is "
                    + "exact."
                : string.Empty)
            + " Note that the diffusion coefficient goes as one over pressure: a thinner gas is "
            + "MORE expensive here, not less");
    }
}
