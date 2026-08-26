using TmuxWatch.Config;
using TmuxWatch.Discovery;
using TmuxWatch.Tmux;

namespace TmuxWatch.Tests;

public class PaneDiscoveryTests
{
    /// <summary>Parses a line written in readable field order. See <see cref="TestPanes"/>.</summary>
    private static Pane? Parse(string line) => PaneDiscovery.Parse(TestPanes.Line(line));

    /// <summary>Builds a line in the real wire order, for the tests that are about that order.</summary>
    private static string Wire(params string[] fields) => string.Join(PaneDiscovery.FieldSeparator, fields);

    /// <summary>
    /// The delimiter is one the data can contain (tmux offers no safe alternative - see
    /// the doc comment on Format), so the guarantee is positional instead: every field
    /// that feeds a decision is parsed before the first field that can carry a '|', and
    /// the last field absorbs the remainder. A window named "build | watch" used to shift
    /// everything after it - no focus marker, a pid guard reading a boolean, an activity
    /// stamp reading the pid and rendering an in-state age of some twenty thousand days.
    /// </summary>
    [Fact]
    public void A_pipe_in_the_window_name_shifts_nothing_and_is_kept_whole()
    {
        var pane = PaneDiscovery.Parse(Wire(
            "%12", "3", "0", "0", "1", "1", "4242", "1784969224",
            "claude", "work", "/home/dan/code", "build | watch"));

        Assert.NotNull(pane);
        Assert.Equal("build | watch", pane!.WindowName);
        Assert.Equal("%12", pane.Id);
        Assert.Equal("work:3", pane.WindowTarget);
        Assert.Equal("claude", pane.Command);
        Assert.True(pane.IsFocused);
        Assert.Equal(4242, pane.Pid);
        Assert.Equal(1784969224, pane.WindowActivityUnix);
    }

    /// <summary>
    /// A '|' in the two fields ahead of the window name still costs something - they are
    /// the only ones it can - but the damage stops at cosmetics. Nothing that decides
    /// identity, targeting, focus, agent match, pid, or activity sits after them.
    /// </summary>
    [Fact]
    public void A_pipe_in_the_path_leaves_every_decision_field_intact()
    {
        var pane = PaneDiscovery.Parse(Wire(
            "%13", "2", "1", "0", "1", "1", "99", "1784969224",
            "copilot", "work", "/home/dan/a|b", "shell"));

        Assert.NotNull(pane);
        Assert.Equal("%13", pane!.Id);
        Assert.Equal("work:2", pane.WindowTarget);
        Assert.Equal(1, pane.PaneIndex);
        Assert.Equal("copilot", pane.Command);
        Assert.True(pane.IsFocused);
        Assert.Equal(99, pane.Pid);
        Assert.Equal(1784969224, pane.WindowActivityUnix);
    }

    /// <summary>The format tmux is handed must emit exactly the fields Parse reads.</summary>
    [Fact]
    public void Format_emits_the_field_count_Parse_expects() =>
        Assert.Equal(PaneDiscovery.FieldCount - 1,
            PaneDiscovery.Format.Count(c => c == PaneDiscovery.FieldSeparator));

    /// <summary>
    /// The free-text fields must all come after the decision fields, which is the whole
    /// basis of the guarantee above. Asserted on the format string so a later reorder
    /// that quietly moves one forward fails here.
    /// </summary>
    [Fact]
    public void Format_puts_every_free_text_field_after_every_decision_field()
    {
        var fields = PaneDiscovery.Format.Split(PaneDiscovery.FieldSeparator);
        var freeText = new[] { "#{pane_current_command}", "#{session_name}", "#{pane_current_path}", "#{window_name}" };
        var firstFree = fields.Select((f, i) => (f, i)).First(x => freeText.Contains(x.f)).i;

        Assert.All(fields.Skip(firstFree), f => Assert.Contains(f, freeText));
    }

    [Fact]
    public void Parse_reads_all_fields()
    {
        var pane = Parse("%10|web-api|2|0|copilot|0|copilot|C:\\dev\\web-api");
        Assert.NotNull(pane);
        Assert.Equal("%10", pane!.Id);
        Assert.Equal("web-api", pane.SessionName);
        Assert.Equal(2, pane.WindowIndex);
        Assert.Equal(0, pane.PaneIndex);
        Assert.Equal("copilot", pane.Command);
        Assert.False(pane.Dead);
        Assert.Equal("web-api:2", pane.WindowTarget);
        Assert.Equal("copilot", pane.WindowName);
        Assert.Equal("web-api", pane.PathLabel);
    }

