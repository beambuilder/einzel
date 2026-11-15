using Einzel.Commands;
using Einzel.Core.Errors;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// What the window does, tested without one (Amendment 25, UI-1, GRD-9).
/// </summary>
/// <remarks>
/// <para>
/// <c>ShellSession</c> lives in the shell and this project cannot reference one — that is
/// invariant 1. So what is tested here is the behaviour it is made of, through the same
/// command objects and the same journal it drives, which is the honest thing to check: if
/// this passes and the window misbehaves, the fault is in layout and input, which is all
/// UI-1 leaves the window to own. (The session's own tests live in
/// <c>Einzel.Shell.Tests</c>, which may reference it.)
/// </para>
/// <para>
/// The parts that genuinely need a window — a grid that redraws, a status bar that turns
/// pink — are not testable here and are not the parts that can be wrong in an interesting
/// way.
/// </para>
/// </remarks>
public sealed class ShellSessionTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-shell-session", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
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

    private string Quadrupole()
    {
        Assert.Equal(0, Cli("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", "q.json");

        Assert.Equal(0, Cli("new", path, "--from-template", "quadrupole").ExitCode);

        return path;
    }

    private static readonly JournalAuthor Person = new("mike", AuthorKind.Human);
    private static readonly JournalAuthor Agent = new("claude", AuthorKind.Agent);

    /// <summary>
    /// A person's edit in the window and an agent's edit land on one journal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the whole point of the window driving the journal rather than the
    /// file.</b> Figure 6's loop B is a person with the shell open and an agent joining
    /// them on the same model; if the window wrote the document directly, the agent could
    /// not undo what the person just did and the person could not see what the agent did.
    /// Two parties, two histories, which is what a shared session is not.
    /// </para>
    /// <para>
    /// Writing the file is the shortest spelling and would have made the session
    /// one-sided silently — the seam this project has dropped evidence at five times, in
    /// the one place where the evidence <em>is</em> the point.
    /// </para>
    /// </remarks>
    [Fact]
    public void APersonsEditAndAnAgentsEditLandOnOneJournal()
    {
        var path = Quadrupole();
        var journal = new SessionJournal(path);

        // The person, in the window.
        journal.Apply(Person, "set inscribedRadius to 7", OutlineCommand.WithParameter(path, "inscribedRadius", 7.0));

        // The agent, over MCP, on the same journal.
        journal.Apply(Agent, "try 9", OutlineCommand.WithParameter(path, "inscribedRadius", 9.0));

        // And the person takes back what the agent did.
        journal.Undo(Person);

        foreach (var line in journal.Lines())
        {
            output.WriteLine(line);
        }

        Assert.Equal(7.0, Assert.Single(
            OutlineCommand.Execute(path).Parameters, p => p.Name == "inscribedRadius").Value);

        // Three entries, both names on the record, and the reversal saying whose edit it
        // took back.
        Assert.Equal(3, journal.Entries.Count);
        Assert.Contains("agent:claude", journal.Entries[2].Description, StringComparison.Ordinal);
        Assert.Equal(Person, journal.Entries[2].Author);
    }

    /// <summary>
    /// An edit out of bounds applies, and the model stays readable with the error on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Live validation has to survive an invalid state or it is useless.</b> A person
    /// typing 500 into a bounded parameter must still see the tree, with the complaint
    /// against it, rather than have the editor empty until they undo what they typed.
    /// </para>
    /// <para>
    /// This is the taint-never-block rule applied to input: the platform never stops you
    /// working, it refuses to let the result look cleaner than it is.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnOutOfBoundsEditAppliesAndTheTreeSurvivesIt()
    {
        var path = Quadrupole();
        var journal = new SessionJournal(path);

        // The journal refuses a document that does not validate, which is right for a
        // shared session - so an out-of-bounds value reaches the model the way a person
        // typing it does, through the file, and the tree is what has to cope.
        File.WriteAllText(path, OutlineCommand.WithParameter(path, "inscribedRadius", 500.0));

        var outline = OutlineCommand.Execute(path);

        output.WriteLine($"valid: {outline.Valid}, {outline.Parameters.Count} parameters still listed");

        foreach (var error in outline.Errors)
        {
            output.WriteLine($"  {error.Path}: {error.Constraint}");
        }

        Assert.False(outline.Valid);
        Assert.NotEmpty(outline.Parameters);
        Assert.Equal(500.0, Assert.Single(
            outline.Parameters, p => p.Name == "inscribedRadius").Value);

        // And the journal, seeing the document move underneath it, records that rather
        // than overwriting it - which is GRD-9 doing its job for an edit made outside
        // the session, including one the window made through the wrong door.
        var moved = journal.Reconcile();

        Assert.NotNull(moved);
        Assert.Equal(AuthorKind.Outside, moved!.Author.Kind);
    }

    /// <summary>Editing a derived parameter is refused, not silently ignored.</summary>
    /// <remarks>
    /// The window shows the refusal AGT-3 already wrote rather than composing its own,
    /// which is why the constraint has to name what derives it: "not editable" leaves a
    /// person guessing which knob to turn instead.
    /// </remarks>
    [Fact]
    public void EditingADerivedParameterIsRefusedWithSomethingToDoAboutIt()
    {
        var path = Quadrupole();

        var derived = OutlineCommand.Execute(path).Parameters
            .First(p => p.Expression is not null);

        var refusal = Assert.Throws<EinzelException>(
            () => OutlineCommand.WithParameter(path, derived.Name, 1.0));

        output.WriteLine($"{refusal.Error.Constraint}\n  {refusal.Error.Suggestion}");

        Assert.Contains(derived.Expression!, refusal.Error.Constraint, StringComparison.Ordinal);
        Assert.Contains(
            "set one of the parameters", refusal.Error.Suggestion!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Everything the window can do has a command spelling (Amendment 25).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The amendment's real content is a prohibition: a capability with no command
    /// spelling cannot be added to the window. So the check is that each thing the shell
    /// session does corresponds to a verb the CLI actually has — and the one that made
    /// this true was <c>einzel outline</c>, which had to be written because the window
    /// needed a model tree and no command returned one.
    /// </para>
    /// <para>
    /// The thing to review as the window grows is the in-process path acquiring an
    /// argument the command form has no spelling for. It will look like a convenience at
    /// the time.
    /// </para>
    /// </remarks>
    [Fact]
    public void EverythingTheWindowCanDoHasACommandSpelling()
    {
        var path = Quadrupole();

        // Reading the tree.
        Assert.Equal(0, Cli("outline", path).ExitCode);

        // Turning a knob.
        Assert.Equal(0, Cli("outline", path, "--set", "inscribedRadius=7").ExitCode);

        Assert.Equal(7.0, Assert.Single(
            OutlineCommand.Execute(path).Parameters, p => p.Name == "inscribedRadius").Value);

        // Checking it.
        Assert.Equal(0, Cli("validate", path).ExitCode);

        // And seeing what it does, cheaply, which is what a person wants while dragging
        // something (AGT-5).
        Assert.Equal(0, Cli("preview", path).ExitCode);

        output.WriteLine("outline, outline --set, validate, preview: all reachable from the CLI");
    }
}
