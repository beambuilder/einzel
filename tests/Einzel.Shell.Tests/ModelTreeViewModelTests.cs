using System.Globalization;

using Einzel.Commands;
using Einzel.Shell.Presentation;

using Xunit.Abstractions;

namespace Einzel.Shell.Tests;

/// <summary>
/// The model tree as a person meets it at the keyboard: what typing does, what it refuses,
/// and what reaches the journal (§16, MCP-1, Amendment 25).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every rule here is one a screenshot cannot check.</b> A box left as it was must write
/// nothing to the journal, or tabbing through the tree records an edit per row; a comma must
/// be refused, or a German keyboard turns 4,5 kV into 45; an edit must be attributed to the
/// person rather than to nobody, or an agent on the same session cannot tell whose change it
/// is undoing. Each fails silently in a window and loudly here.
/// </para>
/// <para>
/// On the single-stage reflectron because it is the smallest model with both kinds of row:
/// three free parameters with bounds, and a gradient derived from two of them.
/// </para>
/// </remarks>
public sealed class ModelTreeViewModelTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-shell-tree", Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private (ShellSession Session, ModelTreeViewModel Tree) Open()
    {
        Assert.Equal(0, Einzel.Cli.Program.Main(["init", _root]));

        var path = Path.Combine(_root, "models", "mirror.json");

        Assert.Equal(
            0,
            Einzel.Cli.Program.Main(["new", path, "--from-example", "single-stage-reflectron"]));

        var session = new ShellSession(path, new JournalAuthor("tester", AuthorKind.Human));

        return (session, new ModelTreeViewModel(session));
    }

    private static ParameterRow Row(ModelTreeViewModel tree, string name) =>
        tree.Parameters.Single(p => p.Name == name);

    /// <summary>An edit reaches the file through the journal, under the person's name.</summary>
    /// <remarks>
    /// <b>Three assertions about one edit, because each catches a different shortcut.</b> The
    /// file changed says it was applied; the journal entry carrying the person's name says it
    /// went through the journal rather than straight to disk, which is what lets an agent on
    /// the same session undo it; and the derived gradient moving says the tree was re-read
    /// rather than patched, which is the reason a model has a parameter surface at all.
    /// </remarks>
    [Fact]
    public void AnEditGoesThroughTheJournalUnderThePersonsName()
    {
        var (session, tree) = Open();

        var before = Row(tree, "gradient").Detail;

        Assert.Equal(ParameterEdit.Applied, tree.Commit(Row(tree, "turningDepth"), "40"));

        output.WriteLine($"gradient: {before}  ->  {Row(tree, "gradient").Detail}");

        Assert.Equal("40", Row(tree, "turningDepth").Shown);
        Assert.Contains("\"value\": 40", File.ReadAllText(session.Journal.ModelPath), StringComparison.Ordinal);

        var entry = Assert.Single(session.Journal.Entries);
        Assert.Equal("tester", entry.Author.Name);
        Assert.Equal(AuthorKind.Human, entry.Author.Kind);
        Assert.Single(tree.Journal);

        // 4 kV over 40 mm, where it was over 50: the consequence moved with its cause.
        Assert.NotEqual(before, Row(tree, "gradient").Detail);
        Assert.Contains("100000", Row(tree, "gradient").Detail, StringComparison.Ordinal);

        // Amendment 25: the edit is listed as the command a person could type.
        Assert.Contains(tree.Commands, c => c.StartsWith("einzel validate ", StringComparison.Ordinal));
    }

    /// <summary>A box left as it was records nothing.</summary>
    /// <remarks>
    /// The window commits when a box loses focus, so this is what tabbing through the tree
    /// does to every row it passes. Surrounding spaces are what a careless paste leaves.
    /// </remarks>
    [Fact]
    public void ABoxLeftAsItWasRecordsNothing()
    {
        var (session, tree) = Open();

        var row = Row(tree, "capPotential");

        Assert.Equal(ParameterEdit.Unchanged, tree.Commit(row, row.Shown));
        Assert.Equal(ParameterEdit.Unchanged, tree.Commit(row, "  " + row.Shown + " "));

        Assert.Empty(session.Journal.Entries);
        Assert.False(tree.CanUndo);
    }

    /// <summary>
    /// Under a locale that writes a decimal comma, a comma is refused and a point is read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Under that locale, because nowhere else does it test anything.</b> The CLI runs
    /// globalization-invariant and this test project does not, for the same reason the shell
    /// does not: a window cannot run in that mode. So the shell is the one process where the
    /// host's locale reaches number formatting, and the check has to be made there.
    /// </para>
    /// <para>
    /// The bounds line is the half that found something: <c>OutlineCommand.BoundsText</c>
    /// formatted with the current culture, so a German desktop printed "0,1 to 20 kV" beside
    /// a box that parses "0.1" and refuses "0,1".
    /// </para>
    /// </remarks>
    [Fact]
    public void UnderADecimalCommaLocaleACommaIsRefusedAndThePointIsRead()
    {
        var (session, tree) = Open();

        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            Assert.Equal(",", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);

            Assert.Equal(ParameterEdit.Refused, tree.Commit(Row(tree, "capPotential"), "4,5"));

            output.WriteLine(tree.Status);

            Assert.Contains("not a number", tree.Status, StringComparison.Ordinal);
            Assert.Empty(session.Journal.Entries);

            Assert.Equal(ParameterEdit.Applied, tree.Commit(Row(tree, "capPotential"), "4.5"));
            Assert.Equal("4.5", Row(tree, "capPotential").Shown);

            tree.Refresh();

            output.WriteLine(Row(tree, "capPotential").Bounds);

            Assert.Equal("0.1 to 20 kV", Row(tree, "capPotential").Bounds);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>A derived parameter is refused, naming what it is derived from.</summary>
    [Fact]
    public void ADerivedParameterIsRefusedByName()
    {
        var (session, tree) = Open();

        var gradient = Row(tree, "gradient");

        Assert.False(gradient.Editable);
        Assert.Equal(ParameterEdit.Refused, tree.Commit(gradient, "5"));

        output.WriteLine(tree.Status);

        Assert.Contains("derived", tree.Status, StringComparison.Ordinal);
        Assert.Contains("capPotential / turningDepth", tree.Status, StringComparison.Ordinal);
        Assert.Empty(session.Journal.Entries);
    }

    /// <summary>
    /// A value out of its bounds applies, the tree stands with the complaint against it, and
    /// undoing it puts both back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Taint, never block, applied to input.</b> A tree that vanished when a value went
    /// invalid would disappear exactly when it is most needed, and refusing every invalid
    /// document forbids any sequence of edits that passes through one.
    /// </para>
    /// <para>
    /// The undo is the person's, by name, and is appended rather than popped: MCP-1 asks that
    /// somebody undoing something be on the record too.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnOutOfBoundsValueAppliesAndUndoPutsItBack()
    {
        var (session, tree) = Open();

        Assert.False(tree.CanUndo);

        Assert.Equal(ParameterEdit.Applied, tree.Commit(Row(tree, "turningDepth"), "500"));

        output.WriteLine(tree.Status);

        Assert.False(tree.Valid);
        Assert.Equal(4, tree.Parameters.Count);
        Assert.Equal("500", Row(tree, "turningDepth").Shown);
        Assert.Contains("turningDepth", tree.Status, StringComparison.Ordinal);
        Assert.True(tree.CanUndo);

        Assert.True(tree.Undo());

        Assert.True(tree.Valid);
        Assert.Equal("50", Row(tree, "turningDepth").Shown);
        Assert.False(tree.CanUndo);

        Assert.Equal(2, session.Journal.Entries.Count);
        Assert.True(session.Journal.Entries[^1].IsUndo);
        Assert.Equal("tester", session.Journal.Entries[^1].Author.Name);
        Assert.Contains("einzel undo", tree.Commands);
    }

    /// <summary>A screen reader hears a box's unit and bounds, not only its number.</summary>
    /// <remarks>
    /// The window names each box by its parameter and gives this as its help text, because the
    /// label beside the box is not associated with it. A value read out as "50" with no unit
    /// is the ambiguity §9 refuses in a document.
    /// </remarks>
    [Fact]
    public void EachBoxSaysItsUnitAndBoundsAloud()
    {
        var (_, tree) = Open();

        Assert.Equal("in mm, 5 to 200 mm", Row(tree, "turningDepth").Help);
        Assert.Equal("in kV, 0.1 to 20 kV", Row(tree, "capPotential").Help);
    }

    /// <summary>A redraw is listed as a command without rebuilding the rows.</summary>
    /// <remarks>
    /// The window refreshes the history whenever an action starts, and a redraw finishes
    /// while somebody may be typing in another row. Rebuilding the rows then would take the
    /// keyboard out of their box, so the rows must be the same objects afterwards.
    /// </remarks>
    [Fact]
    public void RefreshingTheHistoryLeavesTheRowsAlone()
    {
        var (session, tree) = Open();

        var rows = tree.Parameters.ToList();

        _ = session.Viewport();
        tree.RefreshHistory();

        Assert.Equal(rows, tree.Parameters);
        Assert.Contains(tree.Commands, c => c.StartsWith("einzel render section ", StringComparison.Ordinal));
    }
}
