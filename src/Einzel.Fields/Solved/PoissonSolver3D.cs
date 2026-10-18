namespace Einzel.Fields.Solved;

/// <summary>
/// Laplace's equation on a three-dimensional grid, by geometric multigrid.
/// </summary>
/// <remarks>
/// <para>
/// The same machine as the two-dimensional solver and the same reasons for each
/// part: red-black Gauss-Seidel so a sweep is order-independent, full-weighting
/// restriction and trilinear prolongation so the transfer operators are adjoint,
/// and V-cycles so the iteration count does not grow with the mesh.
/// </para>
/// <para>
/// The stencil is Shortley-Weller on six arms rather than four, written in cell
/// units so its coefficients stay of order one at any spacing, and it reduces
/// exactly to the seven-point formula where nothing is cut.
/// </para>
/// <para>
/// Coarse levels are rebuilt from geometry rather than projected down, and they
/// are <em>node-aligned</em> rather than sub-cell. That division is what makes
/// interior electrodes work: accuracy comes from the finest level, where the cuts
/// are, and acceleration from the levels below, where a cut would produce arms a
/// thousandth of a cell long and an operator that is ill-conditioned rather than
/// merely coarse. A charged sphere went from 13 seconds with no coarsening to 783
/// milliseconds with it, at the same answer to the digit.
/// </para>
/// <para>
/// The values a coarse mask carries do not matter, only which nodes it fixes: a
/// V-cycle solves for the error, whose Dirichlet data is zero, and the correction
/// array starts at zero and is never seeded from the mask. So a coarse level that
/// merges two differently-driven electrodes is crude, not wrong. What is not
/// allowed is a coarse cell larger than the electrode itself - that is a different
/// problem rather than a coarser one, and its correction points elsewhere.
/// </para>
/// </remarks>
public static class PoissonSolver3D
{
    /// <summary>Solves for the potential.</summary>
    /// <param name="mask">Fixed nodes, cuts and face conditions.</param>
    /// <param name="tolerance">Relative residual to reach.</param>
    /// <param name="maximumCycles">Cycle ceiling.</param>
    /// <param name="coarsen">
    /// Rebuilds the mask on a coarser grid from the geometry. Supplying it is what
    /// lets a solve with interior electrodes coarsen at all.
    /// </param>
    /// <param name="source">
    /// The right-hand side of <c>grad^2 phi = source</c>, or null for Laplace. A
    /// charge density enters as <c>-rho / epsilon0</c>.
    /// </param>
    /// <param name="initialGuess">A starting field, or null for zeros.</param>
    /// <param name="galerkin">
    /// Whether to build the coarse levels from the fine operator rather than from the
    /// geometry, or null to decide from the geometry - which is the usual case.
    /// </param>
    /// <returns>The potential, and how the solve went.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mask"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The tolerance or the cycle ceiling is not positive.</exception>
    public static (ScalarField3D Potential, SolveReport Report) Solve(
        DirichletMask3D mask,
        double tolerance = 1e-10,
        int maximumCycles = 200,
        Func<Grid3D, DirichletMask3D>? coarsen = null,
        ScalarField3D? initialGuess = null,
        ScalarField3D? source = null,
        bool? galerkin = null)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tolerance);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCycles);

        var grid = mask.Grid;
        var potential = initialGuess?.Clone() ?? new ScalarField3D(grid);

        potential.LowerX = mask.LowerX;
        potential.UpperX = mask.UpperX;
        potential.LowerY = mask.LowerY;
        potential.UpperY = mask.UpperY;
        potential.LowerZ = mask.LowerZ;
        potential.UpperZ = mask.UpperZ;

        mask.ApplyTo(potential);

        // Laplace when nothing is given, Poisson when something is. Same argument as
        // in two dimensions: the cycle already carries a right-hand side, and the
        // coarse levels receive the restricted residual whatever the fine source is.
        // The convention the residual fixes is grad^2 phi = source, so a charge
        // density enters as -rho/epsilon0.
        var rightHandSide = source ?? new ScalarField3D(grid);

        if (source is not null
            && (source.Grid.CountX != grid.CountX
                || source.Grid.CountY != grid.CountY
                || source.Grid.CountZ != grid.CountZ))
        {
            throw new ArgumentException(
                "the source is on a different grid from the mask, so its values do not "
                + "correspond to the nodes being solved",
                nameof(source));
        }
        var residual = new ScalarField3D(grid);

        var initial = Residual(potential, rightHandSide, mask, residual);

        if (initial == 0.0)
        {
            return (potential, new SolveReport(true, 0, 0.0, 0.0, 0.0)
            {
                CoarsestNodes = grid.NodeCount,
            });
        }

        var current = initial;
        var cycles = 0;
        var stalled = 0;
        var work = new CycleWork();

        // Built once, not per cycle: the operator does not change while the solve runs,
        // and forming it is the expensive part of using it.
        var hierarchy = (galerkin ?? WorthGalerkin(mask))
            ? GalerkinHierarchy3D.Build(
                OperatorStencil3D.Assemble(mask, potential),
                mask,
                0.5 * grid.SpacingX * grid.SpacingX)
            : null;

        while (cycles < maximumCycles && current > tolerance * initial)
        {
            if (hierarchy is null)
            {
                Cycle(potential, rightHandSide, mask, coarsen, work, 0);
            }
            else
            {
                GalerkinCycle(potential, rightHandSide, mask, hierarchy, work);
            }

            cycles++;

            var next = Residual(potential, rightHandSide, mask, residual);

            if (next >= current)
            {
                stalled++;

                // Three cycles without progress, not one. A V-cycle on a geometry
                // with sub-cell surfaces can spend a cycle going sideways and then
                // resume; stopping at the first one turns a slow solve into a wrong
                // answer, which is much the worse failure of the two.
                if (stalled >= 3)
                {
                    current = next;
                    break;
                }
            }
            else
            {
                stalled = 0;
            }

            current = next;
        }

        var factor = cycles > 0 ? Math.Pow(current / initial, 1.0 / cycles) : 0.0;

        return (
            potential,
            new SolveReport(current <= tolerance * initial, cycles, initial, current, factor)
            {
                Levels = hierarchy?.Levels ?? work.Levels,
                Sweeps = work.Sweeps + (hierarchy?.Sweeps ?? 0L),
                CoarsestNodes = hierarchy is null ? work.CoarsestNodes : hierarchy.Coarsest.NodeCount,
                Galerkin = hierarchy is not null,
            });
    }

    /// <summary>
    /// What a solve did, as opposed to how many cycles it took to do it.
    /// </summary>
    /// <remarks>
    /// Threaded through the recursion rather than derived afterwards, because how far
    /// a cycle descends depends on the geometry at every level and there is no way to
    /// work it out from the outside without repeating the decision - and a second
    /// implementation of a decision is a second decision.
    /// </remarks>
    private sealed class CycleWork
    {
        public int Levels { get; set; }

        public long Sweeps { get; set; }

        public long CoarsestNodes { get; set; }
    }

    /// <summary>Smoothing sweeps before coarsening and after correcting.</summary>
    private const int PreSmooth = 2;

    /// <summary>Smoothing sweeps after prolongation.</summary>
    private const int PostSmooth = 2;

    /// <summary>Sweeps allowed at the level a V-cycle stops descending from.</summary>
    private const int CoarsestSweeps = 400;

    /// <summary>
    /// Solves the level a V-cycle stops descending from, rather than smoothing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Textbook multigrid stops at a grid small enough that a handful of sweeps is
    /// effectively exact. This one does not always get there: an interior electrode
    /// stops the descent while the grid may still be thousands of nodes, and a few
    /// sweeps on that is not a solve - it is a guess, and a guess prolonged back is
    /// a correction pointing somewhere unhelpful.
    /// </para>
    /// <para>
    /// That cost a real failure. With eight sweeps at the bottom, a charged sphere
    /// in a grounded box reached 137 V of 100 applied and its error grew with
    /// refinement; the same geometry with no coarsening at all was correct to 2.7 V.
    /// The fine operator was never wrong. The bottom of the hierarchy was.
    /// </para>
    /// </remarks>
    /// <summary>Relaxes the bottom level, and returns how many sweeps that took.</summary>
    /// <remarks>
    /// The sweep count is returned rather than discarded because on a geometry that
    /// cannot coarsen this <em>is</em> the solve: the whole V-cycle reduces to this
    /// call on the finest grid, and a "cycle" that reports as one unit of work is
    /// several hundred sweeps over the full mesh.
    /// </remarks>
    private static int SolveCoarsest(
        ScalarField3D potential, ScalarField3D rightHandSide, DirichletMask3D mask)
    {
        var scratch = new ScalarField3D(mask.Grid);

        var initial = Residual(potential, rightHandSide, mask, scratch);

        if (initial == 0.0)
        {
            return 0;
        }

        for (var sweep = 0; sweep < CoarsestSweeps; sweep++)
        {
            Smooth(potential, rightHandSide, mask);

            // Checked every few sweeps rather than every one: a residual sweep costs
            // about what a smoothing sweep does, and doubling the price of the
            // bottom level to stop a little earlier is not a trade worth making.
            if (sweep % 8 == 7 && Residual(potential, rightHandSide, mask, scratch) <= 1e-3 * initial)
            {
                return sweep + 1;
            }
        }

        return CoarsestSweeps;
    }

    /// <summary>
    /// Whether a coarse level would still represent the geometry, or merely record
    /// that it once existed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An electrode occupies a fixed physical size, so every coarsening halves its
    /// extent in each direction - and in three dimensions it loses seven eighths of
    /// its nodes per level rather than three quarters. Past the point where a cell
    /// is bigger than the electrode, the coarse grid is solving a different problem,
    /// and its correction prolonged back drives the iteration apart: a 3 mm sphere
    /// taken down to 12 mm cells sent the potential to 145 V of 100 applied, which
    /// the maximum principle caught and nothing else would have.
    /// </para>
    /// <para>
    /// The test is on the physical size, not on how many nodes are left. A node
    /// count stays positive long after a level has stopped representing anything -
    /// on the finest level because cut links keep recording the surface at any
    /// spacing, and on a coarse level because an electrode too small to rasterise
    /// is pinned to its nearest node rather than allowed to vanish.
    /// </para>
    /// <para>
    /// Against the <em>coarsest</em> of the three spacings, not the finest. Each
    /// axis rounds its own interval count up to a power of two, so a 2:1 aspect is
    /// ordinary rather than exceptional; asking the finest axis let the shipped
    /// segmented quadrupole descend to a level whose z cell was 4.875 mm against a
    /// 4.587 mm rod radius, which is exactly the condition this guard exists to
    /// refuse.
    /// </para>
    /// </remarks>
    private static bool Representable(Grid3D coarse, DirichletMask3D mask) =>
        Representable(coarse, mask.SmallestFeature, mask.InteriorFixedCount > 0);

    /// <summary>The same test, on what a mask records rather than on the mask itself.</summary>
    /// <remarks>
    /// Split out so that <see cref="UsesGalerkin"/> can ask it of a geometry that has not
    /// been rasterized, and so that there is one statement of the rule rather than two.
    /// </remarks>
    private static bool Representable(Grid3D coarse, double smallestFeature, bool interiorElectrodes) =>
        !interiorElectrodes
        || double.IsPositiveInfinity(smallestFeature)
        || coarse.MaximumSpacing <= ResolvedBy * smallestFeature;

    /// <summary>
    /// How well a coarse level must still resolve an electrode to be worth
    /// descending to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One: a level may coarsen while a cell is no larger than the electrode, and
    /// not past it. That is the physical statement - below it the level is a cruder
    /// version of the same problem, above it a different one - and it is only usable
    /// because coarse levels are node-aligned. It was a quarter while they carried
    /// cuts, because a sub-cell surface on a coarse grid is ill-conditioned and the
    /// only defence was to barely coarsen at all; a charged sphere reached 137 V of
    /// 100 applied through two such levels.
    /// </para>
    /// <para>
    /// The real fix is still Galerkin coarsening or operator-dependent
    /// interpolation - building the coarse operator from the fine one rather than
    /// from the geometry again - which would remove this guard rather than tune it,
    /// and is the same fix the two-dimensional solver has been waiting for.
    /// </para>
    /// </remarks>
    private const double ResolvedBy = 1.0;

    /// <summary>
    /// Whether rediscretising the coarse levels would leave the bottom of the V too big.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Neither hierarchy dominates, so the solver works out which it is looking at
    /// rather than being told.</b> The rediscretised one is cheap to build and stops
    /// coarsening once a coarse cell would exceed the smallest electrode - a physical
    /// size that does not move when the mesh is refined, so on a thin electrode it can
    /// stop immediately and leave the whole fine grid as the bottom level. The Galerkin
    /// one always reaches a few dozen nodes, and pays an assembly proportional to the
    /// fine grid for it.
    /// </para>
    /// <para>
    /// Measured both ways on the same geometries, wall clock:
    /// </para>
    /// <list type="table">
    /// <item><description>
    /// two 1 mm slabs, 129 cubed: 45 cycles and 160 s rediscretised against 13 cycles
    /// and 13 s - <b>12x</b>, and the bottom level goes from 274,625 nodes to 27
    /// </description></item>
    /// <item><description>
    /// four rods, 65 cubed: 31 cycles and 5.2 s against 8 and 1.1 s - <b>4.6x</b>
    /// </description></item>
    /// <item><description>
    /// a 2 mm sphere, 65 cubed: 13 cycles and 1.1 s against 14 and 1.7 s -
    /// <b>0.64x</b>, a loss
    /// </description></item>
    /// </list>
    /// <para>
    /// The sphere is the case where the rediscretised hierarchy already worked, and
    /// there the twenty-seven point stencil and the assembly are pure overhead. What
    /// separates it from the others is the size of the bottom level it reaches - 4,913
    /// nodes against 35,937 and 274,625 - so that is what this asks about. The threshold
    /// is where the bottom relaxation stops being cheap, and it is a measured boundary
    /// rather than a derived one.
    /// </para>
    /// </remarks>
    private static bool WorthGalerkin(DirichletMask3D mask) =>
        UsesGalerkin(mask.Grid, mask.SmallestFeature, mask.InteriorFixedCount > 0);

    /// <summary>
    /// Whether a solve over this grid will build its coarse levels by Galerkin, decided
    /// from what the geometry declares rather than from a rasterized mask.
    /// </summary>
    /// <param name="grid">The finest grid.</param>
    /// <param name="smallestFeature">
    /// The smallest half-extent of any interior electrode, in meters; positive infinity
    /// when there is none. <see cref="DirichletMask3D.SmallestFeature"/> is set from the
    /// same quantity, the electrodes' characteristic sizes.
    /// </param>
    /// <param name="interiorElectrodes">
    /// Whether any electrode fixes a node away from the domain faces. A caller that has
    /// not rasterized cannot know this exactly and should pass <see langword="true"/>
    /// whenever the geometry has electrodes, which errs toward the Galerkin hierarchy.
    /// </param>
    /// <returns><see langword="true"/> when the solve will take the Galerkin hierarchy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>Public because what a solve costs depends on it, and the cost has to be known
    /// before the solve.</b> The two hierarchies hold different amounts at their peak - the
    /// Galerkin one assembles the fine operator as a stored stencil and forms the first
    /// coarse level from it, the rediscretized one does neither - so <c>einzel estimate</c>
    /// cannot say what memory a volume solve needs without knowing which it will take.
    /// Answering that with a second copy of this rule would be a second decision, and the
    /// two would part company the first time either was tuned.
    /// </para>
    /// <para>
    /// <see cref="Solve"/> calls this with the mask's own record of both quantities, so
    /// the answer here and the choice there are the same function of the same inputs.
    /// </para>
    /// </remarks>
    public static bool UsesGalerkin(Grid3D grid, double smallestFeature, bool interiorElectrodes)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var levels = RediscretizedLevels(grid, smallestFeature, interiorElectrodes);

        return (levels.Count > 0 ? levels[^1] : grid).NodeCount > RediscretisedBottomLimit;
    }

    /// <summary>
    /// The coarse grids the rediscretized hierarchy descends through, below the finest.
    /// </summary>
    /// <param name="grid">The finest grid.</param>
    /// <param name="smallestFeature">As for <see cref="UsesGalerkin"/>.</param>
    /// <param name="interiorElectrodes">As for <see cref="UsesGalerkin"/>.</param>
    /// <returns>Each level a V-cycle builds below the finest, in order; empty when it builds none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    /// <remarks>
    /// The descent <see cref="UsesGalerkin"/> decides from, and what a memory statement sums
    /// over: every level exists at once at the bottom of a V-cycle, and each is the size its
    /// grid really is - which is more than an eighth of the one above, by a node an axis, and
    /// much more on a thin axis.
    /// </remarks>
    public static IReadOnlyList<Grid3D> RediscretizedLevels(
        Grid3D grid, double smallestFeature, bool interiorElectrodes)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var levels = new List<Grid3D>();

        for (var at = grid; at.CanCoarsen && Representable(at.Coarsen(), smallestFeature, interiorElectrodes);)
        {
            at = at.Coarsen();
            levels.Add(at);
        }

        return levels;
    }

    /// <summary>
    /// How many nodes the rediscretised hierarchy may leave at the bottom before the
    /// Galerkin one is worth its assembly.
    /// </summary>
    /// <remarks>
    /// Twenty thousand: above it the bottom level is relaxed by up to four hundred
    /// sweeps over a grid that does not shrink when the mesh is refined, which is the
    /// cost multigrid exists to remove. Below it the bottom solve is a few million node
    /// updates and the cheaper hierarchy wins.
    /// </remarks>
    private const int RediscretisedBottomLimit = 20_000;

    /// <summary>
    /// One V-cycle whose finest level is the geometry and whose coarse levels are not.
    /// </summary>
    /// <remarks>
    /// <b>The finest level is untouched</b>: it smooths against the mask, with its cut
    /// cells, exactly as it always did - that is where the accuracy comes from and none
    /// of it is in question here. What changes is only what happens below, and the
    /// change is that the coarse problem is the same problem coarsened rather than a
    /// different one rediscretised.
    /// </remarks>
    private static void GalerkinCycle(
        ScalarField3D potential,
        ScalarField3D rightHandSide,
        DirichletMask3D mask,
        GalerkinHierarchy3D hierarchy,
        CycleWork work)
    {
        for (var sweep = 0; sweep < PreSmooth; sweep++)
        {
            Smooth(potential, rightHandSide, mask);
        }

        work.Sweeps += PreSmooth;

        var residual = new ScalarField3D(mask.Grid);

        Residual(potential, rightHandSide, mask, residual);

        var correction = hierarchy.Correct(residual, mask);

        GalerkinHierarchy3D.Apply(correction, potential, mask);

        for (var sweep = 0; sweep < PostSmooth; sweep++)
        {
            Smooth(potential, rightHandSide, mask);
        }

        work.Sweeps += PostSmooth;
    }

    private static void Cycle(
        ScalarField3D potential,
        ScalarField3D rightHandSide,
        DirichletMask3D mask,
        Func<Grid3D, DirichletMask3D>? coarsen,
        CycleWork work,
        int depth)
    {
        var grid = mask.Grid;

        for (var sweep = 0; sweep < PreSmooth; sweep++)
        {
            Smooth(potential, rightHandSide, mask);
        }

        work.Sweeps += PreSmooth;

        if (!grid.CanCoarsen || !Representable(grid.Coarsen(), mask))
        {
            work.Levels = Math.Max(work.Levels, depth);
            work.CoarsestNodes = grid.NodeCount;
            work.Sweeps += SolveCoarsest(potential, rightHandSide, mask);
            return;
        }

        var residual = new ScalarField3D(grid);
        Residual(potential, rightHandSide, mask, residual);

        var coarseGrid = grid.Coarsen();
        var coarseMask = coarsen?.Invoke(coarseGrid) ?? Project(mask, coarseGrid);

        var coarseResidual = Restrict(residual, coarseGrid, coarseMask);
        var correction = new ScalarField3D(coarseGrid);

        Cycle(
            correction,
            coarseResidual,
            coarseMask,
            coarsen is null ? null : g => coarsen(g),
            work,
            depth + 1);

        Prolong(correction, potential, mask);

        for (var sweep = 0; sweep < PostSmooth; sweep++)
        {
            Smooth(potential, rightHandSide, mask);
        }

        work.Sweeps += PostSmooth;
    }

    private static void Smooth(ScalarField3D potential, ScalarField3D rightHandSide, DirichletMask3D mask)
    {
        var grid = mask.Grid;
        var halfH2 = 0.5 * grid.SpacingX * grid.SpacingX;

        var aspectY = (grid.SpacingX / grid.SpacingY) * (grid.SpacingX / grid.SpacingY);
        var aspectZ = (grid.SpacingX / grid.SpacingZ) * (grid.SpacingX / grid.SpacingZ);

        // Red-black so a sweep does not depend on the order nodes are visited in,
        // which is what makes the smoother the same on one thread as on many.
        for (var colour = 0; colour < 2; colour++)
        {
            for (var k = 0; k < grid.CountZ; k++)
            {
                for (var j = 0; j < grid.CountY; j++)
                {
                    for (var i = 0; i < grid.CountX; i++)
                    {
                        if (((i + j + k) & 1) != colour || mask.IsFixed(i, j, k))
                        {
                            continue;
                        }

                        Stencil(potential, mask, i, j, k, aspectY, aspectZ, out var sum, out var weight);

                        potential[i, j, k] = (sum - (halfH2 * rightHandSide[i, j, k])) / weight;
                    }
                }
            }
        }
    }

    private static double Residual(
        ScalarField3D potential,
        ScalarField3D rightHandSide,
        DirichletMask3D mask,
        ScalarField3D residual)
    {
        var grid = mask.Grid;
        var halfH2 = 0.5 * grid.SpacingX * grid.SpacingX;

        var aspectY = (grid.SpacingX / grid.SpacingY) * (grid.SpacingX / grid.SpacingY);
        var aspectZ = (grid.SpacingX / grid.SpacingZ) * (grid.SpacingX / grid.SpacingZ);

        var sum = 0.0;
        var count = 0;

        for (var k = 0; k < grid.CountZ; k++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    if (mask.IsFixed(i, j, k))
                    {
                        residual[i, j, k] = 0.0;
                        continue;
                    }

                    Stencil(potential, mask, i, j, k, aspectY, aspectZ, out var neighbours, out var weight);

                    // f - L(phi), not the other way round. The coarse problem then
                    // solves L e = r and the correction is added, which is the
                    // standard pairing; flip it and the V-cycle subtracts its own
                    // correction and degenerates into a plain smoother.
                    var laplacian = (neighbours - (weight * potential[i, j, k])) / halfH2;
                    var value = rightHandSide[i, j, k] - laplacian;

                    residual[i, j, k] = value;

                    sum += value * value;
                    count++;
                }
            }
        }

        // Root mean square, not the maximum. With sub-cell surfaces the maximum is
        // spiky: one node whose arm is a thousandth of a cell carries an enormous
        // coefficient and its residual dominates the norm, so the norm can rise
        // while the solution improves everywhere. Judging convergence on that
        // stopped a perfectly good V-cycle after two cycles and left a field wrong
        // by a factor of two - the operator was right the whole time and the
        // measurement of it was not.
        return count > 0 ? Math.Sqrt(sum / count) : 0.0;
    }

    /// <summary>
    /// The Shortley-Weller stencil on six arms, in cell units.
    /// </summary>
    /// <remarks>
    /// Each arm reaches either the neighbouring node or a conductor surface part of
    /// the way there, and the coefficients vary continuously with where that surface
    /// sits - which is what makes a shape derivative mean anything and what recovers
    /// second order at a curved boundary. With nothing cut every fraction is one and
    /// this is the ordinary seven-point formula.
    /// </remarks>
    private static void Stencil(
        ScalarField3D potential,
        DirichletMask3D mask,
        int i,
        int j,
        int k,
        double aspectY,
        double aspectZ,
        out double sum,
        out double weight)
    {
        var cuts = mask.Cuts;

        Arm(potential, mask, cuts, i, j, k, 1, 0, 0, Arm3D.East, out var east, out var fEast);
        Arm(potential, mask, cuts, i, j, k, -1, 0, 0, Arm3D.West, out var west, out var fWest);
        Arm(potential, mask, cuts, i, j, k, 0, 1, 0, Arm3D.North, out var north, out var fNorth);
        Arm(potential, mask, cuts, i, j, k, 0, -1, 0, Arm3D.South, out var south, out var fSouth);
        Arm(potential, mask, cuts, i, j, k, 0, 0, 1, Arm3D.Up, out var up, out var fUp);
        Arm(potential, mask, cuts, i, j, k, 0, 0, -1, Arm3D.Down, out var down, out var fDown);

        // Each axis is in cell units of its own spacing, so the y and z halves are
        // scaled into x units. Those factors are exactly one on a cubic grid, and
        // multiplying by one is exact, so an isotropic solve is bit-unchanged.
        var alongX = 1.0 / (fWest + fEast);
        var alongY = aspectY / (fSouth + fNorth);
        var alongZ = aspectZ / (fDown + fUp);

        sum = (alongX * ((west / fWest) + (east / fEast)))
            + (alongY * ((south / fSouth) + (north / fNorth)))
            + (alongZ * ((down / fDown) + (up / fUp)));

        weight = (alongX * ((1.0 / fWest) + (1.0 / fEast)))
            + (alongY * ((1.0 / fSouth) + (1.0 / fNorth)))
            + (alongZ * ((1.0 / fDown) + (1.0 / fUp)));
    }

    private static void Arm(
        ScalarField3D potential,
        DirichletMask3D mask,
        CutLinks3D? cuts,
        int i,
        int j,
        int k,
        int di,
        int dj,
        int dk,
        Arm3D arm,
        out double value,
        out double fraction)
    {
        if (cuts is not null)
        {
            var cut = cuts.Fraction(i, j, k, arm, out var surface);

            if (cut < 1.0)
            {
                value = surface;
                fraction = cut;
                return;
            }
        }

        var grid = mask.Grid;
        var ni = i + di;
        var nj = j + dj;
        var nk = k + dk;

        if (ni < 0 || nj < 0 || nk < 0 || ni >= grid.CountX || nj >= grid.CountY || nk >= grid.CountZ)
        {
            // Off the grid. A Neumann face is a mirror, so the ghost equals its
            // reflection inside; a Dirichlet face that no node holds falls back to
            // zero, which the geometry builder avoids by pinning the face itself.
            var neumann = di > 0 ? mask.UpperX == EdgeCondition.Neumann
                : di < 0 ? mask.LowerX == EdgeCondition.Neumann
                : dj > 0 ? mask.UpperY == EdgeCondition.Neumann
                : dj < 0 ? mask.LowerY == EdgeCondition.Neumann
                : dk > 0 ? mask.UpperZ == EdgeCondition.Neumann
                : mask.LowerZ == EdgeCondition.Neumann;

            value = neumann ? potential[i - di, j - dj, k - dk] : 0.0;
            fraction = 1.0;
            return;
        }

        value = potential[ni, nj, nk];
        fraction = 1.0;
    }

    /// <summary>Projects a mask onto a coarser grid, when geometry cannot be re-read.</summary>
    private static DirichletMask3D Project(DirichletMask3D fine, Grid3D coarse)
    {
        var projected = new DirichletMask3D(coarse)
        {
            LowerX = fine.LowerX,
            UpperX = fine.UpperX,
            LowerY = fine.LowerY,
            UpperY = fine.UpperY,
            LowerZ = fine.LowerZ,
            UpperZ = fine.UpperZ,
        };

        for (var k = 0; k < coarse.CountZ; k++)
        {
            for (var j = 0; j < coarse.CountY; j++)
            {
                for (var i = 0; i < coarse.CountX; i++)
                {
                    if (fine.IsFixed(2 * i, 2 * j, 2 * k))
                    {
                        projected.Fix(i, j, k, fine.Value(2 * i, 2 * j, 2 * k));
                    }
                }
            }
        }

        return projected;
    }

    /// <summary>Full-weighting restriction of a residual onto the coarse grid.</summary>
    private static ScalarField3D Restrict(ScalarField3D residual, Grid3D coarse, DirichletMask3D coarseMask)
    {
        var fine = residual.Grid;
        var result = new ScalarField3D(coarse);

        for (var k = 0; k < coarse.CountZ; k++)
        {
            for (var j = 0; j < coarse.CountY; j++)
            {
                for (var i = 0; i < coarse.CountX; i++)
                {
                    if (coarseMask.IsFixed(i, j, k))
                    {
                        continue;
                    }

                    var fi = 2 * i;
                    var fj = 2 * j;
                    var fk = 2 * k;

                    var total = 0.0;
                    var weight = 0.0;

                    for (var dk = -1; dk <= 1; dk++)
                    {
                        for (var dj = -1; dj <= 1; dj++)
                        {
                            for (var di = -1; di <= 1; di++)
                            {
                                var si = fi + di;
                                var sj = fj + dj;
                                var sk = fk + dk;

                                if (si < 0 || sj < 0 || sk < 0
                                    || si >= fine.CountX || sj >= fine.CountY || sk >= fine.CountZ)
                                {
                                    continue;
                                }

                                // The separable 1-2-1 kernel, which is the adjoint
                                // of trilinear prolongation - the pairing that makes
                                // a V-cycle a genuine two-grid correction rather
                                // than two smoothers that happen to share a grid.
                                var w = Kernel(di) * Kernel(dj) * Kernel(dk);

                                total += w * residual[si, sj, sk];
                                weight += w;
                            }
                        }
                    }

                    result[i, j, k] = weight > 0.0 ? total / weight : 0.0;
                }
            }
        }

        return result;
    }

    private static double Kernel(int offset) => offset == 0 ? 2.0 : 1.0;

    /// <summary>Adds a coarse correction back onto the fine field, trilinearly.</summary>
    private static void Prolong(ScalarField3D correction, ScalarField3D potential, DirichletMask3D mask)
    {
        var fine = mask.Grid;
        var coarse = correction.Grid;

        for (var k = 0; k < fine.CountZ; k++)
        {
            for (var j = 0; j < fine.CountY; j++)
            {
                for (var i = 0; i < fine.CountX; i++)
                {
                    if (mask.IsFixed(i, j, k))
                    {
                        continue;
                    }

                    potential[i, j, k] += Sample(correction, coarse, i, j, k);
                }
            }
        }
    }

    private static double Sample(ScalarField3D correction, Grid3D coarse, int i, int j, int k)
    {
        var ci = i / 2;
        var cj = j / 2;
        var ck = k / 2;

        var oddI = (i & 1) == 1;
        var oddJ = (j & 1) == 1;
        var oddK = (k & 1) == 1;

        var total = 0.0;

        for (var dk = 0; dk <= (oddK ? 1 : 0); dk++)
        {
            for (var dj = 0; dj <= (oddJ ? 1 : 0); dj++)
            {
                for (var di = 0; di <= (oddI ? 1 : 0); di++)
                {
                    var si = Math.Min(ci + di, coarse.CountX - 1);
                    var sj = Math.Min(cj + dj, coarse.CountY - 1);
                    var sk = Math.Min(ck + dk, coarse.CountZ - 1);

                    total += correction[si, sj, sk];
                }
            }
        }

        var count = (oddI ? 2 : 1) * (oddJ ? 2 : 1) * (oddK ? 2 : 1);

        return total / count;
    }
}
