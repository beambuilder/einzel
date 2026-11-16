using Einzel.Commands;
using Einzel.Shell.Presentation;

namespace Einzel.Shell.Tests;

/// <summary>
/// What the window leaves behind when something goes wrong part way: a new model that fails to
/// open is removed again, and a Stop pressed during a solve says what it will do.
/// </summary>
public sealed class ShellRecoveryTests : IDisposable
{
    private static readonly JournalAuthor Tester = new("tester", AuthorKind.Human);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"einzel-recover-{Guid.NewGuid():N}");

    /// <summary>An example with a data file and a test, so every kind of written file is covered.</summary>
    private static string ExampleWithEverything =>
        ExampleModels.Names.First(n => ExampleModels.Assets(n).Count > 0 && ExampleModels.HasTest(n));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ANewModelThatFailsToOpenIsRemovedWithItsDataAndItsTestAndMayBeRetried()
    {
        InitCommand.Execute(_root, withGit: false);
        var name = ExampleWithEverything;
        var destination = Path.Combine(_root, "models", "made.json");
        var before = Files();

        Assert.Throws<InvalidOperationException>(() => ShellSession.New<ShellSession>(
            destination, ModelSource.Example, name, Tester, _ => throw new InvalidOperationException("tree failed")));

        Assert.Equal(before, Files());

        // The obvious retry is not refused with "already exists".
        var session = ShellSession.New(destination, ModelSource.Example, name, Tester);

        Assert.True(File.Exists(destination));
        Assert.True(File.Exists(Path.Combine(_root, "tests", "made.json")));
        Assert.All(ExampleModels.Assets(name), a => Assert.True(File.Exists(Path.Combine(_root, "models", a))));
        Assert.True(session.Validate().Valid);
    }

    [Fact]
    public void AFailedOpenNeverRemovesAFileThatWasThereBefore()
    {
        InitCommand.Execute(_root, withGit: false);
        var name = ExampleWithEverything;
        var models = Path.Combine(_root, "models");
        var asset = Path.Combine(models, ExampleModels.Assets(name)[0]);
        var test = Path.Combine(_root, "tests", "made.json");
        File.WriteAllText(asset, "someone's own data");
        File.WriteAllText(test, "someone's own test");

        Assert.Throws<InvalidOperationException>(() => ShellSession.New<ShellSession>(
            Path.Combine(models, "made.json"), ModelSource.Example, name, Tester,
            _ => throw new InvalidOperationException("tree failed")));

        Assert.False(File.Exists(Path.Combine(models, "made.json")));
        Assert.Equal("someone's own test", File.ReadAllText(test));

        // The data file was rewritten by the command (as `einzel new` does), but it existed
        // before, so it is not this call's to delete.
        Assert.True(File.Exists(asset));
    }

    [Fact]
    public void ARefusalOverAnExistingModelRemovesNothing()
    {
        InitCommand.Execute(_root, withGit: false);
        var existing = Path.Combine(_root, "models", "taken.json");
        File.WriteAllText(existing, "{}");

        Assert.ThrowsAny<Exception>(() => ShellSession.New<ShellSession>(
            existing, ModelSource.Template, "einzel-lens", Tester, s => s));

        Assert.Equal("{}", File.ReadAllText(existing));
    }

    [Fact]
    public void StopSaysTheRunEndsAfterTheSolveOrStepAndKeepsSayingItUntilItDoes()
    {
        using var gate = new RunGate();
        var lease = gate.TryBegin(HeavyRun.Watch)!;

        Assert.Null(gate.StoppingStatus);
        Assert.False(gate.StopRequested);

        Assert.True(gate.Stop());

        Assert.True(gate.StopRequested);
        Assert.Contains("solve", gate.StoppingStatus, StringComparison.Ordinal);
        Assert.Contains("step in progress", gate.StoppingStatus, StringComparison.Ordinal);

        // Still true for as long as the run has not ended, however often it is asked.
        Assert.False(gate.Stop());
        Assert.NotNull(gate.StoppingStatus);

        lease.Dispose();

        Assert.False(gate.StopRequested);
        Assert.Null(gate.StoppingStatus);
    }

    private string[] Files() =>
        [.. Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(_root, f)).Order(StringComparer.Ordinal)];
}
