namespace Einzel.Fields.Solved;

/// <summary>What a solve achieved.</summary>
/// <param name="Converged">Whether the residual reached the requested tolerance.</param>
/// <param name="Cycles">V-cycles performed.</param>
/// <param name="InitialResidual">Residual norm before the first cycle.</param>
/// <param name="FinalResidual">Residual norm after the last cycle.</param>
/// <param name="ConvergenceFactor">
/// Residual reduction per cycle, averaged geometrically. A healthy geometric
/// multigrid sits near 0.1; a value approaching 1 means the cycle is not working
/// and the answer is being reached, if at all, by the smoother alone.
/// </param>
public sealed record SolveReport(
    bool Converged,
    int Cycles,
    double InitialResidual,
    double FinalResidual,
    double ConvergenceFactor)
{
    /// <summary>How many coarse levels the V-cycle actually descended.</summary>
    /// <remarks>
    /// <para>
    /// <b>Without this a cycle count means different amounts of work in different
    /// runs, and the numbers get compared anyway.</b> Coarsening stops once a coarse
    /// cell would exceed the smallest electrode dimension, and that is a
    /// <em>physical</em> size - so it does not move when the mesh is refined. A
    /// geometry with a thin electrode can descend zero levels, in which case the whole
    /// solve is relaxation on the finest grid and a "cycle" contains hundreds of
    /// sweeps rather than four.
    /// </para>
    /// <para>
    /// Measured: two 1 mm slabs in a 32 mm box descend <b>0 levels at a 0.5 mm cell
    /// and 1 at 0.25 mm</b>, while the same box with no interior electrode at all
    /// descends 5 and 6. So the grid-independent cycle count this solver is documented
    /// as having is a property of the boundary-only case, and every device geometry in
    /// the library is running a one- to three-level method.
    /// </para>
    /// </remarks>
    public int Levels { get; init; }

    /// <summary>Smoothing sweeps over every level, summed across all cycles.</summary>
    /// <remarks>
    /// The unit of work a cycle count is usually taken to stand for, reported because
    /// here it does not: at zero levels a cycle is up to four hundred sweeps and at
    /// several it is a handful per level. Two runs whose cycle counts differ by eight
    /// can differ by a factor of a hundred in sweeps, in the other direction.
    /// </remarks>
    public long Sweeps { get; init; }

    /// <summary>Nodes on the coarsest level the cycle reached.</summary>
    /// <remarks>
    /// The quantity that says whether this is multigrid. A true V-cycle bottoms out at
    /// a handful of nodes whatever the fine mesh; here the bottom level is pinned to a
    /// physical cell size, so its node count is <em>fixed in absolute terms</em> and
    /// refining the fine grid does not make the bottom solve any cheaper. That is
    /// exactly the cost multigrid exists to remove.
    /// </remarks>
    public long CoarsestNodes { get; init; }

    /// <summary>Whether the coarse levels came from the fine operator.</summary>
    /// <remarks>
    /// Neither hierarchy dominates - the rediscretised one is cheap to build and can
    /// stop coarsening immediately on a thin electrode, and the Galerkin one always
    /// reaches a few dozen nodes but pays an assembly for it. The solver picks from the
    /// geometry, so this says which it picked rather than leaving a reader to infer it
    /// from a level count.
    /// </remarks>
    public bool Galerkin { get; init; }

    /// <summary>What the geometry builder found wrong with the mesh before solving it.</summary>
    /// <remarks>
    /// <para>
    /// Empty from the solver itself, which sees a mask and not the conductors that made it.
    /// The builders put here what only the geometry can say - so far, nodes sitting on a
    /// face two disagreeing conductors share (<see cref="SharedFaceNodes"/>) - and
    /// <c>FieldAssembly.BuildReported</c> carries it onto every result (GRD-2).
    /// </para>
    /// <para>
    /// On the report because the report is the one thing every consumer of a solve already
    /// reads: a warning returned beside it would need every caller taught to look, and the
    /// callers that were not taught are exactly where this project has lost evidence before.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Core.Results.ValidityWarning> Warnings { get; init; } = [];
}

