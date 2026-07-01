namespace TmuxWatch.Config;

/// <summary>
/// Opt-in configuration for the level-triggered pointer signal. When
/// <see cref="Enabled"/> is false (the default) tmux-watch never touches the OS
/// pointer. The pointer change is an OS call, not a terminal escape sequence, so it
/// behaves identically inside or outside tmux/PSMUX.
/// </summary>
public sealed class PointerSignalConfig
{
    /// <summary>Master opt-in. Default off; the signal is a no-op until enabled.</summary>
    public bool Enabled { get; set; } = false;

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
