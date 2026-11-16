using System.Numerics;

namespace Einzel.Fields.Solved;

/// <summary>
/// A uniform Cartesian grid in the x-y plane, in SI metres.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 10 puts a finite-difference multigrid solver on a regular
/// Cartesian or axisymmetric grid at the centre of Phase 1: straightforward to
/// implement correctly, clean to parallelise, and semantically close enough to
/// SIMION that SIMION becomes a direct cross-validation target.
/// </para>
/// <para>
/// Two dimensions, because that is what the first customer needs. A printed
/// circuit ion mirror is stripe electrodes running along the drift direction, so
/// the potential varies only across the stripes and through the board gap and is
/// invariant along the drift. That symmetry is not in the spec's SYM-1 list,
/// which names cylindrical, mirror-plane, and discrete periodic; it should be,
/// because it is the symmetry the memo's analyzer actually has and it removes a
/// whole dimension from the solve.
/// </para>
/// <para>
/// Node counts are of the form 2^k + 1 so that coarsening halves the intervals
/// exactly and every level shares the fine grid's corner nodes.
/// </para>
/// </remarks>
public sealed class Grid2D
{
    /// <summary>Creates a grid with square cells.</summary>
    /// <param name="originX">x of node 0, in metres.</param>
    /// <param name="originY">y of node 0, in metres.</param>
    /// <param name="spacing">Node spacing along both axes, in metres.</param>
    /// <param name="countX">Node count along x; at least 3.</param>
    /// <param name="countY">Node count along y; at least 3.</param>
    /// <exception cref="ArgumentOutOfRangeException">A count is below 3, or the spacing is not positive.</exception>
    public Grid2D(double originX, double originY, double spacing, int countX, int countY)
        : this(originX, originY, spacing, spacing, countX, countY)
    {
    }

