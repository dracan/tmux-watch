using System.Text;
using TmuxWatch.Config;
using TmuxWatch.Tmux;

namespace TmuxWatch.Workspace;

public sealed record WorkspaceResult(bool Success, string Message, string? Path = null);

public sealed class WorkspaceService
{
    private readonly WorkspaceBackend _backend;
    private readonly PauseStore _pauses;
    private readonly IWorkspaceClipboard _clipboard;

    public WorkspaceService(TmuxRunner tmux, WatchConfig config, PauseStore pauses,
        IWorkspaceClipboard? clipboard = null)
    {
        _backend = new(tmux, config);
        _pauses = pauses;
        _clipboard = clipboard ?? new WorkspaceClipboard();
    }

    public WorkspaceSnapshot Capture()
    {
        var inventory = _backend.Inventory();
        var sessions = inventory.GroupBy(p => p.SessionId).Select(session =>
            new SnapshotSession(session.First().Pane.SessionName,
                session.GroupBy(p => p.WindowId).OrderBy(g => g.First().Pane.WindowIndex).Select(window =>
                {
                    var first = window.First();
                    return new SnapshotWindow(first.SessionId + "/" + first.WindowId,
                        first.Pane.WindowIndex, first.Pane.WindowName, first.Layout,
                        first.Pane.WindowActive, first.Zoomed,
                        window.OrderBy(p => p.Pane.PaneIndex).Select(p => new SnapshotPane(
                            p.Pane.Id, p.Pane.PaneIndex, p.Pane.CurrentPath, p.Pane.AgentId,
                            _pauses.IsPaused(p.Pane), p.Pane.PaneActive, p.Pane.Dead, null,
                            p.Pane.AgentId.Length == 0 ? null :
                                "No verified read-only resolver for this agent version; choose its conversation manually."
                        )).ToList(), first.Linked);
                }).ToList())).ToList();
        // Native window IDs are server-wide. Keep shared identity so import can detect
        // linked windows instead of silently duplicating them. Psmux scopes IDs by session.
        if (!_backend.IsPsmux)
            sessions = sessions.Select(s => s with { Windows = s.Windows.Select(w =>
                w with { SourceId = w.SourceId.Split('/')[1] }).ToList() }).ToList();
        return new(WorkspaceSnapshot.CurrentVersion, DateTimeOffset.UtcNow,
            OperatingSystem.IsWindows() ? "windows" : "unix", sessions);
    }

    public WorkspaceResult Export(string? path = null)
    {
        try
        {
            var snapshot = Capture();
            var json = snapshot.ToJson();
            path = System.IO.Path.GetFullPath(path ?? WorkspaceFiles.NewPath("snapshots", "json"));
            WorkspaceFiles.AtomicWrite(path, json, overwrite: false);
            var warning = _clipboard.Copy(json);
            var unknown = snapshot.Sessions.SelectMany(s => s.Windows).SelectMany(w => w.Panes)
                .Count(p => p.Agent.Length > 0 && p.ConversationId is null);
            var restoreWarnings = Validate(snapshot, Directory.Exists);
            return new(true, $"Saved {path}. " + (warning is null ? "Copied to clipboard." : $"Clipboard unavailable: {warning}") +
                (unknown > 0 ? $" {unknown} agent resume target(s) unknown." : "") +
                (restoreWarnings.Count > 0 ? " Restore preflight warnings: " + string.Join("; ", restoreWarnings.Distinct()) : ""), path);
        }
        catch (Exception e) when (Expected(e)) { return new(false, e.Message, path); }
    }

    public WorkspaceResult ImportFile(string path)
    {
        try { return ImportJson(File.ReadAllText(path)); }
        catch (Exception e) when (Expected(e)) { return new(false, e.Message); }
    }

    public WorkspaceResult ImportClipboard()
    {
        try { return ImportJson(_clipboard.Read()); }
        catch (Exception e) when (Expected(e)) { return new(false, e.Message); }
    }

