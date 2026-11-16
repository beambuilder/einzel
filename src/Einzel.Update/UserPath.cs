using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Einzel.Update;

/// <summary>Reads and writes the current user's PATH in <c>HKCU\Environment</c>.</summary>
/// <remarks>
/// <para>
/// <b>The registry rather than <c>Environment.SetEnvironmentVariable(..., User)</c></b>, because
/// that API reads the value expanded and writes it back as <c>REG_SZ</c>: a PATH holding
/// <c>%USERPROFILE%\bin</c> would come back with the variable frozen to today's value and the
/// value kind changed. Here the value is read unexpanded and written with the kind it had, so
/// nothing but the one entry moves. All the string work is <see cref="PathList"/>.
/// </para>
/// <para>
/// <b>The user PATH, never the machine one</b>: DST-1's installer needs no administrator
/// rights, and neither does this.
/// </para>
/// </remarks>
public static partial class UserPath
{
    private const string EnvironmentKey = "Environment";

    /// <summary>Adds <paramref name="directory"/> to the user PATH unless it is already there.</summary>
    /// <param name="directory">The directory.</param>
    /// <returns>Whether the PATH was changed.</returns>
    [SupportedOSPlatform("windows")]
    public static bool Add(string directory) =>
        Edit(p => PathList.Add(p, directory, Environment.ExpandEnvironmentVariables));

    /// <summary>Removes every entry naming <paramref name="directory"/> from the user PATH.</summary>
    /// <param name="directory">The directory.</param>
    /// <returns>Whether the PATH was changed.</returns>
    [SupportedOSPlatform("windows")]
    public static bool Remove(string directory) =>
        Edit(p => PathList.Remove(p, directory, Environment.ExpandEnvironmentVariables));

    [SupportedOSPlatform("windows")]
    private static bool Edit(Func<string?, PathEdit> edit)
    {
        if (!Edit(new RegistryPathValueStore(), edit, UpdateLog.Write))
        {
            return false;
        }

        Broadcast();

        return true;
    }
    /// <summary>Tells running programs that the environment changed.</summary>
    /// <remarks>
    /// Without this, Explorer keeps the old PATH and a terminal opened from it does not find
    /// <c>einzel</c> until the user signs out. A short per-window timeout with
    /// <c>SMTO_ABORTIFHUNG</c>, because a Velopack hook is killed after 30 seconds and one hung
    /// window must not spend that budget.
    /// </remarks>
    private static void Broadcast()
    {
        const nint HwndBroadcast = 0xffff;
        const uint WmSettingChange = 0x001A;
        const uint SmtoAbortIfHung = 0x0002;

        _ = NativeMethods.SendMessageTimeoutW(
            HwndBroadcast, WmSettingChange, 0, EnvironmentKey, SmtoAbortIfHung, 200, out _);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern nint SendMessageTimeoutW(
            nint hWnd, uint msg, nuint wParam, string lParam, uint flags, uint timeout, out nuint result);
    }
}
