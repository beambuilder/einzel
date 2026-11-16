using Velopack;

namespace Einzel.Update;

/// <summary>
/// Velopack's start-up handling and the installer's hooks, which put <c>einzel</c> on PATH.
/// </summary>
/// <remarks>
/// <para>
/// <b>The command line is Einzel's primary surface</b> (section 15), and Velopack installs into
/// <c>%LocalAppData%\MacCossLab.Einzel\current</c>, which is on nobody's PATH. An installer that
/// leaves the CLI unreachable by name has installed the window and hidden the product. So the
/// install hook adds that directory to the user PATH and the uninstall hook takes it out again.
/// <c>current</c> keeps its name across updates - Velopack replaces its contents - so an update
/// has nothing to do, and re-adds the entry only if somebody removed it.
/// </para>
/// <para>
/// <b>A hook must be quick and must never throw.</b> Velopack runs it during install or
/// uninstall, kills it after 30 seconds, and a failure there would fail the install over
/// something the user can fix by hand. Every hook swallows and records its failure.
/// </para>
/// </remarks>
public static class InstallHooks
{
    /// <summary>
    /// Adds Einzel's installer hooks to Velopack's start-up handling.
    /// </summary>
    /// <param name="app">The builder from <c>VelopackApp.Build()</c>.</param>
    /// <returns>The same builder, for the shell to call <c>Run()</c> on.</returns>
    /// <remarks>
    /// <para>
    /// <b>The shell calls <c>VelopackApp.Build()</c> and <c>Run()</c> itself</b>, first thing in
    /// <c>Main</c>, because <c>vpk pack</c> refuses to package a main executable whose own
    /// assembly does not visibly make that call - a check worth keeping, since an install whose
    /// entry point forgot it can never be updated or cleanly uninstalled. What the hooks do lives
    /// here, where it is tested.
    /// </para>
    /// <para>
    /// On an install, update or uninstall hook Velopack runs the hook and exits the process. In a
    /// copy Velopack does not manage - a development build, an unpacked archive - <c>Run()</c>
    /// returns at once and does nothing.
    /// </para>
    /// <para>
    /// <b>Apply-on-startup is turned off</b> (<c>SetAutoApplyOnStartup(false)</c>; Velopack's
    /// default is on). With it on, a release staged by "Download only" is applied inside
    /// <c>Run()</c> at the next launch, before any of Einzel's code runs - and applying runs
    /// <c>Update.exe apply</c>, which terminates every process under the install root, an
    /// agent's <c>einzel run</c> or an open <c>einzel-mcp</c> included. Off, the staged release
    /// waits and <see cref="UpdateWorkflow"/> offers it for an explicit restart, which refuses
    /// while a run is active or another Einzel process is running from the install (UPD-6).
    /// </para>
    /// </remarks>
    public static VelopackApp Configure(VelopackApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Applying is only ever the explicit, guarded restart in UpdateWorkflow; see above.
        app = app.SetAutoApplyOnStartup(false);

        // The hooks exist only on Windows, which is the only platform an installer is published
        // for (DST-1, DST-3). Elsewhere Run still returns at once for an unmanaged copy, so the
        // shell's entry point is the same on every platform.
        if (OperatingSystem.IsWindows())
        {
            app = app
                .OnAfterInstallFastCallback(_ => AddToPath())
                .OnAfterUpdateFastCallback(_ => AddToPath())
                .OnBeforeUninstallFastCallback(_ => RemoveFromPath());
        }

        return app;
    }

    /// <summary>The directory to put on PATH: the one this executable runs from.</summary>
    /// <returns>The application directory without a trailing separator.</returns>
    /// <remarks>
    /// Velopack starts each hook from <c>current\einzel-shell.exe</c>, and <c>einzel.exe</c> sits
    /// beside it, so the base directory is exactly the entry to add.
    /// </remarks>
    public static string CommandDirectory() =>
        AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static void AddToPath()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                UserPath.Add(CommandDirectory());
            }
        }
#pragma warning disable CA1031 // A hook must never throw: the install matters more than the PATH entry.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            UpdateLog.Write($"could not add {CommandDirectory()} to the user PATH: {ex.Message}");
        }
    }

    private static void RemoveFromPath()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                UserPath.Remove(CommandDirectory());
            }
        }
#pragma warning disable CA1031 // As above: an uninstall must finish whatever happens to PATH.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            UpdateLog.Write($"could not remove {CommandDirectory()} from the user PATH: {ex.Message}");
        }
    }
}
