namespace Einzel.Core.Model;

/// <summary>Which side of a solve domain a condition or an electrode refers to.</summary>
public enum GridEdge
{
    /// <summary>The x-minimum edge.</summary>
    Left,

    /// <summary>The x-maximum edge.</summary>
    Right,

    /// <summary>The y-minimum edge.</summary>
    Bottom,

    /// <summary>The y-maximum edge.</summary>
    Top,
}

/// <summary>How the solver treats an edge of the domain.</summary>
public enum BoundaryKind
{
    /// <summary>The edge holds whatever potential the electrodes put there.</summary>
    Dirichlet,

    /// <summary>Zero normal derivative: a symmetry plane, or a wall the field is parallel to.</summary>
    Neumann,
}

/// <summary>The shapes an electrode may take in a two-dimensional solve.</summary>
/// <remarks>
/// <para>
/// Three primitives, chosen for coverage rather than convenience. A rectangle
/// gives plates, apertures, and any stripe of a printed board. A disc gives round
/// rods, which is what a quadrupole, hexapole, or octopole cross-section is. An
/// edge profile gives a potential that varies along a boundary, which is how a
/// printed-circuit mirror applies its ramp.
/// </para>
/// <para>
/// LIB-1's test is whether a new device needs a change below Einzel.Library. A
/// mirror and a quadrupole differ here only in which of these they use and where
/// they put them, which is the point.
/// </para>
/// </remarks>
public enum ElectrodeShape
{
    /// <summary>An axis-aligned block held at one potential.</summary>
    Rectangle,

    /// <summary>A filled circle held at one potential. A rod, in cross-section.</summary>
    Disc,

    /// <summary>
    /// A domain edge whose potential varies along it, given as a piecewise-linear
    /// profile.
    /// </summary>
    EdgeProfile,

    /// <summary>
    /// A closed polygon held at one potential, given by its vertices in order. The
    /// general cross-section: a hyperbolic rod face, a rod with a slot cut through
    /// it, a wedge, anything a rectangle or a disc cannot say.
    /// </summary>
    /// <remarks>
    /// Convexity is not required and the winding direction does not matter; the
    /// inside is decided by the even-odd rule. A curved face is a polyline, and the
    /// error is the sagitta of each chord, which for a hyperbolic quadrupole rod
    /// sampled every quarter millimetre is about two microns - two orders below the
    /// cell it sits in.
    /// </remarks>
    Polygon,
}

/// <summary>One point of a piecewise-linear potential profile along an edge.</summary>
/// <param name="At">Position along the edge.</param>
/// <param name="Potential">Potential held there.</param>
public sealed record ProfilePointDocument(QuantityValue? At, QuantityValue? Potential);

/// <summary>
/// One vertex of a polygon electrode, or a run of them: a position in the
/// cross-section plane, optionally repeated with an index bound.
/// </summary>
/// <param name="X">The vertex's x, a length. May be an expression over the parameter surface, and over <see cref="Index"/> in a run.</param>
/// <param name="Y">The vertex's y, a length. May be an expression over the parameter surface, and over <see cref="Index"/> in a run.</param>
/// <remarks>
/// A curved face is a run: one pair of expressions evaluated <see cref="Count"/>
/// times with <see cref="Index"/> bound from zero to <c>count - 1</c>, so a
/// hyperbolic quadrupole rod is twenty-five points written once rather than
/// twenty-five times. The same idea as an electrode's <c>repeat</c>, applied to
/// the outline of a single electrode.
/// </remarks>
public sealed record VertexDocument(QuantityValue? X, QuantityValue? Y)
{
    /// <summary>
    /// How many vertices this entry stands for. Absent for a single vertex; at least
    /// two for a run.
    /// </summary>
    public QuantityValue? Count { get; init; }

    /// <summary>
    /// The name the run's index is bound to in <see cref="X"/> and <see cref="Y"/>,
    /// from zero to <see cref="Count"/> minus one. Defaults to <c>index</c>. It may
    /// not shadow a declared parameter.
    /// </summary>
    public string? Index { get; init; }
}

/// <summary>
/// An electrode that can tap the generators its solve declares.
/// </summary>
/// <remarks>
/// Shared by the two-dimensional and three-dimensional electrode documents so the tap
/// validation is one implementation rather than two. The alternative was to copy it,
/// and a computation copied across a seam is exactly how a declared gas came to take
/// part in a run and not in a figure of merit.
/// </remarks>
public interface ITappedElectrode
{
    /// <summary>Amplitude on the first declared drive, for the single-generator form.</summary>
    QuantityValue? DriveAmplitude { get; }

    /// <summary>Phase on the first declared drive, in turns.</summary>
    QuantityValue? DrivePhase { get; }

    /// <summary>One term per generator this electrode is fed by, or null for the short form.</summary>
    IReadOnlyList<TapTermDocument>? Taps { get; }
}

