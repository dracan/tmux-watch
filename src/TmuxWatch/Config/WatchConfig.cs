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

    /// <summary>Number of trailing non-blank lines treated as the status area. Wide
    /// enough to reach the current Claude build's live spinner line above the input
    /// box (with a sub-agent panel below) and tall selection-menu cursors.</summary>
    public int StatusLineCount { get; set; } = 16;

    /// <summary>
    /// How many consecutive enumerations a tracked pane may be absent from before it
    /// is dropped. A transient empty or partial <c>lsp</c> result (host under load)
    /// must not wipe every tracked pane and re-add them as "first sight" next tick -
    /// that would reset every in-state timer and re-fire the WAITING/DONE chime for
    /// panes that never changed. Retaining an absent pane for a few ticks bridges the
    /// gap; a genuinely closed pane still drops once the threshold is crossed. Minimum
    /// 1 (drop on the first absence, the pre-debounce behaviour).
    /// </summary>
    public int MissedEnumerationsBeforeDrop { get; set; } = 3;

    /// <summary>
    /// How many consecutive Unknown classifications a pane may accumulate before its
    /// held state goes stale and Unknown is surfaced. Holding through a handful of
    /// Unknowns bridges failed/empty/mid-redraw captures without flapping timers or
    /// re-firing chimes, but the hold must be bounded: a pane that classifies Unknown
    /// forever (e.g. profile tokens drifted after an agent upgrade, or the pane sits
    /// in copy-mode) must eventually show Unknown so the lost classification is
    /// visible rather than masked by a frozen stale state. Minimum 1 (surface on the
    /// first Unknown, the pre-hold behaviour). Default 15 ticks (~30s at the default
    /// 2s poll).
    /// </summary>
    public int UnknownCapturesBeforeStale { get; set; } = 15;

    /// <summary>Notification channel: "bell", "none" (extendable, e.g. "toast").</summary>
    public string NotificationChannel { get; set; } = "bell";

    /// <summary>
    /// Pointer signal: recolour the OS mouse pointer while any pane is WAITING or
    /// DONE. Default on; disable via config. See <see cref="PointerSignalConfig"/>.
    /// </summary>
    public PointerSignalConfig PointerSignal { get; set; } = new();

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
    /// shared with Copilot. WORKING keys on the current build's live spinner line -
    /// a line starting with an asterisk spinner glyph plus a live qualifier (an
    /// ellipsis, a "(32s · " activity meter, or the background-sub-agents wait) -
    /// because the build no longer prints "esc to interrupt". IDLE keys on the
    /// input-box mode line. These tokens are build-specific - run <c>--calibrate</c>
    /// against a live Claude pane to confirm them after a Claude Code upgrade and
    /// override here if they change.
    /// </summary>
    public static AgentProfile ClaudeProfile() => new()
    {
        Id = "claude",
        Command = "claude",
        WaitingCursorPattern = @"❯\s*\d+\.",
        WaitingFooterNavMarker = "↑/↓",
        WaitingFooterCancelMarker = "esc to cancel",
        // Asterisk animation frames only (no "·" middle dot - it appears in meters and
        // footers, so it must not count as a line-start spinner glyph).
        WorkingSpinnerGlyphs = "✻✽✶✷✸✹✺✢✳∗",
        WorkingWord = "",                       // Claude shows an action label, not "Working"
        WorkingFooterCancelMarker = "",         // gone from the current build
        WorkingMarkerSufficient = false,
        WorkingLiveSpinnerSufficient = true,
        WorkingLiveEllipsisPattern = @"…|\.\.\.",
        WorkingLiveMeterPattern = @"\(\d+[smh][^)]*·",
        WorkingBackgroundAgentsPattern = @"Waiting for \d+ background agent",
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
