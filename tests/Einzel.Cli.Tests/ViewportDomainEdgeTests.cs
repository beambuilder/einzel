using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A conductor face lying on the edge of its solve's domain is drawn, and one lying on a
/// mirror is not.
/// </summary>
/// <remarks>
/// <para>
/// <b>The einzel lens was drawn as three thin-walled tubes with flanges.</b> Each of its tubes
/// is a rectangle from the bore at 5 mm to the domain's outer radius at 12 mm, and the
/// entrance and exit tubes also reach the domain's two ends. A conductor is the zero level set
/// of its signed distance, traced over the domain - and marching squares counts a sample equal
/// to the level as inside, while nothing is sampled past the domain. So a face on the edge had
/// no crossing to find: the outline stayed open there, revolved into a bore and an annulus at
/// each gap, and a 7 mm wall read as a thin sleeve with a flange at either end.
/// </para>
/// <para>
/// <b>The bounds could not show it.</b> The annulus at each gap reaches the outer radius too,
/// so the mesh's largest radius was 12 mm before the fix and after it. What was missing was
/// the wall between - so these ask where the wall is, and what volume the surface encloses,
/// which only a closed surface wound outward can get right.
/// </para>
/// <para>
/// Every expectation is the template's own declared geometry: a 5 mm bore, a 7 mm wall, tubes
/// 30, 24 and 100 mm long with 6 mm gaps.
/// </para>
/// </remarks>
public sealed class ViewportDomainEdgeTests(ITestOutputHelper output) : IDisposable
{
    // The einzel lens as shipped, in millimeters.
    private const double Bore = 5.0;
    private const double Outer = 12.0;

    private static readonly (string Name, double Start, double End)[] Tubes =
    [
        ("entrance", 0.0, 30.0),
        ("centre", 36.0, 60.0),
        ("exit", 66.0, 166.0),
    ];

    // The viewport revolves a profile through this many facets, so the solid it draws is
    // inscribed in the one declared, by the ratio of a polygon's area to its circle's.
    private const int Facets = 48;

    // The viewport samples a solve's half-plane at 240 columns, with rows in proportion: for
    // this lens 0.69 mm along the axis and 0.75 mm in radius. Marching squares cuts a corner
    // by up to one such cell.
    private const double Cell = 0.75;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-viewport-edge", Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Each tube is drawn with its outer wall along its whole length.</summary>
    /// <remarks>
    /// Asked at the middle third of each tube, away from the gaps, because the annulus each
    /// gap leaves reaches the outer radius too: a wall found only at the tube's ends is the
    /// flange the defect drew.
    /// </remarks>
    [Fact]
    public void EachTubeIsDrawnWithItsOuterWall()
    {
        var outcome = ViewportCommand.Execute(Template("einzel-lens"));

        foreach (var (name, start, end) in Tubes)
        {
            var tube = Assert.Single(outcome.Conductors, c => c.Name == name);

            var third = (end - start) / 3.0;
            var middle = Vertices(tube)
                .Where(v => v.X > start + third && v.X < end - third)
                .Select(v => Radius(v))
                .ToList();

            output.WriteLine(
                $"{name,-9} middle third: {middle.Count,4} vertices, radius "
                + $"{middle.Min():F4} to {middle.Max():F4} mm");

            Assert.InRange(middle.Min(), Bore - 1e-3, Bore + 1e-3);
            Assert.InRange(middle.Max(), Outer - 1e-3, Outer + 1e-3);
        }
    }

    /// <summary>
    /// The entrance and exit tubes are drawn with their end faces, on the domain's two ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The left and right edges of an axisymmetric solve are grounded unless a document says
    /// otherwise, so the solve holds nothing past them and a tube reaching one ends there with
    /// a face: an annulus from the bore to the outer wall.
    /// </para>
    /// <para>
    /// To within a sampling cell of each corner, because marching squares cuts a corner by up
    /// to a cell wherever it is - the inner corners at the gaps are cut the same way. What the
    /// defect drew at the domain's ends was the bore's rim and nothing else, a single radius.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTubesReachingTheDomainEndsAreDrawnWithTheirEndFaces()
    {
        var outcome = ViewportCommand.Execute(Template("einzel-lens"));

        foreach (var (name, at) in new[] { ("entrance", 0.0), ("exit", 166.0) })
        {
            var tube = Assert.Single(outcome.Conductors, c => c.Name == name);

            var face = Vertices(tube)
                .Where(v => Math.Abs(v.X - at) < 1e-3)
                .Select(v => Radius(v))
                .ToList();

            output.WriteLine(
                $"{name,-9} at x = {at,5:F1} mm: {face.Count,3} vertices, radius "
                + $"{face.Min():F4} to {face.Max():F4} mm");

            Assert.InRange(face.Min(), Bore, Bore + Cell);
            Assert.InRange(face.Max(), Outer - Cell, Outer);
        }
    }

    /// <summary>Each tube encloses the volume Pappus gives its profile.</summary>
    /// <remarks>
    /// <para>
    /// <b>The sharpest of these, because it tests three things at once</b>: that the surface
    /// is closed, that every triangle is wound outward, and that the vertices sit where the
    /// surface is. The enclosed volume of a mesh is a sum over its triangles and depends on
    /// where the origin is unless the mesh is closed - so an open profile revolved, which is
    /// what the defect drew, gives a number that is not a volume at all.
    /// </para>
    /// <para>
    /// The expectation is a ring of rectangular section, inscribed in the drawn polygon of
    /// 48 facets. The tolerance is what marching squares does at a corner: it cuts each by up
    /// to a cell, and a profile 7 mm thick on a sampling grid of 0.7 by 0.75 mm has four.
    /// </para>
    /// </remarks>
    [Fact]
    public void EachTubeEnclosesItsVolume()
    {
        var outcome = ViewportCommand.Execute(Template("einzel-lens"));

        var inscribed = Facets / (2.0 * Math.PI) * Math.Sin(2.0 * Math.PI / Facets);

        foreach (var (name, start, end) in Tubes)
        {
            var tube = Assert.Single(outcome.Conductors, c => c.Name == name);

            var expected = Math.PI * ((Outer * Outer) - (Bore * Bore)) * (end - start) * inscribed;
            var enclosed = Enclosed(tube);

            output.WriteLine(
                $"{name,-9} encloses {enclosed,10:F1} mm^3 against {expected,10:F1}, "
                + $"ratio {enclosed / expected:F5}");

            Assert.InRange(enclosed / expected, 0.985, 1.0);
        }
    }

