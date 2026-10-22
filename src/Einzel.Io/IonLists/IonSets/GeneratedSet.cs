using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Transport;

namespace Einzel.Io.IonLists;

/// <summary>Expands a set whose attributes are constants and generators into rows.</summary>
/// <remarks>
/// <para>
/// One <see cref="Random"/> per set, seeded with the set's <c>seed</c>, and a fixed draw
/// order: ion by ion, and within an ion massToCharge, the energy, the birth (birthTime or
/// rfPhase), position (x, y, z for a per-axis position), direction. A constant, a
/// <c>sequence</c> and a <c>linspace</c> draw nothing; <c>uniform</c> draws once,
/// <c>gaussian</c> twice per polar attempt, <c>disc</c> and <c>cone</c> twice. So the first k
/// ions of a set are the same whatever its <c>n</c>, except under a <c>linspace</c>, whose
/// spacing is a function of <c>n</c>.
/// </para>
/// <para>
/// Each ion becomes an explicit row in SI and is compiled exactly as a written row is, so
/// defaults, energy arithmetic and refusals are the row's: a drawn mass that is not
/// positive is refused at the set's <c>massToCharge</c>. A refusal is reported at the set's
/// attribute (<c>/ionSets/s/position</c>), never at a key of the synthetic row that the
/// document does not contain.
/// </para>
/// </remarks>
internal static class GeneratedSet
{
    internal static IEnumerable<IonRow> Expand(
        IonSetDocument set, string path, long next, IonList.Context context, List<long> seeds)
    {
        var n = set.N ?? throw new EinzelException(Error(path + "/n", "a set says how many ions it has"));
        IonList.Require(n >= 1, ErrorCodes.ValueOutOfBounds, path + "/n", "a set draws at least one ion");

        var draws = new ScalarDraw?[] { set.MassToCharge, set.KineticEnergy, set.AccelerationPotential, set.BirthTime, set.RfPhase, set.Position?.X, set.Position?.Y, set.Position?.Z };
        var random = draws.Any(d => d?.Uniform is not null || d?.Gaussian is not null)
            || set.Position?.Disc is not null || set.Direction?.Cone is not null;

        IonList.Require(!random || set.Seed is not null, ErrorCodes.SchemaInvalid, path + "/seed",
            "a set that draws at random declares its seed, so it draws the same ions every time");

        if (set.Seed is { } declared)
        {
            seeds.Add(declared);
        }

        var stream = new Random(set.Seed ?? 0);
        var first = IonList.FirstId(set.FirstId, next, n, path);

        var massToCharge = Scalar(set.MassToCharge, path + "/massToCharge", Dimension.MassDimension, n, stream);
        var kinetic = Scalar(set.KineticEnergy, path + "/kineticEnergy", Dimension.Energy, n, stream);
        var potential = Scalar(set.AccelerationPotential, path + "/accelerationPotential", Dimension.ElectricPotential, n, stream);
        var birth = Scalar(set.BirthTime, path + "/birthTime", Dimension.TimeDimension, n, stream);
        var phase = Scalar(set.RfPhase, path + "/rfPhase", null, n, stream);
        var position = Position(set.Position, path + "/position", n, stream);
        var direction = Direction(set.Direction, path + "/direction", context, stream);

        for (var i = 0; i < n; i++)
        {
            // The initialiser runs in the order written, which is the documented draw order.
            var row = new IonRowDocument
            {
                Id = (int)(first + i),
                MassToCharge = massToCharge is null ? null : new QuantityValue(massToCharge(i), "kg"),
                ChargeNumber = set.ChargeNumber,
                KineticEnergy = kinetic is null ? null : new QuantityValue(kinetic(i), "J"),
                AccelerationPotential = potential is null ? null : new QuantityValue(potential(i), "V"),
                BirthTime = birth is null ? null : new QuantityValue(birth(i), "s"),
                RfPhase = phase?.Invoke(i),
                Position = position is null ? null : Vector(position(i)),
                Direction = direction is null ? null : new DirectionValue(Components(direction(i))),
                Tag = set.Tag,
            };

            yield return Compile(row, path, context);
        }
    }

    /// <summary>Compiles one generated ion as a row, its refusal re-addressed to the set's attribute.</summary>
    private static IonRow Compile(IonRowDocument row, string path, IonList.Context context)
    {
        try
        {
            return IonList.Row(row, path, context);
        }
        catch (EinzelException failure) when (failure.Error.Path.StartsWith(path + "/", StringComparison.Ordinal))
        {
            var attribute = failure.Error.Path[(path.Length + 1)..].Split('/')[0];
            throw new EinzelException(failure.Error with { Path = path + "/" + attribute }, failure);
        }
    }

