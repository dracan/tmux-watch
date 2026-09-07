using Spectre.Console;
using TmuxWatch.Detection;
using TmuxWatch.Monitor;
using TmuxWatch.Tmux;
using TmuxWatch.Tui;

namespace TmuxWatch.Tests;

public class AgentLabelTests
{
    private static WatchRow Agent(string id, string agent, string window = "project")
    {
        var pane = new Pane(id, "s", 0, 0, agent, false, window, AgentId: agent);
        return new WatchRow(pane, new TrackedPaneView(pane, PaneState.Idle, DateTimeOffset.UnixEpoch, false));
    }

    private static string Render(IReadOnlyList<WatchRow> rows, int width = 65)
    {
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            Out = new AnsiConsoleOutput(output),
        });
        console.Profile.Width = width;
        var numbers = rows.Select((row, index) => (row.Id, index)).ToDictionary(x => x.Id, x => x.index);
        console.Write(WatcherApp.BuildRowTable("Panes", rows, numbers, DateTimeOffset.UnixEpoch, false, null));
        return output.ToString();
    }

    [Fact]
    public void Mixed_agents_show_the_agreed_prefixes_in_the_window_cell()
    {
        var text = Render(new[] { Agent("%1", "claude"), Agent("%2", "copilot"), Agent("%3", "codex") });
        Assert.Contains("CC project", text);
        Assert.Contains("GHCP project", text);
        Assert.Contains("CDX project", text);
    }

    [Fact]
    public void Prefixes_do_not_wrap_long_window_names_in_a_narrow_split()
    {
        var shortNames = Render(new[] { Agent("%1", "claude", "app"), Agent("%2", "copilot", "api") }, 50);
        var longNames = Render(new[]
        {
            Agent("%1", "claude", "a very long window name with several words"),
            Agent("%2", "copilot", "another very long window name with several words"),
        }, 50);
        Assert.True(shortNames.Split('\n').Length == longNames.Split('\n').Length,
            $"Short names:\n{shortNames}\nLong names:\n{longNames}");
        Assert.Contains("CC ", longNames);
        Assert.Contains("GHCP ", longNames);
    }

    [Theory]
    [InlineData("claude", "CC")]
    [InlineData("copilot", "GHCP")]
    [InlineData("codex", "CDX")]
    public void Multiple_panes_of_one_agent_need_no_labels(string agent, string label)
    {
        var text = Render(new[] { Agent("%1", agent), Agent("%2", agent) });
        Assert.Contains("project", text);
        Assert.DoesNotContain(label + " project", text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Main_and_paused_tables_decide_independently(bool mixedPaused)
    {
        var agents = new[] { Agent("%1", "claude"), Agent("%2", "claude"), Agent("%3", "codex") };
        var paused = mixedPaused ? new HashSet<string> { "%2", "%3" } : new HashSet<string> { "%1" };
        var layout = WatcherApp.BuildLayout(agents.Select(row => row.Agent!).ToList(),
            Array.Empty<Pane>(), paused, true, true);
        var mixed = Render(mixedPaused ? layout.Paused : layout.Agent);
        var single = Render(mixedPaused ? layout.Agent : layout.Paused);
        Assert.Contains("CC project", mixed);
        Assert.Contains("CDX project", mixed);
        Assert.DoesNotContain("CC project", single);
        Assert.Contains("project", single);
    }

    [Fact]
    public void Non_agent_rows_neither_count_nor_receive_labels()
    {
        var shell = new WatchRow(new Pane("%3", "s", 0, 1, "bash", false, "shell"), null);
        var single = Render(new[] { Agent("%1", "claude"), shell });
        Assert.DoesNotContain("CC project", single);
        var mixed = Render(new[] { Agent("%1", "claude"), Agent("%2", "codex"), shell });
        Assert.Contains("CC project", mixed);
        Assert.Contains("CDX project", mixed);
        Assert.Contains("shell", mixed);
        Assert.DoesNotContain("CC shell", mixed);
        Assert.DoesNotContain("CDX shell", mixed);
        Assert.DoesNotContain("bash shell", mixed);
    }

    [Fact]
    public void Custom_ids_and_window_names_are_literal_text_even_when_focused()
    {
        var custom = Agent("%2", "[red]custom[/]", "[blue]app[/]");
        custom = custom with { Pane = custom.Pane with { WindowActive = true, PaneActive = true } };
        var text = Render(new[] { Agent("%1", "codex"), custom }, 100);
        Assert.Contains("CDX project", text);
        Assert.Contains("[red]custom[/] [blue]app[/]", text);
    }
}
