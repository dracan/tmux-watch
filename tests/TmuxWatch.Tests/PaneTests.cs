using TmuxWatch.Tmux;

namespace TmuxWatch.Tests;

public class PaneTests
{
    private static Pane WithPath(string path) =>
        new("%1", "work", 0, 0, "copilot", Dead: false, CurrentPath: path);

    [Theory]
    [InlineData("/home/dan/code/my-project", "my-project")]   // POSIX (WSL2/tmux)
    [InlineData("/home/dan/code/my-project/", "my-project")]  // trailing slash
    [InlineData("C:\\dev\\my-project", "my-project")]  // Windows (psmux host)
    [InlineData("/", "")]                                      // root only
    [InlineData("", "")]                                       // empty
    public void PathLabel_returns_last_segment_for_any_separator(string path, string expected)
    {
        Assert.Equal(expected, WithPath(path).PathLabel);
    }
}
