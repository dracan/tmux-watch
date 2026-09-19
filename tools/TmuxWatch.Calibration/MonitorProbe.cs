using TmuxWatch.Config;
using TmuxWatch.Detection;
using TmuxWatch.Discovery;
using TmuxWatch.Monitor;
using TmuxWatch.Notifications;
using TmuxWatch.Pointer;
using TmuxWatch.Tmux;
using TmuxWatch.Tui;

namespace TmuxWatch.Calibration;

/// <summary>Feeds one already captured frame to the production pipeline, with no input channel.</summary>
public sealed class MonitorProbe : ITmuxClient, INotifier
{
    private readonly string agent;
    private string capture = "";
    private bool dead;
    public int Notifications { get; private set; }
    public AttentionMonitor Monitor { get; }
    public HashSet<string> Paused { get; } = new();
    public string PaneId => "%1";
    public MonitorSnapshot? Last { get; private set; }
    public PointerState Pointer => Last is null ? PointerState.Normal : WatcherApp.AggregatePointerState(Last.Panes, Paused);

    public MonitorProbe(string agent, TimeProvider? clock = null, double backgroundGraceSeconds = 120, int unknownCapturesBeforeStale = 15)
    {
        this.agent = agent;
        var cfg = new WatchConfig { Agents = [AgentAdapter.Profile(agent)], BackgroundGraceSeconds = backgroundGraceSeconds, UnknownCapturesBeforeStale = unknownCapturesBeforeStale };
        Monitor = new AttentionMonitor(new PaneDiscovery(this, cfg, "%self"), this, cfg, this, clock);
    }

    public MonitorSnapshot Tick(string text, bool isDead = false)
    {
        capture = text;
        dead = isDead;
        return Last = Monitor.Tick();
    }

    public bool Acknowledge() => Monitor.Acknowledge(PaneId);
    public void Notify(string title, string body) => Notifications++;
    public TmuxResult ListPanesRaw(string format) => new(true, 0,
        $"%1|0|0|{(dead ? 1 : 0)}|1|1|1|0|{agent}|calibration|/tmp|calibration", "");
    public TmuxResult CapturePane(string paneId) => new(true, 0, capture, "");
    public TmuxResult SwitchClient(string sessionName) => throw new NotSupportedException();
    public TmuxResult SelectWindow(string windowTarget) => throw new NotSupportedException();
    public TmuxResult SelectPane(string paneId) => throw new NotSupportedException();
    public TmuxResult NewWindow(string sessionName, string? windowName) => throw new NotSupportedException();
}

public sealed class ReplayClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(double seconds) => Now += TimeSpan.FromSeconds(seconds);
}

