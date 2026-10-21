using Einzel.Core.Model;

namespace Einzel.Fields.Solved;

/// <summary>A three-dimensional geometry to solve: a box, a mesh size, and electrodes.</summary>
/// <param name="MinX">Lower x, in metres.</param>
/// <param name="MinY">Lower y, in metres.</param>
/// <param name="MinZ">Lower z, in metres.</param>
/// <param name="MaxX">Upper x, in metres.</param>
/// <param name="MaxY">Upper y, in metres.</param>
/// <param name="MaxZ">Upper z, in metres.</param>
/// <param name="CellSize">Requested node spacing, in metres.</param>
/// <param name="Electrodes">The electrodes.</param>
/// <param name="Tolerance">Relative residual the solve must reach.</param>
public sealed record Geometry3D(
    double MinX,
    double MinY,
    double MinZ,
    double MaxX,
    double MaxY,
    double MaxZ,
    double CellSize,
    IReadOnlyList<CompiledElectrode3D> Electrodes,
    double Tolerance = 1e-10)
{
    /// <summary>The generators this geometry is operated with. Empty when static.</summary>
    public IReadOnlyList<Core.Model.CompiledDrive> Drives { get; init; } = [];

    /// <summary>The primary drive - the first declared - or null when static.</summary>
    public Core.Model.CompiledDrive? Drive => Drives.Count > 0 ? Drives[0] : null;

    /// <summary>The timed sequence it is operated through, or empty for one state.</summary>
    public IReadOnlyList<CompiledStage3D> Stages { get; init; } = [];

    /// <summary>What each face of the domain is, in the order lower/upper x, y, z.</summary>
    /// <remarks>
    /// <para>
    /// <b>Dirichlet by default, which is a grounded box and is a third electrode.</b> That
    /// is right for a device inside a housing and wrong for one whose geometry is invariant
    /// along an axis - a stripe electrode running the length of an analyser's drift makes
    /// the field independent of the drift direction, so grounding those faces imposes an
    /// axial field the real instrument does not have.
    /// </para>
    /// <para>
    /// A Neumann face is a mirror, so declaring both z faces Neumann says the structure
    /// repeats forever along z, which is what a stripe electrode means. The solver has
    /// always supported this; until now no document could ask for it.
    /// </para>
    /// </remarks>
    public IReadOnlyList<EdgeCondition> Faces { get; init; } = [];

    /// <summary>Where to mirror the solved half, in metres along x, or null for none.</summary>
    /// <remarks>
    /// <para>
    /// <b>Solve half an instrument and reflect it.</b> An analyser symmetric about a plane
    /// costs twice what it needs to: the far half is the near half seen backwards, and a
    /// volume solve is the one place where halving the domain halves the dominant cost. On a
    /// 3-D Astral, where the solve is 94 per cent of a run, that is the cheapest factor of
    /// two available.
    /// </para>
    /// <para>
    /// The plane path has had this from the beginning. It composes here unchanged because
    /// <see cref="ReflectedField"/> mirrors a coordinate of any field rather than knowing
    /// anything about how that field was meshed - so the two halves are identical by
    /// construction and no difference between them can come from their having been
    /// discretised differently.
    /// </para>
    /// <para>
    /// Declare the mid-plane face Neumann alongside it. A mirror plane is a symmetry plane,
    /// and grounding it instead puts a conductor down the middle of the instrument.
    /// </para>
    /// </remarks>
    public double? ReflectAboutX { get; init; }

    /// <summary>The six faces a compiled solve declares, in this type's own terms.</summary>
    /// <param name="declared">The compiled faces, or empty for a grounded box.</param>
    /// <returns>Six conditions, or empty.</returns>
    /// <remarks>
    /// One conversion in one place: <c>Einzel.Core</c> cannot name this assembly's
    /// <see cref="EdgeCondition"/>, and two enums that must agree are exactly the pair that
    /// stops agreeing. Every construction site calls this rather than mapping it again.
    /// <para>
    /// There were briefly three of these enums, the third being a Core-side one invented for
    /// the volume path alone - although <see cref="Core.Model.BoundaryKind"/> already meant
    /// dirichlet-or-neumann for the plane path. Two is the irreducible number, because an
    /// assembly boundary sits between them; three was a duplicate.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<EdgeCondition> FacesOf(
        IReadOnlyList<Core.Model.BoundaryKind> declared)
    {
        ArgumentNullException.ThrowIfNull(declared);

        if (declared.Count != 6)
        {
            return [];
        }

        var faces = new EdgeCondition[6];

        for (var face = 0; face < 6; face++)
        {
            faces[face] = declared[face] == Core.Model.BoundaryKind.Neumann
                ? EdgeCondition.Neumann
                : EdgeCondition.Dirichlet;
        }

        return faces;
    }

    /// <summary>The geometry a compiled volume element is solved as.</summary>
    /// <param name="solve">The compiled element.</param>
    /// <returns>The geometry, with its drives, stages, faces and mirror plane.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="solve"/> is null.</exception>
    /// <remarks>
    /// Every caller builds it here - the run, <c>einzel solve</c>, <c>export</c>,
    /// <c>estimate</c> and <see cref="SolveHash"/> - because they must agree: a key taken
    /// over a geometry the solve was not handed would name a field nobody computed, and a
    /// copy made field by field drops whatever field was added last.
    /// </remarks>
    public static Geometry3D Of(Core.Model.CompiledSolvedField3D solve)
    {
        ArgumentNullException.ThrowIfNull(solve);

        return new Geometry3D(
            solve.MinX, solve.MinY, solve.MinZ,
            solve.MaxX, solve.MaxY, solve.MaxZ,
            solve.CellSize,
            solve.Electrodes,
            solve.Tolerance)
        {
            Drives = solve.Drives,
            Stages = solve.Stages,
            Faces = FacesOf(solve.Faces),
            ReflectAboutX = solve.ReflectAboutX,
        };
    }
}


/// <summary>Whether a volume mask asks each electrode only about the nodes near it.</summary>
/// <remarks>
/// CMP-1 requires that a reference implementation is never deleted or allowed to rot,
/// and a reference nothing can select is one that rots quietly. The unculled loops are
/// what the culled ones are checked against, node for node and arm for arm, on every
/// shipped volume geometry - so they are kept as written and kept reachable.
/// </remarks>
internal enum MaskCulling
{
    /// <summary>Each electrode is asked only about the nodes and links its bounding box reaches.</summary>
    Bounds,

    /// <summary>Every electrode is asked about every node and every link: the reference.</summary>
    None,
}

/// <summary>Builds and solves a three-dimensional geometry.</summary>
public static class GeometryBuilder3D
{
    /// <summary>Builds the grid a geometry asks for.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <returns>The grid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is null.</exception>
    public static Grid3D BuildGrid(Geometry3D geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        return Grid3D.OverBox(
            geometry.MinX, geometry.MinY, geometry.MinZ,
            geometry.MaxX, geometry.MaxY, geometry.MaxZ,
            geometry.CellSize);
    }

    /// <summary>What building a geometry will hold in memory, worked out without building it.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <returns>The node and channel counts, which hierarchy the solve takes, and the bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Arithmetic on the declaration: the grid is the box and the cell size, the channels are
    /// the same decomposition <see cref="BuildField"/> makes, and the hierarchy is asked of
    /// <see cref="PoissonSolver3D.UsesGalerkin"/> rather than decided a second time. Nothing
    /// is rasterized, which is what lets <c>einzel estimate</c> state the memory of a solve too
    /// large to attempt.
    /// </para>
    /// <para>
    /// <b>One inexactness, in the safe direction.</b> The solver asks whether any electrode
    /// fixes a node away from the faces, which only rasterizing can answer; this assumes it
    /// does whenever there are electrodes. A geometry whose every conductor is thinner than a
    /// cell, or lies on a face, then reads as taking the Galerkin hierarchy when it will not,
    /// and is costed at 149.5 bytes a node where it needs about 51.
    /// </para>
    /// </remarks>
    public static SolveMemory Memory(Geometry3D geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        var grid = BuildGrid(geometry);

        var channels = geometry.Drives.Count == 0 && geometry.Stages.Count == 0
            ? 1
            : Groups(geometry).Count;

        // The mask records this same minimum, over every electrode whatever it holds.
        var smallest = double.PositiveInfinity;

        foreach (var electrode in geometry.Electrodes)
        {
            smallest = Math.Min(smallest, electrode.CharacteristicSize);
        }

        var interior = geometry.Electrodes.Count > 0;
        var galerkin = PoissonSolver3D.UsesGalerkin(grid, smallest, interior);

        // At the sizes this grid's coarse levels really are: an eighth of the fine grid only
        // on a grid many cells deep on every axis.
        var working = galerkin
            ? SolveMemoryModel.VolumeGalerkinPeakOn(grid)
            : SolveMemoryModel.VolumeRediscretizedPeakOn(
                grid, PoissonSolver3D.RediscretizedLevels(grid, smallest, interior));

        return SolveMemoryModel.For(grid.NodeCount, Math.Max(1, channels), working, galerkin);
    }

