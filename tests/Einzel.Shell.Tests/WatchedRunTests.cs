using System.Globalization;

using Einzel.Commands;
using Einzel.Core.Errors;
using Einzel.Shell;
using Einzel.Shell.Presentation;

using Xunit.Abstractions;

namespace Einzel.Shell.Tests;

/// <summary>
/// The live path end to end: a run that is still going hands the viewport frames, and the
/// frames move.
/// </summary>
/// <remarks>
/// <para>
/// This is the claim the window exists for - "run the analysis and see it happen" - and it
/// is checked without a window, so it holds on every platform the engine runs on. What a
/// GUI adds on top is a control that calls this and a control that draws the result.
/// </para>
/// <para>
/// The equivalent for the WPF shell targeted net10.0-windows, so a solution-wide test run
/// walked past it on Linux and the claim was checked on one platform; that project has
/// since been deleted.
/// </para>
/// </remarks>
public sealed class WatchedRunTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _model = Path.Combine(
        Path.GetTempPath(), $"einzel-watch-{Guid.NewGuid():N}.json");

    /// <summary>A packet drifting down a tube in a gas: small, and genuinely diffusive.</summary>
    /// <remarks>
    /// A coarse grid on purpose. What is being checked is that frames arrive and change,
    /// which needs the transport to run and does not need it to be converged - and a test
    /// that took a minute would be a test nobody runs.
    /// </remarks>
    private const string Model = """
    {
      "schemaVersion": "0.4",
      "name": "watched-drift-tube",
      "ion": { "massToCharge": { "value": 500.0, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [2, 0, 0], "unit": "mm" },
        "direction": { "value": [1, 0, 0] },
        "accelerationPotential": { "value": 0.001, "unit": "V" },
        "cloud": {
          "ions": 1, "population": 10000,
          "transverseSpread": { "value": 1.0, "unit": "mm" },
          "longitudinalSpread": { "value": 1.0, "unit": "mm" }
        }
      },
      "fields": [{ "type": "uniform", "field": { "value": [2000.0, 0, 0], "unit": "V/m" } }],
      "detector": {
        "planePoint": { "value": [40.0, 0.0, 0], "unit": "mm" },
        "normal": { "value": [-1, 0, 0] }
      },
      "transport": {
        "mode": "diffusion",
        "maximumFlightTime": { "value": 400, "unit": "us" },
        "mobility": { "zeroField": { "value": 0.09, "unit": "m^2/(V s)" } },
        "densityGrid": {
          "minX": { "value": -2, "unit": "mm" }, "maxX": { "value": 40, "unit": "mm" },
          "minY": { "value": -6, "unit": "mm" }, "maxY": { "value": 6, "unit": "mm" },
          "intervalsX": 64, "intervalsY": 16
        },
        "gas": {
          "model": "hardSphere",
          "pressure": { "value": 1, "unit": "mbar" },
          "mass": { "value": 28.0134, "unit": "Da" },
          "crossSection": { "value": 250, "unit": "angstrom^2" }
        }
      }
    }
    """;

    /// <summary>The viewport is filled repeatedly, and the packet is somewhere new each time.</summary>
    /// <remarks>
    /// <b>Two assertions, and the second is what makes the first mean anything.</b> That
    /// frames arrive says the callback is wired; that the packet's center MOVES says they
    /// are frames of a run rather than the same instant handed over repeatedly, which is
    /// what a watch that had accidentally captured its first frame would look like.
    /// </remarks>
    [Fact]
    public void AWatchedRunDeliversFramesAndThePacketMoves()
    {
        File.WriteAllText(_model, Model);

        var centers = new List<double>();

        var watcher = new Watcher(
            frame =>
            {
                if (frame.Density.Count > 0)
                {
                    // The leading edge of the deepest contour stands in for where the
                    // packet is: the outcome carries shells rather than a centroid.
                    centers.Add(frame.Density.Max(d => Reach(d.VerticesMm)));
                }
            },
            TimeSpan.FromMilliseconds(20));

        var final = ViewportCommand.Watch(_model, watcher);

        output.WriteLine($"{centers.Count} frames with a packet; "
            + $"leading edge {string.Join(" -> ", centers.Take(6).Select(c => c.ToString("F2", CultureInfo.InvariantCulture)))}");
        output.WriteLine($"final frame has {final.Density.Count} contours");

        Assert.True(centers.Count >= 2, $"only {centers.Count} frames carried a packet");

        // It drifts, so the leading edge advances. Asserted as a real distance rather than
        // as "not equal", because two frames differing in the last bit would pass that.
        Assert.True(
            centers[^1] - centers[0] > 1.0,
            $"the packet's leading edge moved {centers[^1] - centers[0]:F3} mm across the run");
    }

    /// <summary>A watch started through the session is the command that would repeat it.</summary>
    /// <remarks>
    /// <para>
    /// <b>The window watches through the session, and that is the claim.</b> Its first
    /// version called <see cref="ViewportCommand.Watch"/> itself: the same physics, and no
    /// record that anything had run - the one action in the window that could not be read
    /// back as something to type (Amendment 25).
    /// </para>
    /// <para>
    /// The frames are counted as well as the command, because a session that recorded the
    /// command and never ran the transport would pass the first assertion alone.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AWatchThroughTheSessionIsRecordedAsTheCommandThatRepeatsIt()
    {
        File.WriteAllText(_model, Model);

        var session = new ShellSession(_model, new JournalAuthor("tester", AuthorKind.Human));
        var frames = 0;

        var watcher = new Watcher(_ => Interlocked.Increment(ref frames), TimeSpan.FromMilliseconds(20));

        _ = await session.WatchAsync(watcher);

        output.WriteLine(string.Join(Environment.NewLine, session.Actions.Select(a => a.Command)));

        Assert.Contains(
            session.Actions,
            a => a.Command == $"einzel run {session.Journal.ModelPath} --progress 5");
        Assert.True(frames > 0, "the session recorded a watch and no frame arrived");
        Assert.NotNull(watcher.LastWithPacket);
    }

    /// <summary>Stopping a watch keeps what it drew, and actually stops it.</summary>
    /// <remarks>
    /// <para>
    /// <b>A stopped watch is not a failed one.</b> The packet it drew is where the run had
    /// got to, which is a real state of the instrument and the reason somebody pressed stop
    /// - discarding it would throw away the only thing the watch produced.
    /// </para>
    /// <para>
    /// <b>The step count is what makes this a test of stopping rather than of cancelling.</b>
    /// A token passed only to <c>Task.Run</c> decides whether the work ever starts and is
    /// never looked at again once it has, so a watch cancelled a step in would run to
    /// completion and pass any assertion about the exception alone. What discriminates is
    /// that far fewer frames arrive than the same run delivers unstopped - the token has to
    /// reach the listener the run asks at every step.
    /// </para>
    /// <para>
    /// This is the one claim lost when <c>Einzel.Wpf.Tests</c> was deleted: its
    /// <c>StoppingAWatchKeepsWhatItDrew</c> had no equivalent here, and the cross-platform
    /// window had no Stop control at all.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task StoppingAWatchKeepsWhatItDrewAndTookFewerSteps()
    {
        File.WriteAllText(_model, Model);

        var session = new ShellSession(_model, new JournalAuthor("tester", AuthorKind.Human));

        // The control: the same run, unstopped, so "few frames" is measured against what
        // this model actually delivers rather than against a number chosen to pass.
        var whole = 0;

        _ = await session.WatchAsync(
            new Watcher(_ => Interlocked.Increment(ref whole), TimeSpan.Zero));

        using var stopping = new CancellationTokenSource();

        var stopped = 0;
        ViewportOutcome? kept = null;

        var watcher = new Watcher(
            frame =>
            {
                Interlocked.Increment(ref stopped);

                if (frame.Density.Count > 0)
                {
                    kept = frame;

                    // Stop at the first frame that actually drew something, which is the
                    // moment a person would: there is now a picture worth keeping.
                    stopping.Cancel();
                }
            },
            TimeSpan.Zero);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => session.WatchAsync(watcher, stopping.Token));

        output.WriteLine($"{stopped} frames stopped against {whole} whole");

        Assert.NotNull(kept);
        Assert.NotEmpty(kept!.Density);

        // What the window shows afterwards, and the reason the viewport is not cleared.
        Assert.NotNull(watcher.LastWithPacket);
        Assert.NotEmpty(watcher.LastWithPacket!.Density);

        // THE ASSERTION. A token reaching only Task.Run gives stopped == whole.
        Assert.True(
            stopped < whole,
            $"the run was not stopped: {stopped} frames against {whole} unstopped");
    }

    /// <summary>A trajectory model is refused, with a reason.</summary>
    /// <remarks>
    /// <b>The refusal is the design.</b> A flight finishes faster than a viewport could draw
    /// it part way through, so the whole bundle arriving at once is sooner than the first
    /// frame of a watch would be - and a button that appeared to do nothing would be worse
    /// than one that says why.
    /// </remarks>
    [Fact]
    public void AFlightIsRefusedRatherThanWatched()
    {
        var flown = Model.Replace(
            "\"mode\": \"diffusion\"", "\"mode\": \"trajectory\"", StringComparison.Ordinal);

        // The edit is asserted to have happened, which this project has had to learn twice:
        // a replacement matching nothing leaves the model diffusive, the refusal never comes,
        // and the failure names the missing exception rather than the stale edit. Asserting
        // the document CHANGED rather than that it now contains something, because the second
        // is true whenever the text was already there - `docs/lessons.md`.
        Assert.NotEqual(Model, flown);

        File.WriteAllText(_model, flown);

        // EinzelException by name rather than ThrowsAny: the refusal is part of
        // ViewportCommand.Watch's declared contract, and a broad catch would also pass on an
        // IO failure or a parse error whose message happened to mention a density.
        var error = Assert.Throws<EinzelException>(
            () => ViewportCommand.Watch(_model, new Watcher(_ => { }, TimeSpan.Zero)));

        output.WriteLine(error.Message);

        Assert.Contains("density", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (File.Exists(_model))
        {
            File.Delete(_model);
        }
    }

    private static double Reach(IReadOnlyList<double> verticesMm)
    {
        var far = double.MinValue;

        for (var i = 0; i + 2 < verticesMm.Count; i += 3)
        {
            far = Math.Max(far, verticesMm[i]);
        }

        return far == double.MinValue ? 0.0 : far;
    }
}