    [Fact]
    public void Parse_reads_focus_flags()
    {
        var focused = Parse("%4|s|1|0|copilot|0|REST API|C:\\dev\\rest|1|1");
        Assert.NotNull(focused);
        Assert.True(focused!.WindowActive);
        Assert.True(focused.PaneActive);
        Assert.True(focused.IsFocused);

        // Active pane but in a non-active window is NOT focused.
        var background = Parse("%1|s|0|0|copilot|0|Other|C:\\dev\\other|0|1");
        Assert.False(background!.IsFocused);
    }

    /// <summary>
    /// A host that supplies only the leading fields must still enumerate; the rest
    /// degrade to empty rather than failing the line.
    /// </summary>
    [Fact]
    public void Parse_tolerates_a_truncated_line()
    {
        var pane = PaneDiscovery.Parse(Wire("%1", "0", "0", "0", "0", "0"));
        Assert.NotNull(pane);
        Assert.Equal("", pane!.WindowName);
        Assert.Equal("", pane.CurrentPath);
        Assert.Equal("", pane.Command);
        Assert.Equal(0, pane.Pid);
    }

    [Fact]
    public void Parse_detects_dead_flag()
    {
        var pane = Parse("%5|work|0|1|bash|1");
        Assert.True(pane!.Dead);
    }

    [Fact]
    public void Parse_ignores_blank_lines() => Assert.Null(Parse("   "));

    [Fact]
    public void Parse_reads_the_window_activity_timestamp()
    {
        var pane = Parse("%2|s|0|0|bash|0|shell|/home/dan|0|0|123|1784969224");
        Assert.Equal(1784969224, pane!.WindowActivityUnix);

        var now = DateTimeOffset.FromUnixTimeSeconds(1784969224).AddMinutes(5);
        Assert.Equal(TimeSpan.FromMinutes(5), pane.TimeSinceActivity(now));
    }

    [Fact]
    public void Parse_treats_a_missing_or_unparseable_activity_stamp_as_unknown()
    {
        // Absent (a host that does not supply the field) and unparseable both degrade to
        // "unknown" rather than failing the line.
        var absent = PaneDiscovery.Parse(Wire("%2", "0", "0", "0", "0", "0", "123"));
        Assert.Equal(0, absent!.WindowActivityUnix);
        Assert.Null(absent.TimeSinceActivity(DateTimeOffset.UnixEpoch));

        var garbage = Parse("%3|s|0|0|bash|0|shell|/home/dan|0|0|123|not-a-number");
        Assert.NotNull(garbage);
        Assert.Equal(0, garbage!.WindowActivityUnix);
    }

    [Fact]
    public void Activity_elapsed_never_goes_negative()
    {
        var pane = Parse("%2|s|0|0|bash|0|shell|/home/dan|0|0|123|1784969224");
        var before = DateTimeOffset.FromUnixTimeSeconds(1784969224).AddMinutes(-5);
        Assert.Equal(TimeSpan.Zero, pane!.TimeSinceActivity(before));
    }

    [Fact]
    public void Matches_copilot_by_command()
    {
        var fake = new FakeTmuxClient
        {
            ListOutput = "%1|s|0|0|copilot|0\n%2|s|0|1|lazygit|0\n%3|s|0|2|bash|0",
        };
        var discovery = new PaneDiscovery(fake, new WatchConfig());

        var agents = discovery.DiscoverAgentPanes();

        Assert.True(agents.Ok);
        Assert.Single(agents.Panes);
        Assert.Equal("%1", agents.Panes[0].Id);
        Assert.Equal("copilot", agents.Panes[0].AgentId);
    }

    [Fact]
    public void Matches_claude_by_command()
    {
        // tmux reports `pane_current_command` as `claude` for a Claude Code pane.
        var fake = new FakeTmuxClient { ListOutput = "%7|s|0|0|claude|0" };
        var discovery = new PaneDiscovery(fake, new WatchConfig());

        var agents = discovery.DiscoverAgentPanes();

        Assert.Single(agents.Panes);
        Assert.Equal("claude", agents.Panes[0].AgentId);
    }

    [Fact]
    public void Matches_both_agents_in_one_server()
    {
        var fake = new FakeTmuxClient
        {
            ListOutput = "%1|s|0|0|copilot|0\n%2|s|0|1|claude|0\n%3|s|0|2|vim|0",
        };
        var discovery = new PaneDiscovery(fake, new WatchConfig());

        var agents = discovery.DiscoverAgentPanes();

        Assert.Equal(2, agents.Panes.Count);
        Assert.Equal(new[] { "copilot", "claude" }, agents.Panes.Select(p => p.AgentId));
    }