    /// <summary>Creates a grid with independent spacings.</summary>
    /// <param name="originX">x of node 0, in metres.</param>
    /// <param name="originY">y of node 0, in metres.</param>
    /// <param name="spacingX">Node spacing along x, in metres.</param>
    /// <param name="spacingY">Node spacing along y, in metres.</param>
    /// <param name="countX">Node count along x; at least 3.</param>
    /// <param name="countY">Node count along y; at least 3.</param>
    /// <exception cref="ArgumentOutOfRangeException">A count is below 3, or a spacing is not positive.</exception>
    /// <remarks>
    /// <para>
    /// Cells need not be square. The Shortley-Weller stencil already carries a
    /// spacing per arm, so anisotropy costs it nothing, and it is what lets a
    /// declared solve domain be meshed exactly rather than rounded to whatever
    /// box a square cell happens to reach.
    /// </para>
    /// <para>
    /// Anisotropy is not free for the <em>solver</em>, though: point smoothing
    /// damps error poorly along the direction with the larger spacing, so a
    /// strongly stretched grid wants line smoothing or semi-coarsening. Ratios up
    /// to two to one are fine, and that is all
    /// <see cref="OverBox(double, double, double, double, int, int?)"/> can
    /// produce, since each axis rounds its interval count up to a power of two
    /// from the same requested cell size.
    /// </para>
    /// </remarks>
    public Grid2D(double originX, double originY, double spacingX, double spacingY, int countX, int countY)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spacingX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spacingY);
        ArgumentOutOfRangeException.ThrowIfLessThan(countX, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(countY, 3);

        OriginX = originX;
        OriginY = originY;
        SpacingX = spacingX;
        SpacingY = spacingY;
        CountX = countX;
        CountY = countY;
    }

    /// <summary>
    /// Creates a grid spanning a box, with the node count set by the refinement
    /// level so that coarsening is exact.
    /// </summary>
    /// <param name="minX">Lower x bound, in metres.</param>
    /// <param name="minY">Lower y bound, in metres.</param>
    /// <param name="maxX">Upper x bound, in metres.</param>
    /// <param name="maxY">Upper y bound, in metres.</param>
    /// <param name="intervalsX">Intervals along x; must be a positive power of two.</param>
    /// <param name="intervalsY">
    /// Intervals along y; must be a positive power of two. When omitted it follows
    /// from the aspect ratio at the x spacing, rounded up so the cells are never
    /// coarser along y than along x.
    /// </param>
    /// <returns>The grid, spanning exactly the box it was given.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The box is degenerate, or an interval count is not a power of two.</exception>
    /// <remarks>
    /// <para>
    /// The grid spans the declared box exactly. It used to derive the y interval
    /// count from the aspect ratio, round that up to a power of two, and keep the
    /// x spacing - which meant the top of the grid landed wherever that count put
    /// it. For a box whose aspect ratio did not suit, that was a long way from
    /// where it was asked to be: a 60 by 20 mm box at a 1 mm cell needs 21.3
    /// intervals in y, rounds to 32, and was solved as a 60 by 30 mm box. Fifty
    /// per cent taller than declared, silently, and nothing checked.
    /// </para>
    /// <para>
    /// Deriving the y spacing from the box instead makes the extent exact and
    /// pushes the compromise into cell shape, where it is bounded and visible.
    /// </para>
    /// </remarks>
    public static Grid2D OverBox(
        double minX, double minY, double maxX, double maxY, int intervalsX, int? intervalsY = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(intervalsX);

        if (!int.IsPow2(intervalsX))
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalsX), intervalsX, "interval counts must be a power of two so coarsening is exact");
        }

        if (intervalsY is { } declared && (declared <= 0 || !int.IsPow2(declared)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalsY), declared, "interval counts must be a power of two so coarsening is exact");
        }

        if (maxX <= minX || maxY <= minY)
        {
            throw new ArgumentOutOfRangeException(nameof(maxX), "the box must have positive extent");
        }

        var spacingX = (maxX - minX) / intervalsX;

        var countY = intervalsY ?? (int)BitOperations.RoundUpToPowerOf2(
            (uint)Math.Max(2, (int)Math.Ceiling((maxY - minY) / spacingX)));

        return new Grid2D(minX, minY, spacingX, (maxY - minY) / countY, intervalsX + 1, countY + 1)
        {
            _declaredMaxX = maxX,
            _declaredMaxY = maxY,
        };
    }

    // The far corner as the box declared it. The last node sits at min + intervals * spacing,
    // which is min + fl(max - min) and can land an ulp short of max (a box from -12.7 mm to
    // 0.3 mm does). A point exactly on the declared face is in the domain - the face node holds a
    // solved value and the interpolant's stencil is clamped there - so containment reaches
    // whichever of the two is further out. NaN, which compares false, when not built from a box.
    private double _declaredMaxX = double.NaN;
    private double _declaredMaxY = double.NaN;

    /// <summary>x of node 0, in metres.</summary>
    public double OriginX { get; }

    /// <summary>y of node 0, in metres.</summary>
    public double OriginY { get; }

    /// <summary>Node spacing along x, in metres.</summary>
    public double SpacingX { get; }

    /// <summary>Node spacing along y, in metres.</summary>
    public double SpacingY { get; }

    /// <summary>
    /// The finer of the two spacings, in metres.
    /// </summary>
    /// <remarks>
    /// What a step-size limit or a resolution claim should use. A field carries no
    /// information below its node spacing in either direction, and the smaller one
    /// is the one that has to be believed.
    /// </remarks>
    public double MinimumSpacing => Math.Min(SpacingX, SpacingY);

    /// <summary>Whether the cells are square.</summary>
    public bool IsSquare => SpacingX == SpacingY;

    /// <summary>
    /// The squared ratio of the x spacing to the y spacing.
    /// </summary>
    /// <remarks>
    /// The factor that scales the y half of a second-difference stencil when the
    /// operator is carried in x cell units. Exactly one for a square grid, and
    /// multiplying by one is exact, so an isotropic solve is unaffected to the
    /// last bit by the fact that anisotropy is supported at all.
    /// </remarks>
    public double AspectSquared => (SpacingX / SpacingY) * (SpacingX / SpacingY);

    /// <summary>Node count along x.</summary>
    public int CountX { get; }

    /// <summary>Node count along y.</summary>
    public int CountY { get; }

    /// <summary>Total node count.</summary>
    public int NodeCount => CountX * CountY;

    /// <summary>Upper x bound, in metres.</summary>
    public double MaxX => OriginX + ((CountX - 1) * SpacingX);

    /// <summary>Upper y bound, in metres.</summary>
    public double MaxY => OriginY + ((CountY - 1) * SpacingY);

    /// <summary>x of a node.</summary>
    /// <param name="i">Node index along x.</param>
    /// <returns>The coordinate, in metres.</returns>
    public double X(int i) => OriginX + (i * SpacingX);

    /// <summary>y of a node.</summary>
    /// <param name="j">Node index along y.</param>
    /// <returns>The coordinate, in metres.</returns>
    public double Y(int j) => OriginY + (j * SpacingY);

    /// <summary>Row-major index of a node.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <returns>The flat index.</returns>
    public int Index(int i, int j) => (j * CountX) + i;

    /// <summary>Whether a point lies inside the grid box.</summary>
    /// <param name="x">x, in metres.</param>
    /// <param name="y">y, in metres.</param>
    /// <returns><see langword="true"/> when inside or on the boundary.</returns>
    /// <remarks>
    /// For a grid built by <see cref="OverBox(double, double, double, double, int, int?)"/> the far
    /// boundary is the declared face or the last node, whichever is further out: the two can
    /// differ by the rounding of one subtraction and one addition, and a point on the declared
    /// face must not read as outside the domain it bounds.
    /// </remarks>
    public bool Contains(double x, double y) =>
        x >= OriginX && (x <= MaxX || x <= _declaredMaxX)
        && y >= OriginY && (y <= MaxY || y <= _declaredMaxY);

    /// <summary>Whether this grid can be coarsened by a further factor of two.</summary>
    public bool CanCoarsen => (CountX - 1) % 2 == 0 && (CountY - 1) % 2 == 0
        && (CountX - 1) / 2 >= 2 && (CountY - 1) / 2 >= 2;

    /// <summary>The next coarser grid, with twice the spacing and the same corners.</summary>
    /// <returns>The coarsened grid.</returns>
    /// <exception cref="InvalidOperationException">The grid cannot be coarsened further.</exception>
    public Grid2D Coarsen()
    {
        if (!CanCoarsen)
        {
            throw new InvalidOperationException(
                $"a {CountX} by {CountY} grid cannot be coarsened further");
        }

        return new Grid2D(
            OriginX, OriginY, SpacingX * 2.0, SpacingY * 2.0, ((CountX - 1) / 2) + 1, ((CountY - 1) / 2) + 1);
    }

    /// <inheritdoc/>
    public override string ToString() =>
        IsSquare
            ? string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{CountX}x{CountY} at h={SpacingX:G6} m")
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{CountX}x{CountY} at hx={SpacingX:G6} m, hy={SpacingY:G6} m");
}
