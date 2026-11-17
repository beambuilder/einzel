using Einzel.Commands;
using Einzel.Shell.Presentation;

using Xunit.Abstractions;

namespace Einzel.Shell.Tests;

/// <summary>
/// The panels that run the model do it in the background, and say what they ran.
/// </summary>
/// <remarks>
/// <para>
/// <b>The WPF shell ran these on its UI thread behind a wait cursor</b>, which on a volume
/// model is minutes of a window that does not answer. The cross-platform one awaits them, so
/// what is checked here is that the background path reports the same thing the foreground one
/// did, is recorded as the same command (Amendment 25), and turns a refusal into a status line
/// rather than an exception the window would have to catch.
/// </para>
/// </remarks>
public sealed class PanelTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-shell-panels", Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ShellSession Open(string example)
    {
        if (!Directory.Exists(Path.Combine(_root, "models")))
        {
            Assert.Equal(0, Einzel.Cli.Program.Main(["init", _root]));
        }

        var path = Path.Combine(_root, "models", example + ".json");

        Assert.Equal(0, Einzel.Cli.Program.Main(["new", path, "--from-example", example]));

        return new ShellSession(path, new JournalAuthor("tester", AuthorKind.Human));
    }

    /// <summary>A run in the background reports its figures and is recorded as the run.</summary>
    [Fact]
    public async Task AResultsRunInTheBackgroundIsRecordedAsTheRun()
    {
        var session = Open("single-stage-reflectron");
        var results = new ResultsViewModel(session);

        Assert.True(await results.RefreshAsync(preview: false));

        output.WriteLine(results.Status);

        var flight = Assert.Single(results.Rows.OfType<FigureRow>(), f => f.Name == "flightTime");

        Assert.True(flight.Present);
        Assert.EndsWith(" us", flight.Value, StringComparison.Ordinal);
        Assert.False(results.Tainted);
        Assert.Contains(results.Rows, r => r is FigureClassHeader);
        Assert.Contains(session.Actions, a => a.Command == $"einzel run {session.Journal.ModelPath}");
    }

    /// <summary>A preview in the background is marked as one, and recorded as one.</summary>
    /// <remarks>GRD-5: a preview number that looks like a run number is the failure the tier exists to prevent.</remarks>
    [Fact]
    public async Task APreviewInTheBackgroundIsMarkedAsOne()
    {
        var session = Open("single-stage-reflectron");
        var results = new ResultsViewModel(session);

        await results.RefreshAsync(preview: true);

        Assert.True(results.Tainted);
        Assert.Contains("PREVIEW", results.Status, StringComparison.Ordinal);
        Assert.Contains(session.Actions, a => a.Command == $"einzel preview {session.Journal.ModelPath}");
    }

    /// <summary>A model that does not validate is refused in a status line, not an exception.</summary>
    [Fact]
    public async Task ARefusalComesBackAsAStatusLine()
    {
        var session = Open("single-stage-reflectron");
        var tree = new ModelTreeViewModel(session);

        Assert.Equal(
            ParameterEdit.Applied,
            tree.Commit(tree.Parameters.Single(p => p.Name == "turningDepth"), "500"));

        var results = new ResultsViewModel(session);
        var regime = new RegimeViewModel(session);

        Assert.False(await results.RefreshAsync(preview: false));
        Assert.False(await regime.RefreshAsync());

        output.WriteLine(results.Status);

        Assert.Contains("bounds", results.Status, StringComparison.Ordinal);
        Assert.Contains("bounds", regime.Status, StringComparison.Ordinal);
        Assert.Empty(results.Rows);
        Assert.Empty(regime.Samples);
    }

    /// <summary>The regime walk in the background reads the gas along the path.</summary>
    [Fact]
    public async Task TheRegimeIsWalkedInTheBackground()
    {
        var session = Open("gas-flow-carry");
        var regime = new RegimeViewModel(session);

        Assert.True(await regime.RefreshAsync());

        output.WriteLine(regime.Status);

        Assert.NotEmpty(regime.Samples);
        Assert.Contains(session.Actions, a => a.Command == $"einzel run {session.Journal.ModelPath}");
    }

    /// <summary>A model's state is coloured by what it calls for, and "not run" by nothing.</summary>
    /// <remarks>
    /// The window colours the state word by these, so a fresh project, where every model is
    /// "not run", must read as neither a problem nor a success.
    /// </remarks>
    [Fact]
    public void NotRunIsNeitherAProblemNorASuccess()
    {
        var session = Open("single-stage-reflectron");
        var project = new ProjectViewModel(session);

        project.Refresh();

        var row = Assert.Single(project.Models, m => m.Open);

        Assert.Equal("not run", row.State);
        Assert.False(row.Bad);
        Assert.False(row.Warn);
        Assert.False(row.Good);
    }
}
