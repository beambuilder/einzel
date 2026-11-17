namespace Einzel.Update;

/// <summary>Where the launch-time update check stands.</summary>
public enum UpdateStage
{
    /// <summary>Not yet asked.</summary>
    Idle,

    /// <summary>Not a copy Velopack manages (a development build or an unpacked archive), so nothing is checked.</summary>
    NotManaged,

    /// <summary>The feed is being asked.</summary>
    Checking,

    /// <summary>The feed has nothing newer.</summary>
    UpToDate,

    /// <summary>The feed could not be reached or read. Logged, never shown.</summary>
    CheckFailed,

    /// <summary>A newer release exists and is offered; nothing has been downloaded.</summary>
    Available,

    /// <summary>The person asked for it and it is downloading.</summary>
    Downloading,

    /// <summary>The person asked for it and the download failed. Shown, because they asked.</summary>
    DownloadFailed,

    /// <summary>Downloaded and staged: applied only when the person restarts, and only if nothing would be ended.</summary>
    Ready,

    /// <summary>The person closed the notice; it comes back at the next launch.</summary>
    Dismissed,

    /// <summary>The offered release is one the person asked to skip.</summary>
    Skipped,

    /// <summary>
    /// Applying the staged release failed; the installed version is unchanged. Shown, because
    /// the person asked, and the restart may be tried again.
    /// </summary>
    ApplyFailed,
}

/// <summary>
/// The shell's update check: once, at launch, offered and never imposed (section 18).
/// </summary>
/// <remarks>
/// <para>
/// <b>Once per process, and only when asked at launch (UPD-1).</b> LabOps re-checks every four
/// hours; Einzel deliberately does not. An agent may be issuing hundreds of <c>einzel</c>
/// commands against the installed CLI, and AGT-8 says it sees one version throughout - so there
/// is no timer here, and a second call to <see cref="CheckAtLaunchAsync"/> does nothing.
/// </para>
/// <para>
/// <b>Offered, never imposed (UPD-3, UPD-4).</b> A newer release is a notice, not a dialog;
/// nothing is downloaded until <see cref="DownloadAsync"/> is called from a person's click, and
/// nothing is applied until they press Restart. A failed check is logged and
/// not shown, because no network is an ordinary state and not something a person can act on.
/// </para>
/// <para>
/// <b>Never while a run is active (UPD-6).</b> The restart is refused while the window reports a
/// run in flight - a solve for a redraw included - and the download is staged rather than lost.
/// </para>
/// <para>
/// <b>Never while another Einzel process runs from the install (UPD-6, AGT-8).</b> Applying is
/// Velopack's <c>Update.exe apply</c>, which terminates every process whose executable lies
/// under the install root before it swaps <c>current\</c> - and the installer puts <c>einzel</c>
/// and <c>einzel-mcp</c> on PATH from there. So the restart asks <see cref="IInstallProcesses"/>
/// first and, if an agent's run or an MCP session is using the installed copy, refuses and names
/// each process. <b>Applying is only ever this explicit, guarded path</b>: the shell turns off
/// Velopack's apply-on-startup (<see cref="InstallHooks.Configure"/>), so a release downloaded
/// and left staged is offered again at the next launch, through the same guard, rather than
/// applied blindly before any of Einzel's code has run.
/// </para>
/// <para>
/// Every decision lives here rather than in the window, so it is tested without one. Members
/// may be called from the window's thread; <see cref="Changed"/> may be raised from another,
/// and the window marshals it.
/// </para>
/// </remarks>
public sealed class UpdateWorkflow
{
    private readonly IUpdateFeed? _feed;
    private readonly ISkippedVersionStore _skipped;
    private readonly Action<string> _log;
    private readonly IInstallProcesses _processes;
    private int _asked;

    /// <summary>Creates the workflow.</summary>
    /// <param name="feed">The installed copy's feed, or null for a copy Velopack does not manage.</param>
    /// <param name="skipped">Where a skipped version is remembered.</param>
    /// <param name="log">Where failures are recorded.</param>
    public UpdateWorkflow(IUpdateFeed? feed, ISkippedVersionStore skipped, Action<string> log)
        : this(feed, skipped, log, NoInstallProcesses.Instance)
    {
    }

