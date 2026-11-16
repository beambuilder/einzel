using System.IO;

using Einzel.Commands;
using Einzel.Shell.Presentation;

using Xunit.Abstractions;

namespace Einzel.Shell.Tests;

/// <summary>
/// The extension manager's own decisions, which are LIC-2's second half: an extension
/// carries a licence and the manager surfaces it.
/// </summary>
/// <remarks>
/// The listing itself is <see cref="ExtensionCommand"/>'s. What is pinned here is what a
/// reader sees, and two of those matter more than the rest: an <b>undeclared</b> licence
/// says so rather than showing blank, and the sandbox's <b>unenforced</b> containment
/// reaches the pane. The second is the one a window would most plausibly drop, since it is
/// a safety statement with no row of its own.
/// </remarks>
public sealed class ExtensionsViewModelTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-shell-ext", Guid.NewGuid().ToString("N"));

    private readonly string _elsewhere = Path.Combine(
        Path.GetTempPath(), "einzel-shell-loose", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        if (Directory.Exists(_elsewhere))
        {
            Directory.Delete(_elsewhere, recursive: true);
        }
    }

    private string Project()
    {
        Assert.Equal(0, Einzel.Cli.Program.Main(["init", _root]));
        return Path.Combine(_root, "models", "reflectron.json");
    }

    private ShellSession Session() =>
        new(Project(), new JournalAuthor("test", AuthorKind.Human));

    /// <summary>Scaffolds an extension, then optionally strips its declared licence.</summary>
    private void Register(string name, bool withLicence)
    {
        Assert.Equal(0, Einzel.Cli.Program.Main(["ext", "register", name, "--project", _root]));

        var manifest = Path.Combine(_root, "extensions", name, "extension.json");
        Assert.True(File.Exists(manifest), manifest);

        if (withLicence)
        {
            return;
        }

        // An extension that declares nothing. This is the case the pane exists for, and
        // `ext register` scaffolds a licence precisely so it is not the default.
        var text = File.ReadAllText(manifest);
        using var document = System.Text.Json.JsonDocument.Parse(text);
        var stripped = new System.Text.Json.Nodes.JsonObject();

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, "licence", StringComparison.Ordinal))
            {
                continue;
            }

            stripped[property.Name] = System.Text.Json.Nodes.JsonNode.Parse(property.Value.GetRawText());
        }

        File.WriteAllText(manifest, stripped.ToJsonString());
    }

    /// <summary>
    /// LIC-2: a declared licence is shown, and an undeclared one says NOT DECLARED rather
    /// than showing an empty cell. A reader cannot recompute this field for themselves, so
    /// the extension most worth asking about must not be the one whose row is quietest.
    /// </summary>
    [Fact]
    public void AnUndeclaredLicenceSaysSoRatherThanShowingNothing()
    {
        var session = Session();
        Register("declared", withLicence: true);
        Register("silent", withLicence: false);

        var manager = new ExtensionsViewModel(session);
        Assert.True(manager.Refresh());

        var declared = Assert.Single(manager.Extensions, e => e.Name == "declared");
        var silent = Assert.Single(manager.Extensions, e => e.Name == "silent");

        output.WriteLine($"declared: '{declared.Licence}' (declared {declared.LicenceDeclared})");
        output.WriteLine($"silent:   '{silent.Licence}' (declared {silent.LicenceDeclared})");
        output.WriteLine($"status:   {manager.Status}");

        Assert.True(declared.LicenceDeclared);
        Assert.NotEqual("NOT DECLARED", declared.Licence);
        Assert.False(string.IsNullOrWhiteSpace(declared.Licence));

        Assert.False(silent.LicenceDeclared);
        Assert.Equal("NOT DECLARED", silent.Licence);

        Assert.Equal(1, manager.Undeclared);
        Assert.Contains("no declared licence", manager.Status, StringComparison.Ordinal);
    }

    /// <summary>
    /// The containment gaps reach the pane. `einzel ext list` prints them on stderr on every
    /// listing, because a containment measure claimed and not applied is worse than one
    /// absent and known to be; a window that dropped them would be the shell weakening a
    /// statement the command layer refuses to weaken.
    /// </summary>
    [Fact]
    public void WhatTheSandboxDoesNotEnforceReachesThePane()
    {
        var session = Session();
        Register("sandboxed", withLicence: true);

        var manager = new ExtensionsViewModel(session);
        Assert.True(manager.Refresh());

        foreach (var gap in manager.Unenforced)
        {
            output.WriteLine($"not enforced: {gap}");
        }

        Assert.NotEmpty(manager.Unenforced);

        // The three the runner names. Asserted by substance rather than by count, so adding
        // a fourth gap does not fail this and removing one of these does.
        var all = string.Join(" ", manager.Unenforced).ToUpperInvariant();
        Assert.Contains("NETWORK", all, StringComparison.Ordinal);
        Assert.Contains("FILESYSTEM", all, StringComparison.Ordinal);
        Assert.Contains("MEMORY", all, StringComparison.Ordinal);

        // The enum name as the command reports it; the pane counts sandboxed case-insensitively.
        Assert.Equal("sandboxed", Assert.Single(manager.Extensions).Trust, ignoreCase: true);
    }

    /// <summary>
    /// A project with nothing installed, and a model outside any project, read differently.
    /// Both show no rows, and only one of them means something is missing.
    /// </summary>
    [Fact]
    public void NothingInstalledAndNowhereToInstallAreDifferentStates()
    {
        var session = Session();

        var empty = new ExtensionsViewModel(session);
        Assert.False(empty.Refresh());
        output.WriteLine($"empty project: {empty.Status}");

        Assert.Empty(empty.Extensions);
        Assert.Contains("no extensions installed", empty.Status, StringComparison.Ordinal);
        Assert.Contains("ext register", empty.Status, StringComparison.Ordinal);

        // The same model copied out of the project - genuinely out, not merely out of the
        // models folder: the layout is found by walking up, so a copy at the project root
        // is still inside a project. An earlier version of this test put it there and
        // reported the two states as identical, which they were.
        Directory.CreateDirectory(_elsewhere);
        var loose = Path.Combine(_elsewhere, "loose.json");
        File.Copy(Path.Combine(_root, "models", "reflectron.json"), loose);

        var outside = new ExtensionsViewModel(
            new ShellSession(loose, new JournalAuthor("test", AuthorKind.Human)));

        Assert.False(outside.Refresh());
        output.WriteLine($"outside a project: {outside.Status}");

        Assert.Empty(outside.Extensions);
        Assert.NotEqual(empty.Status, outside.Status);
    }

    /// <summary>
    /// Amendment 25: every shell action is journalled as the CLI invocation that would
    /// reproduce it, so a human's session hands over to an agent in one vocabulary.
    /// </summary>
    [Fact]
    public void ReadingTheListIsJournalledAsTheCommandThatWouldReproduceIt()
    {
        var session = Session();
        Register("declared", withLicence: true);

        Assert.True(new ExtensionsViewModel(session).Refresh());

        var commands = session.Actions.Select(a => a.Command).ToList();
        output.WriteLine(string.Join("\n", commands));

        Assert.Contains("einzel ext list", commands);
    }
}
