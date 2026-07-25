using TmuxWatch.Detection;
using TmuxWatch.Monitor;
using TmuxWatch.Tmux;
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

    private static Pane Other(string id, int window = 9, int pane = 0, string command = "bash") =>
        new(id, "s", window, pane, command, false, $"win{window}", "");

    [Fact]
    public void Toggling_pauses_the_highlighted_pane()
    {
        var paused = new HashSet<string>();

        var changed = WatcherApp.TogglePause(paused, "%2");

        Assert.True(changed);
        Assert.Equal(new[] { "%2" }, paused);
    }

    [Fact]
    public void Toggling_an_already_paused_pane_resumes_it()
    {
        var paused = new HashSet<string> { "%2" };

        var changed = WatcherApp.TogglePause(paused, "%2");

        Assert.True(changed);
        Assert.Empty(paused);
    }

    [Fact]
    public void Toggling_with_nothing_highlighted_is_a_noop()
    {
        var paused = new HashSet<string>();

        var changed = WatcherApp.TogglePause(paused, null);

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

    [Fact]
    public void Paused_agent_pane_moves_into_the_paused_table()
    {
        var layout = WatcherApp.BuildLayout(
            new[] { View("%1"), View("%2") },
            Array.Empty<Pane>(),
            new HashSet<string> { "%2" },
            showOtherPanes: true,
            showCompanionPanes: true);

        Assert.Equal(new[] { "%1" }, layout.Agent.Select(r => r.Id));
        Assert.Equal(new[] { "%2" }, layout.Paused.Select(r => r.Id));
    }

    [Fact]
    public void Paused_non_agent_pane_moves_into_the_paused_table()
    {
        var layout = WatcherApp.BuildLayout(
            new[] { View("%1") },
            new[] { Other("%7"), Other("%8", window: 10) },
            new HashSet<string> { "%7" },
            showOtherPanes: true,
            showCompanionPanes: true);

        Assert.Equal(new[] { "%8" }, layout.Other.Select(r => r.Id));
        Assert.Equal(new[] { "%7" }, layout.Paused.Select(r => r.Id));
        // The paused non-agent row is still a non-agent row: no classified state.
        Assert.False(layout.Paused[0].IsAgent);
    }

    [Fact]
    public void Paused_table_holds_agent_rows_before_non_agent_rows()
    {
        var layout = WatcherApp.BuildLayout(
            new[] { View("%1"), View("%2") },
            new[] { Other("%7") },
            new HashSet<string> { "%2", "%7" },
            showOtherPanes: true,
            showCompanionPanes: true);

        Assert.Equal(new[] { "%2", "%7" }, layout.Paused.Select(r => r.Id));
    }

    [Fact]
    public void Hiding_other_panes_also_hides_their_paused_rows()
    {
        var layout = WatcherApp.BuildLayout(
            new[] { View("%1") },
            new[] { Other("%7") },
            new HashSet<string> { "%7" },
            showOtherPanes: false,
            showCompanionPanes: true);

        Assert.Empty(layout.Other);
        Assert.Empty(layout.Paused);
    }
}
