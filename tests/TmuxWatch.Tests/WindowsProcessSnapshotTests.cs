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

    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows())
                Skip = "Requires the Windows Toolhelp32 API.";
        }
    }
}
