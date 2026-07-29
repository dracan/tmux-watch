using TmuxWatch.Detection;
using TmuxWatch.Monitor;
using TmuxWatch.Pointer;
using TmuxWatch.Tmux;
using TmuxWatch.Tui;

namespace TmuxWatch.Tests;

public class WatcherPointerTests
{
    private static TrackedPaneView View(string id, PaneState state) =>
        new(
            new Pane(id, "s", 0, 0, "copilot", false, id, "", WindowActive: false, PaneActive: false),
            state,
            DateTimeOffset.UnixEpoch,
            AttentionOutstanding: state is PaneState.Waiting or PaneState.Done);

    private static PointerState Aggregate(IReadOnlyList<TrackedPaneView> panes, params string[] paused) =>
        WatcherApp.AggregatePointerState(panes, new HashSet<string>(paused));

    [Fact]
    public void No_panes_is_normal()
    {
        Assert.Equal(PointerState.Normal, Aggregate(new List<TrackedPaneView>()));
    }

    [Fact]
    public void Waiting_unpaused_pane_is_red()
    {
        var panes = new List<TrackedPaneView> { View("%1", PaneState.Waiting) };
        Assert.Equal(PointerState.Waiting, Aggregate(panes));
    }

    [Fact]
    public void Done_unpaused_pane_is_green()
    {
        var panes = new List<TrackedPaneView> { View("%1", PaneState.Done) };
        Assert.Equal(PointerState.Done, Aggregate(panes));
    }

    [Fact]
    public void Waiting_takes_precedence_over_done()
    {
        var panes = new List<TrackedPaneView>
        {
            View("%1", PaneState.Done),
            View("%2", PaneState.Waiting),
        };
        Assert.Equal(PointerState.Waiting, Aggregate(panes));
    }

    [Fact]
    public void Done_remains_green_when_waiting_pane_is_paused()
    {
        var panes = new List<TrackedPaneView>
        {
            View("%1", PaneState.Waiting),
            View("%2", PaneState.Done),
        };
        // The only waiting pane is parked, so the green (done) cue takes over.
        Assert.Equal(PointerState.Done, Aggregate(panes, "%1"));
    }

    [Fact]
    public void Paused_waiting_pane_alone_is_normal()
    {
        var panes = new List<TrackedPaneView> { View("%1", PaneState.Waiting) };
        Assert.Equal(PointerState.Normal, Aggregate(panes, "%1"));
    }

    [Fact]
    public void Paused_done_pane_alone_is_normal()
    {
        var panes = new List<TrackedPaneView> { View("%1", PaneState.Done) };
        Assert.Equal(PointerState.Normal, Aggregate(panes, "%1"));
    }

    [Fact]
    public void Working_and_idle_panes_are_normal()
    {
        var panes = new List<TrackedPaneView>
        {
            View("%1", PaneState.Working),
            View("%2", PaneState.Idle),
        };
        Assert.Equal(PointerState.Normal, Aggregate(panes));
    }

    [Fact]
    public void Backgnd_pane_does_not_arm_the_cue()
    {
        // A pane holding a background shell has not asked for the user yet; it will
        // announce itself as DONE when it has.
        var panes = new List<TrackedPaneView> { View("%1", PaneState.Backgnd) };
        Assert.Equal(PointerState.Normal, Aggregate(panes));
    }
}
