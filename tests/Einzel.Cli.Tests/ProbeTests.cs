using System.Globalization;
using System.Text.Json;

namespace Einzel.Cli.Tests;

/// <summary>
/// <c>einzel probe</c> reads the potential and electric field of the composite field a run flies
/// through, at a point or along a line.
/// </summary>
/// <remarks>
/// Every expectation is a closed form the probe has no part in: a declared uniform field and its
/// linear potential, the ideal quadrupole's (x^2 - y^2) V / r0^2, the quadro-logarithmic well's
/// k z^2 / 2, the coaxial logarithm of the Kingdon template, and the electrode's own potential
/// inside the electrode. The driven cases are the reason the verb has an instant: reading a driven
/// field through the static interface answers at t = 0 without saying so, a defect this project has
/// met six times.
/// </remarks>
public sealed class ProbeTests : IDisposable
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

    private string Model(string kind, string name)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"{name}.json");
        Assert.Equal(0, Run("new", path, kind == "template" ? "--from-template" : "--from-example", name).ExitCode);
        return path;
    }

    private static string C(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static JsonDocument Probe(string model, params string[] flags)
    {
        var (exitCode, stdout, stderr) = Run(["probe", model, .. flags, "--json"]);
        Assert.True(exitCode == 0, stderr);
        return JsonDocument.Parse(stdout);
    }

    private static JsonElement Sample(JsonDocument document, int index) =>
        document.RootElement.GetProperty("samples")[index];

    private static double Potential(JsonElement sample) => sample.GetProperty("potentialV").GetDouble();

    private static double[] Field(JsonElement sample) =>
        [.. sample.GetProperty("fieldVPerM").EnumerateArray().Select(e => e.GetDouble())];

    private static string[] Codes(JsonDocument document) =>
        [.. document.RootElement.GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetProperty("code").GetString()!)];

    [Fact]
    public void AUniformFieldHasALinearPotentialAndAConstantField()
    {
        var model = Model("example", "bounded-accelerating-gap");

        // E = 10 kV/m declared, so phi = -E x: -100 V at 10 mm, and the field is the declared one.
        using var document = Probe(model, "--at-mm", "10,0,0");
        var sample = Sample(document, 0);

        Assert.Equal(-100.0, Potential(sample), 9);
        Assert.Equal(10000.0, Field(sample)[0], 6);
        Assert.Equal(0.0, Field(sample)[1]);
        Assert.Equal("ok", sample.GetProperty("status").GetString());
        Assert.False(document.RootElement.GetProperty("timeVarying").GetBoolean());
    }

    [Fact]
    public void ALineIncludesBothEndsExactlyAndCrossesARegionBoundary()
    {
        var model = Model("example", "bounded-accelerating-gap");

        // The region ends at x = 20 mm. Both ends are exactly the points asked for (an interpolated
        // end lands an ulp off), and beyond the boundary the element contributes nothing.
        using var document = Probe(
            model, "--line-mm", "0,0,0", "--to-mm", "0.3,0.1,0.07", "--samples", "7");

        var first = Sample(document, 0).GetProperty("positionMm");
        var last = Sample(document, 6).GetProperty("positionMm");

        Assert.Equal(0.0, first[0].GetDouble());
        Assert.Equal(0.3, last[0].GetDouble());
        Assert.Equal(0.1, last[1].GetDouble());
        Assert.Equal(0.07, last[2].GetDouble());
        Assert.Equal(7, document.RootElement.GetProperty("samples").GetArrayLength());

        using var across = Probe(model, "--line-mm", "0,0,0", "--to-mm", "40,0,0", "--samples", "5");

        // x = 0, 10, 20, 30, 40 mm: inside, inside, on the face, outside, outside.
        Assert.Equal(10000.0, Field(Sample(across, 1))[0], 6);
        Assert.Equal(0.0, Field(Sample(across, 3))[0]);
        Assert.Equal("outside-domain", Sample(across, 3).GetProperty("status").GetString());
        Assert.Equal("ok", Sample(across, 1).GetProperty("status").GetString());
        Assert.Contains("probe.outside-domain", Codes(across));
    }

    [Fact]
    public void AnIdealQuadrupoleFollowsItsClosedFormAtTheChosenInstant()
    {
        var model = Model("example", "rf-quadrupole-confined");

        // V = 200 V at 1 MHz on r0 = 4 mm: phi = V cos(2 pi f t) (x^2 - y^2) / r0^2 and
        // E = -2 V cos(...) (x, -y) / r0^2, the factor of two being the gradient's.
        const double V = 200.0;
        const double R0 = 4e-3;
        const double X = 1e-3;
        const double Y = 0.5e-3;

        double Phi(double cosine) => V * cosine * ((X * X) - (Y * Y)) / (R0 * R0);

        double Ex(double cosine) => -2.0 * V * cosine * X / (R0 * R0);

        // t = 0, T/4, T/2: the sign flips and the quarter point is through zero.
        foreach (var (us, cosine) in new[] { (0.0, 1.0), (0.5, -1.0), (1.0, 1.0) })
        {
            using var at = Probe(model, "--at-mm", "1,0.5,0", "--at-us", C(us));
            var sample = Sample(at, 0);

            Assert.Equal(Phi(cosine), Potential(sample), 9);
            Assert.Equal(Ex(cosine), Field(sample)[0], 5);
            Assert.True(at.RootElement.GetProperty("timeVarying").GetBoolean());
            Assert.Equal(us, at.RootElement.GetProperty("instantUs").GetDouble());
            Assert.DoesNotContain("probe.field-at-instant", Codes(at));
        }

        using var quarter = Probe(model, "--at-mm", "1,0.5,0", "--at-us", "0.25");
        Assert.True(Math.Abs(Potential(Sample(quarter, 0))) < 1e-9, "through zero at T/4");
    }

    [Fact]
    public void ADrivenFieldWithNoInstantSaysItIsAtZero()
    {
        var model = Model("example", "rf-quadrupole-confined");

        using var defaulted = Probe(model, "--at-mm", "1,0.5,0");

        Assert.Contains("probe.field-at-instant", Codes(defaulted));
        Assert.Equal(0.0, defaulted.RootElement.GetProperty("instantUs").GetDouble());
        Assert.Equal(9.375, Potential(Sample(defaulted, 0)), 9);

        // Human output states the instant too, not only the JSON.
        var (_, stdout, stderr) = Run("probe", model, "--at-mm", "1,0.5,0");
        Assert.Contains("field at t = 0 us", stdout);
        Assert.Contains("probe.field-at-instant", stdout + stderr);
    }

    [Fact]
    public void ASequencedFieldIsReadAtTheInstantAsked()
    {
        var model = Model("example", "sequenced-uniform");

        // Held for 2 us with the field off, then pushed at 20 kV/m.
        using var during = Probe(model, "--at-mm", "1,0,0", "--at-us", "1");
        using var after = Probe(model, "--at-mm", "1,0,0", "--at-us", "3");

        Assert.Equal(0.0, Field(Sample(during, 0))[0]);
        Assert.Equal(20000.0, Field(Sample(after, 0))[0], 6);
        Assert.Equal(-20.0, Potential(Sample(after, 0)), 9);
    }

    [Fact]
    public void ADrivenSolvedGeometryFlipsSignAndPassesThroughZero()
    {
        var model = Model("template", "quadrupole-rf");

        using var t0 = Probe(model, "--at-mm", "2,1.5,0", "--at-us", "0");
        using var half = Probe(model, "--at-mm", "2,1.5,0", "--at-us", "0.5");
        using var quarter = Probe(model, "--at-mm", "2,1.5,0", "--at-us", "0.25");

        var a = Potential(Sample(t0, 0));

        Assert.True(Math.Abs(a) > 1.0);
        Assert.Equal(-a, Potential(Sample(half, 0)), 9);
        Assert.True(Math.Abs(Potential(Sample(quarter, 0))) < 1e-9 * Math.Abs(a));
        Assert.Equal(-Field(Sample(t0, 0))[0], Field(Sample(half, 0))[0], 3);
    }

    [Fact]
    public void ACoaxialPairHasALogarithmicPotentialAndAnInverseRadiusField()
    {
        var model = Model("template", "kingdon-trap");

        // Between the wire and the cylinder, mid-length, phi = A ln(r) + B, so the potential at r
        // lies on the logarithm through two others, and E_r r is constant.
        double R(double r, string axis) => axis == "y"
            ? Potential(Sample(Probe(model, "--at-mm", $"20,{C(r)},0"), 0))
            : Potential(Sample(Probe(model, "--at-mm", $"20,0,{C(r)}"), 0));

        var (r1, r2, r3) = (2.0, 4.0, 7.0);
        var (p1, p2, p3) = (R(r1, "y"), R(r2, "y"), R(r3, "y"));

        var expected = p1 + ((p3 - p1) * Math.Log(r2 / r1) / Math.Log(r3 / r1));
        Assert.True(Math.Abs(p2 - expected) < 0.01 * Math.Abs(p3 - p1), $"{p2} against {expected}");

        // The axisymmetric mapping: the same radius along z gives the same answer as along y.
        Assert.Equal(p2, R(r2, "z"), 6);

        double Er(double r) => Field(Sample(Probe(model, "--at-mm", $"20,{C(r)},0"), 0))[1];

        var (c1, c2) = (Er(r1) * r1, Er(r3) * r3);
        Assert.True(Math.Abs(c1 - c2) < 0.03 * Math.Abs(c1), $"{c1} against {c2}");

        // The wire holds -100 V and the cylinder 0, and the solve runs on a coarse grid.
        Assert.True(R(3.0, "y") > R(1.0, "y") && R(1.0, "y") < 0.0);
    }

    [Fact]
    public void APointInsideAnElectrodeReturnsItsPotentialAndIsFlagged()
    {
        var model = Model("template", "einzel-lens");

        // Deep inside the centre tube's wall, which is held at 500 V.
        using var inside = Probe(model, "--at-mm", "50,8,0");
        var sample = Sample(inside, 0);

        Assert.Equal("inside-conductor", sample.GetProperty("status").GetString());
        Assert.Equal("centre", sample.GetProperty("conductor").GetString());
        Assert.Equal(500.0, Potential(sample), 6);
        Assert.True(sample.GetProperty("conductorDistanceMm").GetDouble() < 0.0);
        Assert.Contains("probe.inside-conductor", Codes(inside));

        // On the axis it is free space.
        using var free = Probe(model, "--at-mm", "50,0,0");
        Assert.Equal("ok", Sample(free, 0).GetProperty("status").GetString());
        Assert.DoesNotContain("probe.inside-conductor", Codes(free));
    }

    [Fact]
    public void TheAssembliesWarningsTravelOntoTheProbe()
    {
        var model = Model("template", "einzel-lens");

        // The same finding `einzel solve` reports for this geometry: a mesh node on a face a
        // conductor shares with the grounded boundary.
        using var document = Probe(model, "--at-mm", "50,0,0");

        Assert.Contains("mesh.node-on-shared-face", Codes(document));
        Assert.Contains("mesh.node-on-shared-face", Run("probe", model, "--at-mm", "50,0,0").Stderr);
        Assert.Equal(
            "solved",
            document.RootElement.GetProperty("elements")[0].GetProperty("basis").GetString());
    }

    [Fact]
    public void ARefusedPointIsARowAndNotACrashOfTheWholeProfile()
    {
        var model = Model("example", "orbital-trap-frequency");

        // The quadro-logarithmic field refuses its own axis. The profile crosses it.
        using var document = Probe(model, "--line-mm", "0,-3,0", "--to-mm", "0,3,0", "--samples", "7");

        Assert.Equal("refused", Sample(document, 3).GetProperty("status").GetString());
        Assert.False(Sample(document, 3).TryGetProperty("potentialV", out _));
        Assert.Equal("ok", Sample(document, 2).GetProperty("status").GetString());
        Assert.Equal("ok", Sample(document, 4).GetProperty("status").GetString());
        Assert.Contains("probe.refused", Codes(document));
    }

    [Fact]
    public void TheQuadroLogarithmicWellIsHarmonicAlongTheAxis()
    {
        var model = Model("example", "orbital-trap-frequency");

        // phi(z) - phi(0) = k z^2 / 2 at fixed radius, with k = 20 V/mm^2 declared: 90 V at 3 mm.
        using var centre = Probe(model, "--at-mm", "0,10,0");
        using var off = Probe(model, "--at-mm", "3,10,0");

        Assert.Equal(90.0, Potential(Sample(off, 0)) - Potential(Sample(centre, 0)), 6);
        Assert.Equal(-60000.0, Field(Sample(off, 0))[0], 3);
    }

    [Fact]
    public void ACsvCarriesTheUnitsInItsHeaderAndEveryRow()
    {
        var model = Model("example", "bounded-accelerating-gap");
        var csv = Path.Combine(_root, "profile.csv");

        var (exitCode, _, stderr) = Run(
            "probe", model, "--line-mm", "0,0,0", "--to-mm", "10,0,0", "--samples", "3", "--csv", csv);

        Assert.True(exitCode == 0, stderr);

        var lines = File.ReadAllLines(csv).Where(l => !l.StartsWith('#')).ToArray();

        Assert.Equal(
            "x_mm,y_mm,z_mm,potential_V,Ex_V_per_m,Ey_V_per_m,Ez_V_per_m,status,conductor", lines[0]);
        Assert.Equal(4, lines.Length);
        Assert.StartsWith("10,0,0,-100,10000,", lines[3]);
    }

    [Fact]
    public void TheCommandLineRefusesWhatHasNoUnitOrIsMalformed()
    {
        var model = Model("example", "bounded-accelerating-gap");

        // A bare coordinate flag has no unit: named, not defaulted.
        var bare = Run("probe", model, "--at", "1,0,0");
        Assert.Equal(1, bare.ExitCode);
        Assert.Contains("--at-mm", bare.Stderr);

        Assert.Equal(1, Run("probe", model, "--at-mm", "1,0").ExitCode);
        Assert.Equal(1, Run("probe", model).ExitCode);
        Assert.Equal(1, Run("probe", model, "--at-mm", "1,0,0", "--line-mm", "0,0,0").ExitCode);
        Assert.Equal(1, Run("probe", model, "--line-mm", "0,0,0").ExitCode);

        var one = Run("probe", model, "--line-mm", "0,0,0", "--to-mm", "1,0,0", "--samples", "1");
        Assert.Equal(1, one.ExitCode);
        Assert.Contains("samples", one.Stderr);

        Assert.Equal(1, Run("probe", model, "--at-mm", "1,0,0", "--at-us", "soon").ExitCode);
    }

    [Fact]
    public void AStillAndTheViewportOfADrivenFieldSayWhichInstantTheContoursAre()
    {
        var driven = Model("template", "quadrupole-rf");
        var fixedField = Model("template", "einzel-lens");
        var png = Path.Combine(_root, "still.png");

        // The still is the viewport's picture, so the warning is the viewport's: one place chooses
        // the instant and both surfaces carry the choice.
        var (code, stdout, stderr) = Run(
            "render", "still", driven, "--out", png, "--width-px", "200", "--height-px", "150", "--json");
        Assert.True(code == 0, stderr);

        using var still = JsonDocument.Parse(stdout);
        var codes = still.RootElement.GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetProperty("code").GetString()).ToArray();
        Assert.Contains("render.field-at-instant", codes);

        var viewport = Einzel.Commands.ViewportCommand.Execute(driven);
        Assert.Contains(viewport.Warnings, w => w.Code == "render.field-at-instant");

        // A static field has no instant to name.
        var calm = Einzel.Commands.ViewportCommand.Execute(fixedField);
        Assert.DoesNotContain(calm.Warnings, w => w.Code == "render.field-at-instant");
    }
}
