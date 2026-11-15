namespace Einzel.Shell.Presentation;

/// <summary>The runs that hold the model for the length of the physics.</summary>
public enum HeavyRun
{
    /// <summary>A watched run filling the viewport as it goes.</summary>
    Watch,

    /// <summary>The results panel running or previewing the model.</summary>
    Results,

    /// <summary>The regime panel walking the model's path.</summary>
    Regime,
}

/// <summary>
/// Admits one long run at a time, and stops the one that can be stopped.
/// </summary>
/// <remarks>
/// <para>
/// <b>One at a time, because each holds a whole solve.</b> A watch, a full run and a regime
/// walk each build the field and step the transport, and two started side by side hold two
/// sets of working buffers for no picture anybody asked for - on a volume model that is the
/// difference between a run that fits and one that does not. The window used to keep a flag
/// per run and enable each button on its own flag alone, so Watch stayed pressable while the
/// results panel ran and the results stayed pressable while a watch ran.
/// </para>
/// <para>
/// <b>Only a watch can be stopped.</b> Its transport asks the window once a step whether a
/// frame is wanted, and that question is where a cancellation is delivered. A full run and a
/// regime walk go through command objects that take no token and ask nothing of the caller
/// while they solve, so there is no point at which one could be told to stop without
/// threading cancellation through the engine. Offering a Stop button for them would be a
/// button that appears to work and does not, so <see cref="CanStop"/> says false for them.
/// </para>
/// <para>
/// Single-threaded by design: begun, stopped and ended on the window's thread, which is what
/// makes plain fields enough.
/// </para>
/// </remarks>
public sealed class RunGate : IDisposable
{
    private CancellationTokenSource? _stopping;
    private bool _disposed;

    /// <summary>Raised when what is running, or whether it can be stopped, changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The run in flight, or null.</summary>
    public HeavyRun? Running { get; private set; }

    /// <summary>Whether a run is in flight.</summary>
    public bool Busy => Running is not null;

    /// <summary>Whether the run in flight can be stopped and has not already been asked to.</summary>
    public bool CanStop => Running == HeavyRun.Watch && _stopping is { IsCancellationRequested: false };

    /// <summary>Whether the run in flight has been asked to stop and has not yet ended.</summary>
    public bool StopRequested => Running is not null && _stopping is { IsCancellationRequested: true };

    /// <summary>What the status line should say between Stop and the run ending, or null.</summary>
    /// <remarks>
    /// <b>Stop is only heard between steps.</b> The watch's transport asks once a step whether
    /// to go on, and the field solve before the first step takes no token at all, so pressing
    /// Stop during the solve changes nothing visible until the solve has finished. Leaving the
    /// line at "solving, then stepping" beside a greyed-out button reads as a Stop that did
    /// nothing; this says what will happen and when, and stays true until the run ends.
    /// </remarks>
    public string? StoppingStatus => StopRequested
        ? "stopping - the run ends when the field solve or the step in progress finishes; "
            + "a solve cannot be interrupted part way, so this can take as long as the solve does"
        : null;

    /// <summary>Starts a run, unless one is already going.</summary>
    /// <param name="run">Which run.</param>
    /// <returns>
    /// A lease to dispose when the run has ended, carrying the token the run should watch; or
    /// null when another run holds the gate, in which case nothing has started.
    /// </returns>
    public RunLease? TryBegin(HeavyRun run)
    {
        if (Busy || _disposed)
        {
            return null;
        }

        Running = run;
        _stopping = new CancellationTokenSource();

        var lease = new RunLease(this, _stopping);

        Changed?.Invoke(this, EventArgs.Empty);

        return lease;
    }

    /// <summary>Asks the run in flight to stop, where it can be.</summary>
    /// <returns>Whether a stop was delivered.</returns>
    public bool Stop()
    {
        if (!CanStop)
        {
            return false;
        }

        _stopping!.Cancel();
        Changed?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>Asks whatever is running to stop, because the window is going away.</summary>
    /// <remarks>
    /// A watch outliving its window would keep stepping a transport nobody can see - hours
    /// of a core on a driven diffusive model. A run or a regime walk cannot be told, and is
    /// left to the process ending: its worker is a background thread, so it does not keep
    /// the process alive once the window has closed.
    /// </remarks>
    public void Abandon() => _stopping?.Cancel();

    /// <summary>Stops what can be stopped, as <see cref="Abandon"/> does, and admits nothing more.</summary>
    public void Dispose()
    {
        _disposed = true;
        Abandon();
    }

    internal void End(CancellationTokenSource stopping)
    {
        if (!ReferenceEquals(stopping, _stopping))
        {
            return;
        }

        _stopping = null;
        Running = null;
        stopping.Dispose();

        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>A run admitted by a <see cref="RunGate"/>, until it is disposed.</summary>
public sealed class RunLease : IDisposable
{
    private readonly RunGate _gate;
    private readonly CancellationTokenSource _stopping;
    private bool _ended;

    internal RunLease(RunGate gate, CancellationTokenSource stopping)
    {
        _gate = gate;
        _stopping = stopping;
        Stopping = stopping.Token;
    }

    /// <summary>Cancelled when somebody asks the run to stop.</summary>
    public CancellationToken Stopping { get; }

    /// <summary>Ends the run, freeing the gate for the next.</summary>
    public void Dispose()
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        _gate.End(_stopping);
    }
}
