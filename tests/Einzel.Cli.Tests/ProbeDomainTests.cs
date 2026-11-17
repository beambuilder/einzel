using System.Globalization;
using System.Text.Json;

using Einzel.Commands;
using Einzel.Core.Geometry;
using Einzel.Io;
using Einzel.Project;

namespace Einzel.Cli.Tests;

/// <summary>
/// Where the probe and the composite export say a model's domain is, and what they write there:
/// a point on a face, a reflected or tilted solve, a flat box, a refused node, a bad instant, and
/// the hash of the text the field was built from.
/// </summary>
/// <remarks>
/// Every test here was written against a defect a review of the probe found, and each fails on the
/// code before its fix.
/// </remarks>
public sealed class ProbeDomainTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
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

    private string Template(string kind, string name)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"{name}.json");
        Assert.Equal(0, Run("new", path, kind == "template" ? "--from-template" : "--from-example", name).ExitCode);
        return path;
    }

    private static string Q(double value, string unit) =>
        string.Create(CultureInfo.InvariantCulture, $"{{\"value\": {value:R}, \"unit\": \"{unit}\"}}");

    /// <summary>
    /// A cross-section from -12.7 to 0.3 mm in x and 0 to 9 mm in y: bounds whose millimetre values
    /// do not all round-trip, and one (the 0.3 mm face) whose last grid node lands an ulp short of
    /// the declared face. A 100 V plate inside makes the field at every face non-zero; a 50 V plate
    /// runs out through the top face, past the domain.
    /// </summary>
    private string FaceModel(string name = "faces", double tiltHalfTurns = 0.0)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"{name}.json");
        var tilt = tiltHalfTurns != 0.0 ? $"\"tiltHalfTurns\": {Q(tiltHalfTurns, "1")}," : string.Empty;

        File.WriteAllText(path, $$"""
        {
          "schemaVersion": "0.3",
          "name": "{{name}}",
          "ion": { "massToCharge": {{Q(500, "Da")}}, "chargeNumber": 1 },
          "source": {
            "position": { "value": [-11, 1, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": {{Q(1, "kV")}}
          },
          "fields": [
            {
              "type": "solved2d",
              "solve": {
                {{tilt}}
                "minX": {{Q(-12.7, "mm")}},
                "maxX": {{Q(0.3, "mm")}},
                "minY": {{Q(0, "mm")}},
                "maxY": {{Q(9, "mm")}},
                "cellSize": {{Q(0.25, "mm")}},
                "electrodes": [
                  {
                    "name": "plate", "shape": "rectangle",
                    "minX": {{Q(-8, "mm")}}, "maxX": {{Q(-4, "mm")}},
                    "minY": {{Q(3, "mm")}}, "maxY": {{Q(6, "mm")}},
                    "potential": {{Q(100, "V")}}
                  },
                  {
                    "name": "post", "shape": "rectangle",
                    "minX": {{Q(-2, "mm")}}, "maxX": {{Q(-1, "mm")}},
                    "minY": {{Q(7, "mm")}}, "maxY": {{Q(12, "mm")}},
                    "potential": {{Q(50, "V")}}
                  }
                ]
              }
            }
          ],
          "detector": {
            "planePoint": { "value": [0.2, 0, 0], "unit": "mm" },
            "normal": { "value": [-1, 0, 0] }
          },
          "transport": { "mode": "trajectory", "maximumFlightTime": {{Q(10, "us")}} }
        }
        """);

        return path;
    }

    private static JsonDocument Probe(string model, params string[] flags)
    {
        var (exitCode, stdout, stderr) = Run(["probe", model, .. flags, "--json"]);
        Assert.True(exitCode == 0, stderr);
        return JsonDocument.Parse(stdout);
    }

    private static JsonElement Row(JsonDocument document, int index = 0) =>
        document.RootElement.GetProperty("samples")[index];

    private static string[] Codes(JsonDocument document) =>
        [.. document.RootElement.GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetProperty("code").GetString()!)];

    private string Export(string model, params string[] flags)
    {
        var project = Path.Combine(_root, "project");
        var (exitCode, stdout, stderr) = Run(["export", model, "--composite", "--project", project, .. flags]);
        Assert.True(exitCode == 0, stderr + stdout);
        return File.ReadAllText(Path.Combine(project, ".einzel", $"{Path.GetFileNameWithoutExtension(model)}.composite.vti"));
    }

    [Theory]
    [InlineData("0.3,4.5,0")]
    [InlineData("-12.7,4.5,0")]
    [InlineData("-6,9,0")]
    [InlineData("-6,0,0")]
    public void APointOnEachFaceOfASolvedDomainReadsTheSolvedFace(string at)
    {
        // A face node holds a solved value, so a point exactly on a declared face is in the domain
        // and the field there is the solve's: the plate inside puts a non-zero field on every face.
        // The 0.3 mm face is the one whose last grid node falls an ulp short of the declared face,
        // and before the fix the solve answered there as though the point were outside it.
        using var document = Probe(FaceModel(), "--at-mm", at);
        var row = Row(document);

        Assert.Equal("ok", row.GetProperty("status").GetString());
        Assert.True(row.GetProperty("fieldMagnitudeVPerM").GetDouble() > 1.0, at);
        Assert.DoesNotContain("probe.outside-domain", Codes(document));
    }

    [Fact]
    public void ThePointJustPastAFaceIsOutsideTheDomain()
    {
        using var document = Probe(FaceModel(), "--at-mm", "0.3001,4.5,0");

        Assert.Equal("outside-domain", Row(document).GetProperty("status").GetString());
        Assert.Contains("probe.outside-domain", Codes(document));
    }

    [Fact]
    public void TheCompositeExportsFarFacesAreTheSolvedFaces()
    {
        // The default box comes from the SI bounds themselves, so the last layer is the face. Through
        // a millimetre round trip the 9 mm top face landed beyond the domain, and the grid-short
        // 0.3 mm face was read as outside: both far layers were written as zeros.
        var model = FaceModel();
        var vti = Export(model, "--step-mm", "0.5");

        var potential = VtkImageData.Read(vti, "potential_V");
        var field = VtkImageData.Read(vti, "E_V_per_m");
        var (nx, ny, nz) = (potential.CountX, potential.CountY, potential.CountZ);
        var probe = FieldProbe.Open(model);

        double Magnitude(int i, int j, int k)
        {
            var n = 3 * (i + (nx * (j + (ny * k))));
            return Math.Sqrt((field.Values[n] * field.Values[n]) + (field.Values[n + 1] * field.Values[n + 1])
                + (field.Values[n + 2] * field.Values[n + 2]));
        }

        var lastX = 0.0;
        var lastY = 0.0;

        for (var k = 0; k < nz; k++)
        {
            for (var j = 0; j < ny; j++)
            {
                lastX = Math.Max(lastX, Magnitude(nx - 1, j, k));
            }

            for (var i = 0; i < nx; i++)
            {
                lastY = Math.Max(lastY, Magnitude(i, ny - 1, k));
            }
        }

        Assert.True(lastX > 1.0, $"the x = 0.3 mm face reads {lastX} V/m");
        Assert.True(lastY > 1.0, $"the y = 9 mm face reads {lastY} V/m");

        // The millimetre box hands back the same faces under the probe's own conversion, so a box
        // taken from it (as a later caller does) is not an ulp beyond the domain either.
        var (mmMin, mmMax) = probe.DomainMm()!.Value;
        var (siMin, siMax) = probe.DomainSi()!.Value;
        Assert.Equal(siMax.X, mmMax.X * 1e-3);
        Assert.Equal(siMax.Y, mmMax.Y * 1e-3);
        Assert.Equal(siMin.X, mmMin.X * 1e-3);
        Assert.Equal(siMin.Z, mmMin.Z * 1e-3);

        // Node for node, the last x layer is the probe on the face (x = 0.3 mm exactly).
        var (oy, sy) = (potential.OriginSi.Y, potential.SpacingSi.Y);
        var (oz, sz) = (potential.OriginSi.Z, potential.SpacingSi.Z);

        for (var j = 0; j < ny - 1; j += 5)
        {
            var point = new Vec3(0.3 * 1e-3, oy + (j * sy), oz + (sz * (nz / 2)));
            probe.Sample(in point, 0.0, out var phi, out _);
            Assert.Equal(phi, potential.Values[(nx - 1) + (nx * (j + (ny * (nz / 2))))]);
        }
    }

    [Fact]
    public void ABoxFlatAlongOneAxisIsOneLayerWithANonZeroSpacing()
    {
        var vti = Export(FaceModel(), "--box-mm", "-12.7,0,0,0.3,9,0", "--step-mm", "0.5");
        var potential = VtkImageData.Read(vti, "potential_V");

        Assert.Equal(1, potential.CountZ);
        Assert.Equal(1.0, potential.SpacingSi.Z);
        Assert.Contains("WholeExtent=\"0 26 0 18 0 0\"", vti, StringComparison.Ordinal);
        Assert.Equal(27 * 19, potential.Values.Length);
    }

    [Fact]
    public void ADryRunReportsTheGridAndSamplesNothing()
    {
        var model = FaceModel();
        var project = Path.Combine(_root, "dry");

        // A box reaching well past the domain: a run that sampled would count the nodes outside it.
        var (exitCode, stdout, stderr) = Run(
            "export", model, "--composite", "--project", project, "--box-mm", "-50,-50,0,50,50,0",
            "--step-mm", "1", "--dry-run", "--json");

        Assert.True(exitCode == 0, stderr);
        using var document = JsonDocument.Parse(stdout);
        Assert.False(document.RootElement.GetProperty("written").GetBoolean());
        Assert.Contains("101 x 101 x 1", document.RootElement.GetProperty("grid").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            document.RootElement.GetProperty("warnings").EnumerateArray(),
            w => w.GetProperty("code").GetString() == "probe.outside-domain");
        Assert.False(Directory.Exists(Path.Combine(project, ".einzel"))
            && Directory.EnumerateFiles(Path.Combine(project, ".einzel"), "*.vti").Any());
    }

    [Fact]
    public void AReflectedSolveOccupiesBothHalvesAndAFarPointIsOutsideIt()
    {
        var model = Template("template", "planar-mirror-pair");
        var probe = FieldProbe.Open(model);
        var (min, max) = probe.DomainMm()!.Value;

        // The box spans both mirrors, so a point a millimetre inside each end is a mirror image of
        // the other and holds the same potential. Over the declared half alone the two points are
        // a cap and a midpoint, nowhere near each other.
        var near = probe.At(0, new Vec3(min.X + 1.0, 0.0, 0.0), 0.0);
        var far = probe.At(1, new Vec3(max.X - 1.0, 0.0, 0.0), 0.0);

        Assert.Equal("ok", far.Status);
        Assert.Equal(near.PotentialV!.Value, far.PotentialV!.Value, 9);

        // Five metres off, the mirror pair has nothing to say, and the probe must say so rather than
        // report a 0 V, 0 V/m "ok" that reads as a measurement of vacuum.
        using var document = Probe(model, "--at-mm", "5000,0,0");
        Assert.Equal("outside-domain", Row(document).GetProperty("status").GetString());
        Assert.Contains("probe.outside-domain", Codes(document));
    }

    [Fact]
    public void ATiltedCrossSectionIsCheckedInItsOwnFrameAndBoxedByItsRotatedExtent()
    {
        // Nine degrees about the y axis through the origin.
        var model = FaceModel("tilted", tiltHalfTurns: 0.05);
        var probe = FieldProbe.Open(model);
        var (_, max) = probe.DomainMm()!.Value;

        // Unrotated, the cross-section spans z to 9 mm; turned, its far corner reaches past that.
        Assert.True(max.Z > 9.5, $"max z {max.Z} mm");

        using var outside = Probe(model, "--at-mm", "5000,4.5,0");
        Assert.Equal("outside-domain", Row(outside).GetProperty("status").GetString());

        // The plate's middle in the unrotated frame, turned into the world, is inside.
        var cos = Math.Cos(0.05 * Math.PI);
        var sin = Math.Sin(0.05 * Math.PI);
        var inside = probe.At(0, new Vec3(-6.0 * cos, 2.0, 6.0 * sin), 0.0);
        Assert.Equal("ok", inside.Status);
    }

    [Fact]
    public void OutsideEveryDomainWinsOverInsideADeclaredConductor()
    {
        // The post runs from y = 7 to 12 mm and the solve stops at 9: at y = 11 mm the point is
        // inside the post's declared shape and outside everything that was solved, so the value
        // reported is the field's answer outside its grid and not the post's 50 V.
        using var document = Probe(FaceModel(), "--at-mm", "-1.5,11,0");
        var row = Row(document);

        Assert.Equal("outside-domain", row.GetProperty("status").GetString());
        Assert.NotEqual(50.0, row.GetProperty("potentialV").GetDouble());

        var message = document.RootElement.GetProperty("warnings").EnumerateArray()
            .Single(w => w.GetProperty("code").GetString() == "probe.outside-domain")
            .GetProperty("message").GetString()!;
        Assert.Contains("not necessarily zero", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusedNodeIsWrittenAsLowerCaseNanAndMarkedInvalidAndReadsBack()
    {
        // The quadro-logarithmic field refuses its axis, which a flat box through it crosses.
        var model = Template("example", "orbital-trap-frequency");
        var vti = Export(model, "--box-mm", "-1,-1,0,1,1,0", "--step-mm", "0.5");

        Assert.Contains(" nan ", vti, StringComparison.Ordinal);
        Assert.DoesNotContain("NaN", vti, StringComparison.Ordinal);
        Assert.Contains("<DataArray type=\"UInt8\" Name=\"valid\" format=\"ascii\">", vti, StringComparison.Ordinal);

        var potential = VtkImageData.Read(vti, "potential_V");
        var valid = VtkImageData.Read(vti, "valid");
        var field = VtkImageData.Read(vti, "E_V_per_m");

        Assert.Equal(potential.Values.Length, valid.Values.Length);
        var refused = 0;

        for (var n = 0; n < potential.Values.Length; n++)
        {
            Assert.Equal(double.IsNaN(potential.Values[n]) ? 0.0 : 1.0, valid.Values[n]);
            Assert.Equal(double.IsNaN(potential.Values[n]), double.IsNaN(field.Values[3 * n]));
            refused += double.IsNaN(potential.Values[n]) ? 1 : 0;
        }

        // The five nodes on the axis (y = z = 0, x = -1 .. 1 mm).
        Assert.Equal(5, refused);
    }

    [Fact]
    public void ANegativeOrNonFiniteInstantIsOneErrorNamingTheFlag()
    {
        var model = Template("example", "orbital-trap-frequency");

        var (probeExit, _, probeError) = Run("probe", model, "--at-mm", "1,1,0", "--at-us", "-1");
        Assert.Equal(1, probeExit);
        Assert.Contains("--at-us", probeError, StringComparison.Ordinal);

        var (exportExit, _, exportError) = Run(
            "export", model, "--composite", "--project", Path.Combine(_root, "p"),
            "--box-mm", "-1,-1,0,1,1,0", "--at-us", "-1");
        Assert.Equal(1, exportExit);
        Assert.Contains("--at-us", exportError, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProbeAnswersFromTheTextItWasOpenedWithAndCarriesThatTextsHash()
    {
        // Read once: whatever is on disk by now, the field and the hash are of the text handed in,
        // so the header of a composite export cannot name one revision over another's field.
        var model = Template("example", "bounded-accelerating-gap");
        var onDisk = File.ReadAllText(model);
        var edited = onDisk.Replace("10000.0", "20000.0", StringComparison.Ordinal);
        Assert.NotEqual(onDisk, edited);

        var probe = FieldProbe.Open(model, edited);
        var sample = probe.At(0, new Vec3(10.0, 0.0, 0.0), 0.0);

        Assert.Equal(20000.0, sample.FieldVPerM![0], 6);
        Assert.Equal(ContentHash.OfText(edited), probe.ModelHash);
        Assert.Equal(ContentHash.OfText(onDisk), FieldProbe.Open(model).ModelHash);

        var vti = Export(model, "--box-mm", "0,0,0,10,0,0", "--step-mm", "5");
        Assert.Contains($"model: {ContentHash.OfText(onDisk)}", vti, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStreamedDocumentIsTheInMemoryOneNumberForNumber()
    {
        int[] counts = [3, 2, 1];
        double[] origin = [0.0, 1e-3, 0.0];
        double[] spacing = [1e-3 / 3.0, 0.1, 1.0];
        double[] potential = [1.0 / 3.0, -0.0 + 2.5e-300, double.NaN, 12345.678901234567, -7e10, 0.1];
        double[] field = [.. Enumerable.Range(0, 18).Select(i => i == 7 ? double.NaN : Math.PI * (i - 9) * 1e5)];

        var streamed = new StringWriter(CultureInfo.InvariantCulture);
        VtuWriter.WriteSampledField(streamed, counts, origin, spacing, potential, field);
        var text = VtuWriter.WriteSampledField(counts, origin, spacing, potential, field);

        Assert.Equal(text, streamed.ToString());

        var back = VtkImageData.Read(text, "potential_V").Values;

        for (var n = 0; n < potential.Length; n++)
        {
            Assert.Equal(potential[n], back[n]);
        }
    }
}
