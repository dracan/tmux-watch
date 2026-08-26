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
    /// <summary>
    /// Field separator for <see cref="Format"/> and <see cref="Parse"/>.
    /// <para>
    /// It stays a printable character because tmux gives no safe alternative: format
    /// output is escaped, so an ASCII unit separator comes back as the four literal
    /// characters <c>\037</c> and cannot be split on, while a tab survives in the format
    /// string <em>and</em> in the data, so it is no safer than '|'. tmux's own
    /// <c>#{s/.../.../:...}</c> substitution would sanitise the fields at source, but it is a
    /// tmux 2.9+ feature and this watcher documents a psmux host, where an unsupported
    /// format token would break enumeration outright - a worse failure than the one being
    /// fixed. So the delimiter is one the data can contain, and <see cref="Parse"/> is
    /// arranged so that when it does, nothing load-bearing moves.
    /// </para>
    /// </summary>
    public const char FieldSeparator = '|';

    /// <summary>
    /// Field order for <see cref="Parse"/>. Not the order a human would pick: the
    /// numeric and boolean fields come first <em>because</em> they cannot contain the
    /// delimiter, and the three free-text ones are herded to the end.
    /// <para>
    /// A window name is whatever <c>rename-window</c> or automatic-rename produced
    /// ("build | watch"), a session name may hold a '|' too (tmux forbids only '.' and
    /// ':'), and a POSIX path admits every byte but NUL and '/'. With those fields
    /// interleaved, one stray '|' shifted every later field: the pane lost its focus
    /// marker, the pid guard against reused pane ids read a boolean, and the activity
    /// stamp read the pid, rendering an in-state age of some twenty thousand days.
    /// </para>
    /// <para>
    /// Now every field that feeds a decision - identity, jump target, agent match, focus,
    /// pid, activity - is parsed before the first field that can carry a delimiter, so
    /// none of them can shift. Of the three that follow, ordering is by consequence:
    /// <c>pane_current_command</c> (agent matching) first, then <c>session_name</c>
    /// (jump target), then the two purely cosmetic ones. <c>window_name</c> is last so
    /// the bounded split hands it every remaining character verbatim - it is the field
    /// most likely to hold a '|', and this makes it exact rather than merely harmless.
    /// </para>
    /// </summary>
    public const string Format =
        "#{pane_id}|#{window_index}|#{pane_index}|#{pane_dead}|#{window_active}|#{pane_active}|#{pane_pid}|#{window_activity}|#{pane_current_command}|#{session_name}|#{pane_current_path}|#{window_name}";

    /// <summary>Number of fields <see cref="Format"/> emits.</summary>
    internal const int FieldCount = 12;

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

        // Bounded: the last field absorbs every remaining delimiter, so a window name
        // containing '|' arrives whole rather than splitting the line. See Format.
        var parts = line.Split(FieldSeparator, FieldCount);
        if (parts.Length < 6)
            return null;

        var id = parts[0].Trim();
        if (id.Length == 0)
            return null;

        _ = int.TryParse(parts[1], out var win);
        _ = int.TryParse(parts[2], out var pane);
        var dead = parts[3].Trim() == "1";
        var windowActive = parts[4].Trim() == "1";
        var paneActive = parts[5].Trim() == "1";
        var pid = 0;
        if (parts.Length > 6)
            _ = int.TryParse(parts[6].Trim(), out pid);
        // An absent, empty, or unparseable activity stamp degrades to 0 ("unknown")
        // rather than failing the whole line - a host that does not supply
        // #{window_activity} must still enumerate.
        long activity = 0;
        if (parts.Length > 7)
            _ = long.TryParse(parts[7].Trim(), out activity);
        var command = parts.Length > 8 ? parts[8].Trim() : "";
        var session = parts.Length > 9 ? parts[9] : "";
        var currentPath = parts.Length > 10 ? parts[10].Trim() : "";
        var windowName = parts.Length > 11 ? parts[11].Trim() : "";

        return new Pane(id, session, win, pane, command, dead, windowName, currentPath, windowActive, paneActive, pid, "", activity);
    }
}