/// <summary>How one electrode is connected to one generator.</summary>
/// <remarks>
/// The long form of <c>driveAmplitude</c> and <c>drivePhase</c>, needed when a
/// geometry has more than one generator or when an electrode is fed by more than
/// one. The short form remains for the common case of a single drive.
/// </remarks>
public sealed record TapTermDocument
{
    /// <summary>
    /// Which declared drive, by name. May be omitted where there is only one.
    /// </summary>
    public string? Drive { get; init; }

    /// <summary>This electrode's share of that drive, zero to peak.</summary>
    public QuantityValue? Amplitude { get; init; }

    /// <summary>
    /// Where in that drive's cycle it sits, as a fraction of one. Zero when omitted.
    /// </summary>
    public QuantityValue? Phase { get; init; }
}

/// <summary>An electrode, as it appears in a model document.</summary>
public sealed record ElectrodeDocument : ITappedElectrode
{
    /// <summary>
    /// What the electrode is called: how a lost ion, a channel and an error name it, so it
    /// must differ from every other electrode's in the same solve. Omitted, it is
    /// <c>electrode-N</c> after its place in the list.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>One of <c>rectangle</c>, <c>disc</c>, <c>polygon</c>, or <c>edgeProfile</c>.</summary>
    public string? Shape { get; init; }

    /// <summary>Rectangle: lower x bound.</summary>
    public QuantityValue? MinX { get; init; }

    /// <summary>Rectangle: lower y bound.</summary>
    public QuantityValue? MinY { get; init; }

    /// <summary>Rectangle: upper x bound.</summary>
    public QuantityValue? MaxX { get; init; }

    /// <summary>Rectangle: upper y bound.</summary>
    public QuantityValue? MaxY { get; init; }

    /// <summary>Disc: centre x.</summary>
    public QuantityValue? CentreX { get; init; }

    /// <summary>Disc: centre y.</summary>
    public QuantityValue? CentreY { get; init; }

    /// <summary>Disc: radius.</summary>
    public QuantityValue? Radius { get; init; }

    /// <summary>Edge profile: which edge, one of <c>left</c>, <c>right</c>, <c>bottom</c>, <c>top</c>.</summary>
    public string? Edge { get; init; }

    /// <summary>Edge profile: the piecewise-linear potential along the edge.</summary>
    public IReadOnlyList<ProfilePointDocument>? Profile { get; init; }

    /// <summary>
    /// Polygon: the vertices, in order round the outline, at least three. The
    /// outline closes itself from the last back to the first.
    /// </summary>
    public IReadOnlyList<VertexDocument>? Vertices { get; init; }

    /// <summary>
    /// Repeats this electrode, binding an index its expressions can name.
    /// </summary>
    /// <remarks>
    /// A stacked-ring guide is one ring written once. Without this, a funnel is two
    /// hundred near-identical blocks of JSON that no one can read and a sweep cannot
    /// perturb - and "move every ring 50 microns" stops being sayable, which is the
    /// whole point of the parametric format.
    /// </remarks>
    public RepeatDocument? Repeat { get; init; }

    /// <summary>
    /// Amplitude of this electrode's share of the drive, zero to peak. Signed:
    /// a negative amplitude is the same as a half-cycle of phase.
    /// </summary>
    public QuantityValue? DriveAmplitude { get; init; }

    /// <summary>
    /// Where in the cycle this electrode sits, as a fraction of one. Zero when
    /// omitted; a half is antiphase.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A fraction rather than radians or degrees, because every use of it is a
    /// fraction: a quadrupole pair is a half out, a three-phase guide is a third,
    /// and a travelling wave is a ramp from zero to one along its length.
    /// </para>
    /// <para>
    /// <b>An expression, like every other placement.</b> It was a plain number
    /// until a travelling-wave guide needed one, and that is precisely the case a
    /// ramp along the length exists for - a phase that cannot depend on the repeat
    /// index cannot ramp, so the device this field was documented for was the one
    /// device it could not express. Section 9 already says every placement is an
    /// expression; this was the one that had been missed.
    /// </para>
    /// </remarks>
    public QuantityValue? DrivePhase { get; init; }

    /// <summary>
    /// Every generator this electrode is tapped off, where one is not enough.
    /// Mutually exclusive with <c>driveAmplitude</c>.
    /// </summary>
    /// <remarks>
    /// A ring in a travelling-wave guide carries a fast confining RF and a slow
    /// travelling wave at once; a trap endcap carries a supplementary excitation
    /// while the ring carries the main drive. Neither is expressible with one
    /// amplitude and one phase.
    /// </remarks>
    public IReadOnlyList<TapTermDocument>? Taps { get; init; }

    /// <summary>Rectangle and disc: the potential held. The DC part, when driven.</summary>
    public QuantityValue? Potential { get; init; }
}

