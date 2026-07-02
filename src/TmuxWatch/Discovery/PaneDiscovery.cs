using TmuxWatch.Config;
using TmuxWatch.Tmux;

namespace TmuxWatch.Discovery;

public sealed record DiscoveryResult(IReadOnlyList<Pane> Panes, string? Error)
{
    public bool Ok => Error is null;
}

/// <summary>
/// Enumerates panes via a single read-only <c>lsp -a -F</c> call, parses them,
/// and matches each to an agent profile (foreground command, with an optional
/// session-name backstop). Server/CLI failures yield an empty set plus an error
/// rather than throwing.
/// </summary>
public sealed class PaneDiscovery
{
    // Field order must match Parse(); '|' separates fields.
    public const string Format =
        "#{pane_id}|#{session_name}|#{window_index}|#{pane_index}|#{pane_current_command}|#{pane_dead}|#{window_name}|#{pane_current_path}|#{window_active}|#{pane_active}|#{pane_pid}";

    private readonly ITmuxClient _tmux;
    private readonly IReadOnlyList<AgentProfile> _agents;

    public PaneDiscovery(ITmuxClient tmux, WatchConfig cfg)
    {
        _tmux = tmux;
        _agents = cfg.ResolveAgents();
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

    /// <summary>
    /// Only the panes that match an agent profile, each stamped with its matched
    /// agent id (<see cref="Pane.AgentId"/>).
    /// </summary>
    public DiscoveryResult DiscoverAgentPanes()
    {
        var all = EnumerateAll();
        if (!all.Ok)
            return all;

        var matched = new List<Pane>();
        foreach (var pane in all.Panes)
        {
            if (MatchProfile(pane) is { } profile)
                matched.Add(pane with { AgentId = profile.Id });
        }
        return new DiscoveryResult(matched, null);
    }

    /// <summary>
    /// The first agent profile this pane belongs to, or null if none. Command match
    /// is tried across all profiles first (cheap, from enumeration), then the
    /// session-name backstop, so a correctly-named pane still matches even when its
    /// foreground command is momentarily something else.
    /// </summary>
    public AgentProfile? MatchProfile(Pane pane)
    {
        foreach (var agent in _agents)
            if (agent.MatchesCommand(pane.Command))
                return agent;

        foreach (var agent in _agents)
            if (agent.MatchesSession(pane.SessionName))
                return agent;

        return null;
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
        var pid = 0;
        if (parts.Length > 10)
            _ = int.TryParse(parts[10].Trim(), out pid);

        return new Pane(id, parts[1], win, pane, parts[4].Trim(), dead, windowName, currentPath, windowActive, paneActive, pid);
    }
}
