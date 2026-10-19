using System.Globalization;
using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Einzel.Io;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// How many shipped models put a single conductor's planar face exactly on a mesh node plane - the
/// second coin of SPEC.md Amendment 57, the third bullet of item 11 of <em>What to do next</em> -
/// measured, not gated.
/// </summary>
/// <remarks>
/// <para>
/// <b>The coin.</b> Cut cells make the answer continuous in where one conductor's surface sits,
/// except when a face lies on a node plane. Then the arms lying <em>in</em> that plane, beside the
/// conductor, either meet it at its side face or graze past to the node a cell away, according to
/// the last bit of the face's coordinate against the mesh's: <c>CompiledElectrode.FirstEntry</c>
/// asks <c>from &gt;= minimum &amp;&amp; from &lt;= maximum</c> of the arm's constant coordinate
/// (<c>SolvedFieldDocument.cs</c>, <c>Slab</c>), and the row of nodes on the face is fixed or free
/// by a <c>Ceiling</c> of a quotient. Measured on the task-09 einzel lens with its 18 mm bore face
/// on a node plane: a trillionth of a cell either way moves the axis potential by 0.07 to 0.09 V and
/// the focal length by 0.04 to 0.18 percent, the same size as the whole difference between Einzel and
/// SIMION on that lens (0.17 percent), and no refinement ladder sees it because the face stays on a
/// node at every rung.
/// </para>
/// <para>
/// <b>What this counts.</b> Every planar, axis-aligned face of every electrode of every solved
/// element - a rectangle's four sides and a polygon's axis-parallel edges in a cross-section; a box's
/// six faces, a cylinder's and a prism's two caps, and a prism's axis-parallel sides in a volume -
/// whose coordinate is within <see cref="SharedFaceNodes.ToleranceFraction"/> of the finest spacing
/// of the mesh's own node coordinate, <c>origin + k * spacing</c>, and which holds at least one node
/// inside the domain. A face on a domain edge or face is reported apart: on a grounded one it is the
/// coin Amendment 57 already warns about, and on a mirror it is not a surface at all. Tilted boxes,
/// spheres, discs and revolved outlines have no axis-aligned planar face and are not counted; a
/// tilted box is named in the output so the omission is visible.
/// </para>
/// <para>
/// <b>A measurement, not a gate.</b> The survey writes its table to the test output and asserts only
/// that it inspected the corpus; the one assertion that can fail is the control, which plants a face
/// on a node plane in the einzel lens and has to find it. Pin the counts, in the manner of
/// <c>SharedFaceCorpusTests.AgainstTheGroundedWall</c>, once it is decided whether they want a
/// warning, a fix in the cut links, or both.
/// </para>
/// </remarks>
public sealed class FaceOnNodePlaneSurveyTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _assets = Path.Combine(
        Path.GetTempPath(), "einzel-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_assets))
        {
            Directory.Delete(_assets, recursive: true);
        }
    }

    /// <summary>One planar face of one electrode lying on a node plane of the mesh it is solved on.</summary>
    /// <param name="Electrode">The electrode's name.</param>
    /// <param name="Face">Which face, as <c>y = minY</c> or <c>z = upper cap</c>.</param>
    /// <param name="Coordinate">The face's coordinate along its normal, in metres.</param>
    /// <param name="Plane">The node plane's index along that axis.</param>
    /// <param name="Nodes">Nodes of that plane lying on the face, inside the domain.</param>
    /// <param name="Where">
    /// <c>interior</c>; <c>grounded edge</c> (a Dirichlet domain edge or face, Amendment 57's coin);
    /// <c>mirror</c> (a Neumann edge or face, or the axis of an axisymmetric solve); or <c>buried
    /// under '...'</c>, when another conductor lies against every node of the face, so that there is
    /// metal on both sides and no arm beside it - a rod written as two halves.
    /// </param>
    private sealed record FaceOnNodePlane(
        string Electrode, string Face, double Coordinate, int Plane, int Nodes, string Where);

    [Fact]
    public void TheShippedModelsAreSurveyedForAConductorFaceOnANodePlane()
    {
        var inspected = 0;
        var findings = new SortedDictionary<string, List<FaceOnNodePlane>>(StringComparer.Ordinal);
        var notes = new List<string>();

        foreach (var name in DeviceTemplates.Names())
        {
            inspected += Inspect($"template {name}", DeviceTemplates.Read(name), null, findings, notes);
        }

        foreach (var name in ExampleModels.Names)
        {
            var directory = Path.Combine(_assets, name);
            Directory.CreateDirectory(directory);
            ExampleModels.WriteAssets(name, directory);
            inspected += Inspect($"example {name}", ExampleModels.Read(name), directory, findings, notes);
        }

        var invariant = CultureInfo.InvariantCulture;
        output.WriteLine("| element | electrode | face | coordinate (mm) | plane | nodes | where |");
        output.WriteLine("|---|---|---|---|---|---|---|");

        foreach (var (label, faces) in findings)
        {
            foreach (var f in faces)
            {
                output.WriteLine(string.Create(
                    invariant,
                    $"| {label} | {f.Electrode} | {f.Face} | {f.Coordinate * 1e3:G9} | {f.Plane} | {f.Nodes} | {f.Where} |"));
            }
        }

        var withInterior = findings.Count(f => f.Value.Any(x => x.Where == "interior"));
        var interiorFaces = findings.Sum(f => f.Value.Count(x => x.Where == "interior"));
        var buried = findings.Sum(f => f.Value.Count(x => x.Where.StartsWith("buried", StringComparison.Ordinal)));
        var groundedOnly = findings.Count(f => f.Value.All(x => x.Where != "interior") && f.Value.Any(x => x.Where == "grounded edge"));
        var mirrorOnly = findings.Count(f => f.Value.All(x => x.Where == "mirror"));

        output.WriteLine(string.Empty);
        output.WriteLine($"{inspected} solved elements inspected");
        output.WriteLine($"{withInterior} have a free conductor face on an interior node plane, {interiorFaces} such faces in all");
        output.WriteLine($"{buried} further faces on interior node planes have metal on both sides (a conductor written in parts) and are not surfaces");
        output.WriteLine($"{groundedOnly} have faces on node planes only where they lie in a grounded edge or face (Amendment 57's coin)");
        output.WriteLine($"{mirrorOnly} have faces on node planes only in a mirror plane (not a surface)");

        foreach (var note in notes)
        {
            output.WriteLine(note);
        }

        // A sweep that inspected nothing would pass; the corpus has dozens of solved elements.
        Assert.True(inspected > 30, $"only {inspected} solved elements were inspected");
    }

    /// <summary>
    /// The control: the einzel lens's centre tube with its bore face moved onto the nearest interior
    /// node plane has to be found there, and a millionth of a cell off it has to be clean - the
    /// tolerance flags coincidence, not proximity.
    /// </summary>
    [Fact]
    public void TheLensWithItsBoreOnANodePlaneIsFound()
    {
        var model = ModelValidator.Validate(ModelJson.Parse(DeviceTemplates.Read("einzel-lens"))).Model!;
        var solve = model.Fields.Select(f => f.Solve).First(s => s is not null)!;
        var grid = GeometryBuilder.BuildGrid(solve);
        var centre = solve.Electrodes.First(e => e.Name == "centre");

        // The nearest interior node plane to the bore face, and the face's distance from it: the
        // shipped template is off every interior plane, which the survey test reports.
        var k = Math.Clamp((int)Math.Round((centre.MinY - grid.OriginY) / grid.SpacingY), 1, grid.CountY - 2);
        var plane = grid.Y(k);
        Assert.True(Math.Abs(centre.MinY - plane) > 1e-3 * grid.SpacingY, "the shipped bore is already on a node plane");

        CompiledSolvedField Moved(double to) => solve with
        {
            Electrodes = [.. solve.Electrodes.Select(e => e.Name == "centre" ? e with { MinY = to } : e)],
        };

        var shipped = Survey(solve, grid);
        var planted = Survey(Moved(plane), grid);
        var near = Survey(Moved(plane + (1e-6 * grid.SpacingY)), grid);

        foreach (var f in planted)
        {
            output.WriteLine(f.ToString());
        }

        var found = planted.Except(shipped).ToList();
        var hit = Assert.Single(found);
        Assert.Equal("centre", hit.Electrode);
        Assert.Equal("y = minY", hit.Face);
        Assert.Equal(k, hit.Plane);
        Assert.Equal("interior", hit.Where);
        Assert.True(hit.Nodes > 1, $"only {hit.Nodes} nodes on the planted face");

        Assert.Equal(shipped, near);
    }

    private static int Inspect(
        string label,
        string json,
        string? directory,
        SortedDictionary<string, List<FaceOnNodePlane>> findings,
        List<string> notes)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(json), null, directory);

        Assert.True(
            validation.IsValid,
            $"{label}: {(validation.IsValid ? string.Empty : validation.Errors[0].Constraint)}");

        var inspected = 0;

        for (var index = 0; index < validation.Model!.Fields.Count; index++)
        {
            var element = validation.Model.Fields[index];
            List<FaceOnNodePlane> found;

            if (element.Solve is { } plane)
            {
                found = Survey(plane, GeometryBuilder.BuildGrid(plane));
            }
            else if (element.Solve3D is { } volume)
            {
                var geometry = Volume(volume);
                found = Survey(geometry, GeometryBuilder3D.BuildGrid(geometry), $"{label}, field {index}", notes);
            }
            else
            {
                continue;
            }

            inspected++;

            if (found.Count > 0)
            {
                findings[$"{label}, field {index}"] = found;
            }
        }

        return inspected;
    }

    /// <summary>Every axis-aligned face of a cross-section's electrodes lying on a node plane.</summary>
    private static List<FaceOnNodePlane> Survey(CompiledSolvedField solve, Grid2D grid)
    {
        var tolerance = SharedFaceNodes.ToleranceFraction * grid.MinimumSpacing;
        var found = new List<FaceOnNodePlane>();

        // The axis of an axisymmetric solve is a mirror whatever the document says of the bottom
        // edge (GeometryBuilder.Edges).
        var bottomIsAxis = solve.Symmetry == SolveSymmetry.Cylindrical && grid.OriginY <= 0.5 * grid.SpacingY;

        string Where(bool vertical, int plane)
        {
            if (vertical)
            {
                return plane == 0 ? Edge(solve.LeftEdge)
                    : plane == grid.CountX - 1 ? Edge(solve.RightEdge)
                    : "interior";
            }

            return plane == 0 ? (bottomIsAxis ? "mirror" : Edge(solve.BottomEdge))
                : plane == grid.CountY - 1 ? Edge(solve.TopEdge)
                : "interior";
        }

        foreach (var electrode in solve.Electrodes)
        {
            foreach (var (face, vertical, coordinate) in PlaneFaces(electrode))
            {
                var (origin, spacing, count) = vertical
                    ? (grid.OriginX, grid.SpacingX, grid.CountX)
                    : (grid.OriginY, grid.SpacingY, grid.CountY);

                if (OnPlane(coordinate, origin, spacing, count, tolerance) is not { } plane)
                {
                    continue;
                }

                // Nodes of that plane on the electrode's surface, within its bounding box and the grid.
                var (lo, hi) = vertical
                    ? Range(grid.OriginY, grid.SpacingY, grid.CountY, electrode.MinY, electrode.MaxY)
                    : Range(grid.OriginX, grid.SpacingX, grid.CountX, electrode.MinX, electrode.MaxX);

                var nodes = 0;
                var buried = 0;
                string? other = null;
                var lift = 10.0 * tolerance;

                for (var n = lo; n <= hi; n++)
                {
                    var (x, y) = vertical ? (grid.X(plane), grid.Y(n)) : (grid.X(n), grid.Y(plane));

                    if (Math.Abs(electrode.SignedDistance(x, y)) > tolerance)
                    {
                        continue;
                    }

                    nodes++;

                    // Just outside the face: the side where this electrode's signed distance is
                    // positive. Another conductor there means metal on both sides, not a surface.
                    var (ox, oy) = vertical
                        ? (electrode.SignedDistance(x + lift, y) > 0.0 ? (x + lift, y) : (x - lift, y))
                        : (electrode.SignedDistance(x, y + lift) > 0.0 ? (x, y + lift) : (x, y - lift));

                    if (solve.Electrodes.FirstOrDefault(e => e != electrode && e.Shape != ElectrodeShape.EdgeProfile && e.Contains(ox, oy)) is { } cover)
                    {
                        buried++;
                        other ??= cover.Name;
                    }
                }

                if (nodes > 0)
                {
                    var where = buried == nodes && other is not null ? $"buried under '{other}'" : Where(vertical, plane);
                    found.Add(new FaceOnNodePlane(electrode.Name, face, coordinate == 0.0 ? 0.0 : coordinate, plane, nodes, where));
                }
            }
        }

        return found;
    }

    /// <summary>Every axis-aligned planar face of a volume's electrodes lying on a node plane.</summary>
    private static List<FaceOnNodePlane> Survey(Geometry3D geometry, Grid3D grid, string label, List<string> notes)
    {
        var tolerance = SharedFaceNodes.ToleranceFraction * grid.MinimumSpacing;
        var found = new List<FaceOnNodePlane>();
        var faces = geometry.Faces.Count == 6 ? geometry.Faces : Enumerable.Repeat(EdgeCondition.Dirichlet, 6).ToList();

        string Where(int axis, int plane)
        {
            var count = axis switch { 0 => grid.CountX, 1 => grid.CountY, _ => grid.CountZ };

            if (plane == 0)
            {
                return faces[2 * axis] == EdgeCondition.Neumann ? "mirror" : "grounded edge";
            }

            if (plane == count - 1)
            {
                return faces[(2 * axis) + 1] == EdgeCondition.Neumann ? "mirror" : "grounded edge";
            }

            return "interior";
        }

        foreach (var electrode in geometry.Electrodes)
        {
            if (electrode.Shape == Electrode3DShape.Box && electrode.TiltHalfTurns != 0.0)
            {
                notes.Add($"{label}: tilted box '{electrode.Name}' has no axis-aligned face and was not counted");
                continue;
            }

            if (Bounds(electrode) is not { } box)
            {
                continue;
            }

            foreach (var (face, axis, coordinate) in PlaneFaces(electrode))
            {
                var (origin, spacing, count) = axis switch
                {
                    0 => (grid.OriginX, grid.SpacingX, grid.CountX),
                    1 => (grid.OriginY, grid.SpacingY, grid.CountY),
                    _ => (grid.OriginZ, grid.SpacingZ, grid.CountZ),
                };

                if (OnPlane(coordinate, origin, spacing, count, tolerance) is not { } plane)
                {
                    continue;
                }

                // The two axes across the face, and the electrode's extent along each.
                var (a, b) = axis switch { 0 => (1, 2), 1 => (0, 2), _ => (0, 1) };
                var (aLo, aHi) = Range(Origin(grid, a), Spacing(grid, a), Count(grid, a), box.Min[a], box.Max[a]);
                var (bLo, bHi) = Range(Origin(grid, b), Spacing(grid, b), Count(grid, b), box.Min[b], box.Max[b]);

                var nodes = 0;
                var buried = 0;
                string? other = null;
                var lift = 10.0 * tolerance;
                var index = new int[3];
                index[axis] = plane;

                for (var ia = aLo; ia <= aHi; ia++)
                {
                    index[a] = ia;

                    for (var ib = bLo; ib <= bHi; ib++)
                    {
                        index[b] = ib;
                        var p = new[] { grid.X(index[0]), grid.Y(index[1]), grid.Z(index[2]) };

                        if (Math.Abs(electrode.SignedDistance(p[0], p[1], p[2])) > tolerance)
                        {
                            continue;
                        }

                        nodes++;

                        // Just outside the face, as in the plane.
                        var q = (double[])p.Clone();
                        q[axis] += lift;

                        if (electrode.SignedDistance(q[0], q[1], q[2]) <= 0.0)
                        {
                            q[axis] = p[axis] - lift;
                        }

                        if (geometry.Electrodes.FirstOrDefault(e => e != electrode && e.Contains(q[0], q[1], q[2])) is { } cover)
                        {
                            buried++;
                            other ??= cover.Name;
                        }
                    }
                }

                if (nodes > 0)
                {
                    var where = buried == nodes && other is not null ? $"buried under '{other}'" : Where(axis, plane);
                    found.Add(new FaceOnNodePlane(electrode.Name, face, coordinate == 0.0 ? 0.0 : coordinate, plane, nodes, where));
                }
            }
        }

        return found;
    }

    /// <summary>A cross-section electrode's axis-aligned faces: which, whether it is vertical, and where.</summary>
    private static IEnumerable<(string Face, bool Vertical, double Coordinate)> PlaneFaces(CompiledElectrode electrode)
    {
        switch (electrode.Shape)
        {
            case ElectrodeShape.Rectangle:
                yield return ("x = minX", true, electrode.MinX);
                yield return ("x = maxX", true, electrode.MaxX);
                yield return ("y = minY", false, electrode.MinY);
                yield return ("y = maxY", false, electrode.MaxY);
                break;

            case ElectrodeShape.Polygon:
            {
                var count = electrode.Vertices.Count;

                for (int i = 0, j = count - 1; i < count; j = i++)
                {
                    var (ax, ay) = electrode.Vertices[j];
                    var (bx, by) = electrode.Vertices[i];

                    if (ax == bx && ay != by)
                    {
                        yield return ($"x = {ax * 1e3:G9} mm edge {j}-{i}", true, ax);
                    }
                    else if (ay == by && ax != bx)
                    {
                        yield return ($"y = {ay * 1e3:G9} mm edge {j}-{i}", false, ay);
                    }
                }

                break;
            }

            default:
                // A disc has no planar face; an edge profile lies in a domain edge already.
                break;
        }
    }

    /// <summary>A volume electrode's axis-aligned planar faces: which, the normal axis (0, 1, 2), and where.</summary>
    private static IEnumerable<(string Face, int Axis, double Coordinate)> PlaneFaces(CompiledElectrode3D electrode)
    {
        var along = electrode.Axis switch { CylinderAxis.X => 0, CylinderAxis.Y => 1, _ => 2 };

        switch (electrode.Shape)
        {
            case Electrode3DShape.Box:
                yield return ("x = minX", 0, electrode.MinX);
                yield return ("x = maxX", 0, electrode.MaxX);
                yield return ("y = minY", 1, electrode.MinY);
                yield return ("y = maxY", 1, electrode.MaxY);
                yield return ("z = minZ", 2, electrode.MinZ);
                yield return ("z = maxZ", 2, electrode.MaxZ);
                break;

            case Electrode3DShape.Cylinder:
                yield return ($"{Name(along)} = lower cap", along, electrode.Lower);
                yield return ($"{Name(along)} = upper cap", along, electrode.Upper);
                break;

            case Electrode3DShape.Prism:
            {
                yield return ($"{Name(along)} = lower cap", along, electrode.Lower);
                yield return ($"{Name(along)} = upper cap", along, electrode.Upper);

                // The outline's plane holds the two world axes other than the prism's, in world order.
                var (a, b) = along switch { 0 => (1, 2), 1 => (0, 2), _ => (0, 1) };
                var count = electrode.Vertices.Count;

                for (int i = 0, j = count - 1; i < count; j = i++)
                {
                    var (pa, pb) = electrode.Vertices[j];
                    var (qa, qb) = electrode.Vertices[i];

                    if (pa == qa && pb != qb)
                    {
                        yield return ($"{Name(a)} = {pa * 1e3:G9} mm side {j}-{i}", a, pa);
                    }
                    else if (pb == qb && pa != qa)
                    {
                        yield return ($"{Name(b)} = {pb * 1e3:G9} mm side {j}-{i}", b, pb);
                    }
                }

                break;
            }

            default:
                // A sphere and a revolved outline have no axis-aligned planar face.
                break;
        }
    }

    /// <summary>A volume electrode's bounding box, or null for a shape this survey does not enumerate.</summary>
    private static (double[] Min, double[] Max)? Bounds(CompiledElectrode3D electrode)
    {
        switch (electrode.Shape)
        {
            case Electrode3DShape.Box:
                return ([electrode.MinX, electrode.MinY, electrode.MinZ], [electrode.MaxX, electrode.MaxY, electrode.MaxZ]);

            case Electrode3DShape.Cylinder:
            case Electrode3DShape.Prism:
            {
                var along = electrode.Axis switch { CylinderAxis.X => 0, CylinderAxis.Y => 1, _ => 2 };
                var (a, b) = along switch { 0 => (1, 2), 1 => (0, 2), _ => (0, 1) };
                var min = new double[3];
                var max = new double[3];
                min[along] = electrode.Lower;
                max[along] = electrode.Upper;

                if (electrode.Shape == Electrode3DShape.Cylinder)
                {
                    var centre = new[] { electrode.CentreX, electrode.CentreY, electrode.CentreZ };
                    min[a] = centre[a] - electrode.Radius;
                    max[a] = centre[a] + electrode.Radius;
                    min[b] = centre[b] - electrode.Radius;
                    max[b] = centre[b] + electrode.Radius;
                }
                else
                {
                    min[a] = electrode.Vertices.Min(v => v.X);
                    max[a] = electrode.Vertices.Max(v => v.X);
                    min[b] = electrode.Vertices.Min(v => v.Y);
                    max[b] = electrode.Vertices.Max(v => v.Y);
                }

                return (min, max);
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// The node plane a coordinate lies on, or null: the mesh's own node coordinate, origin plus
    /// index times spacing, within the tolerance - as <c>NodesOnSharedFaces</c> reads a face.
    /// </summary>
    private static int? OnPlane(double coordinate, double origin, double spacing, int count, double tolerance)
    {
        var k = (int)Math.Clamp(Math.Round((coordinate - origin) / spacing), 0.0, count - 1.0);

        return Math.Abs(coordinate - (origin + (k * spacing))) <= tolerance ? k : null;
    }

    /// <summary>The inclusive node index range covering a closed interval, one node wider each way, clamped.</summary>
    private static (int Lo, int Hi) Range(double origin, double spacing, int count, double lo, double hi)
    {
        var first = Math.Clamp(Math.Floor((lo - origin) / spacing), 0.0, count - 1.0);
        var last = Math.Clamp(Math.Ceiling((hi - origin) / spacing), 0.0, count - 1.0);

        return ((int)first, (int)last);
    }

    private static string Edge(BoundaryKind kind) => kind == BoundaryKind.Neumann ? "mirror" : "grounded edge";

    private static string Name(int axis) => axis switch { 0 => "x", 1 => "y", _ => "z" };

    private static double Origin(Grid3D grid, int axis) => axis switch { 0 => grid.OriginX, 1 => grid.OriginY, _ => grid.OriginZ };

    private static double Spacing(Grid3D grid, int axis) => axis switch { 0 => grid.SpacingX, 1 => grid.SpacingY, _ => grid.SpacingZ };

    private static int Count(Grid3D grid, int axis) => axis switch { 0 => grid.CountX, 1 => grid.CountY, _ => grid.CountZ };

    private static Geometry3D Volume(CompiledSolvedField3D solve) => new(
        solve.MinX, solve.MinY, solve.MinZ,
        solve.MaxX, solve.MaxY, solve.MaxZ,
        solve.CellSize, solve.Electrodes, solve.Tolerance)
    {
        Drives = solve.Drives,
        Stages = solve.Stages,
        Faces = Geometry3D.FacesOf(solve.Faces),
        ReflectAboutX = solve.ReflectAboutX,
    };
}
