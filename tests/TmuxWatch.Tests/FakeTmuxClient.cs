using TmuxWatch.Tmux;

namespace TmuxWatch.Tests;

/// <summary>
/// Scriptable fake. <see cref="ListOutput"/> and per-pane captures can be swapped
/// between ticks to simulate state transitions. Records switch calls so tests can
/// assert the TUI never sends input (it only ever calls switch/select here).
/// </summary>
public sealed class FakeTmuxClient : ITmuxClient
{
    public bool Started { get; set; } = true;
    public int ExitCode { get; set; }
    public string ErrorMessage { get; set; } = "";
    public string ListOutput { get; set; } = "";
    public Dictionary<string, string> Captures { get; } = new();

    public List<string> SwitchedSessions { get; } = new();
    public List<string> SelectedWindows { get; } = new();

    public TmuxResult ListPanesRaw(string format) =>
        Started ? new TmuxResult(true, ExitCode, ListOutput, ErrorMessage)
                : TmuxResult.NotStarted(ErrorMessage);

    public TmuxResult CapturePane(string paneId) =>
        Captures.TryGetValue(paneId, out var text)
            ? new TmuxResult(true, 0, text, "")
            : new TmuxResult(true, 0, "", "");

    public TmuxResult SwitchClient(string sessionName)
    {
        SwitchedSessions.Add(sessionName);
        return new TmuxResult(true, 0, "", "");
    }

    public TmuxResult SelectWindow(string windowTarget)
    {
        SelectedWindows.Add(windowTarget);
        return new TmuxResult(true, 0, "", "");
    }
}
