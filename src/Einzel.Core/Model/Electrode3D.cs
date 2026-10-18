using System.Buffers;

namespace Einzel.Core.Model;

/// <summary>The shapes a three-dimensional electrode can take.</summary>
/// <remarks>
/// <para>
/// A box is a plate, a segment wall or a housing; a cylinder is a rod, a tube or a ring; a
/// sphere is a bead or a rounded end; a prism is any outline given a length, which is how a
/// hyperbolic quadrupole rod with a slot cut through it is written. A device that needs
/// another is a fair reason to add one, and a device that needs arbitrary geometry is what
/// mesh import is for.
/// </para>
/// <para>
/// <b><see cref="Revolve"/> is the fifth, and the C-trap is why.</b> A curved quadrupole's
/// rods are a shaped cross-section swept round an arc - the same profile a linear trap
/// extrudes along a line, bent. With no such primitive the template modeled each rod as a
/// chain of overlapping spheres, which needed nothing new because <c>repeat</c> binds an
/// index and <c>cosPi</c>/<c>sinPi</c> place a bead anywhere. That is the shape of mistake
/// LIB-1 exists to catch: the abstraction was missing and the expedient hid it, at the cost
/// of a rod whose surface scalloped by 13.6 percent of its own radius.
/// </para>
/// </remarks>
public enum Electrode3DShape
{
    /// <summary>An axis-aligned rectangular box.</summary>
    Box,

    /// <summary>A sphere.</summary>
    Sphere,

    /// <summary>A capped cylinder along one coordinate axis.</summary>
    Cylinder,

    /// <summary>
    /// A closed outline extruded along one axis between two ends: a rod of any
    /// cross-section, a slotted hyperbolic quadrupole rod cut into axial sections, a
    /// wedge. The two-dimensional polygon, given a length.
    /// </summary>
    Prism,

    /// <summary>
    /// A closed outline revolved about one axis through an arc: a bent rod of any
    /// cross-section, a toroidal electrode, a ring of shaped section. The same
    /// two-dimensional polygon <see cref="Prism"/> takes, given a turn instead of a length.
    /// </summary>
    Revolve,
}

/// <summary>Which coordinate axis a cylinder runs along.</summary>
public enum CylinderAxis
{
    /// <summary>Along x.</summary>
    X,

    /// <summary>Along y.</summary>
    Y,

    /// <summary>Along z.</summary>
    Z,
}

/// <summary>A three-dimensional electrode, validated and reduced to SI.</summary>
/// <remarks>
/// One record with a shape discriminator rather than a hierarchy, for the same
/// reason the two-dimensional one is: an unknown or misspelled shape produces a
/// single clear error naming the permitted values, instead of a deserialiser
/// exception naming a .NET type nobody has heard of.
/// </remarks>
public sealed record CompiledElectrode3D
{
    /// <summary>A name, used in reporting and as the basis-field label.</summary>
    public required string Name { get; init; }

    /// <summary>Which shape this is.</summary>
    public required Electrode3DShape Shape { get; init; }

    /// <summary>Box bounds, in metres.</summary>
    public double MinX { get; init; }

    /// <summary>Box bounds, in metres.</summary>
    public double MinY { get; init; }

    /// <summary>Box bounds, in metres.</summary>
    public double MinZ { get; init; }

    /// <summary>Box bounds, in metres.</summary>
    public double MaxX { get; init; }

    /// <summary>Box bounds, in metres.</summary>
    public double MaxY { get; init; }

    /// <summary>Box bounds, in metres.</summary>
    public double MaxZ { get; init; }

    /// <summary>Sphere or cylinder centre, in metres.</summary>
    public double CentreX { get; init; }

    /// <summary>Sphere or cylinder centre, in metres.</summary>
    public double CentreY { get; init; }

    /// <summary>Sphere or cylinder centre, in metres.</summary>
    public double CentreZ { get; init; }

    /// <summary>Sphere or cylinder radius, in metres.</summary>
    public double Radius { get; init; }

    /// <summary>Which axis a cylinder runs along.</summary>
    public CylinderAxis Axis { get; init; }

    /// <summary>
    /// The outline's vertices in its own plane, in meters, in order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>For a prism</b> the plane is the cross-section, whose two coordinates are the two
    /// world axes other than <see cref="Axis"/>, in world order: (y, z) for a prism along x,
    /// (x, z) along y, (x, y) along z.
    /// </para>
    /// <para>
    /// <b>For a revolve the plane contains the axis</b>, so the two coordinates are the
    /// distance from it and the position along it, in that order - the same (radius, axial)
    /// half-plane an axisymmetric solve works in. A vertex at a negative radius is refused
    /// rather than reflected, because a profile crossing its own axis of revolution sweeps a
    /// solid that overlaps itself and no signed distance describes it.
    /// </para>
    /// </remarks>
    public IReadOnlyList<(double X, double Y)> Vertices { get; init; } = [];

    /// <summary>Where a revolved outline's sweep begins, in half turns about its axis.</summary>
    /// <remarks>
    /// Half turns rather than radians, the convention the drive decomposition, the expression
    /// grammar and the box tilt already use: <c>double.CosPi</c> is exact at every quarter
    /// turn where <c>Math.Cos(Math.PI / 2)</c> is 6.1e-17, and a rod meant to start on an axis
    /// would otherwise start a rounding off it.
    /// </remarks>
    public double FromHalfTurns { get; init; }

    /// <summary>Where a revolved outline's sweep ends, in half turns about its axis.</summary>
    public double ToHalfTurns { get; init; }

    /// <summary>Whether a revolved outline closes on itself, so that it has no end caps.</summary>
    private bool SweepsFully =>
        Math.Abs(ToHalfTurns - FromHalfTurns) >= 2.0 - 1e-12;

