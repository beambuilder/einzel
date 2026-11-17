using System.Globalization;
using System.Text.Json;

namespace Einzel.Cli.Tests;

/// <summary>
/// <c>einzel export --composite</c> writes the field an ion sees - every element summed, at a stated
/// instant - as potential and E on a regular grid, and agrees with <c>einzel probe</c> node by node.
/// </summary>
/// <remarks>
/// The ordinary export writes each solved element's potential alone, one basis pattern per channel
/// for a driven geometry, with no instant and no field. This is the file a designer opens in
/// ParaView to see where the field is steep, so what it must not do is differ from what a run (and
/// the probe, which builds the same assembly) would say at the same point.
/// </remarks>
public sealed class CompositeExportTests : IDisposable
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

    private sealed record Volume(
        int[] Counts, double[] Origin, double[] Spacing, double[] Potential, double[] Field, string Header);

    private static Volume Read(string vti)
    {
        string Attribute(string name)
        {
            var start = vti.IndexOf($"{name}=\"", StringComparison.Ordinal) + name.Length + 2;
            return vti[start..vti.IndexOf('"', start)];
        }

        double[] Numbers(string text) =>
            [.. text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => double.Parse(v, CultureInfo.InvariantCulture))];

        double[] Array(string name)
        {
            var at = vti.IndexOf($"Name=\"{name}\"", StringComparison.Ordinal);
            var start = vti.IndexOf("format=\"ascii\">", at, StringComparison.Ordinal) + 15;
            return Numbers(vti[start..vti.IndexOf("</DataArray>", start, StringComparison.Ordinal)]);
        }

        var extent = Numbers(Attribute("WholeExtent"));

        return new Volume(
            [(int)extent[1] + 1, (int)extent[3] + 1, (int)extent[5] + 1],
            Numbers(Attribute("Origin")),
            Numbers(Attribute("Spacing")),
            Array("potential_V"),
            Array("E_V_per_m"),
            vti[..vti.IndexOf("-->", StringComparison.Ordinal)]);
    }

    private string Export(string model, params string[] flags)
    {
        var (exitCode, stdout, stderr) = Run(
            ["export", model, "--composite", "--project", _root, .. flags, "--json"]);
        Assert.True(exitCode == 0, stderr);

        using var document = JsonDocument.Parse(stdout);
        return File.ReadAllText(document.RootElement.GetProperty("artifacts")[0].GetString()!);
    }

    private static JsonElement ProbeAt(string model, double[] metres, string? atUs)
    {
        var mm = string.Join(",", metres.Select(v => (v * 1e3).ToString("R", CultureInfo.InvariantCulture)));
        string[] args = atUs is null
            ? ["probe", model, "--at-mm", mm, "--json"]
            : ["probe", model, "--at-mm", mm, "--at-us", atUs, "--json"];

        var (exitCode, stdout, stderr) = Run(args);
        Assert.True(exitCode == 0, stderr);
        return JsonDocument.Parse(stdout).RootElement.GetProperty("samples")[0];
    }

    private static void AgreeWithTheProbe(string model, Volume volume, string? atUs, IEnumerable<int[]> nodes)
    {
        foreach (var node in nodes)
        {
            var index = node[0] + (volume.Counts[0] * (node[1] + (volume.Counts[1] * node[2])));
            var at = new[]
            {
                volume.Origin[0] + (node[0] * volume.Spacing[0]),
                volume.Origin[1] + (node[1] * volume.Spacing[1]),
                volume.Origin[2] + (node[2] * volume.Spacing[2]),
            };

            var sample = ProbeAt(model, at, atUs);
            var potential = sample.GetProperty("potentialV").GetDouble();
            var field = sample.GetProperty("fieldVPerM").EnumerateArray().Select(e => e.GetDouble()).ToArray();

            var scale = Math.Max(1.0, Math.Abs(potential));
            Assert.True(Math.Abs(volume.Potential[index] - potential) <= 1e-9 * scale, $"potential at node {string.Join(",", node)}");

            for (var c = 0; c < 3; c++)
            {
                var fieldScale = Math.Max(1.0, Math.Abs(field[c]));
                Assert.True(
                    Math.Abs(volume.Field[(3 * index) + c] - field[c]) <= 1e-9 * fieldScale,
                    $"field component {c} at node {string.Join(",", node)}");
            }
        }
    }

    [Fact]
    public void AStaticFieldIsWrittenAsPotentialAndFieldAndMatchesTheProbeNodeByNode()
    {
        var model = Model("template", "einzel-lens");
        var volume = Read(Export(model, "--step-mm", "4"));

        Assert.Equal(volume.Counts[0] * volume.Counts[1] * volume.Counts[2], volume.Potential.Length);
        Assert.Equal(3 * volume.Potential.Length, volume.Field.Length);
        Assert.Contains("potential_V in volts", volume.Header);
        Assert.Contains("E_V_per_m in volts per metre", volume.Header);
        Assert.Contains("instant: none", volume.Header);

        // The solve's own warning rides in the file's header (GRD-2), as the plain export's does.
        Assert.Contains("mesh.node-on-shared-face", volume.Header);

        var (nx, ny, nz) = (volume.Counts[0], volume.Counts[1], volume.Counts[2]);

        AgreeWithTheProbe(
            model, volume, null,
            [[0, 0, 0], [nx / 2, 0, nz / 2], [nx / 3, ny / 4, nz / 2], [nx - 1, ny - 1, nz - 1], [nx / 2, ny / 2, nz - 1]]);

        // The grid spans the box exactly: last node on the far face.
        Assert.Equal(
            volume.Origin[0] + ((nx - 1) * volume.Spacing[0]),
            volume.Origin[0] + (volume.Spacing[0] * (nx - 1)));
    }

    [Fact]
    public void ADrivenFieldIsOneFrameAtTheInstantAskedAndSaysSo()
    {
        var model = Model("example", "rf-quadrupole-confined");
        var box = new[] { "--box-mm", "-3,-3,-1,3,3,1", "--step-mm", "1" };

        var half = Read(Export(model, [.. box, "--at-us", "0.5"]));
        var zero = Read(Export(model, [.. box, "--at-us", "0"]));
        var defaulted = Read(Export(model, box));

        Assert.Contains("instant: t = 0.5 us", half.Header);
        Assert.DoesNotContain("probe.field-at-instant", half.Header);

        // No instant asked for: the file says it is t = 0 and warns, rather than silently being it.
        Assert.Contains("probe.field-at-instant", defaulted.Header);
        Assert.Equal(zero.Potential, defaulted.Potential);

        // The sign flips over half a period, node for node.
        for (var n = 0; n < zero.Potential.Length; n++)
        {
            Assert.Equal(-zero.Potential[n], half.Potential[n], 9);
        }

        // And the file is the probe's field at that instant, node for node.
        AgreeWithTheProbe(model, half, "0.5", [[0, 0, 0], [1, 5, 1], [3, 2, 0], [6, 6, 2], [4, 4, 1]]);
        AgreeWithTheProbe(model, zero, "0", [[1, 5, 1], [4, 4, 1]]);
    }

    [Fact]
    public void ABoxIsRequiredWhereNoSolveDomainCanSupplyOne()
    {
        var model = Model("example", "rf-quadrupole-confined");

        // An analytic model has no solved domain, and a default would be a guess.
        var (exitCode, _, stderr) = Run("export", model, "--composite", "--project", _root);

        Assert.Equal(1, exitCode);
        Assert.Contains("--box-mm", stderr);
    }

    [Fact]
    public void AMalformedBoxOrStepIsRefusedByName()
    {
        var model = Model("example", "rf-quadrupole-confined");

        Assert.Equal(1, Run("export", model, "--composite", "--project", _root, "--box-mm", "1,2,3").ExitCode);
        Assert.Equal(
            1, Run("export", model, "--composite", "--project", _root, "--box-mm", "-3,-3,-1,3,3,1", "--step-mm", "0").ExitCode);

        var huge = Run(
            "export", model, "--composite", "--project", _root, "--box-mm", "-300,-300,-300,300,300,300", "--step-mm", "0.1");
        Assert.Equal(1, huge.ExitCode);
        Assert.Contains("--step-mm", huge.Stderr + huge.Stdout);
    }
}