    public WorkspaceResult ImportJson(string json)
    {
        var report = new StringBuilder();
        var created = false;
        string? reportPath = null;
        try
        {
            var snapshot = WorkspaceSnapshot.Parse(json);
            var errors = Validate(snapshot, Directory.Exists);
            if (_backend.IsPsmux && snapshot.Sessions is not null)
                foreach (var session in snapshot.Sessions.Where(s => s?.Windows is not null))
                    if (!session.Windows.Where(w => w is not null).Select(w => w.Index).Order()
                        .SequenceEqual(Enumerable.Range(0, session.Windows.Count)))
                        errors.Add("Psmux requires contiguous window indexes starting at zero.");
            var existing = _backend.SessionNames();
            if (snapshot.Sessions is not null)
                errors.AddRange(snapshot.Sessions.Where(s => s is not null && existing.Contains(s.Name))
                    .Select(s => $"Session already exists: {s.Name}"));
            if (errors.Count > 0) return new(false, "Nothing created:\n" + string.Join("\n", errors));

            // Ensure reporting is writable before the first lifecycle operation.
            reportPath = WorkspaceFiles.NewPath("restores", "txt");
            WorkspaceFiles.AtomicWrite(reportPath, "Restore started.\n", overwrite: false);
            void Log(string text)
            {
                report.AppendLine(text);
                WorkspaceFiles.AtomicWrite(reportPath, report.ToString(), overwrite: true);
            }
            foreach (var session in snapshot.Sessions!)
            {
                var firstWindow = true;
                foreach (var window in session.Windows.OrderBy(w => w.Index))
                {
                    var layout = PaneLayout.Parse(window.Layout);
                    var ordered = layout.Leaves.Select(l => window.Panes.Single(p => p.SourceId == "%" + l.PaneId)).ToList();
                    Log($"Creating {session.Name}:{window.Index} ({window.Name})");
                    var target = firstWindow
                        ? _backend.CreateSession(session.Name, window, ordered[0], layout)
                        : _backend.CreateWindow(session.Name, window, ordered[0]);
                    created = true;
                    var fields = target.Split('|');
                    if (fields.Length != 2 || !WorkspaceBackend.Identifier(fields[0], '@') || !WorkspaceBackend.Identifier(fields[1], '%'))
                        throw new InvalidDataException("Creation returned no reliable window/pane identity; resources have been retained.");
                    var windowId = fields[0];
                    // Use the fully qualified target for psmux's per-session IDs.
                    var firstIndex = WorkspaceBackend.Number(_backend.Read(session.Name + ":", "#{window_index}"));
                    if (firstWindow && firstIndex != window.Index)
                        _backend.MoveWindow(session.Name + ":" + firstIndex, session.Name, window.Index);
                    var windowTarget = session.Name + ":" + window.Index;
                    firstWindow = false;
                    Log($"Created {windowTarget} ({windowId}) with pane {fields[1]}.");
                    var mapping = new Dictionary<int, int> { [Id(ordered[0].SourceId)] = Id(fields[1]) };
                    var newPanes = new List<string> { fields[1] };
                    for (var i = 1; i < ordered.Count; i++)
                    {
                        // Both multiplexers assign saved layout leaves in pane-list
                        // order. Append each new pane after the last, spreading the
                        // temporary cells between splits so that cell stays splittable.
                        _backend.Layout(windowTarget, "tiled");
                        var last = newPanes[^1];
                        var sizes = _backend.Read(PaneTarget(windowTarget, last), "#{pane_width}|#{pane_height}").Split('|');
                        var width = WorkspaceBackend.Number(sizes[0]);
                        var height = WorkspaceBackend.Number(sizes[1]);
                        var newId = _backend.Split(PaneTarget(windowTarget, last), ordered[i].Directory, width > height * 2);
                        if (!WorkspaceBackend.Identifier(newId, '%')) throw new InvalidDataException("Split returned no reliable pane identity.");
                        newPanes.Add(newId);
                        mapping[Id(ordered[i].SourceId)] = Id(newId);
                        Log($"Created pane {newId} in {windowTarget}: {ordered[i].Directory}");
                    }
                    _backend.Layout(windowTarget, layout.Encode(mapping));
                    var actual = PaneLayout.Parse(_backend.Read(windowTarget, "#{window_layout}"));
                    if (actual.Shape != layout.Shape || !actual.Leaves.Select(l => l.PaneId!.Value)
                        .SequenceEqual(layout.Leaves.Select(l => mapping[l.PaneId!.Value])))
                        throw new IOException($"Restored layout differs for {windowTarget}. Expected {layout.Encode(mapping)}, got {actual.Encode()}. Created panes retained.");
                    foreach (var pane in ordered)
                    {
                        var newId = "%" + mapping[Id(pane.SourceId)];
                        var targetPane = PaneTarget(windowTarget, newId);
                        var pid = WorkspaceBackend.Number(_backend.Read(targetPane, "#{pane_pid}"));
                        var serverPid = WorkspaceBackend.Number(_backend.Read(targetPane, "#{pid}"));
                        var index = WorkspaceBackend.Number(_backend.Read(targetPane, "#{pane_index}"));
                        var actualDirectory = _backend.Read(targetPane, "#{pane_current_path}");
                        if (!SameDirectory(actualDirectory, pane.Directory))
                            throw new IOException($"Directory differs for {targetPane}: expected {pane.Directory}, got {actualDirectory}.");
                        _pauses.Set(new Pane(newId, session.Name, window.Index, index, "", false,
                            window.Name, pane.Directory, Pid: pid, ServerPid: serverPid), pane.Paused);
                        if (pane.Active) _backend.SelectPane(targetPane);
                        Log($"{session.Name}:{window.Index}.{index} | {pane.Directory} | {(pane.Paused ? "paused" : "active")} | {ResumeGuidance.For(pane)}");
                    }
                }
                var active = session.Windows.FirstOrDefault(w => w.Active);
                if (active is not null) _backend.SelectWindow(session.Name + ":" + active.Index);
            }
            Log("Restore complete. Run any listed resume commands manually in their panes.");
            return new(true, report + $"Report: {reportPath}", reportPath);
        }
        catch (Exception e) when (Expected(e))
        {
            report.AppendLine((created ? "Partial restore retained. " : "Restore stopped. ") + e.Message);
            if (reportPath is not null)
                try { WorkspaceFiles.AtomicWrite(reportPath, report.ToString(), overwrite: true); }
                catch (Exception writeError) when (Expected(writeError)) { report.AppendLine("Could not save report: " + writeError.Message); }
            return new(false, report.ToString(), reportPath);
        }
    }

