using System.Text.RegularExpressions;
using TmuxWatch.Config;

namespace TmuxWatch.Detection;

/// <summary>
/// Pure classifier: turns a captured pane screen into a <see cref="PaneState"/>
/// using positive status-bar fingerprints. Applies a fixed precedence
/// (WAITING → WORKING → IDLE → DEAD) so a screen matching more than one signal
/// resolves predictably. Holds no state, so it is trivially fixture-testable.
/// </summary>
public sealed class PaneClassifier
{
    private readonly WatchConfig _cfg;
    private readonly Regex _waitingCursor;
    private readonly Regex _workingSpinner;
    private readonly Regex _spinnerGlyph;

    public PaneClassifier(WatchConfig cfg)
    {
        _cfg = cfg;
        _waitingCursor = cfg.CompileWaitingCursor();
        _workingSpinner = cfg.CompileWorkingSpinner();
        _spinnerGlyph = cfg.CompileSpinnerGlyph();
    }

    /// <summary>
    /// Classify from a capture and liveness facts. <paramref name="commandIsCopilot"/>
    /// and <paramref name="dead"/> come from pane enumeration.
    /// </summary>
    public PaneState Classify(string? capture, bool commandIsCopilot, bool dead)
    {
        if (dead || !commandIsCopilot)
            return PaneState.Dead;

        var statusLines = TailNonBlank(capture, _cfg.StatusLineCount);
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
        // do not match a stray "esc to cancel" appearing elsewhere.
        foreach (var line in statusText.Split('\n'))
        {
            if (line.Contains(_cfg.WaitingFooterNavMarker, StringComparison.Ordinal) &&
                line.Contains(_cfg.WaitingFooterCancelMarker, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private bool IsWorking(string statusText)
    {
        // Legacy: spinner glyph immediately followed by the word "Working".
        if (_workingSpinner.IsMatch(statusText))
            return true;

        // Current CLI: the footer shows a spinner glyph followed by the active
        // action label (e.g. "Adding contracts") and the working cancel marker,
        // with no "Working" word. Require both on one line so stale text elsewhere
        // does not match. "esc cancel" is distinct from the selection footer's
        // "esc to cancel", and WAITING is checked first, so there is no overlap.
        foreach (var line in statusText.Split('\n'))
        {
            if (_spinnerGlyph.IsMatch(line) &&
                line.Contains(_cfg.WorkingFooterCancelMarker, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private bool IsIdle(string statusText) =>
        _cfg.IdleHints.Any(hint => statusText.Contains(hint, StringComparison.Ordinal));

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
