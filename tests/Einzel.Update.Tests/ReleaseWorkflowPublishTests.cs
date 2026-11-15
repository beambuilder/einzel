namespace Einzel.Update.Tests;

/// <summary>
/// How the release workflow assembles the three applications into one directory, and what it
/// claims about prereleases.
/// </summary>
public sealed partial class ReleaseWorkflowAgreementTests
{
    private static readonly string[] Merged = ["dist/$env:ASSET", "dist/installer"];

    /// <summary>
    /// Publishing einzel, einzel-mcp and einzel-shell one after another into one directory lets
    /// the last overwrite a shared dependency an earlier one resolved at another version, while
    /// that one's .deps.json still names the version it was built against. Each is published to
    /// its own directory and merged by a script that refuses a file they disagree about.
    /// </summary>
    [Fact]
    public void EachApplicationIsPublishedToItsOwnDirectoryAndMergedWithAConflictCheck()
    {
        var workflow = Workflow();

        Assert.DoesNotContain("-o \"dist/$ASSET\"", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("-o dist/installer\n", workflow.ReplaceLineEndings("\n"), StringComparison.Ordinal);

        foreach (var destination in Merged)
        {
            Assert.Contains($"./tools/publish/merge-publish.ps1 -Destination {Quoted(destination)}", workflow, StringComparison.Ordinal);
        }

        Assert.Contains("./tools/publish/check-deps.ps1", workflow, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(Root(), "tools", "publish", "merge-publish.ps1")));
        Assert.True(File.Exists(Path.Combine(Root(), "tools", "publish", "check-deps.ps1")));

        static string Quoted(string d) => d.Contains('$', StringComparison.Ordinal) ? $"\"{d}\"" : d;
    }

    /// <summary>The server is started, not only found: the smoke tests run einzel-mcp on a model.</summary>
    [Fact]
    public void TheSmokeTestsStartTheMcpServer()
    {
        var workflow = Workflow();

        Assert.Contains("./einzel-mcp${{ matrix.exe }} smoke/models/reflectron.json < /dev/null", workflow, StringComparison.Ordinal);
        Assert.Contains("./einzel-mcp.exe smoke/models/reflectron.json < /dev/null", workflow, StringComparison.Ordinal);
    }

    /// <summary>
    /// The version job refuses any tag carrying a suffix, so a prerelease expression testing the
    /// tag for alpha, beta or rc could never be true - dead, and misleading to read.
    /// </summary>
    [Fact]
    public void PrereleaseIsNotInferredFromATagTheVersionJobRefuses()
    {
        var workflow = Workflow();

        Assert.Contains("prerelease: false", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("contains(github.ref_name", workflow, StringComparison.Ordinal);
        Assert.Contains("carries a suffix", workflow, StringComparison.Ordinal);
    }

    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, ".github", "workflows", "release.yml")))
            {
                return d.FullName;
            }
        }

        Assert.Fail(".github/workflows/release.yml was not found above the test assembly");

        return string.Empty;
    }
}
