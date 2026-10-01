using System.Globalization;
using TmuxWatch.Config;
using TmuxWatch.Discovery;
using TmuxWatch.Tmux;

namespace TmuxWatch.Workspace;

internal sealed record InventoryPane(Pane Pane, string SessionId, string WindowId,
    string Layout, bool Zoomed, bool Linked);

/// <summary>Metadata reads and fixed, shell-free lifecycle argument lists.</summary>
internal sealed class WorkspaceBackend(TmuxRunner tmux, WatchConfig config)
{
    private const string InventoryFormat = "#{session_id}|#{window_id}|#{pane_id}|#{window_index}|#{pane_index}|#{pane_pid}|#{pane_dead}|#{pane_active}|#{window_active}|#{session_name}";
    public bool IsPsmux => OperatingSystem.IsWindows() ||
        Path.GetFileNameWithoutExtension(config.TmuxExecutable).Equals("psmux", StringComparison.OrdinalIgnoreCase);

    internal static string Checked(TmuxResult result)
    {
        if (!result.Ok) throw new IOException(result.StdErr.Trim().Length > 0 ? result.StdErr.Trim() : "Multiplexer command failed.");
        return result.StdOut.TrimEnd('\r', '\n');
    }

    public string Read(string target, string format) => Checked(tmux.Run("display-message", "-p", "-t", target, format));

    public IReadOnlyList<InventoryPane> Inventory()
    {
        var before = Checked(tmux.ListPanesRaw(InventoryFormat));
        var panes = new List<InventoryPane>();
        var discovery = new PaneDiscovery(tmux, config, "");
        foreach (var line in before.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.TrimEnd('\r').Split('|', 10);
            if (f.Length != 10 || !Identifier(f[0], '$') || !Identifier(f[1], '@') || !Identifier(f[2], '%'))
                throw new InvalidDataException("Multiplexer returned unsupported pane metadata.");
            var wi = Number(f[3]); var pi = Number(f[4]); var pid = Number(f[5]);
            var target = (IsPsmux ? f[9] : f[0]) + ":" + wi + "." + pi;
            // Free-text fields are read separately so pipes in names/paths are lossless.
            var session = Read(target, "#{session_name}");
            var name = Read(target, "#{window_name}");
            var cwd = Read(target, "#{pane_current_path}");
            var command = Read(target, "#{pane_current_command}");
            var lifetime = Read(target, "#{pid}|#{session_created}");
            var serverPid = Number(lifetime.Split('|')[0]);
            var pane = new Pane(f[2], session, wi, pi, command, f[6] == "1", name, cwd,
                f[8] == "1", f[7] == "1", pid, ServerPid: serverPid);
            pane = pane with { AgentId = discovery.MatchProfile(pane)?.Id ?? "" };
            panes.Add(new(pane, f[0], f[1], Read(target, "#{window_layout}"),
                Read(target, "#{window_zoomed_flag}") == "1",
                Read(target, "#{window_linked}") == "1"));
        }
        if (before != Checked(tmux.ListPanesRaw(InventoryFormat)))
            throw new IOException("Workspace changed during export. Please retry.");
        var resolved = panes.Select(p => p.Pane).ToList();
        discovery.ResolveAgents(resolved);
        return panes.Select((p, i) => p with { Pane = resolved[i] }).ToList();
    }

