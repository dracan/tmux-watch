using System.Text.RegularExpressions;

namespace TmuxWatch.Config;

/// <summary>
/// A watched coding-agent CLI: how to recognise its panes (foreground command and
/// an optional session-name backstop) plus the version-specific status-bar tokens
/// used to classify them. Built-in profiles exist for Copilot and Claude Code; the
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

    /// <summary>Any one of these tokens in the status area indicates IDLE.</summary>
    public List<string> IdleHints { get; set; } = new();

    // ---- Compiled helpers ---------------------------------------------------

    public Regex CompileWaitingCursor() => new(WaitingCursorPattern, RegexOptions.Compiled);

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

    private static Regex? CompileOrNull(string pattern) =>
        string.IsNullOrEmpty(pattern) ? null : new Regex(pattern, RegexOptions.Compiled);

    private string SpinnerGlyphClass()
    {
        var glyphs = string.Concat(WorkingSpinnerGlyphs.Select(c => Regex.Escape(c.ToString())));
        return glyphs.Length == 0 ? "(?!)" : $"[{glyphs}]"; // (?!) never matches when no glyphs set
    }

    public Regex? CompileSessionConvention() =>
        string.IsNullOrWhiteSpace(SessionNameConvention)
            ? null
            : new Regex(SessionNameConvention, RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
