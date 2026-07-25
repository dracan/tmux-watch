namespace TmuxWatch.Tmux;

/// <summary>
/// A single tmux pane as reported by <c>lsp -a -F</c>. <see cref="Id"/> (e.g. "%10")
/// is the stable key used across polls; <see cref="Pid"/> (the pane's root process)
/// disambiguates a reused id, e.g. after a tmux server restart.
/// </summary>
public sealed record Pane(
    string Id,
    string SessionName,
    int WindowIndex,
    int PaneIndex,
    string Command,
    bool Dead,
    string WindowName = "",
    string CurrentPath = "",
    bool WindowActive = false,
    bool PaneActive = false,
    int Pid = 0,
    string AgentId = "",
    long WindowActivityUnix = 0)
{
    // AgentId is empty as parsed from tmux; discovery stamps it with the id of the
    // matched agent profile (e.g. "copilot", "claude").

    // WindowActivityUnix is tmux's #{window_activity} (unix seconds). It is
    // window-granular - tmux 3.4 exposes no pane-level equivalent - so every pane in a
    // split reports the same figure. 0 means "unknown" (absent, unparseable, or a host
    // that does not supply it).

    /// <summary>Target usable with tmux -t for window selection, e.g. "work:1".</summary>
    public string WindowTarget => $"{SessionName}:{WindowIndex}";

    public string Location => $"{SessionName}:{WindowIndex}.{PaneIndex}";

    /// <summary>True when this pane currently has focus (active pane of the active window).</summary>
    public bool IsFocused => WindowActive && PaneActive;

    /// <summary>Last path segment of the working directory, e.g. "my-project".</summary>
    public string PathLabel
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CurrentPath))
                return "";
            // Separator-agnostic: tmux on Linux reports POSIX paths ("/home/dan/foo"),
            // while a psmux/Windows host may report "\"-separated paths. Take the last
            // segment regardless of which separator the host uses.
            var trimmed = CurrentPath.TrimEnd('/', '\\');
            var idx = trimmed.LastIndexOfAny(new[] { '/', '\\' });
            return idx >= 0 && idx < trimmed.Length - 1 ? trimmed[(idx + 1)..] : trimmed;
        }
    }

    /// <summary>
    /// Time since this pane's window was last active, or null when tmux gave no usable
    /// activity timestamp. A clock skew that would yield a negative span reads as zero.
    /// </summary>
    public TimeSpan? TimeSinceActivity(DateTimeOffset now)
    {
        if (WindowActivityUnix <= 0)
            return null;
        var elapsed = now - DateTimeOffset.FromUnixTimeSeconds(WindowActivityUnix);
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    /// <summary>Human-readable identifier: window name, falling back to the path label.</summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(WindowName) ? WindowName
        : !string.IsNullOrWhiteSpace(PathLabel) ? PathLabel
        : Location;
}
