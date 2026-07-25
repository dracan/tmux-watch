using TmuxWatch.Config;
using TmuxWatch.Detection;
using TmuxWatch.Discovery;
using TmuxWatch.Monitor;
using TmuxWatch.Notifications;
using TmuxWatch.Pointer;
using TmuxWatch.Tui;

namespace TmuxWatch.Tests;

/// <summary>
/// The non-agent inventory must reach the snapshot while staying inert: never captured,
/// classified, tracked, notified on, or able to move the pointer cue.
/// </summary>
public class NonAgentInventoryTests
{
    private sealed class RecordingNotifier : INotifier
    {
        public List<string> Titles { get; } = new();
        public void Notify(string title, string body) => Titles.Add(title);
    }

    private const string Waiting = "❯ 1. Yes\n↑/↓ to navigate · enter to select · esc to cancel";
    private const string Idle = "❯\n/ commands · ? help · space hold to record   Claude Opus 4.8";

    private static AttentionMonitor Build(FakeTmuxClient fake, RecordingNotifier notifier)
    {
        var cfg = new WatchConfig();
        var discovery = new PaneDiscovery(fake, cfg, selfPaneId: "");
        return new AttentionMonitor(discovery, fake, cfg, notifier);
    }

    [Fact]
    public void Inventory_reaches_the_snapshot_without_being_captured()
    {
        var fake = new FakeTmuxClient
        {
            ListOutput = "%1|s|0|0|copilot|0\n%2|s|0|1|k9s|0\n%3|s|1|0|lazygit|0",
        };
        fake.Captures["%1"] = Idle;
        var monitor = Build(fake, new RecordingNotifier());

        var snap = monitor.Tick();

        Assert.Equal(new[] { "%1" }, snap.Panes.Select(p => p.Pane.Id));
        Assert.Equal(new[] { "%2", "%3" }, snap.OtherPanes.Select(p => p.Id));
        // Only the agent pane was ever captured.
        Assert.Equal(new[] { "%1" }, fake.CapturedPanes);
    }

    [Fact]
    public void Non_agent_panes_raise_no_notifications_as_they_come_and_go()
    {
        var fake = new FakeTmuxClient { ListOutput = "%2|s|0|1|k9s|0" };
        var notifier = new RecordingNotifier();
        var monitor = Build(fake, notifier);

        var first = monitor.Tick();
        fake.ListOutput = "%2|s|0|1|lazygit|0\n%5|s|2|0|bash|0";   // process changed, one appeared
        var second = monitor.Tick();
        fake.ListOutput = "";                                        // all gone
        var third = monitor.Tick();

        Assert.Empty(first.Events);
        Assert.Empty(second.Events);
        Assert.Empty(third.Events);
        Assert.Empty(notifier.Titles);
    }

    [Fact]
    public void Non_agent_panes_do_not_move_the_pointer()
    {
        var fake = new FakeTmuxClient { ListOutput = "%2|s|0|1|k9s|0\n%3|s|1|0|bash|0" };
        var monitor = Build(fake, new RecordingNotifier());

        var snap = monitor.Tick();

        Assert.NotEmpty(snap.OtherPanes);
        Assert.Equal(
            PointerState.Normal,
            WatcherApp.AggregatePointerState(snap.Panes, new HashSet<string>()));
    }

    [Fact]
    public void A_waiting_agent_pane_still_drives_the_pointer_alongside_inventory()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0\n%2|s|0|1|k9s|0" };
        fake.Captures["%1"] = Waiting;
        var monitor = Build(fake, new RecordingNotifier());

        var snap = monitor.Tick();

        Assert.Single(snap.OtherPanes);
        Assert.Equal(
            PointerState.Waiting,
            WatcherApp.AggregatePointerState(snap.Panes, new HashSet<string>()));
    }

    [Fact]
    public void Enumeration_failure_empties_the_inventory_but_keeps_tracked_state()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0\n%2|s|0|1|k9s|0" };
        fake.Captures["%1"] = Waiting;
        var monitor = Build(fake, new RecordingNotifier());

        var good = monitor.Tick();
        Assert.Single(good.OtherPanes);

        fake.Started = false;
        fake.ErrorMessage = "tmux not found";
        var bad = monitor.Tick();

        Assert.NotNull(bad.Error);
        Assert.Empty(bad.OtherPanes);
        // The agent pane's tracked state survives the failed enumeration.
        Assert.Equal(new[] { "%1" }, bad.Panes.Select(p => p.Pane.Id));
        Assert.Equal(PaneState.Waiting, bad.Panes[0].State);
    }
}