    /// <summary>A scalar attribute in SI, or null when the set leaves it to the model.</summary>
    private static Func<int, double>? Scalar(ScalarDraw? draw, string path, Dimension? dimension, int n, Random random)
    {
        if (draw is null)
        {
            return null;
        }

        var forms = new object?[] { draw.Value, draw.Uniform, draw.Gaussian, draw.Sequence, draw.Linspace };
        IonList.Require(forms.Count(f => f is not null) == 1, ErrorCodes.SchemaInvalid, path,
            "an attribute is one of value, uniform, gaussian, sequence or linspace");

        var scale = Scale(draw.Unit, path, dimension);
        Func<int, double> value = draw switch
        {
            { Value: { } constant } => _ => constant,
            { Uniform: { } uniform } => Scalars.Uniform(uniform, path + "/uniform", random),
            { Gaussian: { } gaussian } => Scalars.Gaussian(gaussian, path + "/gaussian", random),
            { Sequence: { } sequence } => Scalars.Sequence(sequence),
            _ => Scalars.Linspace(draw.Linspace!, n),
        };

        return i => value(i) * scale;
    }

    /// <summary>The SI factor of a unit, checked against the dimension; 1 for a number of cycles.</summary>
    private static double Scale(string? unit, string path, Dimension? dimension)
    {
        if (dimension is not { } expected)
        {
            IonList.Require(unit is null, ErrorCodes.SchemaInvalid, path + "/unit", "an RF phase is in cycles and takes no unit");
            return 1.0;
        }

        IonList.Require(unit is not null, ErrorCodes.UnitsRequired, path + "/unit", $"a unit of dimension {expected} is required");
        return new QuantityValue(1.0, unit!).ToQuantity(path + "/unit", expected).SiValue;
    }

    private static Func<int, Core.Geometry.Vec3>? Position(PositionDraw? draw, string path, int n, Random random)
    {
        if (draw is null)
        {
            return null;
        }

        var axes = new[] { draw.X, draw.Y, draw.Z };
        var forms = new[] { draw.Value is not null, draw.Disc is not null, axes.Any(a => a is not null) };
        IonList.Require(forms.Count(f => f) == 1 && (!forms[2] || axes.All(a => a is not null)), ErrorCodes.SchemaInvalid,
            path, "a position is one of value, disc, or all three of x, y and z");

        if (forms[2])
        {
            var (x, y, z) = (
                Scalar(draw.X! with { Unit = draw.X!.Unit ?? draw.Unit }, path + "/x", Dimension.LengthDimension, n, random)!,
                Scalar(draw.Y! with { Unit = draw.Y!.Unit ?? draw.Unit }, path + "/y", Dimension.LengthDimension, n, random)!,
                Scalar(draw.Z! with { Unit = draw.Z!.Unit ?? draw.Unit }, path + "/z", Dimension.LengthDimension, n, random)!);
            return i => new Core.Geometry.Vec3(x(i), y(i), z(i));
        }

        var scale = Scale(draw.Unit, path, Dimension.LengthDimension);

        if (draw.Disc is { } disc)
        {
            return Directions.Disc(disc, path + "/disc", scale, random);
        }

        var constant = new VectorValue(draw.Value!, "m").ToVec3(path + "/value", Dimension.LengthDimension) * scale;
        return _ => constant;
    }

    private static Func<int, Core.Geometry.Vec3>? Direction(DirectionDraw? draw, string path, IonList.Context context, Random random)
    {
        if (draw is null)
        {
            return null;
        }

        IonList.Require((draw.Value is null) != (draw.Cone is null), ErrorCodes.SchemaInvalid, path,
            "a direction is one of value or cone");

        if (draw.Cone is { } cone)
        {
            return Directions.Cone(cone, path + "/cone", context.Parameters, random);
        }

        // Checked here, normalised once by the row, as a written direction is.
        _ = new DirectionValue(draw.Value!).ToUnitVector(path + "/value");
        var constant = new Core.Geometry.Vec3(draw.Value![0], draw.Value[1], draw.Value[2]);
        return _ => constant;
    }

    private static VectorValue Vector(Core.Geometry.Vec3 v) => new(Components(v), "m");

    private static double[] Components(Core.Geometry.Vec3 v) => [v.X, v.Y, v.Z];

    private static EinzelError Error(string path, string constraint) => new()
    {
        Code = ErrorCodes.SchemaInvalid,
        Path = path,
        Constraint = constraint,
    };
}
