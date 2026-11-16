using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using Einzel.Update;

namespace Einzel.Shell;

/// <summary>
/// The non-modal notice that a newer Einzel is available (UPD-4), and the buttons that act on it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Presentation only.</b> Whether to check, what to offer, when a restart is refused and what
/// the notice says are <see cref="UpdateWorkflow"/>'s decisions, tested in
/// <c>Einzel.Update.Tests</c>; this lays out its message and forwards clicks. UI-1 gives the
/// shell the update check, and this is the whole of the shell's part in it.
/// </para>
/// <para>
/// <b>Hidden unless there is something to act on.</b> An up-to-date copy, a failed check and a
/// copy Velopack does not manage all look identical here - nothing is drawn - because none of
/// them is something a person can do anything about.
/// </para>
/// <para>
/// Built in code rather than markup so that the window's own markup gains a single element and
/// nothing else.
/// </para>
/// </remarks>
public sealed class UpdateNoticeBar : Border
{
    private readonly TextBlock _message = new()
    {
        FontSize = 12,
        Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly Button _downloadRestart = new() { Content = "Download and restart" };
    private readonly Button _download = new() { Content = "Download only" };
    private readonly Button _restart = new() { Content = "Restart now" };
    private readonly Button _skip = new() { Content = "Skip this version" };
    private readonly Button _close = new() { Content = "Not now" };

    private UpdateWorkflow? _workflow;
    private Func<bool> _runActive = static () => false;

    /// <summary>Creates the bar, hidden until a workflow is attached and has something to offer.</summary>
    public UpdateNoticeBar()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x5A));
        Padding = new Avalonia.Thickness(10, 6);
        IsVisible = false;

        ToolTip.SetTip(_downloadRestart, "Download the new version now and restart into it. Refused while a run is in progress.");
        ToolTip.SetTip(_download, "Download the new version now; it is installed when you choose Restart now, in this session or a later one.");
        ToolTip.SetTip(_skip, "Do not offer this version again. A later one is still offered.");
        ToolTip.SetTip(_close, "Hide this until Einzel next starts.");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Avalonia.Thickness(10, 0, 0, 0) };
        buttons.Children.AddRange([_downloadRestart, _download, _restart, _skip, _close]);

        var dock = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        dock.Children.Add(buttons);
        dock.Children.Add(_message);
        Child = dock;

        _downloadRestart.Click += async (_, _) => await (_workflow?.DownloadAsync(true, _runActive) ?? Task.CompletedTask);
        _download.Click += async (_, _) => await (_workflow?.DownloadAsync(false, _runActive) ?? Task.CompletedTask);
        _restart.Click += (_, _) => _workflow?.RestartNow(_runActive);
        _skip.Click += (_, _) => _workflow?.Skip();
        _close.Click += (_, _) => _workflow?.Dismiss();
    }

    /// <summary>Attaches the workflow and starts its one launch-time check in the background.</summary>
    /// <param name="workflow">The workflow for this copy.</param>
    /// <param name="runActive">Whether the window has a run in flight (UPD-6).</param>
    /// <remarks>
    /// The check is not awaited: the window opens and works whether or not the feed answers,
    /// and the workflow never throws.
    /// </remarks>
    public void Attach(UpdateWorkflow workflow, Func<bool> runActive)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(runActive);

        _workflow = workflow;
        _runActive = runActive;
        workflow.Changed += (_, _) => Dispatcher.UIThread.Post(Refresh);

        _ = Task.Run(() => workflow.CheckAtLaunchAsync());
    }

    /// <summary>Re-reads the workflow, for when whether a run is active has changed.</summary>
    public void Refresh()
    {
        if (_workflow is not { } w)
        {
            return;
        }

        IsVisible = w.IsVisible;
        _message.Text = w.Message;

        var idle = !_runActive();

        _downloadRestart.IsVisible = w.CanDownload;
        _downloadRestart.IsEnabled = idle;
        _download.IsVisible = w.CanDownload;
        _restart.IsVisible = w.CanRestart;
        _restart.IsEnabled = idle;
        _skip.IsVisible = w.CanSkip;
        _close.IsVisible = w.Stage != UpdateStage.Downloading;
    }
}
