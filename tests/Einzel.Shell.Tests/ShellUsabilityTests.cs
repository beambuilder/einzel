using Einzel.Commands;
using Einzel.Core.Errors;
using Einzel.Shell.Presentation;

using Xunit.Abstractions;

namespace Einzel.Shell.Tests;

/// <summary>
/// What somebody who did not write the shell needs from it: to stop a run, to open a model
/// from the window, and to start one from the catalog.
/// </summary>
/// <remarks>
/// All three are decided in <c>Presentation</c> - the session and the run gate - so they are
/// checked here without a window. What the window adds is a button that calls each.
/// </remarks>
public sealed class ShellUsabilityTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"einzel-usable-{Guid.NewGuid():N}");

    private static readonly JournalAuthor Tester = new("tester", AuthorKind.Human);

    /// <summary>The drift tube the watched-run tests use: small, and genuinely diffusive.</summary>
    private const string DriftTube = """
    {
      "schemaVersion": "0.4",
      "name": "stoppable-drift-tube",
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

    /// <summary>Stop ends a running watch, and the session can be used again afterwards.</summary>
    /// <remarks>
    /// <para>
    /// <b>Stopped from inside the run, after its first frame</b>, which is the case the window
    /// meets: the token reaching only the start of the task would let a watch be stopped in
    /// the microsecond before it began and never after. The watch must end in a cancellation
    /// rather than run to its end, and must have stopped short of the frames a whole run
    /// delivers.
    /// </para>
    /// <para>
    /// Then the session is used again - validated, and watched to the end - because a stop
    /// that left the journal or the gate wedged would be a Stop button that works once.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task StoppingAWatchEndsItAndLeavesTheSessionUsable()
    {
        var model = Write("drift.json", DriftTube);
        var session = new ShellSession(model, Tester);
        var gate = new RunGate();

        // The whole run first, for how many frames it delivers at this cadence.
        var whole = 0;
        _ = await session.WatchAsync(
            new Watcher(_ => Interlocked.Increment(ref whole), TimeSpan.Zero));

        var lease = gate.TryBegin(HeavyRun.Watch)!;
        Assert.True(gate.CanStop);

        var stopped = 0;
        var watcher = new Watcher(
            _ =>
            {
                if (Interlocked.Increment(ref stopped) == 1)
                {
                    Assert.True(gate.Stop());
                }
            },
            TimeSpan.Zero);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => session.WatchAsync(watcher, lease.Stopping));

        output.WriteLine($"whole run {whole} frames; stopped run {stopped}");

        Assert.False(gate.CanStop, "a stopped watch still offers Stop");
        Assert.True(stopped < whole, $"the stopped run delivered {stopped} of {whole} frames");

        lease.Dispose();
        Assert.False(gate.Busy);

        Assert.True(session.Validate().Valid);

        var again = gate.TryBegin(HeavyRun.Watch);
        Assert.NotNull(again);

        var frames = 0;
        _ = await session.WatchAsync(
            new Watcher(_ => Interlocked.Increment(ref frames), TimeSpan.Zero), again!.Stopping);
        again.Dispose();

        Assert.Equal(whole, frames);
    }

    /// <summary>While one long run holds the gate, no other is admitted, whichever it is.</summary>
    /// <remarks>
    /// The window enables Watch, Run, Preview and Inspect on <see cref="RunGate.Busy"/>; a
    /// review found each enabled on its own flag alone, so a watch could start under a full
    /// run and a full run under a watch, each holding a whole solve.
    /// </remarks>
    [Fact]
    public void ASecondLongRunIsRefusedWhileOneIsInFlight()
    {
        var gate = new RunGate();

        foreach (var first in Enum.GetValues<HeavyRun>())
        {
            using (var lease = gate.TryBegin(first))
            {
                Assert.NotNull(lease);
                Assert.Equal(first, gate.Running);

                foreach (var second in Enum.GetValues<HeavyRun>())
                {
                    Assert.Null(gate.TryBegin(second));
                }

                // Only a watch can be stopped: the others go through command objects that
                // take no cancellation, and a Stop that appeared to work would be worse.
                Assert.Equal(first == HeavyRun.Watch, gate.CanStop);
                Assert.Equal(first == HeavyRun.Watch, gate.Stop());
            }

            Assert.False(gate.Busy);
        }

        // A closed window admits nothing more, and stops what it can on the way out.
        var watch = gate.TryBegin(HeavyRun.Watch)!;
        gate.Dispose();
        Assert.True(watch.Stopping.IsCancellationRequested);
        watch.Dispose();
        Assert.Null(gate.TryBegin(HeavyRun.Results));
    }

    /// <summary>
    /// A model started from a template is written by <c>einzel new</c>'s command object,
    /// validates, and is journalled as that command line.
    /// </summary>
    [Fact]
    public void ANewModelFromATemplateValidatesAndIsJournalledAsEinzelNew()
    {
        var catalog = new ModelCatalog();
        Assert.Contains(catalog.Templates, e => e.Name == "einzel-lens");

        var destination = Path.Combine(_directory, "models", ModelCatalog.SuggestedFileName("einzel-lens"));

        var session = ShellSession.New(destination, ModelSource.Template, "einzel-lens", Tester);

        output.WriteLine(string.Join(Environment.NewLine, session.Actions.Select(a => a.Command)));

        Assert.True(File.Exists(destination));
        Assert.Equal(CatalogCommand.Read("template", "einzel-lens"), File.ReadAllText(destination));
        Assert.True(session.Validate().Valid);

        var path = Path.GetFullPath(destination);

        Assert.Equal($"einzel new {path} --from-template einzel-lens", session.Actions[0].Command);
        Assert.Equal($"einzel validate {path}", session.Actions[1].Command);
    }

    /// <summary>An example is spelled <c>--from-example</c>, and validates too.</summary>
    [Fact]
    public void ANewModelFromAnExampleIsJournalledWithItsOwnFlag()
    {
        var catalog = new ModelCatalog();
        Assert.Contains(catalog.Examples, e => e.Name == "free-flight");

        var destination = Path.Combine(_directory, "free flight.json");

        var session = ShellSession.New(destination, ModelSource.Example, "free-flight", Tester);

        Assert.True(session.Validate().Valid);

        // Quoted, because the path has a space and the line is meant to be run.
        Assert.Equal(
            $"einzel new \"{Path.GetFullPath(destination)}\" --from-example free-flight",
            session.Actions[0].Command);
    }

    /// <summary>The window cannot overwrite a model, because the command it calls will not.</summary>
    [Fact]
    public void ANewModelOverAnExistingFileIsRefusedAndTheFileIsKept()
    {
        var existing = Write("taken.json", DriftTube);

        Assert.Throws<EinzelException>(
            () => ShellSession.New(existing, ModelSource.Template, "einzel-lens", Tester));

        Assert.Equal(DriftTube, File.ReadAllText(existing));
    }

    /// <summary>Opening a model is recorded as the command that reads and checks it.</summary>
    [Fact]
    public void OpeningAModelIsRecordedAsValidate()
    {
        var model = Write("opened.json", DriftTube);

        var session = new ShellSession(model, Tester);

        Assert.Equal($"einzel validate {Path.GetFullPath(model)}", Assert.Single(session.Actions).Command);
    }

    private string Write(string name, string text)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