/// <summary>One electrode's connection to one generator, validated and in SI.</summary>
/// <param name="Drive">
/// Which declared drive, by index into the solve's <c>Drives</c>. Zero where a
/// geometry declares only one.
/// </param>
/// <param name="Amplitude">Its share of that drive, zero to peak, in volts.</param>
/// <param name="Phase">Where in that drive's cycle it sits, as a fraction of one.</param>
/// <remarks>
/// A list rather than a single amplitude and phase, because a real electrode can be
/// fed by more than one generator: a travelling-wave guide's rings carry a fast
/// confining RF and a slow travelling wave at once, and a trap's endcaps carry a
/// supplementary excitation while its ring carries the main drive.
/// </remarks>
public readonly record struct CompiledTap(int Drive, double Amplitude, double Phase);

/// <summary>An electrode, validated and reduced to SI.</summary>
public sealed record CompiledElectrode
{
    /// <summary>The electrode's name.</summary>
    public required string Name { get; init; }

    /// <summary>Which primitive this is.</summary>
    public required ElectrodeShape Shape { get; init; }

    /// <summary>Rectangle bounds, in metres.</summary>
    public double MinX { get; init; }

    /// <summary>Rectangle bounds, in metres.</summary>
    public double MinY { get; init; }

    /// <summary>Rectangle bounds, in metres.</summary>
    public double MaxX { get; init; }

    /// <summary>Rectangle bounds, in metres.</summary>
    public double MaxY { get; init; }

    /// <summary>Disc centre, in metres.</summary>
    public double CentreX { get; init; }

    /// <summary>Disc centre, in metres.</summary>
    public double CentreY { get; init; }

    /// <summary>Disc radius, in metres.</summary>
    public double Radius { get; init; }

    /// <summary>Edge profile: which edge.</summary>
    public GridEdge Edge { get; init; }

    /// <summary>Edge profile: position and potential pairs, in SI, sorted by position.</summary>
    public IReadOnlyList<(double At, double Potential)> Profile { get; init; } = [];

    /// <summary>
    /// Polygon: the vertices in order round the outline, in metres. For a polygon
    /// <see cref="MinX"/>, <see cref="MinY"/>, <see cref="MaxX"/> and <see cref="MaxY"/>
    /// hold its bounding box.
    /// </summary>
    public IReadOnlyList<(double X, double Y)> Vertices { get; init; } = [];

    /// <summary>Rectangle and disc: the potential held, in volts. The DC part.</summary>
    public double Potential { get; init; }

    /// <summary>Every generator this electrode is tapped off, in declaration order.</summary>
    public IReadOnlyList<CompiledTap> Taps { get; init; } = [];

    /// <summary>
    /// This electrode's share of the first drive it taps, zero to peak, in volts.
    /// Zero for an electrode that only holds a DC potential.
    /// </summary>
    /// <remarks>
    /// A convenience for the common single-generator case and for diagnostics.
    /// Anything that evaluates or decomposes the field uses <see cref="Taps"/>,
    /// because taking the first of several would silently drop the rest.
    /// </remarks>
    public double DriveAmplitude => Taps.Count > 0 ? Taps[0].Amplitude : 0.0;

    /// <summary>Where in that drive's cycle this electrode sits, as a fraction of one.</summary>
    public double DrivePhase => Taps.Count > 0 ? Taps[0].Phase : 0.0;

    /// <summary>
    /// Whether this electrode's potential varies in time at all.
    /// </summary>
    /// <remarks>
    /// A driven geometry still contains undriven electrodes - a housing, a lens
    /// stack - and they cost nothing extra, because an electrode whose potential is
    /// constant shares a basis solve with every other constant one.
    /// </remarks>
    public bool IsDriven => Taps.Any(t => t.Amplitude != 0.0);