    /// <summary>Creates the workflow, refusing to apply while other processes run from the install.</summary>
    /// <param name="feed">The installed copy's feed, or null for a copy Velopack does not manage.</param>
    /// <param name="skipped">Where a skipped version is remembered.</param>
    /// <param name="log">Where failures are recorded.</param>
    /// <param name="processes">Which other processes are running from the install.</param>
    public UpdateWorkflow(
        IUpdateFeed? feed, ISkippedVersionStore skipped, Action<string> log, IInstallProcesses processes)
    {
        ArgumentNullException.ThrowIfNull(skipped);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(processes);

        _feed = feed;
        _skipped = skipped;
        _log = log;
        _processes = processes;
    }

    /// <summary>Raised whenever <see cref="Stage"/>, the progress or the message changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Where the check stands.</summary>
    public UpdateStage Stage { get; private set; } = UpdateStage.Idle;

    /// <summary>The version offered, once one has been found.</summary>
    public string? AvailableVersion { get; private set; }

    /// <summary>Download progress, percent.</summary>
    public int Percent { get; private set; }

    /// <summary>Why the download failed, when it did.</summary>
    public string? Error { get; private set; }

    /// <summary>Whether a restart was asked for and refused because a run was active.</summary>
    public bool RestartDeferred { get; private set; }

    /// <summary>
    /// The other processes running from the install that stopped the last restart, or empty.
    /// </summary>
    public IReadOnlyList<RunningProcess> BlockingProcesses { get; private set; } = [];

    /// <summary>Whether the window should show the notice.</summary>
    public bool IsVisible => Stage is UpdateStage.Available or UpdateStage.Downloading
        or UpdateStage.DownloadFailed or UpdateStage.Ready or UpdateStage.ApplyFailed;

    /// <summary>Whether a restart into the staged release may be asked for now.</summary>
    public bool CanRestart => Stage is UpdateStage.Ready or UpdateStage.ApplyFailed;

    /// <summary>Whether a download may be started now.</summary>
    public bool CanDownload => Stage is UpdateStage.Available or UpdateStage.DownloadFailed;

    /// <summary>Whether the offered release may be skipped now.</summary>
    public bool CanSkip => Stage is UpdateStage.Available or UpdateStage.DownloadFailed;

    /// <summary>What the notice says.</summary>
    public string Message => Stage switch
    {
        UpdateStage.Available =>
            $"Einzel {AvailableVersion} is available (this copy is {_feed?.CurrentVersion}). Nothing is downloaded until you choose.",
        UpdateStage.Downloading => $"Downloading Einzel {AvailableVersion}: {Percent}%",
        UpdateStage.DownloadFailed => $"Einzel {AvailableVersion} could not be downloaded: {Error}",
        UpdateStage.Ready when RestartDeferred =>
            $"Einzel {AvailableVersion} is downloaded. A run is in progress, so it was not applied: restart when the run ends.",
        UpdateStage.Ready when BlockingProcesses.Count > 0 =>
            $"Einzel {AvailableVersion} is downloaded and was not applied: applying it would end {string.Join(", ", BlockingProcesses)}, running from this install. Restart once they have exited.",
        UpdateStage.Ready =>
            $"Einzel {AvailableVersion} is downloaded. Restart to use it; nothing is applied until you do.",
        UpdateStage.ApplyFailed =>
            $"Einzel {AvailableVersion} could not be applied: {Error}. The installed version is unchanged.",
        _ => string.Empty,
    };

