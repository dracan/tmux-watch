namespace TmuxWatch.Tmux;

public sealed record TmuxResult(bool Started, int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => Started && ExitCode == 0;

    public static TmuxResult NotStarted(string message) => new(false, -1, "", message);
}

/// <summary>
/// Read-only-by-design access to a pane multiplexer. Only the verbs needed for
/// watching and focus-switching are exposed; there is deliberately no way to send
/// input to a watched pane.
/// </summary>
public interface ITmuxClient
{
    /// <summary>Raw output of a single <c>lsp -a -F</c> enumeration.</summary>
    TmuxResult ListPanesRaw(string format);

    /// <summary>Read-only capture of a pane's screen (<c>capture-pane -p -t</c>).</summary>
    TmuxResult CapturePane(string paneId);

    /// <summary>Switch the attached client to a session (focus only).</summary>
    TmuxResult SwitchClient(string sessionName);

    /// <summary>Select a window within a session (focus only).</summary>
    TmuxResult SelectWindow(string windowTarget);
}