    /// <summary>Builds the Dirichlet mask on the finest grid.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="grid">The grid.</param>
    /// <param name="potentialOf">
    /// What each electrode holds, for a basis solve. Its own potential when omitted.
    /// </param>
    /// <returns>The mask.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <remarks>
    /// <para>
    /// Sub-cell: conductor surfaces between nodes are recorded as cut links, which
    /// is where the accuracy of a solve comes from. Coarse multigrid levels are
    /// built by <see cref="Coarsener(Geometry3D, Func{CompiledElectrode3D, double}?)"/>
    /// instead and are deliberately different.
    /// </para>
    /// <para>
    /// <b>Each electrode is asked only about the nodes its bounding box reaches.</b>
    /// Asking every electrode about every node and every link costs nodes times
    /// electrodes, and on the Astral's foil solve - 1.1 million nodes, 86 electrodes -
    /// that was half a billion closed-form entry tests per mask and half of a whole
    /// <c>einzel solve</c> of the template - 10.3 s of 20.6 in Release, where the culled
    /// mask takes 0.15 s. The mask is bit-identical to the unculled one on every shipped
    /// volume geometry, checked node for node and arm for arm against the reference
    /// loops.
    /// </para>
    /// </remarks>
    public static DirichletMask3D BuildMask(
        Geometry3D geometry, Grid3D grid, Func<CompiledElectrode3D, double>? potentialOf = null) =>
        BuildMask(geometry, grid, potentialOf, MaskCulling.Bounds);

    /// <summary>Builds the finest-level mask, culled or by the reference loops.</summary>
    internal static DirichletMask3D BuildMask(
        Geometry3D geometry,
        Grid3D grid,
        Func<CompiledElectrode3D, double>? potentialOf,
        MaskCulling culling) =>
        Assemble(geometry, grid, potentialOf, coarse: false, culling);

    /// <summary>
    /// A builder for the coarse levels of a multigrid hierarchy, memoised by grid.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="potentialOf">
    /// What each electrode holds, for a basis solve. Its own potential when omitted.
    /// </param>
    /// <returns>A function from a coarse grid to its mask.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Handed out as a whole function rather than as a flag on
    /// <see cref="BuildMask(Geometry3D, Grid3D, Func{CompiledElectrode3D, double}?)"/>
    /// because the difference between the two is load-bearing and the obvious
    /// spelling has to be the safe one. A coarse level built the fine way is not
    /// slightly worse - it is ill-conditioned, and the solve converges contentedly
    /// somewhere else.
    /// </para>
    /// <para>
    /// Coarse levels are node-aligned: no cuts, and an electrode too small to hold
    /// a node is pinned to its nearest free one so it does not drop out of the
    /// problem. The values are irrelevant on these levels - a V-cycle solves for the
    /// error, whose Dirichlet data is zero - so only the pattern of fixed nodes
    /// matters, and the pattern is what has to stay recognisable.
    /// </para>
    /// <para>
    /// Memoised because the hierarchy is rebuilt on every cycle otherwise: for the
    /// twelve-rod segmented quadrupole that is over a million <c>Contains</c> calls
    /// per cycle, producing a mask that is identical every time.
    /// </para>
    /// </remarks>
    public static Func<Grid3D, DirichletMask3D> Coarsener(
        Geometry3D geometry, Func<CompiledElectrode3D, double>? potentialOf = null) =>
        Coarsener(geometry, potentialOf, MaskCulling.Bounds);

    /// <summary>The coarse-level builder, culled or by the reference loops.</summary>
    internal static Func<Grid3D, DirichletMask3D> Coarsener(
        Geometry3D geometry, Func<CompiledElectrode3D, double>? potentialOf, MaskCulling culling)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        var cache = new Dictionary<(int X, int Y, int Z), DirichletMask3D>();

