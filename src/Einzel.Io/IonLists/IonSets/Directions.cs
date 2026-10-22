using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;

namespace Einzel.Io.IonLists;

/// <summary>The disc and the cone: positions on a disc, directions in a cone.</summary>
internal static class Directions
{
    /// <summary>Uniform over the disc's area: radius as R sqrt(u), then azimuth; two draws.</summary>
    internal static Func<int, Vec3> Disc(DiscDocument disc, string path, double scale, Random random)
    {
        IonList.Require(disc.Radius > 0.0, ErrorCodes.ValueOutOfBounds, path + "/radius", "a disc's radius is positive");

        var centre = new VectorValue(disc.Centre, "m").ToVec3(path + "/centre", Dimension.LengthDimension) * scale;
        var (a, b) = Perpendiculars(new DirectionValue(disc.Normal).ToUnitVector(path + "/normal"));
        var radius = disc.Radius * scale;

        return _ =>
        {
            var r = radius * Math.Sqrt(random.NextDouble());
            var azimuth = 2.0 * Math.PI * random.NextDouble();
            return centre + (a * (r * Math.Cos(azimuth))) + (b * (r * Math.Sin(azimuth)));
        };
    }

    /// <summary>
    /// Inside the cone uniform in solid angle (cos theta uniform), or on its surface; then
    /// azimuth. Two draws either way, so <c>fill</c> moves no later draw.
    /// </summary>
    internal static Func<int, Vec3> Cone(
        ConeDocument cone, string path, IReadOnlyDictionary<string, Quantity> parameters, Random random)
    {
        var axis = new DirectionValue(cone.Axis).ToUnitVector(path + "/axis");

        // A required member written as null reads as null.
        var halfAngle = (QuantityValue?)cone.HalfAngle ?? throw new EinzelException(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = path + "/halfAngle",
            Constraint = "a cone states its half-angle",
        });
        var half = halfAngle.ToQuantity(path + "/halfAngle", Dimension.Dimensionless, parameters).SiValue;

        IonList.Require(half > 0.0 && half <= Math.PI / 2.0, ErrorCodes.ValueOutOfBounds, path + "/halfAngle",
            "a cone's half-angle is above 0 and at most 90 degrees");

        var (a, b) = Perpendiculars(axis);
        var cosMax = Math.Cos(half);

        return _ =>
        {
            var u = random.NextDouble();
            var cosTheta = cone.Fill ? 1.0 - (u * (1.0 - cosMax)) : cosMax;
            var sinTheta = Math.Sqrt(Math.Max(0.0, 1.0 - (cosTheta * cosTheta)));
            var azimuth = 2.0 * Math.PI * random.NextDouble();

            return (axis * cosTheta) + (a * (sinTheta * Math.Cos(azimuth))) + (b * (sinTheta * Math.Sin(azimuth)));
        };
    }

    /// <summary>Two unit vectors perpendicular to a unit vector and to each other, as <c>IonCloud</c> builds them.</summary>
    internal static (Vec3 A, Vec3 B) Perpendiculars(in Vec3 along)
    {
        var seed = Math.Abs(along.X) < 0.9 ? new Vec3(1.0, 0.0, 0.0) : new Vec3(0.0, 1.0, 0.0);
        var a = Vec3.Cross(along, seed);
        a *= 1.0 / a.Length;

        return (a, Vec3.Cross(along, a));
    }
}
