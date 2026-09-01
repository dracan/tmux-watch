using TmuxWatch.Detection;
using TmuxWatch.Tmux;

namespace TmuxWatch.Monitor;

/// <summary>Immutable view of a tracked pane for rendering.</summary>
public sealed record TrackedPaneView(
    Pane Pane,
    PaneState State,
    DateTimeOffset EnteredAt,
    bool AttentionOutstanding)
{
    public TimeSpan TimeInState(DateTimeOffset now) => now - EnteredAt;
}

public enum AttentionKind { EnteredWaiting, EnteredDone }

public sealed record AttentionEvent(Pane Pane, AttentionKind Kind);

/// <summary>
/// Mutable per-pane state held by the monitor between ticks.
/// </summary>
internal sealed class TrackedPane
{
    public required Pane Pane { get; set; }
    public PaneState State { get; set; } = PaneState.Unknown;
    public DateTimeOffset EnteredAt { get; set; }
    public bool AttentionOutstanding { get; set; }

    /// <summary>Consecutive enumerations this pane has been absent from. Reset to 0
    /// whenever it is seen; used to debounce transient disappearances so a pane is not
    /// dropped and re-added as "first sight" on a single empty/partial enumeration.</summary>
    public int MissedEnumerations { get; set; }

    /// <summary>Consecutive captures that classified Unknown. Reset to 0 on any
    /// recognizable classification; once it reaches the configured stale threshold the
    /// held state is abandoned and Unknown is surfaced.</summary>
    public int ConsecutiveUnknowns { get; set; }

    /// <summary>
    /// This pane finished a turn that has not been announced yet, because it ended into a
    /// live background task (WORKING → BACKGND). It is what distinguishes "finished
    /// while you were away" from "has had a dev server up since before the watcher
    /// started", and so what makes either exit from BACKGND worth a chime.
    ///
    /// Starts false, which is how first sight stays silent: a pane discovered already in
    /// BACKGND has no observed completed turn, so neither its shell exiting nor the grace
    /// period expiring announces anything - the same principle that keeps a freshly
    /// discovered IDLE pane out of DONE.
    /// </summary>
    public bool CompletionPending { get; set; }

    /// <summary>
    /// When the pane most recently entered BACKGND, used for the grace-period fallback.
    /// Distinct from <see cref="EnteredAt"/> because that is only assigned after the
    /// promotion decision has been made: on the WORKING → BACKGND tick it still holds the
    /// WORKING entry time, which could satisfy the grace period immediately.
    /// </summary>
    public DateTimeOffset? BackgndSince { get; set; }

    /// <summary>
    /// The BACKGND reason this pane reported on its previous poll, or
    /// <see cref="BackgndReason.None"/> when it was not BACKGND.
    ///
    /// Exists to detect the reason *narrowing* - a sub-agent finishing while a shell it
    /// started keeps running. The grace period does not apply while an agent is among the
    /// outstanding work, so at that moment the shell becomes the only thing left and
    /// deserves a full grace period measured from then, not from the pane's original entry
    /// into BACKGND (which may already be hours past). Cleared wherever
    /// <see cref="BackgndSince"/> is cleared, so a pane re-entering BACKGND is a fresh entry.
    /// </summary>
    public BackgndReason LastBackgndReason { get; set; }

    public TrackedPaneView ToView() =>
        new(Pane, State, EnteredAt, AttentionOutstanding);
}
