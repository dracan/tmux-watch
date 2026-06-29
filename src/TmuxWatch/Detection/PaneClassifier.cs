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

    public PaneClassifier(AgentProfile profile, int statusLineCount = 6)
    {
        _profile = profile;
        _statusLineCount = statusLineCount;
        _waitingCursor = profile.CompileWaitingCursor();
        _workingSpinner = profile.CompileWorkingSpinner();
        _spinnerGlyph = profile.CompileSpinnerGlyph();
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
        // do not match a stray "esc to cancel" appearing elsewhere.
        foreach (var line in statusText.Split('\n'))
        {
            if (line.Contains(_profile.WaitingFooterNavMarker, StringComparison.Ordinal) &&
                line.Contains(_profile.WaitingFooterCancelMarker, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private bool IsWorking(string statusText)
    {
        // Legacy/Copilot: spinner glyph immediately followed by the word "Working".
        if (_workingSpinner is not null && _workingSpinner.IsMatch(statusText))
            return true;

        // Footer shows the working cancel marker. Copilot pairs it with a spinner
        // glyph on the same line; Claude's marker ("esc to interrupt") is distinctive
        // enough to stand alone (WorkingMarkerSufficient). WAITING is checked first,
        // so a selection footer never reaches here.
        foreach (var line in statusText.Split('\n'))
        {
            if (!line.Contains(_profile.WorkingFooterCancelMarker, StringComparison.Ordinal))
                continue;
            if (_profile.WorkingMarkerSufficient || _spinnerGlyph.IsMatch(line))
                return true;
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
