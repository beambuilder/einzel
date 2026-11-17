namespace Einzel.Update;

/// <summary>The identity of the installed product and where its releases are published.</summary>
/// <remarks>
/// <para>
/// <b>One place for each of these</b>, because the release workflow, the installer and the
/// update check must agree on them exactly and a second spelling is how they come to
/// disagree. The workflow passes the same strings to <c>vpk pack</c>; a test reads
/// <c>.github/workflows/release.yml</c> and checks that it does.
/// </para>
/// </remarks>
public static class UpdateFeedInfo
{
    /// <summary>The Velopack package id.</summary>
    /// <remarks>
    /// <b>Stable forever.</b> It names the install directory
    /// (<c>%LocalAppData%\MacCossLab.Einzel</c>), the uninstall entry and the feed every
    /// installed copy reads; changing it would strand every existing install on the old id,
    /// with no update ever reaching it. LabOps went through exactly that rename and had to ship
    /// a takeover of the old package to recover.
    /// </remarks>
    public const string PackageId = "MacCossLab.Einzel";

    /// <summary>The repository whose GitHub Releases carry the installer and its feed.</summary>
    /// <remarks>Read from <c>git remote get-url origin</c> when this was written.</remarks>
    public const string RepositoryUrl = "https://github.com/maccoss/einzel";

    /// <summary>The only channel published today.</summary>
    /// <remarks>
    /// <para>
    /// <b>There is no beta channel, deliberately.</b> LabOps derives the channel from the tag -
    /// a tag containing <c>beta</c> publishes to <c>beta</c> - but Einzel's release workflow
    /// refuses any tag that is not digits and dots (<c>vYY.feature.patch</c>), so a beta tag
    /// can never reach the pack step and logic choosing a beta channel would be dead code that
    /// looked like a capability. If prereleases are wanted later, the tag guard is the thing
    /// to change first, and this constant and the workflow's <c>--channel</c> with it.
    /// </para>
    /// <para>
    /// The installed app does not pass a channel to its update check at all: Velopack records
    /// the channel a copy was packed with and reads that one, so a copy installed from a future
    /// beta would follow beta without anyone remembering to tell it.
    /// </para>
    /// </remarks>
    public const string Channel = "stable";

    /// <summary>The window's executable: Velopack's main exe and the Start-menu shortcut.</summary>
    public const string MainExe = "einzel-shell.exe";
}
