using System.Text.RegularExpressions;

namespace TmuxWatch.Config;

/// <summary>
/// A watched coding-agent CLI: how to recognise its panes (foreground command and
/// an optional session-name backstop) plus the version-specific status-bar tokens
/// used to classify them. Built-in profiles exist for Copilot, Claude Code, and Codex; the
/// set is overridable via configuration so supporting a new agent is a config
/// change, not a code change.
/// </summary>
public sealed class AgentProfile
{
    public string Id { get; set; } = "agent";

    /// <summary>Foreground command that identifies this agent (extension-insensitive).</summary>
    public string Command { get; set; } = "";

    /// <summary>
    /// Optional regex on the session name used as an identity backstop when the
    /// foreground command does not match. Empty disables the backstop.
    /// </summary>
    public string SessionNameConvention { get; set; } = "";

    // ---- Detection tokens (agent/version-specific, overridable) -------------

    /// <summary>Numbered selection cursor, e.g. "❯ 1." (shared by Copilot and Claude prompts).</summary>
    public string WaitingCursorPattern { get; set; } = @"❯\s*\d+\.";

    /// <summary>Up/down marker present in a selection footer.</summary>
    public string WaitingFooterNavMarker { get; set; } = "↑/↓";

    /// <summary>Cancel text common to a selection footer.</summary>
    public string WaitingFooterCancelMarker { get; set; } = "esc to cancel";

    /// <summary>
    /// Optional regex for a blocking footer below the last composer, or anywhere in
    /// the scanned tail when no composer is present. Unlike the legacy footer pair,
    /// this excludes old transcript hints. Covers question editors that use the same
    /// caret as the composer and therefore hide their selection cursor above it.
    /// Empty disables this check.
    /// </summary>
    public string WaitingChromePattern { get; set; } = "";

    /// <summary>Optional multiline regex for a blocking panel in the scanned tail.
    /// Anchor the panel boundaries to exclude transcript prose. Empty disables it.</summary>
    public string WaitingPanelPattern { get; set; } = "";

    /// <summary>
    /// Optional regex for a complete live status line in the scanned tail. Use anchors
    /// and live qualifiers to exclude ordinary prose and frozen completion banners.
    /// Empty disables this check.
    /// </summary>
    public string WorkingLinePattern { get; set; } = "";

    /// <summary>Spinner glyphs shown while the agent is working.</summary>
    public string WorkingSpinnerGlyphs { get; set; } = "◎◉●○";

    /// <summary>Literal "working" word, if the agent shows one. Empty if it does not.</summary>
    public string WorkingWord { get; set; } = "Working";

    /// <summary>Cancel text on the working footer (e.g. "esc cancel" / "esc to interrupt").</summary>
    public string WorkingFooterCancelMarker { get; set; } = "esc cancel";

    /// <summary>
    /// When true, the working cancel marker on a line is sufficient to classify
    /// WORKING without also requiring a spinner glyph. Claude Code's spinner glyph
    /// set animates and is build-specific, but its "esc to interrupt" marker is
    /// distinctive, so the marker alone is a robust signal.
    /// </summary>
    public bool WorkingMarkerSufficient { get; set; }

    /// <summary>
    /// When true, a status line that begins (after leading whitespace) with a spinner
    /// glyph is classified WORKING provided it also carries a "live" qualifier - an
    /// ellipsis or a live activity meter (see <see cref="WorkingLiveEllipsisPattern"/>
    /// / <see cref="WorkingLiveMeterPattern"/>) - or the background-sub-agents wait
    /// phrase (<see cref="WorkingBackgroundAgentsPattern"/>). This is how the current
    /// Claude Code build is detected now that it no longer prints a working cancel
    /// marker: the action word is random ("Tinkering"/"Enchanting") and the glyph is
    /// one animation frame, so neither is a stable hook alone, while a frozen
    /// past-tense completed line (same glyph, "&lt;Word&gt; for &lt;dur&gt;") carries
    /// none of the qualifiers and is excluded.
    /// </summary>
    public bool WorkingLiveSpinnerSufficient { get; set; }

    /// <summary>Regex matching a live-activity ellipsis on a spinner line (either the
    /// single glyph … or three ASCII dots). Empty disables the ellipsis qualifier.</summary>
    public string WorkingLiveEllipsisPattern { get; set; } = "";

