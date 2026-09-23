using System.Diagnostics;
using System.Text;

namespace TmuxWatch.Workspace;

public interface IWorkspaceClipboard
{
    /// <returns>Null on success, otherwise a diagnostic. The saved file remains usable.</returns>
    string? Copy(string text);
    string Read();
}

public sealed class WorkspaceClipboard : IWorkspaceClipboard
{
    public string? Copy(string text)
    {
        try { Transfer(text); return null; }
        catch (IOException e) { return e.Message; }
    }

    public string Read() => Transfer(null);

    private static string Transfer(string? input)
    {
        var failures = new List<string>();
        foreach (var (exe, args) in Commands(input is not null))
        {
            try
            {
                var start = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardInputEncoding = new UTF8Encoding(false),
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                };
                foreach (var arg in args) start.ArgumentList.Add(arg);
                using var process = Process.Start(start) ?? throw new IOException("Clipboard process did not start.");
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                var write = Task.Run(async () =>
                {
                    if (input is not null) await process.StandardInput.WriteAsync(input);
                    process.StandardInput.Close();
                });
                var completed = Task.WhenAll(output, error, write, process.WaitForExitAsync());
                if (!completed.Wait(TimeSpan.FromSeconds(5)))
                {
                    try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                    throw new IOException("Clipboard command timed out.");
                }
                if (process.ExitCode != 0) throw new IOException(error.Result.Trim());
                return output.Result;
            }
            catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or AggregateException)
            { failures.Add(exe + ": " + e.GetBaseException().Message); }
        }
        throw new IOException(string.Join("; ", failures));
    }

    private static IEnumerable<(string Exe, string[] Args)> Commands(bool copy)
    {
        if (OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") is not null)
        {
            var script = "[Console]::InputEncoding=[System.Text.UTF8Encoding]::new($false);" +
                "[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false);" +
                (copy ? "Set-Clipboard -Value ([Console]::In.ReadToEnd())" : "[Console]::Write((Get-Clipboard -Raw))");
            yield return ("powershell.exe", ["-NoProfile", "-NonInteractive", "-STA", "-EncodedCommand",
                Convert.ToBase64String(Encoding.Unicode.GetBytes(script))]);
            if (OperatingSystem.IsWindows()) yield break;
        }
        if (OperatingSystem.IsMacOS())
        {
            yield return (copy ? "pbcopy" : "pbpaste", []);
            yield break;
        }
        yield return (copy ? "wl-copy" : "wl-paste", copy ? [] : ["--no-newline"]);
        yield return ("xclip", copy ? ["-selection", "clipboard", "-in"] : ["-selection", "clipboard", "-out"]);
    }
}
