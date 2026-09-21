using System.Diagnostics;
using TmuxWatch.Config;
using TmuxWatch.Discovery;

namespace TmuxWatch.Tests;

public class WindowsProcessSnapshotTests
{
    [Fact]
    public void Unix_does_not_inspect_local_processes()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Null(WindowsProcessSnapshot.Capture(new HashSet<int> { Environment.ProcessId }));
    }

    [WindowsFact]
    public void Native_snapshot_recognises_the_live_root_process()
    {
        using var process = Process.GetCurrentProcess();
        var roots = new HashSet<int> { process.Id };
        var snapshot = WindowsProcessSnapshot.Capture(roots);
        Assert.NotNull(snapshot);
        var profile = new AgentProfile { Id = "test", Command = process.ProcessName };
        Assert.Same(profile, snapshot.FindOwner(process.Id, roots, new[] { profile }));
    }

    [WindowsFact]
    public void Native_discovery_follows_a_child_and_releases_it_after_exit()
    {
        // A disposable local child, unrelated to tmux: wait on redirected stdin.
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true,
        };
        start.ArgumentList.Add("/d");
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("set /p hold=");
        using var child = Process.Start(start)!;
        try
        {
            var cfg = new WatchConfig
            {
                Agents = new() { new AgentProfile { Id = "test-child", Command = "cmd" } },
            };
            var tmux = new FakeTmuxClient
            {
                ListOutput = $"%test|test|0|0|helper|0|||0|0|{Environment.ProcessId}",
            };
            // Exercise the production constructor and adapter, not an injected snapshot.
            var discovery = new PaneDiscovery(tmux, cfg, "");
            Assert.Equal("test-child", Assert.Single(discovery.DiscoverPanes().AgentPanes).AgentId);
            child.StandardInput.Close();
            Assert.True(child.WaitForExit(5000));
            Assert.Empty(discovery.DiscoverPanes().AgentPanes);
            Assert.Empty(tmux.CapturedPanes);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
            }
        }
    }

    [WindowsFact]
    public async Task Native_discovery_keeps_the_owner_when_its_running_executable_is_renamed()
    {
        // Reproduce an updater moving a running binary aside. Only our disposable
        // copy is renamed; neither cmd.exe nor an installed agent is modified.
        var directory = Directory.CreateTempSubdirectory("tmux-watch-rename-").FullName;
        var executable = Path.Combine(directory, "copilot.exe");
        Process? child = null;
        try
        {
            File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), executable);
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("/d");
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add("echo ready&set /p hold=");
            child = Process.Start(start)!;
            Assert.Equal("ready", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            var startedAt = child.StartTime;
            var tmux = new FakeTmuxClient();
            var discovery = new PaneDiscovery(tmux, new WatchConfig(), "");
            foreach (var command in new[] { "apphost", "tgrep" })
            {
                tmux.ListOutput = $"%test|test|0|0|{command}|0|||0|0|{child.Id}";
                Assert.Equal("copilot", Assert.Single(discovery.DiscoverPanes().AgentPanes).AgentId);
            }

            File.Move(executable, executable + ".old-123-456");
            using var renamed = Process.GetProcessById(child.Id);
            Assert.False(renamed.HasExited);
            Assert.Equal(startedAt, renamed.StartTime);
            // A fresh Process instance is important: ProcessName can be cached.
            Assert.Equal("copilot.exe.old-123-456", renamed.ProcessName);
            foreach (var command in new[] { "apphost", "tgrep" })
            {
                tmux.ListOutput = $"%test|test|0|0|{command}|0|||0|0|{child.Id}";
                var inventory = discovery.DiscoverPanes();
                Assert.Equal("copilot", Assert.Single(inventory.AgentPanes).AgentId);
                Assert.Empty(inventory.OtherPanes);
            }

            child.StandardInput.Close();
            Assert.True(child.WaitForExit(5000));
            Assert.Empty(discovery.DiscoverPanes().AgentPanes);
            Assert.Empty(tmux.CapturedPanes);
        }
        finally
        {
            if (child is not null)
            {
                if (!child.HasExited)
                {
                    child.Kill(entireProcessTree: true);
                    child.WaitForExit();
                }
                child.Dispose();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows())
                Skip = "Requires the Windows Toolhelp32 API.";
        }
    }
}
