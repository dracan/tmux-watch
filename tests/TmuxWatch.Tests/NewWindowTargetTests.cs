using TmuxWatch.Config;
using TmuxWatch.Detection;
using TmuxWatch.Discovery;
using TmuxWatch.Monitor;
using TmuxWatch.Notifications;
using TmuxWatch.Tmux;
using TmuxWatch.Tui;

namespace TmuxWatch.Tests;

/// <summary>
/// The new-window action targets a <em>session</em>, taken from the focus marker rather
/// than the highlighted row. Because tmux tracks the active window and pane per session,
/// several rows can carry the marker at once, so the tie-break is pinned here.
/// </summary>
public class NewWindowTargetTests
{
    private static readonly HashSet<string> None = new();

    private static TrackedPaneView Agent(
        string id, string session, int window = 0, bool focused = false) =>
        new(
            new Pane(id, session, window, 0, "claude", false, $"w{window}", "",
                WindowActive: focused, PaneActive: focused),
            PaneState.Idle,
            DateTimeOffset.UnixEpoch,
            false);

    private static Pane Other(string id, string session, int window = 0, bool focused = false) =>
        new(id, session, window, 0, "bash", false, $"w{window}", "",
            WindowActive: focused, PaneActive: focused);

    private static IReadOnlyList<WatchRow> Layout(
        IEnumerable<TrackedPaneView>? agents = null, IEnumerable<Pane>? others = null) =>
        WatcherApp.BuildLayout(
            (agents ?? Array.Empty<TrackedPaneView>()).ToList(),
            (others ?? Array.Empty<Pane>()).ToList(),
            None,
            showOtherPanes: true,
            showCompanionPanes: true).All;

    [Fact]
    public void Targets_the_focused_panes_session()
    {
        var rows = Layout(new[]
        {
            Agent("%1", "work", window: 0),
            Agent("%2", "work", window: 1, focused: true),
        });

        Assert.Equal("work", WatcherApp.ResolveTargetSession(rows, "%1"));
    }

    [Fact]
    public void Targets_the_focus_marker_not_the_highlighted_row()
    {
        // The highlighted row is in "dotfiles"; the marker is in "work".
        var rows = Layout(new[]
        {
            Agent("%1", "dotfiles"),
            Agent("%2", "work", focused: true),
        });

        Assert.Equal("work", WatcherApp.ResolveTargetSession(rows, "%1"));
    }

    [Fact]
    public void Several_marked_panes_break_toward_the_highlighted_rows_session()
    {
        // Each session has its own active window and pane, so both rows carry the marker.
        var rows = Layout(new[]
        {
            Agent("%1", "alpha", focused: true),
            Agent("%2", "beta", focused: true),
        });

        Assert.Equal("beta", WatcherApp.ResolveTargetSession(rows, "%2"));
        Assert.Equal("alpha", WatcherApp.ResolveTargetSession(rows, "%1"));
    }

    [Fact]
    public void Several_marked_panes_are_deterministic_when_the_highlight_matches_neither()
    {
        var rows = Layout(
            agents: new[] { Agent("%1", "alpha", focused: true), Agent("%2", "beta", focused: true) },
            others: new[] { Other("%3", "gamma") });

        // Highlighting the unmarked "gamma" row leaves no preferred session; the first
        // marked row in render order wins rather than an arbitrary one.
        Assert.Equal("alpha", WatcherApp.ResolveTargetSession(rows, "%3"));
    }

    [Fact]
    public void Falls_back_to_the_highlighted_row_when_nothing_is_marked()
    {
        var rows = Layout(new[] { Agent("%1", "alpha"), Agent("%2", "beta") });

        Assert.Equal("beta", WatcherApp.ResolveTargetSession(rows, "%2"));
    }

    [Fact]
    public void Falls_back_to_the_first_row_when_the_highlight_is_stale()
    {
        var rows = Layout(new[] { Agent("%1", "alpha"), Agent("%2", "beta") });

        Assert.Equal("alpha", WatcherApp.ResolveTargetSession(rows, "%gone"));
    }

    [Fact]
    public void No_rows_means_no_target()
    {
        Assert.Null(WatcherApp.ResolveTargetSession(Layout(), highlightedId: null));
    }

