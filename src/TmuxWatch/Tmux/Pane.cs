namespace TmuxWatch.Tmux;

/// <summary>
/// A single tmux pane as reported by <c>lsp -a -F</c>. <see cref="Id"/> (e.g. "%10")
/// is the stable key used across polls.
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
    bool PaneActive = false)
{
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

    /// <summary>Human-readable identifier: window name, falling back to the path label.</summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(WindowName) ? WindowName
        : !string.IsNullOrWhiteSpace(PathLabel) ? PathLabel
        : Location;
}
