using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// <c>einzel render still</c>: the viewport's picture, as a file, with no window.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it is for</b> is the half of AGT-2 the command layer could not reach: an agent
/// could run everything the window runs and could not see what it shows. These tests check
/// that the picture is the viewport's - composed by the same function, framed by the same
/// camera - rather than that some picture exists.
/// </para>
/// <para>
/// What they cannot check is the GPU's half: the window's shader and uploads need a GL context.
/// The still shares every decision with the window and none of its pixels.
/// </para>
/// </remarks>
public sealed class RenderStillTests(ITestOutputHelper output) : IDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-still", Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Both mirrors of the pair are in the picture, where the camera puts them.</summary>
    /// <remarks>
    /// <para>
    /// The caps are located by carrying their own centers through the framing the window uses,
    /// then reading those pixels out of the file. A cap is dark red and the ground is white, so
    /// a pixel that is still white means the thing was not drawn where it belongs.
    /// </para>
    /// <para>
    /// Both, because one of them is the reflected half: a still that dropped the reflection
    /// would draw one mirror and pass a test that asked only for one.
    /// </para>
    /// </remarks>
    [Fact]
    public void BothMirrorsAreWhereTheCameraPutsThem()
    {
        var model = Template("planar-mirror-pair");
        var png = Path.Combine(_root, "figures", "pair.png");

        var (code, stdout, stderr) = Cli("render", "still", model, "--out", png, "--width-px", "1400", "--height-px", "500");

        output.WriteLine(stdout);
        output.WriteLine(stderr);

        Assert.Equal(0, code);
        Assert.True(File.Exists(png));

        var (width, height, rgb) = Decode(File.ReadAllBytes(png));

        Assert.Equal((1400, 500), (width, height));

        var scene = ViewportCommand.Execute(model);
        var matrix = Framing.Measure(scene, -32.0, 24.0).Matrix((double)width / height);

        foreach (var cap in scene.Conductors.Where(c => c.Name == "cap"))
        {
            var (cx, cy, cz) = Centroid(cap.VerticesMm);
            var (px, py) = Pixel(matrix, cx, cy, cz, width, height);
            var (r, g, b) = At(rgb, width, px, py);

            output.WriteLine($"cap centered at x = {cx:F1} mm lands at pixel ({px}, {py}): {r}, {g}, {b}");

            Assert.True(r > g + 40 && r > b + 40, $"the cap at x = {cx:F1} mm is not drawn at ({px}, {py})");
        }

        Assert.Equal(2, scene.Conductors.Count(c => c.Name == "cap"));
    }

    /// <summary>The file carries its provenance and every warning, not only the stdout.</summary>
    /// <remarks>
    /// A PNG is pasted into slides and chat without the JSON beside it, so what qualified it has
    /// to be inside it (GRD-2), and which model made it has to be too (PRJ-3).
    /// </remarks>
    [Fact]
    public void TheFileCarriesItsProvenanceAndItsWarnings()
    {
        var model = Template("planar-mirror-pair");
        var png = Path.Combine(_root, "figures", "pair.png");

        var (code, stdout, _) = Cli("render", "still", model, "--out", png, "--json");

        Assert.Equal(0, code);

        using var result = JsonDocument.Parse(stdout);
        var hash = result.RootElement.GetProperty("modelHash").GetString()!;

        Assert.Equal(3, result.RootElement.GetProperty("electrodes").GetInt32());

        var text = Text(File.ReadAllBytes(png));

        foreach (var (keyword, value) in text)
        {
            output.WriteLine($"{keyword}: {value}");
        }

        Assert.Contains(text, t => t.Keyword == "Source" && t.Text.Contains(hash, StringComparison.Ordinal));
        Assert.Contains(text, t => t.Keyword == "Software" && t.Text.StartsWith("einzel ", StringComparison.Ordinal));
        Assert.Contains(text, t => t.Keyword.StartsWith("Warning", StringComparison.Ordinal)
            && t.Text.Contains("render.extruded-depth", StringComparison.Ordinal));
    }

    /// <summary>A validity violation marks the picture itself, and a clean model's is unmarked.</summary>
    /// <remarks>
    /// RND-11: the figure is the artifact most likely to travel without its apparatus. The
    /// control is the clean model, because a band drawn on every still would pass the first
    /// half and mark nothing.
    /// </remarks>
    [Fact]
    public void AViolationMarksThePictureAndACleanModelIsUnmarked()
    {
        var tainted = Still(Example("drift-tube-diffusion"), "tainted.png");
        var clean = Still(Example("thermal-emittance"), "clean.png");

        Assert.True(tainted.Tainted);
        Assert.False(clean.Tainted);

        var marked = BottomRowRed(tainted.Artifact);
        var unmarked = BottomRowRed(clean.Artifact);

        output.WriteLine($"red pixels on the bottom row: tainted {marked}, clean {unmarked}");

        Assert.True(marked > 100);
        Assert.Equal(0, unmarked);
    }

    /// <summary>A view that has no name is refused with the names that exist.</summary>
    [Fact]
    public void AnUnknownViewIsRefusedWithTheNamesThatExist()
    {
        var (code, _, stderr) = Cli("render", "still", Template("einzel-lens"), "--view", "oblique");

        Assert.NotEqual(0, code);
        Assert.Contains("iso", stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// A pixel size that is not a whole number is the caller's mistake, refused as a
    /// validation failure that names the flag - not reported as a defect in the engine.
    /// </summary>
    /// <param name="flag">The size flag.</param>
    /// <param name="given">What was typed.</param>
    /// <remarks>It used to go through <c>int.Parse</c>, which threw and exited 6.</remarks>
    [Theory]
    [InlineData("--width-px", "1600px")]
    [InlineData("--height-px", "99999999999")]
    [InlineData("--width-px", "1e4")]
    public void APixelSizeThatIsNotANumberIsAValidationFailure(string flag, string given)
    {
        var (code, _, stderr) = Cli("render", "still", Template("einzel-lens"), flag, given);

        output.WriteLine(stderr);

        Assert.Equal(1, code);
        Assert.Contains(flag, stderr, StringComparison.Ordinal);
        Assert.Contains(given, stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("INTERNAL_ERROR", stderr, StringComparison.Ordinal);
    }

    private StillOutcome Still(string model, string name)
    {
        var png = Path.Combine(_root, "figures", name);
        var (code, stdout, _) = Cli("render", "still", model, "--out", png, "--width-px", "400", "--height-px", "300", "--json");

        Assert.Equal(0, code);

        return JsonSerializer.Deserialize<StillOutcome>(stdout, Web)!;
    }

    /// <summary>
    /// A still carries a ruler where the layout puts it, says how long its arms are, and leaves
    /// it out when asked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ruler is found by carrying its own layout - the same call the window makes, from the
    /// same framing - into the file and reading the pixels halfway along the x arm. The arm is an
    /// antialiased line a pixel and a half wide, centered wherever the layout put it, so it is
    /// looked for across the row it runs through and the rows either side; the ground is white,
    /// so nothing distinctly darker there means it was not drawn where it belongs.
    /// </para>
    /// <para>
    /// The control is the same picture with <c>--no-scale</c>: a ruler drawn on every still, or a
    /// dark pixel there for some other reason, would pass the first half alone.
    /// </para>
    /// </remarks>
    [Fact]
    public void AStillCarriesARulerWhereTheLayoutPutsItAndOmitsItWhenAsked()
    {
        const int Width = 900;
        const int Height = 560;

        var model = Template("einzel-lens");
        var with = Path.Combine(_root, "figures", "lens-ruler.png");
        var without = Path.Combine(_root, "figures", "lens-bare.png");

        var (code, stdout, _) = Cli(
            "render", "still", model, "--view", "side", "--out", with,
            "--width-px", "900", "--height-px", "560", "--json");

        Assert.Equal(0, code);

        using var result = JsonDocument.Parse(stdout);
        var arm = result.RootElement.GetProperty("scaleMm").GetDouble();
        var axes = result.RootElement.GetProperty("scaleAxes").EnumerateArray().Select(a => a.GetString()!).ToArray();

        var scene = ViewportCommand.Execute(model);
        var (azimuth, elevation) = ViewportPicture.Views["side"];
        var matrix = Framing.Measure(scene, azimuth, elevation).Matrix((double)Width / Height);
        var ruler = ScaleIndicator.For(
            matrix, Width, Height, ScaleIndicator.TextHeightFor(Width, Height), 0.0, ViewportPicture.Compose(scene))!;

        output.WriteLine($"ruler arms {ScaleIndicator.Format(arm)} along {string.Join(", ", axes)}");

        Assert.Equal(ruler.LengthMm, arm, 12);
        Assert.Equal(["x", "y"], axes);
        Assert.Equal(ruler.Corner, result.RootElement.GetProperty("scaleCorner").GetString());

        var x = ruler.Arms.Single(a => a.Axis == "x");
        var middle = ((int)Math.Round((ruler.OriginPx.X + x.TipPx.X) / 2.0), (int)Math.Round(ruler.OriginPx.Y));

        var (_, _, drawn) = Decode(File.ReadAllBytes(with));
        var darkest = Rows(drawn, middle).Min();

        Assert.True(darkest < 200, $"the ruler's x arm is not at {middle}: darkest {darkest}");

        var (bareCode, bareOut, _) = Cli(
            "render", "still", model, "--view", "side", "--out", without,
            "--width-px", "900", "--height-px", "560", "--no-scale", "--json");

        Assert.Equal(0, bareCode);

        using var bare = JsonDocument.Parse(bareOut);
        Assert.False(bare.RootElement.TryGetProperty("scaleMm", out var none) && none.ValueKind != JsonValueKind.Null);

        var (_, _, plain) = Decode(File.ReadAllBytes(without));

        Assert.All(Rows(plain, middle), value => Assert.Equal(255, value));

        // The darkest channel of the pixel at the arm's midpoint and one row either side.
        static IEnumerable<int> Rows(byte[] rgb, (int X, int Y) at) =>
            Enumerable.Range(at.Y - 1, 3).Select(y => At(rgb, Width, at.X, y)).Select(p => Math.Min(p.R, Math.Min(p.G, p.B)));
    }

    /// <summary>
    /// A still too small to hold the ruler reports none, rather than reporting one it drew off
    /// the edge.
    /// </summary>
    /// <remarks>
    /// Found by review: at 32 pixels a side the ruler's origin sat above the picture with both
    /// arms clipped away while <c>scaleMm</c> still said a ruler was drawn.
    /// </remarks>
    [Fact]
    public void ATinyStillReportsNoRuler()
    {
        var png = Path.Combine(_root, "figures", "lens-tiny.png");
        var (code, stdout, _) = Cli(
            "render", "still", Template("einzel-lens"), "--view", "side", "--out", png,
            "--width-px", "32", "--height-px", "32", "--json");

        Assert.Equal(0, code);

        using var result = JsonDocument.Parse(stdout);

        Assert.False(result.RootElement.TryGetProperty("scaleMm", out var arm) && arm.ValueKind != JsonValueKind.Null);
        Assert.Empty(result.RootElement.GetProperty("scaleAxes").EnumerateArray());
    }

    private static int BottomRowRed(string png)
    {
        var (width, height, rgb) = Decode(File.ReadAllBytes(png));

        return Enumerable.Range(0, width).Count(x =>
        {
            var (r, g, b) = At(rgb, width, x, height - 1);
            return r > 150 && g < 80 && b < 80;
        });
    }

    private static (double X, double Y, double Z) Centroid(IReadOnlyList<double> vertices)
    {
        double x = 0, y = 0, z = 0;
        var n = vertices.Count / 3;

        for (var i = 0; i < n; i++)
        {
            x += vertices[3 * i];
            y += vertices[(3 * i) + 1];
            z += vertices[(3 * i) + 2];
        }

        return (x / n, y / n, z / n);
    }

    private static (int X, int Y) Pixel(double[] m, double x, double y, double z, int width, int height)
    {
        var cx = (m[0] * x) + (m[4] * y) + (m[8] * z) + m[12];
        var cy = (m[1] * x) + (m[5] * y) + (m[9] * z) + m[13];

        return ((int)((cx + 1.0) * 0.5 * width), (int)((1.0 - cy) * 0.5 * height));
    }

    private static (int R, int G, int B) At(byte[] rgb, int width, int x, int y)
    {
        var i = 3 * ((y * width) + x);
        return (rgb[i], rgb[i + 1], rgb[i + 2]);
    }

    private static List<(string Type, byte[] Data)> Chunks(byte[] png)
    {
        var chunks = new List<(string, byte[])>();

        for (var at = 8; at < png.Length;)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at));
            chunks.Add((Encoding.ASCII.GetString(png, at + 4, 4), png.AsSpan(at + 8, length).ToArray()));
            at += 12 + length;
        }

        return chunks;
    }

    private static (int Width, int Height, byte[] Rgb) Decode(byte[] png)
    {
        var chunks = Chunks(png);
        var header = chunks.Single(c => c.Type == "IHDR").Data;
        var width = (int)BinaryPrimitives.ReadUInt32BigEndian(header);
        var height = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));

        using var inflated = new MemoryStream();

        using (var zlib = new ZLibStream(
            new MemoryStream([.. chunks.Where(c => c.Type == "IDAT").SelectMany(c => c.Data)]),
            CompressionMode.Decompress))
        {
            zlib.CopyTo(inflated);
        }

        var raw = inflated.ToArray();
        var stride = 3 * width;
        var rgb = new byte[stride * height];

        for (var y = 0; y < height; y++)
        {
            Array.Copy(raw, (y * (stride + 1)) + 1, rgb, y * stride, stride);
        }

        return (width, height, rgb);
    }

    private static List<(string Keyword, string Text)> Text(byte[] png) =>
        [.. Chunks(png).Where(c => c.Type == "iTXt").Select(c =>
        {
            var end = Array.IndexOf(c.Data, (byte)0);
            return (Encoding.ASCII.GetString(c.Data, 0, end),
                Encoding.UTF8.GetString(c.Data, end + 5, c.Data.Length - end - 5));
        })];

    private string Template(string name) => New(name, "--from-template");

    private string Example(string name) => New(name, "--from-example");

    private string New(string name, string flag)
    {
        if (!Directory.Exists(Path.Combine(_root, "models")))
        {
            Assert.Equal(0, Cli("init", _root).ExitCode);
        }

        var path = Path.Combine(_root, "models", $"{name}.json");

        if (!File.Exists(path))
        {
            Assert.Equal(0, Cli("new", path, flag, name).ExitCode);
        }

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
