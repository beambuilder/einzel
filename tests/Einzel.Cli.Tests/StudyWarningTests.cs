using System.Text.Json;

namespace Einzel.Cli.Tests;

/// <summary>
/// What a study says about the quality of the flights it ranked.
/// </summary>
/// <remarks>
/// GRD-2 requires a warning to propagate through the engine, the command layer,
/// the CLI and the exported file, and to be non-suppressible above threshold. A
/// sweep broke that at one seam and it was the seam that matters most: the
/// evaluator a driver ranks by returns a bare double, so every warning the flight
/// behind it earned stopped there. A thousand draws could each have been computed
/// in a field that missed its tolerance and the study would report a distribution,
/// a ranking, and nothing else.
/// </remarks>
public sealed class StudyWarningTests : IDisposable
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

    /// <summary>A quadrupole whose solve cannot reach the tolerance it declares.</summary>
    /// <remarks>
    /// A tolerance below round-off rather than a broken geometry, so the field is
    /// otherwise ordinary and the only thing wrong with the study is the one thing
    /// under test.
    /// </remarks>
    private string StrainedQuadrupole()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);

        var model = Path.Combine(_root, "models", "quad.json");

        Assert.Equal(0, Run("new", model, "--from-template", "quadrupole").ExitCode);

        var text = File.ReadAllText(model);
        var at = text.IndexOf("\"solve\":", StringComparison.Ordinal);

        Assert.True(at >= 0, "expected a solved2d element to strain");

        at = text.IndexOf('{', at) + 1;
        File.WriteAllText(model, text[..at] + "\"tolerance\": 1e-30," + text[at..]);

        return model;
    }

    private string Study(string name, string body)
    {
        var path = Path.Combine(_root, "studies", $"{name}.json");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, body);

        return path;
    }

    [Fact]
    public void ASweepCarriesTheWarningsItsDrawsEarned()
    {
        var model = StrainedQuadrupole();

        var study = Study("tol", """
        {
          "model": "../models/quad.json",
          "figureOfMerit": "flightTime",
          "channels": [
            { "parameter": "rodRatio", "distribution": "normal",
              "halfWidth": 0.005, "unit": "1" }
          ],
          "draws": 2, "seed": 5, "oneAtATime": false
        }
        """);

        Assert.True(File.Exists(model));

        var (exitCode, stdout, stderr) = Run("sweep", study, "--project", _root, "--json");

        // Taint, never block: the sweep still ran and still ranked. What changed is
        // that it says what it ranked.
        Assert.Equal(0, exitCode);

        var warnings = JsonDocument.Parse(stdout).RootElement
            .GetProperty("warnings")
            .EnumerateArray()
            .ToArray();

        var missed = warnings.Single(
            w => w.GetProperty("code").GetString() == "field.not-converged");

        // GRD-3: only an advisory may be silenced, and a solve that missed its
        // tolerance is not advice.
        Assert.False(missed.GetProperty("isSuppressible").GetBoolean());

        // Counted, because "on 1 of 3 draws" and "on 3 of 3 draws" are the
        // difference between a corner of the tolerance box and a study to discard.
        Assert.Contains("evaluations)", missed.GetProperty("message").GetString()!, StringComparison.Ordinal);

        // On stderr the study's one field line, as a run prints its own, and no warning: under
        // --json the warnings are in the document.
        Assert.StartsWith("  field: ", Assert.Single(stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries)).TrimEnd('\r'), StringComparison.Ordinal);
    }

    [Fact]
    public void TheHumanOutputPutsThemOnStderrAndEchoesTheBands()
    {
        var model = StrainedQuadrupole();

        var study = Study("tol", """
        {
          "model": "../models/quad.json",
          "figureOfMerit": "flightTime",
          "channels": [
            { "parameter": "rodRatio", "distribution": "normal",
              "halfWidth": 0.005, "unit": "1" }
          ],
          "draws": 2, "seed": 5
        }
        """);

        Assert.True(File.Exists(model));

        var (exitCode, stdout, stderr) = Run("sweep", study, "--project", _root);

        Assert.Equal(0, exitCode);

        // CLI-2: a diagnostic goes to stderr, so a caller piping stdout into a file
        // still sees it.
        Assert.Contains("field.not-converged", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("field.not-converged", stdout, StringComparison.Ordinal);

        // The attribution table says what the two ends were, not only how far apart
        // they are. A swing alone does not say which direction hurts.
        Assert.Contains("swing", stdout, StringComparison.Ordinal);
        Assert.Contains("..", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOptimisationCarriesThemToo()
    {
        // The case that matters more: a search walks towards whatever scores best,
        // so a corner of the box where the field stops converging is somewhere it
        // will actively go.
        var model = StrainedQuadrupole();

        var study = Study("opt", """
        {
          "model": "../models/quad.json",
          "figureOfMerit": "flightTime",
          "variables": [
            { "parameter": "rodRatio", "minimum": 1.10, "maximum": 1.20, "unit": "1" }
          ],
          "sense": "minimise", "algorithm": "nelderMead",
          "maximumEvaluations": 3, "seed": 3
        }
        """);

        Assert.True(File.Exists(model));

        var (_, stdout, _) = Run("optimise", study, "--project", _root, "--json");

        var warnings = JsonDocument.Parse(stdout).RootElement
            .GetProperty("warnings")
            .EnumerateArray()
            .Select(w => w.GetProperty("code").GetString())
            .ToArray();

        Assert.Contains("field.not-converged", warnings);
    }

    [Fact]
    public void ACleanModelDoesNotEarnTheWarningTheStrainedOneDoes()
    {
        // The control. Without it the tests above pass on a build that attaches a
        // warning to everything, which is the same failure in the other direction:
        // a warning that is always present is one nobody reads.
        //
        // Not an assertion that the list is empty, because it is not and should not
        // be - a sweep now reports the same convergence provenance a run does, and
        // that is the point of the seam being open. What has to be absent is the
        // specific claim being made about the strained model.
        Assert.Equal(0, Run("init", _root).ExitCode);

        var study = Study("clean", """
        {
          "model": "../models/reflectron.json",
          "figureOfMerit": "flightTime",
          "channels": [
            { "parameter": "turningDepth", "distribution": "normal",
              "halfWidth": 0.05, "unit": "mm" }
          ],
          "draws": 2, "seed": 5, "oneAtATime": false
        }
        """);

        var (exitCode, stdout, _) = Run("sweep", study, "--project", _root, "--json");

        Assert.Equal(0, exitCode);

        var codes = JsonDocument.Parse(stdout).RootElement
            .GetProperty("warnings")
            .EnumerateArray()
            .Select(w => w.GetProperty("code").GetString())
            .ToArray();

        Assert.DoesNotContain("field.not-converged", codes);
    }
}
