using System.Globalization;

using Avalonia;

namespace Einzel.Shell;

/// <summary>The shell's entry point.</summary>
public static class Program
{
    /// <summary>
    /// Starts the window, optionally on a model named on the command line.
    /// </summary>
    /// <param name="args">
    /// The model document to open, if any. A shell started with none opens empty and
    /// waits, which is what a person double-clicking the binary gets.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>No platform is named here.</b> <c>UsePlatformDetect</c> chooses Win32, X11 or
    /// Cocoa at run time, which is the whole reason this project exists beside the WPF
    /// one: the engine runs where .NET runs and the window should too.
    /// </para>
    /// </remarks>
    [STAThread]
    public static void Main(string[] args)
    {
        // Must be first. On an installer hook (install, update, uninstall) Velopack runs the
        // hook and ends the process here, before a window or a culture exists. A staged update is
        // NOT applied here: Configure turns that off, so it waits for the window's guarded restart.
        // In a copy Velopack does not manage it returns at
        // once. Nothing in this call touches the network - the update check is the window's,
        // once, after it opens. Spelled out here rather than inside Einzel.Update because
        // `vpk pack` checks that the main executable's own assembly calls Build() and Run().
        Einzel.Update.InstallHooks.Configure(Velopack.VelopackApp.Build().SetArgs(args)).Run();

        UseInvariantNumbers();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Formats and parses numbers the way the model document does, on every thread.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The shell turns globalization back on and must not turn the locale on with it.</b>
    /// A window cannot run globalization-invariant - text layout needs the culture data - so
    /// the build setting every other assembly relies on is reversed here. What that setting
    /// was really doing for the CLI is making the <em>current culture</em> invariant, and
    /// about 265 interpolated strings below the shell format numbers without naming a culture
    /// because, in the process they were written for, none is needed. In this process, on a
    /// German desktop, every one of them would print a decimal comma - into status lines and
    /// warnings shown beside a box that parses <c>4.5</c> and refuses <c>4,5</c>, because the
    /// file being edited is invariant.
    /// </para>
    /// <para>
    /// <b>One setting rather than 265 edits</b>, because the edits would be made once and the
    /// next string written without a culture would undo them; the culture data stays
    /// available, and only what the current culture is changes. The user-interface culture is
    /// left alone, since nothing here is translated.
    /// </para>
    /// </remarks>
    public static void UseInvariantNumbers()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    /// <summary>The application, configured but not started.</summary>
    /// <returns>A builder the entry point starts and a designer can reuse.</returns>
    /// <remarks>
    /// Separate from <see cref="Main"/> because Avalonia's tooling looks for exactly this
    /// shape, and because a test can build the application without starting a message loop.
    /// </remarks>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<ShellApp>()
            .UsePlatformDetect()
            .LogToTrace();
}