    /// <summary>Which axis a box is tilted about, through its own centre.</summary>
    public CylinderAxis TiltAxis { get; init; }

    /// <summary>How far a box is tilted about <see cref="TiltAxis"/>, in half turns.</summary>
    /// <remarks>
    /// <para>
    /// <b>Boxes only, and a small angle is the point.</b> Every primitive here is
    /// axis-aligned, which builds the devices the specification's table asks for and
    /// cannot express a plate that is deliberately <i>not</i> parallel to another. An
    /// asymmetric-track analyser is exactly that: its two mirrors converge by a couple of
    /// hundred microns over a third of a metre, and that convergence is the whole mechanism
    /// - it is what makes the drift decelerate and reverse. Without it the model is a
    /// generic multi-reflection analyser wearing the right dimensions.
    /// </para>
    /// <para>
    /// <b>Half turns rather than radians</b>, the convention the drive decomposition and the
    /// expression grammar already chose, and for the same reason: <c>double.CosPi</c> is
    /// exact at every quarter turn, where <c>Math.Cos(Math.PI / 2)</c> is 6.1e-17 and would
    /// tilt a nominally upright plate by a rounding.
    /// </para>
    /// <para>
    /// <b>A tilt below one cell is resolved, not lost</b>, because the surface is a cut cell:
    /// Shortley-Weller stores how far a conductor is as a fraction of a cell, so a 200 um
    /// convergence on a 1.2 mm mesh is carried rather than rasterised away. That is the same
    /// property that made the shape derivative in FLD-1 legible, met again.
    /// </para>
    /// </remarks>
    public double TiltHalfTurns { get; init; }

    /// <summary>Whether this electrode is tilted at all.</summary>
    private bool IsTilted => Shape == Electrode3DShape.Box && TiltHalfTurns != 0.0;

    /// <summary>Takes a point into the box's own frame, where it is axis-aligned again.</summary>
    /// <remarks>
    /// The whole of what a tilt costs: rotate the query by minus the tilt about the box's
    /// centre and every existing formula applies unchanged. Written this way rather than as
    /// a new shape because a tilted box <i>is</i> a box - giving it its own signed-distance
    /// and first-entry code would be two implementations of one solid, which is how the two
    /// disagree later.
    /// </remarks>
    private (double X, double Y, double Z) ToLocal(double x, double y, double z)
    {
        if (!IsTilted)
        {
            return (x, y, z);
        }

        var centreX = 0.5 * (MinX + MaxX);
        var centreY = 0.5 * (MinY + MaxY);
        var centreZ = 0.5 * (MinZ + MaxZ);

        var dx = x - centreX;
        var dy = y - centreY;
        var dz = z - centreZ;

        // Minus the tilt: the box is declared tilted, so a world point comes back by
        // rotating the other way.
        var cos = double.CosPi(TiltHalfTurns);
        var sin = double.SinPi(TiltHalfTurns);

        return TiltAxis switch
        {
            CylinderAxis.X => (
                x,
                centreY + (dy * cos) + (dz * sin),
                centreZ - (dy * sin) + (dz * cos)),

            CylinderAxis.Y => (
                centreX + (dx * cos) - (dz * sin),
                y,
                centreZ + (dx * sin) + (dz * cos)),

            CylinderAxis.Z => (
                centreX + (dx * cos) + (dy * sin),
                centreY - (dx * sin) + (dy * cos),
                z),

            _ => throw Unhandled(),
        };
    }

    /// <summary>Lower end of a cylinder along its axis, in metres.</summary>
    public double Lower { get; init; }

    /// <summary>Upper end of a cylinder along its axis, in metres.</summary>
    public double Upper { get; init; }

    /// <summary>The potential held, in volts. The DC part when driven.</summary>
    public double Potential { get; init; }

    /// <summary>Every generator this electrode is tapped off, in declaration order.</summary>
    public IReadOnlyList<CompiledTap> Taps { get; init; } = [];

    /// <summary>This electrode's share of the first drive it taps, in volts.</summary>
    public double DriveAmplitude => Taps.Count > 0 ? Taps[0].Amplitude : 0.0;

    /// <summary>Where in that drive's cycle this electrode sits, as a fraction of one.</summary>
    public double DrivePhase => Taps.Count > 0 ? Taps[0].Phase : 0.0;

    /// <summary>Whether this electrode's potential varies in time.</summary>
    public bool IsDriven => Taps.Any(t => t.Amplitude != 0.0);

    /// <summary>
    /// The smallest half-extent of this electrode, in metres.
    /// </summary>
    /// <remarks>
    /// What decides how far a multigrid hierarchy may coarsen before the electrode
    /// stops being represented. A conductor is representable while a cell is no
    /// larger than it is; past that the sub-cell machinery is still recording a
    /// surface, but on arms so short that the coefficients it produces are enormous
    /// and the coarse operator is ill-conditioned rather than merely coarse.
    /// </remarks>
    public double CharacteristicSize => Shape switch
    {
        Electrode3DShape.Sphere => Radius,
        Electrode3DShape.Cylinder => Math.Min(Radius, 0.5 * Math.Abs(Upper - Lower)),
        Electrode3DShape.Prism => Math.Min(
            0.5 * Math.Min(
                Vertices.Max(v => v.X) - Vertices.Min(v => v.X),
                Vertices.Max(v => v.Y) - Vertices.Min(v => v.Y)),
            0.5 * Math.Abs(Upper - Lower)),
        Electrode3DShape.Box => Math.Min(
            Math.Abs(MaxX - MinX),
            Math.Min(Math.Abs(MaxY - MinY), Math.Abs(MaxZ - MinZ))) * 0.5,

        // The profile's smallest half-extent, and the swept arc's own length where the
        // sweep is short enough to be the thinner dimension - a 2 degree segment of a fat
        // torus is a thin thing however wide its profile is.
        Electrode3DShape.Revolve => Math.Min(
            0.5 * Math.Min(
                Vertices.Max(v => v.X) - Vertices.Min(v => v.X),
                Vertices.Max(v => v.Y) - Vertices.Min(v => v.Y)),
            0.5 * Vertices.Min(v => v.X) * Math.Abs(ToHalfTurns - FromHalfTurns) * Math.PI),

        _ => throw Unhandled(),
    };