    /// <summary>
    /// Signed distance to this electrode's surface: negative inside the
    /// conductor, positive outside, zero on it.
    /// </summary>
    /// <param name="x">x, in metres.</param>
    /// <param name="y">y, in metres.</param>
    /// <returns>
    /// The signed distance, in metres, or positive infinity for a shape that has
    /// no sub-cell surface to locate.
    /// </returns>
    /// <remarks>
    /// <para>
    /// What lets the solver place a boundary between nodes rather than snapping it
    /// to the nearest one. A rasterised boundary moves in whole cells, which makes
    /// the discrete operator a staircase function of electrode position — fatal
    /// for shape derivatives, and the reason the FLD-1 spike failed. With a signed
    /// distance the crossing point on each grid segment is known continuously, and
    /// the operator moves with it.
    /// </para>
    /// <para>
    /// Edge profiles return infinity: they lie along a domain edge, exactly on
    /// grid lines, so there is no sub-cell position for them to occupy.
    /// </para>
    /// </remarks>
    public double SignedDistance(double x, double y)
    {
        switch (Shape)
        {
            case ElectrodeShape.Disc:
            {
                var dx = x - CentreX;
                var dy = y - CentreY;
                return Math.Sqrt((dx * dx) + (dy * dy)) - Radius;
            }

            case ElectrodeShape.Rectangle:
            {
                // Distance to an axis-aligned box: outside is the length of the
                // positive part, inside is the largest negative coordinate.
                var dx = Math.Max(MinX - x, x - MaxX);
                var dy = Math.Max(MinY - y, y - MaxY);

                if (dx <= 0.0 && dy <= 0.0)
                {
                    return Math.Max(dx, dy);
                }

                var ox = Math.Max(dx, 0.0);
                var oy = Math.Max(dy, 0.0);
                return Math.Sqrt((ox * ox) + (oy * oy));
            }

            case ElectrodeShape.Polygon:
            {
                // Exact: the distance to the nearest edge, signed by the even-odd
                // rule. Both are closed form, so a slot a quarter of a millimetre
                // wide is located as sharply as a rod's face - which is what lets
                // the cut cells resolve it below one cell rather than rasterising
                // it into whichever row of nodes it happens to fall on.
                var count = Vertices.Count;
                var nearest = double.PositiveInfinity;
                var inside = false;

                for (int i = 0, j = count - 1; i < count; j = i++)
                {
                    var (ax, ay) = Vertices[j];
                    var (bx, by) = Vertices[i];
                    var ex = bx - ax;
                    var ey = by - ay;
                    var px = x - ax;
                    var py = y - ay;
                    var length2 = (ex * ex) + (ey * ey);
                    var t = length2 > 0.0 ? Math.Clamp(((px * ex) + (py * ey)) / length2, 0.0, 1.0) : 0.0;
                    var dx = px - (t * ex);
                    var dy = py - (t * ey);
                    nearest = Math.Min(nearest, (dx * dx) + (dy * dy));

                    if ((ay > y) != (by > y))
                    {
                        var crossing = ax + ((y - ay) * ex / ey);
                        if (x < crossing)
                        {
                            inside = !inside;
                        }
                    }
                }

                var distance = Math.Sqrt(nearest);
                return inside ? -distance : distance;
            }

            default:
                return double.PositiveInfinity;
        }
    }

    /// <summary>Whether a point lies within this electrode's conductor.</summary>
    /// <param name="x">x, in metres.</param>
    /// <param name="y">y, in metres.</param>
    /// <returns><see langword="true"/> when inside or on the surface.</returns>
    public bool Contains(double x, double y) => SignedDistance(x, y) <= 0.0;

    /// <summary>
    /// Where an axis-aligned segment first enters this electrode's conductor, as a
    /// fraction of the segment.
    /// </summary>
    /// <param name="fromX">Segment start x, in metres.</param>
    /// <param name="fromY">Segment start y, in metres.</param>
    /// <param name="toX">Segment end x, in metres.</param>
    /// <param name="toY">Segment end y, in metres.</param>
    /// <returns>
    /// The fraction along the segment at which the conductor is first met, or null
    /// when the segment misses it entirely.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Entry rather than crossing, and closed form rather than bisection, for one
    /// reason each. Entry, because an electrode thinner than a cell lies wholly
    /// between two nodes: neither end of the segment is inside it, so a test that
    /// asks whether the endpoints straddle the surface reports nothing and the
    /// electrode disappears. That is not an exotic case — it is every coarse level
    /// of a multigrid hierarchy, which is where the geometry used to dissolve.
    /// </para>
    /// <para>
    /// Closed form, because bisection on the signed distance has the same blind
    /// spot at a different scale: it can only find a crossing it already knows is
    /// bracketed, and sampling to find the bracket would miss features smaller
    /// than the sample interval. The algebra below is exact for any segment and
    /// any thickness.
    /// </para>
    /// </remarks>
    public double? FirstEntry(double fromX, double fromY, double toX, double toY)
    {
        // The interval of the segment parameter over which each shape is entered.
        double low;
        double high;

        switch (Shape)
        {
            case ElectrodeShape.Rectangle:
            {
                if (!Slab(fromX, toX, MinX, MaxX, out var xLow, out var xHigh)
                    || !Slab(fromY, toY, MinY, MaxY, out var yLow, out var yHigh))
                {
                    return null;
                }

                low = Math.Max(xLow, yLow);
                high = Math.Min(xHigh, yHigh);
                break;
            }

            case ElectrodeShape.Disc:
            {
                var dx = toX - fromX;
                var dy = toY - fromY;
                var px = fromX - CentreX;
                var py = fromY - CentreY;

                var a = (dx * dx) + (dy * dy);
                var b = 2.0 * ((dx * px) + (dy * py));
                var c = (px * px) + (py * py) - (Radius * Radius);

                if (a == 0.0)
                {
                    return null;
                }

                var discriminant = (b * b) - (4.0 * a * c);

                if (discriminant < 0.0)
                {
                    return null;
                }

                var root = Math.Sqrt(discriminant);
                low = (-b - root) / (2.0 * a);
                high = (-b + root) / (2.0 * a);
                break;
            }

            case ElectrodeShape.Polygon:
            {
                // A segment starting inside is met at once; otherwise the first
                // crossing of any edge is the entry, because a segment that starts
                // outside cannot cross into the interior anywhere but at an edge.
                if (Contains(fromX, fromY))
                {
                    return 0.0;
                }

                var dx = toX - fromX;
                var dy = toY - fromY;
                var first = double.PositiveInfinity;
                var count = Vertices.Count;

                for (int i = 0, j = count - 1; i < count; j = i++)
                {
                    var (ax, ay) = Vertices[j];
                    var (bx, by) = Vertices[i];
                    var ex = bx - ax;
                    var ey = by - ay;
                    var denominator = (dx * ey) - (dy * ex);

                    if (denominator == 0.0)
                    {
                        // Parallel, including collinear. A collinear overlap is a
                        // segment running along the surface, and the nodes on it
                        // are already classified as conductor by SignedDistance.
                        continue;
                    }

                    var qx = ax - fromX;
                    var qy = ay - fromY;
                    var t = ((qx * ey) - (qy * ex)) / denominator;
                    var u = ((qx * dy) - (qy * dx)) / denominator;

                    if (t >= 0.0 && t <= 1.0 && u >= 0.0 && u <= 1.0)
                    {
                        first = Math.Min(first, t);
                    }
                }

                return double.IsFinite(first) ? first : null;
            }

            default:
                // An edge profile lies along a domain edge, on grid lines already.
                return null;
        }

        if (low > high || high < 0.0 || low > 1.0)
        {
            return null;
        }

        return Math.Max(low, 0.0);
    }

