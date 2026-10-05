using TmuxWatch.Config;
using TmuxWatch.Detection;
using TmuxWatch.Discovery;
using TmuxWatch.Monitor;
using TmuxWatch.Notifications;
using TmuxWatch.Tui;

namespace TmuxWatch.Tests;

/// <summary>
/// The live view polls on a background thread. These pin the pieces that make that safe:
/// the monitor's lock covers only the state-machine apply, a poll that predates a local
/// change is not drawn, and notifications wait for the render thread.
/// </summary>
public class BackgroundPollingTests
{
    private const string Working = "────\n ◎ Working esc cancel   Claude Opus 4.8 · 1M context";
    private const string Idle = "❯\n/ commands · ? help · space hold to record   Claude Opus 4.8";

    [Fact]
    public void Acknowledge_does_not_wait_for_an_in_flight_capture()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        var cfg = new WatchConfig();
        var monitor = new AttentionMonitor(
            new PaneDiscovery(fake, cfg, selfPaneId: ""), fake, cfg, new NullNotifier());

        fake.Captures["%1"] = Working;
        monitor.Tick();
        fake.Captures["%1"] = Idle;
        Assert.Equal(PaneState.Done, Assert.Single(monitor.Tick().Panes).State);

        using var capturing = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fake.BeforeCapture = _ => { capturing.Set(); release.Wait(); };
        var tick = Task.Run(monitor.Tick);
        Assert.True(capturing.Wait(TimeSpan.FromSeconds(5)));

        // The tick is parked inside its capture; the ack must still go straight through.
        var ack = Task.Run(() => monitor.Acknowledge("%1"));
        Assert.True(ack.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(ack.Result);

        release.Set();
        Assert.True(tick.Wait(TimeSpan.FromSeconds(5)));
        // The in-flight tick applies after the ack and must not resurrect DONE.
        Assert.Equal(PaneState.Idle, Assert.Single(tick.Result.Panes).State);
    }

    [Fact]
    public void A_poll_that_began_before_a_local_action_is_stale()
    {
        var action = DateTimeOffset.UnixEpoch.AddSeconds(10);
        Assert.True(WatcherApp.IsStaleSnapshot(action.AddMilliseconds(-1), action));
        Assert.False(WatcherApp.IsStaleSnapshot(action, action));
        Assert.False(WatcherApp.IsStaleSnapshot(action.AddMilliseconds(1), action));
        Assert.False(WatcherApp.IsStaleSnapshot(action, DateTimeOffset.MinValue));
    }

    [Fact]
    public void Deferred_notifications_replay_in_order_only_on_flush()
    {
        var inner = new RecordingNotifier();
        var deferred = new DeferredNotifier(inner);

        deferred.Notify("a", "1");
        deferred.Notify("b", "2");
        Assert.Empty(inner.Seen);

        deferred.Flush();
        Assert.Equal(["a", "b"], inner.Seen);

        deferred.Flush();
        Assert.Equal(2, inner.Seen.Count);
    }

    private sealed class RecordingNotifier : INotifier
    {
        public List<string> Seen { get; } = new();
        public void Notify(string title, string body) => Seen.Add(title);
    }
}