        return grid =>
        {
            var key = (grid.CountX, grid.CountY, grid.CountZ);

            if (!cache.TryGetValue(key, out var mask))
            {
                mask = Assemble(geometry, grid, potentialOf, coarse: true, culling);
                cache[key] = mask;
            }

            return mask;
        };
    }

    /// <summary>Mirrors a solved half about the declared plane, or returns it unchanged.</summary>
    private static IElectrostaticField Reflected(IElectrostaticField field, Geometry3D geometry) =>
        geometry.ReflectAboutX is { } plane
            ? new SuperposedField([field, new ReflectedField(field, plane)])
            : field;

    private static DirichletMask3D Assemble(
        Geometry3D geometry,
        Grid3D grid,
        Func<CompiledElectrode3D, double>? potentialOf,
        bool coarse,
        MaskCulling culling)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(grid);

        var mask = new DirichletMask3D(grid);

        // Where each electrode can be, in node indices, or null for the reference loops.
        var reach = culling == MaskCulling.Bounds ? Reach(geometry.Electrodes, grid) : null;

        if (geometry.Faces.Count == 6)
        {
            mask.LowerX = geometry.Faces[0];
            mask.UpperX = geometry.Faces[1];
            mask.LowerY = geometry.Faces[2];
            mask.UpperY = geometry.Faces[3];
            mask.LowerZ = geometry.Faces[4];
            mask.UpperZ = geometry.Faces[5];
        }

        List<CompiledElectrode3D>? missed = null;

        for (var index = 0; index < geometry.Electrodes.Count; index++)
        {
            var electrode = geometry.Electrodes[index];
            var potential = potentialOf?.Invoke(electrode) ?? electrode.Potential;

            var found = reach is null
                ? Rasterise(mask, grid, electrode, potential)
                : Rasterise(mask, grid, electrode, potential, reach[index]);

            if (!found && coarse)
            {
                (missed ??= []).Add(electrode);
            }
        }

        PinFaces(mask, grid);

        // After the faces, so a pin can never overwrite a grounded boundary, and
        // only onto a free node, so two electrodes whose centres round together
        // leave one of them present rather than both of them confused. An electrode
        // that rasterises to nothing has stopped being part of the problem and the
        // coarse grid then solves a different one; keeping it at the smallest size
        // the level can express is the least-wrong thing available.
        foreach (var electrode in missed ?? [])
        {
            var (cx, cy, cz) = electrode.Centre;

            var i = Math.Clamp((int)Math.Round((cx - grid.OriginX) / grid.SpacingX), 0, grid.CountX - 1);
            var j = Math.Clamp((int)Math.Round((cy - grid.OriginY) / grid.SpacingY), 0, grid.CountY - 1);
            var k = Math.Clamp((int)Math.Round((cz - grid.OriginZ) / grid.SpacingZ), 0, grid.CountZ - 1);

            if (!mask.IsFixed(i, j, k))
            {
                mask.Fix(i, j, k, potentialOf?.Invoke(electrode) ?? electrode.Potential);
            }
        }

        // Sub-cell surfaces on the fine level only. A coarse level exists to
        // accelerate, not to be accurate, and a cut there is actively harmful: an
        // electrode a fraction of a coarse cell across produces arms a thousandth
        // of a cell long, whose coefficients are enormous, and the correction that
        // comes back does not converge slowly - it converges somewhere else.
        //
        // Node-aligned geometry on the coarse levels is cruder and stable. The
        // accuracy still comes from the fine level, which is unchanged.
        if (!coarse)
        {
            if (reach is null)
            {
                AddCuts(geometry, grid, mask, potentialOf);
            }
            else
            {
                AddCuts(geometry, grid, mask, potentialOf, reach);
            }
        }

        foreach (var electrode in geometry.Electrodes)
        {
            mask.SmallestFeature = Math.Min(mask.SmallestFeature, electrode.CharacteristicSize);
        }

        return mask;
    }

    /// <summary>Builds, solves, and wraps a geometry as a field.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <returns>The field, and how the solve went.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is null.</exception>
    public static (SolvedField3D Field, SolveReport Report) Build(Geometry3D geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        var grid = BuildGrid(geometry);
        var mask = BuildMask(geometry, grid);

        var (potential, report) = PoissonSolver3D.Solve(
            mask,
            geometry.Tolerance,
            maximumCycles: 200,
            coarsen: Coarsener(geometry));

        return (
            new SolvedField3D(potential, geometry.Electrodes),
            report with { Warnings = SharedFaceNodes.Warnings(NodesOnSharedFaces(geometry, grid, mask)) });
    }

    /// <summary>
    /// Finds where a mesh samples a face two conductors share while holding different
    /// excitations - a node on it, or a stencil arm that first meets metal on it - so that
    /// which conductor the sample is credited to is decided by rounding.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="grid">The finest grid it is solved on.</param>
    /// <param name="mask">
    /// Any finest-level mask of this geometry on this grid, for which nodes are fixed; asked of
    /// the geometry node by node when omitted. Every channel's mask fixes the same nodes - an
    /// electrode's nodes are fixed whatever its weight in the channel - so whichever the
    /// caller has will do.
    /// </param>
    /// <returns>What was found, or null when the mesh samples no such face.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <remarks>
    /// <para>
    /// A mesh samples the geometry in two places, and each gets its own test. A <b>node</b>
    /// is on a surface when its signed distance is within
    /// <see cref="SharedFaceNodes.ToleranceFraction"/> of the finest spacing. An <b>arm</b>
    /// from a free node samples a face when the point where it first meets one conductor is
    /// within the same distance of the other's surface, and nothing is nearer along it - that
    /// is the entry the cut link records, and which of the two conductors it records is then
    /// whichever the rounding put in front.
    /// </para>
    /// <para>
    /// <b>The arms are the case that mattered.</b> <c>astral-3d</c>'s foil stripes are thinner
    /// than a cell and hold no node at all, so with its mesh shift removed the node test finds
    /// nothing; the coin was tossed on the arms lying in the plane of each shared face.
    /// </para>
    /// <para>
    /// A pair counts when it disagrees in <em>any</em> state the instrument has - asked
    /// through <see cref="ElectrodeOverlap3D.StatesOf"/> and
    /// <see cref="ElectrodeOverlap3D.Agrees"/>, the overlap check's own functions, so the two
    /// questions asked of a pair cannot come to disagree about what "different excitations"
    /// means. A pair that agrees everywhere shares a face harmlessly: either answer to the
    /// coin toss is the same potential.
    /// </para>
    /// <para>
    /// <b>Only nodes near both bounding boxes are visited</b>: a point on both surfaces is in
    /// both boxes, and an arm's entry is within one cell of its node, so the box is widened by
    /// a cell and nothing further. For abutting conductors that is a slab three nodes thick
    /// round the shared face, and a node is let through to the arm test only when it is within
    /// a cell of both surfaces - so the cost is a face's worth of signed distances per
    /// touching pair, on a geometry whose rasterization makes a pass over the whole grid per
    /// electrode.
    /// </para>
    /// <para>
    /// Only the finest grid. The coarse levels exist to accelerate, their Dirichlet values
    /// are not used, and the answer is decided where the cut cells are.
    /// </para>
    /// <para>
    /// <b>A grounded face of the domain counts as a conductor at zero volts</b>, and nodes on
    /// it lying on an electrode's surface are counted apart, as
    /// <see cref="SharedFaceNodes.BoundaryNodes"/>. A Neumann face is a mirror and takes no part.
    /// </para>
    /// </remarks>
    public static SharedFaceNodes? NodesOnSharedFaces(
        Geometry3D geometry, Grid3D grid, DirichletMask3D? mask = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(grid);

        var electrodes = geometry.Electrodes;
        var states = ElectrodeOverlap3D.StatesOf(electrodes, geometry.Stages);
        var tolerance = SharedFaceNodes.ToleranceFraction * grid.MinimumSpacing;
        var reach = Math.Max(grid.SpacingX, Math.Max(grid.SpacingY, grid.SpacingZ));

        var nodes = new HashSet<long>();
        var arms = new HashSet<long>();
        (string First, string Second, double X, double Y, double Z)? nodeExample = null;
        (string First, string Second, double X, double Y, double Z)? armExample = null;

        for (var i = 0; i < electrodes.Count; i++)
        {
            for (var j = i + 1; j < electrodes.Count; j++)
            {
                var (i1, i2) = (i, j);

                if (states.All(state => ElectrodeOverlap3D.Agrees(state[i1], state[i2])))
                {
                    continue;
                }

                var a = electrodes[i];
                var b = electrodes[j];

                var (aMinX, aMinY, aMinZ, aMaxX, aMaxY, aMaxZ) = a.Bounds;
                var (bMinX, bMinY, bMinZ, bMaxX, bMaxY, bMaxZ) = b.Bounds;

                var x0 = Math.Max(aMinX, bMinX) - tolerance;
                var x1 = Math.Min(aMaxX, bMaxX) + tolerance;
                var y0 = Math.Max(aMinY, bMinY) - tolerance;
                var y1 = Math.Min(aMaxY, bMaxY) + tolerance;
                var z0 = Math.Max(aMinZ, bMinZ) - tolerance;
                var z1 = Math.Min(aMaxZ, bMaxZ) + tolerance;

                // Boxes that do not meet cannot share a surface, and that settles almost
                // every pair - on the Astral, all but a few dozen of three thousand.
                if (x1 < x0 || y1 < y0 || z1 < z0)
                {
                    continue;
                }

                var (iLo, iHi) = SharedFaceNodes.Range(grid.OriginX, grid.SpacingX, grid.CountX, x0 - reach, x1 + reach);
                var (jLo, jHi) = SharedFaceNodes.Range(grid.OriginY, grid.SpacingY, grid.CountY, y0 - reach, y1 + reach);
                var (kLo, kHi) = SharedFaceNodes.Range(grid.OriginZ, grid.SpacingZ, grid.CountZ, z0 - reach, z1 + reach);

                for (var k = kLo; k <= kHi; k++)
                {
                    var z = grid.Z(k);

                    for (var jj = jLo; jj <= jHi; jj++)
                    {
                        var y = grid.Y(jj);

                        for (var ii = iLo; ii <= iHi; ii++)
                        {
                            var x = grid.X(ii);

                            // The same coordinates the rasterizer and the cut links use,
                            // which is the point: the coin is tossed in this arithmetic.
                            var da = Math.Abs(a.SignedDistance(x, y, z));

                            if (da > reach + tolerance)
                            {
                                continue;
                            }

                            var db = Math.Abs(b.SignedDistance(x, y, z));

                            if (db > reach + tolerance)
                            {
                                continue;
                            }

                            var node = ((((long)k * grid.CountY) + jj) * grid.CountX) + ii;

                            if (da <= tolerance && db <= tolerance)
                            {
                                if (nodes.Add(node))
                                {
                                    nodeExample ??= (a.Name, b.Name, x, y, z);
                                }

                                continue;
                            }

                            for (var arm = 0; arm < 6; arm++)
                            {
                                var (di, dj, dk) = Arms[arm];
                                var (ni, nj, nk) = (ii + di, jj + dj, k + dk);

                                if (ni < 0 || nj < 0 || nk < 0
                                    || ni >= grid.CountX || nj >= grid.CountY || nk >= grid.CountZ)
                                {
                                    continue;
                                }

                                var (tx, ty, tz) = (grid.X(ni), grid.Y(nj), grid.Z(nk));

                                // Where the arm meets one of them, if that point is on the other's
                                // surface too - the nearer such point, when both qualify.
                                var entry = double.PositiveInfinity;

                                if (SharedFaceNodes.Interior(a.FirstEntry(x, y, z, tx, ty, tz), out var ea)
                                    && Math.Abs(b.SignedDistance(
                                        x + (ea * (tx - x)), y + (ea * (ty - y)), z + (ea * (tz - z)))) <= tolerance)
                                {
                                    entry = ea;
                                }

                                if (SharedFaceNodes.Interior(b.FirstEntry(x, y, z, tx, ty, tz), out var eb)
                                    && Math.Abs(a.SignedDistance(
                                        x + (eb * (tx - x)), y + (eb * (ty - y)), z + (eb * (tz - z)))) <= tolerance)
                                {
                                    entry = Math.Min(entry, eb);
                                }

                                if (double.IsPositiveInfinity(entry))
                                {
                                    continue;
                                }

                                // A fixed node has no stencil to cut. Asked of the mask when the
                                // caller has one, and otherwise of the geometry the way the finest
                                // mask is built - which costs one pass over the electrodes for a
                                // candidate, where building the mask costs a pass over the grid
                                // for every electrode: on astral-3d, minutes.
                                if (mask?.IsFixed(ii, jj, k) ?? IsFixedFine(geometry, grid, ii, jj, k))
                                {
                                    break;
                                }

                                if (Shadowed(electrodes, x, y, z, tx, ty, tz, entry))
                                {
                                    continue;
                                }

                                if (arms.Add((node * 6) + arm))
                                {
                                    armExample ??= (
                                        a.Name, b.Name,
                                        x + (entry * (tx - x)), y + (entry * (ty - y)), z + (entry * (tz - z)));
                                }
                            }
                        }
                    }
                }
            }
        }

        var (boundary, boundaryExample) = NodesOnGroundedFaces(geometry, grid, states, tolerance);

        if ((nodeExample ?? armExample) is { } first)
        {
            return new SharedFaceNodes(
                nodes.Count, arms.Count, first.First, first.Second, first.X, first.Y, first.Z,
                ExampleIsArm: nodeExample is null)
            {
                BoundaryNodes = boundary,
            };
        }

        return boundaryExample is { } face
            ? new SharedFaceNodes(0, 0, face.First, face.Face, face.X, face.Y, face.Z, ExampleIsArm: false)
            {
                BoundaryNodes = boundary,
                ExampleIsBoundary = true,
            }
            : null;
    }

    /// <summary>
    /// Nodes on a grounded face of the domain that lie on the surface of an electrode holding
    /// something other than zero volts in some state, so that whether the electrode or the face
    /// claims them is decided by rounding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The grounded boundary is a third conductor at zero volts: <see cref="PinFaces"/> fixes a
    /// face node to zero unless an electrode has claimed it first, and an electrode claims it by
    /// <c>Contains</c> - signed distance at most zero - against the node's coordinate, origin plus
    /// index times spacing, which need not equal the declared domain bound. So a node on the face
    /// that is on the electrode's surface holds the electrode's potential or zero on the last bit.
    /// </para>
    /// <para>
    /// Nodes only, for the reason the plane gives: a face node is fixed either way and the face
    /// is never a cut target. "Holds something other than zero" is the overlap check's reading,
    /// asked of the electrode against itself stripped of its excitation. A Neumann face is a
    /// mirror and takes no part - <c>astral-3d</c>'s earthed boards end on its Neumann z faces
    /// and flip between fixed and free at zero volts, which is an ordinary cut cell - and neither
    /// does a zero-volt electrode against a grounded face, since either answer is zero.
    /// </para>
    /// <para>
    /// <b>A face lying in a mirror plane is not a surface</b>, since by symmetry the conductor
    /// continues across it: a stripe running the length of a drift between two Neumann faces has
    /// no ends. So a node where a grounded face meets a Neumann one is asked about a point just
    /// inside the mirror, which keeps a face crossing the mirror and drops one lying in it.
    /// </para>
    /// </remarks>
    private static (int Count, (string First, string Face, double X, double Y, double Z)? Example) NodesOnGroundedFaces(
        Geometry3D geometry,
        Grid3D grid,
        IReadOnlyList<IReadOnlyList<CompiledElectrode3D>> states,
        double tolerance)
    {
        var electrodes = geometry.Electrodes;

        // The same rule the mask and IsFixedFine apply: six declared faces, or a grounded box.
        bool Dirichlet(int face) =>
            geometry.Faces.Count != 6 || geometry.Faces[face] == EdgeCondition.Dirichlet;

        // Ten tolerances into the domain from a mirror face: clear of a face lying in it, and
        // still on any face crossing it.
        var lift = 10.0 * tolerance;

        double Asked(int index, int count, int lower, double at) =>
            at
            + (index == 0 && !Dirichlet(lower) ? lift : 0.0)
            - (index == count - 1 && !Dirichlet(lower + 1) ? lift : 0.0);

        // Each grounded face as the plane of nodes it is: which axis it is normal to, which
        // index along that axis is held.
        var faces = new List<(string Name, int Axis, int Plane)>(6);
        string[] names = ["lower x face", "upper x face", "lower y face", "upper y face", "lower z face", "upper z face"];
        int[] counts = [grid.CountX, grid.CountY, grid.CountZ];

        for (var face = 0; face < 6; face++)
        {
            if (Dirichlet(face))
            {
                faces.Add((names[face], face / 2, face % 2 == 0 ? 0 : counts[face / 2] - 1));
            }
        }

        var found = new HashSet<long>();
        (string First, string Face, double X, double Y, double Z)? example = null;

        for (var e = 0; e < electrodes.Count; e++)
        {
            var index = e;

            if (states.All(state => ElectrodeOverlap3D.Agrees(
                    state[index], state[index] with { Potential = 0.0, Taps = [] })))
            {
                continue;
            }

            var electrode = electrodes[e];
            var (minX, minY, minZ, maxX, maxY, maxZ) = electrode.Bounds;
            double[] min = [minX, minY, minZ];
            double[] max = [maxX, maxY, maxZ];

            foreach (var (name, axis, plane) in faces)
            {
                // The plane of nodes' own coordinate, not the declared bound, since the plane
                // of nodes is what gets pinned.
                var across = axis switch
                {
                    0 => grid.X(plane),
                    1 => grid.Y(plane),
                    _ => grid.Z(plane),
                };

                if (across < min[axis] - tolerance || across > max[axis] + tolerance)
                {
                    continue;
                }

                var (iLo, iHi) = axis == 0
                    ? (plane, plane)
                    : SharedFaceNodes.Range(grid.OriginX, grid.SpacingX, grid.CountX, minX - tolerance, maxX + tolerance);
                var (jLo, jHi) = axis == 1
                    ? (plane, plane)
                    : SharedFaceNodes.Range(grid.OriginY, grid.SpacingY, grid.CountY, minY - tolerance, maxY + tolerance);
                var (kLo, kHi) = axis == 2
                    ? (plane, plane)
                    : SharedFaceNodes.Range(grid.OriginZ, grid.SpacingZ, grid.CountZ, minZ - tolerance, maxZ + tolerance);

                for (var k = kLo; k <= kHi; k++)
                {
                    for (var j = jLo; j <= jHi; j++)
                    {
                        for (var i = iLo; i <= iHi; i++)
                        {
                            var (x, y, z) = (grid.X(i), grid.Y(j), grid.Z(k));

                            var distance = electrode.SignedDistance(
                                Asked(i, grid.CountX, 0, x), Asked(j, grid.CountY, 2, y), Asked(k, grid.CountZ, 4, z));

                            if (Math.Abs(distance) > tolerance)
                            {
                                continue;
                            }

                            if (found.Add(((((long)k * grid.CountY) + j) * grid.CountX) + i))
                            {
                                example ??= (electrode.Name, name, x, y, z);
                            }
                        }
                    }
                }
            }
        }

        return (found.Count, example);
    }

    /// <summary>
    /// Whether the finest mask would fix a node: inside a conductor, or on a Dirichlet face.
    /// </summary>
    /// <remarks>
    /// The same two rules <see cref="Assemble"/> applies on the finest level -
    /// <c>Rasterise</c> by <c>Contains</c>, culled or reference alike, then
    /// <see cref="PinFaces"/> - asked of one node. The coarse-level pinning of an electrode too small to hold a node does not
    /// happen on the finest level, so it has no part here.
    /// </remarks>
    private static bool IsFixedFine(Geometry3D geometry, Grid3D grid, int i, int j, int k)
    {
        bool Dirichlet(int face) =>
            geometry.Faces.Count != 6 || geometry.Faces[face] == EdgeCondition.Dirichlet;

        if ((i == 0 && Dirichlet(0)) || (i == grid.CountX - 1 && Dirichlet(1))
            || (j == 0 && Dirichlet(2)) || (j == grid.CountY - 1 && Dirichlet(3))
            || (k == 0 && Dirichlet(4)) || (k == grid.CountZ - 1 && Dirichlet(5)))
        {
            return true;
        }

        var (x, y, z) = (grid.X(i), grid.Y(j), grid.Z(k));

        return geometry.Electrodes.Any(electrode => electrode.Contains(x, y, z));
    }

    /// <summary>The six stencil arms, in the order both <c>AddCuts</c> overloads visit them.</summary>
    private static readonly (int Di, int Dj, int Dk)[] Arms =
        [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

    /// <summary>
    /// Whether some conductor meets the arm clearly before <paramref name="entry"/>, so the
    /// cut records that one and a tie at <paramref name="entry"/> is in its shadow.
    /// </summary>
    private static bool Shadowed(
        IReadOnlyList<CompiledElectrode3D> electrodes,
        double fromX, double fromY, double fromZ, double toX, double toY, double toZ, double entry)
    {
        foreach (var electrode in electrodes)
        {
            if (electrode.FirstEntry(fromX, fromY, fromZ, toX, toY, toZ) is { } nearer
                && nearer > 0.0
                && nearer < entry - SharedFaceNodes.ToleranceFraction)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds a geometry as a field, driven or sequenced if it declares either.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <returns>The field, and the worst of the basis solves.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is null.</exception>
    /// <exception cref="Core.Errors.EinzelException">
    /// The mesh has more nodes than a volume solve may hold; see
    /// <see cref="Core.Errors.ErrorCodes.GridTooLarge"/>. Refused before anything is allocated.
    /// </exception>
    /// <remarks>
    /// The channel decomposition is the same code the plane uses, because nothing
    /// about it is dimensional: what makes a channel a channel is how the electrodes
    /// are wired, not where they are. A segmented quadrupole with three sections at
    /// different working points is three patterns, whatever the mesh is.
    /// </remarks>
    public static (IElectrostaticField Field, SolveReport Report) BuildField(Geometry3D geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        // At the entry, not only in the allocators. Each per-node allocator also refuses, but
        // that holds only while the mask happens to be the first thing a solve allocates: a
        // stencil or a scratch buffer moved ahead of it would be gigabytes spent before the
        // refusal again. Asked here, the order inside the solve stops mattering.
        BuildGrid(geometry).ThrowIfTooLargeToSolve();

        if (geometry.Drives.Count == 0 && geometry.Stages.Count == 0)
        {
            var (statik, staticReport) = Build(geometry);
            return (Reflected(statik, geometry), staticReport);
        }

        var groups = Groups(geometry);
        var solves = SolveGroups(geometry, groups);

        // Once per geometry rather than per channel: which nodes sit on a shared face is a
        // property of the mesh and the conductors, and every channel solves on both. The
        // first channel's solve asks it while its mask is still in hand; see SolveGroup.
        var meshWarnings = solves.Count > 0 ? solves[0].Report.Warnings : SharedFaceNodes.Warnings(null);

        var channels = new List<IElectrostaticField>(groups.Count);
        var direct = new List<double>(groups.Count);
        var harmonics = new List<IReadOnlyList<WeightTerm>>(groups.Count);

        SolveReport worst = new(true, 0, 0.0, 0.0, 0.0);

        for (var index = 0; index < groups.Count; index++)
        {
            // Reflected per channel rather than once over the composition, matching the
            // plane path: superposition is linear either way, and mirroring each basis
            // field keeps the drive weights attached to the thing they weight.
            channels.Add(Reflected(
                new SolvedField3D(solves[index].Potential, geometry.Electrodes), geometry));
            direct.Add(groups[index].Direct);
            harmonics.Add(groups[index].Harmonics);

            if (solves[index].Report.Cycles > worst.Cycles)
            {
                worst = solves[index].Report;
            }
        }

        var (frequencies, waveforms, quadrature) = Clocks(geometry.Drives);

        worst = worst with { Warnings = meshWarnings };

        if (geometry.Stages.Count == 0)
        {
            return (
                new DrivenSolvedField(channels, direct, harmonics, frequencies, waveforms), worst);
        }

        var boundaries = new List<double>(geometry.Stages.Count);
        var stageDirect = new List<IReadOnlyList<double>>(geometry.Stages.Count);
        var stageHarmonics =
            new List<IReadOnlyList<IReadOnlyList<WeightTerm>>>(geometry.Stages.Count);
        var stageEndDirect = new List<IReadOnlyList<double>?>(geometry.Stages.Count);
        var stageEndHarmonics = new List<IReadOnlyList<IReadOnlyList<WeightTerm>>?>(geometry.Stages.Count);
        var anyRamp = false;

        var elapsed = 0.0;

        foreach (var stage in geometry.Stages)
        {
            elapsed += stage.DurationSeconds;
            boundaries.Add(elapsed);

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

            // A ramp: weighed at both ends, the oscillating terms aligned so every start
            // term has its end partner, exactly as the plane path does it.
            anyRamp = true;
            var endWeights = DriveChannels.Weigh(
                groups, [.. stage.EndElectrodes.Select(Excited)], quadrature);
            var alignedStart = new List<IReadOnlyList<WeightTerm>>(groups.Count);
            var alignedEnd = new List<IReadOnlyList<WeightTerm>>(groups.Count);
            for (var k = 0; k < groups.Count; k++)
            {
                var (a, b) = GeometryBuilder.Align(weights.Harmonics[k], endWeights.Harmonics[k]);
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

    /// <summary>One basis channel of a solve, with the evidence that solve produced.</summary>
    /// <param name="Index">Which channel, in the order the decomposition found them.</param>
    /// <param name="FixedNodes">How many finest-level nodes the channel's mask held fixed.</param>
    /// <param name="CutLinks">How many stencil arms a conductor surface cut.</param>
    /// <param name="Potential">The solved potential.</param>
    /// <param name="Report">How the solve went.</param>
    /// <remarks>
    /// <b>The mask's two counts rather than the mask.</b> This carried the finest-level
    /// mask itself, for <c>einzel solve</c> to read those two numbers off, and a list of
    /// these is built channel by channel - so every channel already solved kept its mask
    /// and its cut links alive while the next one was solving. That is 105 bytes a node a
    /// channel, against the 8 the solved potential needs, and on a driven volume geometry
    /// it was most of the difference between one solve's working set and the whole build's.
    /// </remarks>
    public sealed record ChannelSolve(
        int Index, int FixedNodes, int CutLinks, ScalarField3D Potential, SolveReport Report)
    {
        /// <summary>
        /// What each declared electrode held in this solve, in volts and in declaration order.
        /// </summary>
        /// <remarks>
        /// The declared potentials for a static geometry, and for a driven or sequenced one
        /// the channel's spatial pattern with its leading electrode at one volt. By position
        /// rather than by name, for the reason the two-dimensional
        /// <see cref="GeometryBuilder.ChannelSolve.Holding"/> gives.
        /// </remarks>
        public IReadOnlyList<double> Holding { get; init; } = [];
    }

    /// <summary>Solves a geometry channel by channel, and hands back the diagnostics.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <returns>One entry per basis channel; exactly one for a static geometry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is null.</exception>
    /// <remarks>
    /// What <c>einzel solve</c> reports against. A driven structure is not one solve
    /// but one per spatial pattern, and a residual quoted for "the field" would be
    /// quoting whichever of them happened to be last.
    /// </remarks>
    public static IReadOnlyList<ChannelSolve> SolveChannels(Geometry3D geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        if (geometry.Drives.Count == 0 && geometry.Stages.Count == 0)
        {
            var grid = BuildGrid(geometry);
            var mask = BuildMask(geometry, grid);

            var (potential, report) = PoissonSolver3D.Solve(
                mask, geometry.Tolerance, maximumCycles: 200, coarsen: Coarsener(geometry));

            return
            [
                new ChannelSolve(
                    0, mask.FixedCount, mask.Cuts?.CutCount ?? 0, potential,
                    report with { Warnings = SharedFaceNodes.Warnings(NodesOnSharedFaces(geometry, grid, mask)) })
                {
                    Holding = StaticHolding(geometry),
                },
            ];
        }

        // Every channel carries the same finding, because every channel solves on the same
        // mesh round the same conductors; einzel solve reports it once per element.
        var solves = SolveGroups(geometry, Groups(geometry));
        var warnings = solves.Count > 0 ? solves[0].Report.Warnings : SharedFaceNodes.Warnings(null);

        return [.. solves.Select(channel => channel with { Report = channel.Report with { Warnings = warnings } })];
    }

    /// <summary>
    /// The waveform, frequency and quadrature flag of every generator, in
    /// declaration order.
    /// </summary>
    /// <remarks>
    /// A geometry with stages and no drive still switches; it just switches between
    /// states that do not oscillate. There is then one nominal clock whose frequency
    /// nothing uses, and one hertz keeps the step cap out of the way.
    /// </remarks>
    private static (List<double> Frequencies, List<Analytic.RfWaveform> Waveforms, List<bool> Quadrature)
        Clocks(IReadOnlyList<Core.Model.CompiledDrive> drives)
    {
        var frequencies = new List<double>(drives.Count);
        var waveforms = new List<Analytic.RfWaveform>(drives.Count);
        var quadrature = new List<bool>(drives.Count);

        foreach (var drive in drives)
        {
            frequencies.Add(drive.FrequencyHz);

            waveforms.Add(drive.Waveform == Core.Model.DriveWaveform.Rectangular
                ? new Analytic.RfWaveform.Rectangular(drive.DutyCycle)
                : new Analytic.RfWaveform.Sinusoid());

            quadrature.Add(drive.Waveform != Core.Model.DriveWaveform.Rectangular);
        }

        if (frequencies.Count == 0)
        {
            frequencies.Add(1.0);
            waveforms.Add(new Analytic.RfWaveform.Sinusoid());
            quadrature.Add(true);
        }

        return (frequencies, waveforms, quadrature);
    }

    /// <summary>What each declared electrode holds in each channel, worked out without solving.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <returns>
    /// One list per basis channel, in the order <see cref="SolveChannels"/> and
    /// <see cref="BuildField"/> solve them; each the <see cref="ChannelSolve.Holding"/> that
    /// solve will report.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is null.</exception>
    /// <remarks>
    /// What <see cref="SolveHash"/> keys a channel on, before there is a channel, from the same
    /// decomposition and the same weights the masks are built with - the plane builder's
    /// <see cref="GeometryBuilder.Holdings"/>, for a volume.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<double>> Holdings(Geometry3D geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        if (geometry.Drives.Count == 0 && geometry.Stages.Count == 0)
        {
            return [StaticHolding(geometry)];
        }

        return [.. Groups(geometry).Select(group => (IReadOnlyList<double>)
            [.. geometry.Electrodes.Select(WeightOf(group.Pattern))])];
    }

    /// <summary>A static solve holds every electrode at its declared potential.</summary>
    private static IReadOnlyList<double> StaticHolding(Geometry3D geometry) =>
        [.. geometry.Electrodes.Select(e => e.Potential)];

    /// <summary>A channel's pattern as the function a mask is built with.</summary>
    private static Func<CompiledElectrode3D, double> WeightOf(Dictionary<string, double> pattern) =>
        electrode => pattern.GetValueOrDefault(electrode.Name, 0.0);

    private static List<DriveChannel> Groups(Geometry3D geometry)
    {
        // Every state the electrodes pass through, a ramp's end included: a ramp from
        // zero has its whole pattern at its end, and a pattern gathered from starts alone
        // would leave it unsolved.
        var states = new List<IReadOnlyList<CompiledElectrode3D>>();
        if (geometry.Stages.Count == 0)
        {
            states.Add(geometry.Electrodes);
        }
        else
        {
            foreach (var stage in geometry.Stages)
            {
                states.Add(stage.Electrodes);
                if (stage.EndElectrodes is not null)
                {
                    states.Add(stage.EndElectrodes);
                }
            }
        }

        var (_, _, quadrature) = Clocks(geometry.Drives);

        // One state at a time, never the states flattened together: a supply's coefficients
        // are keyed by electrode name, so a flattened decomposition lets the same electrode
        // in two states collide and leaves a pattern belonging to no stage. See
        // DriveChannels.DecomposeStates.
        return DriveChannels.DecomposeStates(
            [.. states.Select(state => (IReadOnlyList<Excitation>)[.. state.Select(Excited)])],
            quadrature);
    }

    private static List<ChannelSolve> SolveGroups(
        Geometry3D geometry, List<DriveChannel> groups)
    {
        var grid = BuildGrid(geometry);
        var solves = new List<ChannelSolve>(groups.Count);

        for (var index = 0; index < groups.Count; index++)
        {
            solves.Add(SolveGroup(geometry, grid, groups[index], index));
        }

        return solves;
    }

    /// <summary>Solves one channel, and lets go of everything but its potential.</summary>
    /// <remarks>
    /// <para>
    /// <b>A method of its own so that its temporaries die with its frame.</b> A method called
    /// once runs as first-tier JIT code, and first-tier code keeps every reference it has
    /// ever put in a temporary reachable until the slot is reused or the method returns.
    /// Written inline in the channel loop, the temporary behind <c>mask.Cuts</c> held the
    /// previous channel's cut links through the next channel's whole solve - 96 bytes a
    /// node while every arm was stored, measured on the linear ion trap as 344 bytes a node
    /// for two channels against the 248 that one working set and one kept potential then
    /// accounted for. Setting the local to null would not have reached it: the reference was
    /// in a slot with no name.
    /// </para>
    /// <para>
    /// Before that, the loop kept each mask in the <see cref="ChannelSolve"/> it returned,
    /// which cost 105 bytes a node a channel for the same reason at a larger scale. Both are
    /// smaller now that the cut links hold their cut arms alone - a kept mask about 11 - and
    /// both are still wrong.
    /// </para>
    /// </remarks>
    private static ChannelSolve SolveGroup(
        Geometry3D geometry, Grid3D grid, DriveChannel group, int index)
    {
        var weight = WeightOf(group.Pattern);

        var mask = BuildMask(geometry, grid, weight);

        var (potential, report) = PoissonSolver3D.Solve(
            mask,
            geometry.Tolerance,
            maximumCycles: 200,
            coarsen: Coarsener(geometry, weight));

        // Asked of the first channel only and while its mask is in hand, because which nodes
        // sit on a shared face is a property of the mesh and the conductors and every channel
        // solves on both - and keeping a mask for the question afterwards is what this method
        // exists to stop.
        if (index == 0)
        {
            report = report with { Warnings = SharedFaceNodes.Warnings(NodesOnSharedFaces(geometry, grid, mask)) };
        }

        // The counts, not the mask: holding the mask kept every solved channel's cut links
        // alive through every later channel's solve. See ChannelSolve.
        return new ChannelSolve(index, mask.FixedCount, mask.Cuts?.CutCount ?? 0, potential, report)
        {
            Holding = [.. geometry.Electrodes.Select(weight)],
        };
    }

    /// <summary>How a three-dimensional electrode is excited, for the shared decomposition.</summary>
    private static Excitation Excited(CompiledElectrode3D electrode) =>
        new(electrode.Name, electrode.Potential, [.. electrode.Taps.Select(
            t => new DriveTap(t.Drive, t.Amplitude, t.Phase))]);

    /// <summary>
    /// The nodes an electrode's bounding box reaches: an index range per axis, with one
    /// node of slack at each end.
    /// </summary>
    /// <param name="LowI">First node index along x.</param>
    /// <param name="HighI">Last node index along x, inclusive.</param>
    /// <param name="LowJ">First node index along y.</param>
    /// <param name="HighJ">Last node index along y, inclusive.</param>
    /// <param name="LowK">First node index along z.</param>
    /// <param name="HighK">Last node index along z, inclusive.</param>
    private readonly record struct NodeBox(
        int LowI, int HighI, int LowJ, int HighJ, int LowK, int HighK)
    {
        /// <summary>Whether no node of the grid is near the electrode at all.</summary>
        public bool IsEmpty => LowI > HighI || LowJ > HighJ || LowK > HighK;

        /// <summary>Whether a link spanning these node indices meets the box.</summary>
        /// <remarks>
        /// A link is closed at both ends, so an arm running along the face of a box and an
        /// arm ending on it both count. The slack in the box itself already puts a whole
        /// cell between anything this refuses and the electrode's surface.
        /// </remarks>
        public bool Meets(int lowI, int highI, int lowJ, int highJ, int lowK, int highK) =>
            highI >= LowI && lowI <= HighI
            && highJ >= LowJ && lowJ <= HighJ
            && highK >= LowK && lowK <= HighK;
    }

    /// <summary>Where every electrode can be, in node indices, in declaration order.</summary>
    /// <remarks>
    /// <para>
    /// <b>The bounding box is trusted as conservative</b> - every point where
    /// <see cref="CompiledElectrode3D.Contains"/> holds lies inside it - which the volume
    /// overlap check already relies on to settle most pairs without a search. It is the
    /// electrode's own statement about where it is, so nothing here switches on a shape
    /// and a sixth primitive is culled the day it has a bounding box.
    /// </para>
    /// <para>
    /// <b>Floor and ceiling, then a node more each way, never rounding to nearest.</b>
    /// <c>Contains</c> is a signed distance at most zero, so a node lying exactly on a face
    /// is inside, and a bound converted to an index is a division that may land a rounding
    /// either side of the integer that node sits at. Floor and ceiling already keep every
    /// such node; the extra node is there so that a bound a few ulps short of the metal it
    /// bounds - a tilted box's corners come from a rotation, its signed distance from the
    /// inverse one - can never drop a node the reference loops would have fixed. It costs a
    /// layer of nodes per face of each box and is worth it: the claim is bit-identity with
    /// the unculled mask, and slack is what makes that a property rather than a coincidence
    /// of where the faces fell on this mesh.
    /// </para>
    /// </remarks>
    private static NodeBox[] Reach(IReadOnlyList<CompiledElectrode3D> electrodes, Grid3D grid)
    {
        var boxes = new NodeBox[electrodes.Count];

        for (var index = 0; index < electrodes.Count; index++)
        {
            var (minX, minY, minZ, maxX, maxY, maxZ) = electrodes[index].Bounds;

            var (lowI, highI) = Span(minX, maxX, grid.OriginX, grid.SpacingX, grid.CountX);
            var (lowJ, highJ) = Span(minY, maxY, grid.OriginY, grid.SpacingY, grid.CountY);
            var (lowK, highK) = Span(minZ, maxZ, grid.OriginZ, grid.SpacingZ, grid.CountZ);

            boxes[index] = new NodeBox(lowI, highI, lowJ, highJ, lowK, highK);
        }

        return boxes;
    }

    /// <summary>The node indices along one axis that an interval reaches, with a node of slack.</summary>
    /// <returns>An inclusive range, empty (low above high) when it misses the grid.</returns>
    private static (int Low, int High) Span(
        double lower, double upper, double origin, double spacing, int count)
    {
        var low = Math.Floor((Math.Min(lower, upper) - origin) / spacing) - 1.0;
        var high = Math.Ceiling((Math.Max(lower, upper) - origin) / spacing) + 1.0;

        if (double.IsNaN(low) || double.IsNaN(high))
        {
            // A box that cannot say where it is is asked about everywhere, which is what
            // the reference does for every electrode.
            return (0, count - 1);
        }

        // Clamped in floating point before the cast, so a bound far outside the grid - or
        // an infinite one - never overflows an int on its way to being clamped.
        low = Math.Max(low, 0.0);
        high = Math.Min(high, count - 1.0);

        return low > high ? (0, -1) : ((int)low, (int)high);
    }

    /// <summary>Fixes every node inside an electrode, and says whether it found any.</summary>
    /// <remarks>
    /// Only the nodes its bounding box reaches are asked. The loop body is the reference's,
    /// and the order is the reference's too - electrode by electrode in declaration order,
    /// then z, y, x - which matters because the mask is written electrode by electrode and
    /// where two overlap at one potential the last one written wins.
    /// </remarks>
    private static bool Rasterise(
        DirichletMask3D mask, Grid3D grid, CompiledElectrode3D electrode, double potential, NodeBox box)
    {
        var any = false;

        for (var k = box.LowK; k <= box.HighK; k++)
        {
            var z = grid.Z(k);

            for (var j = box.LowJ; j <= box.HighJ; j++)
            {
                var y = grid.Y(j);

                for (var i = box.LowI; i <= box.HighI; i++)
                {
                    if (electrode.Contains(grid.X(i), y, z))
                    {
                        mask.Fix(i, j, k, potential);
                        any = true;
                    }
                }
            }
        }

        return any;
    }

    /// <summary>Fixes every node inside an electrode, and says whether it found any.</summary>
    /// <remarks>
    /// The reference (CMP-1): every node of the grid, asked of every electrode. Kept as
    /// written and reachable through <see cref="MaskCulling.None"/>, because it is what the
    /// culled loop is checked against and a reference nothing runs is one nobody can trust.
    /// </remarks>
    private static bool Rasterise(
        DirichletMask3D mask, Grid3D grid, CompiledElectrode3D electrode, double potential)
    {
        var any = false;

        for (var k = 0; k < grid.CountZ; k++)
        {
            var z = grid.Z(k);

            for (var j = 0; j < grid.CountY; j++)
            {
                var y = grid.Y(j);

                for (var i = 0; i < grid.CountX; i++)
                {
                    if (electrode.Contains(grid.X(i), y, z))
                    {
                        mask.Fix(i, j, k, potential);
                        any = true;
                    }
                }
            }
        }

        return any;
    }

    /// <summary>
    /// Grounds the nodes on every Dirichlet face.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The face node itself, not a ghost one cell outside it. The alternative reading
    /// puts the boundary a cell further out at every level of a multigrid hierarchy,
    /// so the domain grows as it coarsens and the coarse problem is a different one -
    /// which in two dimensions sent a cap plate in a grounded box to 1e50 volts.
    /// </para>
    /// <para>
    /// Only a node no electrode has claimed, so an electrode reaching the face holds it -
    /// and an electrode whose face is flush with the domain's holds the nodes on it or
    /// leaves them grounded according to the rounding, which
    /// <see cref="NodesOnSharedFaces"/> reports.
    /// </para>
    /// </remarks>
    private static void PinFaces(DirichletMask3D mask, Grid3D grid)
    {
        for (var k = 0; k < grid.CountZ; k++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    var onDirichletFace =
                        (i == 0 && mask.LowerX == EdgeCondition.Dirichlet)
                        || (i == grid.CountX - 1 && mask.UpperX == EdgeCondition.Dirichlet)
                        || (j == 0 && mask.LowerY == EdgeCondition.Dirichlet)
                        || (j == grid.CountY - 1 && mask.UpperY == EdgeCondition.Dirichlet)
                        || (k == 0 && mask.LowerZ == EdgeCondition.Dirichlet)
                        || (k == grid.CountZ - 1 && mask.UpperZ == EdgeCondition.Dirichlet);

                    if (onDirichletFace && !mask.IsFixed(i, j, k))
                    {
                        mask.Fix(i, j, k, 0.0);
                    }
                }
            }
        }
    }

    /// <summary>Records every cut link, asking every electrode about every arm.</summary>
    /// <remarks>
    /// The reference (CMP-1), reachable through <see cref="MaskCulling.None"/>. Six arms
    /// of every free node against every electrode is half a billion entry tests on the
    /// Astral's foil solve, which is why it is not the path a solve takes.
    /// </remarks>
    private static void AddCuts(
        Geometry3D geometry,
        Grid3D grid,
        DirichletMask3D mask,
        Func<CompiledElectrode3D, double>? potentialOf)
    {
        var cuts = new CutLinks3D(grid);

        for (var k = 0; k < grid.CountZ; k++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    if (mask.IsFixed(i, j, k))
                    {
                        // A node inside metal has no free stencil to cut.
                        continue;
                    }

                    CutTowards(geometry, grid, cuts, potentialOf, i, j, k, 1, 0, 0, Arm3D.East);
                    CutTowards(geometry, grid, cuts, potentialOf, i, j, k, -1, 0, 0, Arm3D.West);
                    CutTowards(geometry, grid, cuts, potentialOf, i, j, k, 0, 1, 0, Arm3D.North);
                    CutTowards(geometry, grid, cuts, potentialOf, i, j, k, 0, -1, 0, Arm3D.South);
                    CutTowards(geometry, grid, cuts, potentialOf, i, j, k, 0, 0, 1, Arm3D.Up);
                    CutTowards(geometry, grid, cuts, potentialOf, i, j, k, 0, 0, -1, Arm3D.Down);
                }
            }
        }

        if (cuts.CutCount > 0)
        {
            // The packed arms grew by doubling, and are held for the whole solve.
            cuts.TrimExcess();
            mask.Cuts = cuts;
        }
    }

    private static void CutTowards(
        Geometry3D geometry,
        Grid3D grid,
        CutLinks3D cuts,
        Func<CompiledElectrode3D, double>? potentialOf,
        int i,
        int j,
        int k,
        int di,
        int dj,
        int dk,
        Arm3D arm)
    {
        var ni = i + di;
        var nj = j + dj;
        var nk = k + dk;

        if (ni < 0 || nj < 0 || nk < 0 || ni >= grid.CountX || nj >= grid.CountY || nk >= grid.CountZ)
        {
            return;
        }

        var fromX = grid.X(i);
        var fromY = grid.Y(j);
        var fromZ = grid.Z(k);

        var toX = grid.X(ni);
        var toY = grid.Y(nj);
        var toZ = grid.Z(nk);

        var nearest = 1.0;
        var potential = 0.0;
        var found = false;

        foreach (var electrode in geometry.Electrodes)
        {
            if (electrode.FirstEntry(fromX, fromY, fromZ, toX, toY, toZ) is not { } entry || entry >= nearest)
            {
                continue;
            }

            // A surface at zero puts the node itself on the conductor, where
            // rasterisation should already have fixed it. The two tests are
            // different arithmetic - a signed distance against a quadratic root -
            // so they can disagree by a rounding at a node that sits exactly on the
            // surface, and the disagreement is expensive: it makes an arm a
            // thousandth of a cell long holding the full electrode potential, which
            // is an enormous coefficient at precisely the nodes next to the metal.
            // The solve then converges contentedly to a wrong answer, and the
            // maximum principle is the only thing that notices.
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
            cuts.Cut(i, j, k, arm, nearest, potential);
        }
    }

    /// <summary>How many nodes a side a candidate block covers, as a power of two.</summary>
    /// <remarks>
    /// Eight. Small enough that a thin electrode's list stays out of blocks it is nowhere
    /// near, large enough that the lists cost little beside the mask they serve - the
    /// Astral's foil grid is three thousand blocks.
    /// </remarks>
    private const int BlockShift = 3;

    /// <summary>Records every cut link, asking only the electrodes near each arm.</summary>
    /// <remarks>
    /// <para>
    /// <b>Two screens, and the order the reference asks in survives both.</b> The grid is
    /// divided into blocks, each listing the electrodes whose reach any link from one of
    /// its nodes can touch; then each arm asks only those of its block's electrodes whose
    /// box the arm itself meets. A list is built by walking the electrodes in declaration
    /// order and is therefore in that order, which is load-bearing: two surfaces at exactly
    /// the same fraction along one arm go to the one declared first, as in the reference.
    /// </para>
    /// <para>
    /// <b>What is skipped could only ever have been a miss.</b> An arm that does not meet an
    /// electrode's box is at least a cell from its surface, where the reference's entry test
    /// returns nothing - so skipping it changes no fraction, no potential, and no count.
    /// </para>
    /// </remarks>
    private static void AddCuts(
        Geometry3D geometry,
        Grid3D grid,
        DirichletMask3D mask,
        Func<CompiledElectrode3D, double>? potentialOf,
        NodeBox[] reach)
    {
        var cuts = new CutLinks3D(grid);

        var blocksX = ((grid.CountX - 1) >> BlockShift) + 1;
        var blocksY = ((grid.CountY - 1) >> BlockShift) + 1;
        var blocks = Candidates(reach, grid, blocksX, blocksY);

        for (var k = 0; k < grid.CountZ; k++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    if (mask.IsFixed(i, j, k))
                    {
                        // A node inside metal has no free stencil to cut.
                        continue;
                    }

                    var near = blocks[
                        (i >> BlockShift) + (blocksX * ((j >> BlockShift) + (blocksY * (k >> BlockShift))))];

                    if (near.Length == 0)
                    {
                        continue;
                    }

                    CutTowards(geometry, reach, near, grid, cuts, potentialOf, i, j, k, 1, 0, 0, Arm3D.East);
                    CutTowards(geometry, reach, near, grid, cuts, potentialOf, i, j, k, -1, 0, 0, Arm3D.West);
                    CutTowards(geometry, reach, near, grid, cuts, potentialOf, i, j, k, 0, 1, 0, Arm3D.North);
                    CutTowards(geometry, reach, near, grid, cuts, potentialOf, i, j, k, 0, -1, 0, Arm3D.South);
                    CutTowards(geometry, reach, near, grid, cuts, potentialOf, i, j, k, 0, 0, 1, Arm3D.Up);
                    CutTowards(geometry, reach, near, grid, cuts, potentialOf, i, j, k, 0, 0, -1, Arm3D.Down);
                }
            }
        }

        if (cuts.CutCount > 0)
        {
            // The packed arms grew by doubling, and are held for the whole solve.
            cuts.TrimExcess();
            mask.Cuts = cuts;
        }
    }

    /// <summary>
    /// For each block of nodes, the electrodes a link from one of its nodes could meet, in
    /// declaration order.
    /// </summary>
    /// <remarks>
    /// A link reaches one node beyond the node it starts from, so a block is handed every
    /// electrode whose reach, widened by that one node, overlaps it.
    /// </remarks>
    private static int[][] Candidates(NodeBox[] reach, Grid3D grid, int blocksX, int blocksY)
    {
        var blocksZ = ((grid.CountZ - 1) >> BlockShift) + 1;
        var lists = new List<int>?[blocksX * blocksY * blocksZ];

        for (var index = 0; index < reach.Length; index++)
        {
            var box = reach[index];

            if (box.IsEmpty)
            {
                continue;
            }

            var fromI = Math.Max(box.LowI - 1, 0) >> BlockShift;
            var toI = Math.Min(box.HighI + 1, grid.CountX - 1) >> BlockShift;
            var fromJ = Math.Max(box.LowJ - 1, 0) >> BlockShift;
            var toJ = Math.Min(box.HighJ + 1, grid.CountY - 1) >> BlockShift;
            var fromK = Math.Max(box.LowK - 1, 0) >> BlockShift;
            var toK = Math.Min(box.HighK + 1, grid.CountZ - 1) >> BlockShift;

            for (var bk = fromK; bk <= toK; bk++)
            {
                for (var bj = fromJ; bj <= toJ; bj++)
                {
                    for (var bi = fromI; bi <= toI; bi++)
                    {
                        (lists[bi + (blocksX * (bj + (blocksY * bk)))] ??= []).Add(index);
                    }
                }
            }
        }

        var blocks = new int[lists.Length][];

        for (var block = 0; block < lists.Length; block++)
        {
            blocks[block] = lists[block] is { } list ? [.. list] : [];
        }

        return blocks;
    }

    /// <summary>Cuts one arm against the electrodes near it.</summary>
    /// <remarks>
    /// The body is the reference's, line for line, over a shorter list: the same endpoints,
    /// the same entry test, the same refusal of a surface at zero and the same strict
    /// comparison, so the same electrode wins every arm.
    /// </remarks>
    private static void CutTowards(
        Geometry3D geometry,
        NodeBox[] reach,
        int[] near,
        Grid3D grid,
        CutLinks3D cuts,
        Func<CompiledElectrode3D, double>? potentialOf,
        int i,
        int j,
        int k,
        int di,
        int dj,
        int dk,
        Arm3D arm)
    {
        var ni = i + di;
        var nj = j + dj;
        var nk = k + dk;

        if (ni < 0 || nj < 0 || nk < 0 || ni >= grid.CountX || nj >= grid.CountY || nk >= grid.CountZ)
        {
            return;
        }

        var lowI = Math.Min(i, ni);
        var highI = Math.Max(i, ni);
        var lowJ = Math.Min(j, nj);
        var highJ = Math.Max(j, nj);
        var lowK = Math.Min(k, nk);
        var highK = Math.Max(k, nk);

        var fromX = grid.X(i);
        var fromY = grid.Y(j);
        var fromZ = grid.Z(k);

        var toX = grid.X(ni);
        var toY = grid.Y(nj);
        var toZ = grid.Z(nk);

        var nearest = 1.0;
        var potential = 0.0;
        var found = false;

        foreach (var index in near)
        {
            if (!reach[index].Meets(lowI, highI, lowJ, highJ, lowK, highK))
            {
                continue;
            }

            var electrode = geometry.Electrodes[index];

            if (electrode.FirstEntry(fromX, fromY, fromZ, toX, toY, toZ) is not { } entry || entry >= nearest)
            {
                continue;
            }

            // A surface at zero is the node itself, which rasterization has already
            // decided; see the reference for why accepting it is expensive.
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
            cuts.Cut(i, j, k, arm, nearest, potential);
        }
    }
}
