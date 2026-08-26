using System.Text.RegularExpressions;
using TmuxWatch.Config;

namespace TmuxWatch.Detection;

/// <summary>
/// Pure classifier: turns a captured pane screen into a <see cref="PaneState"/>
/// using one <see cref="AgentProfile"/>'s positive status-bar fingerprints. Applies
/// a fixed precedence (WAITING → WORKING → BACKGND → IDLE → DEAD) so a screen matching
/// more than one signal resolves predictably. Holds no mutable state, so it is trivially
/// fixture-testable. The caller selects the profile for a pane during discovery.
///
/// BACKGND sits below WORKING because the background-task counter stays in the chrome
/// while the agent works, and above IDLE because a screen matching both is making the
/// more specific claim.
///
/// Where the profile configures a composer prompt, that line also splits the scanned
/// region in two: the agent's **transcript** above it and its **chrome** below. The
/// distinction is load-bearing, not cosmetic - the transcript keeps prose that never was
/// a live signal, or has outlived the thing it describes ("· 1 shell still running" after
/// the shell exited, "Running in the background as @name" after the sub-agent reported, a
/// "❯ 1." written out in a sentence), so only the chrome may be searched for the BACKGND
/// fingerprints and the WAITING cursor.
/// </summary>
public sealed class PaneClassifier
{
    private readonly AgentProfile _profile;
    private readonly int _statusLineCount;
    private readonly Regex _waitingCursor;
    private readonly Regex? _workingSpinner;
    private readonly Regex _spinnerGlyph;
    private readonly Regex _workingLineStartGlyph;
    private readonly Regex? _workingLiveEllipsis;
    private readonly Regex? _workingLiveMeter;
    private readonly Regex? _workingBackgroundAgents;
    private readonly Regex? _idlePrompt;
    private readonly Regex? _backgroundTask;
    private readonly Regex? _backgroundAgentRow;

    // Default scan depth. The current Claude Code build renders the live spinner line
    // above the input box with a sub-agent panel below it, and tall selection menus
    // whose cursor sits well above the footer, so the signal is no longer on the last
    // few lines. WORKING's live-only qualifiers keep widening safe from stale
    // scrollback (a frozen completed line carries none of them).
    public PaneClassifier(AgentProfile profile, int statusLineCount = 16)
    {
        _profile = profile;
        _statusLineCount = statusLineCount;
        _waitingCursor = profile.CompileWaitingCursor();
        _workingSpinner = profile.CompileWorkingSpinner();
        _spinnerGlyph = profile.CompileSpinnerGlyph();
        _workingLineStartGlyph = profile.CompileWorkingLineStartGlyph();
        _workingLiveEllipsis = profile.CompileWorkingLiveEllipsis();
        _workingLiveMeter = profile.CompileWorkingLiveMeter();
        _workingBackgroundAgents = profile.CompileWorkingBackgroundAgents();
        _idlePrompt = profile.CompileIdlePrompt();
        _backgroundTask = profile.CompileBackgroundTask();
        _backgroundAgentRow = profile.CompileBackgroundAgentRow();
    }

    /// <summary>
    /// Classify from a capture and the pane's dead flag. The pane is already known
    /// to belong to this profile's agent (matched during discovery).
    /// </summary>
    public PaneState Classify(string? capture, bool dead)
    {
        if (dead)
            return PaneState.Dead;

        var statusLines = TailNonBlank(capture, _statusLineCount);
        var statusText = string.Join("\n", statusLines);

        // Index of the composer prompt within statusLines, or -1. Everything after it is
        // chrome; everything before it is transcript. Resolved once and shared by the
        // checks below, all of which are anchored on it.
        var composer = FindComposerLine(statusLines);

        if (IsWaiting(statusLines, statusText, composer))
            return PaneState.Waiting;
        if (IsWorking(statusText))
            return PaneState.Working;
        if (IsBackgnd(statusLines, composer))
            return PaneState.Backgnd;
        if (IsIdle(statusText, composer))
            return PaneState.Idle;

        return PaneState.Unknown;
    }

