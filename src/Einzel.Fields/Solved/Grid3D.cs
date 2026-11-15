using Einzel.Core.Errors;
using Einzel.Core.Numerics;

namespace Einzel.Fields.Solved;

/// <summary>
/// A uniform three-dimensional node grid, with independent spacing per axis.
/// </summary>
/// <remarks>
/// <para>
/// Written beside <see cref="Grid2D"/> rather than generalising it. The
/// two-dimensional path carries every validated number this engine has - the
/// reflectron at 1.3e-13, cut cells at 3.1e-10, the coaxial and Bessel closed forms
/// - and refactoring a numerical core that is known to be right, in order to add a
/// case beside it, is how those numbers get quietly lost. The duplication is the
/// price and it is the cheaper one.
/// </para>
/// <para>
/// Interval counts are powers of two on each axis so a multigrid hierarchy
/// coarsens cleanly, and each axis rounds its own count up from the same requested
/// cell size - so the domain is meshed exactly, no direction is ever coarser than
/// asked, and the worst aspect ratio is two to one.
/// </para>
/// </remarks>
public sealed class Grid3D
{
    /// <summary>Creates a grid with independent spacings.</summary>
    /// <param name="originX">x of node 0, in metres.</param>
    /// <param name="originY">y of node 0, in metres.</param>
    /// <param name="originZ">z of node 0, in metres.</param>
    /// <param name="spacingX">Node spacing along x, in metres.</param>
    /// <param name="spacingY">Node spacing along y, in metres.</param>
    /// <param name="spacingZ">Node spacing along z, in metres.</param>
    /// <param name="countX">Nodes along x, at least three.</param>
    /// <param name="countY">Nodes along y, at least three.</param>
    /// <param name="countZ">Nodes along z, at least three.</param>
    /// <exception cref="ArgumentOutOfRangeException">A spacing is not positive, or a count is below three.</exception>
    public Grid3D(
        double originX, double originY, double originZ,
        double spacingX, double spacingY, double spacingZ,
        int countX, int countY, int countZ)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spacingX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spacingY);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spacingZ);
        ArgumentOutOfRangeException.ThrowIfLessThan(countX, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(countY, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(countZ, 3);

        OriginX = originX;
        OriginY = originY;
        OriginZ = originZ;
        SpacingX = spacingX;
        SpacingY = spacingY;
        SpacingZ = spacingZ;
        CountX = countX;
        CountY = countY;
        CountZ = countZ;
    }

    /// <summary>x of node 0, in metres.</summary>
    public double OriginX { get; }

    /// <summary>y of node 0, in metres.</summary>
    public double OriginY { get; }

    /// <summary>z of node 0, in metres.</summary>
    public double OriginZ { get; }

    /// <summary>Node spacing along x, in metres.</summary>
    public double SpacingX { get; }

    /// <summary>Node spacing along y, in metres.</summary>
    public double SpacingY { get; }

    /// <summary>Node spacing along z, in metres.</summary>
    public double SpacingZ { get; }

    /// <summary>Nodes along x.</summary>
    public int CountX { get; }

    /// <summary>Nodes along y.</summary>
    public int CountY { get; }

    /// <summary>Nodes along z.</summary>
    public int CountZ { get; }

    /// <summary>Total nodes, saturating at <see cref="long.MaxValue"/> rather than overflowing.</summary>
    /// <remarks>
    /// It was an unchecked product, and <see cref="OverBox"/> builds axes of up to 2^30 + 1
    /// nodes - so 1073741825 x 1073741825 x 9 wrapped to a negative count, which the solve
    /// guard read as within its limit and passed to an allocation that failed as a defect.
    /// </remarks>
    public long NodeCount => VolumeMesh.Product(CountX, CountY, CountZ);

    /// <summary>The finest spacing, which is what a step may not outrun.</summary>
    public double MinimumSpacing => Math.Min(SpacingX, Math.Min(SpacingY, SpacingZ));

    /// <summary>The coarsest node spacing of the three axes, in metres.</summary>
    /// <remarks>
    /// The one that decides whether a level still resolves a feature. Each axis
    /// rounds its own interval count up to a power of two, so a 2:1 aspect ratio is
    /// ordinary here rather than exceptional, and a feature stops being resolved as
    /// soon as the <em>worst</em> direction outgrows it - asking the best direction
    /// lets a hierarchy descend one level past the point where an axis has stopped
    /// seeing the electrode at all.
    /// </remarks>
    public double MaximumSpacing => Math.Max(SpacingX, Math.Max(SpacingY, SpacingZ));

    /// <summary>Whether the cells are cubes.</summary>
    public bool IsCubic => SpacingX == SpacingY && SpacingY == SpacingZ;

    /// <summary>The far corner along x, in metres.</summary>
    public double MaxX => OriginX + ((CountX - 1) * SpacingX);

    /// <summary>The far corner along y, in metres.</summary>
    public double MaxY => OriginY + ((CountY - 1) * SpacingY);

    /// <summary>The far corner along z, in metres.</summary>
    public double MaxZ => OriginZ + ((CountZ - 1) * SpacingZ);

    /// <summary>Whether a point lies inside the grid box.</summary>
    /// <param name="x">x, in metres.</param>
    /// <param name="y">y, in metres.</param>
    /// <param name="z">z, in metres.</param>
    /// <returns><see langword="true"/> when inside or on the boundary.</returns>
    /// <remarks>
    /// For a grid built by <see cref="OverBox"/> the far boundary is the declared face or the
    /// last node, whichever is further out: the last node is min + fl(max - min), which can land
    /// an ulp short of max, and a point on the declared face must not read as outside the domain
    /// it bounds.
    /// </remarks>
    public bool Contains(double x, double y, double z) =>
        x >= OriginX && (x <= MaxX || x <= _declaredMaxX)
        && y >= OriginY && (y <= MaxY || y <= _declaredMaxY)
        && z >= OriginZ && (z <= MaxZ || z <= _declaredMaxZ);

    // The far corner as the box declared it; NaN, which compares false, when not built from one.
    private double _declaredMaxX = double.NaN;
    private double _declaredMaxY = double.NaN;
    private double _declaredMaxZ = double.NaN;

    /// <summary>x of a node index.</summary>
    /// <param name="i">Node index along x.</param>
    /// <returns>The coordinate, in metres.</returns>
    public double X(int i) => OriginX + (i * SpacingX);

    /// <summary>y of a node index.</summary>
    /// <param name="j">Node index along y.</param>
    /// <returns>The coordinate, in metres.</returns>
    public double Y(int j) => OriginY + (j * SpacingY);

    /// <summary>z of a node index.</summary>
    /// <param name="k">Node index along z.</param>
    /// <returns>The coordinate, in metres.</returns>
    public double Z(int k) => OriginZ + (k * SpacingZ);

    /// <summary>The flat index of a node.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <param name="k">Node index along z.</param>
    /// <returns>The index into a row-major buffer.</returns>
    public int Index(int i, int j, int k) => i + (CountX * (j + (CountY * k)));

    /// <summary>Whether a multigrid level below this one exists.</summary>
    public bool CanCoarsen =>
        (CountX - 1) % 2 == 0 && (CountY - 1) % 2 == 0 && (CountZ - 1) % 2 == 0
        && (CountX - 1) / 2 >= 2 && (CountY - 1) / 2 >= 2 && (CountZ - 1) / 2 >= 2;

    /// <summary>The next coarser grid, with twice the spacing and the same corners.</summary>
    /// <returns>The coarsened grid.</returns>
    /// <exception cref="InvalidOperationException">The grid cannot be coarsened further.</exception>
    public Grid3D Coarsen()
    {
        if (!CanCoarsen)
        {
            throw new InvalidOperationException(
                $"a {CountX} by {CountY} by {CountZ} grid cannot be coarsened further");
        }

        return new Grid3D(
            OriginX, OriginY, OriginZ,
            2.0 * SpacingX, 2.0 * SpacingY, 2.0 * SpacingZ,
            ((CountX - 1) / 2) + 1, ((CountY - 1) / 2) + 1, ((CountZ - 1) / 2) + 1);
    }

    /// <summary>
    /// A grid covering a box, meshed at about a requested cell size.
    /// </summary>
    /// <param name="minX">Lower x, in metres.</param>
    /// <param name="minY">Lower y, in metres.</param>
    /// <param name="minZ">Lower z, in metres.</param>
    /// <param name="maxX">Upper x, in metres.</param>
    /// <param name="maxY">Upper y, in metres.</param>
    /// <param name="maxZ">Upper z, in metres.</param>
    /// <param name="cellSize">Requested node spacing, in metres.</param>
    /// <returns>The grid.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The box is empty, or the cell size is not positive.</exception>
    /// <exception cref="EinzelException">
    /// An axis would need more intervals than an index can count; see
    /// <see cref="ErrorCodes.GridTooLarge"/>.
    /// </exception>
    /// <remarks>
    /// Each axis rounds its own interval count <em>up</em> to a power of two from
    /// the same requested cell size. Deriving one axis from another - which the
    /// two-dimensional builder once did - silently stretches the domain, and it did:
    /// a 60 by 20 mm box became 60 by 30 mm, and the solve was of a different
    /// instrument.
    /// </remarks>
    public static Grid3D OverBox(
        double minX, double minY, double minZ,
        double maxX, double maxY, double maxZ,
        double cellSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellSize);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxX, minX);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxY, minY);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxZ, minZ);

        var nx = VolumeMesh.Intervals(maxX - minX, cellSize);
        var ny = VolumeMesh.Intervals(maxY - minY, cellSize);
        var nz = VolumeMesh.Intervals(maxZ - minZ, cellSize);

        // A count an index cannot hold is past the node limit by orders of magnitude, so it
        // is the same refusal rather than an overflow: `Index` is an int, and the doubling
        // this replaced wrapped to zero and never terminated.
        if (Math.Max(nx, Math.Max(ny, nz)) >= int.MaxValue)
        {
            throw new EinzelException(VolumeMesh.Refusal(
                "/", nx + 1, ny + 1, nz + 1, maxX - minX, maxY - minY, maxZ - minZ, cellSize, unit: null));
        }

        return new Grid3D(
            minX, minY, minZ,
            (maxX - minX) / nx, (maxY - minY) / ny, (maxZ - minZ) / nz,
            (int)nx + 1, (int)ny + 1, (int)nz + 1)
        {
            _declaredMaxX = maxX,
            _declaredMaxY = maxY,
            _declaredMaxZ = maxZ,
        };
    }

    /// <summary>Refuses a grid with more nodes than a volume solve may hold.</summary>
    /// <exception cref="EinzelException">
    /// <see cref="ErrorCodes.GridTooLarge"/>, located at the document root because a grid has
    /// no document; a caller that knows the path locates it with <see cref="EinzelError.At"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>A backstop, not the refusal a model meets.</b> <c>ModelValidator</c> refuses such a
    /// mesh from the document, before anything is built, and names the cell size. This is for
    /// a grid that reached the solver some other way.
    /// </para>
    /// <para>
    /// <b>Called by every per-node allocation, and the conductor mask is the first of them.</b>
    /// The guard used to sit in the field constructor alone, and a solve builds its mask
    /// before any field - so the refusal fired only after a mask of the same node count, with
    /// six cut-link arms of two doubles a node, had been assembled. A guard that runs after
    /// the expensive thing it guards against has guarded nothing.
    /// </para>
    /// <para>
    /// Built grids are not refused on construction, deliberately: <c>einzel estimate</c>
    /// builds one to count its nodes and report its spacing, which is arithmetic, and a grid
    /// too large to solve is still a grid whose size is worth stating.
    /// </para>
    /// </remarks>
    internal void ThrowIfTooLargeToSolve()
    {
        if (NodeCount <= VolumeMesh.MaximumNodes)
        {
            return;
        }

        throw new EinzelException(VolumeMesh.Refusal(
            "/",
            CountX, CountY, CountZ,
            MaxX - OriginX, MaxY - OriginY, MaxZ - OriginZ,
            MinimumSpacing,
            unit: null));
    }
}
