using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace TmuxWatch.Tmux;

/// <summary>
/// Shells out to the tmux CLI via <see cref="ProcessStartInfo.ArgumentList"/>
/// (avoids quoting issues). A verb whitelist enforces the read-only guarantee:
/// input-injecting verbs such as <c>send-keys</c> can never be invoked.
/// </summary>
public sealed class TmuxRunner : ITmuxClient
{
    private static readonly HashSet<string> AllowedVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "lsp", "list-panes",
        "capture-pane", "capturep",
        "display-message", "display",
        "switch-client", "switchc",
        "select-window", "selectw",
        "select-pane", "selectp",
    };

    private readonly string _exe;

    public TmuxRunner(string executable) => _exe = executable;

    public TmuxResult ListPanesRaw(string format) =>
        Run("lsp", "-a", "-F", format);

    public TmuxResult CapturePane(string paneId) =>
        Run("capture-pane", "-p", "-t", paneId);

    public TmuxResult SwitchClient(string sessionName) =>
        Run("switch-client", "-t", sessionName);

    public TmuxResult SelectWindow(string windowTarget) =>
        Run("select-window", "-t", windowTarget);

    public TmuxResult SelectPane(string paneId) =>
        Run("select-pane", "-t", paneId);

    public TmuxResult Run(params string[] args)
    {
        if (args.Length == 0)
            throw new ArgumentException("At least one tmux verb is required.", nameof(args));

        var verb = args[0];
        if (!AllowedVerbs.Contains(verb))
            throw new InvalidOperationException(
                $"tmux verb '{verb}' is not permitted; tmux-watch is read-only toward panes.");

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
                return TmuxResult.NotStarted($"Failed to start '{_exe}'.");

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return new TmuxResult(true, proc.ExitCode, stdout, stderr);
        }
        catch (Win32Exception ex)
        {
            // Executable not found / not on PATH.
            return TmuxResult.NotStarted($"Could not run '{_exe}': {ex.Message}");
        }
    }
}
