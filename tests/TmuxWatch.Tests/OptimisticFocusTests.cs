using TmuxWatch.Detection;
using TmuxWatch.Monitor;
using TmuxWatch.Tmux;
using TmuxWatch.Tui;

namespace TmuxWatch.Tests;

public class OptimisticFocusTests
{
    private static TrackedPaneView View(Pane p) =>
        new(p, PaneState.Idle, DateTimeOffset.UnixEpoch, false);

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