    /// <summary>Checks the feed, once per process.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the check has finished or been skipped.</returns>
    /// <remarks>Never throws: a check that fails is logged and the window carries on.</remarks>
    public async Task CheckAtLaunchAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _asked, 1) == 1)
        {
            return;
        }

        if (_feed is null)
        {
            Set(UpdateStage.NotManaged);

            return;
        }

        // A release downloaded earlier and left staged is offered for an explicit restart, not
        // applied at launch: Velopack's apply-on-startup is off, so this is where it resurfaces.
        if (Staged() is { } staged)
        {
            AvailableVersion = staged;
            Set(UpdateStage.Ready);

            return;
        }

        Set(UpdateStage.Checking);

        string? newer;

        try
        {
            newer = await _feed.FindNewerAsync(cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Any failure to reach the feed is the same outcome: no offer, a log line.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log($"update check failed: {ex.GetType().Name}: {ex.Message}");
            Set(UpdateStage.CheckFailed);

            return;
        }

        if (newer is null)
        {
            Set(UpdateStage.UpToDate);

            return;
        }

        AvailableVersion = newer;

        Set(string.Equals(_skipped.Read(), newer, StringComparison.Ordinal)
            ? UpdateStage.Skipped
            : UpdateStage.Available);
    }

    /// <summary>Downloads the offered release, because a person asked.</summary>
    /// <param name="restartWhenDone">Restart into it as soon as it is staged, unless a run is active.</param>
    /// <param name="runActive">Asked when the download finishes: whether the window has a run in flight.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>A task that completes when the download has finished or failed.</returns>
    public async Task DownloadAsync(bool restartWhenDone, Func<bool> runActive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runActive);

        if (_feed is null || !CanDownload)
        {
            return;
        }

        Error = null;
        Percent = 0;
        Set(UpdateStage.Downloading);

        try
        {
            await _feed.DownloadAsync(
                p =>
                {
                    Percent = Math.Clamp(p, 0, 100);
                    Changed?.Invoke(this, EventArgs.Empty);
                },
                cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Shown to the person who asked, and logged; the window carries on.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log($"downloading {AvailableVersion} failed: {ex.GetType().Name}: {ex.Message}");
            Error = ex.Message;
            Set(UpdateStage.DownloadFailed);

            return;
        }

        Set(UpdateStage.Ready);

        if (restartWhenDone)
        {
            RestartNow(runActive);
        }
    }

    /// <summary>Applies the staged release and restarts, unless a run is active.</summary>
    /// <param name="runActive">Whether the window has a run in flight.</param>
    /// <returns>False when nothing is staged or a run is active; on success the process ends.</returns>
    /// <remarks>
    /// Refused, and the reason shown, while the window has a run in flight or while any other
    /// process is running from the install - Velopack would terminate it. A failure to apply is
    /// caught, logged and shown, never thrown: this is called from a click.
    /// </remarks>
    public bool RestartNow(Func<bool> runActive)
    {
        ArgumentNullException.ThrowIfNull(runActive);

        if (_feed is null || !CanRestart)
        {
            return false;
        }

        if (runActive())
        {
            RestartDeferred = true;
            BlockingProcesses = [];
            Changed?.Invoke(this, EventArgs.Empty);

            return false;
        }

        RestartDeferred = false;

        IReadOnlyList<RunningProcess> others;

        try
        {
            others = _processes.Others();
        }
#pragma warning disable CA1031 // Not knowing what would be ended is a reason not to apply, not a crash.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log($"could not list the processes running from the install, so {AvailableVersion} was not applied: {ex.Message}");
            Error = "could not check whether other Einzel processes are running from this install (" + ex.Message + ")";
            Set(UpdateStage.ApplyFailed);

            return false;
        }

        BlockingProcesses = others;

        if (others.Count > 0)
        {
            _log($"{AvailableVersion} not applied: {string.Join(", ", others)} running from the install");
            Set(UpdateStage.Ready);

            return false;
        }

        try
        {
            _feed.ApplyAndRestart();
        }
#pragma warning disable CA1031 // Shown to the person who asked, and logged; the window carries on.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log($"applying {AvailableVersion} failed: {ex.GetType().Name}: {ex.Message}");
            Error = ex.Message;
            Set(UpdateStage.ApplyFailed);

            return false;
        }

        return true;
    }

    /// <summary>Closes the notice until the next launch. A staged release stays staged.</summary>
    public void Dismiss()
    {
        if (IsVisible && Stage != UpdateStage.Downloading)
        {
            Set(UpdateStage.Dismissed);
        }
    }

    /// <summary>Never offers this release again; a later one is still offered.</summary>
    public void Skip()
    {
        if (!CanSkip || AvailableVersion is null)
        {
            return;
        }

        try
        {
            _skipped.Write(AvailableVersion);
        }
#pragma warning disable CA1031 // Failing to remember is not worth an error; the notice still closes.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log($"could not remember skipping {AvailableVersion}: {ex.Message}");
        }

        Set(UpdateStage.Skipped);
    }

    private string? Staged()
    {
        try
        {
            return _feed?.StagedVersion;
        }
#pragma warning disable CA1031 // A staged package that cannot be read is simply not offered.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log($"could not read a staged update: {ex.Message}");

            return null;
        }
    }

    private void Set(UpdateStage stage)
    {
        Stage = stage;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