public static class MonitorReplay
{
    public static List<CheckResult> Run(string fixtures)
    {
        var results = new List<CheckResult>();
        string Read(string name) => File.ReadAllText(Path.Combine(fixtures, name + ".txt"));
        foreach (var agent in AgentAdapter.Ids)
        {
            var idle = Read(agent == "copilot" ? "idle" : agent + "-idle");
            var working = Read(agent == "copilot" ? "working" : agent + "-working");
            var waiting = Read(agent switch { "copilot" => "waiting-ask-user", "claude" => "claude-waiting", _ => "codex-question-options" });
            void Test(string name, Action<MonitorProbe, ReplayClock> test)
            {
                try
                {
                    var clock = new ReplayClock();
                    test(new MonitorProbe(agent, clock, backgroundGraceSeconds: 5, unknownCapturesBeforeStale: 3), clock);
                    results.Add(new(agent + "/replay/" + name, Outcome.Pass, "production monitor with scrubbed fixtures"));
                }
                catch (InvalidOperationException e) { results.Add(new(agent + "/replay/" + name, Outcome.Mismatch, e.Message)); }
            }
            void State(MonitorProbe p, string text, PaneState expected, int notifications, bool dead = false)
            {
                var s = p.Tick(text, dead);
                Require(s.Panes.Single().State == expected, "Expected " + expected + ", got " + s.Panes.Single().State);
                Require(p.Notifications == notifications, $"Expected {notifications} notifications, got {p.Notifications}");
            }
            Test("first-sight-completion-acknowledgement", (p, _) =>
            {
                State(p, idle, PaneState.Idle, 0);
                State(p, working, PaneState.Working, 0);
                State(p, idle, PaneState.Done, 1);
                Require(p.Pointer == PointerState.Done, "DONE pointer missing");
                State(p, idle, PaneState.Done, 1);
                Require(p.Acknowledge(), "DONE acknowledgement failed");
                State(p, idle, PaneState.Idle, 1);
                Require(p.Pointer == PointerState.Normal, "Acknowledgement did not clear pointer");
                Require(!p.Acknowledge(), "IDLE acknowledged as DONE");
            });
            Test("waiting-notification-pause-dead", (p, _) =>
            {
                State(p, waiting, PaneState.Waiting, 1);
                Require(p.Pointer == PointerState.Waiting, "WAITING pointer missing");
                State(p, waiting, PaneState.Waiting, 1);
                p.Paused.Add(p.PaneId);
                Require(p.Pointer == PointerState.Normal, "Paused pane affected pointer");
                p.Paused.Clear();
                State(p, idle, PaneState.Idle, 1);
                State(p, "", PaneState.Dead, 1, true);
                Require(p.Pointer == PointerState.Normal, "Dead pane affected pointer");
            });
            Test("unknown-debounce-and-recovery", (p, _) =>
            {
                State(p, working, PaneState.Working, 0);
                State(p, "", PaneState.Working, 0);
                State(p, idle, PaneState.Done, 1);
                State(p, "", PaneState.Done, 1);
                State(p, "", PaneState.Done, 1);
                State(p, "", PaneState.Unknown, 1);
                State(p, idle, PaneState.Idle, 1);
            });
            if (agent != "claude") continue;
            var task = Read("claude-backgnd");
            var subagent = Read("claude-backgnd-subagent");
            // Both fingerprints are present below the same composer.
            var both = subagent + "\n  \u00b7 1 shell\n";
            Test("background-first-sight", (p, clock) =>
            {
                State(p, task, PaneState.Backgnd, 0);
                clock.Advance(10);
                State(p, task, PaneState.Backgnd, 0);
                State(p, idle, PaneState.Idle, 0);
            });
            Test("background-exit-and-grace", (p, clock) =>
            {
                State(p, working, PaneState.Working, 0);
                State(p, task, PaneState.Backgnd, 0);
                Require(p.Pointer == PointerState.Normal, "Background task affected pointer");
                clock.Advance(6);
                State(p, task, PaneState.Done, 1);
                State(p, task, PaneState.Done, 1);
                p.Acknowledge();
                State(p, task, PaneState.Backgnd, 1);
                State(p, working, PaneState.Working, 1);
                State(p, task, PaneState.Backgnd, 1);
                State(p, idle, PaneState.Done, 2);
            });
            Test("background-agent-and-reason-narrowing", (p, clock) =>
            {
                State(p, working, PaneState.Working, 0);
                State(p, subagent, PaneState.Backgnd, 0);
                clock.Advance(100);
                State(p, subagent, PaneState.Backgnd, 0);
                State(p, both, PaneState.Backgnd, 0);
                clock.Advance(100);
                State(p, both, PaneState.Backgnd, 0);
                State(p, task, PaneState.Backgnd, 0);
                clock.Advance(4);
                State(p, task, PaneState.Backgnd, 0);
                clock.Advance(2);
                State(p, task, PaneState.Done, 1);
            });
            Test("background-agent-exit-waiting-and-new-work", (p, _) =>
            {
                State(p, working, PaneState.Working, 0);
                State(p, subagent, PaneState.Backgnd, 0);
                State(p, idle, PaneState.Done, 1);
                p.Acknowledge();
                State(p, working, PaneState.Working, 1);
                State(p, subagent, PaneState.Backgnd, 1);
                State(p, waiting, PaneState.Waiting, 2);
                State(p, idle, PaneState.Idle, 2);
                State(p, working, PaneState.Working, 2);
                State(p, subagent, PaneState.Backgnd, 2);
                State(p, working, PaneState.Working, 2);
                State(p, idle, PaneState.Done, 3);
            });
        }
        return results;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
