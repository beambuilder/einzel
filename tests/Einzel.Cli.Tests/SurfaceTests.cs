using System.Text.Json;
using Einzel.Core.Model;
using Einzel.Cli;

namespace Einzel.Cli.Tests;

/// <summary>
/// The verbs an agent uses to find out what exists, and the contract every verb
/// keeps.
/// </summary>
/// <remarks>
/// <para>
/// The Phase 1 acceptance criterion is that an agent builds a model from prose
/// with nothing but a project directory and the CLI. Everything here is what that
/// sentence actually requires: the platform has to be able to describe its own
/// format, list what it ships, and hand over a working starting point - because
/// there are no forum posts to search and no decades of example files in anyone's
/// training data.
/// </para>
/// <para>
/// These drive <see cref="Program.Main"/> rather than the command objects. The
/// things that break an agent loop are exit codes, which stream output lands on,
/// and whether <c>--json</c> parses, and none of those live in the engine.
/// </para>
/// </remarks>
public sealed class SurfaceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-surface", Guid.NewGuid().ToString("N"));

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

    [Fact]
    public void TheSchemaDescribesTheFormatAndSaysWhereItCameFrom()
    {
        var (exitCode, stdout, _) = Run("schema");

        Assert.Equal(0, exitCode);

        using var schema = JsonDocument.Parse(stdout);
        var root = schema.RootElement;

        Assert.Equal(ModelSchema.CurrentVersion, root.GetProperty("x-schemaVersion").GetString());
        Assert.Equal("object", root.GetProperty("type").GetString());

        var properties = root.GetProperty("properties");

        // The top-level shape an agent has to get right.
        foreach (var expected in new[] { "schemaVersion", "parameters", "ion", "source", "fields", "detector", "transport" })
        {
            Assert.True(properties.TryGetProperty(expected, out _), $"the schema omits '{expected}'");
        }

        // Generated from the types, so a property added to the format cannot fail
        // to appear here. That is the whole reason it is generated.
        Assert.True(
            root.GetProperty("$defs").TryGetProperty("SolvedFieldDocument", out _),
            "the schema does not describe a solved field, so an agent cannot write one");
    }

    [Fact]
    public void TheSchemaCarriesTheDescriptionsTheCodeCarries()
    {
        // AGT-7: descriptions come from the same metadata as everything else, so
        // they cannot drift. If the XML documentation is missing the schema still
        // emits and says so rather than looking complete.
        var (_, stdout, _) = Run("schema");

        using var schema = JsonDocument.Parse(stdout);
        var comment = schema.RootElement.GetProperty("$comment").GetString();

        if (comment!.Contains("UNAVAILABLE", StringComparison.Ordinal))
        {
            // A deployment without the XML file is legitimate. What is not
            // legitimate is being quiet about it.
            return;
        }

        var described = schema.RootElement
            .GetProperty("properties")
            .GetProperty("parameters")
            .GetProperty("description")
            .GetString();

        Assert.False(string.IsNullOrWhiteSpace(described));
        Assert.Contains("parameter", described!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("templates")]
    [InlineData("examples")]
    public void ACatalogueListsWhatShipsAndPrintsWhatIsAskedFor(string verb)
    {
        var (exitCode, listing, _) = Run(verb, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(listing);
        var entries = document.RootElement.GetProperty("entries");

        Assert.True(entries.GetArrayLength() > 0, $"nothing is listed by '{verb}'");

        var names = entries.EnumerateArray().Select(e => e.GetProperty("name").GetString()!).ToArray();

        // CLI-5: deterministic ordering. A catalogue that reorders between runs
        // makes every diff of agent output noisy for no reason.
        Assert.Equal([.. names.OrderBy(n => n, StringComparer.Ordinal)], names);

        // Every entry describes itself, read from the artifact rather than a
        // table beside it.
        foreach (var entry in entries.EnumerateArray())
        {
            var description = entry.GetProperty("description").GetString();
            Assert.False(string.IsNullOrWhiteSpace(description), $"{entry.GetProperty("name")} has no description");
        }

        // And asking for one by name gives a model that validates.
        var (readCode, text, _) = Run(verb, names[0]);
        Assert.Equal(0, readCode);

        using var model = JsonDocument.Parse(text);
        Assert.True(model.RootElement.TryGetProperty("schemaVersion", out _));
    }

    [Fact]
    public void AnUnknownCatalogueNameSaysWhatThereIsInstead()
    {
        // AGT-3: an error is a recovery instruction. "No such template" leaves an
        // agent guessing; the list of real names does not.
        var (exitCode, _, stderr) = Run("templates", "no-such-device");

        Assert.Equal(1, exitCode);
        Assert.Contains("no-such-device", stderr, StringComparison.Ordinal);
        Assert.Contains("quadrupole", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void NewCreatesAWorkingModelFromATemplate()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var model = Path.Combine(_root, "models", "quad.json");

        Assert.Equal(0, Run("new", model, "--from-template", "quadrupole").ExitCode);
        Assert.True(File.Exists(model));

        // The whole point: what it wrote is immediately valid.
        Assert.Equal(0, Run("validate", model).ExitCode);
    }

    [Fact]
    public void NewRefusesToOverwriteAModel()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var model = Path.Combine(_root, "models", "quad.json");

        Assert.Equal(0, Run("new", model, "--from-template", "quadrupole").ExitCode);
        var (exitCode, _, stderr) = Run("new", model, "--from-template", "quadrupole");

        Assert.Equal(1, exitCode);
        Assert.Contains("already exists", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void DryRunWritesNothing()
    {
        // CLI-4 on every mutating command. An agent exploring what a command would
        // do should not have to undo it afterwards.
        Assert.Equal(0, Run("init", _root).ExitCode);
        var model = Path.Combine(_root, "models", "quad.json");

        var (exitCode, stdout, _) = Run("new", model, "--from-template", "quadrupole", "--dry-run");

        Assert.Equal(0, exitCode);
        Assert.Contains("would write", stdout, StringComparison.Ordinal);
        Assert.False(File.Exists(model), "--dry-run wrote the file anyway");
    }

    [Fact]
    public void SolveReportsWhatTheDiscretisationDidWithTheGeometry()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var model = Path.Combine(_root, "models", "quad.json");
        Assert.Equal(0, Run("new", model, "--from-template", "quadrupole").ExitCode);

        var (exitCode, stdout, _) = Run("solve", model, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var element = document.RootElement.GetProperty("elements")[0];

        Assert.True(element.GetProperty("converged").GetBoolean());
        Assert.True(element.GetProperty("cutLinks").GetInt32() > 0, "round rods should cut the stencil");

        // The maximum principle, which is the cheapest exact check that a solve
        // has not diverged: no potential may exceed the largest applied value.
        Assert.True(
            element.GetProperty("peakPotentialVolts").GetDouble() <= 100.0 + 1e-9,
            "a potential above the applied value means the solve diverged");
    }

    [Fact]
    public void SolveNamesTheChannelWhenAnElementHasSeveral()
    {
        // A driven element is one solve per basis channel. The terminal printed each
        // as "field 0" and nothing else, so the RF funnel's two channels came out as
        // two identical blocks while --json numbered them 0 and 1.
        Assert.Equal(0, Run("init", _root).ExitCode);
        var funnel = Path.Combine(_root, "models", "funnel.json");
        Assert.Equal(0, Run("new", funnel, "--from-example", "ion-funnel-rf").ExitCode);

        var (exitCode, stdout, _) = Run("solve", funnel);
        Assert.Equal(0, exitCode);

        Assert.Equal(["field 0 channel 0", "field 0 channel 1"], Headers(stdout));

        // The control: a static element has one channel and carries no number,
        // so the label changes only where there is something to tell apart.
        var quadrupole = Path.Combine(_root, "models", "quad.json");
        Assert.Equal(0, Run("new", quadrupole, "--from-template", "quadrupole").ExitCode);

        var (quadrupoleExit, quadrupoleOut, _) = Run("solve", quadrupole);
        Assert.Equal(0, quadrupoleExit);

        Assert.Equal(["field 0"], Headers(quadrupoleOut));
    }

    /// <summary>
    /// The label of each block <c>einzel solve</c> prints: a header line up to the
    /// two spaces that separate it from the grid.
    /// </summary>
    private static List<string> Headers(string stdout) =>
        stdout.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("field ", StringComparison.Ordinal))
            .Select(line => line[..line.IndexOf("  ", StringComparison.Ordinal)])
            .ToList();

    [Fact]
    public void EstimateCostsNothingAndSaysHowItGuessed()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var model = Path.Combine(_root, "models", "quad.json");
        Assert.Equal(0, Run("new", model, "--from-template", "quadrupole").ExitCode);

        var (exitCode, stdout, _) = Run("estimate", model, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;

        Assert.True(root.GetProperty("seconds").GetDouble() > 0.0);

        // An estimate presented with the confidence of a measurement is the same
        // mistake GRD-1 exists to prevent, so it states its basis.
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("basis").GetString()));

        // The gate is the comparison it reports, and nothing about this machine. The rates behind
        // an estimate are measured by a pilot on the machine it runs on (Amendment 33), so
        // asserting that this model sits UNDER the threshold was asserting how fast the runner
        // was: it failed on a loaded Windows runner whose pilot put the estimate over 30 s.
        var seconds = root.GetProperty("seconds").GetDouble();
        var threshold = root.GetProperty("thresholdSeconds").GetDouble();

        Assert.True(threshold > 0.0);
        Assert.Equal(seconds > threshold, root.GetProperty("aboveThreshold").GetBoolean());
    }

    [Fact]
    public void ExportWritesAFieldParaViewCanOpen()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var model = Path.Combine(_root, "models", "quad.json");
        Assert.Equal(0, Run("new", model, "--from-template", "quadrupole").ExitCode);

        var (exitCode, stdout, _) = Run("export", model, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var artifact = document.RootElement.GetProperty("artifacts")[0].GetString()!;

        Assert.True(File.Exists(artifact));

        // ImageData rather than an unstructured grid, because a uniform grid is
        // image data and saying so is both smaller and more useful in ParaView.
        var text = File.ReadAllText(artifact);
        Assert.Contains("type=\"ImageData\"", text, StringComparison.Ordinal);
        Assert.Contains("Spacing=", text, StringComparison.Ordinal);
        Assert.Contains("potential_V", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DoctorNoticesWhenGuidanceHasFallenBehindTheEngine()
    {
        // PRJ-6, made detectable. Guidance written for one engine version sitting
        // in a project driven by another is worse than none, because an agent
        // trusts it and cannot see the drift. The version stamp is what makes it
        // visible, and this is the thing that looks.
        Assert.Equal(0, Run("init", _root).ExitCode);

        var agents = Path.Combine(_root, "AGENTS.md");
        var contents = File.ReadAllText(agents);
        File.WriteAllText(agents, contents.Replace("Generated by einzel ", "Generated by einzel 0.0.1-stale ", StringComparison.Ordinal));

        var (exitCode, _, stderr) = Run("doctor", _root);

        Assert.Equal(1, exitCode);
        Assert.Contains("AGENTS.md", stderr, StringComparison.Ordinal);
        Assert.Contains("agents-md", stderr, StringComparison.Ordinal);

        // And regenerating fixes it.
        Assert.Equal(0, Run("agents-md", _root).ExitCode);
        Assert.Equal(0, Run("doctor", _root).ExitCode);
    }

    [Fact]
    public void RegeneratingGuidanceKeepsWhatTheProjectWrote()
    {
        // The file has two authors. The platform owns the generated region; the
        // project owns everything else, and that half is the reason anyone opens
        // it. Regenerating by overwriting would satisfy PRJ-6 and destroy it.
        Assert.Equal(0, Run("init", _root).ExitCode);

        var agents = Path.Combine(_root, "AGENTS.md");
        File.AppendAllText(agents, "\n## Project notes\n\nThe mirror gap is fixed by the board stack.\n");

        Assert.Equal(0, Run("agents-md", _root).ExitCode);

        var contents = File.ReadAllText(agents);
        Assert.Contains("The mirror gap is fixed by the board stack.", contents, StringComparison.Ordinal);
        Assert.Contains(Commands.AgentsFile.BeginMarker, contents, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryVerbAcceptsJsonAndPutsItOnStdout()
    {
        // CLI-1 and CLI-2 together: --json on every verb, results on stdout so a
        // caller may pipe straight to a parser without filtering.
        Assert.Equal(0, Run("init", _root).ExitCode);
        var model = Path.Combine(_root, "models", "quad.json");
        Assert.Equal(0, Run("new", model, "--from-template", "quadrupole").ExitCode);

        string[][] invocations =
        [
            ["templates", "--json"],
            ["examples", "--json"],
            ["doctor", _root, "--json"],
            ["validate", model, "--json"],
            ["estimate", model, "--json"],
            ["solve", model, "--json"],
            ["export", model, "--json", "--dry-run"],
            ["agents-md", _root, "--json", "--dry-run"],
        ];

        foreach (var invocation in invocations)
        {
            var (_, stdout, _) = Run(invocation);

            // Parsing is the assertion: anything printed alongside would break it.
            var parsed = () => JsonDocument.Parse(stdout);
            var document = parsed();

            Assert.NotEqual(JsonValueKind.Undefined, document.RootElement.ValueKind);
        }
    }

    [Fact]
    public void AnUnknownVerbIsAValidationFailureAndPrintsUsage()
    {
        var (exitCode, _, stderr) = Run("frobnicate");

        Assert.Equal(1, exitCode);
        Assert.Contains("unknown command", stderr, StringComparison.Ordinal);
        Assert.Contains("doctor", stderr, StringComparison.Ordinal);
    }
}
