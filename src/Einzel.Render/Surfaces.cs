namespace Einzel.Render;

/// <summary>A triangle mesh, in metres, as something can draw it.</summary>
/// <param name="Vertices">Positions, as consecutive x, y, z triples.</param>
/// <param name="Normals">Outward unit normals, one triple per vertex.</param>
/// <param name="Triangles">Vertex indices, three per triangle.</param>
public sealed record SurfaceMesh(
    IReadOnlyList<double> Vertices,
    IReadOnlyList<double> Normals,
    IReadOnlyList<int> Triangles)
{
    /// <summary>How many vertices it has.</summary>
    public int VertexCount => Vertices.Count / 3;

    /// <summary>How many triangles it has.</summary>
    public int TriangleCount => Triangles.Count / 3;
}

/// <summary>
/// Conductor surfaces, as the zero level set of a signed distance.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same argument <see cref="Contours"/> makes, one dimension up.</b> An electrode
/// outline is the zero level set of its signed distance, so one routine draws every
/// conductor there is and a shape added to the model format needs no change here
/// (architecture invariant 2). Drawing from the declared primitives instead — a box as a
/// box, a cylinder as a cylinder — would be shorter today and would need a new case for
/// the fourth shape.
/// </para>
/// <para>
/// <b>Three producers, because a solve says three different things about the third
/// dimension.</b> A cross-section says the conductor repeats along the invariant axis, so
/// it is an <em>extrusion</em>; an axisymmetric half-plane says it repeats all the way
/// round, so it is a <em>revolution</em>; a volume solve says nothing, so it needs a
/// genuine surface extraction. Treating all three as the third case would be wrong rather
/// than merely slow: a cross-section's signed distance is a function of two coordinates
/// and its zero set in space is a prism of infinite length.
/// </para>
/// <para>
/// <b>Orientation comes from the field, not from the winding.</b> Every producer emits
/// positions and triangles in whatever order falls out, and <see cref="Orient"/> then sets
/// each normal to the gradient of the same signed distance that defined the surface and
/// flips any triangle that disagrees with it. That is exact and needs no reasoning about
/// which way a marching-squares run happens to run — which is worth having, because the
/// segments <see cref="Contours"/> emits are deliberately undirected.
/// </para>
/// </remarks>
public static class Surfaces
{
    /// <summary>A scalar over space, in metres in and metres out.</summary>
    /// <param name="x">Position, in metres.</param>
    /// <param name="y">Position, in metres.</param>
    /// <param name="z">Position, in metres.</param>
    /// <returns>The signed distance: negative inside, positive outside.</returns>
    public delegate double SignedDistance(double x, double y, double z);

    /// <summary>How many cells the thinnest direction of a box must get.</summary>
    /// <remarks>
    /// Four rather than two: two puts a sample plane on each face of a slab, so the
    /// extracted surface is made entirely of boundary cases and a slab comes out as a
    /// sheet with no thickness.
    /// </remarks>
    private const int MinimumCellsAcross = 4;

    /// <summary>How many cells one axis of a box gets.</summary>
    /// <param name="span">This axis, in metres.</param>
    /// <param name="longest">The longest axis of the same box, in metres.</param>
    /// <param name="cells">The count the longest axis is asked to have.</param>
    /// <returns>The count for this axis, at least <see cref="MinimumCellsAcross"/>.</returns>
    private static int Resolution(double span, double longest, int cells) =>
        Math.Clamp(
            (int)Math.Ceiling(cells * span / longest), MinimumCellsAcross, cells);

