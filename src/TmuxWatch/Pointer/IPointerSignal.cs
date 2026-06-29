namespace TmuxWatch.Pointer;

/// <summary>
/// Level-triggered OS pointer cue. Unlike the edge-triggered
/// <see cref="Notifications.INotifier"/>, this reflects the aggregate "any pane
/// waiting" state and is held until cleared. Implementations act only on transitions
/// of the boolean so repeated calls with the same value cost nothing.
/// </summary>
public interface IPointerSignal
{
    /// <summary>Set the waiting pointer when <paramref name="anyWaiting"/> is true,
    /// or restore the normal pointer when false. No-op if already in that state.</summary>
    void SetWaiting(bool anyWaiting);

    /// <summary>Unconditionally restore the normal pointer. Idempotent; safe to call at
    /// startup (self-heal after a crash) and on shutdown.</summary>
    void Restore();
}

/// <summary>
/// Shared transition bookkeeping: tracks whether the waiting pointer is currently
/// applied and routes to the template methods only on edges. Backends implement the
/// two apply hooks; the de-dup logic lives here so it is tested once.
/// </summary>
public abstract class PointerSignalBase : IPointerSignal
{
    private bool _waitingApplied;

    public void SetWaiting(bool anyWaiting)
    {
        if (anyWaiting == _waitingApplied)
            return;

        if (anyWaiting)
            ApplyWaiting();
        else
            ApplyNormal();

        _waitingApplied = anyWaiting;
    }

    public void Restore()
    {
        ApplyNormal();
        _waitingApplied = false;
    }

    /// <summary>Switch the OS pointer to the waiting (red) cursor.</summary>
    protected abstract void ApplyWaiting();

    /// <summary>Restore the user's normal OS pointer.</summary>
    protected abstract void ApplyNormal();
}

/// <summary>No-op backend for when the signal is disabled or the host is unsupported.</summary>
public sealed class NullPointerSignal : IPointerSignal
{
    public void SetWaiting(bool anyWaiting) { }
    public void Restore() { }
}