    [Fact]
    public void A_non_agent_row_can_supply_the_target()
    {
        // The marker is on a plain shell pane; it still names a session.
        var rows = Layout(
            agents: new[] { Agent("%1", "alpha") },
            others: new[] { Other("%2", "beta", focused: true) });

        Assert.Equal("beta", WatcherApp.ResolveTargetSession(rows, "%1"));
    }

    [Fact]
    public void Create_targets_the_session_and_then_jumps_to_the_new_window()
    {
        var tmux = new FakeTmuxClient { NewWindowId = "@7" };

        var error = WatcherApp.CreateAndJump(tmux, "work", "scratch");

        Assert.Null(error);
        Assert.Equal(("work", "scratch"), tmux.CreatedWindows.Single());
        // Focus-only verbs, and the window is selected by the id tmux reported - a
        // detached create leaves it non-current, so the session switch alone would land
        // on whichever window the session had before.
        Assert.Equal(new[] { "work" }, tmux.SwitchedSessions);
        Assert.Equal(new[] { "@7" }, tmux.SelectedWindows);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_name_is_sent_as_null_so_tmux_names_the_window(string name)
    {
        var tmux = new FakeTmuxClient();

        WatcherApp.CreateAndJump(tmux, "work", name);

        Assert.Equal(("work", (string?)null), tmux.CreatedWindows.Single());
    }

    [Fact]
    public void A_failed_create_surfaces_an_error_and_does_not_jump()
    {
        var tmux = new FakeTmuxClient { NewWindowFails = true };

        var error = WatcherApp.CreateAndJump(tmux, "work", "scratch");

        Assert.Equal("can't create window", error);
        Assert.Empty(tmux.SwitchedSessions);
        Assert.Empty(tmux.SelectedWindows);
    }

    [Fact]
    public void A_host_that_reports_no_window_id_still_switches_session()
    {
        var tmux = new FakeTmuxClient { NewWindowId = "" };

        Assert.Null(WatcherApp.CreateAndJump(tmux, "work", "scratch"));
        Assert.Equal(new[] { "work" }, tmux.SwitchedSessions);
        Assert.Empty(tmux.SelectedWindows);
    }

    [Fact]
    public void Creating_never_sends_input_or_captures()
    {
        var tmux = new FakeTmuxClient();

        WatcherApp.CreateAndJump(tmux, "work", "scratch");

        // The inviolable tier: a create touches no pane's content.
        Assert.Empty(tmux.CapturedPanes);
        Assert.Empty(tmux.SelectedPanes);
    }

    [Fact]
    public void Polling_never_reaches_a_lifecycle_verb()
    {
        // The lifecycle tier is keystroke-driven only: no amount of polling, classifying,
        // or finding dead panes may create, rename, or destroy anything.
        var fake = new FakeTmuxClient
        {
            ListOutput = "%1|s|0|0|copilot|0\n%2|s|0|1|bash|0\n%3|s|1|0|claude|1",
        };
        var cfg = new WatchConfig();
        var monitor = new AttentionMonitor(
            new PaneDiscovery(fake, cfg, selfPaneId: ""), fake, cfg, new NullNotifier());

        for (var i = 0; i < 5; i++)
            monitor.Tick();

        Assert.Empty(fake.CreatedWindows);
        Assert.Empty(fake.SwitchedSessions);
        Assert.Empty(fake.SelectedWindows);
    }

    [Fact]
    public void Stale_focus_markers_clear_in_the_session_that_gained_a_window()
    {
        var agents = new List<TrackedPaneView>
        {
            Agent("%1", "work", focused: true),
            Agent("%2", "other", focused: true),
        };
        var others = new List<Pane> { Other("%3", "work", focused: true) };

        WatcherApp.ClearFocusMarker(agents, "work");
        WatcherApp.ClearFocusMarker(others, "work");

        // Focus moved to a window that has no row yet, so nothing in "work" may still
        // claim the marker - but another session's marker is left alone.
        Assert.False(agents[0].Pane.IsFocused);
        Assert.True(agents[1].Pane.IsFocused);
        Assert.False(others[0].IsFocused);
    }
}