    /// <summary>Regex matching a live activity meter on a spinner line, e.g. the
    /// "(32s · " that opens "(32s · ↓ 1.4k tokens)". Empty disables the qualifier.</summary>
    public string WorkingLiveMeterPattern { get; set; } = "";

    /// <summary>Regex matching the background-sub-agents wait line (treated as WORKING
    /// because the pane is busy, not awaiting the user). Empty disables it.</summary>
    public string WorkingBackgroundAgentsPattern { get; set; } = "";

    /// <summary>
    /// Regex matching the agent's composer prompt line - the input caret the user types
    /// at, e.g. a `❯` at the start of a line that is not the numbered selection cursor.
    /// When set, this is the IDLE anchor and <see cref="IdleHints"/> is not consulted;
    /// empty falls back to the hints.
    ///
    /// It is a sturdier anchor than any status-footer token because it is structure
    /// rather than an affordance hint: it is present in every live state and in every
    /// editor mode, and the agent has no reason to recycle its slot. Claude Code's footer
    /// segments, by contrast, are all conditional - `? for shortcuts` renders only while
    /// the composer is empty, and `(shift+tab to cycle)` is dropped whenever the footer
    /// needs the space, including while a background shell runs. A pane hitting both at
    /// once had no IDLE signal left and classified Unknown.
    ///
    /// The composer line also splits the screen into the agent's transcript (above) and
    /// its chrome (below), which is what makes
    /// <see cref="BackgroundTaskPattern"/> safe to match.
    /// </summary>
    public string IdlePromptPattern { get; set; } = "";

    /// <summary>
    /// Regex matching a background-task counter in the status chrome, e.g. the
    /// `· 1 shell ·` or `· 2 monitors ·` the current Claude Code build renders while work
    /// it started is still running after the turn ended. Both kinds share one footer slot
    /// (`1 shell · 1 monitor`), and both mean the same thing here: the agent has handed
    /// the turn back but its work has not finished. Empty disables BACKGND for this
    /// profile.
    ///
    /// Only lines *below* the <see cref="IdlePromptPattern"/> match are searched. The
    /// transcript above it routinely contains shell prose that would otherwise match -
    /// a tool-use line (`Ran 1 shell command`) and, worse, an end-of-turn line
    /// (`✻ Cooked for 16s · 1 shell still running`) that stays frozen on screen after the
    /// shell has exited and would pin the pane in BACKGND indefinitely.
    /// </summary>
    public string BackgroundTaskPattern { get; set; } = "";

    /// <summary>
    /// Regex matching a background-*agent* row in the status chrome - the row the agent CLI
    /// renders once per detached sub-agent that is still running, e.g. the `◯ slow-sweep …`
    /// lines of Claude Code's fleet panel. Empty disables this fingerprint for the profile.
    ///
    /// This is a second, independent BACKGND fingerprint rather than an alternation inside
    /// <see cref="BackgroundTaskPattern"/>, because agents never reach that counter's slot:
    /// a pane running a detached sub-agent and nothing else renders the ordinary
    /// `(shift+tab to cycle)` hint there. The counter *does* pick up a sub-agent's own
    /// shells and monitors, which is why the gap presented as an intermittent flicker
    /// rather than a constant miss - it was sampling the sub-agent's incidental resource
    /// use, not the sub-agent.
    ///
    /// Keeping the two separate also keeps each honest: the counter is a middot-delimited
    /// footer *segment*, this is a *row* in a panel. They are different UI surfaces and will
    /// drift on different schedules, so they stay independently overridable.
    ///
    /// Searched over the same below-composer chrome as the counter, and for the same reason:
    /// the transcript keeps a delegation line (`Running in the background as @name`) that
    /// outlives the sub-agent by the rest of the session.
    /// </summary>
    public string BackgroundAgentRowPattern { get; set; } = "";

    /// <summary>Any one of these tokens in the status area indicates IDLE. Used only when
    /// <see cref="IdlePromptPattern"/> is empty.</summary>
    public List<string> IdleHints { get; set; } = new();

    // ---- Compiled helpers ---------------------------------------------------

    public Regex CompileWaitingCursor() => new(WaitingCursorPattern, RegexOptions.Compiled);

    public Regex? CompileWaitingChrome() => CompileOrNull(WaitingChromePattern);

    public Regex? CompileWaitingPanel() => CompileOrNull(WaitingPanelPattern);

