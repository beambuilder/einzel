using System.Globalization;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using Einzel.Commands;
using Einzel.Core.Results;

namespace Einzel.Shell;

/// <summary>The shell's window.</summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Opens the window, on a model if one was named.</summary>
    /// <param name="modelPath">The model document to open, or null for an empty shell.</param>
    public MainWindow(string? modelPath = null)
    {
        InitializeComponent();
        Load(modelPath);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Runs the model's transport, filling the viewport as it goes.</summary>
    /// <remarks>
    /// <para>
    /// <b>On a background thread, because the run is the length of the physics.</b> A TIMS
    /// elution is twenty minutes; a window that computed it on the UI thread would be a
    /// window that stopped responding for twenty minutes, which is exactly the state this
    /// button exists to replace.
    /// </para>
    /// <para>
    /// <b>A trajectory model is refused rather than watched</b>, by the command layer and
    /// with a reason: a flight finishes faster than a viewport could draw it part way
    /// through, so the whole bundle arriving at once is sooner than the first frame of a
    /// watch would be. The refusal is shown rather than swallowed - a button that appears
    /// to do nothing is worse than one that says why it did not.
    /// </para>
    /// </remarks>
    private static void Watch(string modelPath, SceneView view, Button button, TextBlock status)
    {
        button.IsEnabled = false;
        status.Text = "einzel run " + modelPath + " --progress 5   ·   solving, then stepping";

        var watcher = new Watcher(
            frame => Dispatcher.UIThread.Post(() => view.Show(frame)),
            TimeSpan.FromMilliseconds(400));

        _ = Task.Run(() =>
        {
            try
            {
                var final = ViewportCommand.Watch(modelPath, watcher);

                Dispatcher.UIThread.Post(() =>
                {
                    // The last frame that held a packet, not the last frame: a run whose
                    // ions all arrived ends with an empty box.
                    var shown = watcher.LastWithPacket ?? final;

                    if (watcher.LastWithPacket is { } held)
                    {
                        view.Show(held);
                    }

                    // Counted off the frame ACTUALLY SHOWN rather than off the final
                    // bundle. A run whose ions arrived ends empty, so reading the count
                    // from `final` printed "0 density contours" over a viewport displaying
                    // five - a number describing something other than the picture beside it.
                    var drawn = shown.Density.Count > 0
                        ? string.Create(
                            CultureInfo.InvariantCulture,
                            $"{shown.Density.Count} density contours at the last frame with a packet")
                        : "no packet remained at any frame";

                    status.Text = "run finished · " + drawn + Worst(final.Warnings);

                    button.IsEnabled = true;
                });
            }
            catch (Exception error)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    status.Text = "Cannot watch this run: " + error.Message;
                    button.IsEnabled = true;
                });
            }
        });
    }

    /// <summary>
    /// Builds the scene through the command layer and hands it to the viewport.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>UI-1 is the whole of this method.</b> The window does not parse the document, does
    /// not extract a conductor surface and does not fly an ion: it calls
    /// <see cref="ViewportCommand.Execute(string, int, double?)"/> and draws what comes
    /// back. A viewport that integrated its own trajectories would be a second transport
    /// implementation, which is how <c>run</c> and <c>test</c> came to disagree twice.
    /// </para>
    /// <para>
    /// <b>Amendment 25</b>: the action is the CLI invocation a person could have typed, and
    /// it is shown rather than merely honoured - so what the window did is legible to the
    /// agent that takes the session over.
    /// </para>
    /// </remarks>
    private void Load(string? modelPath)
    {
        var heading = this.FindControl<TextBlock>("Heading")!;
        var status = this.FindControl<TextBlock>("Status")!;
        var host = this.FindControl<Panel>("ViewportHost")!;

        if (string.IsNullOrWhiteSpace(modelPath))
        {
            heading.Text = "Einzel";
            status.Text = "No model. Start the shell with a model document to draw one.";
            return;
        }

        // Off the UI thread, for the reason `Watch` gives twelve lines above: this solves
        // the geometry and flies the bundle, which is nine seconds on the shipped C-trap and
        // minutes on a volume model. Doing it in the constructor showed no window at all
        // until it finished - the window that exists to replace waiting, spent waiting.
        heading.Text = modelPath;
        status.Text = "einzel render section " + modelPath + "   ·   solving";

        _ = Task.Run(() =>
        {
            try
            {
                var outcome = ViewportCommand.Execute(modelPath);

                // Gathered here rather than on the UI thread because it compiles the
                // document too, and a failed outline must not cost the picture.
                IReadOnlyList<ParameterRow> rows;

                try
                {
                    rows = [.. OutlineCommand.Execute(modelPath).Parameters.Select(ParameterRow.From)];
                }
                catch (Exception outlineFailed)
                {
                    rows = [new ParameterRow(
                        "could not read the surface", outlineFailed.Message, string.Empty, null)];
                }

                Dispatcher.UIThread.Post(() => Draw(modelPath, outcome, rows, heading, status, host));
            }
            catch (Exception error)
            {
                // Named rather than swallowed. A window that opens empty because the model
                // was refused looks exactly like a window that opened empty because the
                // renderer failed, and only one of those is the reader's to fix.
                Dispatcher.UIThread.Post(() =>
                    status.Text = "Could not open this model: " + error.Message);
            }
        });
    }

    /// <summary>Puts a measured scene into the window.</summary>
    /// <param name="modelPath">The model document that was drawn.</param>
    /// <param name="outcome">What the command layer produced.</param>
    /// <param name="rows">The model's declared parameter surface.</param>
    /// <param name="heading">The title line.</param>
    /// <param name="status">The status line.</param>
    /// <param name="host">Where the viewport goes.</param>
    /// <remarks>
    /// On the UI thread, and separate from the measuring so that the split between what is
    /// computed and what is displayed is visible rather than implied.
    /// </remarks>
    private void Draw(
        string modelPath,
        ViewportOutcome outcome,
        IReadOnlyList<ParameterRow> rows,
        TextBlock heading,
        TextBlock status,
        Panel host)
    {
        var view = new SceneView { Scene = outcome };
        host.Children.Add(view);

        // Over the viewport, so it is never hidden by the instrument and never takes a click.
        host.Children.Add(new ScaleOverlay(view));

        // The model's own knobs, through the command that exists so the window need not
        // parse the document. An outline that fails becomes one row carrying its reason
        // rather than taking the window down - the picture is still worth having, and an
        // empty list would be indistinguishable from a model that declares no parameters.
        this.FindControl<ItemsControl>("Parameters")!.ItemsSource = rows;

        // Amendment 25: each of these is a camera, not a capability, so none of them needs
        // a command spelling - what a named view changes is where somebody is standing.
        // From the command layer's table, so the window and `einzel render still --view`
        // mean the same thing by each name.
        Named("ViewIso", ViewportPicture.Views["iso"]);
        Named("ViewSide", ViewportPicture.Views["side"]);
        Named("ViewTop", ViewportPicture.Views["top"]);
        Named("ViewFront", ViewportPicture.Views["front"]);

        var transparent = this.FindControl<ToggleButton>("Transparent")!;
        transparent.IsCheckedChanged += (_, _) =>
            view.ConductorOpacity = transparent.IsChecked == true ? 0.35 : 1.0;

        var watch = this.FindControl<Button>("WatchRun")!;
        watch.Click += (_, _) => Watch(modelPath, view, watch, status);

        void Named(string name, (double Azimuth, double Elevation) at) =>
            this.FindControl<Button>(name)!.Click += (_, _) => view.View = at;

        heading.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{outcome.ModelPath} — {outcome.ElectrodeCount()} electrodes, {outcome.Trajectories.Count} trajectories");

        // RND-8 is the transport mode's answer carried through, not a decision taken here:
        // a diffusive model produces no trajectories, and saying so is different from
        // having drawn none.
        var paths = outcome.ProducesTrajectories
            ? string.Create(CultureInfo.InvariantCulture, $"{outcome.Trajectories.Count} flights drawn")
            : "this model computes a density, so it has no trajectories to draw";

        status.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"einzel render section {modelPath}   ·   {paths}")
            + Worst(outcome.Warnings);
    }

    /// <summary>The validity violations on a result, ready to append to a status line.</summary>
    /// <param name="warnings">Everything the command layer reported.</param>
    /// <returns>The codes, or an empty string where there are none.</returns>
    /// <remarks>
    /// <para>
    /// <b>GRD-2 reaches the window or it does not hold.</b> A warning that survives the
    /// engine, the command layer and the result record and then stops at the last surface a
    /// person actually reads has been dropped, and this project has dropped evidence at a
    /// seam seven times. The watch path had its own line and showed none of them.
    /// </para>
    /// <para>
    /// Violations only, because that is the class GRD-3 makes unsuppressible; a status bar
    /// carrying every advisory teaches the reader to skim the one kind that must never be
    /// skimmed - the same argument that keeps the report's hatched band off everything but
    /// a violation.
    /// </para>
    /// </remarks>
    private static string Worst(IReadOnlyList<ValidityWarning> warnings)
    {
        var codes = warnings
            .Where(w => w.Severity == WarningSeverity.ValidityViolation)
            .Select(w => w.Code)
            .ToList();

        return codes.Count > 0 ? "   ·   " + string.Join(", ", codes) : string.Empty;
    }
}