    /// <summary>The segment parameters over which one coordinate lies within a slab.</summary>
    /// <remarks>
    /// A segment along one axis holds the other coordinate constant, so that
    /// coordinate's slab test has no parameter interval at all: it either admits
    /// the whole line or none of it. Returning an unbounded interval for the
    /// constant case lets the caller intersect the two axes uniformly.
    /// </remarks>
    private static bool Slab(double from, double to, double minimum, double maximum, out double low, out double high)
    {
        var delta = to - from;

        if (delta == 0.0)
        {
            low = double.NegativeInfinity;
            high = double.PositiveInfinity;
            return from >= minimum && from <= maximum;
        }

        var a = (minimum - from) / delta;
        var b = (maximum - from) / delta;

        low = Math.Min(a, b);
        high = Math.Max(a, b);
        return true;
    }

    /// <summary>The potential this electrode applies at a position along its edge.</summary>
    /// <param name="along">Position along the edge, in metres.</param>
    /// <returns>The potential, in volts, by linear interpolation between profile points.</returns>
    public double ProfileAt(double along)
    {
        if (Profile.Count == 0)
        {
            return Potential;
        }

        if (along <= Profile[0].At)
        {
            return Profile[0].Potential;
        }

        for (var k = 1; k < Profile.Count; k++)
        {
            if (along <= Profile[k].At)
            {
                var span = Profile[k].At - Profile[k - 1].At;
                var t = span > 0.0 ? (along - Profile[k - 1].At) / span : 0.0;
                return Profile[k - 1].Potential + (t * (Profile[k].Potential - Profile[k - 1].Potential));
            }
        }

        return Profile[^1].Potential;
    }
}

/// <summary>A two-dimensional solved field, as it appears in a model document.</summary>
/// <remarks>
/// The field element that makes a device a document rather than a class. It
/// carries the solve domain, the resolution, the edge conditions, and the
/// electrodes — everything the solver in Einzel.Fields needs, and nothing about
/// what the arrangement is called.
/// </remarks>
public sealed record SolvedFieldDocument
{
    /// <summary>
    /// Tilts the whole cross-section's extrusion axis about y, in half turns, so that
    /// 1.0 is 180 degrees. Absent or zero leaves it along z.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A cross-section solve is a two-dimensional geometry extruded along z. This tilts
    /// the direction it is extruded along, which is how an asymmetric-track analyser's
    /// converging mirrors are built: a pair of cross-sections tilted oppositely, each
    /// carrying one mirror at potential with the other grounded.
    /// </para>
    /// <para>
    /// It belongs here rather than on the electrodes, and that is a correctness matter
    /// rather than a convenience. Tilting a three-dimensional geometry and solving it
    /// cannot resolve the result: the entire signal is the field anisotropy
    /// <c>Ez/Ex = tan(alpha)</c>, 2.9e-4 for a 200 micron convergence over 350 mm, which
    /// is well below the field error of a second-order solve on any affordable mesh.
    /// Rotating a solved cross-section instead is exact, because rotations commute with
    /// the Laplacian.
    /// </para>
    /// </remarks>
    public QuantityValue? TiltHalfTurns { get; init; }

