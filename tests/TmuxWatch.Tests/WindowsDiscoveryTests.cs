using Spectre.Console;
using TmuxWatch.Config;
using TmuxWatch.Discovery;
using TmuxWatch.Detection;
using TmuxWatch.Monitor;
using TmuxWatch.Notifications;
using TmuxWatch.Tmux;
using TmuxWatch.Tui;

namespace TmuxWatch.Tests;

public class WindowsDiscoveryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1789988801);

    private static FakeTmuxClient ReportedPane() => new()
    {
        // Scrubbed metadata; the process relationships below are synthetic.
        ListOutput = "%20|work|2|0|tgrep|0|project||0|0|100|1789979431",
        SupportsWindowActivity = false,
    };

    private static ProcessSnapshot CopilotTree() => new(new[]
    {
        new ProcessEntry(100, 1, "pwsh.exe", 10),
        new ProcessEntry(101, 100, "copilot.exe", 20),
        new ProcessEntry(102, 101, "tgrep.exe", 30),
    });

    [Fact]
    public void Reported_helper_is_matched_to_live_copilot_owner()
    {
        var tmux = ReportedPane();
        var discovery = new PaneDiscovery(tmux, new WatchConfig(), "", _ => CopilotTree());

        var inventory = discovery.DiscoverPanes();

        var agent = Assert.Single(inventory.AgentPanes);
        Assert.Equal("copilot", agent.AgentId);
        Assert.Equal("tgrep", agent.Command);
        Assert.Empty(inventory.OtherPanes);
        Assert.Empty(tmux.CapturedPanes);
        Assert.Equal(1, tmux.ListCalls);
    }

    [Fact]
    public void New_pane_does_not_inherit_psmux_session_age()
    {
        var discovery = new PaneDiscovery(ReportedPane(), new WatchConfig(), "", _ => null);
        var pane = Assert.Single(discovery.DiscoverPanes().OtherPanes);

        Assert.Null(pane.TimeSinceActivity(Now));
        var rendered = Render(new[] { new WatchRow(pane, null) });
        Assert.DoesNotContain("2h 36m", rendered);
        Assert.Contains("Command", rendered);
        Assert.Contains("Quiet for", rendered);
        Assert.DoesNotContain("In state", rendered);
    }

    [Fact]
    public void Exit_and_reused_pane_root_do_not_keep_an_old_agent_match()
    {
        var tmux = ReportedPane();
        var snapshot = CopilotTree();
        var discovery = new PaneDiscovery(tmux, new WatchConfig(), "", _ => snapshot);
        Assert.Single(discovery.DiscoverPanes().AgentPanes);

        // Copilot exited; its orphan helper still names the old parent pid.
        snapshot = new(new[]
        {
            new ProcessEntry(100, 1, "pwsh.exe", 10),
            new ProcessEntry(102, 101, "tgrep.exe", 30),
        });
        Assert.Single(discovery.DiscoverPanes().OtherPanes);

        // Even if pane %20 is reused, no identity is inherited from its old root.
        tmux.ListOutput = "%20|work|2|0|tgrep|0|project||0|0|200|1789979431";
        snapshot = CopilotTree();
        Assert.Single(discovery.DiscoverPanes().OtherPanes);
    }

    [Fact]
    public void Calibration_uses_the_same_stamped_identity_without_recapturing_processes()
    {
        var calls = 0;
        var tmux = ReportedPane();
        tmux.ListOutput = "%20|work|2|0|tgrep|0|||0|0|100\n%21|work|3|0|rg|0|||0|0|200";
        var discovery = new PaneDiscovery(tmux, new WatchConfig(), "", roots =>
        {
            calls++;
            Assert.Equal(new[] { 100, 200 }, roots.Order());
            return CopilotTree();
        });

        var all = discovery.EnumerateAll();
        Assert.Equal("copilot", discovery.MatchProfile(all.Panes[0])!.Id);
        Assert.Null(discovery.MatchProfile(all.Panes[1]));
        Assert.Equal(1, calls);
        Assert.Equal(1, tmux.ListCalls);
        Assert.Empty(tmux.CapturedPanes);
    }

    [Fact]
    public void Process_match_precedes_naming_but_direct_command_keeps_precedence()
    {
        var tmux = ReportedPane();
        var cfg = new WatchConfig
        {
            Agents = new()
            {
                new AgentProfile { Id = "claude", Command = "claude", SessionNameConvention = "^work$" },
                WatchConfig.CopilotProfile(),
            },
        };
        var calls = 0;
        var discovery = new PaneDiscovery(tmux, cfg, "", _ => { calls++; return CopilotTree(); });
        Assert.Equal("copilot", Assert.Single(discovery.DiscoverPanes().AgentPanes).AgentId);

        tmux.ListOutput = "%20|work|2|0|claude|0|||0|0|100";
        Assert.Equal("claude", Assert.Single(discovery.DiscoverPanes().AgentPanes).AgentId);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Snapshot_failure_preserves_command_and_naming_matches_and_drops_old_ownership()
    {
        var tmux = ReportedPane();
        ProcessSnapshot? snapshot = CopilotTree();
        var cfg = new WatchConfig { Agents = new() { WatchConfig.CopilotProfile() } };
        var discovery = new PaneDiscovery(tmux, cfg, "", _ => snapshot);
        Assert.Single(discovery.DiscoverPanes().AgentPanes);
        snapshot = null;
        Assert.Single(discovery.DiscoverPanes().OtherPanes);
        cfg.Agents[0].SessionNameConvention = "^work$";
        Assert.Single(discovery.DiscoverPanes().AgentPanes);
        tmux.ListOutput = "%20|other|2|0|copilot|0|||0|0|100";
        Assert.Single(discovery.DiscoverPanes().AgentPanes);
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "%20")]
    public void Dead_and_self_panes_do_not_acquire_process_owners(bool dead, string self)
    {
        var tmux = ReportedPane();
        tmux.ListOutput = $"%20|work|2|0|tgrep|{(dead ? 1 : 0)}|||0|0|100";
        var calls = 0;
        var discovery = new PaneDiscovery(tmux, new WatchConfig(), self, _ => { calls++; return CopilotTree(); });
        Assert.Empty(discovery.DiscoverPanes().AgentPanes);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Explicit_profile_list_controls_process_matching()
    {
        var cfg = new WatchConfig { Agents = new() { WatchConfig.ClaudeProfile() } };
        var discovery = new PaneDiscovery(ReportedPane(), cfg, "", _ => CopilotTree());
        Assert.Empty(discovery.DiscoverPanes().AgentPanes);
    }

    [Fact]
    public void Helpers_do_not_break_the_working_to_done_transition()
    {
        var tmux = ReportedPane();
        var cfg = new WatchConfig();
        ProcessSnapshot? snapshot = CopilotTree();
        var monitor = new AttentionMonitor(new PaneDiscovery(tmux, cfg, "", _ => snapshot),
            tmux, cfg, new NullNotifier());
        // Use the existing Copilot interrupt footer, without a synthetic new token.
        tmux.Captures["%20"] = "\u25ce Working esc cancel";
        Assert.Equal(PaneState.Working, Assert.Single(monitor.Tick().Panes).State);
        tmux.Captures["%20"] = "/ commands ? help";
        Assert.Equal(PaneState.Done, Assert.Single(monitor.Tick().Panes).State);
        snapshot = new ProcessSnapshot(new[] { new ProcessEntry(100, 1, "pwsh.exe", 10) });
        var exited = monitor.Tick();
        Assert.Empty(exited.Panes);
        Assert.Single(exited.OtherPanes);
        Assert.Equal(2, tmux.CapturedPanes.Count);
    }

    [Fact]
    public void Supported_hosts_keep_equal_activity_timestamps()
    {
        var tmux = ReportedPane();
        tmux.SupportsWindowActivity = true;
        tmux.ListOutput = "%1|work|0|0|pwsh|0|||0|0|100|1789979431\n%2|work|1|0|pwsh|0|||0|0|200|1789979431";
        var discovery = new PaneDiscovery(tmux, new WatchConfig(), "", _ => null);
        Assert.All(discovery.EnumerateAll().Panes,
            pane => Assert.Equal(TimeSpan.FromSeconds(9370), pane.TimeSinceActivity(Now)));
    }

    [Theory]
    [InlineData("psmux")]
    [InlineData("PSMUX.EXE")]
    [InlineData("C:\\tools\\psmux.exe")]
    [InlineData("/usr/local/bin/psmux")]
    public void Explicit_psmux_clients_do_not_advertise_window_activity(string executable) =>
        Assert.False(new TmuxRunner(executable).SupportsWindowActivity);

    [Fact]
    public void Tmux_alias_activity_support_follows_the_native_host() =>
        Assert.Equal(!OperatingSystem.IsWindows(), new TmuxRunner("tmux.exe").SupportsWindowActivity);

    [Fact]
    public void Agent_and_mixed_paused_headings_describe_their_rows()
    {
        var pane = new Pane("%1", "work", 0, 0, "copilot", false, AgentId: "copilot");
        var agent = new WatchRow(pane, new TrackedPaneView(pane, PaneState.Idle, Now, false));
        var other = new WatchRow(new Pane("%2", "work", 1, 0, "pwsh", false), null);
        var agentText = Render(new[] { agent });
        Assert.Contains("State", agentText);
        Assert.Contains("In state", agentText);
        Assert.DoesNotContain("Quiet for", agentText);
        var mixed = Render(new[] { agent, other });
        Assert.Contains("State / Cmd", mixed);
        Assert.Contains("Age", mixed);
        Assert.Contains("agents = time in state", mixed);
        Assert.Contains("others = window inactivity", mixed);
    }

    internal static string Render(IReadOnlyList<WatchRow> rows)
    {
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No, Out = new AnsiConsoleOutput(output),
        });
        console.Profile.Width = 100;
        var numbers = rows.Select((row, i) => (row.Id, i)).ToDictionary(x => x.Id, x => x.i);
        console.Write(WatcherApp.BuildRowTable("Panes", rows, numbers, Now, false, null));
        return output.ToString();
    }
}
