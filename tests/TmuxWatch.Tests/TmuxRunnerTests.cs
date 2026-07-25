using TmuxWatch.Tmux;

namespace TmuxWatch.Tests;

public class TmuxRunnerTests
{
    [Theory]
    [InlineData("send-keys")]
    [InlineData("send")]
    [InlineData("kill-pane")]
    [InlineData("paste-buffer")]
    [InlineData("send-prefix")]
    [InlineData("respawn-pane")]
    public void Forbids_input_injecting_verbs(string verb)
    {
        var runner = new TmuxRunner("tmux-does-not-exist");
        Assert.Throws<InvalidOperationException>(() => runner.Run(verb, "-t", "%1", "hello"));
    }

    [Theory]
    [InlineData("lsp")]
    [InlineData("capture-pane")]
    [InlineData("switch-client")]
    [InlineData("select-window")]
    [InlineData("select-pane")]
    public void Allows_read_and_focus_verbs(string verb)
    {
        // Non-existent executable => NotStarted result, but the verb guard must pass
        // (no InvalidOperationException) for whitelisted verbs.
        var runner = new TmuxRunner("tmux-does-not-exist-xyz");
        var result = runner.Run(verb, "-t", "%1");
        Assert.False(result.Started);
    }

    [Fact]
    public void Select_pane_is_the_only_verb_added_for_pane_precise_jumps()
    {
        // Pane selection is permitted (focus only, no input); input injection is not, and
        // widening the whitelist for select-pane must not have loosened that.
        var runner = new TmuxRunner("tmux-does-not-exist-xyz");

        Assert.False(runner.SelectPane("%1").Started);
        Assert.Throws<InvalidOperationException>(() => runner.Run("send-keys", "-t", "%1", "x"));
    }
}
