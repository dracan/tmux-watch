using TmuxWatch.Psmux;

namespace TmuxWatch.Tests;

public class PsmuxRunnerTests
{
    [Theory]
    [InlineData("send-keys")]
    [InlineData("send")]
    [InlineData("kill-pane")]
    [InlineData("paste-buffer")]
    public void Forbids_input_injecting_verbs(string verb)
    {
        var runner = new PsmuxRunner("psmux-does-not-exist");
        Assert.Throws<InvalidOperationException>(() => runner.Run(verb, "-t", "%1", "hello"));
    }

    [Theory]
    [InlineData("lsp")]
    [InlineData("capture-pane")]
    [InlineData("switch-client")]
    [InlineData("select-window")]
    public void Allows_read_and_focus_verbs(string verb)
    {
        // Non-existent executable => NotStarted result, but the verb guard must pass
        // (no InvalidOperationException) for whitelisted verbs.
        var runner = new PsmuxRunner("psmux-does-not-exist-xyz");
        var result = runner.Run(verb, "-t", "%1");
        Assert.False(result.Started);
    }
}
