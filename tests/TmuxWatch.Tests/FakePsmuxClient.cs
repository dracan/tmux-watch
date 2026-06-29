using TmuxWatch.Psmux;

namespace TmuxWatch.Tests;

/// <summary>
/// Scriptable fake. <see cref="ListOutput"/> and per-pane captures can be swapped
/// between ticks to simulate state transitions. Records switch calls so tests can
/// assert the TUI never sends input (it only ever calls switch/select here).
/// </summary>
public sealed class FakePsmuxClient : IPsmuxClient
{
    public bool Started { get; set; } = true;
    public int ExitCode { get; set; }
    public string ErrorMessage { get; set; } = "";
    public string ListOutput { get; set; } = "";
    public Dictionary<string, string> Captures { get; } = new();

    public List<string> SwitchedSessions { get; } = new();
    public List<string> SelectedWindows { get; } = new();

    public PsmuxResult ListPanesRaw(string format) =>
        Started ? new PsmuxResult(true, ExitCode, ListOutput, ErrorMessage)
                : PsmuxResult.NotStarted(ErrorMessage);

    public PsmuxResult CapturePane(string paneId) =>
        Captures.TryGetValue(paneId, out var text)
            ? new PsmuxResult(true, 0, text, "")
            : new PsmuxResult(true, 0, "", "");

    public PsmuxResult SwitchClient(string sessionName)
    {
        SwitchedSessions.Add(sessionName);
        return new PsmuxResult(true, 0, "", "");
    }

    public PsmuxResult SelectWindow(string windowTarget)
    {
        SelectedWindows.Add(windowTarget);
        return new PsmuxResult(true, 0, "", "");
    }
}