    /// <summary>The x coordinate of the tilt axis. Defaults to the domain's centre.</summary>
    public QuantityValue? TiltCentreX { get; init; }

    /// <summary>
    /// The z coordinate of the tilt axis. Defaults to zero, and matters because it sets
    /// where along the drift the geometry sits at its declared position.
    /// </summary>
    public QuantityValue? TiltCentreZ { get; init; }

    /// <summary>Lower x bound of the solve domain.</summary>
    public QuantityValue? MinX { get; init; }

    /// <summary>Lower y bound.</summary>
    public QuantityValue? MinY { get; init; }

    /// <summary>Upper x bound.</summary>
    public QuantityValue? MaxX { get; init; }

    /// <summary>Upper y bound.</summary>
    public QuantityValue? MaxY { get; init; }

    /// <summary>
    /// A timed sequence of states this geometry is operated through, if any.
    /// </summary>
    /// <remarks>
    /// The sequencer the architecture diagram calls a timed state machine. A trap
    /// fills, isolates, then extracts, and the electrode potentials are different
    /// in each - so a geometry that can only be driven one way for a whole run
    /// cannot describe a trap at all, whatever else it can do.
    /// </remarks>
    public IReadOnlyList<StageDocument>? Stages { get; init; }

    /// <summary>
    /// The RF drive this geometry is operated with, if any. Static when omitted.
    /// </summary>
    /// <remarks>
    /// One generator per solve. Electrodes tap off it through their own
    /// <c>driveAmplitude</c> and <c>drivePhase</c>; the frequency and the waveform
    /// belong to the generator.
    /// </remarks>
    public DriveDocument? Drive { get; init; }

    /// <summary>
    /// Several generators, where one is not enough. Mutually exclusive with
    /// <see cref="Drive"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A real trap runs its main RF and a supplementary excitation at once; a real
    /// travelling-wave guide superposes a fast confining RF on a slow travelling
    /// wave. Each electrode names which generators it taps, and with what amplitude
    /// and phase, through <c>taps</c>.
    /// </para>
    /// <para>
    /// It costs nothing in the solver. Basis superposition is indifferent to what
    /// the weights are functions of, so two generators reaching the same electrodes
    /// in the same proportions are one solved pattern carrying two weights on two
    /// clocks. What multiplies the solve count is a different spatial pattern, never
    /// a different frequency.
    /// </para>
    /// </remarks>
    public IReadOnlyList<DriveDocument>? Drives { get; init; }

    /// <summary>
    /// What the plane is a cross-section of: <c>translational</c> or
    /// <c>cylindrical</c>. Translational when omitted.
    /// </summary>
    /// <remarks>
    /// SYM-1. Cylindrical makes x the axis of rotation and y the radius, so the
    /// domain must lie at y greater than or equal to zero. It changes the operator,
    /// not the presentation: a field solved with the wrong one converges contentedly
    /// to the wrong answer.
    /// </remarks>
    public string? Symmetry { get; init; }

    /// <summary>Node spacing. Rounded to make the interval count a power of two.</summary>
    public QuantityValue? CellSize { get; init; }

    /// <summary>Condition on the x-minimum edge. Dirichlet by default.</summary>
    public string? LeftEdge { get; init; }

    /// <summary>Condition on the x-maximum edge.</summary>
    public string? RightEdge { get; init; }

    /// <summary>Condition on the y-minimum edge.</summary>
    public string? BottomEdge { get; init; }

    /// <summary>Condition on the y-maximum edge.</summary>
    public string? TopEdge { get; init; }

    /// <summary>The electrodes.</summary>
    public IReadOnlyList<ElectrodeDocument>? Electrodes { get; init; }

    /// <summary>
    /// Whether the field genuinely jumps at the domain edge. False where the
    /// domain was drawn wide enough that the field has decayed there.
    /// </summary>
    public bool BoundaryIsDiscontinuous { get; init; } = true;

    /// <summary>Relative residual the solve must reach.</summary>
    public double Tolerance { get; init; } = 1e-12;

    /// <summary>
    /// Reflect the solved field through a plane normal to x, at this position, and
    /// superpose it with the original.
    /// </summary>
    /// <remarks>
    /// A mirror pair is one mirror and its reflection, and expressing that here
    /// rather than in code means both halves are the same solve by construction.
    /// Omitted for a geometry that is not reflected.
    /// </remarks>
    public QuantityValue? ReflectAboutX { get; init; }
}

