namespace Einzel.Shell;

/// <summary>
/// Runs a piece of work one at a time, and folds every request made while it runs into a
/// single run after it.
/// </summary>
/// <param name="work">The work, started once per run.</param>
/// <remarks>
/// <para>
/// <b>For the viewport's redraw, which is a field solve and a transport.</b> Each parameter
/// edit asks for one, and a person editing three parameters in a row would otherwise start
/// three solves side by side, each holding its working buffers, for two pictures nobody will
/// see. Discarding the stale results after they finish keeps the right picture on screen and
/// still pays for all three.
/// </para>
/// <para>
/// <b>The run after the last request is what matters</b>, and it is always made: a request that
/// arrives while work is in flight cannot be answered by that work, which started from the
/// document as it was. So the requests between are folded into one more run rather than
/// dropped, and that run reads the document as it stands by then.
/// </para>
/// <para>
/// <b>Single-threaded by design.</b> Requests are made and continuations run on the UI
/// thread, which is what makes two plain flags enough: nothing else can read them between a
/// check and a set.
/// </para>
/// </remarks>
public sealed class Coalescing(Func<Task> work)
{
    private bool _again;

    /// <summary>Raised when <see cref="Busy"/> changes.</summary>
    public event EventHandler? BusyChanged;

    /// <summary>Whether a run is in flight or owed.</summary>
    public bool Busy { get; private set; }

    /// <summary>Asks for a run.</summary>
    /// <returns>
    /// A task that completes when the runs this request caused have finished, or at once when
    /// the request was folded into one already owed. It faults only when the last run fails.
    /// </returns>
    public async Task RequestAsync()
    {
        if (Busy)
        {
            _again = true;
            return;
        }

        Busy = true;
        BusyChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            do
            {
                _again = false;

                try
                {
                    await work().ConfigureAwait(true);
                }
                catch (Exception) when (_again)
                {
                    // Superseded: a run is owed after this one, from a newer document, and it
                    // reports for itself. Thrown only when nothing is owed, so a failure of
                    // the last run asked for is never lost.
                }
            }
            while (_again);
        }
        finally
        {
            Busy = false;
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
