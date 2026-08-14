using TmuxWatch.Detection;
using TmuxWatch.Monitor;
using TmuxWatch.Tmux;
using TmuxWatch.Tui;

namespace TmuxWatch.Tests;

/// <summary>
/// The settle time that keeps an acknowledged row still. Acknowledging demotes a pane
/// from DONE to IDLE and resets its in-state timer, so without a hold the row the user
/// just addressed drops four ranks in the same frame as their keystroke - and takes every
/// other row's address key with it.
/// </summary>
public class AckHoldTests
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static TrackedPaneView View(
        string id, PaneState state, int enteredSeconds = 0) =>
        new(
            new Pane(id, "s", 0, 0, "copilot", false, id, ""),
            state,
            T0.AddSeconds(enteredSeconds),
            false);

    /// <summary>A hold pinning <paramref name="state"/>'s rank, expiring at 5s.</summary>
    private static AckHold Hold(PaneState state, int enteredSeconds = 0) =>
        new(WatcherApp.Priority(state), T0.AddSeconds(enteredSeconds), T0.AddSeconds(5));

    [Fact]
    public void Held_pane_sorts_by_its_pinned_rank_not_its_current_state()
    {
        // The pane is IDLE now - it was DONE when acknowledged a moment ago.
        var panes = new List<TrackedPaneView>
        {
            View("%working", PaneState.Working),
            View("%acked", PaneState.Idle),
        };
        var holds = new Dictionary<string, AckHold> { ["%acked"] = Hold(PaneState.Done) };

        var ordered = WatcherApp.OrderAll(panes, None, holds);

        // Without the hold, IDLE would sink below WORKING.
        Assert.Equal(new[] { "%acked", "%working" }, ordered.Select(v => v.Pane.Id));
    }

    [Fact]
    public void Unheld_pane_still_sorts_by_its_current_state()
    {
        var panes = new List<TrackedPaneView>
        {
            View("%idle", PaneState.Idle),
            View("%working", PaneState.Working),
        };

        var ordered = WatcherApp.OrderAll(panes, None, new Dictionary<string, AckHold>());

        Assert.Equal(new[] { "%working", "%idle" }, ordered.Select(v => v.Pane.Id));
    }

    [Fact]
    public void Held_pane_keeps_its_place_among_panes_of_the_same_rank()
    {
        // Acknowledging resets EnteredAt to now, and ties break on it descending - so
        // pinning the rank alone would still float this row to the top of the DONE group.
        var panes = new List<TrackedPaneView>
        {
            // Reset by the ack to "just now", well after the other two entered DONE.
            View("%acked", PaneState.Idle, enteredSeconds: 900),
            View("%newer-done", PaneState.Done, enteredSeconds: 200),
            View("%older-done", PaneState.Done, enteredSeconds: 100),
        };
        var holds = new Dictionary<string, AckHold>
        {
            // Its real, pre-ack entry time sits between the other two.
            ["%acked"] = Hold(PaneState.Done, enteredSeconds: 150),
        };

        var ordered = WatcherApp.OrderAll(panes, None, holds);

        Assert.Equal(
            new[] { "%newer-done", "%acked", "%older-done" }, ordered.Select(v => v.Pane.Id));
    }

    [Fact]
    public void Hold_survives_a_state_change_to_working()
    {
        // You jumped in and typed; the pane is genuinely WORKING now. It still must not
        // move until the deadline - a move at 1.5s is the same double take.
        var panes = new List<TrackedPaneView>
        {
            View("%acked", PaneState.Working),
            View("%backgnd", PaneState.Backgnd),
        };
        var holds = new Dictionary<string, AckHold> { ["%acked"] = Hold(PaneState.Done) };

        var ordered = WatcherApp.OrderAll(panes, None, holds);

        Assert.Equal(new[] { "%acked", "%backgnd" }, ordered.Select(v => v.Pane.Id));
    }

    [Fact]
    public void Hold_survives_a_promotion_to_waiting()
    {
        // The hold is absolute in both directions. WAITING sits one rank above DONE, so
        // the cost is a single slot, and the chime and pointer cue are state-driven and
        // fire regardless.
        var panes = new List<TrackedPaneView>
        {
            View("%acked", PaneState.Waiting),
            View("%other-waiting", PaneState.Waiting, enteredSeconds: 10),
        };
        var holds = new Dictionary<string, AckHold> { ["%acked"] = Hold(PaneState.Done) };

        var ordered = WatcherApp.OrderAll(panes, None, holds);

        Assert.Equal(new[] { "%other-waiting", "%acked" }, ordered.Select(v => v.Pane.Id));
    }

    [Fact]
    public void Recording_captures_the_pre_ack_rank_and_entry_time()
    {
        var holds = new Dictionary<string, AckHold>();

        WatcherApp.RecordAckHold(
            holds, View("%1", PaneState.Done, enteredSeconds: 30), holdSeconds: 5, now: T0);

        var hold = holds["%1"];
        Assert.Equal(WatcherApp.Priority(PaneState.Done), hold.Priority);
        Assert.Equal(T0.AddSeconds(30), hold.EnteredAt);
        Assert.Equal(T0.AddSeconds(5), hold.ReleaseAt);
    }

    [Fact]
    public void Recording_is_a_noop_when_the_hold_is_configured_off()
    {
        var holds = new Dictionary<string, AckHold>();

        WatcherApp.RecordAckHold(
            holds, View("%1", PaneState.Done), holdSeconds: 0, now: T0);

        Assert.Empty(holds);
    }

    [Fact]
    public void Re_acknowledging_replaces_the_hold_rather_than_stacking()
    {
        var holds = new Dictionary<string, AckHold> { ["%1"] = Hold(PaneState.Done) };

        WatcherApp.RecordAckHold(
            holds, View("%1", PaneState.Done, enteredSeconds: 60),
            holdSeconds: 5, now: T0.AddSeconds(60));

        Assert.Equal(T0.AddSeconds(65), Assert.Single(holds).Value.ReleaseAt);
    }

    [Fact]
    public void Expiry_drops_a_hold_at_its_deadline_and_keeps_one_before_it()
    {
        var holds = new Dictionary<string, AckHold>
        {
            ["%expired"] = new(0, T0, T0.AddSeconds(5)),
            ["%live"] = new(0, T0, T0.AddSeconds(9)),
        };

        WatcherApp.ExpireHolds(holds, new[] { "%expired", "%live" }, T0.AddSeconds(6));

        Assert.Equal(new[] { "%live" }, holds.Keys);
    }

    [Fact]
    public void Expiry_drops_a_hold_for_a_pane_that_is_no_longer_there()
    {
        var holds = new Dictionary<string, AckHold> { ["%closed"] = Hold(PaneState.Done) };

        // Deadline has not passed; the pane has simply gone.
        WatcherApp.ExpireHolds(holds, Array.Empty<string>(), T0.AddSeconds(1));

        Assert.Empty(holds);
    }

    [Fact]
    public void Expiry_leaves_a_hold_alone_before_its_deadline()
    {
        var holds = new Dictionary<string, AckHold> { ["%1"] = Hold(PaneState.Done) };

        WatcherApp.ExpireHolds(holds, new[] { "%1" }, T0.AddSeconds(4));

        Assert.Equal(new[] { "%1" }, holds.Keys);
    }

    [Fact]
    public void Pausing_releases_the_hold()
    {
        var holds = new Dictionary<string, AckHold> { ["%1"] = Hold(PaneState.Done) };

        WatcherApp.TogglePause(new HashSet<string>(), "%1", holds);

        Assert.Empty(holds);
    }

    [Fact]
    public void Resuming_a_row_also_leaves_no_hold_behind()
    {
        var holds = new Dictionary<string, AckHold> { ["%1"] = Hold(PaneState.Done) };

        WatcherApp.TogglePause(new HashSet<string> { "%1" }, "%1", holds);

        Assert.Empty(holds);
    }

    [Fact]
    public void Held_pane_still_sinks_when_paused()
    {
        // Pausing releases the hold at its key handler, but the ordering rule itself also
        // keeps paused below active - the hold never outranks that.
        var panes = new List<TrackedPaneView>
        {
            View("%acked", PaneState.Idle),
            View("%idle", PaneState.Idle, enteredSeconds: 10),
        };
        var holds = new Dictionary<string, AckHold> { ["%acked"] = Hold(PaneState.Done) };

        var ordered = WatcherApp.OrderAll(panes, new HashSet<string> { "%acked" }, holds);

        Assert.Equal(new[] { "%idle", "%acked" }, ordered.Select(v => v.Pane.Id));
    }

    [Fact]
    public void Layout_applies_holds_to_the_agent_table()
    {
        var holds = new Dictionary<string, AckHold> { ["%acked"] = Hold(PaneState.Done) };

        var layout = WatcherApp.BuildLayout(
            new[] { View("%working", PaneState.Working), View("%acked", PaneState.Idle) },
            Array.Empty<Pane>(),
            None,
            showOtherPanes: true,
            showCompanionPanes: true,
            ackHolds: holds);

        // Address keys are positional, so this is also what keeps them stable.
        Assert.Equal(new[] { "%acked", "%working" }, layout.All.Select(r => r.Id));
    }
}