/// <summary>
/// Geometric multigrid for Laplace's equation on a uniform Cartesian grid.
/// </summary>
/// <remarks>
/// <para>
/// The five-point stencil, red-black Gauss-Seidel smoothing, full-weighting
/// restriction, bilinear prolongation, V-cycles. Spec section 10 chooses
/// finite-difference multigrid for Phase 1 because it is "straightforward to
/// implement correctly, clean to parallelize and GPU-offload, naturally
/// accommodating of SYM-1".
/// </para>
/// <para>
/// Why multigrid rather than something simpler: Gauss-Seidel alone removes error
/// at the grid scale quickly and error at the domain scale barely at all, so its
/// iteration count grows with the square of the node count along an edge.
/// Multigrid carries the long-wavelength error to a coarse grid where it is
/// short-wavelength again, giving a residual reduction per cycle that does not
/// depend on the grid size. PERF-1 budgets thirty minutes for a full basis
/// campaign, and that budget is only reachable with a solver whose cost is linear
/// in the node count.
/// </para>
/// <para>
/// The correction scheme is used rather than full approximation storage: the
/// coarse grids solve for the error, so Dirichlet nodes carry a correction of
/// zero and electrode potentials never need to be represented on a coarse grid at
/// all.
/// </para>
/// </remarks>
public static class PoissonSolver2D
{
    /// <summary>
    /// How much of the fixed set a coarsening may lose before it is refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A V-cycle is only useful while the coarse grid still poses the same
    /// problem. Interior electrodes — a rod, an aperture — occupy a fixed physical
    /// size, so each coarsening halves how many nodes represent them, and past a
    /// few levels they stop being represented at all. The coarse grid then
    /// computes a correction for a domain with no electrodes in it: not a worse
    /// approximation but a different one, and prolonging it back drives the
    /// iteration apart. Four discs in a box reached 1e134 V that way.
    /// </para>
    /// <para>
    /// The rule applies only to interior electrodes, and only they need it. A
    /// domain pinned on its edges alone coarsens all the way down safely — the
    /// manufactured-solution tests hold 8, 7, 7, 7 cycles from 32 to 256 intervals
    /// doing exactly that — so limiting every geometry would cost those cases
    /// their multigrid for nothing. Note that a total-node test does not work
    /// here: a disc loses three quarters of its nodes per level, which is
    /// precisely the rate healthy coarsening produces, so the ratio stays flat
    /// until the rod disappears and then it is too late.
    /// </para>
    /// </remarks>
    private const int MinimumInteriorFixedNodes = 128;

    /// <summary>
    /// What a solve did, as opposed to how many cycles it took to do it.
    /// </summary>
    /// <remarks>
    /// Threaded through the recursion rather than derived afterwards, because how far
    /// a cycle descends depends on the geometry at every level and working it out from
    /// the outside would mean a second implementation of the same decision.
    /// </remarks>
    private sealed class CycleWork
    {
        public int Levels { get; set; }

        public long Sweeps { get; set; }

        public long CoarsestNodes { get; set; }
    }

    private const int PreSmooth = 2;
    private const int PostSmooth = 2;
    private const int CoarseSmooth = 60;

