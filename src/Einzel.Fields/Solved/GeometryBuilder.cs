using System.Numerics;
using Einzel.Fields.Analytic;
using Einzel.Core.Model;

namespace Einzel.Fields.Solved;

/// <summary>
/// Turns the electrode geometry declared in a model document into a grid, a
/// Dirichlet mask, and a solve.
/// </summary>
/// <remarks>
/// <para>
/// The seam that makes LIB-1 true. Before this existed, a mirror was a C# class
/// and a quadrupole would have been another one; now both are documents naming
/// the same three primitives in different places, and adding a device requires no
/// change below Einzel.Library — which is exactly the test LIB-1 sets.
/// </para>
/// <para>
/// Nothing here knows what any arrangement is for. It rasterises shapes onto a
/// grid and hands the result to the solver.
/// </para>
/// </remarks>
public static class GeometryBuilder
{
    /// <summary>Builds the grid a declared domain calls for.</summary>
    /// <param name="solve">The declared geometry.</param>
    /// <returns>The grid, spanning the declared box with power-of-two interval counts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="solve"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Each axis gets its own interval count, from the same requested cell size,
    /// rounded up to a power of two so coarsening is exact. Two consequences,
    /// both wanted. The spacing is at least as fine as asked for in <em>both</em>
    /// directions and never coarser. And the grid spans exactly the declared box,
    /// rather than whatever box a single spacing happened to reach.
    /// </para>
    /// <para>
    /// The cost is that cells need not be square. Since both spacings lie in the
    /// half-open interval from half the requested cell size to the cell size, the
    /// worst anisotropy is two to one - fine for a point smoother, and much
    /// cheaper than the alternative, which was silently solving a different
    /// domain.
    /// </para>
    /// </remarks>
    public static Grid2D BuildGrid(CompiledSolvedField solve)
    {
        ArgumentNullException.ThrowIfNull(solve);

        return Grid2D.OverBox(
            solve.MinX,
            solve.MinY,
            solve.MaxX,
            solve.MaxY,
            Intervals(solve.MaxX - solve.MinX, solve.CellSize),
            Intervals(solve.MaxY - solve.MinY, solve.CellSize));
    }

    private static int Intervals(double extent, double cellSize) =>
        (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(4, (int)Math.Ceiling(extent / cellSize)));

