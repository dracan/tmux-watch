using System.Diagnostics;
using TmuxWatch.Config;
using TmuxWatch.Tmux;
using TmuxWatch.Workspace;

namespace TmuxWatch.Tests;

public sealed class WorkspaceIntegrationFactAttribute : FactAttribute
{
    public WorkspaceIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TMUX_WATCH_WORKSPACE_INTEGRATION") != "1")
            Skip = "Opt-in isolated multiplexer integration test.";
    }
}

public class WorkspaceIntegrationTests
{
    private sealed class Clipboard : IWorkspaceClipboard
    {
        public string? Text;
        public string? Copy(string text) { Text = text; return "synthetic unavailable clipboard"; }
        public string Read() => Text!;
    }

    [WorkspaceIntegrationFact]
    public void Native_workspace_round_trip_and_conflict_preflight()
    {
        var exe = Environment.GetEnvironmentVariable("TMUX_WATCH_TEST_EXECUTABLE") ?? "tmux";
        var tag = "tw-snapshot-" + Guid.NewGuid().ToString("N")[..12];
        var source = tag + "-source";
        var restored = tag + "-restored";
        var sourceExtra = source + "-extra";
        var restoredExtra = restored + "-extra";
        var testSessions = new[] { source, sourceExtra, restored, restoredExtra };
        var reportPaths = new List<string>();
        var root = Path.Combine(Path.GetTempPath(), tag);
        var cwd1 = Path.Combine(root, "work with spaces");
        var cwd2 = Path.Combine(root, "other & directory");
        Directory.CreateDirectory(cwd1); Directory.CreateDirectory(cwd2);
        var tmux = new TmuxRunner(exe, tag);
        var config = new WatchConfig { TmuxExecutable = exe };
        var pauses = new PauseStore(Path.Combine(root, "pauses"));
        var clipboard = new Clipboard();
        var service = new WorkspaceService(tmux, config, pauses, clipboard);
        var backend = new WorkspaceBackend(tmux, config);
        string Ok(TmuxResult r) => WorkspaceBackend.Checked(r);
        string PaneTarget(string id) => backend.PaneTarget(source + ":0", id);
        try
        {
            var id1 = Ok(tmux.Run("new-session", "-d", "-s", source, "-n", "build | watch",
                "-c", cwd1, "-x", "120", "-y", "40", "-P", "-F", "#{pane_id}"));
            var id2 = Ok(tmux.Run("split-window", "-d", "-h", "-t", PaneTarget(id1), "-c", cwd2, "-P", "-F", "#{pane_id}"));
            Ok(tmux.Run("split-window", "-d", "-v", "-t", PaneTarget(id2), "-c", cwd1, "-P", "-F", "#{pane_id}"));
            if (!OperatingSystem.IsWindows()) backend.MoveWindow(source + ":0", source, 3);
            Ok(tmux.Run("new-window", "-d", "-t", source + (OperatingSystem.IsWindows() ? ":1" : ":7"), "-n", "other window", "-c", cwd2));
            Ok(tmux.Run("new-session", "-d", "-s", sourceExtra, "-n", "second session", "-c", cwd1));
            var inventory = backend.Inventory();
            Assert.All(inventory, p => Assert.Contains(p.Pane.SessionName, new[] { source, sourceExtra }));
            pauses.Set(inventory.First(p => p.Pane.CurrentPath == cwd2).Pane, true);
            var exported = service.Export(Path.Combine(root, "snapshot.json"));
            Assert.True(exported.Success, exported.Message);
            Assert.Contains("Clipboard unavailable", exported.Message);
            Assert.Equal(File.ReadAllText(exported.Path!), clipboard.Text);
            var snapshot = WorkspaceSnapshot.Parse(clipboard.Text!);
            Assert.Equal(2, snapshot.Sessions.Count);
            Assert.Equal(5, snapshot.Sessions.SelectMany(s => s.Windows).Sum(w => w.Panes.Count));
            snapshot = snapshot with { Sessions = snapshot.Sessions.OrderBy(s => s.Name).ToList() };
            Assert.Contains(snapshot.Sessions[0].Windows.SelectMany(w => w.Panes), p => p.Paused);
            snapshot.Sessions[0] = snapshot.Sessions[0] with { Name = restored };
            snapshot.Sessions[1] = snapshot.Sessions[1] with { Name = restoredExtra };
            var result = service.ImportJson(snapshot.ToJson());
            if (result.Path is not null) reportPaths.Add(result.Path);
            Assert.True(result.Success, result.Message);
            var after = service.Capture().Sessions.Single(s => s.Name == restored);
            Assert.Equal(snapshot.Sessions[0].Windows.Select(w => w.Index), after.Windows.Select(w => w.Index));
            Assert.Equal(snapshot.Sessions[0].Windows.Select(w => w.Name), after.Windows.Select(w => w.Name));
            Assert.Equal(snapshot.Sessions[0].Windows.SelectMany(w => w.Panes).Select(p => p.Directory).Order(),
                after.Windows.SelectMany(w => w.Panes).Select(p => p.Directory).Order());
            Assert.Equal(1, after.Windows.SelectMany(w => w.Panes).Count(p => p.Paused));
            var beforeCount = backend.Inventory().Count;
            var conflict = service.ImportJson(snapshot.ToJson());
            Assert.False(conflict.Success);
            Assert.Contains("Session already exists", conflict.Message);
            Assert.Equal(beforeCount, backend.Inventory().Count);
        }
        catch (Exception e) { Console.WriteLine("Round-trip failure: " + e); throw; }
        finally
        {
            var owned = new List<(int Pid, long Start)>();
            try
            {
                foreach (var entry in backend.Inventory().Where(p => testSessions.Contains(p.Pane.SessionName)))
                {
                    foreach (var pid in new[] { entry.Pane.Pid, entry.Pane.ServerPid }.Distinct())
                    {
                        try { using var p = Process.GetProcessById(pid); owned.Add((p.Id, p.StartTime.Ticks)); }
                        catch (ArgumentException) { }
                    }
                }
            }
            catch (IOException) { }
            // Test-only cleanup names only this test's sessions, never kill-server.
            foreach (var name in testSessions)
            {
                var start = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in new[] { "-L", tag, "kill-session", "-t", name }) start.ArgumentList.Add(arg);
                using var cleanup = Process.Start(start);
                cleanup?.WaitForExit(5000);
            }
            foreach (var identity in owned)
            {
                try
                {
                    using var p = Process.GetProcessById(identity.Pid);
                    if (p.StartTime.Ticks == identity.Start) { p.Kill(entireProcessTree: true); p.WaitForExit(5000); }
                }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
            for (var attempt = 0; attempt < 50 && Directory.Exists(root); attempt++)
            {
                try { Directory.Delete(root, true); }
                catch (IOException) { Thread.Sleep(100); }
            }
            if (Directory.Exists(root)) Console.WriteLine("Cleanup still pending for " + root);
            foreach (var path in reportPaths) File.Delete(path);
        }
    }
}
