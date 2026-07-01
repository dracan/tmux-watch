namespace TmuxWatch.Pointer;

/// <summary>
/// Aggregate pointer cue the OS pointer should show. WAITING (red) outranks DONE
/// (green): the watcher computes the aggregate over non-paused panes and picks the
/// most urgent.
/// </summary>
public enum PointerState { Normal, Waiting, Done }

/// <summary>
/// Level-triggered OS pointer cue. Unlike the edge-triggered
/// <see cref="Notifications.INotifier"/>, this reflects an aggregate pane state
/// (any WAITING, else any DONE, else normal) and is held until it changes.
/// Implementations act only on transitions of the value so repeated calls with the
/// same state cost nothing.
/// </summary>
public interface IPointerSignal
{
    /// <summary>Drive the pointer to the given aggregate state (red for Waiting, green
    /// for Done, normal otherwise). No-op if already in that state.</summary>
    void SetState(PointerState state);

    /// <summary>Unconditionally restore the normal pointer. Idempotent; safe to call at
    /// startup (self-heal after a crash) and on shutdown.</summary>
    void Restore();
}

/// <summary>
/// Shared transition bookkeeping: tracks which pointer state is currently applied and
/// routes to the template methods only on edges. Backends implement the apply hooks;
/// the de-dup logic lives here so it is tested once.
/// </summary>
public abstract class PointerSignalBase : IPointerSignal
{
    private PointerState _applied = PointerState.Normal;

    public void SetState(PointerState state)
    {
        if (state == _applied)
            return;

        switch (state)
        {
            case PointerState.Waiting: ApplyWaiting(); break;
            case PointerState.Done: ApplyDone(); break;
            default: ApplyNormal(); break;
        }

        _applied = state;
    }

    public void Restore()
    {
        ApplyNormal();
        _applied = PointerState.Normal;
    }

    /// <summary>Switch the OS pointer to the waiting (red) cursor.</summary>
    protected abstract void ApplyWaiting();

    /// <summary>Switch the OS pointer to the done (green) cursor.</summary>
    protected abstract void ApplyDone();

    /// <summary>Restore the user's normal OS pointer.</summary>
    protected abstract void ApplyNormal();
}

/// <summary>No-op backend for when the signal is disabled or the host is unsupported.</summary>
public sealed class NullPointerSignal : IPointerSignal
{
    public void SetState(PointerState state) { }
    public void Restore() { }
}
