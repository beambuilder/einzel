using System.Reflection;

using Einzel.Update;

using Velopack;

namespace Einzel.Update.Tests;

/// <summary>
/// Applying an update ends every process running from the install, so it is only ever an
/// explicit restart, refused while anything else is using the installed copy (UPD-6, AGT-8).
/// </summary>
public sealed class UpdateApplyGuardTests
{
    private sealed class Feed : IUpdateFeed
    {
        public Exception? ApplyFails { get; set; }

        public string? Staged { get; set; }

        public int Checks { get; private set; }

        public int Restarts { get; private set; }

        public string CurrentVersion => "26.1.0";

        public string? StagedVersion => Staged;

        public Task<string?> FindNewerAsync(CancellationToken cancellationToken)
        {
            Checks++;

            return Task.FromResult<string?>("26.2.0");
        }

        public Task DownloadAsync(Action<int> progress, CancellationToken cancellationToken) => Task.CompletedTask;

        public void ApplyAndRestart()
        {
            if (ApplyFails is not null)
            {
                throw ApplyFails;
            }

            Restarts++;
        }
    }

    private sealed class Processes : IInstallProcesses
    {
        public List<RunningProcess> Running { get; } = [];

        public Exception? Fails { get; set; }

        public IReadOnlyList<RunningProcess> Others() => Fails is null ? [.. Running] : throw Fails;
    }

    private sealed class Store : ISkippedVersionStore
    {
        public string? Read() => null;

        public void Write(string version)
        {
        }
    }

    private static (UpdateWorkflow Workflow, Feed Feed, Processes Processes, List<string> Log) Make()
    {
        var feed = new Feed();
        var processes = new Processes();
        var log = new List<string>();

        return (new UpdateWorkflow(feed, new Store(), log.Add, processes), feed, processes, log);
    }

    [Fact]
    public async Task ARestartIsRefusedWhileAnotherEinzelProcessRunsFromTheInstallAndNamesIt()
    {
        var (w, feed, processes, log) = Make();
        processes.Running.Add(new RunningProcess("einzel", 4120));
        processes.Running.Add(new RunningProcess("einzel-mcp", 512));
        await w.CheckAtLaunchAsync();

        await w.DownloadAsync(restartWhenDone: true, runActive: () => false);

        Assert.Equal(0, feed.Restarts);
        Assert.Equal(UpdateStage.Ready, w.Stage);
        Assert.Equal(2, w.BlockingProcesses.Count);
        Assert.Contains("einzel (PID 4120)", w.Message, StringComparison.Ordinal);
        Assert.Contains("einzel-mcp (PID 512)", w.Message, StringComparison.Ordinal);
        Assert.Contains("once they have exited", w.Message, StringComparison.Ordinal);
        Assert.Contains(log, l => l.Contains("PID 4120", StringComparison.Ordinal));

        Assert.False(w.RestartNow(() => false));
        Assert.Equal(0, feed.Restarts);

        // They exit; the same restart now applies.
        processes.Running.Clear();
        Assert.True(w.RestartNow(() => false));
        Assert.Equal(1, feed.Restarts);
        Assert.Empty(w.BlockingProcesses);
    }

    [Fact]
    public async Task NotKnowingWhatIsRunningIsAReasonNotToApply()
    {
        var (w, feed, processes, _) = Make();
        processes.Fails = new InvalidOperationException("access denied");
        await w.CheckAtLaunchAsync();

        await w.DownloadAsync(restartWhenDone: true, runActive: () => false);

        Assert.Equal(0, feed.Restarts);
        Assert.Equal(UpdateStage.ApplyFailed, w.Stage);
        Assert.Contains("access denied", w.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailureToApplyIsShownNotThrownAndMayBeRetried()
    {
        var (w, feed, _, log) = Make();
        feed.ApplyFails = new IOException("Update.exe not found");
        await w.CheckAtLaunchAsync();

        await w.DownloadAsync(restartWhenDone: true, runActive: () => false);

        Assert.Equal(UpdateStage.ApplyFailed, w.Stage);
        Assert.True(w.IsVisible);
        Assert.True(w.CanRestart);
        Assert.Contains("could not be applied: Update.exe not found", w.Message, StringComparison.Ordinal);
        Assert.Contains("installed version is unchanged", w.Message, StringComparison.Ordinal);
        Assert.Contains(log, l => l.Contains("Update.exe not found", StringComparison.Ordinal));

        feed.ApplyFails = null;
        Assert.True(w.RestartNow(() => false));
        Assert.Equal(1, feed.Restarts);
    }

    [Fact]
    public async Task AReleaseStagedByAnEarlierLaunchIsOfferedForRestartNotApplied()
    {
        var (w, feed, _, _) = Make();
        feed.Staged = "26.2.0";

        await w.CheckAtLaunchAsync();

        Assert.Equal(UpdateStage.Ready, w.Stage);
        Assert.Equal("26.2.0", w.AvailableVersion);
        Assert.Equal(0, feed.Restarts);
        Assert.Equal(0, feed.Checks);
        Assert.Contains("nothing is applied until you do", w.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Velopack applies a staged release inside <c>Run()</c> at launch unless told not to, before
    /// any of Einzel's code can ask what that would end. Read off the builder, since there is no
    /// public getter: if Velopack renames the field this fails loudly rather than passing.
    /// </summary>
    [Fact]
    public void TheShellTurnsOffVelopacksApplyOnStartup()
    {
        var field = typeof(VelopackApp).GetField("_autoApply", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        Assert.True((bool)field.GetValue(VelopackApp.Build())!, "Velopack's default was expected to be on");
        Assert.False((bool)field.GetValue(InstallHooks.Configure(VelopackApp.Build()))!);
    }

    [Fact]
    public void OnlyAProcessUnderTheInstallRootCounts()
    {
        var root = Path.Combine(Path.GetTempPath(), "MacCossLab.Einzel");
        var finder = new InstallProcesses(root);

        Assert.True(finder.IsUnder(Path.Combine(root, "current", "einzel.exe")));
        Assert.True(finder.IsUnder(Path.Combine(root.ToUpperInvariant(), "current", "einzel-mcp.exe")));
        Assert.False(finder.IsUnder(Path.Combine(root + "-portable", "einzel.exe")));
        Assert.False(finder.IsUnder(Path.Combine(Path.GetTempPath(), "elsewhere", "einzel.exe")));
    }

    [Fact]
    public void TheRunningTestHostIsNotAnotherEinzelProcess()
    {
        var here = Path.GetDirectoryName(Environment.ProcessPath)!;

        Assert.DoesNotContain(new InstallProcesses(here).Others(), p => p.Id == Environment.ProcessId);
    }
}