    /// <summary>Extracts the zero surface of a signed distance over a box.</summary>
    /// <param name="distance">Negative inside the conductor.</param>
    /// <param name="minX">Box corner, in metres.</param>
    /// <param name="minY">Box corner, in metres.</param>
    /// <param name="minZ">Box corner, in metres.</param>
    /// <param name="maxX">Box corner, in metres.</param>
    /// <param name="maxY">Box corner, in metres.</param>
    /// <param name="maxZ">Box corner, in metres.</param>
    /// <param name="cells">Cells along the longest axis.</param>
    /// <returns>The surface, empty when the box holds none of it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="distance"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The box is empty or the resolution is below two.</exception>
    /// <remarks>
    /// <para>
    /// <b>Surface nets rather than marching cubes</b>, which is a deliberate trade. One
    /// vertex per cell placed at the mean of that cell's edge crossings, and one quad per
    /// sign-changing lattice edge: watertight by construction, no 256-case table, and a
    /// vertex that sits where the surface is rather than on a cell edge. What it gives up
    /// is sharpness at a true crease — a box corner is rounded by about a cell — and §17
    /// is explicit that this path is screen tuning rather than an artifact, so a rounded
    /// corner costs nothing that leaves Einzel. The publication figure is a vector section
    /// and is drawn by <see cref="Contours"/> at full sharpness.
    /// </para>
    /// <para>
    /// Zero counts as inside, matching <c>Contains</c>: an ion on the surface has struck
    /// it.
    /// </para>
    /// </remarks>
    /// <summary>How many cells the thinnest direction of a box must get.</summary>
    /// <remarks>
    /// Four rather than two: two puts a sample plane on each face of a slab and the
    /// extracted surface is then entirely made of boundary cases, which marching schemes
    /// handle correctly and degenerately - a slab comes out as a sheet with no thickness.
    /// </remarks>
    public static SurfaceMesh FromSignedDistance(
        SignedDistance distance,
        double minX, double minY, double minZ,
        double maxX, double maxY, double maxZ,
        int cells = 48)
    {
        ArgumentNullException.ThrowIfNull(distance);
        ArgumentOutOfRangeException.ThrowIfLessThan(cells, 2);

        var spanX = maxX - minX;
        var spanY = maxY - minY;
        var spanZ = maxZ - minZ;

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Math.Min(spanX, Math.Min(spanY, spanZ)));

        var longest = Math.Max(spanX, Math.Max(spanY, spanZ));

        // Counted per axis rather than taken from one step, because a box may be any
        // shape. A uniform step from the LONGEST span loses anything thin - an analyser's
        // stripe is 4 mm thick and 635 mm long, so 48 cells across the length gives 15 mm
        // cells and the metal falls between two sample planes, producing no surface at all
        // and saying nothing. That is the sub-cell failure the electrode's own bounding box
        // was introduced to fix, returning through the box's aspect ratio instead of the
        // domain's.
        //
        // Taking the step from the THINNEST span instead fixes that and over-tessellates
        // the other way: the same stripe came out at 72,000 triangles for what is a box,
        // and sixteen of them made a 77 MB file. So each axis is resolved on its own,
        // between a floor that keeps a slab a slab and the requested count.
        //
        // The cells are then not cubes, which costs nothing: surface nets works on a
        // lattice and the spacings are already carried separately. On an isotropic shape
        // every axis lands on `cells` and the mesh is exactly what it was before - which
        // is what keeps the sphere's area, volume and Pappus checks meaningful.
        var nx = Resolution(spanX, longest, cells);
        var ny = Resolution(spanY, longest, cells);
        var nz = Resolution(spanZ, longest, cells);

        var hx = spanX / nx;
        var hy = spanY / ny;
        var hz = spanZ / nz;

        var corners = new double[(nx + 1) * (ny + 1) * (nz + 1)];

        int Corner(int i, int j, int k) => i + ((nx + 1) * (j + ((ny + 1) * k)));

        for (var k = 0; k <= nz; k++)
        {
            for (var j = 0; j <= ny; j++)
            {
                for (var i = 0; i <= nx; i++)
                {
                    corners[Corner(i, j, k)] =
                        distance(minX + (i * hx), minY + (j * hy), minZ + (k * hz));
                }
            }
        }