    public HashSet<string> SessionNames()
    {
        var result = tmux.Run("list-sessions", "-F", "#{session_name}");
        if (!result.Ok && result.Started &&
            (result.StdErr.Contains("no server running", StringComparison.OrdinalIgnoreCase) ||
             result.StdErr.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase) ||
             result.StdErr.Contains("no sessions", StringComparison.OrdinalIgnoreCase)))
            return new(StringComparer.Ordinal);
        return Checked(result).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.TrimEnd('\r')).ToHashSet(StringComparer.Ordinal);
    }

    public string PaneTarget(string window, string pane)
    {
        if (!IsPsmux) return pane;
        var lines = Checked(tmux.Run("lsp", "-t", window, "-F", "#{pane_id}|#{pane_index}"));
        foreach (var line in lines.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = line.TrimEnd('\r').Split('|');
            if (pair.Length == 2 && pair[0] == pane) return window + "." + Number(pair[1]);
        }
        throw new IOException($"Pane {pane} is no longer in {window}.");
    }

    public string CreateSession(string session, SnapshotWindow window, SnapshotPane first, PaneLayout layout) =>
        Created(tmux.Run("new-session", "-d", "-s", session, "-n", window.Name,
            "-c", first.Directory, "-x", layout.Width.ToString(CultureInfo.InvariantCulture),
            "-y", layout.Height.ToString(CultureInfo.InvariantCulture), "-P", "-F", "#{window_id}|#{pane_id}"));

    // Psmux always appends a new window, and since 3.3.8 resolves -t as an existing
    // window first, so an index target fails. Name only the session there.
    public string CreateWindow(string session, SnapshotWindow window, SnapshotPane first) =>
        Created(tmux.Run("new-window", "-d", "-t", session + ":" + (IsPsmux ? "" : window.Index.ToString(CultureInfo.InvariantCulture)),
            "-n", window.Name, "-c", first.Directory, "-P", "-F", "#{window_id}|#{pane_id}"));

    // Psmux reports a refused command as "ERROR: ..." on stdout with exit status 0.
    private static string Created(TmuxResult result)
    {
        var output = Checked(result);
        return output.StartsWith("ERROR: ", StringComparison.Ordinal) ? throw new IOException(output["ERROR: ".Length..]) : output;
    }

    /// <summary>How long to wait for a creation whose -P output was missed.</summary>
    internal TimeSpan CreationWait { get; set; } = TimeSpan.FromSeconds(30);

    // Psmux answers -P only if the new window is ready within 2s, but finishes
    // creating it regardless. A slow shell start therefore prints nothing, and
    // the window appears at its target moments later.
    public string CreatedIdentity(string printed, string target)
    {
        if (WindowPaneIdentity(printed.Trim())) return printed.Trim();
        var read = "";
        var found = Await(() =>
        {
            var result = tmux.Run("display-message", "-p", "-t", target, "#{window_id}|#{pane_id}");
            read = result.Ok ? result.StdOut.Trim() : result.StdErr.Trim();
            return result.Ok && WindowPaneIdentity(read);
        });
        if (found) return read;
        throw new InvalidDataException($"Creation of {target} returned no reliable window/pane identity " +
            $"within {CreationWait.TotalSeconds:0}s (printed \"{Visible(printed)}\", read \"{Visible(read)}\"); resources have been retained.");
    }

    private static bool WindowPaneIdentity(string text) => text.Split('|') is [var w, var p] &&
        Identifier(w, '@') && Identifier(p, '%');

    private static string Visible(string text) => new(text.Select(c => char.IsControl(c) ? '?' : c).Take(200).ToArray());

    public void MoveWindow(string source, string session, int index) =>
        Checked(tmux.Run("move-window", "-s", source, "-t", session + ":" + index));

    public string Split(string window, string target, string directory, bool horizontal)
    {
        var before = PaneIds(window);
        var printed = Created(tmux.Run("split-window", "-d", horizontal ? "-h" : "-v", "-t", target,
            "-c", directory, "-P", "-F", "#{pane_id}")).Trim();
        if (Identifier(printed, '%')) return printed;
        // Same missed -P as creation: the new pane is the one the window gained.
        string[] added = [];
        if (Await(() => (added = PaneIds(window).Except(before).ToArray()).Length > 0) && added.Length == 1)
            return added[0];
        throw new InvalidDataException($"Split of {target} returned no reliable pane identity " +
            $"(printed \"{Visible(printed)}\", new panes {added.Length}); created panes retained.");
    }

    private HashSet<string> PaneIds(string window) =>
        Checked(tmux.Run("lsp", "-t", window, "-F", "#{pane_id}")).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()).ToHashSet(StringComparer.Ordinal);

    internal bool Await(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow + CreationWait;
        while (!ready())
        {
            if (DateTime.UtcNow >= deadline) return false;
            Thread.Sleep(250);
        }
        return true;
    }

    public void Layout(string target, string layout) =>
        Checked(tmux.Run("select-layout", "-t", target, layout));
    public void SelectPane(string target) => Checked(tmux.SelectPane(target));
    public void SelectWindow(string target) => Checked(tmux.SelectWindow(target));

    internal static bool Identifier(string? value, char prefix) => value is { Length: > 1 }
        && value[0] == prefix && value.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0;
    internal static int Number(string s) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 0
        ? n : throw new InvalidDataException("Invalid numeric multiplexer metadata.");
}
