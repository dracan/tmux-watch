using TmuxWatch.Detection;
using TmuxWatch.Monitor;
using TmuxWatch.Psmux;
using TmuxWatch.Tui;

namespace TmuxWatch.Tests;

public class PauseTests
{
    private static TrackedPaneView View(
        string id, PaneState state = PaneState.Idle, bool focused = false, int enteredSeconds = 0) =>
        new(
            new Pane(id, "s", 0, 0, "copilot", false, id, "",
                WindowActive: focused, PaneActive: focused),
            state,
            DateTimeOffset.UnixEpoch.AddSeconds(enteredSeconds),
            false);

    [Fact]
    public void Toggling_pauses_the_focused_pane()
    {
        var paused = new HashSet<string>();
        var all = new List<TrackedPaneView>
        {
            View("%1"),
            View("%2", focused: true),
        };

        var changed = WatcherApp.ToggleFocusedPause(paused, all);

        Assert.True(changed);
        Assert.Equal(new[] { "%2" }, paused);
    }

    [Fact]
    public void Toggling_an_already_paused_focused_pane_resumes_it()
    {
        var paused = new HashSet<string> { "%2" };
        var all = new List<TrackedPaneView>
        {
            View("%1"),
            View("%2", focused: true),
        };

        var changed = WatcherApp.ToggleFocusedPause(paused, all);

        Assert.True(changed);
        Assert.Empty(paused);
    }

    [Fact]
    public void Toggling_with_no_focused_pane_is_a_noop()
    {
        var paused = new HashSet<string>();
        var all = new List<TrackedPaneView> { View("%1"), View("%2") };

        var changed = WatcherApp.ToggleFocusedPause(paused, all);

        Assert.False(changed);
        Assert.Empty(paused);
    }

    [Fact]
    public void Ordering_sinks_paused_panes_below_active_ones()
    {
        var paused = new HashSet<string> { "%waiting-paused" };
        var panes = new List<TrackedPaneView>
        {
            View("%waiting-paused", PaneState.Waiting),
            View("%idle-active", PaneState.Idle),
        };

        var ordered = WatcherApp.OrderAll(panes, paused);

        // Even though the paused pane is WAITING, it must sit below the active one.
        Assert.Equal(new[] { "%idle-active", "%waiting-paused" }, ordered.Select(v => v.Pane.Id));
    }

    [Fact]
    public void Ordering_keeps_waiting_at_the_top_within_the_active_group()
    {
        var paused = new HashSet<string>();
        var panes = new List<TrackedPaneView>
        {
            View("%idle", PaneState.Idle),
            View("%waiting", PaneState.Waiting),
            View("%working", PaneState.Working),
        };

        var ordered = WatcherApp.OrderAll(panes, paused);

        Assert.Equal(new[] { "%waiting", "%working", "%idle" }, ordered.Select(v => v.Pane.Id));
    }
}