/// <summary>
/// Repeats an electrode a number of times, with an index bound for its expressions.
/// </summary>
/// <remarks>
/// The discrete periodicity SYM-1 lists beside cylindrical symmetry and mirror
/// planes. Each copy is compiled with <see cref="Index"/> bound to its position, so
/// every placement stays a parametric expression and the whole stack still moves
/// when one parameter does.
/// </remarks>
public sealed record RepeatDocument
{
    /// <summary>How many copies. Must be at least one.</summary>
    public QuantityValue? Count { get; init; }

    /// <summary>
    /// The name the index is bound to, running from zero. <c>index</c> when omitted.
    /// </summary>
    /// <remarks>
    /// Nameable because a document that already has a parameter called
    /// <c>index</c> should not have it shadowed silently, and because
    /// <c>ring</c> or <c>plate</c> reads better in the expressions that use it.
    /// </remarks>
    public string? Index { get; init; }
}

/// <summary>
/// One state of a timed sequence, as it appears in a model document.
/// </summary>
/// <remarks>
/// <para>
/// A stage says what changes and for how long, and what changes is expressed as
/// <em>parameter values</em> rather than as electrode settings. That is the whole
/// design: electrode potentials are already expressions over parameters, so setting
/// a parameter moves everything that depends on it at once, coherently, including
/// derived parameters and geometry. Listing electrodes instead would let a stage
/// change an amplitude while leaving the thing it was derived from behind.
/// </para>
/// <para>
/// It also means a stage costs no new vocabulary. The same override mechanism a
/// sweep or an optimiser uses to perturb a design is what a sequence uses to
/// operate one.
/// </para>
/// </remarks>
public sealed record StageDocument
{
    /// <summary>What this stage is for, in a word. Used in reporting.</summary>
    public string? Name { get; init; }

    /// <summary>How long the stage lasts.</summary>
    public QuantityValue? Duration { get; init; }

    /// <summary>
    /// Parameter values that hold during this stage, with units. Everything not
    /// named keeps the value it has outside the sequence.
    /// </summary>
    public IReadOnlyDictionary<string, QuantityValue>? Set { get; init; }

    /// <summary>
    /// Parameter values reached at the <em>end</em> of this phase, ramped linearly in
    /// time from where each stands at its start. Everything not named holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A mass-selective instability scan is a ramp: the RF amplitude rises steadily and
    /// each ion leaves when its own q reaches the ejection point. Written as phases that
    /// each hold a value, a scan is a staircase of thousands of steps; written with a
    /// ramp it is one phase. A parameter named here and in <see cref="Set"/> starts at
    /// the set value; named here alone, it starts at whatever value was in force.
    /// </para>
    /// <para>
    /// A ramp is exact where the electrodes' potentials and drive amplitudes are linear
    /// in the ramped parameter, because the solved field's channel weights are then
    /// linear in time. That is checked at the phase's midpoint and a non-linear
    /// dependence is refused - write the curve as more phases. A ramp may not move a
    /// drive's phase, may not reach an analytic element, and is not supported in a
    /// diffusive phase or on a volume solve.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, QuantityValue>? Ramp { get; init; }

    /// <summary>
    /// The transport mode this phase runs in, or absent to keep the model's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SEQ-1: "a phase boundary may change transport mode; the conversion is explicit,
    /// reported, and named as a source of uncertainty". §9 lists transport mode among
    /// what a phase carries, alongside its duration and its excitation overrides.
    /// </para>
    /// <para>
    /// That is a real instrument's ordinary behaviour rather than an exotic case: ions
    /// are collected and thermalised in a gas-filled trap, where the description is a
    /// density, then extracted into vacuum and flown, where it is trajectories.
    /// </para>
    /// <para>
    /// Absent means the model's own <c>transport.mode</c>, which is the same rule
    /// <see cref="Set"/> follows - anything a phase does not name keeps the value it has
    /// outside the sequence. A mode is a property of the run, which is why it lives on
    /// the phase rather than on an element: two elements naming different modes for one
    /// instant is not something a superposition can resolve.
    /// </para>
    /// </remarks>
    public string? Mode { get; init; }
}

/// <summary>
/// An RF drive, as it appears in a model document.
/// </summary>
public sealed record DriveDocument
{
    /// <summary>
    /// What this generator is called, so an electrode can name which one it taps.
    /// </summary>
    /// <remarks>
    /// Needed only where a geometry declares more than one. A single unnamed drive
    /// is what nearly every device has, and requiring a name for it would be
    /// ceremony.
    /// </remarks>
    public string? Name { get; init; }

    /// <summary>Drive frequency.</summary>
    public QuantityValue? Frequency { get; init; }

