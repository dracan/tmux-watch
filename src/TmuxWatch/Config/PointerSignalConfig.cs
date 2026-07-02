namespace TmuxWatch.Config;

/// <summary>
/// Configuration for the level-triggered pointer signal. On by default (a no-op on
/// hosts with no path to the Windows pointer); set <see cref="Enabled"/> to false to
/// keep tmux-watch away from the OS pointer entirely. The pointer change is an OS
/// call, not a terminal escape sequence, so it behaves identically inside or outside
/// tmux/PSMUX.
/// </summary>
public sealed class PointerSignalConfig
{
    /// <summary>Master switch. Default on; set false to disable the signal.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Path to the cursor asset shown while any pane is WAITING. Relative paths are
    /// resolved against the app base directory. Defaults to the shipped red arrow.
    /// </summary>
    public string WaitingCursorFile { get; set; } = "assets/waiting-cursor.cur";

    /// <summary>
    /// Path to the cursor asset shown while any pane is DONE (a finished turn) and none
    /// is WAITING. Relative paths are resolved against the app base directory. Defaults
    /// to the shipped green arrow.
    /// </summary>
    public string DoneCursorFile { get; set; } = "assets/done-cursor.cur";

    /// <summary>
    /// Which OS pointer shapes to recolour. "arrow" covers other applications;
    /// "ibeam" covers the terminal's text area.
    /// </summary>
    public List<string> Shapes { get; set; } = new() { "arrow", "ibeam" };
}