    /// <summary>Solves Laplace's equation subject to a Dirichlet mask.</summary>
    /// <param name="mask">Fixed nodes and edge conditions.</param>
    /// <param name="tolerance">
    /// Relative residual to reach, against the residual of the initial guess.
    /// </param>
    /// <param name="maximumCycles">Ceiling on V-cycles.</param>
    /// <param name="initialGuess">
    /// Optional starting field. A previous solve on the same geometry makes a good
    /// one; otherwise the fixed potentials are smoothed into a zero interior.
    /// </param>
    /// <param name="source">
    /// The right-hand side of <c>grad^2 phi = source</c>, or null for Laplace. A
    /// charge density enters as <c>-rho / epsilon0</c>, which is the sign the
    /// residual convention already fixes.
    /// </param>
    /// <param name="coarsen">
    /// Optional factory that rebuilds the mask for a coarser grid from the
    /// geometry itself. Strongly preferred over the default, which projects the
    /// fine mask down and loses sub-cell boundaries as it goes: rebuilding keeps
    /// an electrode present, with its true surface position, at every level.
    /// </param>
    /// <returns>The solved potential and a report on how it was reached.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mask"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The tolerance or cycle ceiling is not positive.</exception>
    public static (ScalarField2D Potential, SolveReport Report) Solve(
        DirichletMask mask,
        double tolerance = 1e-10,
        int maximumCycles = 200,
        ScalarField2D? initialGuess = null,
        Func<Grid2D, DirichletMask>? coarsen = null,
        ScalarField2D? source = null)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tolerance);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCycles);

        var grid = mask.Grid;
        var potential = initialGuess?.Clone() ?? new ScalarField2D(grid);

        // Stamped onto the field so the interpolant knows which of its edges are
        // mirrors. Without this the solve is right and the sampling is not.
        potential.LeftEdge = mask.LeftEdge;
        potential.RightEdge = mask.RightEdge;
        potential.BottomEdge = mask.BottomEdge;
        potential.TopEdge = mask.TopEdge;
        mask.ApplyTo(potential);

        // Laplace when nothing is given, Poisson when something is. The whole cycle
        // already carries a right-hand side and has only ever been handed zeros -
        // the smoother subtracts it, the residual is defined against it, and the
        // coarse levels receive the restricted residual, which is what they need
        // whatever the fine level's source is. So a charge density costs one
        // argument and no numerics.
        //
        // The convention is the one the residual already fixes: this solves
        // grad^2 phi = source, so a charge density enters as -rho/epsilon0.
        var rightHandSide = source ?? new ScalarField2D(grid);
        var residual = new ScalarField2D(grid);

        if (source is not null && !ReferenceEquals(source.Grid, grid)
            && (source.Grid.CountX != grid.CountX || source.Grid.CountY != grid.CountY))
        {
            throw new ArgumentException(
                "the source is on a different grid from the mask, so its values do not "
                + "correspond to the nodes being solved",
                nameof(source));
        }

        var initial = Residual(potential, rightHandSide, mask, residual);

        // A geometry with no free nodes, or one already solved, is not an error.
        if (initial == 0.0)
        {
            return (potential, new SolveReport(true, 0, 0.0, 0.0, 0.0)
            {
                CoarsestNodes = grid.NodeCount,
            });
        }

        var current = initial;
        var cycles = 0;
        var work = new CycleWork();

        for (; cycles < maximumCycles; cycles++)
        {
            VCycle(potential, rightHandSide, mask, coarsen, work, 0);
            current = Residual(potential, rightHandSide, mask, residual);

            if (current <= tolerance * initial)
            {
                cycles++;
                break;
            }
        }

        var factor = cycles > 0 && initial > 0.0
            ? Math.Pow(current / initial, 1.0 / cycles)
            : 0.0;

        return (
            potential,
            new SolveReport(current <= tolerance * initial, cycles, initial, current, factor)
            {
                Levels = work.Levels,
                Sweeps = work.Sweeps,
                CoarsestNodes = work.CoarsestNodes,
            });
    }

    private static void VCycle(
        ScalarField2D potential,
        ScalarField2D rightHandSide,
        DirichletMask mask,
        Func<Grid2D, DirichletMask>? coarsen,
        CycleWork work,
        int depth)
    {
        var grid = potential.Grid;

        if (!grid.CanCoarsen)
        {
            Smooth(potential, rightHandSide, mask, CoarseSmooth);

            work.Levels = Math.Max(work.Levels, depth);
            work.CoarsestNodes = grid.NodeCount;
            work.Sweeps += CoarseSmooth;

            return;
        }

        // Rebuilding from geometry keeps the surface where it is at every level;
        // projecting the fine mask down is what let electrodes dissolve.
        var coarseMask = coarsen is not null ? coarsen(grid.Coarsen()) : mask.Coarsen();

        // Refuse a coarsening that would dissolve interior electrodes rather than
        // represent them more coarsely. A mask rebuilt from the geometry keeps its
        // surfaces wherever they are, at any spacing, so it needs only to still
        // have them; a mask projected down from the fine one loses a quarter of an
        // electrode's nodes per level and needs the far more cautious floor.
        var minimum = coarsen is not null ? 1 : MinimumInteriorFixedNodes;

        if (mask.InteriorGeometryCount > 0 && coarseMask.InteriorGeometryCount < minimum)
        {
            Smooth(potential, rightHandSide, mask, CoarseSmooth);

            work.Levels = Math.Max(work.Levels, depth);
            work.CoarsestNodes = grid.NodeCount;
            work.Sweeps += CoarseSmooth;

            return;
        }

        Smooth(potential, rightHandSide, mask, PreSmooth);

        work.Sweeps += PreSmooth;

        Prolong(Correction(potential, rightHandSide, mask, coarseMask, coarsen, work, depth), potential, mask);
        Smooth(potential, rightHandSide, mask, PostSmooth);

        work.Sweeps += PostSmooth;
    }

    /// <summary>The coarse-grid correction for one level of a V-cycle, in a frame of its own.</summary>
    /// <remarks>
    /// <para>
    /// Split out of <see cref="VCycle"/> so that the coarse level's right-hand side is a local of
    /// a frame that has returned by the time this level prolongs and smooths, whichever tier of
    /// the JIT is running.
    /// </para>
    /// <para>
    /// <b>It is not what fixed the peak, and that was measured.</b> With this frame inlined
    /// back and <see cref="RestrictedResidual"/> kept, a first-tier plane build reads 135.57
    /// bytes a node, the same as with both frames; with <see cref="RestrictedResidual"/> inlined
    /// and this frame kept it reads 143.57. The coarse arrays this holds are alive through the
    /// recursion either way, because the recursion works on them; the fine residual was the one
    /// array that had no business there. <c>FirstTierMemoryTests</c> is the guard.
    /// </para>
    /// </remarks>
    private static ScalarField2D Correction(
        ScalarField2D potential,
        ScalarField2D rightHandSide,
        DirichletMask mask,
        DirichletMask coarseMask,
        Func<Grid2D, DirichletMask>? coarsen,
        CycleWork work,
        int depth)
    {
        var coarseRhs = RestrictedResidual(potential, rightHandSide, mask, coarseMask);
        var coarseCorrection = new ScalarField2D(coarseMask.Grid);

        // The coarse problem solves for the error, which is zero on every
        // conductor, so its cut links carry zero potential rather than the
        // electrode's.
        coarseMask.ZeroCutPotentials();

        VCycle(coarseCorrection, coarseRhs, coarseMask, coarsen, work, depth + 1);

        return coarseCorrection;
    }

    /// <summary>
    /// The fine level's residual, restricted onto the coarse grid, with the fine residual
    /// itself dead by the time this returns.
    /// </summary>
    /// <remarks>
    /// <b>A frame of its own, because the residual must not live across the recursion.</b>
    /// Written inline in <see cref="VCycle"/>, the fine residual was a local of the frame that
    /// goes on to recurse into every coarser level, and whether it stayed reachable through
    /// that recursion was the JIT's choice rather than the code's: optimised code ends it at
    /// its last use, while first-tier code - what every method runs as until it has been
    /// called often enough to be recompiled, and what Debug always runs - keeps every local
    /// alive to the end of its method. So the deepest instant of a cycle held one more array
    /// of the fine grid's size than the memory statement charges, for exactly as long as the
    /// solver had not yet been promoted: 130 KiB on a 129 x 129 self-field, read by
    /// <c>SelfFieldMemoryTests</c> as 67 bytes a node against 62 on a slow runner, and
    /// reproduced every time with call counting switched off. Here the array is a local of a
    /// frame that has returned before the recursion starts, so no tier can keep it.
    /// <c>FirstTierMemoryTests</c> measures a solve in a child process with promotion switched
    /// off, so putting this back inline fails on every machine rather than on a slow one.
    /// </remarks>
    private static ScalarField2D RestrictedResidual(
        ScalarField2D potential, ScalarField2D rightHandSide, DirichletMask mask, DirichletMask coarseMask)
    {
        var residual = new ScalarField2D(potential.Grid);
        Residual(potential, rightHandSide, mask, residual);

        return Restrict(residual, coarseMask);
    }

    /// <summary>
    /// Red-black Gauss-Seidel. Red and black sweeps are each independent within
    /// themselves, which is why this smoother and not lexicographic ordering:
    /// spec section 6 puts a SIMD and GPU dispatch layer under the engine, and a
    /// smoother that cannot be parallelised would have to be replaced later.
    /// </summary>
    private static void Smooth(
        ScalarField2D potential, ScalarField2D rightHandSide, DirichletMask mask, int sweeps)
    {
        var grid = potential.Grid;

        // The stencil is carried in cell units, so its coefficients stay of order
        // one whatever the spacing and whatever a cut fraction does to them, and
        // the mesh enters exactly once, here. Folding one over h squared into the
        // stencil instead would scale every term by millions while leaving the
        // quantity actually wanted - the difference between them - unchanged, and
        // spend the precision on nothing. In cell units the uniform case reduces
        // to the old five-point arithmetic exactly, halves and quarters being
        // exact in binary, so the change carries no rounding of its own.
        var halfH2 = 0.5 * grid.SpacingX * grid.SpacingX;
        var geometry = GeometryOf(mask);

        for (var sweep = 0; sweep < sweeps; sweep++)
        {
            for (var colour = 0; colour < 2; colour++)
            {
                for (var j = 0; j < grid.CountY; j++)
                {
                    for (var i = (j + colour) % 2; i < grid.CountX; i += 2)
                    {
                        if (mask.IsFixed(i, j))
                        {
                            continue;
                        }

                        Stencil(potential, mask, i, j, in geometry, out var sum, out var weight);
                        potential[i, j] = (sum - (halfH2 * rightHandSide[i, j])) / weight;
                    }
                }
            }
        }
    }

    /// <summary>
    /// The Shortley-Weller stencil: a second-derivative approximation on unequal
    /// spacings, so a conductor surface may sit between nodes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For spacings h- and h+ either side of a node, the second derivative is
    /// 2/(h- + h+) [ f-/h- + f+/h+ - f0 (1/h- + 1/h+) ]. With every spacing equal
    /// to h this is exactly the familiar five-point formula, so the uniform case
    /// costs nothing and is not a separate code path.
    /// </para>
    /// <para>
    /// What it buys is that the coefficients vary *continuously* with the surface
    /// position. A rasterised boundary snaps to the nearest node, so the operator
    /// is a staircase function of where an electrode sits; here it moves smoothly,
    /// which is what makes a shape derivative mean anything and what recovers
    /// second-order accuracy at a curved boundary.
    /// </para>
    /// <para>
    /// A Neumann edge is a mirror plane, so the ghost node outside it equals its
    /// reflection inside, which appears as the interior neighbour counted twice at
    /// the same spacing.
    /// </para>
    /// </remarks>
    private static void Stencil(
        ScalarField2D potential,
        DirichletMask mask,
        int i,
        int j,
        in StencilGeometry geometry,
        out double sum,
        out double weight)
    {
        var cuts = mask.Cuts;

        Arm(potential, mask, cuts, i, j, 1, 0, StencilDirection.East, out var east, out var fEast);
        Arm(potential, mask, cuts, i, j, -1, 0, StencilDirection.West, out var west, out var fWest);
        Arm(potential, mask, cuts, i, j, 0, 1, StencilDirection.North, out var north, out var fNorth);
        Arm(potential, mask, cuts, i, j, 0, -1, StencilDirection.South, out var south, out var fSouth);

        // Both halves are in cell units of their own axis, so the y half is scaled
        // by (hx/hy) squared to bring it into the x units the caller works in.
        // That factor is exactly one on a square grid, and multiplying by one is
        // exact, so nothing about an isotropic solve changes by a single bit.
        var alongX = 1.0 / (fWest + fEast);

        sum = alongX * ((west / fWest) + (east / fEast));
        weight = alongX * ((1.0 / fWest) + (1.0 / fEast));

        if (geometry.Cylindrical)
        {
            Radial(
                in geometry, j, south, north, fSouth, fNorth, out var radialSum, out var radialWeight);

            sum += radialSum;
            weight += radialWeight;
            return;
        }

        var alongY = geometry.AspectSquared / (fSouth + fNorth);

        sum += alongY * ((south / fSouth) + (north / fNorth));
        weight += alongY * ((1.0 / fSouth) + (1.0 / fNorth));
    }

    /// <summary>
    /// The radial half of the axisymmetric operator, in the same units as the
    /// axial half.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In cylindrical coordinates the radial part is (1/r) d/dr (r dphi/dr) rather
    /// than d2phi/dr2. Written in conservative form - the flux through the outer
    /// face of a ring minus the flux through its inner face, divided by the ring's
    /// own volume - because that is what makes the discrete operator conserve
    /// exactly what the continuous one does, and because the alternative of
    /// discretising the 1/r term directly is unstable near the axis.
    /// </para>
    /// <para>
    /// Face radii are measured in cells: a node at radius rho cells with arms
    /// reaching fSouth and fNorth cells has faces at rho - fSouth/2 and
    /// rho + fNorth/2. The ring's volume measure is the difference of their
    /// squares, which is where the whole r-dependence lives.
    /// </para>
    /// <para>
    /// On the axis the inner face has zero area, so no flux crosses it and the ring
    /// is a disc. That limit gives 4(phi_1 - phi_0)/h^2 for a uniform arm - twice
    /// what a mirrored plane stencil gives, which is the factor a solve gets wrong
    /// if it treats the axis as an ordinary symmetry plane.
    /// </para>
    /// </remarks>
    private static void Radial(
        in StencilGeometry geometry,
        int j,
        double south,
        double north,
        double fSouth,
        double fNorth,
        out double sum,
        out double weight)
    {
        var rho = geometry.AxisOffsetCells + j;

        // On the axis, to within a rounding of the offset. The inner face has no
        // area, so the south arm carries no flux however far away it is.
        if (rho <= AxisTolerance)
        {
            var scale = 2.0 * geometry.AspectSquared / (fNorth * fNorth);

            sum = scale * north;
            weight = scale;
            return;
        }

        var outer = rho + (fNorth / 2.0);
        var inner = rho - (fSouth / 2.0);

        // The ring's volume measure, up to the constant the whole stencil shares.
        var volume = (outer * outer) - (inner * inner);

        var scaleR = geometry.AspectSquared / volume;

        sum = scaleR * (((outer * north) / fNorth) + ((inner * south) / fSouth));
        weight = scaleR * ((outer / fNorth) + (inner / fSouth));
    }

    /// <summary>
    /// How close to the axis a node must be to be treated as on it, in cells.
    /// </summary>
    /// <remarks>
    /// A grid whose first row sits exactly on the axis puts rho at an exact zero, so
    /// this is a guard against an offset that arrived through arithmetic rather than
    /// a tolerance on physics. A node a thousandth of a cell off the axis is on it.
    /// </remarks>
    private const double AxisTolerance = 1e-9;

    /// <summary>What the stencil needs to know about the grid it is running on.</summary>
    /// <param name="AspectSquared">The x spacing over the y spacing, squared.</param>
    /// <param name="Cylindrical">Whether the radial half carries the r weighting.</param>
    /// <param name="AxisOffsetCells">
    /// Where the grid's first row sits relative to the axis, in cells, so that the
    /// radius of row j is this plus j. Zero for a grid that starts on the axis.
    /// </param>
    private readonly record struct StencilGeometry(
        double AspectSquared, bool Cylindrical, double AxisOffsetCells);

    private static StencilGeometry GeometryOf(DirichletMask mask)
    {
        var grid = mask.Grid;
        var cylindrical = mask.Symmetry == Core.Model.SolveSymmetry.Cylindrical;

        return new StencilGeometry(
            grid.AspectSquared,
            cylindrical,
            cylindrical ? grid.OriginY / grid.SpacingY : 0.0);
    }

    /// <summary>
    /// One arm of the stencil: the value it reaches, and how far away that is as a
    /// fraction of a cell.
    /// </summary>
    private static void Arm(
        ScalarField2D potential,
        DirichletMask mask,
        CutLinks? cuts,
        int i,
        int j,
        int di,
        int dj,
        StencilDirection direction,
        out double value,
        out double fraction)
    {
        var grid = potential.Grid;
        var ni = i + di;
        var nj = j + dj;

        if (ni < 0 || nj < 0 || ni >= grid.CountX || nj >= grid.CountY)
        {
            // Off the grid. A Neumann edge reflects; a Dirichlet edge that no node
            // holds falls back to zero a full cell away, which the geometry builder
            // avoids by pinning the edge itself.
            var neumann = di > 0 ? mask.RightEdge == EdgeCondition.Neumann
                : di < 0 ? mask.LeftEdge == EdgeCondition.Neumann
                : dj > 0 ? mask.TopEdge == EdgeCondition.Neumann
                : mask.BottomEdge == EdgeCondition.Neumann;

            value = neumann ? potential[i - di, j - dj] : 0.0;
            fraction = 1.0;
            return;
        }

        fraction = cuts?.Fraction(i, j, direction) ?? 1.0;

        // A cut surface is nearer than the neighbouring node, so it is what the
        // arm reaches.
        value = fraction < 1.0 ? cuts!.Potential(i, j, direction) : potential[ni, nj];
    }

    /// <summary>Computes the residual and returns its root-mean-square norm.</summary>
    private static double Residual(
        ScalarField2D potential, ScalarField2D rightHandSide, DirichletMask mask, ScalarField2D residual)
    {
        var grid = potential.Grid;
        var inverseHalfH2 = 2.0 / (grid.SpacingX * grid.SpacingX);
        var geometry = GeometryOf(mask);
        var sum = 0.0;
        var count = 0;

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                if (mask.IsFixed(i, j))
                {
                    residual[i, j] = 0.0;
                    continue;
                }

                Stencil(potential, mask, i, j, in geometry, out var neighbours, out var weight);
                var laplacian = (neighbours - (weight * potential[i, j])) * inverseHalfH2;
                var value = rightHandSide[i, j] - laplacian;

                residual[i, j] = value;
                sum += value * value;
                count++;
            }
        }

        return count == 0 ? 0.0 : Math.Sqrt(sum / count);
    }

    /// <summary>Full-weighting restriction onto the coarse grid.</summary>
    private static ScalarField2D Restrict(ScalarField2D fine, DirichletMask coarseMask)
    {
        var coarseGrid = coarseMask.Grid;
        var coarse = new ScalarField2D(coarseGrid);
        var fineGrid = fine.Grid;

        for (var j = 0; j < coarseGrid.CountY; j++)
        {
            for (var i = 0; i < coarseGrid.CountX; i++)
            {
                if (coarseMask.IsFixed(i, j))
                {
                    continue;
                }

                var fi = i * 2;
                var fj = j * 2;

                // Full weighting: 1/4 centre, 1/8 each edge neighbour, 1/16 each
                // diagonal. Simple injection would alias high-frequency residual
                // onto the coarse grid and stall the cycle.
                var value = 0.25 * fine[fi, fj];

                value += 0.125 * (Sample(fine, fi - 1, fj) + Sample(fine, fi + 1, fj)
                    + Sample(fine, fi, fj - 1) + Sample(fine, fi, fj + 1));

                value += 0.0625 * (Sample(fine, fi - 1, fj - 1) + Sample(fine, fi + 1, fj - 1)
                    + Sample(fine, fi - 1, fj + 1) + Sample(fine, fi + 1, fj + 1));

                coarse[i, j] = value;
            }
        }

        return coarse;

        static double Sample(ScalarField2D field, int i, int j) =>
            i < 0 || j < 0 || i >= field.Grid.CountX || j >= field.Grid.CountY ? 0.0 : field[i, j];
    }

    /// <summary>Bilinear prolongation, added into the fine field at free nodes.</summary>
    private static void Prolong(ScalarField2D coarse, ScalarField2D fine, DirichletMask fineMask)
    {
        var fineGrid = fine.Grid;
        var coarseGrid = coarse.Grid;

        for (var j = 0; j < fineGrid.CountY; j++)
        {
            for (var i = 0; i < fineGrid.CountX; i++)
            {
                if (fineMask.IsFixed(i, j))
                {
                    continue;
                }

                var ci = i / 2;
                var cj = j / 2;
                var oddI = (i & 1) == 1;
                var oddJ = (j & 1) == 1;

                double correction;

                if (!oddI && !oddJ)
                {
                    correction = coarse[ci, cj];
                }
                else if (oddI && !oddJ)
                {
                    correction = 0.5 * (coarse[ci, cj] + coarse[Math.Min(ci + 1, coarseGrid.CountX - 1), cj]);
                }
                else if (!oddI && oddJ)
                {
                    correction = 0.5 * (coarse[ci, cj] + coarse[ci, Math.Min(cj + 1, coarseGrid.CountY - 1)]);
                }
                else
                {
                    var i1 = Math.Min(ci + 1, coarseGrid.CountX - 1);
                    var j1 = Math.Min(cj + 1, coarseGrid.CountY - 1);
                    correction = 0.25 * (coarse[ci, cj] + coarse[i1, cj] + coarse[ci, j1] + coarse[i1, j1]);
                }

                fine[i, j] += correction;
            }
        }
    }
}