    /// <summary>
    /// A point guaranteed to be inside this electrode, for a level too coarse to
    /// contain one of its nodes.
    /// </summary>
    /// <remarks>
    /// Its centre, which every one of these shapes is convex about. Used so that a
    /// coarse multigrid level can still say the electrode is <em>there</em>: an
    /// electrode that rasterises to no nodes at all has stopped being part of the
    /// problem, and the coarse grid then solves a different one.
    /// </remarks>
    public (double X, double Y, double Z) Centre => Shape switch
    {
        Electrode3DShape.Sphere => (CentreX, CentreY, CentreZ),

        Electrode3DShape.Box => (
            0.5 * (MinX + MaxX), 0.5 * (MinY + MaxY), 0.5 * (MinZ + MaxZ)),

        Electrode3DShape.Cylinder => Axis switch
        {
            CylinderAxis.X => (0.5 * (Lower + Upper), CentreY, CentreZ),
            CylinderAxis.Y => (CentreX, 0.5 * (Lower + Upper), CentreZ),
            _ => (CentreX, CentreY, 0.5 * (Lower + Upper)),
        },
        Electrode3DShape.Prism => ToWorld(
            0.5 * (Lower + Upper),
            0.5 * (Vertices.Min(v => v.X) + Vertices.Max(v => v.X)),
            0.5 * (Vertices.Min(v => v.Y) + Vertices.Max(v => v.Y))),

        Electrode3DShape.Revolve => RevolveCenter(),

        _ => throw Unhandled(),
    };

    /// <summary>The smallest box containing this electrode, in metres.</summary>
    /// <remarks>
    /// <para>
    /// Beside <see cref="Centre"/> and <see cref="CharacteristicSize"/>, and here for the
    /// same reason: a caller that needs to know where an electrode <em>is</em> must ask the
    /// electrode rather than switch on its shape, or architecture invariant 2 is broken one
    /// caller at a time and a fourth shape needs a change in every one of them.
    /// </para>
    /// <para>
    /// What wanted it was the viewport, which extracts a conductor's surface as the zero
    /// level set of this type's own signed distance. Sampling that over the whole solve
    /// domain misses anything thinner than a cell - a 1 mm plate in a 60 mm box at 48 cells
    /// falls between lattice planes and comes out as no surface at all, which is what it
    /// did.
    /// </para>
    /// </remarks>
    public (double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)
        Bounds => Shape switch
    {
        Electrode3DShape.Sphere => (
            CentreX - Radius, CentreY - Radius, CentreZ - Radius,
            CentreX + Radius, CentreY + Radius, CentreZ + Radius),

        Electrode3DShape.Box => TiltedBounds(),

        Electrode3DShape.Prism => PrismBounds(),
        Electrode3DShape.Revolve => RevolveBounds(),
        Electrode3DShape.Cylinder => Axis switch
        {
            CylinderAxis.X => (
                Math.Min(Lower, Upper), CentreY - Radius, CentreZ - Radius,
                Math.Max(Lower, Upper), CentreY + Radius, CentreZ + Radius),

            CylinderAxis.Y => (
                CentreX - Radius, Math.Min(Lower, Upper), CentreZ - Radius,
                CentreX + Radius, Math.Max(Lower, Upper), CentreZ + Radius),

            _ => (
                CentreX - Radius, CentreY - Radius, Math.Min(Lower, Upper),
                CentreX + Radius, CentreY + Radius, Math.Max(Lower, Upper)),
        },

        _ => throw Unhandled(),
    };

    /// <summary>
    /// The failure for a shape no member of this type knows about.
    /// </summary>
    /// <remarks>
    /// Every shape-dispatching member here names all three cases and throws on the
    /// rest, rather than letting one of them stand as a default. Defaults were how
    /// two of them came to disagree - the size switch fell through to a box and the
    /// centre switch to a sphere - and a fourth shape would have been sized as one
    /// thing and centred as another with no diagnostic anywhere. Unreachable through
    /// the document format, which rejects an unknown shape at parse.
    /// </remarks>
    private ArgumentOutOfRangeException Unhandled() =>
        new(nameof(Shape), Shape, "unhandled electrode shape");

    /// <summary>Whether a point lies within this electrode's conductor.</summary>
    /// <param name="x">x, in metres.</param>
    /// <param name="y">y, in metres.</param>
    /// <param name="z">z, in metres.</param>
    /// <returns><see langword="true"/> when inside or on the surface.</returns>
    public bool Contains(double x, double y, double z) => SignedDistance(x, y, z) <= 0.0;

