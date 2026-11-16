using System.Globalization;

namespace Einzel.Update;

/// <summary>Where update and installer problems are written, since the window stays quiet about them.</summary>
/// <remarks>
/// <para>
/// <b>A failed check is not shown</b>: no network is the ordinary state of an instrument PC and
/// not an error a person can act on. But a check that never succeeds must be findable by
/// somebody who goes looking, so it is appended to <c>einzel-update.log</c> beside Velopack's
/// own log, in the install's root directory. That directory exists only for an installed copy,
/// which is the only kind that checks. Writing the log never throws.
/// </para>
/// </remarks>
public static class UpdateLog
{
    /// <summary>The log file's path, or null when the install root cannot be determined.</summary>
    public static string? FilePath
    {
        get
        {
            // current\ is the application directory; its parent is the install root.
            var parent = Directory.GetParent(InstallHooks.CommandDirectory());

            return parent is null ? null : Path.Combine(parent.FullName, "einzel-update.log");
        }
    }

    /// <summary>Appends one line, stamped with the time, and swallows any failure.</summary>
    /// <param name="message">What happened.</param>
    public static void Write(string message)
    {
        System.Diagnostics.Trace.TraceWarning("einzel update: {0}", message);

        try
        {
            if (FilePath is { } file && Directory.Exists(Path.GetDirectoryName(file)))
            {
                File.AppendAllText(
                    file,
                    string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"));
            }
        }
#pragma warning disable CA1031 // A log that can fail the thing it logs is worse than no log.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
