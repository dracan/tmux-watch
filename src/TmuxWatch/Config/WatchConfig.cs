using System.Text.Json;

namespace TmuxWatch.Config;

/// <summary>
/// Global watcher behaviour plus the set of <see cref="AgentProfile"/>s to watch.
/// When <see cref="Agents"/> is unset the built-in Copilot, Claude Code, and Codex profiles
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

    /// <summary>
    /// How long a pane may sit in BACKGND holding an unannounced completed turn before
    /// the completed-turn notification fires anyway, without waiting for the background
    /// shell to exit.
    ///
    /// Deferring the chime is the point of BACKGND - a turn that ends into a live shell
    /// is not finished work yet - but the deferral cannot be unbounded, or a shell that
    /// never exits (a dev server) would swallow the announcement entirely.
    ///
    /// Deliberately long: at the default 2s poll, 120s is 60 consecutive polls, so a
    /// transient WORKING miss cannot reach it. A pane that sustains a misclassification
    /// for two full minutes has a genuinely broken WORKING detector, which is a problem
    /// this fallback neither causes nor conceals.
    /// </summary>
    public double BackgroundGraceSeconds { get; set; } = 120.0;

    /// <summary>
    /// How long an acknowledged pane keeps the position it had before the
    /// acknowledgement, so the row the user just addressed does not leap down the table
    /// in response to their own keystroke.
    ///
    /// Acknowledging demotes a pane from DONE to IDLE - four priority ranks - and the
    /// view is rebuilt in the same frame, before tmux has reported anything new. Without
    /// a settle time the movement reads as the UI reacting to the keypress, and because
    /// address keys are positional and reassigned every frame, the next key of a triage
    /// burst is aimed at a layout that no longer exists.
    ///
    /// Held panes are released only by a poll at or after the deadline, never by a
    /// keystroke-driven rebuild, so the eventual movement does not coincide with a
    /// keypress either. Deliberately short: this is a settle time, not a freeze, and the
    /// row's state, colour, and cue update immediately regardless. Set to 0 to disable
    /// the hold and demote at once.
    /// </summary>
    public double AckHoldSeconds { get; set; } = 5.0;

    /// <summary>Notification channel: "bell", "none" (extendable, e.g. "toast").</summary>
    public string NotificationChannel { get; set; } = "bell";

    /// <summary>
    /// Pointer signal: recolour the OS mouse pointer while any pane is WAITING or
    /// DONE. Default on; disable via config. See <see cref="PointerSignalConfig"/>.
    /// </summary>
    public PointerSignalConfig PointerSignal { get; set; } = new();

    /// <summary>
    /// Agent profiles to watch. When null or empty, the built-in Copilot, Claude
    /// Code, and Codex defaults are used.
    /// </summary>
    public List<AgentProfile>? Agents { get; set; }

    public IReadOnlyList<AgentProfile> ResolveAgents() =>
        Agents is { Count: > 0 } ? Agents : DefaultAgents;

    public static IReadOnlyList<AgentProfile> DefaultAgents { get; } = new List<AgentProfile>
    {
        CopilotProfile(),
        ClaudeProfile(),
        CodexProfile(),
    };

    /// <summary>
    /// Codex CLI core states. Discovery, composer, and working status verified on
    /// 0.153.4; approval/question shapes checked against upstream renderer snapshots
    /// (see fixtures/codex-provenance.md). Question editors share the composer caret
    /// and can also say "esc to interrupt", so their submit footer takes precedence
    /// and WORKING requires a complete timed status line. No BACKGND inference.
    /// </summary>
    public static AgentProfile CodexProfile() => new()
    {
        Id = "codex",
        Command = "codex",
        WaitingCursorPattern = @"^\s*\u203A\s*\d+\.",
        WaitingFooterNavMarker = "",
        WaitingFooterCancelMarker = "",
        WaitingChromePattern = @"^\s*(?:Press enter to confirm or esc to (?:cancel|go back)|(?:[^|\r\n]+\|\s*)*enter to submit (?:answer|all)(?:\s*\|[^\r\n]*)?)\s*$",
        WorkingSpinnerGlyphs = "",
        WorkingWord = "",
        WorkingFooterCancelMarker = "",
        WorkingLinePattern = @"^\s*[\u2022\u25E6]\s+[^()\r\n]+\(\d+[smh](?:\s+\d+[smh])*\s+\u2022\s+esc to interrupt\)(?:\s+\u00B7\s+\d+ background terminals? running(?:\s+\u00B7\s+[^\r\n]+)?)?\s*$",
        IdlePromptPattern = @"^\s*\u203A(?!\s*\d+\.)",
    };

    /// <summary>Built-in Copilot CLI profile (core and form tokens verified against Copilot v1.0.86).</summary>
    public static AgentProfile CopilotProfile() => new()
    {
        Id = "copilot",
        Command = "copilot",
        // The question panel replaces the composer. Its heading and enclosing
        // rules are invariant across choice, text, and multiple-field forms.
        WaitingPanelPattern = @"(?m)^\u2500{3,}\n[ \t]*Copilot needs information\.[ \t]*\n(?:(?!\u2500)[^\n]*\n)+\u2500{3,}\z",
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
    /// because the build no longer prints "esc to interrupt".
    ///
    /// IDLE keys on the **composer box** (<see cref="AgentProfile.IdlePromptPattern"/>),
    /// not on the status footer. It used to key on the footer's affordance hints, but
    /// every segment of that footer is conditional: `? for shortcuts` renders only while
    /// the composer is empty, and `(shift+tab to cycle)` is displaced whenever the footer
    /// needs the space - notably by the background-task counter. A pane with a running
    /// shell *and* typed text lost both hints at once and classified Unknown, which also
    /// made it ineligible for DONE (derived from the WORKING to IDLE edge), so it never
    /// chimed. The composer prompt is the input caret rather than a hint, is present in
    /// every live state, and renders identically in vim insert and normal modes.
    /// <see cref="AgentProfile.IdleHints"/> is therefore left empty here.
    ///
    /// BACKGND has two fingerprints, either sufficient, both matched only below the composer
    /// line so frozen transcript prose cannot trigger them. The first is that same displaced
    /// footer segment (`· 1 shell ·`, `· 2 monitors ·`). The second is a fleet-panel agent
    /// row, for a sub-agent launched *detached* - Claude reports it as running in the
    /// background and hands the turn straight back, so the pane is not blocked and is not
    /// WORKING. The counter cannot cover that case: agents are never counted in that slot.
    ///
    /// The blocked form of the same feature - `Waiting for N background agents to finish` -
    /// stays WORKING via <see cref="AgentProfile.WorkingBackgroundAgentsPattern"/>. The two
    /// are told apart by the live status line alone, and the existing precedence (WORKING
    /// above BACKGND) resolves a screen showing both.
    ///
    /// These tokens are build-specific - run <c>--calibrate</c> against a live Claude
    /// pane to confirm them after a Claude Code upgrade and override here if they change.
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
        // Composer prompt at line start, excluding the numbered selection cursor
        // ("❯ 1.") which is the WAITING signal sharing the same glyph.
        IdlePromptPattern = @"^\s*❯(?!\s*\d+\.)",
        // A footer segment, not prose: the middot separator must precede the count, and
        // the segment must end at another separator or the end of the line. That is what
        // excludes "Ran 1 shell command" and "· 1 shell still running". Shells and
        // monitors share the slot ("· 1 shell · 1 monitor ·") and mean the same thing to
        // the watcher, so both count.
        BackgroundTaskPattern = @"·\s*\d+\s+(shells?|monitors?)\s*(·|$)",
        // A fleet-panel row, one per live detached sub-agent. `◯` (U+25EF LARGE CIRCLE) is
        // the per-agent bullet; `●` (U+25CF BLACK CIRCLE) is the `main` row, which renders
        // whether or not any agent is running and must not match. Both glyphs were read
        // from raw capture bytes, not inferred from a screenshot - they are easily confused.
        //
        // The row's trailing meter (`2m 5s · ↓ 104.3k tokens`) is deliberately NOT part of
        // the token. It is paren-less, unlike WorkingLiveMeterPattern - which is precisely
        // why such a pane classified IDLE rather than WORKING - and a just-launched agent
        // renders a bare `0s` with no separator and no counter at all, so a meter-shaped
        // token would miss the opening seconds of every agent. The bullet is structure; the
        // meter is decoration.
        BackgroundAgentRowPattern = @"^\s*◯",
        IdleHints = new(),                      // superseded by IdlePromptPattern
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
