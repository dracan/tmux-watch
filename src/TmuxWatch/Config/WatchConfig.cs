using System.Text.Json;

namespace TmuxWatch.Config;

/// <summary>
/// Global watcher behaviour plus the set of <see cref="AgentProfile"/>s to watch.
/// When <see cref="Agents"/> is unset the built-in Copilot and Claude Code profiles
/// are used; supply <see cref="Agents"/> in config to override or extend them.
/// </summary>
public sealed class WatchConfig
{
    /// <summary>
    /// Multiplexer executable on PATH. Defaults to <c>tmux</c>; override to
    /// <c>psmux</c> (or another tmux-compatible CLI) to run against a different host.
    /// </summary>
    public string TmuxExecutable { get; set; } = "tmux";

    public double PollIntervalSeconds { get; set; } = 2.0;

    /// <summary>Number of trailing non-blank lines treated as the status area.</summary>
    public int StatusLineCount { get; set; } = 6;

    public bool NotifyOnIdle { get; set; } = false;

    /// <summary>Notification channel: "bell", "none" (extendable, e.g. "toast").</summary>
    public string NotificationChannel { get; set; } = "bell";

    /// <summary>
    /// Agent profiles to watch. When null or empty, the built-in Copilot + Claude
    /// Code defaults are used.
    /// </summary>
    public List<AgentProfile>? Agents { get; set; }

    public IReadOnlyList<AgentProfile> ResolveAgents() =>
        Agents is { Count: > 0 } ? Agents : DefaultAgents;

    public static IReadOnlyList<AgentProfile> DefaultAgents { get; } = new List<AgentProfile>
    {
        CopilotProfile(),
        ClaudeProfile(),
    };

    /// <summary>Built-in Copilot CLI profile (tokens verified against Copilot v1.0.63).</summary>
    public static AgentProfile CopilotProfile() => new()
    {
        Id = "copilot",
        Command = "copilot",
        WaitingCursorPattern = @"❯\s*\d+\.",
        WaitingFooterNavMarker = "↑/↓",
        WaitingFooterCancelMarker = "esc to cancel",
        WorkingSpinnerGlyphs = "◎◉●○",
        WorkingWord = "Working",
        WorkingFooterCancelMarker = "esc cancel",
        WorkingMarkerSufficient = false,
        IdleHints = new() { "/ commands", "? help", "space hold to record" },
    };

    /// <summary>
    /// Built-in Claude Code profile. tmux reports <c>pane_current_command</c> as
    /// <c>claude</c>, so identity is a plain command match. The WAITING cursor is
    /// shared with Copilot; WORKING keys on the distinctive "esc to interrupt"
    /// marker; IDLE keys on the input-box mode line. The IDLE/WORKING tokens are
    /// provisional - run <c>--calibrate</c> against a live Claude pane to confirm
    /// them after a Claude Code upgrade and override here if they change.
    /// </summary>
    public static AgentProfile ClaudeProfile() => new()
    {
        Id = "claude",
        Command = "claude",
        WaitingCursorPattern = @"❯\s*\d+\.",
        WaitingFooterNavMarker = "↑/↓",
        WaitingFooterCancelMarker = "esc to cancel",
        WorkingSpinnerGlyphs = "✻✽✶✷✸✹✺·✢✳∗",
        WorkingWord = "",                       // Claude shows an action label, not "Working"
        WorkingFooterCancelMarker = "esc to interrupt",
        WorkingMarkerSufficient = true,         // "esc to interrupt" alone is distinctive
        IdleHints = new() { "shift+tab to cycle", "? for shortcuts" },
    };

    public static WatchConfig Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new WatchConfig();

        var json = File.ReadAllText(path);
        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        return JsonSerializer.Deserialize<WatchConfig>(json, opts) ?? new WatchConfig();
    }
}