    [Fact]
    public void Matches_copilot_when_command_has_exe_extension()
    {
        // A Windows/psmux host reports the foreground command as "copilot.exe".
        var fake = new FakeTmuxClient
        {
            ListOutput = "%1|s|0|0|copilot.exe|0\n%2|s|0|1|cmd|0\n%3|s|0|2|pwsh|0",
        };
        var discovery = new PaneDiscovery(fake, new WatchConfig());

        var agents = discovery.DiscoverAgentPanes();

        Assert.Single(agents.Panes);
        Assert.Equal("%1", agents.Panes[0].Id);
    }

    [Fact]
    public void Session_name_convention_is_a_backstop()
    {
        // A node-hosted pane named by convention still matches, even though its
        // foreground command is not the agent's command.
        var fake = new FakeTmuxClient { ListOutput = "%9|cop-experiment|0|0|node|0" };
        var cfg = new WatchConfig
        {
            Agents = new()
            {
                new AgentProfile { Id = "copilot", Command = "copilot", SessionNameConvention = "^cop-" },
            },
        };
        var discovery = new PaneDiscovery(fake, cfg);

        var agents = discovery.DiscoverAgentPanes();

        Assert.Single(agents.Panes);
        Assert.Equal("copilot", agents.Panes[0].AgentId);
    }

    [Fact]
    public void Non_agent_panes_are_excluded()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|bash|0\n%2|s|0|1|vim|0" };
        var discovery = new PaneDiscovery(fake, new WatchConfig());

        var agents = discovery.DiscoverAgentPanes();

        Assert.Empty(agents.Panes);
    }

    [Fact]
    public void Non_agent_panes_are_returned_as_inventory()
    {
        var fake = new FakeTmuxClient
        {
            ListOutput = "%1|s|0|0|copilot|0\n%2|s|0|1|k9s|0\n%3|s|1|0|lazygit|0\n%4|s|2|0|bash|0",
        };
        // selfPaneId "" pins "not running inside tmux", so an ambient TMUX_PANE in the
        // test host cannot filter one of these ids out from under the assertion.
        var discovery = new PaneDiscovery(fake, new WatchConfig(), selfPaneId: "");

        var inventory = discovery.DiscoverPanes();

        Assert.True(inventory.Ok);
        Assert.Equal(new[] { "%1" }, inventory.AgentPanes.Select(p => p.Id));
        Assert.Equal(new[] { "%2", "%3", "%4" }, inventory.OtherPanes.Select(p => p.Id));
        Assert.Equal(new[] { "k9s", "lazygit", "bash" }, inventory.OtherPanes.Select(p => p.Command));
    }

    [Fact]
    public void Inventory_costs_a_single_enumeration()
    {
        var fake = new FakeTmuxClient
        {
            ListOutput = "%1|s|0|0|copilot|0\n%2|s|0|1|bash|0",
        };
        var discovery = new PaneDiscovery(fake, new WatchConfig(), selfPaneId: "");

        _ = discovery.DiscoverPanes();

        Assert.Equal(1, fake.ListCalls);
        // The inventory is never captured - discovery does not capture at all.
        Assert.Empty(fake.CapturedPanes);
    }

    [Fact]
    public void Watchers_own_pane_is_kept_out_of_the_inventory()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|bash|0\n%2|s|0|1|dotnet|0" };
        var discovery = new PaneDiscovery(fake, new WatchConfig(), selfPaneId: "%2");

        var inventory = discovery.DiscoverPanes();

        Assert.Equal(new[] { "%1" }, inventory.OtherPanes.Select(p => p.Id));
    }

    [Fact]
    public void Every_non_agent_pane_is_listed_when_the_watcher_is_not_inside_tmux()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|bash|0\n%2|s|0|1|dotnet|0" };
        var discovery = new PaneDiscovery(fake, new WatchConfig(), selfPaneId: "");

        var inventory = discovery.DiscoverPanes();

        Assert.Equal(new[] { "%1", "%2" }, inventory.OtherPanes.Select(p => p.Id));
    }

    [Fact]
    public void Inventory_is_empty_when_the_server_is_unavailable()
    {
        var fake = new FakeTmuxClient { Started = false, ErrorMessage = "tmux not found" };
        var discovery = new PaneDiscovery(fake, new WatchConfig());

        var inventory = discovery.DiscoverPanes();

        Assert.False(inventory.Ok);
        Assert.Empty(inventory.AgentPanes);
        Assert.Empty(inventory.OtherPanes);
        Assert.Contains("tmux not found", inventory.Error);
    }

    [Fact]
    public void Server_unavailable_returns_empty_with_error()
    {
        var fake = new FakeTmuxClient { Started = false, ErrorMessage = "tmux not found" };
        var discovery = new PaneDiscovery(fake, new WatchConfig());

        var result = discovery.EnumerateAll();

        Assert.False(result.Ok);
        Assert.Empty(result.Panes);
        Assert.Contains("tmux not found", result.Error);
    }
}
