using System.ComponentModel;
using System.Diagnostics;

namespace Einzel.Update;

/// <summary>A process running from the install, other than this one.</summary>
/// <param name="Name">The process name, as Task Manager shows it (<c>einzel</c>, <c>einzel-mcp</c>).</param>
/// <param name="Id">The process id.</param>
public sealed record RunningProcess(string Name, int Id)
{
    /// <summary>The process as a person would look for it: <c>einzel (PID 4120)</c>.</summary>
    /// <returns>The name and id.</returns>
    public override string ToString() => $"{Name} (PID {Id})";
}

/// <summary>Which other processes are running from this install.</summary>
/// <remarks>
/// <para>
/// An interface so that <see cref="UpdateWorkflow"/>, which decides whether an update may be
/// applied, is tested with a stub - the same pattern as <see cref="IUpdateFeed"/>.
/// </para>
/// <para>
/// <b>Why it is asked at all.</b> Applying an update is Velopack's <c>Update.exe apply</c>, and
/// before it swaps the install's <c>current\</c> directory it terminates every process whose
/// executable lies under the install root (<c>force_stop_package</c>, which kills rather than
/// asks). The installer puts <c>einzel</c> and <c>einzel-mcp</c> on PATH from that same
/// directory, so an agent's hours-long <c>einzel run</c>, or an <c>einzel-mcp</c> a client holds
/// open, would be ended by somebody pressing Restart in a window that knew nothing about them -
/// against UPD-6 (never while a run, sweep or solve is active) and AGT-8 (the environment is
/// stable within a session).
/// </para>
/// </remarks>
public interface IInstallProcesses
{
    /// <summary>The other processes running from the install, this one excluded.</summary>
    /// <returns>Each one, by name and id; empty when there are none.</returns>
    IReadOnlyList<RunningProcess> Others();
}

/// <summary>Finds the other processes whose executable lies under a directory.</summary>
/// <remarks>
/// <para>
/// <b>By executable path, not by name.</b> A portable copy of <c>einzel</c> unpacked elsewhere
/// is not touched by applying an update to the install, so it must not block one; only a
/// process whose main module is under the install root would be terminated.
/// </para>
/// <para>
/// <b>Only processes named <c>einzel*</c> are opened.</b> Reading another process's main module
/// opens it, which costs a system call per process and fails for the ones this user may not
/// read; the install holds only Einzel's own executables (and Velopack's <c>Update.exe</c>,
/// which is the updater itself), so a name filter loses nothing that matters. A process whose
/// path cannot be read is skipped: it is not this user's, so it was not started from this
/// user's per-user install.
/// </para>
/// </remarks>
public sealed class InstallProcesses : IInstallProcesses
{
    private readonly string _root;

    /// <summary>Looks under <paramref name="root"/>.</summary>
    /// <param name="root">The install root, e.g. <c>%LocalAppData%\MacCossLab.Einzel</c>.</param>
    public InstallProcesses(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
    }

    /// <summary>The install root of the running copy: the parent of its application directory.</summary>
    /// <returns>A finder over that root.</returns>
    public static InstallProcesses ForThisCopy()
    {
        var parent = Directory.GetParent(InstallHooks.CommandDirectory());

        return new InstallProcesses(parent?.FullName ?? InstallHooks.CommandDirectory());
    }

    /// <inheritdoc />
    public IReadOnlyList<RunningProcess> Others()
    {
        var mine = Environment.ProcessId;
        var found = new List<RunningProcess>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == mine
                        || !process.ProcessName.StartsWith("einzel", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var path = process.MainModule?.FileName;

                    if (path is not null && IsUnder(path))
                    {
                        found.Add(new RunningProcess(process.ProcessName, process.Id));
                    }
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Exited meanwhile, or not ours to read: either way not running from this install.
                }
            }
        }

        return [.. found.OrderBy(p => p.Id)];
    }

    /// <summary>Whether a path lies under the install root.</summary>
    /// <param name="path">An executable's full path.</param>
    /// <returns>True when it is inside the root.</returns>
    internal bool IsUnder(string path) =>
        Path.GetFullPath(path).StartsWith(_root, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Reports no other processes: for a copy that is not installed, where nothing is applied.</summary>
internal sealed class NoInstallProcesses : IInstallProcesses
{
    /// <summary>The one instance.</summary>
    public static readonly NoInstallProcesses Instance = new();

    /// <inheritdoc />
    public IReadOnlyList<RunningProcess> Others() => [];
}
