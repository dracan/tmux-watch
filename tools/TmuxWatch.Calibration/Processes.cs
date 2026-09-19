using System.Diagnostics;

namespace TmuxWatch.Calibration;

public sealed record ProcessResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;
}

public static class Processes
{
    public static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";

    public static async Task<ProcessResult> Run(string executable, IEnumerable<string> args,
        CancellationToken cancellation = default, string? cwd = null, int timeoutSeconds = 15)
    {
        var info = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, WorkingDirectory = cwd ?? Environment.CurrentDirectory,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            return new(process.ExitCode, await stdout.WaitAsync(deadline.Token), await stderr.WaitAsync(deadline.Token));
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }

    public static string? Find(string executable) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        .Select(p => Path.Combine(p, executable)).FirstOrDefault(File.Exists);
}

/// <summary>Development-only driver. It cannot accept an existing socket or foreign pane.</summary>
public sealed class OwnedTmux : IAsyncDisposable
{
    public string Socket { get; } = "tw-cal-" + Guid.NewGuid().ToString("N");
    private readonly HashSet<string> panes = new(StringComparer.Ordinal);
    private bool started;
    public bool Retain { get; set; }

    private Task<ProcessResult> Call(CancellationToken ct, params string[] args) =>
        Processes.Run("tmux", new[] { "-L", Socket, "-f", "/dev/null" }.Concat(args), ct);

    private async Task<string> Checked(CancellationToken ct, params string[] args)
    {
        var result = await Call(ct, args);
        if (!result.Ok) throw new IOException("Private tmux command failed: " + result.Error.Trim());
        return result.Output.TrimEnd('\r', '\n');
    }

    public async Task Start(int lifetimeSeconds, CancellationToken ct)
    {
        // The keeper and every agent have their own timeout even if this process is killed.
        started = true;
        await Checked(ct, "new-session", "-d", "-s", "keeper", "bash", "-c",
            $"sleep {lifetimeSeconds}; exec tmux -L {Socket} kill-server");
        await Checked(ct, "set-option", "-g", "remain-on-exit", "on");
        await Checked(ct, "set-option", "-g", "status", "off");
        await Checked(ct, "set-option", "-g", "history-limit", "0");
    }

    public async Task<string> Launch(string session, string cwd, string launcher, int width, int height, CancellationToken ct)
    {
        var pane = await Checked(ct, "new-session", "-d", "-P", "-F", "#{pane_id}",
            "-s", session, "-x", width.ToString(), "-y", height.ToString(), "-c", cwd, "bash", launcher);
        if (!System.Text.RegularExpressions.Regex.IsMatch(pane, "^%[0-9]+$"))
            throw new IOException("tmux did not return a pane id");
        panes.Add(pane);
        return pane;
    }

    public void RequireOwned(string pane)
    {
        if (!panes.Contains(pane)) throw new InvalidOperationException("Pane is not owned by this harness");
    }

    public async Task<(string Text, bool Dead)> Capture(string pane, CancellationToken ct)
    {
        RequireOwned(pane);
        var text = await Checked(ct, "capture-pane", "-p", "-t", pane);
        var dead = await Checked(ct, "display-message", "-p", "-t", pane, "#{pane_dead}");
        return (text, dead == "1");
    }

    public Task<string> CurrentPath(string pane, CancellationToken ct)
    {
        RequireOwned(pane);
        return Checked(ct, "display-message", "-p", "-t", pane, "#{pane_current_path}");
    }

    public async Task SendKey(string pane, string key, CancellationToken ct)
    {
        RequireOwned(pane);
        if (key is not ("Enter" or "Escape" or "C-c" or "C-d" or "Down" or "Tab"))
            throw new ArgumentException("Unsupported harness key");
        await Checked(ct, "send-keys", "-t", pane, key);
    }

    public async Task SendText(string pane, string text, CancellationToken ct)
    {
        RequireOwned(pane);
        await Checked(ct, "send-keys", "-t", pane, "-l", "--", text);
        await SendKey(pane, "Enter", ct);
    }

    public async Task Close(string pane)
    {
        RequireOwned(pane);
        var result = await Call(CancellationToken.None, "kill-pane", "-t", pane);
        if (!result.Ok && !ServerAbsent(result.Error)) throw new IOException("Could not clean up private pane: " + result.Error.Trim());
        panes.Remove(pane);
    }

    public async ValueTask DisposeAsync()
    {
        if (started && !Retain)
        {
            var result = await Call(CancellationToken.None, "kill-server");
            if (!result.Ok && !ServerAbsent(result.Error)) throw new IOException("Could not clean up private server: " + result.Error.Trim());
            started = false;
        }
    }

    private static bool ServerAbsent(string error) => error.Contains("no server running", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase);
}
