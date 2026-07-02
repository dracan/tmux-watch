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

    public TrackedPaneView ToView() =>
        new(Pane, State, EnteredAt, AttentionOutstanding);
}
