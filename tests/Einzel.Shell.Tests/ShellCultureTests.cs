using System.Globalization;

namespace Einzel.Shell.Tests;

/// <summary>Runs alone, because it changes the culture every thread in the process defaults to.</summary>
[CollectionDefinition(nameof(ProcessCulture), DisableParallelization = true)]
public sealed class ProcessCulture;

/// <summary>
/// The shell formats numbers the way the model document does, whatever the desktop's locale.
/// </summary>
/// <remarks>
/// <para>
/// <b>The CLI gets this from a build setting the shell cannot use.</b> Globalization-invariant
/// mode makes the current culture invariant, and a window cannot run in that mode, so the
/// shell reverses it - and with it, about 265 interpolated strings below the shell that name
/// no culture would start printing decimal commas on a German desktop.
/// </para>
/// <para>
/// Each assertion has a control beside it that passes only while the desktop's locale still
/// reaches an interpolation, so the test cannot pass because the machine it runs on happens to
/// use a point.
/// </para>
/// </remarks>
[Collection(nameof(ProcessCulture))]
public sealed class ShellCultureTests
{
    /// <summary>On this thread and on every thread started after it.</summary>
    [Fact]
    public void NumbersAreInvariantOnEveryThreadOnceTheShellStarts()
    {
        var thread = CultureInfo.CurrentCulture;
        var defaulted = CultureInfo.DefaultThreadCurrentCulture;

        try
        {
            var german = CultureInfo.GetCultureInfo("de-DE");

            CultureInfo.CurrentCulture = german;
            CultureInfo.DefaultThreadCurrentCulture = german;

            // The controls: as things stand, both this thread and a new one print a comma.
            Assert.Equal("4,5", Format(4.5));
            Assert.Equal("4,5", OnANewThread(4.5));

            Program.UseInvariantNumbers();

            Assert.Equal("4.5", Format(4.5));
            Assert.Equal("4.5", OnANewThread(4.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = thread;
            CultureInfo.DefaultThreadCurrentCulture = defaulted;
        }
    }

    /// <summary>An interpolation that names no culture, as the strings below the shell do.</summary>
    private static string Format(double value) => $"{value:G6}";

    private static string OnANewThread(double value)
    {
        string? formatted = null;

        var thread = new Thread(() => formatted = Format(value));
        thread.Start();
        thread.Join();

        return formatted!;
    }
}
