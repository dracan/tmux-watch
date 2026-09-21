using TmuxWatch.Config;
using TmuxWatch.Discovery;

namespace TmuxWatch.Tests;

public class ProcessSnapshotTests
{
    private static readonly IReadOnlySet<int> Roots = new HashSet<int> { 100 };
    private static ProcessEntry Shell(int pid = 100, int parent = 1, long startedAt = 10) =>
        new(pid, parent, "pwsh.exe", startedAt);
    private static ProcessEntry Agent(int pid = 101, int parent = 100, string command = "copilot.exe", long startedAt = 20) =>
        new(pid, parent, command, startedAt);
    private static string? Owner(params ProcessEntry[] entries) =>
        new ProcessSnapshot(entries).FindOwner(100, Roots, WatchConfig.DefaultAgents)?.Id;

    [Theory]
    [InlineData("tgrep.exe")]
    [InlineData("rg.exe")]
    [InlineData("pwsh.exe")]
    [InlineData("a-new-helper.exe")]
    public void Arbitrary_helpers_do_not_hide_their_owner(string helper) =>
        Assert.Equal("copilot", Owner(Shell(), Agent(), new(102, 101, helper, 30)));

    [Fact]
    public void Standalone_tgrep_is_not_an_agent() =>
        Assert.Null(Owner(Shell(), new(102, 100, "tgrep.exe", 30)));

    [Fact]
    public void Root_can_be_the_agent_itself() =>
        Assert.Equal("copilot", Owner(Agent(pid: 100, parent: 1)));

    [Fact]
    public void Missing_or_invalid_root_is_not_ownership()
    {
        Assert.Null(Owner(Agent()));
        Assert.Null(Owner(Shell(startedAt: 0), Agent()));
    }

    [Fact]
    public void Child_older_than_reused_parent_cannot_attach() =>
        Assert.Null(Owner(Shell(startedAt: 40), Agent(startedAt: 20)));

    [Fact]
    public void Intermediate_reused_parent_breaks_the_chain() =>
        Assert.Null(Owner(Shell(), Shell(110, 100, 40), Agent(parent: 110, startedAt: 20)));

    [Fact]
    public void Another_pane_root_is_a_boundary()
    {
        var tree = new ProcessSnapshot(new[] { Shell(), Shell(200, 100), Agent(parent: 200) });
        var roots = new HashSet<int> { 100, 200 };
        Assert.Null(tree.FindOwner(100, roots, WatchConfig.DefaultAgents));
        Assert.Equal("copilot", tree.FindOwner(200, roots, WatchConfig.DefaultAgents)!.Id);
    }

    [Theory]
    [InlineData("copilot.exe")]
    [InlineData("claude.exe")]
    public void Independent_owners_are_ambiguous(string second) =>
        Assert.Null(Owner(Shell(), Agent(), Agent(pid: 102, command: second)));

    [Fact]
    public void Parent_agent_owns_its_nested_agent() =>
        Assert.Equal("copilot", Owner(Shell(), Agent(), Agent(102, 101, "claude.exe", 30)));

    [Fact]
    public void Cycles_terminate_without_guessing() =>
        Assert.Null(Owner(Shell(parent: 110), Shell(110, 100)));

    [Fact]
    public void First_profile_wins_for_one_owner_with_duplicate_configured_commands()
    {
        var profiles = new[]
        {
            new AgentProfile { Id = "custom", Command = "copilot" }, WatchConfig.CopilotProfile(),
        };
        Assert.Equal("custom", new ProcessSnapshot(new[] { Shell(), Agent() }).FindOwner(100, Roots, profiles)!.Id);
    }
}
