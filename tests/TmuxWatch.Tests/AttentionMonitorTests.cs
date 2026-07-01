using TmuxWatch.Config;
using TmuxWatch.Detection;
using TmuxWatch.Discovery;
using TmuxWatch.Monitor;
using TmuxWatch.Notifications;

namespace TmuxWatch.Tests;

public class AttentionMonitorTests
{
    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public void Advance(TimeSpan d) => _now += d;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private const string Working = "────\n ◎ Working esc cancel   Claude Opus 4.8 · 1M context";
    private const string Waiting = "❯ 1. Yes\n↑/↓ to navigate · enter to select · esc to cancel";
    private const string Idle = "❯\n/ commands · ? help · space hold to record   Claude Opus 4.8";

    private static AttentionMonitor Build(FakeTmuxClient fake, out FakeClock clock, WatchConfig? cfg = null)
    {
        cfg ??= new WatchConfig();
        clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var discovery = new PaneDiscovery(fake, cfg);
        return new AttentionMonitor(discovery, fake, cfg, new NullNotifier(), clock);
    }

    [Fact]
    public void Entering_waiting_emits_one_event()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Working;
        var monitor = Build(fake, out var clock);

        var first = monitor.Tick();           // working
        Assert.Empty(first.Events);

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = Waiting;         // transition to waiting
        var second = monitor.Tick();

        Assert.Single(second.Events);
        Assert.Equal(AttentionKind.EnteredWaiting, second.Events[0].Kind);
    }

    [Fact]
    public void Remaining_waiting_does_not_repeat()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Waiting;
        var monitor = Build(fake, out _);

        monitor.Tick();                        // first sight: waiting -> event
        var again = monitor.Tick();            // still waiting -> no event

        Assert.Empty(again.Events);
    }

    [Fact]
    public void Leaving_waiting_clears_outstanding()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Waiting;
        var monitor = Build(fake, out _);

        monitor.Tick();
        fake.Captures["%1"] = Working;
        var snap = monitor.Tick();

        var view = Assert.Single(snap.Panes);
        Assert.False(view.AttentionOutstanding);
        Assert.Equal(PaneState.Working, view.State);
    }

    [Fact]
    public void Paused_working_does_not_emit_attention()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Working;
        var monitor = Build(fake, out _);

        monitor.Tick();
        var again = monitor.Tick();            // identical working frame

        Assert.Empty(again.Events);
    }

    [Fact]
    public void Fresh_idle_pane_is_idle_not_done()
    {
        // First sight has no history, so an already-idle pane is genuine IDLE, not a
        // just-finished turn - and it emits no attention event.
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Idle;
        var monitor = Build(fake, out _);

        var snap = monitor.Tick();

        var view = Assert.Single(snap.Panes);
        Assert.Equal(PaneState.Idle, view.State);
        Assert.Empty(snap.Events);
    }

    [Fact]
    public void Finishing_a_turn_promotes_to_done_and_notifies_once()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Working;
        var monitor = Build(fake, out var clock);

        monitor.Tick();                        // working

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = Idle;            // WORKING -> IDLE is a completed turn
        var done = monitor.Tick();

        var view = Assert.Single(done.Panes);
        Assert.Equal(PaneState.Done, view.State);
        Assert.True(view.AttentionOutstanding);
        Assert.Single(done.Events);
        Assert.Equal(AttentionKind.EnteredDone, done.Events[0].Kind);

        // Still idle on the next poll: stays DONE, no repeat event.
        clock.Advance(TimeSpan.FromSeconds(2));
        var again = monitor.Tick();
        Assert.Equal(PaneState.Done, Assert.Single(again.Panes).State);
        Assert.Empty(again.Events);
    }

    [Fact]
    public void Waiting_to_idle_is_idle_not_done()
    {
        // Answering a prompt with no intervening work returns to IDLE, not DONE.
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Waiting;
        var monitor = Build(fake, out _);

        monitor.Tick();                        // waiting
        fake.Captures["%1"] = Idle;
        var snap = monitor.Tick();

        Assert.Equal(PaneState.Idle, Assert.Single(snap.Panes).State);
    }

    [Fact]
    public void Acknowledging_done_returns_to_idle_and_does_not_repromote()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Working;
        var monitor = Build(fake, out _);

        monitor.Tick();                        // working
        fake.Captures["%1"] = Idle;
        monitor.Tick();                        // -> DONE

        Assert.True(monitor.Acknowledge("%1"));
        // The pane is now IDLE and, staying idle, must not bounce back to DONE.
        var after = monitor.Tick();
        var view = Assert.Single(after.Panes);
        Assert.Equal(PaneState.Idle, view.State);
        Assert.False(view.AttentionOutstanding);
        Assert.Empty(after.Events);
    }

    [Fact]
    public void Acknowledge_is_a_noop_when_pane_is_not_done()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Working;
        var monitor = Build(fake, out _);
        monitor.Tick();                        // working, not done

        Assert.False(monitor.Acknowledge("%1"));
        Assert.False(monitor.Acknowledge("%nonexistent"));
    }

    [Fact]
    public void Re_promotes_to_done_only_after_another_work_cycle()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Working;
        var monitor = Build(fake, out _);

        monitor.Tick();                        // working
        fake.Captures["%1"] = Idle;
        monitor.Tick();                        // -> DONE
        monitor.Acknowledge("%1");             // -> IDLE

        fake.Captures["%1"] = Working;
        monitor.Tick();                        // working again
        fake.Captures["%1"] = Idle;
        var snap = monitor.Tick();             // -> DONE again

        Assert.Equal(PaneState.Done, Assert.Single(snap.Panes).State);
    }

    [Fact]
    public void Classifies_pane_when_command_has_exe_extension()
    {
        // Windows reports "copilot.exe"; the monitor must still classify the
        // captured screen rather than treating the pane as Dead.
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot.exe|0" };
        fake.Captures["%1"] = Waiting;
        var monitor = Build(fake, out _);

        var snap = monitor.Tick();

        var view = Assert.Single(snap.Panes);
        Assert.Equal(PaneState.Waiting, view.State);
    }

    [Fact]
    public void Disappearing_pane_is_dropped()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Working;
        var monitor = Build(fake, out _);
        monitor.Tick();

        fake.ListOutput = "";                  // pane gone
        var snap = monitor.Tick();

        Assert.Empty(snap.Panes);
        Assert.Null(snap.Error);
    }

    [Fact]
    public void Server_failure_keeps_running_with_error()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Working;
        var monitor = Build(fake, out _);
        monitor.Tick();

        fake.Started = false;
        fake.ErrorMessage = "server gone";
        var snap = monitor.Tick();

        Assert.NotNull(snap.Error);
        Assert.Single(snap.Panes);             // prior state retained
    }
}
