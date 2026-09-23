namespace TmuxWatch.Tmux;

public sealed record TmuxResult(bool Started, int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => Started && ExitCode == 0;

    public static TmuxResult NotStarted(string message) => new(false, -1, "", message);
}

/// <summary>
/// Access to a pane multiplexer, bounded by the three tiers in AGENTS.md: a pane's
/// content is never written (there is deliberately no way to send input to a pane),
/// focus may be moved, and windows may be created on an explicit keystroke. Only the
/// verbs those tiers permit are exposed.
/// </summary>
public interface ITmuxClient
{
    /// <summary>Whether window_activity is a real window activity timestamp.</summary>
    bool SupportsWindowActivity => true;

    /// <summary>Raw output of a single <c>lsp -a -F</c> enumeration.</summary>
    TmuxResult ListPanesRaw(string format);

    /// <summary>Read-only capture of a pane's screen (<c>capture-pane -p -t</c>).</summary>
    TmuxResult CapturePane(string paneId);

    /// <summary>Switch the attached client to a session (focus only).</summary>
    TmuxResult SwitchClient(string sessionName);

    /// <summary>Select a window within a session (focus only).</summary>
    TmuxResult SelectWindow(string windowTarget);

    /// <summary>
    /// Select a pane within its window (focus only), so a jump lands on the exact pane a
    /// row names rather than on whichever pane that window last had active. This changes
    /// the window's active pane - server state other clients can observe - but injects no
    /// input; see the read-only guarantee in AGENTS.md.
    /// </summary>
    TmuxResult SelectPane(string paneId);

    /// <summary>
    /// Create a window in <paramref name="sessionName"/>, detached, so no client moves;
    /// the caller jumps to it separately through the focus verbs. This is the watcher's
    /// interactive new-window action and may only be reached from an explicit keystroke, never from
    /// the poll loop.
    /// <para>
    /// <paramref name="windowName"/> is user-entered text and MUST be used only as the
    /// window's name; implementations must never place it in a command position, since
    /// the underlying verb also accepts a shell command. A null or blank name is omitted
    /// entirely, leaving the multiplexer to name the window itself.
    /// </para>
    /// <para>
    /// On success the result's stdout carries the new window's id, so the caller can
    /// select it - a detached create does not make the window current.
    /// </para>
    /// </summary>
    TmuxResult NewWindow(string sessionName, string? windowName);
}
