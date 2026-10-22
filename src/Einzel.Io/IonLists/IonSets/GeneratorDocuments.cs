using Einzel.Core.Model;

namespace Einzel.Io.IonLists;

/// <summary>
/// A scalar attribute of a generated set: one constant, or one generator, in one unit.
/// </summary>
/// <remarks>
/// Exactly one of <see cref="Value"/>, <see cref="Uniform"/>, <see cref="Gaussian"/>,
/// <see cref="Sequence"/> and <see cref="Linspace"/>. Values are in <see cref="Unit"/>, which
/// is required for a dimensioned attribute and absent for <c>rfPhase</c> (cycles).
/// </remarks>
public sealed record ScalarDraw
{
    /// <summary>A constant.</summary>
    public double? Value { get; init; }

    /// <summary>The unit of every number in this attribute.</summary>
    public string? Unit { get; init; }

    /// <summary>Uniform on [min, max).</summary>
    public UniformDocument? Uniform { get; init; }

    /// <summary>Normal with a mean and a standard deviation.</summary>
    public GaussianDocument? Gaussian { get; init; }

    /// <summary><c>first + i * step</c> for the i-th ion; draws nothing.</summary>
    public SequenceDocument? Sequence { get; init; }

    /// <summary><c>n</c> values from first to last, both included; draws nothing.</summary>
    public LinspaceDocument? Linspace { get; init; }
}

/// <summary>A generated set's position: a constant, a disc, or one scalar per axis.</summary>
public sealed record PositionDraw
{
    /// <summary>A constant position, three components.</summary>
    public IReadOnlyList<double>? Value { get; init; }

    /// <summary>The unit of the value, the disc, and any axis that names none.</summary>
    public string? Unit { get; init; }

    /// <summary>Uniform over the area of a disc.</summary>
    public DiscDocument? Disc { get; init; }

    /// <summary>The x component, when the position is given per axis.</summary>
    public ScalarDraw? X { get; init; }

    /// <summary>The y component, when the position is given per axis.</summary>
    public ScalarDraw? Y { get; init; }

    /// <summary>The z component, when the position is given per axis.</summary>
    public ScalarDraw? Z { get; init; }
}

/// <summary>A generated set's direction: a constant or a cone.</summary>
public sealed record DirectionDraw
{
    /// <summary>A constant direction; normalised.</summary>
    public IReadOnlyList<double>? Value { get; init; }

    /// <summary>Directions inside or on a cone.</summary>
    public ConeDocument? Cone { get; init; }
}

/// <summary>Uniform on [min, max).</summary>
public sealed record UniformDocument
{
    /// <summary>The lower bound, included.</summary>
    public required double Min { get; init; }

    /// <summary>The upper bound, excluded; not below <see cref="Min"/>.</summary>
    public required double Max { get; init; }
}

/// <summary>Normal, by the polar Box-Muller method <c>IonCloud</c> uses.</summary>
public sealed record GaussianDocument
{
    /// <summary>The mean.</summary>
    public required double Mean { get; init; }

    /// <summary>The standard deviation; not negative.</summary>
    public required double Sigma { get; init; }
}

/// <summary>An arithmetic sequence over the set's ions.</summary>
public sealed record SequenceDocument
{
    /// <summary>The first ion's value.</summary>
    public required double First { get; init; }

    /// <summary>The step from one ion to the next.</summary>
    public required double Step { get; init; }
}

/// <summary><c>n</c> evenly spaced values, both ends included.</summary>
public sealed record LinspaceDocument
{
    /// <summary>The first ion's value.</summary>
    public required double First { get; init; }

    /// <summary>The last ion's value.</summary>
    public required double Last { get; init; }
}

/// <summary>Positions uniform over the area of a disc, in the position's unit.</summary>
public sealed record DiscDocument
{
    /// <summary>The centre, three components.</summary>
    public required IReadOnlyList<double> Centre { get; init; }

    /// <summary>The normal of the disc's plane; normalised.</summary>
    public required IReadOnlyList<double> Normal { get; init; }

    /// <summary>The radius; positive.</summary>
    public required double Radius { get; init; }
}

/// <summary>Directions in a cone around an axis.</summary>
public sealed record ConeDocument
{
    /// <summary>The cone's axis; normalised.</summary>
    public required IReadOnlyList<double> Axis { get; init; }

    /// <summary>The half-angle, an angle with a unit; above 0 and at most 90 degrees.</summary>
    public required QuantityValue HalfAngle { get; init; }

    /// <summary>
    /// True for directions uniform in solid angle inside the cone; false for directions on
    /// its surface, uniform in azimuth.
    /// </summary>
    public bool Fill { get; init; } = true;
}
