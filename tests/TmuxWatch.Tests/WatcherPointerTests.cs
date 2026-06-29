using TmuxWatch.Detection;
using TmuxWatch.Monitor;
using TmuxWatch.Tmux;
using TmuxWatch.Tui;

namespace TmuxWatch.Tests;

public class WatcherPointerTests
{
    private static TrackedPaneView View(string id, bool outstanding) =>
        new(
            new Pane(id, "s", 0, 0, "copilot", false, id, "", WindowActive: false, PaneActive: false),
            outstanding ? PaneState.Waiting : PaneState.Working,
            DateTimeOffset.UnixEpoch,
            outstanding);

    [Fact]
    public void No_panes_is_not_active_waiting()
    {
        Assert.False(WatcherApp.AnyActiveWaiting(new List<TrackedPaneView>(), new HashSet<string>()));
    }

    [Fact]
    public void Waiting_unpaused_pane_is_active_waiting()
    {
        var panes = new List<TrackedPaneView> { View("%1", outstanding: true) };
        Assert.True(WatcherApp.AnyActiveWaiting(panes, new HashSet<string>()));
    }

    [Fact]
    public void Paused_waiting_pane_is_excluded()
    {
        var panes = new List<TrackedPaneView> { View("%1", outstanding: true) };
        Assert.False(WatcherApp.AnyActiveWaiting(panes, new HashSet<string> { "%1" }));
    }

    [Fact]
    public void Non_waiting_pane_is_not_active_waiting()
    {
        var panes = new List<TrackedPaneView> { View("%1", outstanding: false) };
        Assert.False(WatcherApp.AnyActiveWaiting(panes, new HashSet<string>()));
    }

    [Fact]
    public void Held_active_while_one_waiting_pane_is_not_paused()
    {
        var panes = new List<TrackedPaneView>
        {
            View("%1", outstanding: true),
            View("%2", outstanding: true),
        };
        // One of the two waiting panes is paused; the other still demands attention.
        Assert.True(WatcherApp.AnyActiveWaiting(panes, new HashSet<string> { "%1" }));
    }

    [Fact]
    public void Cleared_when_all_waiting_panes_are_paused()
    {
        var panes = new List<TrackedPaneView>
        {
            View("%1", outstanding: true),
            View("%2", outstanding: true),
        };
        Assert.False(WatcherApp.AnyActiveWaiting(panes, new HashSet<string> { "%1", "%2" }));
    }
}
