namespace Einzel.Shell.Tests;

/// <summary>
/// The viewport's redraw runs one at a time and folds the requests made meanwhile into one
/// more run.
/// </summary>
/// <remarks>
/// A redraw is a field solve and a transport. Three quick edits used to start three of them
/// side by side, each holding its buffers, and the two stale pictures were thrown away only
/// after they had been paid for. Each test holds the work open with a completion source, so
/// what is checked is the order of events rather than a race.
/// </remarks>
public sealed class CoalescingTests
{
    /// <summary>Runs a test with no synchronization context, as continuations run in order.</summary>
    /// <remarks>
    /// The window's continuations run on its UI thread, one after another. The test framework
    /// installs a context of its own that may post them elsewhere instead, which would make
    /// "the owed run has started by now" a race; off any context, completing a gate runs
    /// what follows at once, so each assertion reads a settled state.
    /// </remarks>
    private static Task OffContext(Func<Task> test) => Task.Run(test);

    /// <summary>How long a wait may take before it counts as a failure rather than a hang.</summary>
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    /// <summary>Three requests while one run is in flight give one more run, never two at once.</summary>
    [Fact]
    public Task RequestsMadeWhileRunningAreFoldedIntoOneMoreRun() => OffContext(async () =>
    {
        var gates = new List<TaskCompletionSource>();
        var inFlight = 0;
        var mostAtOnce = 0;

        var redraw = new Coalescing(async () =>
        {
            mostAtOnce = Math.Max(mostAtOnce, ++inFlight);

            var gate = new TaskCompletionSource();
            gates.Add(gate);

            await gate.Task;

            inFlight--;
        });

        var first = redraw.RequestAsync();

        Assert.True(redraw.Busy);
        Assert.Single(gates);

        // Folded: each comes back already finished, and none starts work of its own.
        // Asserted rather than awaited, so a request that did start work fails here instead
        // of waiting on a gate nobody will open.
        Assert.True(redraw.RequestAsync().IsCompleted);
        Assert.True(redraw.RequestAsync().IsCompleted);
        Assert.True(redraw.RequestAsync().IsCompleted);

        Assert.Single(gates);

        gates[0].SetResult();

        // The run owed after the first, from the document as it stands by then.
        Assert.Equal(2, gates.Count);

        gates[1].SetResult();
        await first.WaitAsync(Bound);

        Assert.Equal(2, gates.Count);
        Assert.Equal(1, mostAtOnce);
        Assert.False(redraw.Busy);
    });

    /// <summary>A request after everything has finished starts a run of its own.</summary>
    [Fact]
    public Task ARequestWhenIdleRuns() => OffContext(async () =>
    {
        var runs = 0;
        var redraw = new Coalescing(() =>
        {
            runs++;
            return Task.CompletedTask;
        });

        await redraw.RequestAsync().WaitAsync(Bound);
        await redraw.RequestAsync().WaitAsync(Bound);

        Assert.Equal(2, runs);
    });

    /// <summary>
    /// A failed run that has been superseded is not reported; the failure of the last run is.
    /// </summary>
    /// <remarks>
    /// The window reports its own failures and never throws here, so this is about the class
    /// keeping its promise rather than about the window: a run owed after a failure must still
    /// be made, and the last failure must not be swallowed.
    /// </remarks>
    [Fact]
    public Task OnlyTheLastRunsFailureIsReported() => OffContext(async () =>
    {
        var gates = new List<TaskCompletionSource>();

        var redraw = new Coalescing(() =>
        {
            var gate = new TaskCompletionSource();
            gates.Add(gate);
            return gate.Task;
        });

        var first = redraw.RequestAsync();
        Assert.True(redraw.RequestAsync().IsCompleted);

        gates[0].SetException(new InvalidOperationException("superseded"));

        Assert.Equal(2, gates.Count);

        gates[1].SetException(new InvalidOperationException("last"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => first.WaitAsync(Bound));

        Assert.Equal("last", error.Message);
        Assert.False(redraw.Busy);
    });

    /// <summary>The window is told when the redraw starts and when it is done.</summary>
    /// <remarks>
    /// The watch button is enabled from this, so a missed notification would leave it
    /// enabled while a redraw computes, which is the case it exists to prevent.
    /// </remarks>
    [Fact]
    public Task BusyIsAnnouncedBothWays() => OffContext(async () =>
    {
        var gate = new TaskCompletionSource();
        var redraw = new Coalescing(() => gate.Task);
        var seen = new List<bool>();

        redraw.BusyChanged += (_, _) => seen.Add(redraw.Busy);

        var run = redraw.RequestAsync();
        gate.SetResult();
        await run.WaitAsync(Bound);

        Assert.Equal([true, false], seen);
    });
}
