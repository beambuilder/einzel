using Einzel.Update;

namespace Einzel.Update.Tests;

/// <summary>
/// The launch-time update check: once, offered, never imposed, never during a run (section 18).
/// </summary>
public sealed class UpdateWorkflowTests
{
    private sealed class FakeFeed : IUpdateFeed
    {
        public string? Newer { get; set; } = "26.2.0";

        public Exception? CheckFails { get; set; }

        public Exception? DownloadFails { get; set; }

        public int Checks { get; private set; }

        public int Downloads { get; private set; }

        public int Restarts { get; private set; }

        public string CurrentVersion => "26.1.0";

        public Task<string?> FindNewerAsync(CancellationToken cancellationToken)
        {
            Checks++;

            return CheckFails is null ? Task.FromResult(Newer) : Task.FromException<string?>(CheckFails);
        }

        public Task DownloadAsync(Action<int> progress, CancellationToken cancellationToken)
        {
            Downloads++;

            if (DownloadFails is not null)
            {
                return Task.FromException(DownloadFails);
            }

            progress(50);
            progress(100);

            return Task.CompletedTask;
        }

        public void ApplyAndRestart() => Restarts++;
    }

    private sealed class MemoryStore : ISkippedVersionStore
    {
        public string? Value { get; set; }

        public string? Read() => Value;

        public void Write(string version) => Value = version;
    }

    private static (UpdateWorkflow Workflow, FakeFeed Feed, MemoryStore Store, List<string> Log) Make(bool installed = true)
    {
        var feed = new FakeFeed();
        var store = new MemoryStore();
        var log = new List<string>();

        return (new UpdateWorkflow(installed ? feed : null, store, log.Add), feed, store, log);
    }

    [Fact]
    public async Task ACopyVelopackDoesNotManageNeverChecks()
    {
        var (w, feed, _, log) = Make(installed: false);

        await w.CheckAtLaunchAsync();

        Assert.Equal(UpdateStage.NotManaged, w.Stage);
        Assert.Equal(0, feed.Checks);
        Assert.False(w.IsVisible);
        Assert.Empty(log);
    }

    [Fact]
    public async Task TheFeedIsAskedOncePerProcessHoweverOftenTheCheckIsCalled()
    {
        var (w, feed, _, _) = Make();

        await w.CheckAtLaunchAsync();
        await w.CheckAtLaunchAsync();
        await Task.WhenAll(w.CheckAtLaunchAsync(), w.CheckAtLaunchAsync());

        Assert.Equal(1, feed.Checks);
    }

    [Fact]
    public async Task ANewerReleaseIsOfferedAndNothingIsDownloaded()
    {
        var (w, feed, _, _) = Make();

        await w.CheckAtLaunchAsync();

        Assert.Equal(UpdateStage.Available, w.Stage);
        Assert.True(w.IsVisible);
        Assert.Equal("26.2.0", w.AvailableVersion);
        Assert.Contains("26.2.0", w.Message, StringComparison.Ordinal);
        Assert.Contains("26.1.0", w.Message, StringComparison.Ordinal);
        Assert.Equal(0, feed.Downloads);
        Assert.Equal(0, feed.Restarts);
    }

    [Fact]
    public async Task UpToDateShowsNothing()
    {
        var (w, feed, _, _) = Make();
        feed.Newer = null;

        await w.CheckAtLaunchAsync();

        Assert.Equal(UpdateStage.UpToDate, w.Stage);
        Assert.False(w.IsVisible);
    }

    [Fact]
    public async Task AFailedCheckIsLoggedAndNotShown()
    {
        var (w, feed, _, log) = Make();
        feed.CheckFails = new HttpRequestException("no route to host");

        await w.CheckAtLaunchAsync();

        Assert.Equal(UpdateStage.CheckFailed, w.Stage);
        Assert.False(w.IsVisible);
        Assert.Contains(log, l => l.Contains("no route to host", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DownloadAndRestartRestartsWhenNoRunIsActive()
    {
        var (w, feed, _, _) = Make();
        await w.CheckAtLaunchAsync();

        await w.DownloadAsync(restartWhenDone: true, runActive: () => false);

        Assert.Equal(1, feed.Downloads);
        Assert.Equal(1, feed.Restarts);
        Assert.Equal(100, w.Percent);
    }

    [Fact]
    public async Task DownloadOnlyStagesAndDoesNotRestart()
    {
        var (w, feed, _, _) = Make();
        await w.CheckAtLaunchAsync();

        await w.DownloadAsync(restartWhenDone: false, runActive: () => false);

        Assert.Equal(UpdateStage.Ready, w.Stage);
        Assert.Equal(0, feed.Restarts);
        Assert.Contains("nothing is applied until you do", w.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARestartIsRefusedWhileARunIsActiveAndTheDownloadIsKept()
    {
        var (w, feed, _, _) = Make();
        await w.CheckAtLaunchAsync();

        await w.DownloadAsync(restartWhenDone: true, runActive: () => true);

        Assert.Equal(UpdateStage.Ready, w.Stage);
        Assert.True(w.RestartDeferred);
        Assert.Equal(0, feed.Restarts);
        Assert.Contains("run is in progress", w.Message, StringComparison.Ordinal);

        Assert.False(w.RestartNow(() => true));
        Assert.True(w.RestartNow(() => false));
        Assert.Equal(1, feed.Restarts);
    }

    [Fact]
    public async Task NothingCanBeDownloadedOrRestartedBeforeAnOffer()
    {
        var (w, feed, _, _) = Make();

        await w.DownloadAsync(true, () => false);

        Assert.False(w.RestartNow(() => false));
        Assert.Equal(0, feed.Downloads);
        Assert.Equal(0, feed.Restarts);
    }

    [Fact]
    public async Task AFailedDownloadIsShownBecauseThePersonAskedAndMayBeRetried()
    {
        var (w, feed, _, log) = Make();
        await w.CheckAtLaunchAsync();
        feed.DownloadFails = new IOException("disk full");

        await w.DownloadAsync(true, () => false);

        Assert.Equal(UpdateStage.DownloadFailed, w.Stage);
        Assert.True(w.IsVisible);
        Assert.Contains("disk full", w.Message, StringComparison.Ordinal);
        Assert.Single(log);
        Assert.Equal(0, feed.Restarts);

        feed.DownloadFails = null;
        await w.DownloadAsync(false, () => false);

        Assert.Equal(UpdateStage.Ready, w.Stage);
    }

    [Fact]
    public async Task ASkippedVersionIsNotOfferedAgainButALaterOneIs()
    {
        var (w, _, store, _) = Make();
        await w.CheckAtLaunchAsync();

        w.Skip();

        Assert.Equal("26.2.0", store.Value);
        Assert.False(w.IsVisible);

        // The next launch: same release, then a later one.
        var again = new FakeFeed();
        var next = new UpdateWorkflow(again, store, _ => { });
        await next.CheckAtLaunchAsync();
        Assert.Equal(UpdateStage.Skipped, next.Stage);
        Assert.False(next.IsVisible);

        var later = new UpdateWorkflow(new FakeFeed { Newer = "26.3.0" }, store, _ => { });
        await later.CheckAtLaunchAsync();
        Assert.Equal(UpdateStage.Available, later.Stage);
    }

    [Fact]
    public async Task NotNowHidesTheNoticeAndDoesNotRemember()
    {
        var (w, _, store, _) = Make();
        await w.CheckAtLaunchAsync();

        w.Dismiss();

        Assert.Equal(UpdateStage.Dismissed, w.Stage);
        Assert.False(w.IsVisible);
        Assert.Null(store.Value);
    }
}
