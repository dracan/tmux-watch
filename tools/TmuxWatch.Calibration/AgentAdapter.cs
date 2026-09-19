using System.Text.Json;
using TmuxWatch.Config;

namespace TmuxWatch.Calibration;

public sealed record Preflight(string Agent, string? Executable, string Version, bool Available, string Authentication);

public static class AgentAdapter
{
    public static readonly string[] Ids = ["copilot", "claude", "codex"];
    public static AgentProfile Profile(string id) => id switch
    {
        "copilot" => WatchConfig.CopilotProfile(), "claude" => WatchConfig.ClaudeProfile(),
        "codex" => WatchConfig.CodexProfile(), _ => throw new ArgumentException("Unknown agent"),
    };

    public static async Task<Preflight> Check(string id, CancellationToken ct)
    {
        var executable = Processes.Find(id);
        if (executable is null) return new(id, null, "unknown", false, "not installed");
        try
        {
            var version = await Processes.Run(executable, ["--version"], ct);
            if (!version.Ok) return new(id, executable, "unknown", false, "version command failed");
            var text = version.Output.Trim().Split('\n')[0];
            if (id == "copilot") return new(id, executable, text, true, "verify in interactive startup");
            var auth = await Processes.Run(executable, id == "claude" ? ["auth", "status"] : ["login", "status"], ct);
            var loggedIn = id == "claude"
                ? auth.Ok && JsonDocument.Parse(auth.Output).RootElement.GetProperty("loggedIn").GetBoolean()
                : auth.Ok && (auth.Output + auth.Error).Contains("Logged in", StringComparison.OrdinalIgnoreCase);
            return new(id, executable, text, loggedIn, loggedIn ? "authenticated" : "login required");
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new(id, executable, "unknown", false, "preflight failed (" + e.GetType().Name + ")");
        }
    }

    public static async Task<string> Prepare(string id, string executable, Scenario scenario, string workspace,
        string? model, int lifetimeSeconds, CancellationToken ct)
    {
        Directory.CreateDirectory(workspace);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "helper.py"), Path.Combine(workspace, "calibration-helper.py"));
        File.WriteAllText(Path.Combine(workspace, "sample.txt"), "Synthetic calibration sample.\n");
        Directory.CreateDirectory(Path.Combine(workspace, "evidence"));
        await Processes.Run("git", ["init", "-q", workspace], ct);
        var hookPath = Path.Combine(workspace, "calibration-helper.py");
        var events = new[] { "SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "PermissionRequest", "Stop", "SubagentStart", "SubagentStop" };
        var hooks = new Dictionary<string, object>();
        foreach (var ev in events)
        {
            var command = $"python3 {Processes.Quote(hookPath)} hook {ev} {scenario.Id} {id}";
            if (id == "copilot")
            {
                var name = ev switch { "UserPromptSubmit" => "userPromptSubmitted", "Stop" => "agentStop", _ => char.ToLowerInvariant(ev[0]) + ev[1..] };
                // PermissionRequest precedes auto-allow rules, so it cannot prove
                // a visible prompt. Notification supplies the actual dialog signal.
                if (ev == "PermissionRequest" || ev == "SubagentStart") continue;
                hooks[name] = new[] { new { type = "command", bash = command, timeoutSec = 5 } };
            }
            else hooks[ev] = new[] { new { hooks = new[] { new { type = "command", command, timeout = 5 } } } };
        }
        if (id == "copilot")
            hooks["notification"] = new[] { new { type = "command",
                bash = $"python3 {Processes.Quote(hookPath)} hook Notification {scenario.Id} {id}", timeoutSec = 5 } };
        var args = new List<string>();
        if (id == "claude")
        {
            var settings = Path.Combine(workspace, "claude-settings.json");
            File.WriteAllText(settings, JsonSerializer.Serialize(new { hooks }));
            args.AddRange(["--setting-sources", "", "--settings", settings, "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}", "--permission-mode", "manual"]);
            if (!scenario.Id.StartsWith("approval-")) args.AddRange(["--allowedTools", "Bash(python3 calibration-helper.py *)"]);
        }
        else if (id == "codex")
        {
            Directory.CreateDirectory(Path.Combine(workspace, ".codex"));
            File.WriteAllText(Path.Combine(workspace, ".codex", "hooks.json"), JsonSerializer.Serialize(new { hooks }));
            args.AddRange(["-c", "projects." + JsonSerializer.Serialize(workspace) + ".trust_level=\"trusted\"",
                "--dangerously-bypass-hook-trust", "-s", scenario.Id == "approval-edit" ? "read-only" : "workspace-write",
                "-a", "on-request", "-c", "approvals_reviewer=\"user\""]);
            if (scenario.Id == "approval-command")
            {
                var rules = Path.Combine(workspace, ".codex", "rules");
                Directory.CreateDirectory(rules);
                File.WriteAllText(Path.Combine(rules, "calibration.rules"),
                    "prefix_rule(pattern=[\"python3\", \"calibration-helper.py\", \"gate\"], decision=\"prompt\")\n");
            }
            if (scenario.Id.StartsWith("question-"))
                args.AddRange(["-c", "features.default_mode_request_user_input=true"]);
        }
        else
        {
            Directory.CreateDirectory(Path.Combine(workspace, ".github", "hooks"));
            File.WriteAllText(Path.Combine(workspace, ".github", "hooks", "calibration.json"), JsonSerializer.Serialize(new { version = 1, hooks }));
            args.AddRange(["--no-auto-update", "--disable-builtin-mcps"]);
            if (!scenario.Id.StartsWith("approval-")) args.AddRange(["--allow-tool", "shell(python3)"]);
        }
        if (model is not null) args.AddRange(["--model", model]);
        var prompt = scenario.Prompt(id);
        if (id == "copilot") args.AddRange(["-i", prompt]);
        else args.AddRange(["--", prompt]);
        File.WriteAllText(Path.Combine(workspace, "launch-metadata.json"), JsonSerializer.Serialize(new
        {
            agent = id, model = model ?? "agent default; see hook model when supplied", args,
            settings = "temporary project hooks; existing authentication; normal permission enforcement",
        }, Report.Json));
        var launcher = Path.Combine(workspace, "launch.sh");
        File.WriteAllText(launcher, "#!/bin/sh\n" +
            "unset CLAUDECODE TMUX TMUX_PANE\n" +
            "export TERM=xterm-256color\n" +
            "exec timeout --foreground --signal=TERM --kill-after=5s " + lifetimeSeconds + " " +
            Processes.Quote(executable) + " " + string.Join(" ", args.Select(Processes.Quote)) + "\n");
        return launcher;
    }
}