    /// <summary>The axis-aligned box that contains this box, tilted or not.</summary>
    /// <remarks>
    /// <b>The one query a rotation really changes.</b> Signed distance and first entry both
    /// work in the box's own frame and need nothing; a bounding box is a statement in world
    /// coordinates and genuinely grows. Rotating a half-extent about an axis mixes the other
    /// two, and the extent of the result is the sum of their absolute contributions - which
    /// is exact rather than a bound, since a box's corners realise it.
    /// </remarks>
    private (double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)
        TiltedBounds()
    {
        var lowX = Math.Min(MinX, MaxX);
        var lowY = Math.Min(MinY, MaxY);
        var lowZ = Math.Min(MinZ, MaxZ);
        var highX = Math.Max(MinX, MaxX);
        var highY = Math.Max(MinY, MaxY);
        var highZ = Math.Max(MinZ, MaxZ);

        if (!IsTilted)
        {
            return (lowX, lowY, lowZ, highX, highY, highZ);
        }

        var centreX = 0.5 * (lowX + highX);
        var centreY = 0.5 * (lowY + highY);
        var centreZ = 0.5 * (lowZ + highZ);

        var halfX = 0.5 * (highX - lowX);
        var halfY = 0.5 * (highY - lowY);
        var halfZ = 0.5 * (highZ - lowZ);

        var cos = Math.Abs(double.CosPi(TiltHalfTurns));
        var sin = Math.Abs(double.SinPi(TiltHalfTurns));

        (halfX, halfY, halfZ) = TiltAxis switch
        {
            CylinderAxis.X => (halfX, (halfY * cos) + (halfZ * sin), (halfY * sin) + (halfZ * cos)),
            CylinderAxis.Y => ((halfX * cos) + (halfZ * sin), halfY, (halfX * sin) + (halfZ * cos)),
            CylinderAxis.Z => ((halfX * cos) + (halfY * sin), (halfX * sin) + (halfY * cos), halfZ),
            _ => throw Unhandled(),
        };

        return (
            centreX - halfX, centreY - halfY, centreZ - halfZ,
            centreX + halfX, centreY + halfY, centreZ + halfZ);
    }