    /// <summary>
    /// The agent is blocked on the user: a selection cursor, or a footer offering to
    /// navigate and cancel.
    ///
    /// The cursor is searched in the chrome only - below the composer, or anywhere on
    /// screen when no composer is present. That second case is what every real prompt
    /// looks like: the box *replaces* the input box rather than stacking above it (all
    /// four WAITING fixtures), so the position rule costs a genuine prompt nothing. What
    /// it excludes is the transcript, where a "❯ 1." is prose - a recap, a diff, a
    /// quoted screen, this very paragraph - and an agent that has merely *written* the
    /// glyph is not waiting on anyone. Matching it pinned the pane in WAITING, which
    /// outranks IDLE and so withheld DONE for as long as the text stayed on screen.
    ///
    /// The footer check below stays whole-screen deliberately, as the asymmetry is the
    /// safety net: if some prompt does render a composer above its menu, the nav+cancel
    /// pair still catches it. Only the bare permission box lacks that footer, and that is
    /// the one form confirmed to replace the composer.
    /// </summary>
    private bool IsWaiting(List<string> statusLines, string statusText, int composer)
    {
        for (var i = composer + 1; i < statusLines.Count; i++)
        {
            if (_waitingCursor.IsMatch(statusLines[i]))
                return true;
        }

        // Footer wording varies between approval and ask_user prompts; only the
        // nav marker + cancel marker are invariant. Require both on one line so we
        // do not match a stray "esc to cancel" appearing elsewhere. The cancel text
        // is matched case-insensitively so a capitalised footer ("Esc to cancel",
        // as the current Claude build renders it) still matches.
        // Both markers must be configured for the check to run at all. string.Contains("")
        // is always true, so an empty marker would make the pair match every non-blank
        // line and pin the pane in WAITING - top priority, so it chimes, reddens the
        // pointer and never reaches DONE. Empty-means-disabled is the idiom everywhere
        // else on the profile (IsWorking guards its own marker the same way, and the
        // shipped Claude profile switches WorkingFooterCancelMarker off by emptying it),
        // so someone writing a third-agent profile has been shown exactly this move.
        if (_profile.WaitingFooterNavMarker.Length > 0 &&
            _profile.WaitingFooterCancelMarker.Length > 0)
        {
            foreach (var line in statusText.Split('\n'))
            {
                if (line.Contains(_profile.WaitingFooterNavMarker, StringComparison.Ordinal) &&
                    line.Contains(_profile.WaitingFooterCancelMarker, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private bool IsWorking(string statusText)
    {
        // Legacy/Copilot: spinner glyph immediately followed by the word "Working".
        if (_workingSpinner is not null && _workingSpinner.IsMatch(statusText))
            return true;

        foreach (var line in statusText.Split('\n'))
        {
            // Footer shows the working cancel marker. Copilot pairs it with a spinner
            // glyph on the same line; an older Claude build's marker ("esc to
            // interrupt") was distinctive enough to stand alone (WorkingMarkerSufficient).
            // Skipped when the profile has no marker configured. WAITING is checked
            // first, so a selection footer never reaches here.
            if (_profile.WorkingFooterCancelMarker.Length > 0 &&
                line.Contains(_profile.WorkingFooterCancelMarker, StringComparison.Ordinal) &&
                (_profile.WorkingMarkerSufficient || _spinnerGlyph.IsMatch(line)))
                return true;

            // Current Claude build: a live spinner status line. The activity meter
            // ("(11s · ↓ 307 tokens)") and the background-sub-agents wait are
            // distinctive enough to stand alone, independent of the leading glyph -
            // which matters because the spinner animates through frames we cannot
            // fully enumerate (·, *, ✻, ✢ …), so anchoring on a glyph whitelist drops
            // ~a quarter of frames and flickers the pane to IDLE. The bare
            // gerund+ellipsis moment (before the meter appears) still needs the
            // spinner-glyph anchor so a truncated table cell or prose line that merely
            // contains an ellipsis is not mistaken for work. A frozen, past-tense
            // completed line ("<Word> for <dur>") carries none of these qualifiers.
            if (_profile.WorkingLiveSpinnerSufficient)
            {
                if ((_workingLiveMeter?.IsMatch(line) ?? false) ||
                    (_workingBackgroundAgents?.IsMatch(line) ?? false))
                    return true;
                if (_workingLineStartGlyph.IsMatch(line) &&
                    (_workingLiveEllipsis?.IsMatch(line) ?? false))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Index of the composer prompt in <paramref name="statusLines"/>, or -1 when the
    /// profile configures no prompt pattern or none is on screen. The **last** match wins:
    /// the composer is the bottom-most prompt, so anything resembling one further up is
    /// transcript and must stay on the transcript side of the split.
    /// </summary>
    private int FindComposerLine(List<string> statusLines)
    {
        if (_idlePrompt is null)
            return -1;

        for (var i = statusLines.Count - 1; i >= 0; i--)
        {
            if (_idlePrompt.IsMatch(statusLines[i]))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// The agent finished its turn but work it started - a background shell, a monitor, or
    /// a detached sub-agent - is still running.
    ///
    /// Two tokens are searched, and either is sufficient. They are alternatives, not one
    /// refining the other: the counter is a footer segment that never counts *agents*, so
    /// a pane whose only outstanding work is a detached sub-agent shows nothing in that
    /// slot and is visible only as a row in the agent panel. (The counter does pick up a
    /// sub-agent's own shells and monitors, which made the gap flicker rather than fail
    /// outright - it was sampling the sub-agent's incidental resource use.)
    ///
    /// Both are matched only in the chrome below the composer: the transcript above it
    /// holds prose that either never meant a live task ("Ran 1 shell command") or has
    /// outlived one ("· 1 shell still running" frozen after the shell exited, "Running in
    /// the background as @name" frozen after the sub-agent reported). Matching any of them
    /// would pin the pane in BACKGND for good.
    /// </summary>
    private bool IsBackgnd(List<string> statusLines, int composer)
    {
        if (composer < 0 || (_backgroundTask is null && _backgroundAgentRow is null))
            return false;

        for (var i = composer + 1; i < statusLines.Count; i++)
        {
            if ((_backgroundTask?.IsMatch(statusLines[i]) ?? false) ||
                (_backgroundAgentRow?.IsMatch(statusLines[i]) ?? false))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The composer anchor when the profile configures one, else the legacy status-hint
    /// tokens. The anchor is preferred because footer hints are conditional slots the
    /// agent recycles, while the composer is structure - see
    /// <see cref="AgentProfile.IdlePromptPattern"/>.
    /// </summary>
    private bool IsIdle(string statusText, int composer) =>
        _idlePrompt is not null
            ? composer >= 0
            : _profile.IdleHints.Any(hint => statusText.Contains(hint, StringComparison.Ordinal));

    private static List<string> TailNonBlank(string? capture, int count)
    {
        if (string.IsNullOrEmpty(capture))
            return new List<string>();

        return capture
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Trim().Length > 0)
            .TakeLast(count)
            .ToList();
    }
}