    /// <summary>Rasterises the declared electrodes onto a mask.</summary>
    /// <param name="solve">The declared geometry.</param>
    /// <param name="grid">The grid to rasterise onto.</param>
    /// <param name="potentialOf">
    /// Optional override of each electrode's potential, by name. Used to build
    /// basis fields, where one electrode sits at one volt and the rest at zero.
    /// </param>
    /// <returns>The mask.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static DirichletMask BuildMask(
        CompiledSolvedField solve, Grid2D grid, Func<CompiledElectrode, double>? potentialOf = null)
    {
        ArgumentNullException.ThrowIfNull(solve);
        ArgumentNullException.ThrowIfNull(grid);

        var (left, right, bottom, top) = Edges(solve, grid);

        var mask = new DirichletMask(grid)
        {
            LeftEdge = left,
            RightEdge = right,
            BottomEdge = bottom,
            TopEdge = top,
            Symmetry = solve.Symmetry,
        };

        foreach (var electrode in solve.Electrodes)
        {
            var potential = Held(electrode, potentialOf);

            switch (electrode.Shape)
            {
                case ElectrodeShape.Rectangle:
                    RasteriseRectangle(mask, grid, electrode, potential);
                    break;

                case ElectrodeShape.Disc:
                    RasteriseDisc(mask, grid, electrode, potential);
                    break;

                case ElectrodeShape.Polygon:
                    RasterisePolygon(mask, grid, electrode, potential);
                    break;

                case ElectrodeShape.EdgeProfile:
                    RasteriseEdgeProfile(mask, grid, electrode, potential);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(solve), electrode.Shape, "unhandled electrode shape");
            }
        }

        PinDirichletEdges(mask, grid);
        AddCuts(solve, grid, mask, potentialOf);

        return mask;
    }

    /// <summary>What each edge of a solve's mask is, the axis of an axisymmetric solve included.</summary>
    /// <remarks>
    /// <para>
    /// The axis of an axisymmetric solve is a mirror plane whatever the document says,
    /// because it is one. Forced rather than validated: requiring the author to declare it
    /// is an opportunity to get it wrong, and there is no second thing it could be.
    /// </para>
    /// <para>
    /// One function, so the mask, the check for nodes on a grounded edge
    /// (<see cref="NodesOnSharedFaces"/>) and the drawing of a conductor against the edge
    /// (<see cref="IsMirror"/>) cannot come to disagree about which edges are grounded.
    /// </para>
    /// </remarks>
    private static (EdgeCondition Left, EdgeCondition Right, EdgeCondition Bottom, EdgeCondition Top) Edges(
        CompiledSolvedField solve, Grid2D grid)
    {
        var bottom = solve.Symmetry == SolveSymmetry.Cylindrical && grid.OriginY <= 0.5 * grid.SpacingY
            ? EdgeCondition.Neumann
            : Translate(solve.BottomEdge);

        return (Translate(solve.LeftEdge), Translate(solve.RightEdge), bottom, Translate(solve.TopEdge));
    }

    /// <summary>
    /// Whether one edge of a solve's domain is a mirror plane, the axis of an axisymmetric
    /// solve included.
    /// </summary>
    /// <param name="solve">The declared geometry.</param>
    /// <param name="edge">Which edge.</param>
    /// <returns>True for a Neumann edge, false for one the solve holds at a potential.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="solve"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Asked by whatever draws a conductor against the edge, because the two kinds of edge
    /// end a conductor differently. By symmetry a conductor continues across a mirror, so
    /// the edge is where the half-domain stops describing it and not where it stops; at any
    /// other edge the solve holds nothing beyond, so a conductor reaching the edge ends there
    /// with a face.
    /// </para>
    /// <para>
    /// Answered by the same function the mask is built from, so a drawing and the solve it
    /// is a drawing of cannot disagree about which edges are which - including the axis,
    /// which is a mirror whatever the document says.
    /// </para>
    /// </remarks>
    public static bool IsMirror(CompiledSolvedField solve, GridEdge edge)
    {
        ArgumentNullException.ThrowIfNull(solve);

        var (left, right, bottom, top) = Edges(solve, BuildGrid(solve));

        var condition = edge switch
        {
            GridEdge.Left => left,
            GridEdge.Right => right,
            GridEdge.Bottom => bottom,
            GridEdge.Top => top,
            _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, "unhandled edge"),
        };

        return condition == EdgeCondition.Neumann;
    }

    /// <summary>
    /// Grounds every node on a Dirichlet domain edge that no electrode has already
    /// claimed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A Dirichlet edge has to mean the potential is zero <em>on the edge</em>. The
    /// alternative reading — a ghost node one cell outside the grid held at zero,
    /// with the edge node itself solved — is self-consistent on any single grid,
    /// and it was what the solver did. It is wrong the moment there is more than
    /// one grid: the ghost sits one cell out, so the boundary is one cell out at
    /// the fine level, two at the next, four at the next. Every level of a V-cycle
    /// then solves a slightly larger domain than the one above it, and a coarse
    /// correction computed on the wrong domain does not correct anything.
    /// </para>
    /// <para>
    /// It diverged rather than merely converging slowly: 1e50 V on a cap plate in
    /// a grounded box. The reason it went unnoticed is that the coarsening limit
    /// happened to stop these geometries before they reached a second level, so
    /// the solver fell back on plain Gauss-Seidel and reported a convergence
    /// factor of 0.84 — poor, but not obviously a bug.
    /// </para>
    /// <para>
    /// Electrodes are rasterised first and are not overwritten, so a plate that
    /// reaches the edge of the domain still holds the edge. Which edge nodes it reaches
    /// is decided by rasterization, so a plate whose face is <em>flush</em> with the edge
    /// holds the nodes on that face or leaves them grounded according to the rounding -
    /// which <see cref="NodesOnSharedFaces"/> reports.
    /// </para>
    /// </remarks>
    private static void PinDirichletEdges(DirichletMask mask, Grid2D grid)
    {
        for (var i = 0; i < grid.CountX; i++)
        {
            if (mask.BottomEdge == EdgeCondition.Dirichlet && !mask.IsFixed(i, 0))
            {
                mask.Fix(i, 0, 0.0);
            }

            if (mask.TopEdge == EdgeCondition.Dirichlet && !mask.IsFixed(i, grid.CountY - 1))
            {
                mask.Fix(i, grid.CountY - 1, 0.0);
            }
        }

        for (var j = 0; j < grid.CountY; j++)
        {
            if (mask.LeftEdge == EdgeCondition.Dirichlet && !mask.IsFixed(0, j))
            {
                mask.Fix(0, j, 0.0);
            }

            if (mask.RightEdge == EdgeCondition.Dirichlet && !mask.IsFixed(grid.CountX - 1, j))
            {
                mask.Fix(grid.CountX - 1, j, 0.0);
            }
        }
    }

    /// <summary>
    /// Records where each electrode surface crosses between nodes, so the solver
    /// can place the boundary where it is rather than at the nearest node.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately independent of which nodes were rasterised. Asking "is my
    /// neighbour a fixed node?" would tie the sub-cell boundary back to the
    /// staircase it exists to remove, and would miss an electrode thinner than a
    /// cell entirely — which is every coarse multigrid level of a thin plate.
    /// Asking the geometry directly finds the surface whether or not any node
    /// happens to lie behind it.
    /// </para>
    /// <para>
    /// A node may be cut by more than one electrode in the same direction, at a
    /// gap between two plates narrower than a cell. The nearest one wins, because
    /// it is the one the stencil can see; the far one is in shadow behind a
    /// conductor.
    /// </para>
    /// </remarks>
    private static void AddCuts(
        CompiledSolvedField solve, Grid2D grid, DirichletMask mask, Func<CompiledElectrode, double>? potentialOf)
    {
        var cuts = new CutLinks(grid);

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                if (mask.IsFixed(i, j))
                {
                    continue;
                }

                CutTowards(solve, grid, cuts, potentialOf, i, j, 1, 0, StencilDirection.East);
                CutTowards(solve, grid, cuts, potentialOf, i, j, -1, 0, StencilDirection.West);
                CutTowards(solve, grid, cuts, potentialOf, i, j, 0, 1, StencilDirection.North);
                CutTowards(solve, grid, cuts, potentialOf, i, j, 0, -1, StencilDirection.South);
            }
        }

        mask.Cuts = cuts.HasCuts ? cuts : null;
    }

    private static void CutTowards(
        CompiledSolvedField solve,
        Grid2D grid,
        CutLinks cuts,
        Func<CompiledElectrode, double>? potentialOf,
        int i,
        int j,
        int di,
        int dj,
        StencilDirection direction)
    {
        var ni = i + di;
        var nj = j + dj;

        if (ni < 0 || nj < 0 || ni >= grid.CountX || nj >= grid.CountY)
        {
            return;
        }

        var fromX = grid.X(i);
        var fromY = grid.Y(j);
        var toX = grid.X(ni);
        var toY = grid.Y(nj);

        var nearest = 1.0;
        var potential = 0.0;
        var found = false;

        foreach (var electrode in solve.Electrodes)
        {
            if (electrode.FirstEntry(fromX, fromY, toX, toY) is not { } entry || entry >= nearest)
            {
                continue;
            }

            // A surface at zero would put the node itself on the conductor, where
            // rasterisation should already have fixed it. Ignoring it keeps a
            // rounding disagreement between the two from producing a stencil with
            // no extent at all.
            if (entry <= 0.0)
            {
                continue;
            }

            nearest = entry;
            potential = potentialOf?.Invoke(electrode) ?? electrode.Potential;
            found = true;
        }

        if (found)
        {
            cuts.Cut(i, j, direction, nearest, potential);
        }
    }

    /// <summary>
    /// Finds where a mesh samples a face two conductors share while holding different
    /// excitations - a node on it, or a stencil arm that first meets metal on it - so that
    /// which conductor the sample is credited to is decided by rounding.
    /// </summary>
    /// <param name="solve">The declared geometry.</param>
    /// <param name="grid">The finest grid it is solved on.</param>
    /// <param name="mask">
    /// Any finest-level mask of this geometry on this grid, for which nodes are fixed; built
    /// when omitted, and only if an arm needs confirming. Every channel's mask fixes the same
    /// nodes, so whichever the caller has will do.
    /// </param>
    /// <returns>What was found, or null when the mesh samples no such face.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <remarks>
    /// <para>
    /// The plane half of <see cref="GeometryBuilder3D.NodesOnSharedFaces"/>, with the same
    /// rules: a node is on a surface within <see cref="SharedFaceNodes.ToleranceFraction"/>
    /// of the finest spacing; an arm from a free node samples a face when the point where it
    /// first meets one conductor is that near the other's surface and nothing is nearer; a pair
    /// counts when it disagrees in any state the instrument has, asked through
    /// <see cref="ElectrodeOverlap.StatesOf"/> and <see cref="ElectrodeOverlap.Agrees"/>; and
    /// only nodes within a cell of both bounding boxes are visited.
    /// </para>
    /// <para>
    /// <b>The plane's node coin is tossed a little differently, and it is still a coin.</b> A
    /// rectangle is rasterized by index, inclusive at both ends so abutting electrodes share
    /// their contact nodes, and whether the node on a shared face falls inside each range is a
    /// <c>Floor</c> and a <c>Ceiling</c> of a computed quotient. It is never left free, but it
    /// holds one potential or the other according to the rounding - or, when both ranges
    /// include it, according to which electrode was declared last.
    /// </para>
    /// <para>
    /// <b>A grounded edge of the domain counts as a conductor at zero volts</b>, and nodes on
    /// it lying on an electrode's surface are counted apart, as
    /// <see cref="SharedFaceNodes.BoundaryNodes"/>: the edge pins a node to zero unless an
    /// electrode claimed it, so such a node holds the electrode's potential or zero on the last
    /// bit. Nodes only, since an edge node is fixed either way.
    /// </para>
    /// <para>
    /// An edge profile has no surface to be near: it lies along a domain edge and its signed
    /// distance is infinite, so it takes no part. A profiled board meeting an interior
    /// electrode at the edge is decided by declaration order rather than by rounding, which is
    /// a different question and one <see cref="ElectrodeOverlap"/> also leaves aside.
    /// </para>
    /// </remarks>
    public static SharedFaceNodes? NodesOnSharedFaces(
        CompiledSolvedField solve, Grid2D grid, DirichletMask? mask = null)
    {
        ArgumentNullException.ThrowIfNull(solve);
        ArgumentNullException.ThrowIfNull(grid);

        var electrodes = solve.Electrodes;
        var states = ElectrodeOverlap.StatesOf(electrodes, solve.Stages);
        var tolerance = SharedFaceNodes.ToleranceFraction * grid.MinimumSpacing;
        var reach = Math.Max(grid.SpacingX, grid.SpacingY);

        var nodes = new HashSet<long>();
        var arms = new HashSet<long>();
        (string First, string Second, double X, double Y)? nodeExample = null;
        (string First, string Second, double X, double Y)? armExample = null;

        for (var i = 0; i < electrodes.Count; i++)
        {
            for (var j = i + 1; j < electrodes.Count; j++)
            {
                var (i1, i2) = (i, j);

                if (states.All(state => ElectrodeOverlap.Agrees(state[i1], state[i2])))
                {
                    continue;
                }

                var a = electrodes[i];
                var b = electrodes[j];

                if (BoundsOf(a) is not { } boxA || BoundsOf(b) is not { } boxB)
                {
                    continue;
                }

                var x0 = Math.Max(boxA.MinX, boxB.MinX) - tolerance;
                var x1 = Math.Min(boxA.MaxX, boxB.MaxX) + tolerance;
                var y0 = Math.Max(boxA.MinY, boxB.MinY) - tolerance;
                var y1 = Math.Min(boxA.MaxY, boxB.MaxY) + tolerance;

                if (x1 < x0 || y1 < y0)
                {
                    continue;
                }

                var (iLo, iHi) = SharedFaceNodes.Range(grid.OriginX, grid.SpacingX, grid.CountX, x0 - reach, x1 + reach);
                var (jLo, jHi) = SharedFaceNodes.Range(grid.OriginY, grid.SpacingY, grid.CountY, y0 - reach, y1 + reach);

                for (var jj = jLo; jj <= jHi; jj++)
                {
                    var y = grid.Y(jj);

                    for (var ii = iLo; ii <= iHi; ii++)
                    {
                        var x = grid.X(ii);

                        var da = Math.Abs(a.SignedDistance(x, y));

                        if (da > reach + tolerance)
                        {
                            continue;
                        }

                        var db = Math.Abs(b.SignedDistance(x, y));

                        if (db > reach + tolerance)
                        {
                            continue;
                        }

                        var node = ((long)jj * grid.CountX) + ii;

                        if (da <= tolerance && db <= tolerance)
                        {
                            if (nodes.Add(node))
                            {
                                nodeExample ??= (a.Name, b.Name, x, y);
                            }

                            continue;
                        }

                        for (var arm = 0; arm < 4; arm++)
                        {
                            var (di, dj) = PlaneArms[arm];
                            var (ni, nj) = (ii + di, jj + dj);

                            if (ni < 0 || nj < 0 || ni >= grid.CountX || nj >= grid.CountY)
                            {
                                continue;
                            }

                            var (tx, ty) = (grid.X(ni), grid.Y(nj));

                            // Where the arm meets one of them, if that point is on the other's
                            // surface too - the nearer such point, when both qualify.
                            var entry = double.PositiveInfinity;

                            if (SharedFaceNodes.Interior(a.FirstEntry(x, y, tx, ty), out var ea)
                                && Math.Abs(b.SignedDistance(x + (ea * (tx - x)), y + (ea * (ty - y)))) <= tolerance)
                            {
                                entry = ea;
                            }

                            if (SharedFaceNodes.Interior(b.FirstEntry(x, y, tx, ty), out var eb)
                                && Math.Abs(a.SignedDistance(x + (eb * (tx - x)), y + (eb * (ty - y)))) <= tolerance)
                            {
                                entry = Math.Min(entry, eb);
                            }

                            if (double.IsPositiveInfinity(entry))
                            {
                                continue;
                            }

                            // Only now, as in the volume: a fixed node has no stencil to cut, and
                            // a mask is worth building only once there is something to confirm.
                            mask ??= BuildMask(solve, grid);

                            if (mask.IsFixed(ii, jj))
                            {
                                break;
                            }

                            if (Shadowed(electrodes, x, y, tx, ty, entry))
                            {
                                continue;
                            }

                            if (arms.Add((node * 4) + arm))
                            {
                                armExample ??= (a.Name, b.Name, x + (entry * (tx - x)), y + (entry * (ty - y)));
                            }
                        }
                    }
                }
            }
        }

        var (boundary, boundaryExample) = NodesOnGroundedEdges(solve, grid, states, tolerance);

        if ((nodeExample ?? armExample) is { } first)
        {
            return new SharedFaceNodes(
                nodes.Count, arms.Count, first.First, first.Second, first.X, first.Y, null,
                ExampleIsArm: nodeExample is null)
            {
                BoundaryNodes = boundary,
            };
        }

        return boundaryExample is { } edge
            ? new SharedFaceNodes(0, 0, edge.First, edge.Face, edge.X, edge.Y, null, ExampleIsArm: false)
            {
                BoundaryNodes = boundary,
                ExampleIsBoundary = true,
            }
            : null;
    }

    /// <summary>
    /// Nodes on a grounded edge of the domain that lie on the surface of an electrode holding
    /// something other than zero volts in some state, so that whether the electrode or the
    /// edge claims them is decided by rounding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The grounded boundary is a third conductor at zero volts (<c>docs/numerics.md</c>,
    /// "Domain edges"): <see cref="PinDirichletEdges"/> fixes an edge node to zero unless an
    /// electrode has claimed it first, and an electrode claims it by containing it. So a node
    /// on the edge that is on the electrode's surface - on a face flush with the edge, or where
    /// a face crossing the edge meets it - holds the electrode's potential or zero on the last
    /// bit.
    /// </para>
    /// <para>
    /// Nodes only. An edge node is fixed either way and the edge is never a cut target, so no
    /// arm samples the boundary; an arm that reaches a flipped node uncut - from a conductor
    /// lying outside the domain against it - reads the node's value, which is the node's coin.
    /// </para>
    /// <para>
    /// "Holds something other than zero" is the overlap check's own reading, asked of the
    /// electrode against itself stripped of its excitation, so a zero-volt electrode is
    /// harmless against the edge exactly when it would be harmless against another zero-volt
    /// electrode. A Neumann edge - the axis of an axisymmetric solve included - is a mirror and
    /// takes no part: a node there flips between fixed and free beside the conductor, which is
    /// an ordinary cut cell rather than a choice between two values. An edge carrying an edge
    /// profile is not grounded anywhere, since the profile claims every node on it.
    /// </para>
    /// <para>
    /// <b>A face lying in a mirror plane is not a surface.</b> By symmetry the conductor
    /// continues across a Neumann edge, so a face declared in it is where the half-domain stops
    /// describing the conductor, not where the conductor stops. The case that forced it is the
    /// axis of an axisymmetric solve: a solid rod is written as a rectangle from the axis
    /// outward, and in space the axis is inside it. So where a grounded edge meets a mirror
    /// edge, the corner node is asked about a point just inside the mirror instead. A face
    /// crossing the mirror still counts there - a rod whose end is flush with the grounded edge
    /// is flagged at that corner - and a face lying in it does not, so a rod carried past the
    /// edge is clean, as the warning's advice says it will be.
    /// </para>
    /// </remarks>
    private static (int Count, (string First, string Face, double X, double Y)? Example) NodesOnGroundedEdges(
        CompiledSolvedField solve,
        Grid2D grid,
        IReadOnlyList<IReadOnlyList<CompiledElectrode>> states,
        double tolerance)
    {
        var electrodes = solve.Electrodes;
        var (left, right, bottom, top) = Edges(solve, grid);

        // Ten tolerances into the domain from a mirror edge: clear of a face lying in it, and
        // still on any face crossing it.
        var lift = 10.0 * tolerance;

        var profiled = electrodes
            .Where(e => e.Shape == ElectrodeShape.EdgeProfile)
            .Select(e => e.Edge)
            .ToHashSet();

        // Each grounded edge as the line of nodes it is: which index is held, along which axis.
        var edges = new List<(string Face, bool Vertical, int Line)>(4);

        void Add(string face, EdgeCondition condition, GridEdge edge, bool vertical, int line)
        {
            if (condition == EdgeCondition.Dirichlet && !profiled.Contains(edge))
            {
                edges.Add((face, vertical, line));
            }
        }

        Add("left edge", left, GridEdge.Left, vertical: true, 0);
        Add("right edge", right, GridEdge.Right, vertical: true, grid.CountX - 1);
        Add("bottom edge", bottom, GridEdge.Bottom, vertical: false, 0);
        Add("top edge", top, GridEdge.Top, vertical: false, grid.CountY - 1);

        var found = new HashSet<long>();
        (string First, string Face, double X, double Y)? example = null;

        for (var e = 0; e < electrodes.Count; e++)
        {
            var index = e;
            var electrode = electrodes[e];

            if (BoundsOf(electrode) is not { } box
                || states.All(state => ElectrodeOverlap.Agrees(
                    state[index], state[index] with { Potential = 0.0, Taps = [] })))
            {
                continue;
            }

            foreach (var (face, vertical, line) in edges)
            {
                // The line of nodes' own coordinate, not the declared bound: a last node written
                // as origin plus count times spacing need not land on the declared maximum
                // exactly, and the line of nodes is what gets pinned.
                var (across, lo, hi) = vertical
                    ? (grid.X(line), box.MinX, box.MaxX)
                    : (grid.Y(line), box.MinY, box.MaxY);

                if (across < lo - tolerance || across > hi + tolerance)
                {
                    continue;
                }

                var (first, last) = vertical
                    ? SharedFaceNodes.Range(
                        grid.OriginY, grid.SpacingY, grid.CountY, box.MinY - tolerance, box.MaxY + tolerance)
                    : SharedFaceNodes.Range(
                        grid.OriginX, grid.SpacingX, grid.CountX, box.MinX - tolerance, box.MaxX + tolerance);

                for (var along = first; along <= last; along++)
                {
                    var (i, j) = vertical ? (line, along) : (along, line);
                    var (x, y) = (grid.X(i), grid.Y(j));

                    var askedX = x
                        + (i == 0 && left == EdgeCondition.Neumann ? lift : 0.0)
                        - (i == grid.CountX - 1 && right == EdgeCondition.Neumann ? lift : 0.0);
                    var askedY = y
                        + (j == 0 && bottom == EdgeCondition.Neumann ? lift : 0.0)
                        - (j == grid.CountY - 1 && top == EdgeCondition.Neumann ? lift : 0.0);

                    if (Math.Abs(electrode.SignedDistance(askedX, askedY)) > tolerance)
                    {
                        continue;
                    }

                    if (found.Add(((long)j * grid.CountX) + i))
                    {
                        example ??= (electrode.Name, face, x, y);
                    }
                }
            }
        }

        return (found.Count, example);
    }

    /// <summary>The four stencil arms, in the order <see cref="AddCuts"/> visits them.</summary>
    private static readonly (int Di, int Dj)[] PlaneArms = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>
    /// Whether some conductor meets the arm clearly before <paramref name="entry"/>, so the
    /// cut records that one and a tie at <paramref name="entry"/> is in its shadow.
    /// </summary>
    private static bool Shadowed(
        IReadOnlyList<CompiledElectrode> electrodes,
        double fromX, double fromY, double toX, double toY, double entry)
    {
        foreach (var electrode in electrodes)
        {
            if (electrode.FirstEntry(fromX, fromY, toX, toY) is { } nearer
                && nearer > 0.0
                && nearer < entry - SharedFaceNodes.ToleranceFraction)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The box a plane electrode's surface lies within, or null for one with no surface to
    /// locate.
    /// </summary>
    private static (double MinX, double MinY, double MaxX, double MaxY)? BoundsOf(
        CompiledElectrode electrode) =>
        electrode.Shape switch
        {
            ElectrodeShape.Rectangle or ElectrodeShape.Polygon =>
                (electrode.MinX, electrode.MinY, electrode.MaxX, electrode.MaxY),

            ElectrodeShape.Disc => (
                electrode.CentreX - electrode.Radius, electrode.CentreY - electrode.Radius,
                electrode.CentreX + electrode.Radius, electrode.CentreY + electrode.Radius),

            ElectrodeShape.EdgeProfile => null,

            _ => throw new ArgumentOutOfRangeException(
                nameof(electrode), electrode.Shape, "unhandled electrode shape"),
        };

    private static EdgeCondition Translate(BoundaryKind kind) =>
        kind == BoundaryKind.Neumann ? EdgeCondition.Neumann : EdgeCondition.Dirichlet;

    private static void RasteriseRectangle(
        DirichletMask mask, Grid2D grid, CompiledElectrode electrode, double potential)
    {
        // Half-open in index space but inclusive in coordinate space: a node lying
        // on the boundary of the rectangle belongs to it, so two abutting
        // electrodes share their contact nodes rather than leaving a gap.
        var i0 = (int)Math.Ceiling((electrode.MinX - grid.OriginX) / grid.SpacingX);
        var i1 = (int)Math.Floor((electrode.MaxX - grid.OriginX) / grid.SpacingX);
        var j0 = (int)Math.Ceiling((electrode.MinY - grid.OriginY) / grid.SpacingY);
        var j1 = (int)Math.Floor((electrode.MaxY - grid.OriginY) / grid.SpacingY);

        mask.FixRectangle(i0, j0, i1, j1, potential);
    }

    private static void RasteriseDisc(
        DirichletMask mask, Grid2D grid, CompiledElectrode electrode, double potential)
    {
        var radiusSquared = electrode.Radius * electrode.Radius;

        var i0 = Math.Max(0, (int)Math.Floor((electrode.CentreX - electrode.Radius - grid.OriginX) / grid.SpacingX));
        var i1 = Math.Min(grid.CountX - 1,
            (int)Math.Ceiling((electrode.CentreX + electrode.Radius - grid.OriginX) / grid.SpacingX));
        var j0 = Math.Max(0, (int)Math.Floor((electrode.CentreY - electrode.Radius - grid.OriginY) / grid.SpacingY));
        var j1 = Math.Min(grid.CountY - 1,
            (int)Math.Ceiling((electrode.CentreY + electrode.Radius - grid.OriginY) / grid.SpacingY));

        for (var j = j0; j <= j1; j++)
        {
            var dy = grid.Y(j) - electrode.CentreY;

            for (var i = i0; i <= i1; i++)
            {
                var dx = grid.X(i) - electrode.CentreX;

                if ((dx * dx) + (dy * dy) <= radiusSquared)
                {
                    mask.Fix(i, j, potential);
                }
            }
        }
    }


    /// <summary>
    /// Fixes every node inside a polygon electrode. Nodes are classified by the
    /// polygon's own signed distance, so the raster and the cut links that follow it
    /// agree about where the surface is.
    /// </summary>
    private static void RasterisePolygon(
        DirichletMask mask, Grid2D grid, CompiledElectrode electrode, double potential)
    {
        var i0 = Math.Max(0, (int)Math.Floor((electrode.MinX - grid.OriginX) / grid.SpacingX));
        var i1 = Math.Min(grid.CountX - 1, (int)Math.Ceiling((electrode.MaxX - grid.OriginX) / grid.SpacingX));
        var j0 = Math.Max(0, (int)Math.Floor((electrode.MinY - grid.OriginY) / grid.SpacingY));
        var j1 = Math.Min(grid.CountY - 1, (int)Math.Ceiling((electrode.MaxY - grid.OriginY) / grid.SpacingY));

        for (var j = j0; j <= j1; j++)
        {
            var y = grid.Y(j);

            for (var i = i0; i <= i1; i++)
            {
                if (electrode.Contains(grid.X(i), y))
                {
                    mask.Fix(i, j, potential);
                }
            }
        }
    }

    /// <summary>
    /// Fixes an entire domain edge to a potential that varies along it.
    /// </summary>
    /// <remarks>
    /// The <paramref name="scale"/> multiplies the profile, so a basis solve can
    /// raise a whole printed board to unit potential without restating its shape.
    /// A profile is one electrode even though it spans many nodes, because that is
    /// how it is driven: one supply feeding a resistive divider.
    /// </remarks>
    private static void RasteriseEdgeProfile(
        DirichletMask mask, Grid2D grid, CompiledElectrode electrode, double scale)
    {
        switch (electrode.Edge)
        {
            case GridEdge.Bottom:
            case GridEdge.Top:
            {
                var j = electrode.Edge == GridEdge.Bottom ? 0 : grid.CountY - 1;

                for (var i = 0; i < grid.CountX; i++)
                {
                    mask.Fix(i, j, scale * electrode.ProfileAt(grid.X(i)));
                }

                break;
            }

            case GridEdge.Left:
            case GridEdge.Right:
            {
                var i = electrode.Edge == GridEdge.Left ? 0 : grid.CountX - 1;

                for (var j = 0; j < grid.CountY; j++)
                {
                    mask.Fix(i, j, scale * electrode.ProfileAt(grid.Y(j)));
                }

                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(electrode), electrode.Edge, "unhandled edge");
        }
    }

    /// <summary>One basis channel of a solve, with the evidence that solve produced.</summary>
    /// <param name="Index">Which channel, in the order the decomposition found them.</param>
    /// <param name="FixedNodes">How many finest-level nodes the channel's mask held fixed.</param>
    /// <param name="CutLinks">How many stencil arms a conductor surface cut.</param>
    /// <param name="Potential">The solved potential.</param>
    /// <param name="Report">How the solve went.</param>
    /// <remarks>
    /// The mask's two counts rather than the mask, for the reason the three-dimensional
    /// record gives: a list of these built channel by channel kept every earlier channel's
    /// mask and cut links alive through every later solve, to report two integers.
    /// </remarks>
    public sealed record ChannelSolve(
        int Index, int FixedNodes, int CutLinks, ScalarField2D Potential, SolveReport Report)
    {
        /// <summary>
        /// What each declared electrode held in this solve, in declaration order: volts, or for
        /// an edge profile a scale on the profile that holds its volts.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The declared potentials for a static geometry, and for a driven or sequenced one
        /// the channel's spatial pattern with its leading electrode at one volt - which is
        /// what a reader of an exported channel needs to know it is looking at, since two
        /// files called <c>channel0</c> and <c>channel1</c> say nothing about which is the DC
        /// chain and which the RF.
        /// </para>
        /// <para>
        /// <b>By position, not by name.</b> Each entry is the value the mask was built with,
        /// through the same function and in the same order, so it cannot describe a solve other
        /// than the one that ran. It was a dictionary keyed by name once, when a name defaulted
        /// to <c>electrode</c> and need not have been unique, and two unnamed plates reported
        /// one potential between them; the validator now makes names unique within a solve, and
        /// the list keeps the report from depending on that.
        /// </para>
        /// </remarks>
        public IReadOnlyList<double> Holding { get; init; } = [];
    }

    /// <summary>Solves a geometry channel by channel, and hands back the diagnostics.</summary>
    /// <param name="solve">The compiled solve.</param>
    /// <returns>One entry per basis channel; exactly one for a static geometry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="solve"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// What <c>einzel solve</c> reports against, and it had to exist for the same
    /// reason the three-dimensional version does: a driven structure is not one
    /// solve but one per spatial pattern, and a residual quoted for "the field"
    /// would be quoting whichever of them happened to be last.
    /// </para>
    /// <para>
    /// Without it the verb reported the <em>DC</em> pattern alone. For the shipped
    /// RF quadrupole, whose every electrode holds zero volts of DC and all of its
    /// potential as drive, that was a solve of a grounded box: peak potential 0 V,
    /// zero cycles, converged true, exit 0. The verb whose entire job is to report
    /// a residual gave a clean bill of health to a field it had never touched, for
    /// a mass filter. The same defect was found and fixed in three dimensions by a
    /// code review; this is the half that was left.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ChannelSolve> SolveChannels(CompiledSolvedField solve)
    {
        ArgumentNullException.ThrowIfNull(solve);

        var grid = BuildGrid(solve);
        var patterns = Patterns(solve);
        var solves = new List<ChannelSolve>(patterns.Count);
        IReadOnlyList<Core.Results.ValidityWarning>? warnings = null;

        for (var index = 0; index < patterns.Count; index++)
        {
            var potentialOf = patterns[index];
            var channel = SolvePattern(solve, grid, potentialOf, askMesh: index == 0);

            // Every channel carries the same finding, because every channel solves on the
            // same mesh round the same conductors; einzel solve reports it once per element.
            warnings ??= channel.MeshWarnings;

            solves.Add(new ChannelSolve(
                index, channel.FixedNodes, channel.CutLinks, channel.Potential, channel.Report with { Warnings = warnings ?? [] })
            {
                Holding = [.. solve.Electrodes.Select(e => Held(e, potentialOf))],
            });
        }

        return solves;
    }

    /// <summary>Solves one channel's pattern, and lets go of everything but its potential.</summary>
    /// <param name="solve">The declared geometry.</param>
    /// <param name="grid">The finest grid.</param>
    /// <param name="potentialOf">
    /// What each electrode holds in this channel, or null for every electrode as declared.
    /// </param>
    /// <param name="askMesh">Whether to ask, while the mask is in hand, where the mesh samples a shared face.</param>
    /// <remarks>
    /// <para>
    /// <b>A method of its own so that the mask dies with its frame</b>, which is the volume
    /// builder's lesson met in the plane. A channel loop written inline kept the first
    /// channel's mask for the shared-face check after the loop - 73 bytes a node, through
    /// every later channel's solve - and a method run once executes as first-tier JIT code,
    /// which keeps every temporary reachable until its slot is reused, so even a mask the loop
    /// no longer named could outlive its channel. Asked here, the shared-face question needs
    /// no mask afterwards: every channel solves on the same mesh round the same conductors,
    /// so the first channel's answer is every channel's.
    /// </para>
    /// </remarks>
    private static (ScalarField2D Potential, SolveReport Report, int FixedNodes, int CutLinks,
        IReadOnlyList<Core.Results.ValidityWarning>? MeshWarnings) SolvePattern(
        CompiledSolvedField solve, Grid2D grid, Func<CompiledElectrode, double>? potentialOf, bool askMesh)
    {
        var mask = BuildMask(solve, grid, potentialOf);

        var (potential, report) = PoissonSolver2D.Solve(
            mask,
            solve.Tolerance,
            maximumCycles: 400,
            coarsen: coarse => BuildMask(solve, coarse, potentialOf));

        var warnings = askMesh ? SharedFaceNodes.Warnings(NodesOnSharedFaces(solve, grid, mask)) : null;

        return (potential, report, mask.FixedCount, mask.Cuts?.CutCount ?? 0, warnings);
    }

    /// <summary>What building a declared geometry will hold in memory, worked out without building it.</summary>
    /// <param name="solve">The declared geometry.</param>
    /// <returns>The node and channel counts and the bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="solve"/> is null.</exception>
    /// <remarks>
    /// The channels come from the same decomposition <see cref="Build"/> makes, so the count
    /// this uses and the solves that run cannot disagree. A plane build solves them one at a
    /// time and keeps each potential, and no channel's mask outlives its solve.
    /// </remarks>
    public static SolveMemory Memory(CompiledSolvedField solve)
    {
        ArgumentNullException.ThrowIfNull(solve);

        var grid = BuildGrid(solve);

        return SolveMemoryModel.For(
            grid.NodeCount, Math.Max(1, Patterns(solve).Count), SolveMemoryModel.PlanePeak, galerkin: false);
    }

    /// <summary>What each electrode holds in each independent channel.</summary>
    /// <remarks>
    /// <para>
    /// One entry for a static geometry, and one per spatial pattern for a driven or sequenced
    /// one. Written once so the field builder and the diagnostics cannot disagree about how
    /// many solves a geometry needs, which is exactly the disagreement that let the
    /// diagnostics report a field nothing else used.
    /// </para>
    /// <para>
    /// <b>A static geometry's entry is null, meaning every electrode as declared</b> - the mask
    /// <see cref="Build"/> makes, electrode by electrode. It used to be a dictionary keyed by
    /// name, and that was wrong twice. Read from each electrode's scalar it zeroed every edge
    /// profile, whose volts are in its profile, so <c>einzel solve</c> solved the planar
    /// mirror pair with no boards. And a name defaulted to <c>electrode</c> and need not have
    /// been unique, so two unnamed plates at +100 V and -100 V both solved at -100.
    /// </para>
    /// </remarks>
    private static List<Func<CompiledElectrode, double>?> Patterns(CompiledSolvedField solve)
    {
        if (solve.Drives.Count == 0 && solve.Stages.Count == 0)
        {
            return [null];
        }

        var states = StageStates(solve);

        var (_, _, quadrature) = Clocks(solve.Drives);

        return
        [
            .. DriveChannels
                .DecomposeStates(States(states), quadrature)
                .Select(group => (Func<CompiledElectrode, double>?)Weighted(group.Pattern)),
        ];
    }

    /// <summary>Builds, solves, and wraps a declared geometry as a field.</summary>
    /// <param name="solve">The declared geometry.</param>
    /// <returns>The field, and how the solve went.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="solve"/> is null.</exception>
    public static (IElectrostaticField Field, SolveReport Report) Build(CompiledSolvedField solve)
    {
        ArgumentNullException.ThrowIfNull(solve);

        if (solve.Drives.Count > 0 || solve.Stages.Count > 0)
        {
            return BuildDriven(solve);
        }

        var grid = BuildGrid(solve);
        var mask = BuildMask(solve, grid);
        var (potential, report) = PoissonSolver2D.Solve(
            mask, solve.Tolerance, maximumCycles: 400, coarsen: coarse => BuildMask(solve, coarse));

        report = report with { Warnings = SharedFaceNodes.Warnings(NodesOnSharedFaces(solve, grid, mask)) };

        IElectrostaticField field = new SolvedField2D(
            potential,
            new BicubicInterpolant(potential),
            boundaryIsDiscontinuous: solve.BoundaryIsDiscontinuous,
            conductors: solve.Electrodes);

        // Wrapped before any reflection, so a reflection composes in space rather
        // than in the half-plane - it mirrors the axial coordinate of a field that
        // already knows how to be three-dimensional.
        if (solve.Symmetry == SolveSymmetry.Cylindrical)
        {
            field = new AxisymmetricField(field);
        }

        if (solve.ReflectAboutX is { } plane)
        {
            // The reflected half is the same solve seen backwards, so the two are
            // identical by construction and no difference between them can come
            // from their having been meshed differently.
            field = new SuperposedField([field, new ReflectedField(field, plane)]);
        }

        return (field, report);
    }

    /// <summary>
    /// Builds a driven geometry: one solve per independent channel, superposed with
    /// weights that are functions of time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing about the Poisson equation is time-dependent. The field is linear in
    /// the applied potentials, so solving once per channel at unit potential and
    /// then varying the weights <em>is</em> the RF - which is why driving a real
    /// geometry costs the same per step as a static one times the channel count,
    /// and no re-solves at all.
    /// </para>
    /// <para>
    /// Electrodes are grouped by their potential as a function of time, up to sign.
    /// A quadrupole's two pairs are exact negatives, so the whole device is one
    /// channel; add a grounded housing and it is two.
    /// </para>
    /// </remarks>
    private static (IElectrostaticField Field, SolveReport Report) BuildDriven(CompiledSolvedField solve)
    {
        var grid = BuildGrid(solve);
        var (frequencies, waveforms, quadrature) = Clocks(solve.Drives);

        // Every stage is decomposed on its own and the patterns are then shared, so a
        // pattern appearing in two stages is solved once and simply weighted differently
        // in each. A trap that fills and then extracts usually shares most of its patterns
        // between the two, and paying for them twice would be paying for the sequencer
        // rather than for the physics. Decomposing the states *together* is what this used
        // to do and is the one thing that must not happen - see DecomposeStates.
        var states = StageStates(solve);

        // A sinusoid resolves every phase into two fixed quadrature components, so
        // a structure with a phase ramp along it costs two solves rather than one
        // per electrode. Any other waveform cannot be decomposed that way and each
        // distinct phase stays its own supply. Decided per generator, since an
        // instrument may run a sinusoidal confinement and a switched excitation at
        // once and each collapses or does not on its own terms.
        var groups = DriveChannels.DecomposeStates(States(states), quadrature);

        var channels = new List<IElectrostaticField>(groups.Count);
        var direct = new List<double>(groups.Count);
        var harmonics = new List<IReadOnlyList<WeightTerm>>(groups.Count);

        SolveReport worst = new(true, 0, 0.0, 0.0, 0.0);
        IReadOnlyList<Core.Results.ValidityWarning>? meshWarnings = null;

        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];

            // The channel's relative potentials, and zero on every electrode it
            // does not reach. The scale was taken out during normalisation and put
            // back on the weight, so this solve is in units of that weight. Solved in a
            // frame of its own, which lets go of the mask - see SolvePattern.
            var (potential, report, _, _, warnings) =
                SolvePattern(solve, grid, Weighted(group.Pattern), askMesh: index == 0);

            meshWarnings ??= warnings;

            IElectrostaticField basis = new SolvedField2D(
                potential,
                new BicubicInterpolant(potential),
                boundaryIsDiscontinuous: solve.BoundaryIsDiscontinuous,
                conductors: solve.Electrodes);

            if (solve.Symmetry == SolveSymmetry.Cylindrical)
            {
                basis = new AxisymmetricField(basis);
            }

            if (solve.ReflectAboutX is { } plane)
            {
                basis = new SuperposedField([basis, new ReflectedField(basis, plane)]);
            }

            channels.Add(basis);
            direct.Add(group.Direct);
            harmonics.Add(group.Harmonics);

            if (report.Cycles > worst.Cycles)
            {
                worst = report;
            }
        }

        // Once per geometry rather than per channel: which nodes sit on a shared face is a
        // property of the mesh and the conductors, and every channel solves on both. Asked of
        // the first channel while its mask was in hand; a geometry with no channel is asked
        // node by node, as it always was.
        worst = worst with
        {
            Warnings = meshWarnings ?? SharedFaceNodes.Warnings(NodesOnSharedFaces(solve, grid)),
        };

        if (solve.Stages.Count == 0)
        {
            return (
                new DrivenSolvedField(channels, direct, harmonics, frequencies, waveforms), worst);
        }

        var boundaries = new List<double>(solve.Stages.Count);
        var stageDirect = new List<IReadOnlyList<double>>(solve.Stages.Count);
        var stageHarmonics =
            new List<IReadOnlyList<IReadOnlyList<WeightTerm>>>(solve.Stages.Count);
        var stageEndDirect = new List<IReadOnlyList<double>?>(solve.Stages.Count);
        var stageEndHarmonics = new List<IReadOnlyList<IReadOnlyList<WeightTerm>>?>(solve.Stages.Count);
        var anyRamp = false;

        var elapsed = 0.0;

        foreach (var stage in solve.Stages)
        {
            elapsed += stage.DurationSeconds;
            boundaries.Add(elapsed);

            // The same channels, re-weighted for this stage. Every pattern already
            // has a solve; what a stage changes is only how much of each is on.
            var weights = DriveChannels.Weigh(
                groups, [.. stage.Electrodes.Select(Excited)], quadrature);

            if (stage.EndElectrodes is null)
            {
                stageDirect.Add(weights.Direct);
                stageHarmonics.Add(weights.Harmonics);
                stageEndDirect.Add(null);
                stageEndHarmonics.Add(null);
                continue;
            }

            // A ramp: the same channels weighed at the end of the stage as well, with
            // the oscillating terms aligned so each start term has its end partner - a
            // supply that is off at one end and on at the other appears in only one
            // list, and is given a zero-amplitude partner in the other.
            anyRamp = true;
            var endWeights = DriveChannels.Weigh(
                groups, [.. stage.EndElectrodes.Select(Excited)], quadrature);
            var alignedStart = new List<IReadOnlyList<WeightTerm>>(groups.Count);
            var alignedEnd = new List<IReadOnlyList<WeightTerm>>(groups.Count);
            for (var k = 0; k < groups.Count; k++)
            {
                var (a, b) = Align(weights.Harmonics[k], endWeights.Harmonics[k]);
                alignedStart.Add(a);
                alignedEnd.Add(b);
            }

            stageDirect.Add(weights.Direct);
            stageHarmonics.Add(alignedStart);
            stageEndDirect.Add(endWeights.Direct);
            stageEndHarmonics.Add(alignedEnd);
        }

        var sequenced = new DrivenSolvedField(
            channels, direct, harmonics, frequencies, waveforms,
            boundaries, stageDirect, stageHarmonics,
            anyRamp ? stageEndDirect : null, anyRamp ? stageEndHarmonics : null);

        return (sequenced, worst);
    }

    /// <summary>
    /// Two lists of oscillating weight terms, one per end of a ramp, made term-for-term
    /// comparable: the same (drive, phase) in the same order, a zero amplitude standing
    /// in for a term one end does not have.
    /// </summary>
    internal static (IReadOnlyList<WeightTerm> Start, IReadOnlyList<WeightTerm> End) Align(
        IReadOnlyList<WeightTerm> start, IReadOnlyList<WeightTerm> end)
    {
        var keys = new List<(int Drive, double Phase)>();
        foreach (var term in start.Concat(end))
        {
            if (!keys.Contains((term.Drive, term.Phase)))
            {
                keys.Add((term.Drive, term.Phase));
            }
        }

        WeightTerm Pick(IReadOnlyList<WeightTerm> terms, (int Drive, double Phase) key)
        {
            var amplitude = 0.0;
            foreach (var term in terms)
            {
                if (term.Drive == key.Drive && term.Phase == key.Phase)
                {
                    amplitude += term.Amplitude;
                }
            }

            return new WeightTerm(key.Drive, amplitude, key.Phase);
        }

        return ([.. keys.Select(k => Pick(start, k))], [.. keys.Select(k => Pick(end, k))]);
    }


    /// <summary>
    /// Every state the electrodes pass through: each stage's start, and its end where it
    /// ramps. A ramp that starts from zero has its whole pattern at its end, so gathering
    /// starts alone would leave that pattern unsolved and the ramp would weigh nothing.
    /// </summary>
    private static List<IReadOnlyList<CompiledElectrode>> StageStates(CompiledSolvedField solve)
    {
        if (solve.Stages.Count == 0)
        {
            return [solve.Electrodes];
        }

        var states = new List<IReadOnlyList<CompiledElectrode>>(2 * solve.Stages.Count);
        foreach (var stage in solve.Stages)
        {
            states.Add(stage.Electrodes);
            if (stage.EndElectrodes is not null)
            {
                states.Add(stage.EndElectrodes);
            }
        }

        return states;
    }

    /// <summary>Each stage state as its own list of excitations, for the decomposition.</summary>
    private static List<IReadOnlyList<Excitation>> States(
        IReadOnlyList<IReadOnlyList<CompiledElectrode>> states) =>
        [.. states.Select(state => (IReadOnlyList<Excitation>)[.. state.Select(Excited)])];

    /// <summary>How a two-dimensional electrode is excited, for the shared decomposition.</summary>
    /// <remarks>
    /// <para>
    /// An <b>edge profile carries its volts in its profile</b> rather than in a scalar
    /// potential, and every shipped one leaves that scalar null. So asking the electrode
    /// what it holds returns zero, the decomposition gives it no supply, and the channel
    /// weights it at zero - which <see cref="RasteriseEdgeProfile"/> then multiplies
    /// through, so the board is not there at all. Silently: the solve converges, reports
    /// its other channels, and a mirror simply has no field.
    /// </para>
    /// <para>
    /// It never showed because the two templates with edge profiles declare no drive, and
    /// an undriven solve takes the <c>potentialOf is null</c> arm, where the scale is 1.0
    /// and the profile is used as written. The moment such a geometry declares a drive it
    /// goes through the channels and the board vanishes. A unit coefficient is the fix
    /// that fits the scheme: the pattern is normalized by its leading coefficient and the
    /// channel's weight is that leading value, so weight times normalized coefficient is
    /// exactly one however many other electrodes share the supply - which is precisely
    /// "apply my profile once, as written".
    /// </para>
    /// <para>
    /// A profile that is everywhere zero gets zero rather than one, so a grounded edge
    /// profile does not conjure a DC channel whose field is nothing.
    /// </para>
    /// </remarks>
    private static Excitation Excited(CompiledElectrode electrode) =>
        new(electrode.Name, DirectOf(electrode), [.. electrode.Taps.Select(
            t => new DriveTap(t.Drive, t.Amplitude, t.Phase))]);

    /// <summary>What an electrode holds in a mask built with <paramref name="potentialOf"/>.</summary>
    /// <remarks>
    /// The one definition <see cref="BuildMask"/> rasterizes with and
    /// <see cref="ChannelSolve.Holding"/> reports, so the two cannot disagree. With no
    /// override an edge profile is applied as written, a scale of one on the profile that
    /// holds its volts; with one, the override is that scale.
    /// </remarks>
    private static double Held(CompiledElectrode electrode, Func<CompiledElectrode, double>? potentialOf) =>
        potentialOf is not null ? potentialOf(electrode)
        : electrode.Shape == ElectrodeShape.EdgeProfile ? 1.0
        : electrode.Potential;

    /// <summary>A channel's pattern as the function a mask is built with.</summary>
    /// <remarks>
    /// The decomposition keys its patterns by name, which the validator makes unique within a
    /// solve and the decomposition itself refuses to see repeated.
    /// </remarks>
    private static Func<CompiledElectrode, double> Weighted(Dictionary<string, double> pattern) =>
        electrode => pattern.GetValueOrDefault(electrode.Name, 0.0);

    /// <summary>
    /// What an electrode contributes to the constant supply: its DC potential, or for an
    /// edge profile a unit scale on the profile that holds its volts.
    /// </summary>
    private static double DirectOf(CompiledElectrode electrode) =>
        electrode.Shape != ElectrodeShape.EdgeProfile
            ? electrode.Potential
            : electrode.Profile.Any(point => point.Potential != 0.0) ? 1.0 : 0.0;

    /// <summary>
    /// The waveform, frequency and quadrature flag of every generator a geometry
    /// declares, in declaration order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A geometry with stages and no drive still switches; it just switches between
    /// states that do not oscillate, and it gets <b>no clock at all</b>. It used to get a
    /// nominal one at one hertz, with the comment that nothing used its frequency. Two
    /// things did. The shortest period came out as one second rather than infinite, so
    /// the diffusive path's pseudopotential wrapper - gated on a finite period, because a
    /// ramp is not an oscillation - cycle-averaged a DC elution ramp over a one-second
    /// cycle, most of which is zero field, and a TIMS tunnel that measurably held its
    /// density for a millisecond let it all drift out at gas speed during a 300 us hold.
    /// And the regime report called the geometry a 1 Hz drive.
    /// </para>
    /// <para>
    /// The step cap the comment worried about needs no placeholder: an infinite period
    /// divided by the steps per period is an infinite cap, which is the cap a static
    /// field already has, and a stage boundary is landed on through NextSwitchAfter.
    /// </para>
    /// </remarks>
    private static (List<double> Frequencies, List<RfWaveform> Waveforms, List<bool> Quadrature)
        Clocks(IReadOnlyList<Core.Model.CompiledDrive> drives)
    {
        var frequencies = new List<double>(drives.Count);
        var waveforms = new List<RfWaveform>(drives.Count);
        var quadrature = new List<bool>(drives.Count);

        foreach (var drive in drives)
        {
            frequencies.Add(drive.FrequencyHz);

            waveforms.Add(drive.Waveform == DriveWaveform.Rectangular
                ? new RfWaveform.Rectangular(drive.DutyCycle)
                : new RfWaveform.Sinusoid());

            quadrature.Add(drive.Waveform != DriveWaveform.Rectangular);
        }

        return (frequencies, waveforms, quadrature);
    }

}