    /// <summary>
    /// Signed distance to this electrode's surface: negative inside, positive
    /// outside, zero on it.
    /// </summary>
    /// <param name="x">x, in metres.</param>
    /// <param name="y">y, in metres.</param>
    /// <param name="z">z, in metres.</param>
    /// <returns>The signed distance, in metres.</returns>
    public double SignedDistance(double x, double y, double z)
    {
        switch (Shape)
        {
            case Electrode3DShape.Sphere:
            {
                var dx = x - CentreX;
                var dy = y - CentreY;
                var dz = z - CentreZ;

                return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) - Radius;
            }

            case Electrode3DShape.Box:
            {
                // A rotation is rigid, so a distance measured in the box's own frame is the
                // distance in the world - which is why the tilt needs nothing but this.
                var (localX, localY, localZ) = ToLocal(x, y, z);

                var dx = Math.Max(MinX - localX, localX - MaxX);
                var dy = Math.Max(MinY - localY, localY - MaxY);
                var dz = Math.Max(MinZ - localZ, localZ - MaxZ);

                if (dx <= 0.0 && dy <= 0.0 && dz <= 0.0)
                {
                    return Math.Max(dx, Math.Max(dy, dz));
                }

                var ox = Math.Max(dx, 0.0);
                var oy = Math.Max(dy, 0.0);
                var oz = Math.Max(dz, 0.0);

                return Math.Sqrt((ox * ox) + (oy * oy) + (oz * oz));
            }

            case Electrode3DShape.Cylinder:
            {
                var (along, a, b) = Resolve(x, y, z);

                var da = a - CentreOf(0);
                var db = b - CentreOf(1);

                var radial = Math.Sqrt((da * da) + (db * db)) - Radius;
                var axial = Math.Max(Lower - along, along - Upper);

                if (radial <= 0.0 && axial <= 0.0)
                {
                    return Math.Max(radial, axial);
                }

                var orad = Math.Max(radial, 0.0);
                var oax = Math.Max(axial, 0.0);

                return Math.Sqrt((orad * orad) + (oax * oax));
            }

            case Electrode3DShape.Prism:
            {
                // The exact distance to an extrusion: the outline's own signed distance in
                // the cross-section and the slab's along the axis, combined as a box's
                // two-dimensional and axial parts are. Exact because the outline's
                // distance is exact, which is what lets a slot a quarter of a millimetre
                // wide be a cut cell along the whole length of a rod.
                var (along, a, b) = Resolve(x, y, z);
                var across = PolygonDistance(a, b);
                var axial = Math.Max(Lower - along, along - Upper);

                if (across <= 0.0 && axial <= 0.0)
                {
                    return Math.Max(across, axial);
                }

                var oacross = Math.Max(across, 0.0);
                var oaxial = Math.Max(axial, 0.0);
                return Math.Sqrt((oacross * oacross) + (oaxial * oaxial));
            }

            case Electrode3DShape.Revolve:
                return RevolveDistance(x, y, z);

            default:
                throw Unhandled();
        }
    }

    /// <summary>
    /// Where a segment first enters this electrode's conductor, as a fraction of it.
    /// </summary>
    /// <param name="fromX">Segment start x, in metres.</param>
    /// <param name="fromY">Segment start y, in metres.</param>
    /// <param name="fromZ">Segment start z, in metres.</param>
    /// <param name="toX">Segment end x, in metres.</param>
    /// <param name="toY">Segment end y, in metres.</param>
    /// <param name="toZ">Segment end z, in metres.</param>
    /// <returns>The fraction at which the conductor is first met, or null when missed.</returns>
    /// <remarks>
    /// Entry rather than crossing, and closed form rather than bisection, for the
    /// same two reasons as in the plane. Entry, because an electrode thinner than a
    /// cell lies wholly between two nodes and a straddle test reports nothing -
    /// which is every coarse level of a multigrid hierarchy. Closed form, because
    /// bisection can only find a crossing it already knows is bracketed.
    /// </remarks>
    public double? FirstEntry(
        double fromX, double fromY, double fromZ, double toX, double toY, double toZ)
    {
        double low;
        double high;

        switch (Shape)
        {
            case Electrode3DShape.Box:
            {
                // Both ends into the box's frame. A rotation is affine, so the segment maps
                // to a segment and the fraction along it is preserved exactly - which is
                // what lets the slab test come back unchanged and still return a fraction
                // the caller can use in world coordinates.
                var (fromLocalX, fromLocalY, fromLocalZ) = ToLocal(fromX, fromY, fromZ);
                var (toLocalX, toLocalY, toLocalZ) = ToLocal(toX, toY, toZ);

                if (!Slab(fromLocalX, toLocalX, MinX, MaxX, out var xLow, out var xHigh)
                    || !Slab(fromLocalY, toLocalY, MinY, MaxY, out var yLow, out var yHigh)
                    || !Slab(fromLocalZ, toLocalZ, MinZ, MaxZ, out var zLow, out var zHigh))
                {
                    return null;
                }

                low = Math.Max(xLow, Math.Max(yLow, zLow));
                high = Math.Min(xHigh, Math.Min(yHigh, zHigh));
                break;
            }

            case Electrode3DShape.Sphere:
            {
                if (!Quadratic(
                    fromX - CentreX, toX - fromX,
                    fromY - CentreY, toY - fromY,
                    fromZ - CentreZ, toZ - fromZ,
                    Radius, out low, out high))
                {
                    return null;
                }

                break;
            }

            case Electrode3DShape.Prism:
                return PrismEntry(fromX, fromY, fromZ, toX, toY, toZ);

            case Electrode3DShape.Revolve:
                return RevolveEntry(fromX, fromY, fromZ, toX, toY, toZ);

            case Electrode3DShape.Cylinder:
            {
                var (fromAlong, fromA, fromB) = Resolve(fromX, fromY, fromZ);
                var (toAlong, toA, toB) = Resolve(toX, toY, toZ);

                if (!Quadratic(
                    fromA - CentreOf(0), toA - fromA,
                    fromB - CentreOf(1), toB - fromB,
                    0.0, 0.0,
                    Radius, out var radialLow, out var radialHigh))
                {
                    return null;
                }

                if (!Slab(fromAlong, toAlong, Lower, Upper, out var axialLow, out var axialHigh))
                {
                    return null;
                }

                low = Math.Max(radialLow, axialLow);
                high = Math.Min(radialHigh, axialHigh);
                break;
            }

            default:
                throw Unhandled();
        }

        if (low > high || high < 0.0 || low > 1.0)
        {
            return null;
        }

        return Math.Max(low, 0.0);
    }

    /// <summary>The segment parameters over which one coordinate lies within a slab.</summary>
    private static bool Slab(double from, double to, double lower, double upper, out double low, out double high)
    {
        var delta = to - from;

        if (delta == 0.0)
        {
            // Parallel to the slab: either wholly inside it for the whole segment
            // or wholly outside for all of it.
            low = 0.0;
            high = 1.0;

            return from >= lower && from <= upper;
        }

        var a = (lower - from) / delta;
        var b = (upper - from) / delta;

        low = Math.Min(a, b);
        high = Math.Max(a, b);

        return true;
    }

    /// <summary>The segment parameters over which a point lies within a radius.</summary>
    private static bool Quadratic(
        double px, double dx, double py, double dy, double pz, double dz,
        double radius, out double low, out double high)
    {
        low = 0.0;
        high = 0.0;

        var a = (dx * dx) + (dy * dy) + (dz * dz);

        if (a == 0.0)
        {
            // No motion in the plane the radius is measured in. Inside for the
            // whole segment, or outside for all of it.
            if ((px * px) + (py * py) + (pz * pz) <= radius * radius)
            {
                low = 0.0;
                high = 1.0;
                return true;
            }

            return false;
        }

        var b = 2.0 * ((dx * px) + (dy * py) + (dz * pz));
        var c = (px * px) + (py * py) + (pz * pz) - (radius * radius);

        var discriminant = (b * b) - (4.0 * a * c);

        if (discriminant < 0.0)
        {
            return false;
        }

        var root = Math.Sqrt(discriminant);

        low = (-b - root) / (2.0 * a);
        high = (-b + root) / (2.0 * a);

        return true;
    }

    /// <summary>Splits a point into the coordinate along a cylinder's axis and the two across it.</summary>
    private (double Along, double A, double B) Resolve(double x, double y, double z) => Axis switch
    {
        CylinderAxis.X => (x, y, z),
        CylinderAxis.Y => (y, x, z),
        _ => (z, x, y),
    };

    /// <summary>The cylinder centre's component across its axis.</summary>
    private double CentreOf(int which) => Axis switch
    {
        CylinderAxis.X => which == 0 ? CentreY : CentreZ,
        CylinderAxis.Y => which == 0 ? CentreX : CentreZ,
        _ => which == 0 ? CentreX : CentreY,
    };

    /// <summary>The inverse of <see cref="Resolve"/>: an (along, a, b) triple back in world axes.</summary>
    private (double X, double Y, double Z) ToWorld(double along, double a, double b) => Axis switch
    {
        CylinderAxis.X => (along, a, b),
        CylinderAxis.Y => (a, along, b),
        _ => (a, b, along),
    };

    private (double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ) PrismBounds()
    {
        var (lowAlong, lowA, lowB) = ToWorld(Math.Min(Lower, Upper), Vertices.Min(v => v.X), Vertices.Min(v => v.Y));
        var (highAlong, highA, highB) = ToWorld(Math.Max(Lower, Upper), Vertices.Max(v => v.X), Vertices.Max(v => v.Y));
        return (lowAlong, lowA, lowB, highAlong, highA, highB);
    }

    /// <summary>Signed distance to the outline in its own plane: nearest edge, signed by the even-odd rule.</summary>
    private double PolygonDistance(double a, double b)
    {
        var count = Vertices.Count;
        var nearest = double.PositiveInfinity;
        var inside = false;

        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            var (ax, ay) = Vertices[j];
            var (bx, by) = Vertices[i];
            var ex = bx - ax;
            var ey = by - ay;
            var px = a - ax;
            var py = b - ay;
            var length2 = (ex * ex) + (ey * ey);
            var t = length2 > 0.0 ? Math.Clamp(((px * ex) + (py * ey)) / length2, 0.0, 1.0) : 0.0;
            var dx = px - (t * ex);
            var dy = py - (t * ey);
            nearest = Math.Min(nearest, (dx * dx) + (dy * dy));

            if ((ay > b) != (by > b))
            {
                var crossing = ax + ((b - ay) * ex / ey);
                if (a < crossing)
                {
                    inside = !inside;
                }
            }
        }

        var distance = Math.Sqrt(nearest);
        return inside ? -distance : distance;
    }

    private bool PolygonContains(double a, double b) => PolygonDistance(a, b) <= 0.0;

    /// <summary>
    /// How many crossings an entry test holds on the stack before it borrows an array: 1 KiB,
    /// which is a revolved outline of 62 vertices or an extruded one of 126.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An entry test is asked once per stencil arm near the metal, so it must allocate
    /// nothing.</b> The prism and revolve tests each gathered their crossings in a new
    /// <c>List&lt;double&gt;</c>, and the revolve test took each edge's roots from an iterator
    /// and its two cap angles from a new array - so building a mask allocated 5.5 kB a node on
    /// the C-trap's revolved rods and 260 bytes on the linear ion trap's prisms, against the
    /// 105 the mask keeps. Before the bounding-box cull asked each electrode only about the
    /// nodes near it, those figures were 61 kB and 4.2 kB.
    /// </para>
    /// <para>
    /// An outline too long for the stack borrows from the shared pool, which reuses one array
    /// per thread rather than allocating per call, so the test allocates nothing whatever the
    /// vertex count. <c>MaskAllocationTests</c> holds both paths to zero.
    /// </para>
    /// </remarks>
    private const int CrossingsOnStack = 128;

    /// <summary>Where a segment first enters an extruded outline, as a fraction of it.</summary>
    /// <remarks>
    /// The outline is not convex in general, so the segment may enter and leave its
    /// cross-section more than once. Every crossing of an outline edge is found in closed
    /// form; between consecutive crossings the segment is wholly inside or wholly outside the
    /// outline, decided at the midpoint; the first inside interval that also lies within the
    /// axial slab is the entry.
    /// </remarks>
    private double? PrismEntry(
        double fromX, double fromY, double fromZ, double toX, double toY, double toZ)
    {
        var (fromAlong, fromA, fromB) = Resolve(fromX, fromY, fromZ);
        var (toAlong, toA, toB) = Resolve(toX, toY, toZ);

        if (!Slab(fromAlong, toAlong, Lower, Upper, out var axialLow, out var axialHigh))
        {
            return null;
        }

        // The two ends, and at most one crossing an edge.
        var capacity = Vertices.Count + 2;

        if (capacity <= CrossingsOnStack)
        {
            return PrismEntry(
                stackalloc double[CrossingsOnStack], fromA, fromB, toA - fromA, toB - fromB, axialLow, axialHigh);
        }

        var borrowed = ArrayPool<double>.Shared.Rent(capacity);

        try
        {
            return PrismEntry(borrowed, fromA, fromB, toA - fromA, toB - fromB, axialLow, axialHigh);
        }
        finally
        {
            ArrayPool<double>.Shared.Return(borrowed);
        }
    }

    /// <summary>The prism entry test over its outline, gathering crossings in the space given.</summary>
    private double? PrismEntry(
        Span<double> crossings, double fromA, double fromB, double da, double db, double axialLow, double axialHigh)
    {
        var found = 0;
        crossings[found++] = 0.0;
        crossings[found++] = 1.0;

        var count = Vertices.Count;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            var (ax, ay) = Vertices[j];
            var (bx, by) = Vertices[i];
            var ex = bx - ax;
            var ey = by - ay;
            var denominator = (da * ey) - (db * ex);
            if (denominator == 0.0)
            {
                continue;
            }

            var qx = ax - fromA;
            var qy = ay - fromB;
            var t = ((qx * ey) - (qy * ex)) / denominator;
            var u = ((qx * db) - (qy * da)) / denominator;
            if (t > 0.0 && t < 1.0 && u >= 0.0 && u <= 1.0)
            {
                crossings[found++] = t;
            }
        }

        crossings = crossings[..found];
        crossings.Sort();
        for (var k = 0; k + 1 < crossings.Length; k++)
        {
            var start = crossings[k];
            var end = crossings[k + 1];
            if (end <= start)
            {
                continue;
            }

            var middle = 0.5 * (start + end);
            if (!PolygonContains(fromA + (da * middle), fromB + (db * middle)))
            {
                continue;
            }

            var entry = Math.Max(start, axialLow);
            var exit = Math.Min(end, axialHigh);
            if (entry <= exit && exit >= 0.0 && entry <= 1.0)
            {
                return Math.Max(entry, 0.0);
            }
        }

        return null;
    }

    /// <summary>A point inside a revolved outline: its profile's middle, halfway round.</summary>
    private (double X, double Y, double Z) RevolveCenter()
    {
        var radius = 0.5 * (Vertices.Min(v => v.X) + Vertices.Max(v => v.X));
        var axial = 0.5 * (Vertices.Min(v => v.Y) + Vertices.Max(v => v.Y));
        var middle = 0.5 * (FromHalfTurns + ToHalfTurns);

        return ToWorld(axial, radius * double.CosPi(middle), radius * double.SinPi(middle));
    }

    /// <summary>The smallest box containing a revolved outline.</summary>
    /// <remarks>
    /// <b>Exact rather than the whole annulus.</b> A quarter-turn sweep occupies a quadrant,
    /// and bounding it by the full ring would quadruple the box the viewport samples a
    /// surface over and the region a coarse level pins. The extremes of an annular sector are
    /// at its two ends and at whichever quarter turns fall inside it, which is a short list
    /// to evaluate.
    /// </remarks>
    private (double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)
        RevolveBounds()
    {
        var inner = Vertices.Min(v => v.X);
        var outer = Vertices.Max(v => v.X);

        var from = Math.Min(FromHalfTurns, ToHalfTurns);
        var to = Math.Max(FromHalfTurns, ToHalfTurns);

        var angles = new List<double> { from, to };

        for (var quarter = Math.Ceiling(from * 2.0); quarter <= to * 2.0; quarter += 1.0)
        {
            angles.Add(quarter * 0.5);
        }

        double minA = double.MaxValue, maxA = double.MinValue;
        double minB = double.MaxValue, maxB = double.MinValue;

        foreach (var angle in angles)
        {
            var c = double.CosPi(angle);
            var s = double.SinPi(angle);

            foreach (var radius in (double[])[inner, outer])
            {
                minA = Math.Min(minA, radius * c);
                maxA = Math.Max(maxA, radius * c);
                minB = Math.Min(minB, radius * s);
                maxB = Math.Max(maxB, radius * s);
            }
        }

        var (lowAlong, lowA, lowB) = ToWorld(Vertices.Min(v => v.Y), minA, minB);
        var (highAlong, highA, highB) = ToWorld(Vertices.Max(v => v.Y), maxA, maxB);

        return (
            Math.Min(lowAlong, highAlong), Math.Min(lowA, highA), Math.Min(lowB, highB),
            Math.Max(lowAlong, highAlong), Math.Max(lowA, highA), Math.Max(lowB, highB));
    }

    /// <summary>The exact signed distance to a revolved outline.</summary>
    /// <remarks>
    /// <para>
    /// <b>Inside the sweep it is the profile's own two-dimensional distance, exactly.</b> A
    /// copy of the profile stands at every angle in the sweep, so for a query at an angle
    /// within it the nearest copy is the one at that very angle - turning either way only
    /// adds arc. So this reduces to <see cref="PolygonDistance"/> in the (radius, axial)
    /// half-plane, which is the same reduction an axisymmetric solve makes for a field.
    /// </para>
    /// <para>
    /// <b>Outside it, the nearest feature is an end cap</b>, which is the filled profile
    /// standing in a half-plane. The query splits into a component in that plane and one
    /// perpendicular to it; the in-plane part goes through the outline's distance and the two
    /// combine as a box's do. That is exact at the cap face and exact along the edge where
    /// the cap meets the swept surface, which is where a cut cell needs it.
    /// </para>
    /// </remarks>
    private double RevolveDistance(double x, double y, double z)
    {
        var (along, a, b) = Resolve(x, y, z);
        var radius = Math.Sqrt((a * a) + (b * b));

        if (SweepsFully)
        {
            return PolygonDistance(radius, along);
        }

        var from = Math.Min(FromHalfTurns, ToHalfTurns);
        var to = Math.Max(FromHalfTurns, ToHalfTurns);

        // Half turns in (-1, 1], brought up into [from, from + 2) so that "within the
        // sweep" is a plain interval test rather than a case analysis about wrapping.
        var angle = double.Atan2Pi(b, a);

        while (angle < from)
        {
            angle += 2.0;
        }

        while (angle >= from + 2.0)
        {
            angle -= 2.0;
        }

        if (angle <= to)
        {
            return PolygonDistance(radius, along);
        }

        var cap = angle - to <= from + 2.0 - angle ? to : from;

        var c = double.CosPi(cap);
        var s = double.SinPi(cap);

        var inPlane = (a * c) + (b * s);
        var outOfPlane = (b * c) - (a * s);

        var across = Math.Max(PolygonDistance(inPlane, along), 0.0);

        return Math.Sqrt((across * across) + (outOfPlane * outOfPlane));
    }

    /// <summary>Where a segment first enters a revolved outline, as a fraction of it.</summary>
    /// <remarks>
    /// <para>
    /// <b>Closed form, because every edge of the profile revolves into a quadric.</b> A
    /// slanted edge sweeps a cone, one parallel to the axis a cylinder, one perpendicular to
    /// it an annular disc - and a segment meets each of those at the roots of a quadratic.
    /// So this is the prism's method with the edge lines replaced by the surfaces they turn
    /// into: find every crossing exactly, then decide each interval between consecutive
    /// crossings at its midpoint, since between two crossings the segment is wholly inside
    /// or wholly outside.
    /// </para>
    /// <para>
    /// <b>The algebra admits a cone's mirror image and the geometry does not.</b> Squaring
    /// the profile radius loses its sign, so a root can land where the swept radius would be
    /// negative - on the reflected half of the double cone, which is not part of the
    /// electrode. Those are dropped by checking the radius the edge implies is not negative.
    /// </para>
    /// </remarks>
    private double? RevolveEntry(
        double fromX, double fromY, double fromZ, double toX, double toY, double toZ)
    {
        // The two ends, at most two roots an edge, and the two caps. See CrossingsOnStack.
        var capacity = (2 * Vertices.Count) + 4;

        if (capacity <= CrossingsOnStack)
        {
            return RevolveEntry(stackalloc double[CrossingsOnStack], fromX, fromY, fromZ, toX, toY, toZ);
        }

        var borrowed = ArrayPool<double>.Shared.Rent(capacity);

        try
        {
            return RevolveEntry(borrowed, fromX, fromY, fromZ, toX, toY, toZ);
        }
        finally
        {
            ArrayPool<double>.Shared.Return(borrowed);
        }
    }

    /// <summary>The revolve entry test, gathering crossings in the space given.</summary>
    private double? RevolveEntry(
        Span<double> crossings, double fromX, double fromY, double fromZ, double toX, double toY, double toZ)
    {
        var (fromAlong, fromA, fromB) = Resolve(fromX, fromY, fromZ);
        var (toAlong, toA, toB) = Resolve(toX, toY, toZ);

        var dAlong = toAlong - fromAlong;
        var dA = toA - fromA;
        var dB = toB - fromB;

        var found = 0;
        crossings[found++] = 0.0;
        crossings[found++] = 1.0;

        var count = Vertices.Count;

        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            var (r0, h0) = Vertices[j];
            var (r1, h1) = Vertices[i];

            var dh = h1 - h0;

            // FLAT TO WITHIN WHAT THE COORDINATES CARRY, not flat exactly - and testing that
            // against zero is what made a symmetric electrode solve to an asymmetric field.
            // An outline is written as expressions, so a face meant to be flat comes out flat
            // only to rounding: the C-trap's inner rod has its last hyperbola vertex at
            // 3.0000000000000009 mm meeting a corner at 3.0000000000000001, an edge sloping by
            // 8.7e-19 m. Compared with zero that is not a disc but a cone of slope 2e15, whose
            // quadratic is so ill-conditioned that the root is lost and the crossing simply
            // disappears. The same rod's other face WAS exactly flat, because -rodHalfWidth and
            // the run's first vertex round the same way - so one side of a mirror-symmetric
            // electrode got its cut cell and the other did not, and a field with a plane of
            // symmetry came out 11 percent asymmetric across it. The conductor mask was
            // right, which is what made it hard to see.
            var scale = Math.Abs(h0) + Math.Abs(h1) + Math.Abs(r1 - r0);

            if (Math.Abs(dh) <= 1e-12 * scale)
            {
                // An annular disc: one plane, met once, at a radius the edge has to span.
                if (dAlong == 0.0)
                {
                    continue;
                }

                var flat = (h0 - fromAlong) / dAlong;

                if (flat > 0.0 && flat < 1.0)
                {
                    crossings[found++] = flat;
                }

                continue;
            }

            // On the edge the radius is a linear function of the axial coordinate, so the
            // swept surface is a^2 + b^2 = (g0 + g1 t)^2 - a quadratic in the segment
            // parameter whichever of cone, cylinder or plane it happens to be.
            var slope = (r1 - r0) / dh;
            var g0 = r0 + (slope * (fromAlong - h0));
            var g1 = slope * dAlong;

            var roots = Roots(
                (dA * dA) + (dB * dB) - (g1 * g1),
                2.0 * ((fromA * dA) + (fromB * dB) - (g0 * g1)),
                (fromA * fromA) + (fromB * fromB) - (g0 * g0),
                out var first,
                out var second);

            if (roots > 0 && OnEdge(first, g0, g1, fromAlong, dAlong, h0, dh))
            {
                crossings[found++] = first;
            }

            if (roots > 1 && OnEdge(second, g0, g1, fromAlong, dAlong, h0, dh))
            {
                crossings[found++] = second;
            }
        }

        if (!SweepsFully)
        {
            for (var side = 0; side < 2; side++)
            {
                var cap = side == 0 ? FromHalfTurns : ToHalfTurns;
                var c = double.CosPi(cap);
                var s = double.SinPi(cap);

                var offset = (fromB * c) - (fromA * s);
                var rate = (dB * c) - (dA * s);

                if (rate == 0.0)
                {
                    continue;
                }

                var crossing = -offset / rate;

                if (crossing > 0.0 && crossing < 1.0)
                {
                    crossings[found++] = crossing;
                }
            }
        }

        crossings = crossings[..found];
        crossings.Sort();

        for (var k = 0; k + 1 < crossings.Length; k++)
        {
            var start = crossings[k];
            var end = crossings[k + 1];

            if (end <= start)
            {
                continue;
            }

            var middle = 0.5 * (start + end);

            if (Contains(
                fromX + ((toX - fromX) * middle),
                fromY + ((toY - fromY) * middle),
                fromZ + ((toZ - fromZ) * middle)))
            {
                return Math.Max(start, 0.0);
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a root of an edge's quadratic is a crossing: inside the segment, on the swept
    /// side of the axis rather than the cone's mirror image, and within the edge's own span.
    /// </summary>
    private static bool OnEdge(double root, double g0, double g1, double fromAlong, double dAlong, double h0, double dh)
    {
        if (root <= 0.0 || root >= 1.0 || g0 + (g1 * root) < 0.0)
        {
            return false;
        }

        var fraction = (fromAlong + (dAlong * root) - h0) / dh;

        return fraction >= 0.0 && fraction <= 1.0;
    }

    /// <summary>The real roots of a quadratic, which may be linear when its leading term is not there.</summary>
    /// <returns>
    /// How many roots there are: none, one in <paramref name="first"/>, or two in the order the
    /// quadratic formula gives them, minus before plus.
    /// </returns>
    /// <remarks>
    /// Out parameters rather than an iterator, which allocated an enumerator for every sloping
    /// edge of every profile on every arm a mask asked about. See <see cref="CrossingsOnStack"/>.
    /// </remarks>
    private static int Roots(double a, double b, double c, out double first, out double second)
    {
        first = 0.0;
        second = 0.0;

        if (a == 0.0)
        {
            if (b != 0.0)
            {
                first = -c / b;
                return 1;
            }

            return 0;
        }

        var discriminant = (b * b) - (4.0 * a * c);

        if (discriminant < 0.0)
        {
            return 0;
        }

        var root = Math.Sqrt(discriminant);

        first = (-b - root) / (2.0 * a);
        second = (-b + root) / (2.0 * a);

        return 2;
    }
}