        var vertices = new List<double>();
        var cellVertex = new int[nx * ny * nz];

        Array.Fill(cellVertex, -1);

        int Cell(int i, int j, int k) => i + (nx * (j + (ny * k)));

        for (var k = 0; k < nz; k++)
        {
            for (var j = 0; j < ny; j++)
            {
                for (var i = 0; i < nx; i++)
                {
                    if (Vertex(corners, Corner, i, j, k) is not { } local)
                    {
                        continue;
                    }

                    cellVertex[Cell(i, j, k)] = vertices.Count / 3;

                    vertices.Add(minX + ((i + local.X) * hx));
                    vertices.Add(minY + ((j + local.Y) * hy));
                    vertices.Add(minZ + ((k + local.Z) * hz));
                }
            }
        }

        var triangles = new List<int>();

        // One quad per lattice edge that crosses the surface, from the four cells that
        // share it. Interior edges only - an edge on the box face has fewer than four
        // cells, so a conductor clipped by the box is left open there. That is the honest
        // answer: the box is a window, not a lid.
        for (var k = 0; k <= nz; k++)
        {
            for (var j = 0; j <= ny; j++)
            {
                for (var i = 0; i <= nx; i++)
                {
                    var here = corners[Corner(i, j, k)];

                    if (i < nx && j is > 0 && j < ny && k is > 0 && k < nz
                        && Crosses(here, corners[Corner(i + 1, j, k)]))
                    {
                        Quad(
                            triangles,
                            cellVertex[Cell(i, j - 1, k - 1)],
                            cellVertex[Cell(i, j, k - 1)],
                            cellVertex[Cell(i, j, k)],
                            cellVertex[Cell(i, j - 1, k)]);
                    }

                    if (j < ny && i is > 0 && i < nx && k is > 0 && k < nz
                        && Crosses(here, corners[Corner(i, j + 1, k)]))
                    {
                        Quad(
                            triangles,
                            cellVertex[Cell(i - 1, j, k - 1)],
                            cellVertex[Cell(i, j, k - 1)],
                            cellVertex[Cell(i, j, k)],
                            cellVertex[Cell(i - 1, j, k)]);
                    }

                    if (k < nz && i is > 0 && i < nx && j is > 0 && j < ny
                        && Crosses(here, corners[Corner(i, j, k + 1)]))
                    {
                        Quad(
                            triangles,
                            cellVertex[Cell(i - 1, j - 1, k)],
                            cellVertex[Cell(i, j - 1, k)],
                            cellVertex[Cell(i, j, k)],
                            cellVertex[Cell(i - 1, j, k)]);
                    }
                }
            }
        }

        // The gradient is differenced at the SMALLEST spacing, since the cells need not be
        // cubes: a step longer than the thinnest direction of a slab would sample straight
        // through it and normalise a gradient of nearly nothing.
        var finest = Math.Min(hx, Math.Min(hy, hz));

