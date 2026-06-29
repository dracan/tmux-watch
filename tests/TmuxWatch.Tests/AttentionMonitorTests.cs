using TmuxWatch.Config;
using TmuxWatch.Detection;
using TmuxWatch.Discovery;
using TmuxWatch.Monitor;
using TmuxWatch.Notifications;
using TmuxWatch.Pointer;

namespace TmuxWatch.Tests;

public class AttentionMonitorTests
{
    /// <summary>Records the boolean the monitor drives the pointer with each tick.</summary>
    private sealed class RecordingPointer : IPointerSignal
    {
        public List<bool> Calls { get; } = new();
        public bool? Last => Calls.Count > 0 ? Calls[^1] : null;
        public void SetWaiting(bool anyWaiting) => Calls.Add(anyWaiting);
        public void Restore() { }
    }

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
    public void Idle_does_not_emit_unless_enabled()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Idle;
        var monitor = Build(fake, out _);

        Assert.Empty(monitor.Tick().Events);
    }

    [Fact]
    public void Idle_emits_when_enabled()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Working;
        var monitor = Build(fake, out _, new WatchConfig { NotifyOnIdle = true });

        monitor.Tick();
        fake.Captures["%1"] = Idle;
        var snap = monitor.Tick();

        Assert.Single(snap.Events);
        Assert.Equal(AttentionKind.EnteredIdle, snap.Events[0].Kind);
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

    // --- Pointer signal driven from aggregate state (task 4.3) ---

    private static AttentionMonitor BuildWithPointer(FakeTmuxClient fake, RecordingPointer pointer, out FakeClock clock)
    {
        var cfg = new WatchConfig();
        clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var discovery = new PaneDiscovery(fake, cfg);
        return new AttentionMonitor(discovery, fake, cfg, new NullNotifier(), clock, pointer);
    }

    [Fact]
    public void Pointer_set_true_when_pane_enters_waiting_and_false_when_resolved()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Working;
        var pointer = new RecordingPointer();
        var monitor = BuildWithPointer(fake, pointer, out var clock);

        monitor.Tick();                                  // working
        Assert.Equal(false, pointer.Last);

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = Waiting;
        monitor.Tick();                                  // -> waiting
        Assert.Equal(true, pointer.Last);

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = Idle;                      // user responded, pane leaves waiting
        monitor.Tick();
        Assert.Equal(false, pointer.Last);
    }

    [Fact]
    public void Pointer_stays_true_while_any_pane_still_waiting()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0\n%2|s|0|1|copilot|0" };
        fake.Captures["%1"] = Waiting;
        fake.Captures["%2"] = Waiting;
        var pointer = new RecordingPointer();
        var monitor = BuildWithPointer(fake, pointer, out var clock);

        monitor.Tick();                                  // both waiting
        Assert.Equal(true, pointer.Last);

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = Idle;                      // one resolves, one still waiting
        monitor.Tick();
        Assert.Equal(true, pointer.Last);

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%2"] = Idle;                      // last one resolves
        monitor.Tick();
        Assert.Equal(false, pointer.Last);
    }
}
