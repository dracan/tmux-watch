using TmuxWatch.Config;
using TmuxWatch.Tmux;

namespace TmuxWatch.Discovery;

public sealed record DiscoveryResult(IReadOnlyList<Pane> Panes, string? Error)
{
    public bool Ok => Error is null;
}

/// <summary>
/// Enumerates panes via a single read-only <c>lsp -a -F</c> call, parses them,
/// and filters to Copilot sessions (foreground command, with an optional
/// session-name backstop). Server/CLI failures yield an empty set plus an error
/// rather than throwing.
/// </summary>
public sealed class PaneDiscovery
{
    // Field order must match Parse(); '|' separates fields.
    public const string Format =
        "#{pane_id}|#{session_name}|#{window_index}|#{pane_index}|#{pane_current_command}|#{pane_dead}|#{window_name}|#{pane_current_path}|#{window_active}|#{pane_active}";

    private readonly ITmuxClient _tmux;
    private readonly WatchConfig _cfg;

    public PaneDiscovery(ITmuxClient tmux, WatchConfig cfg)
    {
        _tmux = tmux;
        _cfg = cfg;
    }

    /// <summary>All panes across all sessions (unfiltered).</summary>
    public DiscoveryResult EnumerateAll()
    {
        var result = _tmux.ListPanesRaw(Format);
        if (!result.Started)
            return new DiscoveryResult(Array.Empty<Pane>(), result.StdErr);
        if (result.ExitCode != 0)
            return new DiscoveryResult(Array.Empty<Pane>(),
                $"tmux exited {result.ExitCode}: {result.StdErr.Trim()}");

        var panes = new List<Pane>();
        foreach (var line in result.StdOut.Replace("\r\n", "\n").Split('\n'))
        {
            if (Parse(line) is { } pane)
                panes.Add(pane);
        }
        return new DiscoveryResult(panes, null);
    }

    /// <summary>Only the panes that are Copilot sessions.</summary>
    public DiscoveryResult DiscoverCopilotPanes()
    {
        var all = EnumerateAll();
        if (!all.Ok)
            return all;

        var convention = _cfg.CompileSessionConvention();
        var copilot = all.Panes.Where(p => IsCopilot(p, convention)).ToList();
        return new DiscoveryResult(copilot, null);
    }

    public bool IsCopilot(Pane pane, System.Text.RegularExpressions.Regex? convention)
    {
        if (CommandIsCopilot(pane.Command))
            return true;
        if (convention is not null && convention.IsMatch(pane.SessionName))
            return true;
        return false;
    }

    /// <summary>
    /// Matches the foreground command against the configured Copilot command.
    /// A Windows/psmux host reports <c>pane_current_command</c> with the executable
    /// extension (e.g. "copilot.exe"), so we also compare the extensionless stem
    /// to keep the configured command ("copilot") working on every platform.
    /// </summary>
    public bool CommandIsCopilot(string command)
    {
        if (string.Equals(command, _cfg.CopilotCommand, StringComparison.OrdinalIgnoreCase))
            return true;

        var stem = Path.GetFileNameWithoutExtension(command);
        return stem.Length > 0 &&
            string.Equals(stem, _cfg.CopilotCommand, StringComparison.OrdinalIgnoreCase);
    }

    public static Pane? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var parts = line.Split('|');
        if (parts.Length < 6)
            return null;

        var id = parts[0].Trim();
        if (id.Length == 0)
            return null;

        _ = int.TryParse(parts[2], out var win);
        _ = int.TryParse(parts[3], out var pane);
        var dead = parts[5].Trim() == "1";
        var windowName = parts.Length > 6 ? parts[6].Trim() : "";
        var currentPath = parts.Length > 7 ? parts[7].Trim() : "";
        var windowActive = parts.Length > 8 && parts[8].Trim() == "1";
        var paneActive = parts.Length > 9 && parts[9].Trim() == "1";

        return new Pane(id, parts[1], win, pane, parts[4].Trim(), dead, windowName, currentPath, windowActive, paneActive);
    }
}