        return Orient(
            new SurfaceMesh(vertices, new double[vertices.Count], triangles), distance, finest);
    }

    /// <summary>Revolves a closed profile in the half-plane about the x axis.</summary>
    /// <param name="profile">Points as (x, radius), in metres.</param>
    /// <param name="segments">Facets around the turn.</param>
    /// <returns>The surface of revolution.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Fewer than three facets.</exception>
    /// <remarks>
    /// <para>
    /// What an axisymmetric solve means (SYM-1): the half-plane is not a picture of the
    /// geometry, it is a geometry that repeats all the way round. A ring in the half-plane
    /// is a torus in space, and drawing the half-plane instead would be drawing the
    /// model's coordinates rather than its instrument.
    /// </para>
    /// <para>
    /// No caps, because a closed profile revolved is already closed. A profile point on
    /// the axis is a pole and its ring degenerates to a point; the triangles there
    /// collapse to zero area and are dropped rather than emitted with an undefined normal.
    /// </para>
    /// </remarks>
    public static SurfaceMesh Revolve(
        IReadOnlyList<(double X, double Radius)> profile, int segments = 32)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 3);

        var (points, closed) = Weld(profile, (a, b) => a.X == b.X && a.Radius == b.Radius);

        if (points.Count < 2)
        {
            return new SurfaceMesh([], [], []);
        }

        var vertices = new List<double>(3 * points.Count * segments);

        for (var s = 0; s < segments; s++)
        {
            var angle = 2.0 * Math.PI * s / segments;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);

            foreach (var (x, radius) in points)
            {
                vertices.Add(x);
                vertices.Add(radius * cos);
                vertices.Add(radius * sin);
            }
        }

        var triangles = new List<int>();
        var spans = closed ? points.Count : points.Count - 1;

        for (var s = 0; s < segments; s++)
        {
            var here = s * points.Count;
            var next = (s + 1) % segments * points.Count;

            for (var p = 0; p < spans; p++)
            {
                var ahead = (p + 1) % points.Count;

                // A pole contributes no area on the side that touches it.
                if (points[p].Radius > 0.0 || points[ahead].Radius > 0.0)
                {
                    Quad(triangles, here + p, here + ahead, next + ahead, next + p);
                }
            }
        }

        return new SurfaceMesh(vertices, new double[vertices.Count], triangles);
    }

    /// <summary>Revolves a traced outline in the half-plane about the x axis.</summary>
    /// <param name="run">The outline, as (x, radius) in meters.</param>
    /// <param name="segments">Facets around the turn.</param>
    /// <returns>The surface of revolution.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Fewer than three facets.</exception>
    /// <remarks>
    /// Closed exactly when the run says it is. <see cref="Contours"/> does not repeat a
    /// closed run's first point, so handing its points to the profile overload drops the
    /// closing segment; this is the spelling that cannot.
    /// </remarks>
    public static SurfaceMesh Revolve(Contours.Run run, int segments = 32)
    {
        ArgumentNullException.ThrowIfNull(run);

        return Revolve(Profile(run), segments);
    }

    /// <summary>Extrudes a traced outline in the section plane along z.</summary>
    /// <param name="run">The outline, as (x, y) in meters.</param>
    /// <param name="minZ">Where the drawn prism starts, in meters.</param>
    /// <param name="maxZ">Where it ends, in meters.</param>
    /// <returns>The prism's sides, uncapped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> is null.</exception>
    /// <remarks>
    /// Closed exactly when the run says it is, for the reason the revolving overload gives.
    /// </remarks>
    public static SurfaceMesh Extrude(Contours.Run run, double minZ, double maxZ)
    {
        ArgumentNullException.ThrowIfNull(run);

        return Extrude(Profile(run), minZ, maxZ);
    }

    /// <summary>A run's points as a profile, with the closing point repeated when it is closed.</summary>
    private static (double, double)[] Profile(Contours.Run run) =>
        run.Closed && run.Points.Count > 0
            ? [.. run.Points, run.Points[0]]
            : [.. run.Points];

    /// <summary>Extrudes a profile in the section plane along z.</summary>
    /// <param name="profile">Points as (x, y), in metres.</param>
    /// <param name="minZ">Where the drawn prism starts, in metres.</param>
    /// <param name="maxZ">Where it ends, in metres.</param>
    /// <returns>The prism's sides, uncapped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    /// <remarks>
    /// <b>Uncapped, and that is the correct depiction rather than a shortcut.</b> A
    /// translational solve assumes the geometry is invariant along z, so the electrode
    /// genuinely extends past whatever is drawn — capping it would draw an end the model
    /// does not have. Where the prism stops is a drawing convention and the caller says so
    /// (GRD-12).
    /// </remarks>
    public static SurfaceMesh Extrude(
        IReadOnlyList<(double X, double Y)> profile, double minZ, double maxZ)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var (points, closed) = Weld(profile, (a, b) => a.X == b.X && a.Y == b.Y);

        if (points.Count < 2)
        {
            return new SurfaceMesh([], [], []);
        }

        var vertices = new List<double>(6 * points.Count);

        foreach (var z in (double[])[minZ, maxZ])
        {
            foreach (var (x, y) in points)
            {
                vertices.Add(x);
                vertices.Add(y);
                vertices.Add(z);
            }
        }

        var triangles = new List<int>();
        var spans = closed ? points.Count : points.Count - 1;

        for (var p = 0; p < spans; p++)
        {
            var ahead = (p + 1) % points.Count;

            Quad(triangles, p, ahead, points.Count + ahead, points.Count + p);
        }

        return new SurfaceMesh(vertices, new double[vertices.Count], triangles);
    }

    /// <summary>Points every normal outward and winds every triangle to match.</summary>
    /// <param name="mesh">The mesh to orient.</param>
    /// <param name="distance">The signed distance the surface is the zero set of.</param>
    /// <param name="step">Differencing step for the gradient, in metres.</param>
    /// <returns>The same surface, oriented.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="step"/> is not positive.</exception>
    /// <remarks>
    /// <para>
    /// <b>The outward direction is the gradient of the signed distance</b>, which is exact
    /// and available wherever the surface came from. The alternative — inferring it from
    /// the winding a producer happened to emit — is not available here at all, because
    /// marching squares emits deliberately undirected segments and a revolved profile
    /// inherits that.
    /// </para>
    /// <para>
    /// A vertex where the gradient vanishes is given the normal of the facets around it,
    /// and its triangles are left wound as they came. That is a genuine degeneracy - the
    /// centre of a shape, a crease, or a sheet with no thickness, whose distance is
    /// <c>|x - X|</c> and whose central difference on the sheet is exactly zero - and a
    /// flat-shaded facet is the honest drawing of it.
    /// </para>
    /// <para>
    /// <b>This said "keeps a zero normal" and claimed that produced a flat-shaded facet. It
    /// does not, in either window.</b> A zero normal is not a direction: the cross-platform
    /// shader normalizes it, which GLSL leaves undefined, and the WPF viewport lights it at
    /// its ambient term alone. The planar mirror pair's end cap is such a sheet, and every
    /// one of its vertices came back zero - found by a test asking whether a mirrored image
    /// is wound like its original, which a surface with no normals cannot answer.
    /// </para>
    /// </remarks>
    public static SurfaceMesh Orient(SurfaceMesh mesh, SignedDistance distance, double step)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(distance);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);

        var vertices = mesh.Vertices;
        var normals = new double[vertices.Count];
        var half = 0.5 * step;

        for (var v = 0; v < vertices.Count; v += 3)
        {
            var x = vertices[v];
            var y = vertices[v + 1];
            var z = vertices[v + 2];

            var gx = distance(x + half, y, z) - distance(x - half, y, z);
            var gy = distance(x, y + half, z) - distance(x, y - half, z);
            var gz = distance(x, y, z + half) - distance(x, y, z - half);

            var length = Math.Sqrt((gx * gx) + (gy * gy) + (gz * gz));

            if (length > 0.0 && double.IsFinite(length))
            {
                normals[v] = gx / length;
                normals[v + 1] = gy / length;
                normals[v + 2] = gz / length;
            }
        }

        var triangles = mesh.Triangles.ToArray();

        for (var t = 0; t + 2 < triangles.Length; t += 3)
        {
            var a = triangles[t];
            var b = triangles[t + 1];
            var c = triangles[t + 2];

            var (ux, uy, uz) = (
                vertices[(3 * b)] - vertices[3 * a],
                vertices[(3 * b) + 1] - vertices[(3 * a) + 1],
                vertices[(3 * b) + 2] - vertices[(3 * a) + 2]);

            var (wx, wy, wz) = (
                vertices[(3 * c)] - vertices[3 * a],
                vertices[(3 * c) + 1] - vertices[(3 * a) + 1],
                vertices[(3 * c) + 2] - vertices[(3 * a) + 2]);

            var facet = (
                X: (uy * wz) - (uz * wy),
                Y: (uz * wx) - (ux * wz),
                Z: (ux * wy) - (uy * wx));

            var outward =
                (facet.X * (normals[3 * a] + normals[3 * b] + normals[3 * c]))
                + (facet.Y * (normals[(3 * a) + 1] + normals[(3 * b) + 1] + normals[(3 * c) + 1]))
                + (facet.Z * (normals[(3 * a) + 2] + normals[(3 * b) + 2] + normals[(3 * c) + 2]));

            if (outward < 0.0)
            {
                (triangles[t + 1], triangles[t + 2]) = (c, b);
            }
        }

        FaceNormalsWhereTheGradientVanished(vertices, normals, triangles);

        return new SurfaceMesh(vertices, normals, triangles);
    }

    /// <summary>
    /// Gives each vertex with no gradient the area-weighted normal of its facets, taken from
    /// their winding.
    /// </summary>
    /// <remarks>
    /// Only those vertices: everywhere the signed distance has a gradient it is exact, and
    /// averaging facets there would replace an exact normal with an approximate one.
    /// </remarks>
    private static void FaceNormalsWhereTheGradientVanished(
        IReadOnlyList<double> vertices, double[] normals, int[] triangles)
    {
        var bare = new bool[normals.Length / 3];
        var any = false;

        for (var v = 0; v < bare.Length; v++)
        {
            bare[v] = normals[3 * v] == 0.0 && normals[(3 * v) + 1] == 0.0 && normals[(3 * v) + 2] == 0.0;
            any |= bare[v];
        }

        if (!any)
        {
            return;
        }

        var sum = new double[normals.Length];

        for (var t = 0; t + 2 < triangles.Length; t += 3)
        {
            int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];

            if (!bare[a] && !bare[b] && !bare[c])
            {
                continue;
            }

            double ux = vertices[3 * b] - vertices[3 * a];
            double uy = vertices[(3 * b) + 1] - vertices[(3 * a) + 1];
            double uz = vertices[(3 * b) + 2] - vertices[(3 * a) + 2];
            double wx = vertices[3 * c] - vertices[3 * a];
            double wy = vertices[(3 * c) + 1] - vertices[(3 * a) + 1];
            double wz = vertices[(3 * c) + 2] - vertices[(3 * a) + 2];

            // Unnormalized, so each facet counts in proportion to its area.
            var fx = (uy * wz) - (uz * wy);
            var fy = (uz * wx) - (ux * wz);
            var fz = (ux * wy) - (uy * wx);

            foreach (var v in (int[])[a, b, c])
            {
                if (bare[v])
                {
                    sum[3 * v] += fx;
                    sum[(3 * v) + 1] += fy;
                    sum[(3 * v) + 2] += fz;
                }
            }
        }

        for (var v = 0; v < bare.Length; v++)
        {
            if (!bare[v])
            {
                continue;
            }

            var length = Math.Sqrt((sum[3 * v] * sum[3 * v]) + (sum[(3 * v) + 1] * sum[(3 * v) + 1])
                + (sum[(3 * v) + 2] * sum[(3 * v) + 2]));

            if (length > 0.0 && double.IsFinite(length))
            {
                normals[3 * v] = sum[3 * v] / length;
                normals[(3 * v) + 1] = sum[(3 * v) + 1] / length;
                normals[(3 * v) + 2] = sum[(3 * v) + 2] / length;
            }
        }
    }

    /// <summary>Drops a profile's repeated closing point and says it was closed.</summary>
    /// <remarks>
    /// <para>
    /// <b>Welding rather than carrying the duplicate</b>, because two vertices at one
    /// position leave a seam that is invisible on screen and is not invisible to anything
    /// that asks whether the surface is closed.
    /// </para>
    /// <para>
    /// <b>A closed profile is one whose last point repeats its first, and a closed run from
    /// <see cref="Contours"/> is not one.</b> This said it was: that a run from
    /// <see cref="Contours"/> repeats its first point at the end. It never has - the joiner
    /// drops the repeat and says <see cref="Contours.Run.Closed"/> instead - so every closed
    /// outline the viewport revolved or extruded was read as open and lost its closing
    /// segment: a slit down a prism, a band missing from a ring. The tests here built their
    /// profiles by hand, with the repeat, and so tested a convention the producer did not
    /// follow. A run goes through the overloads taking a <see cref="Contours.Run"/>, which
    /// read its own flag.
    /// </para>
    /// </remarks>
    private static (IReadOnlyList<T> Points, bool Closed) Weld<T>(
        IReadOnlyList<T> profile, Func<T, T, bool> same)
    {
        if (profile.Count >= 3 && same(profile[0], profile[^1]))
        {
            return (profile.Take(profile.Count - 1).ToList(), true);
        }

        return (profile, false);
    }

    /// <summary>Whether a lattice edge crosses the surface.</summary>
    /// <remarks>Zero is inside, matching <c>Contains</c>.</remarks>
    private static bool Crosses(double a, double b) =>
        double.IsFinite(a) && double.IsFinite(b) && (a <= 0.0) != (b <= 0.0);

    /// <summary>Where in a cell the surface sits, or null when it does not.</summary>
    /// <remarks>
    /// The mean of the crossings on the cell's twelve edges. Averaging rather than solving
    /// a least-squares problem for the crease is what makes this surface nets rather than
    /// dual contouring: it rounds a true corner by about a cell and needs no normals at
    /// extraction time.
    /// </remarks>
    private static (double X, double Y, double Z)? Vertex(
        double[] corners, Func<int, int, int, int> corner, int i, int j, int k)
    {
        Span<double> f =
        [
            corners[corner(i, j, k)],
            corners[corner(i + 1, j, k)],
            corners[corner(i, j + 1, k)],
            corners[corner(i + 1, j + 1, k)],
            corners[corner(i, j, k + 1)],
            corners[corner(i + 1, j, k + 1)],
            corners[corner(i, j + 1, k + 1)],
            corners[corner(i + 1, j + 1, k + 1)],
        ];

        double sumX = 0.0, sumY = 0.0, sumZ = 0.0;
        var crossings = 0;

        // Corner c has bit 0 as x, bit 1 as y, bit 2 as z, matching the order above.
        for (var c = 0; c < 8; c++)
        {
            for (var axis = 0; axis < 3; axis++)
            {
                var bit = 1 << axis;

                if ((c & bit) != 0)
                {
                    continue;
                }

                var other = c | bit;

                if (!Crosses(f[c], f[other]))
                {
                    continue;
                }

                var t = f[c] / (f[c] - f[other]);

                sumX += ((c & 1) != 0 ? 1.0 : 0.0) + (axis == 0 ? t : 0.0);
                sumY += ((c & 2) != 0 ? 1.0 : 0.0) + (axis == 1 ? t : 0.0);
                sumZ += ((c & 4) != 0 ? 1.0 : 0.0) + (axis == 2 ? t : 0.0);

                crossings++;
            }
        }

        return crossings == 0
            ? null
            : (sumX / crossings, sumY / crossings, sumZ / crossings);
    }

    /// <summary>Two triangles for a quad, dropping it if any corner is missing.</summary>
    private static void Quad(List<int> triangles, int a, int b, int c, int d)
    {
        if (a < 0 || b < 0 || c < 0 || d < 0)
        {
            return;
        }

        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);

        triangles.Add(a);
        triangles.Add(c);
        triangles.Add(d);
    }
}
