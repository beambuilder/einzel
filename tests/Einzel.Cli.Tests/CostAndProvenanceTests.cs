using System.Text.Json;
using Einzel.Project;

namespace Einzel.Cli.Tests;

/// <summary>
/// What a run will cost before it starts, and what a result says produced it.
/// </summary>
/// <remarks>
/// GRD-8 gates operations above a cost threshold and needs a number to gate on
/// without doing the work. GRD-7 says every result references a manifest, and
/// PRJ-3 says a manifest fully determines its run - which now has to include the
/// interpreter, because an extension result depends on which Python computed it.
/// </remarks>
public sealed class CostAndProvenanceTests : IDisposable
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

    /// <summary>A drift tube at a declared pressure, driven by a uniform field.</summary>
    private static string Tube(double pressureMbar, string densityStep = "") => $$"""
    {
      "schemaVersion": "0.4",
      "name": "tube",
      "description": "A drift tube for measuring what a diffusive run costs.",
      "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [2, 0, 0], "unit": "mm" },
        "direction": { "value": [1, 0, 0] },
        "accelerationPotential": { "value": 0.001, "unit": "V" },
        "cloud": { "ions": 1, "population": 1000,
                   "transverseSpread": { "value": 1.0, "unit": "mm" } }
      },
      "fields": [
        { "type": "uniform", "field": { "value": [2000, 0, 0], "unit": "V/m" } }
      ],
      "detector": {
        "planePoint": { "value": [40, 0, 0], "unit": "mm" },
        "normal": { "value": [-1, 0, 0] }
      },
      "transport": {
        "mode": "diffusion",
        "maximumFlightTime": { "value": 400, "unit": "us" },{{densityStep}}
        "densityGrid": {
          "minX": { "value": -2, "unit": "mm" }, "maxX": { "value": 40, "unit": "mm" },
          "minY": { "value": -6, "unit": "mm" }, "maxY": { "value": 6, "unit": "mm" },
          "intervalsX": 64, "intervalsY": 16
        },
        "gas": {
          "model": "hardSphere",
          "pressure": { "value": {{pressureMbar}}, "unit": "mbar" },
          "mass": { "value": 28.0134, "unit": "Da" },
          "crossSection": { "value": 250, "unit": "Å^2" }
        }
      }
    }
    """;

    private string Write(string name, double pressureMbar, string densityStep = "")
    {
        Assert.Equal(0, Run("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", $"{name}.json");
        File.WriteAllText(path, Tube(pressureMbar, densityStep));

        return path;
    }

    private static long Steps(string json) =>
        JsonDocument.Parse(json).RootElement.GetProperty("diffusion").GetProperty("steps").GetInt64();

    [Fact]
    public void TheEstimatedStepCountIsExactForAnAnalyticField()
    {
        // Not close: exact. The step is set by two stability limits, both computable
        // from the mesh, the mobility and the field - and where the field is analytic
        // it costs nothing to sample, so there is nothing left to approximate.
        //
        // Both sides call the same function, which is the point. An estimate computed
        // by a second implementation of the step rule is an estimate of that
        // implementation.
        var model = Write("tube", 1.0);

        var (estimateCode, estimate, _) = Run("estimate", model, "--json");
        Assert.Equal(0, estimateCode);

        var basis = JsonDocument.Parse(estimate).RootElement.GetProperty("basis").GetString()!;

        var (runCode, run, _) = Run("run", model, "--json");
        Assert.Equal(0, runCode);

        var actual = Steps(run);

        Assert.Contains($"about {actual:N0} steps", basis, StringComparison.Ordinal);

        // And it says it included both limits, rather than leaving a reader to guess
        // whether the number is a bound or a prediction.
        Assert.Contains("Both stability limits are included", basis, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEstimatedStepCountIsExactForAnImplicitRunToo()
    {
        // The exactness above is what makes the estimate worth gating on, and adding a
        // branch to the cost model is exactly how such a property gets lost - the
        // estimate would keep reporting the explicit step count while the run took a
        // sixty-fourth as many, and it would look conservative rather than wrong.
        //
        // Both sides multiply the same stability limit by the same declared gain, so
        // this stays exact for the same reason the explicit one does.
        var model = Write(
            "implicit-tube",
            1.0,
            """

        "densityStep": { "scheme": "implicit", "gain": 64 },
""");

        var (estimateCode, estimate, _) = Run("estimate", model, "--json");
        Assert.Equal(0, estimateCode);

        var basis = JsonDocument.Parse(estimate).RootElement.GetProperty("basis").GetString()!;

        var (runCode, run, _) = Run("run", model, "--json");
        Assert.Equal(0, runCode);

        var actual = Steps(run);

        Assert.Contains($"about {actual:N0} steps", basis, StringComparison.Ordinal);

        // And it says what it did rather than reporting a step count with no scheme
        // attached: the sweeps are a multiplier on the work and an assumption, and a
        // reader has to be able to tell that from the exact part.
        Assert.Contains("stepped implicitly at 64x", basis, StringComparison.Ordinal);
        Assert.Contains("Gauss-Seidel sweeps", basis, StringComparison.Ordinal);
        Assert.Contains("not knowable in advance", basis, StringComparison.Ordinal);
    }

    [Fact]
    public void AThinnerGasCostsMoreNotLess()
    {
        // The direction that catches people out, this engine's author included. The
        // diffusion coefficient goes as one over pressure, so a thinner gas diffuses
        // faster, needs a smaller explicit step, and costs more - the opposite of the
        // event-driven mode, where a thinner gas means fewer collisions.
        var dense = Write("dense", 1.0);
        var thin = Path.Combine(_root, "models", "thin.json");

        File.WriteAllText(thin, Tube(0.01));

        static double Seconds(string json) =>
            JsonDocument.Parse(json).RootElement.GetProperty("seconds").GetDouble();

        var (_, denseEstimate, _) = Run("estimate", dense, "--json");
        var (_, thinEstimate, _) = Run("estimate", thin, "--json");

        var denseSeconds = Seconds(denseEstimate);
        var thinSeconds = Seconds(thinEstimate);

        Assert.True(
            thinSeconds > denseSeconds,
            $"a hundredth of the pressure cost {thinSeconds:F2} s against {denseSeconds:F2} s, so the "
            + "estimate has the pressure dependence backwards");

        // And the basis says so, because a number that surprises without explaining
        // itself gets worked around rather than understood.
        Assert.Contains(
            "thinner gas is MORE expensive",
            JsonDocument.Parse(thinEstimate).RootElement.GetProperty("basis").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AStudyWritesAManifestItsResultReferences()
    {
        // GRD-7: every result references a manifest. Studies wrote results and no
        // manifest at all - and a sweep is exactly the operation whose draws are
        // worth being able to regenerate.
        Assert.Equal(0, Run("init", _root).ExitCode);

        var model = Path.Combine(_root, "models", "quad.json");
        Assert.Equal(0, Run("new", model, "--from-template", "quadrupole").ExitCode);

        var study = Path.Combine(_root, "studies", "sweep.json");
        Directory.CreateDirectory(Path.GetDirectoryName(study)!);

        File.WriteAllText(study, """
        {
          "model": "../models/quad.json",
          "figureOfMerit": "transmission",
          "channels": [
            { "parameter": "rodRatio", "distribution": "normal",
              "halfWidth": 0.01, "unit": "1" }
          ],
          "draws": 3, "seed": 11, "ions": 5
        }
        """);

        var (exitCode, stdout, _) = Run("sweep", study, "--project", _root, "--json");
        Assert.Equal(0, exitCode);

        var artifacts = JsonDocument.Parse(stdout).RootElement
            .GetProperty("artifacts")
            .EnumerateArray()
            .Select(a => a.GetString()!)
            .ToArray();

        var manifestPath = Path.Combine(
            _root, artifacts.Single(a => a.Contains("manifest", StringComparison.Ordinal)));

        Assert.True(File.Exists(manifestPath));

        var manifest = RunManifest.FromJson(File.ReadAllText(manifestPath))!;

        Assert.StartsWith("sha256:", manifest.ModelHash, StringComparison.Ordinal);
        Assert.Equal(EngineBuild.Version, manifest.EngineVersion);
        Assert.Contains(11L, manifest.Seeds);

        // No extension ran, so no interpreter is recorded. Absent rather than filled
        // in with whatever was on the path: an interpreter that took no part in a run
        // is not provenance, and recording it would imply it mattered.
        Assert.Empty(manifest.Extensions);
        Assert.Null(manifest.Interpreter);
    }

    [Fact]
    public void AStudyDrivenByAnExtensionRecordsWhichPythonRanIt()
    {
        // PRJ-3: a manifest fully determines its run. An extension result depends on
        // which interpreter computed it - a different Python is a different rounding
        // of a transcendental and in the worst case a different answer from the same
        // source file - so recording the engine version and not the interpreter would
        // leave a run reproducible in the part this project wrote and unreproducible
        // in the part it did not.
        Assert.Equal(0, Run("init", _root).ExitCode);
        Assert.Equal(0, Run("ext", "register", "shortest", "--project", _root).ExitCode);
        ExtensionRoom.Give(_root, "shortest");

        var model = Path.Combine(_root, "models", "quad.json");
        Assert.Equal(0, Run("new", model, "--from-template", "quadrupole").ExitCode);

        var study = Path.Combine(_root, "studies", "obj.json");
        Directory.CreateDirectory(Path.GetDirectoryName(study)!);

        File.WriteAllText(study, """
        {
          "model": "../models/quad.json",
          "figureOfMerit": "ext:shortest",
          "variables": [
            { "parameter": "rodRatio", "minimum": 1.05, "maximum": 1.25, "unit": "1" }
          ],
          "sense": "minimise", "algorithm": "nelderMead",
          "maximumEvaluations": 3, "ions": 5, "seed": 7
        }
        """);

        var (_, stdout, _) = Run("optimise", study, "--project", _root, "--json");

        var artifacts = JsonDocument.Parse(stdout).RootElement
            .GetProperty("artifacts")
            .EnumerateArray()
            .Select(a => a.GetString()!)
            .ToArray();

        var manifest = RunManifest.FromJson(File.ReadAllText(Path.Combine(
            _root, artifacts.Single(a => a.Contains("manifest", StringComparison.Ordinal)))))!;

        // GRD-6: the extension's identity and version, so the result cannot present
        // itself as first-party even at one remove.
        Assert.Contains("shortest 0.1.0", manifest.Extensions);

        Assert.False(string.IsNullOrWhiteSpace(manifest.Interpreter));
    }
}
