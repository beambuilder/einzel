namespace Einzel.Fields.Solved;

/// <summary>Nodal values on a <see cref="Grid3D"/>.</summary>
public sealed class ScalarField3D
{
    private readonly double[] _values;

    /// <summary>Creates a field of zeros.</summary>
    /// <param name="grid">The grid.</param>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    /// <exception cref="Core.Errors.EinzelException">
    /// The grid has more nodes than a volume solve may hold; see
    /// <see cref="Core.Errors.ErrorCodes.GridTooLarge"/>.
    /// </exception>
    public ScalarField3D(Grid3D grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        // A 3D grid runs out of address space long before it runs out of patience: 1024
        // cubed is a billion nodes and eight gigabytes for one field. Refused with the
        // number rather than as an out-of-memory somewhere further in - and as a refusal
        // about the model, which is what it is, rather than as an argument exception the
        // CLI can only report as a defect in the engine.
        grid.ThrowIfTooLargeToSolve();

        Grid = grid;
        _values = new double[grid.NodeCount];
    }

    /// <summary>Wraps values already in <c>Grid.Index</c> order, as a store hands them back.</summary>
    internal ScalarField3D(Grid3D grid, double[] values)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(values.LongLength, grid.NodeCount, nameof(values));
        Grid = grid;
        _values = values;
    }

    /// <summary>The grid these values live on.</summary>
    public Grid3D Grid { get; }

    /// <summary>Condition the x-minimum face was solved under.</summary>
    /// <remarks>
    /// Carried on the field because the interpolant needs it. A four-node stencil
    /// reaches outside the grid, and what the ghost should hold depends on what kind
    /// of face it is outside of: a Dirichlet face is the end of the data and the
    /// ghost continues the ramp, a Neumann face is a mirror and the ghost is the
    /// reflection. Getting that wrong puts a spurious normal field on a plane where
    /// the field is normal to nothing, which cost a real bug in two dimensions.
    /// </remarks>
    public EdgeCondition LowerX { get; set; }

    /// <summary>Condition the x-maximum face was solved under.</summary>
    public EdgeCondition UpperX { get; set; }

    /// <summary>Condition the y-minimum face was solved under.</summary>
    public EdgeCondition LowerY { get; set; }

    /// <summary>Condition the y-maximum face was solved under.</summary>
    public EdgeCondition UpperY { get; set; }

    /// <summary>Condition the z-minimum face was solved under.</summary>
    public EdgeCondition LowerZ { get; set; }

    /// <summary>Condition the z-maximum face was solved under.</summary>
    public EdgeCondition UpperZ { get; set; }

    /// <summary>Indexes a node.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <param name="k">Node index along z.</param>
    /// <returns>The value.</returns>
    public double this[int i, int j, int k]
    {
        get => _values[Grid.Index(i, j, k)];
        set => _values[Grid.Index(i, j, k)] = value;
    }

    /// <summary>The underlying values, row-major.</summary>
    public Span<double> Values => _values;

    /// <summary>Copies the field, edge conditions and all.</summary>
    /// <returns>The copy.</returns>
    public ScalarField3D Clone()
    {
        var copy = new ScalarField3D(Grid)
        {
            LowerX = LowerX,
            UpperX = UpperX,
            LowerY = LowerY,
            UpperY = UpperY,
            LowerZ = LowerZ,
            UpperZ = UpperZ,
        };

        _values.AsSpan().CopyTo(copy._values);
        return copy;
    }
}