    /// <summary>
    /// A cross-section's conductor is ended at a grounded edge and not at a mirror, and its
    /// prism stays uncapped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same lens declared as a cross-section with its left edge a mirror, so one model
    /// has a conductor against each kind of edge. By symmetry a conductor continues across a
    /// mirror, so a face there is a face the instrument does not have; the right edge and the
    /// outer edge are grounded, so a conductor reaching them ends with a face.
    /// </para>
    /// <para>
    /// <b>And the prism's ends stay open.</b> A cross-section claims the geometry repeats along
    /// z, so capping the prism would draw an end the model does not have - the convention the
    /// viewport has always kept, and one a fix that closes outlines could break by closing the
    /// wrong thing.
    /// </para>
    /// </remarks>
    [Fact]
    public void ACrossSectionIsEndedAtAGroundedEdgeAndNotAtAMirror()
    {
        var path = Template("einzel-lens");

        const string Cylindrical = "\"symmetry\": \"cylindrical\",";

        var model = File.ReadAllText(path);

        Assert.Contains(Cylindrical, model, StringComparison.Ordinal);

        File.WriteAllText(
            path, model.Replace(Cylindrical, "\"leftEdge\": \"neumann\",", StringComparison.Ordinal));

        var outcome = ViewportCommand.Execute(path);

        var entrance = Assert.Single(outcome.Conductors, c => c.Name == "entrance");
        var exit = Assert.Single(outcome.Conductors, c => c.Name == "exit");

        var onMirror = Faces(entrance).Count(t => t.All(v => Math.Abs(v.X) < 1e-3));
        var onRight = Faces(exit).Count(t => t.All(v => Math.Abs(v.X - 166.0) < 1e-3));
        var onOuter = Faces(entrance).Count(t => t.All(v => Math.Abs(v.Y - Outer) < 1e-3));

        var (nearZ, farZ) = Span(outcome.Conductors.SelectMany(Vertices).Select(v => v.Z));
        var capped = outcome.Conductors
            .SelectMany(Faces)
            .Count(t => t.All(v => Math.Abs(v.Z - nearZ) < 1e-6) || t.All(v => Math.Abs(v.Z - farZ) < 1e-6));

        output.WriteLine($"triangles on the mirror at x = 0:          {onMirror}");
        output.WriteLine($"triangles on the grounded right edge:      {onRight}");
        output.WriteLine($"triangles on the grounded outer edge:      {onOuter}");
        output.WriteLine($"triangles capping the prism at z = {nearZ:F1} or {farZ:F1} mm: {capped}");

        Assert.Equal(0, onMirror);
        Assert.True(onRight > 0, "the exit bar has no face on the grounded right edge");
        Assert.True(onOuter > 0, "the entrance bar has no face on the grounded outer edge");
        Assert.Equal(0, capped);
    }

    /// <summary>A conductor's vertices, in millimeters.</summary>
    private static IEnumerable<(double X, double Y, double Z)> Vertices(ConductorSurface conductor)
    {
        for (var i = 0; i + 2 < conductor.VerticesMm.Count; i += 3)
        {
            yield return (conductor.VerticesMm[i], conductor.VerticesMm[i + 1], conductor.VerticesMm[i + 2]);
        }
    }

    /// <summary>A conductor's triangles, each as its three vertices in millimeters.</summary>
    private static IEnumerable<(double X, double Y, double Z)[]> Faces(ConductorSurface conductor)
    {
        var vertices = Vertices(conductor).ToList();

        for (var t = 0; t + 2 < conductor.Triangles.Count; t += 3)
        {
            yield return
            [
                vertices[conductor.Triangles[t]],
                vertices[conductor.Triangles[t + 1]],
                vertices[conductor.Triangles[t + 2]],
            ];
        }
    }

    /// <summary>The volume a mesh encloses, by the divergence theorem, in cubic millimeters.</summary>
    private static double Enclosed(ConductorSurface conductor)
    {
        var volume = 0.0;

        foreach (var t in Faces(conductor))
        {
            var (a, b, c) = (t[0], t[1], t[2]);

            volume += (a.X * ((b.Y * c.Z) - (b.Z * c.Y)))
                - (a.Y * ((b.X * c.Z) - (b.Z * c.X)))
                + (a.Z * ((b.X * c.Y) - (b.Y * c.X)));
        }

        return volume / 6.0;
    }

    private static double Radius((double X, double Y, double Z) v) =>
        Math.Sqrt((v.Y * v.Y) + (v.Z * v.Z));

    private static (double Min, double Max) Span(IEnumerable<double> values)
    {
        var list = values.ToList();

        return (list.Min(), list.Max());
    }

    private string Template(string name)
    {
        Assert.Equal(0, Cli("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", $"{name}.json");

        Assert.Equal(0, Cli("new", path, "--from-template", name).ExitCode);

        return path;
    }

    private static (int ExitCode, string Stdout, string Stderr) Cli(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            return (Program.Main(args), stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }
}