    private string PaneTarget(string window, string pane) => _backend.PaneTarget(window, pane);
    private static int Id(string value) => WorkspaceBackend.Number(value[1..]);
    private static bool SameDirectory(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
        Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal static bool Expected(Exception e) => e is IOException or UnauthorizedAccessException or
        System.Text.Json.JsonException or ArgumentException or InvalidOperationException or NotSupportedException;

    internal static List<string> Validate(WorkspaceSnapshot snapshot, Func<string, bool> directoryExists)
    {
        var errors = new List<string>();
        if (snapshot.Version != WorkspaceSnapshot.CurrentVersion) errors.Add($"Unsupported snapshot version: {snapshot.Version}.");
        if (snapshot.Sessions is null || snapshot.Sessions.Count == 0 || snapshot.Sessions.Count > 256)
        { errors.Add("Snapshot must contain 1-256 sessions."); return errors; }
        var names = new HashSet<string>(StringComparer.Ordinal);
        var windows = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in snapshot.Sessions)
        {
            if (session is null) { errors.Add("Null session."); continue; }
            if (!SafeText(session.Name) || session.Name.IndexOfAny(['.', ':']) >= 0 || !names.Add(session.Name))
                errors.Add("Invalid or duplicate session name.");
            if (session.Windows is null || session.Windows.Count == 0 || session.Windows.Count > 256)
            { errors.Add($"Invalid windows in {session.Name}."); continue; }
            var indexes = new HashSet<int>();
            foreach (var window in session.Windows)
            {
                if (window is null) { errors.Add("Null window."); continue; }
                if (!SafeText(window.Name) || string.IsNullOrEmpty(window.SourceId) || window.Index < 0 || !indexes.Add(window.Index))
                    errors.Add($"Invalid window in {session.Name}.");
                if (!windows.Add(window.SourceId)) errors.Add("Linked windows cannot yet be restored faithfully; nothing will be created.");
                if (window.Linked) errors.Add($"Linked window {session.Name}:{window.Index} cannot yet be restored faithfully.");
                if (window.Zoomed) errors.Add($"Unzoom {session.Name}:{window.Index} before exporting; zoomed layouts are unsupported.");
                if (window.Panes is null || window.Panes.Count == 0 || window.Panes.Count > 256)
                { errors.Add($"Invalid panes in {session.Name}:{window.Index}."); continue; }
                var ids = new HashSet<int>();
                foreach (var pane in window.Panes)
                {
                    if (pane is null) { errors.Add("Null pane."); continue; }
                    if (!WorkspaceBackend.Identifier(pane.SourceId, '%') || !int.TryParse(pane.SourceId.AsSpan(1), out var id) || !ids.Add(id))
                        errors.Add("Invalid or duplicate pane identity.");
                    if (!SafeText(pane.Directory) || !Path.IsPathFullyQualified(pane.Directory) || !directoryExists(pane.Directory))
                        errors.Add($"Missing or unsupported directory: {pane.Directory}");
                    if (pane.IdentityNote is not null && (pane.IdentityNote.Length > 4096 || pane.IdentityNote.Any(char.IsControl)))
                        errors.Add("Invalid identity note.");
                    if ((pane.Agent is null || (pane.Agent.Length > 0 && !SafeText(pane.Agent))) || (pane.ConversationId is not null && !Guid.TryParseExact(pane.ConversationId, "D", out _)))
                        errors.Add("Invalid agent or conversation identity.");
                }
                try
                {
                    var layout = PaneLayout.Parse(window.Layout);
                    if (!layout.Leaves.Select(l => l.PaneId!.Value).ToHashSet().SetEquals(ids))
                        errors.Add("Layout does not cover exactly the saved panes.");
                }
                catch (InvalidDataException e) { errors.Add(e.Message); }
            }
        }
        return errors;
    }

    // tmux expands formats in cwd arguments and treats semicolons as command
    // separators even in argv. Reject these forms instead of interpreting imported data.
    internal static bool SafeText(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 4096 &&
        !text.Any(char.IsControl) && !text.Contains(';') && !text.Contains("#{", StringComparison.Ordinal) &&
        !text.Contains("#(", StringComparison.Ordinal);
}