    /// <summary>
    /// One of <c>sinusoid</c> or <c>rectangular</c>. A sinusoid when omitted.
    /// </summary>
    /// <remarks>
    /// Not a detail of the supply. A sinusoid gives the Mathieu equation and a
    /// rectangular wave gives Meissner's, and their stability boundaries are in
    /// different places - the square-wave low-mass cut-off is q = 0.712 against a
    /// sinusoid's 0.908.
    /// </remarks>
    public string? Waveform { get; init; }

    /// <summary>
    /// Fraction of the cycle at the positive level, for a rectangular wave. One
    /// half when omitted.
    /// </summary>
    /// <remarks>
    /// Away from one half the wave carries a mean of 2d - 1, which enters the
    /// equation of motion exactly where a DC offset would. That is how a digital
    /// mass filter gets its resolution without a DC supply existing anywhere in the
    /// instrument: the working point is set by switching times.
    /// </remarks>
    public double? DutyCycle { get; init; }
}

/// <summary>One state of a timed sequence, validated and reduced to SI.</summary>
/// <param name="Name">What the stage is for.</param>
/// <param name="DurationSeconds">How long it lasts.</param>
/// <param name="Electrodes">The electrodes as they stand during it.</param>
public sealed record CompiledStage(
    string Name, double DurationSeconds, IReadOnlyList<CompiledElectrode> Electrodes)
{
    /// <summary>
    /// The electrodes as they stand at the end of the stage when it ramps, or null when
    /// it holds. Same metal in the same places as <see cref="Electrodes"/>; only what
    /// each holds differs, and linearly in time between the two.
    /// </summary>
    public IReadOnlyList<CompiledElectrode>? EndElectrodes { get; init; }
}

/// <summary>A two-dimensional solved field, validated and reduced to SI.</summary>
public sealed record CompiledSolvedField
{
    /// <summary>The extrusion-axis tilt about y, in half turns. Zero leaves it along z.</summary>
    public double TiltHalfTurns { get; init; }

    /// <summary>The x coordinate of the tilt axis, in metres.</summary>
    public double TiltCentreX { get; init; }

    /// <summary>The z coordinate of the tilt axis, in metres.</summary>
    public double TiltCentreZ { get; init; }

    /// <summary>Solve domain, in metres.</summary>
    public required double MinX { get; init; }

    /// <summary>Solve domain, in metres.</summary>
    public required double MinY { get; init; }

    /// <summary>Solve domain, in metres.</summary>
    public required double MaxX { get; init; }

    /// <summary>Solve domain, in metres.</summary>
    public required double MaxY { get; init; }

    /// <summary>Node spacing, in metres.</summary>
    public required double CellSize { get; init; }

    /// <summary>What the plane is a cross-section of (SYM-1).</summary>
    public SolveSymmetry Symmetry { get; init; }

    /// <summary>
    /// The generators this geometry is operated with. Empty when it is static.
    /// </summary>
    /// <remarks>
    /// More than one where an instrument runs a confining drive and a supplementary
    /// excitation at once. Each electrode names which it taps.
    /// </remarks>
    public IReadOnlyList<CompiledDrive> Drives { get; init; } = [];

    /// <summary>
    /// The primary drive - the first declared - or null when the geometry is static.
    /// </summary>
    /// <remarks>
    /// A convenience for the many places that only need to know whether a geometry is
    /// driven at all. Anything that evaluates the field uses <see cref="Drives"/>,
    /// because taking the first of several would silently drop the rest.
    /// </remarks>
    public CompiledDrive? Drive => Drives.Count > 0 ? Drives[0] : null;

    /// <summary>
    /// The timed sequence this geometry is operated through, or empty when it holds
    /// one state for the whole run.
    /// </summary>
    /// <remarks>
    /// Each stage carries its own compiled electrodes, because a stage changes
    /// parameters and parameters reach everything - so what differs between stages
    /// is not a list of settings but the whole geometry as it stands during that
    /// stage. The geometry itself must not actually move; that is checked.
    /// </remarks>
    public IReadOnlyList<CompiledStage> Stages { get; init; } = [];

    /// <summary>Condition on the x-minimum edge.</summary>
    public BoundaryKind LeftEdge { get; init; }

    /// <summary>Condition on the x-maximum edge.</summary>
    public BoundaryKind RightEdge { get; init; }

    /// <summary>Condition on the y-minimum edge.</summary>
    public BoundaryKind BottomEdge { get; init; }

    /// <summary>Condition on the y-maximum edge.</summary>
    public BoundaryKind TopEdge { get; init; }

    /// <summary>The electrodes.</summary>
    public required IReadOnlyList<CompiledElectrode> Electrodes { get; init; }

    /// <summary>Whether the domain edge is a genuine field discontinuity.</summary>
    public bool BoundaryIsDiscontinuous { get; init; }

    /// <summary>Relative residual for the solve.</summary>
    public double Tolerance { get; init; }

    /// <summary>Reflection plane, in metres, or null when the field is not reflected.</summary>
    public double? ReflectAboutX { get; init; }
}
