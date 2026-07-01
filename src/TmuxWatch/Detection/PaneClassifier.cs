using System.Text.RegularExpressions;
using TmuxWatch.Config;

namespace TmuxWatch.Detection;

/// <summary>
/// Pure classifier: turns a captured pane screen into a <see cref="PaneState"/>
/// using one <see cref="AgentProfile"/>'s positive status-bar fingerprints. Applies
/// a fixed precedence (WAITING → WORKING → IDLE → DEAD) so a screen matching more
/// than one signal resolves predictably. Holds no mutable state, so it is trivially
/// fixture-testable. The caller selects the profile for a pane during discovery.
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

        if (IsWaiting(statusText))
            return PaneState.Waiting;
        if (IsWorking(statusText))
            return PaneState.Working;
        if (IsIdle(statusText))
            return PaneState.Idle;

        return PaneState.Unknown;
    }

    private bool IsWaiting(string statusText)
    {
        if (_waitingCursor.IsMatch(statusText))
            return true;

        // Footer wording varies between approval and ask_user prompts; only the
        // nav marker + cancel marker are invariant. Require both on one line so we
        // do not match a stray "esc to cancel" appearing elsewhere. The cancel text
        // is matched case-insensitively so a capitalised footer ("Esc to cancel",
        // as the current Claude build renders it) still matches.
        foreach (var line in statusText.Split('\n'))
        {
            if (line.Contains(_profile.WaitingFooterNavMarker, StringComparison.Ordinal) &&
                line.Contains(_profile.WaitingFooterCancelMarker, StringComparison.OrdinalIgnoreCase))
                return true;
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

    private bool IsIdle(string statusText) =>
        _profile.IdleHints.Any(hint => statusText.Contains(hint, StringComparison.Ordinal));

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
