using System.Text.Json.Nodes;

using Einzel.Extensions;

namespace Einzel.Cli.Tests;

/// <summary>
/// Gives a scaffolded extension a generous timeout, in its manifest, for a test that is not
/// timing it.
/// </summary>
/// <remarks>
/// <para>
/// <c>einzel ext register</c> writes the product's default, thirty seconds - generous for a
/// script that doubles a number, and still not enough on a windows-latest runner sharing four
/// cores with a dozen test hosts, where starting CPython took longer than that and an
/// extension test failed with a timeout that was the machine's rather than the extension's.
/// </para>
/// <para>
/// <b>In the manifest, which is data the test writes</b>, rather than by patching the parsed
/// manifest inside a runner helper: an extension reached through the command surface - <c>ext
/// test</c>, or a study naming <c>ext:</c> - reads the manifest from disk, so only the file
/// reaches every path, and an author can do exactly this. The product's default is unchanged,
/// and <c>SubprocessRunnerTests.TheDefaultTimeoutIsThirtySeconds</c> pins what it is.
/// </para>
/// </remarks>
internal static class ExtensionRoom
{
    /// <summary>The timeout a test that is not timing an extension declares: three minutes.</summary>
    public const int TimeoutMs = 180_000;

    /// <summary>Rewrites one installed extension's <c>resources.timeoutMs</c>.</summary>
    /// <param name="projectRoot">The project the extension was registered in.</param>
    /// <param name="name">The extension's name.</param>
    public static void Give(string projectRoot, string name)
    {
        var path = Path.Combine(projectRoot, "extensions", name, ExtensionCatalogue.ManifestName);
        var manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        if (manifest["resources"] is not JsonObject resources)
        {
            resources = [];
            manifest["resources"] = resources;
        }

        resources["timeoutMs"] = TimeoutMs;

        File.WriteAllText(path, manifest.ToJsonString() + "\n");
    }
}