    public Regex? CompileWorkingLine() => CompileOrNull(WorkingLinePattern);

    /// <summary>Spinner-glyph-immediately-before-the-working-word, or null when no word is configured.</summary>
    public Regex? CompileWorkingSpinner() =>
        string.IsNullOrEmpty(WorkingWord)
            ? null
            : new Regex($"{SpinnerGlyphClass()}\\s*{Regex.Escape(WorkingWord)}", RegexOptions.Compiled);

    /// <summary>Matches a single spinner glyph anywhere on a line.</summary>
    public Regex CompileSpinnerGlyph() => new(SpinnerGlyphClass(), RegexOptions.Compiled);

    /// <summary>Matches a spinner glyph at the start of a line (after leading whitespace).</summary>
    public Regex CompileWorkingLineStartGlyph() =>
        new($"^\\s*{SpinnerGlyphClass()}", RegexOptions.Compiled);

    /// <summary>Compiled live-ellipsis qualifier, or null when none is configured.</summary>
    public Regex? CompileWorkingLiveEllipsis() => CompileOrNull(WorkingLiveEllipsisPattern);

    /// <summary>Compiled live-meter qualifier, or null when none is configured.</summary>
    public Regex? CompileWorkingLiveMeter() => CompileOrNull(WorkingLiveMeterPattern);

    /// <summary>Compiled background-sub-agents qualifier, or null when none is configured.</summary>
    public Regex? CompileWorkingBackgroundAgents() => CompileOrNull(WorkingBackgroundAgentsPattern);

    /// <summary>Compiled composer-prompt anchor, or null when none is configured.</summary>
    public Regex? CompileIdlePrompt() => CompileOrNull(IdlePromptPattern);

    /// <summary>Compiled background-task counter, or null when none is configured.</summary>
    public Regex? CompileBackgroundTask() => CompileOrNull(BackgroundTaskPattern);

    /// <summary>Compiled background-agent row, or null when none is configured.</summary>
    public Regex? CompileBackgroundAgentRow() => CompileOrNull(BackgroundAgentRowPattern);

    private static Regex? CompileOrNull(string pattern) =>
        string.IsNullOrEmpty(pattern) ? null : new Regex(pattern, RegexOptions.Compiled);

    private string SpinnerGlyphClass()
    {
        var glyphs = string.Concat(WorkingSpinnerGlyphs.Select(c => Regex.Escape(c.ToString())));
        return glyphs.Length == 0 ? "(?!)" : $"[{glyphs}]"; // (?!) never matches when no glyphs set
    }

    /// <summary>
    /// Cache for <see cref="CompileSessionConvention"/>, keyed on the pattern the
    /// compiled regex came from. Unlike the classifier's patterns - compiled once into
    /// fields by its constructor - this one is reached through <see cref="MatchesSession"/>
    /// on the discovery hot path: once per unmatched pane per profile, every poll. A
    /// RegexOptions.Compiled build emits IL, so recompiling it there burned dozens of
    /// throwaway regexes a tick. The properties on this type are settable (it is
    /// deserialised from config), hence keying on the pattern rather than a plain
    /// once-only field: a later edit to the convention still takes effect.
    /// </summary>
    private (string Pattern, Regex? Compiled)? _sessionConvention;

    public Regex? CompileSessionConvention()
    {
        var pattern = SessionNameConvention;
        if (_sessionConvention is { } cached &&
            string.Equals(cached.Pattern, pattern, StringComparison.Ordinal))
            return cached.Compiled;

        var compiled = string.IsNullOrWhiteSpace(pattern)
            ? null
            : new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        _sessionConvention = (pattern, compiled);
        return compiled;
    }

    /// <summary>
    /// Matches the foreground command against this agent's command, comparing the
    /// extensionless stem too so a Windows/psmux host ("claude.exe"/"copilot.exe")
    /// matches the bare command used on Linux/macOS.
    /// </summary>
    public bool MatchesCommand(string command)
    {
        if (string.IsNullOrEmpty(Command))
            return false;
        if (string.Equals(command, Command, StringComparison.OrdinalIgnoreCase))
            return true;
        var stem = Path.GetFileNameWithoutExtension(command);
        return stem.Length > 0 && string.Equals(stem, Command, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the session name matches this agent's configured backstop.</summary>
    public bool MatchesSession(string sessionName) =>
        CompileSessionConvention() is { } rx && rx.IsMatch(sessionName);
}
