using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace TmuxWatch.Psmux;

/// <summary>
/// Shells out to the psmux CLI via <see cref="ProcessStartInfo.ArgumentList"/>
/// (avoids quoting issues). A verb whitelist enforces the read-only guarantee:
/// input-injecting verbs such as <c>send-keys</c> can never be invoked.
/// </summary>
public sealed class PsmuxRunner : IPsmuxClient
{
    private static readonly HashSet<string> AllowedVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "lsp", "list-panes",
        "capture-pane", "capturep",
        "display-message", "display",
        "switch-client", "switchc",
        "select-window", "selectw",
    };

    private readonly string _exe;

    public PsmuxRunner(string executable) => _exe = executable;

    public PsmuxResult ListPanesRaw(string format) =>
        Run("lsp", "-a", "-F", format);

    public PsmuxResult CapturePane(string paneId) =>
        Run("capture-pane", "-p", "-t", paneId);

    public PsmuxResult SwitchClient(string sessionName) =>
        Run("switch-client", "-t", sessionName);

    public PsmuxResult SelectWindow(string windowTarget) =>
        Run("select-window", "-t", windowTarget);

    public PsmuxResult Run(params string[] args)
    {
        if (args.Length == 0)
            throw new ArgumentException("At least one psmux verb is required.", nameof(args));

        var verb = args[0];
        if (!AllowedVerbs.Contains(verb))
            throw new InvalidOperationException(
                $"psmux verb '{verb}' is not permitted; tmux-watch is read-only toward panes.");

        var psi = new ProcessStartInfo
        {
            FileName = _exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
                return PsmuxResult.NotStarted($"Failed to start '{_exe}'.");

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return new PsmuxResult(true, proc.ExitCode, stdout, stderr);
        }
        catch (Win32Exception ex)
        {
            // Executable not found / not on PATH.
            return PsmuxResult.NotStarted($"Could not run '{_exe}': {ex.Message}");
        }
    }
}
