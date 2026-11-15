using System.Text.RegularExpressions;

using Einzel.Update;

namespace Einzel.Update.Tests;

/// <summary>
/// The installer the workflow packs and the feed the app reads must name the same things.
/// </summary>
/// <remarks>
/// The package id and the main executable are written twice, once in C# and once in
/// <c>.github/workflows/release.yml</c>, because the workflow cannot read a C# constant. A
/// disagreement would not fail any build: it would publish an installer whose copies look for
/// a feed under another name and never see an update. So the second spelling is checked
/// against the first here.
/// </remarks>
public sealed partial class ReleaseWorkflowAgreementTests
{
    private static string Workflow()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var file = Path.Combine(d.FullName, ".github", "workflows", "release.yml");

            if (File.Exists(file))
            {
                return File.ReadAllText(file);
            }
        }

        Assert.Fail(".github/workflows/release.yml was not found above the test assembly");

        return string.Empty;
    }

    [Fact]
    public void TheWorkflowPacksUnderThePackageIdTheAppReads()
    {
        var match = PackId().Match(Workflow());

        Assert.True(match.Success, "PACK_ID was not found in release.yml");
        Assert.Equal(UpdateFeedInfo.PackageId, match.Groups[1].Value);
    }

    [Fact]
    public void TheWorkflowPublishesTheChannelTheAppFollows()
    {
        var match = Channel().Match(Workflow());

        Assert.True(match.Success, "CHANNEL was not found in release.yml");
        Assert.Equal(UpdateFeedInfo.Channel, match.Groups[1].Value);
    }

    [Fact]
    public void TheWorkflowNamesTheShellAsTheMainExecutable()
    {
        Assert.Contains($"--mainExe {UpdateFeedInfo.MainExe}", Workflow(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheRepositoryIsTheOneTheWorkflowPublishesTo()
    {
        // The workflow publishes to whichever repository runs it; the app's constant must be
        // a GitHub repository URL with no trailing slash or .git, which is what Velopack expects.
        Assert.Matches(@"^https://github\.com/[^/]+/[^/.]+$", UpdateFeedInfo.RepositoryUrl);
    }

    [GeneratedRegex(@"^\s*PACK_ID:\s*(\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex PackId();

    [GeneratedRegex(@"^\s*CHANNEL:\s*(\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex Channel();
}
