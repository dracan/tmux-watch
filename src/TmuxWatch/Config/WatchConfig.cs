using System.Text.Json;
using System.Text.RegularExpressions;

namespace TmuxWatch.Config;

/// <summary>
/// All tunable behaviour and the version-specific match tokens used to classify
/// Copilot panes. Tokens are configurable because they depend on the Copilot CLI
/// version (verified against v1.0.63).
/// </summary>
public sealed class WatchConfig
{
    /// <summary>
    /// Multiplexer executable on PATH. Defaults to <c>tmux</c>; override to
    /// <c>psmux</c> (or another tmux-compatible CLI) to run against a different host.
    /// </summary>
    public string TmuxExecutable { get; set; } = "tmux";

    public double PollIntervalSeconds { get; set; } = 2.0;

    /// <summary>Foreground command that identifies a Copilot pane.</summary>
    public string CopilotCommand { get; set; } = "copilot";

    /// <summary>
    /// Optional regex on the session name used as a backstop when the foreground
    /// command is not (yet) the Copilot command. Empty disables the backstop.
    /// </summary>
    public string SessionNameConvention { get; set; } = "";

    /// <summary>Number of trailing non-blank lines treated as the status area.</summary>
    public int StatusLineCount { get; set; } = 6;

    public bool NotifyOnIdle { get; set; } = false;

    /// <summary>Notification channel: "bell", "none" (extendable, e.g. "toast").</summary>
    public string NotificationChannel { get; set; } = "bell";

    // ---- Detection tokens (version-specific, overridable) -------------------

    /// <summary>Numbered selection cursor, e.g. "❯ 1.".</summary>
    public string WaitingCursorPattern { get; set; } = @"❯\s*\d+\.";

    /// <summary>Up/down marker present in every selection footer.</summary>
    public string WaitingFooterNavMarker { get; set; } = "↑/↓";

    /// <summary>Cancel text common to every selection footer.</summary>
    public string WaitingFooterCancelMarker { get; set; } = "esc to cancel";

    /// <summary>Spinner glyphs that precede the "Working" word.</summary>
    public string WorkingSpinnerGlyphs { get; set; } = "◎◉●○";

    public string WorkingWord { get; set; } = "Working";

    /// <summary>
    /// Cancel text on the working footer. Newer Copilot builds render the spinner
    /// followed by the current action label (e.g. "Adding contracts") instead of
    /// the literal word "Working", so a spinner glyph plus this marker on one line
    /// also indicates WORKING. Distinct from the selection footer's "esc to cancel".
    /// </summary>
    public string WorkingFooterCancelMarker { get; set; } = "esc cancel";

    /// <summary>Any one of these tokens in the status area indicates IDLE.</summary>
    public List<string> IdleHints { get; set; } = new()
    {
        "/ commands",
        "? help",
        "space hold to record",
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

    // Compiled helpers ------------------------------------------------------

    public Regex CompileWaitingCursor() =>
        new(WaitingCursorPattern, RegexOptions.Compiled);

    public Regex CompileWorkingSpinner()
    {
        var glyphs = SpinnerGlyphClass();
        return new Regex($"{glyphs}\\s*{Regex.Escape(WorkingWord)}", RegexOptions.Compiled);
    }

    /// <summary>Matches a single spinner glyph anywhere on a line.</summary>
    public Regex CompileSpinnerGlyph() =>
        new(SpinnerGlyphClass(), RegexOptions.Compiled);

    private string SpinnerGlyphClass()
    {
        var glyphs = string.Concat(WorkingSpinnerGlyphs.Select(c => Regex.Escape(c.ToString())));
        return $"[{glyphs}]";
    }

    public Regex? CompileSessionConvention() =>
        string.IsNullOrWhiteSpace(SessionNameConvention)
            ? null
            : new Regex(SessionNameConvention, RegexOptions.Compiled | RegexOptions.IgnoreCase);
}
