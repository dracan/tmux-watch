using TmuxWatch.Detection;
using TmuxWatch.Monitor;
using TmuxWatch.Tmux;
using TmuxWatch.Tui;

namespace TmuxWatch.Tests;

public class OptimisticFocusTests
{
    private static TrackedPaneView View(Pane p) =>
        new(p, PaneState.Idle, DateTimeOffset.UnixEpoch, false);

    private static TrackedPaneView View(string id, PaneState state) =>
        new(
            new Pane(id, "s", 0, 0, "copilot", false, id, "", WindowActive: false, PaneActive: false),
            state,
            DateTimeOffset.UnixEpoch,
            AttentionOutstanding: state is PaneState.Waiting or PaneState.Done);

    [Fact]
    public void Optimistic_ack_clears_only_the_targeted_done_pane()
    {
        var ordered = new List<TrackedPaneView>
        {
            View("%1", PaneState.Done),
            View("%2", PaneState.Done),
        };

        WatcherApp.ApplyOptimisticAck(ordered, "%1");

        Assert.Equal(PaneState.Idle, ordered[0].State);
        Assert.False(ordered[0].AttentionOutstanding);
        // The other DONE pane is untouched - ack is per-pane and keystroke-driven.
        Assert.Equal(PaneState.Done, ordered[1].State);
    }

    [Fact]
    public void Optimistic_ack_is_a_noop_on_a_non_done_pane()
    {
        var ordered = new List<TrackedPaneView> { View("%1", PaneState.Working) };

        WatcherApp.ApplyOptimisticAck(ordered, "%1");

        Assert.Equal(PaneState.Working, ordered[0].State);
    }

    [Fact]
    public void Switching_marks_exactly_one_focused_row()
    {
        var ordered = new List<TrackedPaneView>
        {
            View(new Pane("%1", "s", 0, 0, "copilot", false, "A", "", WindowActive: true, PaneActive: true)),
            View(new Pane("%2", "s", 1, 0, "copilot", false, "B", "", WindowActive: false, PaneActive: true)),
            View(new Pane("%3", "s", 2, 0, "copilot", false, "C", "", WindowActive: false, PaneActive: true)),
        };

        WatcherApp.ApplyOptimisticFocus(ordered, ordered[2].Pane);

        Assert.Equal(new[] { false, false, true }, ordered.Select(v => v.Pane.IsFocused));
    }

    [Fact]
    public void Other_sessions_are_left_untouched()
    {
        var ordered = new List<TrackedPaneView>
        {
            View(new Pane("%1", "s1", 0, 0, "copilot", false, "A", "", WindowActive: true, PaneActive: true)),
            View(new Pane("%9", "s2", 0, 0, "copilot", false, "Z", "", WindowActive: true, PaneActive: true)),
        };

        // Switch focus to %1's window in s1; the s2 pane must be unchanged.
        WatcherApp.ApplyOptimisticFocus(ordered, ordered[0].Pane);

        Assert.True(ordered[0].Pane.IsFocused);
        Assert.True(ordered[1].Pane.IsFocused); // s2 untouched (still its own active window)
    }

    [Fact]
    public void Clears_stale_focus_from_previously_focused_window()
    {
        var ordered = new List<TrackedPaneView>
        {
            View(new Pane("%1", "s", 0, 0, "copilot", false, "A", "", WindowActive: true, PaneActive: true)),
            View(new Pane("%2", "s", 1, 0, "copilot", false, "B", "", WindowActive: false, PaneActive: false)),
        };

        WatcherApp.ApplyOptimisticFocus(ordered, ordered[1].Pane);

        Assert.False(ordered[0].Pane.IsFocused);
        Assert.True(ordered[1].Pane.IsFocused);
    }
}
