using TmuxWatch.Config;
using TmuxWatch.Discovery;
using TmuxWatch.Psmux;

namespace TmuxWatch.Tests;

public class PaneDiscoveryTests
{
    [Fact]
    public void Parse_reads_all_fields()
    {
        var pane = PaneDiscovery.Parse("%10|web-api|2|0|copilot|0|copilot|C:\\dev\\web-api");
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
        var focused = PaneDiscovery.Parse("%4|s|1|0|copilot|0|REST API|C:\\dev\\rest|1|1");
        Assert.NotNull(focused);
        Assert.True(focused!.WindowActive);
        Assert.True(focused.PaneActive);
        Assert.True(focused.IsFocused);

        // Active pane but in a non-active window is NOT focused.
        var background = PaneDiscovery.Parse("%1|s|0|0|copilot|0|Other|C:\\dev\\other|0|1");
        Assert.False(background!.IsFocused);
    }

    [Fact]
    public void Parse_tolerates_legacy_six_field_lines()
    {
        var pane = PaneDiscovery.Parse("%1|s|0|0|copilot|0");
        Assert.NotNull(pane);
        Assert.Equal("", pane!.WindowName);
        Assert.Equal("", pane.CurrentPath);
    }

    [Fact]
    public void Parse_detects_dead_flag()
    {
        var pane = PaneDiscovery.Parse("%5|work|0|1|pwsh|1");
        Assert.True(pane!.Dead);
    }

    [Fact]
    public void Parse_ignores_blank_lines() => Assert.Null(PaneDiscovery.Parse("   "));

    [Fact]
    public void Filters_to_copilot_by_command()
    {
        var fake = new FakePsmuxClient
        {
            ListOutput = "%1|s|0|0|copilot|0\n%2|s|0|1|lazygit|0\n%3|s|0|2|pwsh|0",
        };
        var discovery = new PaneDiscovery(fake, new WatchConfig());

        var copilot = discovery.DiscoverCopilotPanes();

        Assert.True(copilot.Ok);
        Assert.Single(copilot.Panes);
        Assert.Equal("%1", copilot.Panes[0].Id);
    }

    [Fact]
    public void Filters_to_copilot_when_command_has_exe_extension()
    {
        // Windows psmux reports the foreground command as "copilot.exe".
        var fake = new FakePsmuxClient
        {
            ListOutput = "%1|s|0|0|copilot.exe|0\n%2|s|0|1|cmd|0\n%3|s|0|2|pwsh|0",
        };
        var discovery = new PaneDiscovery(fake, new WatchConfig());

        var copilot = discovery.DiscoverCopilotPanes();

        Assert.True(copilot.Ok);
        Assert.Single(copilot.Panes);
        Assert.Equal("%1", copilot.Panes[0].Id);
    }

    [Fact]
    public void Session_name_convention_is_a_backstop()
    {
        var fake = new FakePsmuxClient { ListOutput = "%9|cop-experiment|0|0|node|0" };
        var cfg = new WatchConfig { SessionNameConvention = "^cop-" };
        var discovery = new PaneDiscovery(fake, cfg);

        var copilot = discovery.DiscoverCopilotPanes();

        Assert.Single(copilot.Panes);
    }

    [Fact]
    public void Server_unavailable_returns_empty_with_error()
    {
        var fake = new FakePsmuxClient { Started = false, ErrorMessage = "psmux not found" };
        var discovery = new PaneDiscovery(fake, new WatchConfig());

        var result = discovery.EnumerateAll();

        Assert.False(result.Ok);
        Assert.Empty(result.Panes);
        Assert.Contains("psmux not found", result.Error);
    }
}
