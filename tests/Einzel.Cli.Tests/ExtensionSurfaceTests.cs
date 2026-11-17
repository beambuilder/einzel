using System.Text.Json;

namespace Einzel.Cli.Tests;

/// <summary>
/// The extension authoring loop, driven through the command surface.
/// </summary>
/// <remarks>
/// Section 5 opens with the reason this exists: agents must extend the platform,
/// not only drive it, and an extension path requiring C#, a compile and a restart
/// is not usable in a loop. So what is under test is the loop - scaffold, call,
/// read what came back - rather than the runner, which has its own tests.
/// </remarks>
public sealed class ExtensionSurfaceTests : IDisposable
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

    private void Project()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
    }

    [Fact]
    public void RegisterScaffoldsAnExtensionThatRunsImmediately()
    {
        // The same rule 'einzel init' follows: what is scaffolded works. An agent
        // with a template that runs can change one thing and see what happens; an
        // agent with a stub has to fix somebody else's code before it can start.
        Project();

        var (exitCode, stdout, _) = Run("ext", "register", "shortest", "--project", _root);

        Assert.Equal(0, exitCode);
        Assert.Contains("extension.json", stdout, StringComparison.Ordinal);

        ExtensionRoom.Give(_root, "shortest");

        var payload = Path.Combine(_root, "payload.json");

        File.WriteAllText(payload, """
        { "parameters": {}, "figures": { "resolvingPower": 8347.0 } }
        """);

        var (testCode, testOut, testErr) = Run(
            "ext", "test", "shortest", "--project", _root, "--input", payload, "--json");

        Assert.Equal(0, testCode);

        using var document = JsonDocument.Parse(testOut);
        var root = document.RootElement;

        Assert.Equal("shortest", root.GetProperty("name").GetString());
        Assert.Equal(-8347.0, root.GetProperty("output").GetProperty("value").GetDouble(), 1e-9);

        Assert.True(root.GetProperty("elapsedMs").GetDouble() > 0.0);
        Assert.Empty(testErr);
    }

    [Fact]
    public void AnExtensionResultCannotPassAsFirstParty()
    {
        // GRD-6. A figure of merit computed by somebody's Python has to stay
        // distinguishable from one the engine computed, however far downstream it
        // travels.
        Project();
        Assert.Equal(0, Run("ext", "register", "attributed", "--project", _root).ExitCode);
        ExtensionRoom.Give(_root, "attributed");

        var payload = Path.Combine(_root, "payload.json");
        File.WriteAllText(payload, """{ "figures": { "resolvingPower": 1000.0 } }""");

        var (_, stdout, _) = Run(
            "ext", "test", "attributed", "--project", _root, "--input", payload, "--json");

        var warnings = JsonDocument.Parse(stdout).RootElement
            .GetProperty("warnings")
            .EnumerateArray()
            .ToArray();

        var attribution = warnings.Single(w => w.GetProperty("code").GetString() == "extension.attributed");

        Assert.Contains("attributed", attribution.GetProperty("message").GetString()!, StringComparison.Ordinal);

        // And EXT-3's gap is stated, non-suppressibly, on every sandboxed result.
        var isolation = warnings.Single(
            w => w.GetProperty("code").GetString() == "extension.isolation-incomplete");

        Assert.False(isolation.GetProperty("suppressible").GetBoolean());
    }

    [Fact]
    public void ListReportsWhatTheSandboxDoesNotDo()
    {
        // Claiming containment that is not applied is worse than having none and
        // saying so: the first makes somebody run untrusted code they would
        // otherwise have read first.
        Project();

        var (exitCode, stdout, stderr) = Run("ext", "list", "--project", _root, "--json");
        Assert.Equal(0, exitCode);

        var unenforced = JsonDocument.Parse(stdout).RootElement
            .GetProperty("unenforcedContainment")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToArray();

        Assert.Contains(unenforced, u => u.StartsWith("no network", StringComparison.Ordinal));
        Assert.Contains(unenforced, u => u.StartsWith("filesystem confinement", StringComparison.Ordinal));

        // Also on the human path, on stderr where a diagnostic belongs.
        var (_, _, plain) = Run("ext", "list", "--project", _root);

        Assert.Contains("does NOT enforce", plain + stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExtensionOutsideTheEngineRangeIsRefusedByName()
    {
        // EXT-8's check, on the path that uses it. The updater that would report
        // this before an update is applied is not built; the comparison is.
        Project();

        var folder = Path.Combine(_root, "extensions", "ancient");
        Directory.CreateDirectory(folder);

        File.WriteAllText(Path.Combine(folder, "extension.json"), """
        {
          "name": "ancient",
          "version": "0.1.0",
          "kind": "objective",
          "entry": "extension.py",
          "engineMaximum": "0.0.1"
        }
        """);

        File.WriteAllText(Path.Combine(folder, "extension.py"), """
        def run(payload):
            return {"value": 0.0}
        """);

        var (_, stdout, _) = Run("ext", "list", "--project", _root, "--json");

        var entry = JsonDocument.Parse(stdout).RootElement
            .GetProperty("extensions")
            .EnumerateArray()
            .Single();

        Assert.Contains("untested", entry.GetProperty("incompatibility").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownExtensionSaysWhatToDoInstead()
    {
        // AGT-3: an error is a recovery instruction.
        Project();

        var (exitCode, stdout, stderr) = Run("ext", "test", "nowhere", "--project", _root);

        Assert.NotEqual(0, exitCode);

        var text = stdout + stderr;

        Assert.Contains("no extension named 'nowhere'", text, StringComparison.Ordinal);
        Assert.Contains("ext register nowhere", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOptimiserCanDriveAPythonObjective()
    {
        // Section 13: the optimiser composes objectives from section 12, which may
        // be Python extensions. This is that sentence, end to end - a study naming
        // ext:name, an optimiser that does not know the difference, and a result.
        Project();
        Assert.Equal(0, Run("ext", "register", "shortest", "--project", _root).ExitCode);
        ExtensionRoom.Give(_root, "shortest");

        var model = Path.Combine(_root, "models", "quad.json");
        Assert.Equal(0, Run("new", model, "--from-template", "quadrupole").ExitCode);

        var study = Path.Combine(_root, "studies", "objective.json");
        Directory.CreateDirectory(Path.GetDirectoryName(study)!);

        File.WriteAllText(study, """
        {
          "model": "../models/quad.json",
          "figureOfMerit": "ext:shortest",
          "variables": [
            { "parameter": "rodRatio", "minimum": 1.05, "maximum": 1.25, "unit": "1" }
          ],
          "sense": "minimise",
          "algorithm": "nelderMead",
          "maximumEvaluations": 4,
          "ions": 5,
          "seed": 3
        }
        """);

        var (exitCode, stdout, _) = Run("optimise", study, "--project", _root, "--json");

        // Four evaluations will not meet a tolerance, so the search exits with
        // ConvergenceFailure and says why. That is the honest code for a
        // budget-limited search and is not what this test is about; what matters is
        // that the optimiser drove a Python objective at all.
        Assert.Equal((int)Einzel.Core.Errors.ExitCode.ConvergenceFailure, exitCode);

        var root = JsonDocument.Parse(stdout).RootElement;

        Assert.Equal("ext:shortest", root.GetProperty("figureOfMerit").GetProperty("name").GetString());
        Assert.True(root.GetProperty("evaluations").GetInt32() > 0);
    }
}
