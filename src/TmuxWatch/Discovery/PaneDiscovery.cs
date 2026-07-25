using TmuxWatch.Config;
using TmuxWatch.Tmux;

namespace TmuxWatch.Discovery;

public sealed record DiscoveryResult(IReadOnlyList<Pane> Panes, string? Error)
{
    public bool Ok => Error is null;
}

/// <summary>
/// One enumeration split into the panes that matched an agent profile and those that did
/// not. <see cref="OtherPanes"/> is inert inventory: it exists so the TUI can list and
/// jump to non-agent panes, and must never be captured, classified, or tracked by the
/// attention state machine.
/// </summary>
public sealed record PaneInventory(
    IReadOnlyList<Pane> AgentPanes,
    IReadOnlyList<Pane> OtherPanes,
    string? Error)
{
    public bool Ok => Error is null;

    public static PaneInventory Failed(string? error) =>
        new(Array.Empty<Pane>(), Array.Empty<Pane>(), error);
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
        "#{pane_id}|#{session_name}|#{window_index}|#{pane_index}|#{pane_current_command}|#{pane_dead}|#{window_name}|#{pane_current_path}|#{window_active}|#{pane_active}|#{pane_pid}|#{window_activity}";

    private readonly ITmuxClient _tmux;
    private readonly IReadOnlyList<AgentProfile> _agents;
    private readonly string? _selfPaneId;

    /// <param name="selfPaneId">
    /// The watcher's own pane id when it is itself running inside tmux, so that pane can
    /// be kept out of the non-agent inventory rather than listing itself. Defaults to
    /// tmux's <c>TMUX_PANE</c>, which is unset when the watcher runs outside tmux (the
    /// normal case).
    /// </param>
    public PaneDiscovery(ITmuxClient tmux, WatchConfig cfg, string? selfPaneId = null)
    {
        _tmux = tmux;
        _agents = cfg.ResolveAgents();
        var self = selfPaneId ?? Environment.GetEnvironmentVariable("TMUX_PANE");
        _selfPaneId = string.IsNullOrWhiteSpace(self) ? null : self.Trim();
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
        var inventory = DiscoverPanes();
        return new DiscoveryResult(inventory.AgentPanes, inventory.Error);
    }

    /// <summary>
    /// One enumeration split into agent panes (stamped with their matched agent id) and
    /// the non-agent inventory. Both come from the same single <c>lsp -a</c> call, so
    /// listing non-agent panes costs no extra multiplexer round-trip. The watcher's own
    /// pane is omitted from the inventory when it is running inside tmux.
    /// </summary>
    public PaneInventory DiscoverPanes()
    {
        var all = EnumerateAll();
        if (!all.Ok)
            return PaneInventory.Failed(all.Error);

        var matched = new List<Pane>();
        var others = new List<Pane>();
        foreach (var pane in all.Panes)
        {
            if (MatchProfile(pane) is { } profile)
                matched.Add(pane with { AgentId = profile.Id });
            else if (!string.Equals(pane.Id, _selfPaneId, StringComparison.Ordinal))
                others.Add(pane);
        }
        return new PaneInventory(matched, others, null);
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
        // An absent, empty, or unparseable activity stamp degrades to 0 ("unknown")
        // rather than failing the whole line - a host that does not supply
        // #{window_activity} must still enumerate.
        long activity = 0;
        if (parts.Length > 11)
            _ = long.TryParse(parts[11].Trim(), out activity);

        return new Pane(id, parts[1], win, pane, parts[4].Trim(), dead, windowName, currentPath, windowActive, paneActive, pid, "", activity);
    }
}
