using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using TmuxWatch.Tmux;

namespace TmuxWatch.Workspace;

/// <summary>Per-pane atomic records avoid read/modify/write races across watchers.</summary>
public sealed class PauseStore
{
    private readonly string _directory;
    private readonly Func<int, long> _started;

    public PauseStore(string? directory = null) : this(directory ?? Path.Combine(WorkspaceFiles.DataDirectory, "paused"), ProcessStarted) { }
    internal PauseStore(string directory, Func<int, long> started)
    {
        _directory = directory;
        _started = started;
    }

    private static long ProcessStarted(int pid)
    {
        if (pid <= 0) return 0;
        try { using var p = Process.GetProcessById(pid); return p.StartTime.ToUniversalTime().Ticks; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return 0; }
    }

    private string RecordPath(Pane pane)
    {
        // Server process lifetime survives a dead pane and changes on a server restart.
        // Psmux runs session processes; the same rule scopes its locally assigned IDs.
        var owner = pane.ServerPid > 0 ? pane.ServerPid : pane.Pid;
        var started = _started(owner);
        if (started <= 0) throw new IOException($"Cannot verify pause identity for {pane.Location}.");
        var identity = $"{Environment.MachineName}|{owner}|{started}|{pane.Id}|{pane.Pid}";
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return Path.Combine(_directory, key + ".state");
    }

    public bool IsPaused(Pane pane)
    {
        var path = RecordPath(pane);
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(file);
            return reader.ReadToEnd() switch
            {
                "paused\n" => true,
                "active\n" => false,
                _ => throw new InvalidDataException("Invalid pause record: " + path),
            };
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    public void Set(Pane pane, bool paused)
    {
        var path = RecordPath(pane);
        Directory.CreateDirectory(_directory);
        // Write an explicit false record: rename is atomic and concurrent toggles have
        // a well-defined last-completed-write result without lost updates to other panes.
        WorkspaceFiles.AtomicWrite(path, paused ? "paused\n" : "active\n", overwrite: true);
    }
}

public static class WorkspaceFiles
{
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "tmux-watch");

    public static string NewPath(string kind, string extension) => Path.Combine(DataDirectory, kind,
        $"{kind}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}.{extension}");

    internal static void AtomicWrite(string path, string text, bool overwrite)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions
            { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var file = new FileStream(temporary, options))
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
