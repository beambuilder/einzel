using System.Text.RegularExpressions;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// <c>docs/warning-codes.md</c> lists every code the source can attach to an error or a warning,
/// and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>The page was compiled by hand</b> and there is no registry of warning codes - the dotted
/// ones are string literals written where each warning is built, which is deliberate (the code
/// sits beside the condition that raises it). So the page is held to the source here, in both
/// directions: a code added in <c>src/</c> without an entry fails, and an entry for a code no
/// longer in <c>src/</c> fails.
/// </para>
/// <para>
/// <b>How a code is recognised.</b> Every string literal in <c>src/</c> of one of the two shapes
/// codes take: upper-case words joined by underscores (<c>UNITS_INCOMPATIBLE</c>, the
/// <c>ErrorCodes</c> constants and the few error and warning literals beside them), or dotted
/// lower-case words with hyphens (<c>mesh.node-on-shared-face</c>). Reading literals rather than
/// the first argument of <c>new ValidityWarning(</c> keeps codes that pass through a helper or a
/// constant. The only false positives that shape admits in this source are file names
/// (<c>reflectron.json</c>, <c>python.exe</c>), excluded by extension below; anything else that
/// matches and is not a code fails the test, which is the right default - it should be looked at.
/// </para>
/// </remarks>
public sealed partial class DocumentationWarningCodeTests(ITestOutputHelper output)
{
    /// <summary>
    /// File extensions: a dotted literal ending in one of these is a file name, not a code.
    /// Extend this list, with a reason, rather than loosening the patterns.
    /// </summary>
    private static readonly string[] FileExtensions =
        [".json", ".py", ".exe", ".dll", ".html", ".vti", ".vtu", ".csv", ".svg", ".pdf", ".png", ".md", ".txt", ".log"];

    [Fact]
    public void EveryCodeInTheSourceHasAnEntryAndEveryEntryIsInTheSource()
    {
        var root = RepositoryRoot();
        var inSource = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');

            if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);

            foreach (var match in UpperCode().Matches(text).Concat(DottedCode().Matches(text)))
            {
                var code = match.Groups[1].Value;

                if (FileExtensions.Any(e => code.EndsWith(e, StringComparison.Ordinal)))
                {
                    continue;
                }

                inSource.TryAdd(code, relative);
            }
        }

        var page = File.ReadAllText(Path.Combine(root, "docs", "warning-codes.md"));
        var entries = Entry().Matches(page).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        output.WriteLine($"{inSource.Count} codes in src/, {entries.Count} entries in docs/warning-codes.md");

        // The control: a matcher that had stopped matching would pass over nothing.
        Assert.True(inSource.Count > 100, $"only {inSource.Count} codes found in src/; the patterns have stopped matching");
        Assert.Contains("UNITS_INCOMPATIBLE", inSource.Keys);
        Assert.Contains("mesh.node-on-shared-face", inSource.Keys);

        var missing = inSource.Where(c => !entries.Contains(c.Key)).Select(c => $"{c.Key} (from {c.Value})").ToList();
        var stale = entries.Where(e => !inSource.ContainsKey(e)).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            missing.Count == 0,
            "codes in src/ with no '### <code>' entry in docs/warning-codes.md:\n  " + string.Join("\n  ", missing));
        Assert.True(
            stale.Count == 0,
            "entries in docs/warning-codes.md for codes no longer in src/:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>The index at the top links every entry, and only entries, by their own anchor.</summary>
    [Fact]
    public void TheIndexLinksEveryEntry()
    {
        var page = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "warning-codes.md"));
        var entries = Entry().Matches(page).Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToList();
        var index = page[page.IndexOf("## Index", StringComparison.Ordinal)..];
        index = index[..index.IndexOf("\n## ", 1, StringComparison.Ordinal)];

        var links = IndexLink().Matches(index).ToList();

        Assert.Equal(entries, links.Select(l => l.Groups[1].Value).Order(StringComparer.Ordinal).ToList());
        Assert.All(links, l => Assert.Equal(l.Groups[1].Value.ToLowerInvariant().Replace(".", string.Empty, StringComparison.Ordinal), l.Groups[2].Value));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SPEC.md")))
            {
                return directory.FullName;
            }
        }

        throw new FileNotFoundException("SPEC.md is not above the test binary");
    }

    [GeneratedRegex("\"([A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+)\"")]
    private static partial Regex UpperCode();

    [GeneratedRegex("\"([a-z][a-z0-9]*(?:\\.[a-z][a-z0-9]*(?:-[a-z0-9]+)*)+)\"")]
    private static partial Regex DottedCode();

    [GeneratedRegex(@"^### (\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex Entry();

    [GeneratedRegex(@"\[`([^`]+)`\]\(#([^)]+)\)")]
    private static partial Regex IndexLink();
}
