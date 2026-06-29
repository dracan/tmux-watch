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

public enum AttentionKind { EnteredWaiting, EnteredIdle }

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

    public TrackedPaneView ToView() =>
        new(Pane, State, EnteredAt, AttentionOutstanding);
}
