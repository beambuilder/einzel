using Velopack;
using Velopack.Sources;

namespace Einzel.Update;

/// <summary>The real feed: GitHub Releases, read through Velopack.</summary>
/// <remarks>
/// <para>
/// <b>No decisions here</b> - which copies check, what is offered and when anything is applied
/// are all <see cref="UpdateWorkflow"/>'s, where they are tested. This only translates.
/// </para>
/// <para>
/// <b>Only an installed, non-portable copy gets a feed.</b> <see cref="TryForThisCopy"/> returns
/// null for a development build, an unpacked archive and a Velopack portable copy, and a null
/// feed is a workflow that never touches the network - UPD-9's "portable installs never
/// self-update". The release workflow also passes <c>--noPortable</c> so no Velopack portable
/// zip is published; the framework-dependent archives are the portable form (DST-2).
/// </para>
/// </remarks>
public sealed class VelopackFeed : IUpdateFeed
{
    private readonly UpdateManager _manager;
    private UpdateInfo? _found;
    private bool _staged;

    private VelopackFeed(UpdateManager manager) => _manager = manager;

    /// <inheritdoc />
    public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? "unknown";

    /// <summary>The feed for this copy, or null when Velopack does not manage it.</summary>
    /// <returns>A feed, or null for a copy that must never check.</returns>
    public static VelopackFeed? TryForThisCopy()
    {
        try
        {
            // prerelease: true so the channel recorded at install, not GitHub's prerelease flag,
            // decides which releases a copy follows - the lesson LabOps took from PanoramaBridge.
            // No access token: the repository is public, and the shell holds no credentials.
            var source = new GithubSource(UpdateFeedInfo.RepositoryUrl, null, true);
            var manager = new UpdateManager(source);

            return manager.IsInstalled && !manager.IsPortable ? new VelopackFeed(manager) : null;
        }
#pragma warning disable CA1031 // A copy whose install cannot be read is treated as unmanaged: no check.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            UpdateLog.Write($"could not read this copy's install: {ex.Message}");

            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> FindNewerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _found = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);

        return _found?.TargetFullRelease.Version.ToString();
    }

    /// <inheritdoc />
    public async Task DownloadAsync(Action<int> progress, CancellationToken cancellationToken)
    {
        if (_found is null)
        {
            throw new InvalidOperationException("No newer release has been found to download.");
        }

        await _manager.DownloadUpdatesAsync(_found, progress, cancellationToken).ConfigureAwait(false);
        _staged = true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Velopack's own record of a package downloaded and not yet applied, which is how a release
    /// staged by "Download only" in an earlier launch is found again now that apply-on-startup is
    /// off (<see cref="InstallHooks.Configure"/>).
    /// </remarks>
    public string? StagedVersion => _manager.UpdatePendingRestart?.Version.ToString();

    /// <inheritdoc />
    public void ApplyAndRestart()
    {
        // Downloaded in this process, or staged by an earlier one and offered again at launch.
        var target = _found is not null && _staged ? _found.TargetFullRelease : _manager.UpdatePendingRestart;

        if (target is null)
        {
            throw new InvalidOperationException("No downloaded release is staged.");
        }

        _manager.ApplyUpdatesAndRestart(target);
    }
}

/// <summary>Remembers a skipped version in a one-line file in the install's root directory.</summary>
/// <remarks>
/// The install root (<c>%LocalAppData%\MacCossLab.Einzel</c>) rather than <c>current\</c>,
/// because an update replaces <c>current\</c> wholesale; and rather than anywhere else, because
/// uninstalling removes the root and should take this with it.
/// </remarks>
public sealed class FileSkippedVersionStore : ISkippedVersionStore
{
    private readonly string? _file;

    /// <summary>Creates the store beside this copy's install.</summary>
    public FileSkippedVersionStore()
    {
        var parent = Directory.GetParent(InstallHooks.CommandDirectory());
        _file = parent is null ? null : Path.Combine(parent.FullName, "einzel-skipped-version.txt");
    }

    /// <inheritdoc />
    public string? Read()
    {
        try
        {
            return _file is not null && File.Exists(_file) ? File.ReadAllText(_file).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Write(string version)
    {
        if (_file is not null)
        {
            File.WriteAllText(_file, version);
        }
    }
}

/// <summary>The workflow for the running copy.</summary>
public static class UpdateCheck
{
    /// <summary>
    /// Builds the workflow for this process: a real feed for an installed copy, none otherwise.
    /// </summary>
    /// <returns>A workflow whose <see cref="UpdateWorkflow.CheckAtLaunchAsync"/> the shell calls once.</returns>
    public static UpdateWorkflow ForThisCopy()
    {
        var feed = VelopackFeed.TryForThisCopy();

        return new UpdateWorkflow(
            feed, new FileSkippedVersionStore(), UpdateLog.Write,
            feed is null ? NoInstallProcesses.Instance : InstallProcesses.ForThisCopy());
    }
}
