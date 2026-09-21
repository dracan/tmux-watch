using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace TmuxWatch.Tmux;

/// <summary>
/// Shells out to the tmux CLI via <see cref="ProcessStartInfo.ArgumentList"/>
/// (avoids quoting issues). A verb whitelist enforces the inviolable tier of the
/// boundary in AGENTS.md: input-injecting verbs such as <c>send-keys</c> can never be
/// invoked, so a pane's content stays read-only. The whitelist also carries the focus
/// verbs and the one lifecycle verb (<c>new-window</c>) the watcher may use.
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
        "new-window", "neww",
    };

    private readonly string _exe;

    public TmuxRunner(string executable) => _exe = executable;

    // psmux 3.3.8 returns session creation time for window_activity. Native Windows
    // includes its tmux.exe alias; explicit psmux paths are unsupported on any host.
    // Keep unknown until a host/version with genuine window activity is verified.
    public bool SupportsWindowActivity => !OperatingSystem.IsWindows() &&
        !string.Equals(Path.GetFileNameWithoutExtension(_exe.Replace('\\', '/')),
            "psmux", StringComparison.OrdinalIgnoreCase);

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

    public TmuxResult NewWindow(string sessionName, string? windowName) =>
        Run(NewWindowArgs(sessionName, windowName));

    /// <summary>
    /// The exact argument list for a create. Built here rather than taken from the caller,
    /// so no code path can append the trailing shell-command argument new-window accepts:
    /// the user-typed name reaches <c>-n</c> and nothing else, and because <c>-n</c>
    /// consumes its value a name starting with '-' is not read as a flag.
    /// <para>
    /// <c>-d</c> keeps the create detached - the caller jumps itself, through the focus
    /// verbs - and <c>-P -F</c> prints the new window's id, which is the only way to then
    /// select a window that was never made current.
    /// </para>
    /// <para>
    /// The target must be <c>"&lt;session&gt;:"</c>. new-window's <c>-t</c> is a target
    /// <em>window</em>, not a target session, and a bare session name is only read as a
    /// session when it cannot be read as a window index - so a numerically named session
    /// ("0", tmux's default for an unnamed one) silently becomes an index in whichever
    /// session is current, landing the window in the wrong session or failing with
    /// "index N in use". The trailing colon names the session with the window part left
    /// empty, so tmux appends at the next free index.
    /// </para>
    /// </summary>
    internal static string[] NewWindowArgs(string sessionName, string? windowName)
    {
        var args = new List<string>
        {
            "new-window", "-d", "-P", "-F", "#{window_id}", "-t", sessionName + ":",
        };
        if (!string.IsNullOrWhiteSpace(windowName))
        {
            args.Add("-n");
            args.Add(windowName);
        }
        return args.ToArray();
    }

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
