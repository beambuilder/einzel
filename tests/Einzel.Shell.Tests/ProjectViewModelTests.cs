using System.IO;

using Einzel.Commands;
using Einzel.Shell.Presentation;

using Xunit.Abstractions;

namespace Einzel.Shell.Tests;

/// <summary>
/// The project view's own decisions: which model is open, and how the four states read.
/// </summary>
/// <remarks>
/// The state itself is <see cref="ProjectCommand"/>'s and is tested in Einzel.Cli.Tests,
/// which runs on Linux. What is left here is presentation, and it has two things in it
/// worth pinning: that the open model is marked as such, and that "not run" does not read
/// as a problem.
/// </remarks>
public sealed class ProjectViewModelTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-shell-project", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Example(string name, string as_)
    {
        if (!Directory.Exists(Path.Combine(_root, "models")))
        {
            Assert.Equal(0, Einzel.Cli.Program.Main(["init", _root]));
        }

        var path = Path.Combine(_root, "models", $"{as_}.json");

        if (!File.Exists(path))
        {
            Assert.Equal(0, Einzel.Cli.Program.Main(["new", path, "--from-example", name]));
        }

        return path;
    }

    private static ProjectViewModel Over(string modelPath) =>
        new(new ShellSession(modelPath, new JournalAuthor("test", AuthorKind.Human)));

    /// <summary>The model the window has open is marked among the rest.</summary>
    /// <remarks>
    /// The project view is read while editing one model, so which row is the one on screen
    /// is the first thing a reader looks for. Exactly one row carries it, which is the half
    /// that would fail on a comparison that matched everything or nothing.
    /// </remarks>
    [Fact]
    public void TheOpenModelIsMarked()
    {
        var open = Example("single-stage-reflectron", "refl");

        Example("drift-tube-diffusion", "dt");

        var view = Over(open);

        Assert.True(view.Refresh());

        foreach (var row in view.Models)
        {
            output.WriteLine($"{row.State,-8} {row.Path,-26} open {row.Open}  {row.Mode}");
        }

        var marked = Assert.Single(view.Models, r => r.Open);

        Assert.EndsWith("refl.json", marked.Path, StringComparison.Ordinal);
    }

    /// <summary>A model nobody has run does not read as a problem.</summary>
    /// <remarks>
    /// <b>The distinction the severity exists for.</b> A fresh project has run nothing, and
    /// a view that painted every row the same colour as a stale result would say a project
    /// is broken when it is merely new. "not run" is where every model starts.
    /// </remarks>
    [Fact]
    public void AModelNobodyHasRunDoesNotReadAsAProblem()
    {
        var model = Example("single-stage-reflectron", "refl");

        var view = Over(model);

        Assert.True(
            view.Refresh(),
            "a project whose models have never been run has nothing wrong with it");

        var row = Assert.Single(view.Models, r => r.Path.EndsWith("refl.json", StringComparison.Ordinal));

        output.WriteLine($"{row.State} / {row.Severity}: {row.Detail}");

        Assert.Equal("not run", row.State);
        Assert.Equal("neutral", row.Severity);
        Assert.NotEqual("bad", row.Severity);
        Assert.NotEqual("warn", row.Severity);
    }

    /// <summary>An invalid model reads as bad, and says why.</summary>
    [Fact]
    public void AnInvalidModelReadsAsBad()
    {
        var model = Example("single-stage-reflectron", "refl");

        File.WriteAllText(
            Path.Combine(_root, "models", "broken.json"),
            """{"schemaVersion": "0.6", "nonsense": 1}""");

        var view = Over(model);

        Assert.False(view.Refresh(), "an invalid model is something to fix");

        var row = Assert.Single(
            view.Models, r => r.Path.EndsWith("broken.json", StringComparison.Ordinal));

        output.WriteLine($"{row.State} / {row.Severity}: {row.Detail}");

        Assert.Equal("invalid", row.State);
        Assert.Equal("bad", row.Severity);

        // AGT-3: the detail is the recovery instruction, not the word "invalid" again.
        Assert.Contains("nonsense", row.Detail, StringComparison.Ordinal);
    }

    /// <summary>The status line says where and how much, in one line.</summary>
    [Fact]
    public void TheStatusSaysWhereAndHowMuch()
    {
        var model = Example("single-stage-reflectron", "refl");

        var view = Over(model);

        Assert.Equal("not yet read", view.Status);

        view.Refresh();

        output.WriteLine(view.Status);

        Assert.Contains(_root, view.Status, StringComparison.Ordinal);
        Assert.Contains("never run", view.Status, StringComparison.Ordinal);
    }

    /// <summary>Every action reaches the journal as a command line (Amendment 25).</summary>
    /// <remarks>
    /// The rule the shell is held to: a capability with no command spelling cannot be added
    /// to the window, and a person's session hands over to an agent in the same vocabulary.
    /// </remarks>
    [Fact]
    public void ReadingTheProjectIsJournalledAsACommand()
    {
        var model = Example("single-stage-reflectron", "refl");
        var session = new ShellSession(model, new JournalAuthor("test", AuthorKind.Human));

        session.Project();

        // The last, not the only: opening a session journals a validate of its own, which
        // is correct and is what the model tree is built from.
        var action = session.Actions[^1];

        output.WriteLine(action.Command);

        Assert.StartsWith("einzel project ", action.Command, StringComparison.Ordinal);
        Assert.Contains(_root, action.Command, StringComparison.Ordinal);
    }
}
