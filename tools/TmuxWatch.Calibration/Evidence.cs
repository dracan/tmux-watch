using System.Text.Json;
using TmuxWatch.Detection;

namespace TmuxWatch.Calibration;

public sealed record EvidenceEvent
{
    public string Event { get; init; } = "";
    public DateTimeOffset At { get; init; }
    public string Tool { get; init; } = "";
    public bool Gate { get; init; }
    public bool Background { get; init; }
    public string Child { get; init; } = "";
    public string Session { get; init; } = "";
    public bool AnswerReceived { get; init; }
    public string Call { get; init; } = "";
    public string Name { get; init; } = "";
    public string Model { get; init; } = "";
    public string Notification { get; init; } = "";
    public int QuestionCount { get; init; }
    public int[] OptionCounts { get; init; } = [];
}

public sealed record EstablishedState(PaneState State, BackgndReason Reason, DateTimeOffset Since, string Source);
public sealed record Confirmation(PaneState State, BackgndReason Reason, DateTimeOffset At, string Note);

/// <summary>No screen tokens or classifier dependency. Missing facts stay missing.</summary>
public static class Evidence
{
    public static List<EvidenceEvent> Read(string workspace) => Directory.GetFiles(Path.Combine(workspace, "evidence"), "*.json")
        .Select(f => JsonSerializer.Deserialize<EvidenceEvent>(File.ReadAllText(f), Report.Json)!)
        .OrderBy(e => e.At).ToList();

    public static EstablishedState? Establish(Scenario scenario, IReadOnlyList<EvidenceEvent> events,
        bool dead, DateTimeOffset now, Confirmation? confirmation = null)
    {
        if (dead) return new(PaneState.Dead, BackgndReason.None, now.AddSeconds(-2), "tmux pane_dead");
        var last = events.LastOrDefault();
        // A manual label expires on any later lifecycle change and after 15 seconds.
        if (confirmation is not null && now - confirmation.At < TimeSpan.FromSeconds(15) &&
            (last is null || last.At <= confirmation.At))
            return new(confirmation.State, confirmation.Reason, confirmation.At, "operator: " + confirmation.Note);

        var rootSession = events.FirstOrDefault(e => e.Event == "SessionStart" && e.Child.Length == 0)?.Session ?? "";
        var main = events.Where(e => e.Child.Length == 0 &&
            (rootSession.Length == 0 || e.Session.Length == 0 || e.Session == rootSession)).ToList();
        var stop = main.LastOrDefault(e => e.Event == "Stop");
        var start = main.LastOrDefault(e => e.Event == "UserPromptSubmit");
        if (stop is not null && start is not null && start.At > stop.At) stop = null;
        if (stop is not null && main.Any(e => e.At > stop.At && e.Event == "PreToolUse")) stop = null;
        var gates = events.Where(e => e.Event is "GateStart" or "GateEnd")
            .GroupBy(e => e.Name).Select(g => g.Last()).Where(e => e.Event == "GateStart").ToList();

        var dialog = main.LastOrDefault(e => e.Event == "Notification" &&
            e.Notification is "permission_prompt" or "elicitation_dialog");
        var dialogTool = dialog is null ? null : main.LastOrDefault(e => e.Event == "PreToolUse" && e.At <= dialog.At);
        if (dialog is not null && dialogTool is not null && !main.Any(e => e.At > dialogTool.At &&
            e.Event is "PostToolUse" or "Stop" or "GateStart" or "UserPromptSubmit"))
            return new(PaneState.Waiting, BackgndReason.None, dialog.At, "native dialog notification pending");

        var permission = main.LastOrDefault(e => e.Event == "PermissionRequest");
        if (permission is not null && !main.Any(e => e.At > permission.At && e.Event is "PostToolUse" or "Stop" or "GateStart"))
            return new(PaneState.Waiting, BackgndReason.None, permission.At, "native permission request pending");

        // PreToolUse alone does not establish that a native question rendered.
        // Those paths need a permission event, a later verified answer receipt,
        // or a time-scoped operator label.
        var question = main.LastOrDefault(e => e.Event == "PreToolUse" && IsQuestion(e.Tool));
        if (question is not null && !main.Any(e => e.At > question.At && e.Event is "PostToolUse" or "Stop")) return null;

        if (stop is not null)
        {
            if (gates.Count == 0)
            {
                var lastGateEnd = events.LastOrDefault(e => e.Event == "GateEnd");
                // A helper exiting inside a subagent does not establish that the
                // subagent itself has reported. Require that independent event too.
                var agentGate = events.LastOrDefault(e => e.Event == "GateStart" && e.Name == "agent");
                var agentCall = agentGate is null ? null : events.LastOrDefault(e => e.Event == "PreToolUse" && e.Gate && e.Child.Length > 0 && e.At <= agentGate.At);
                var agentStop = agentCall is null ? null : events.LastOrDefault(e => e.Event == "SubagentStop" && e.Child == agentCall.Child);
                if (agentGate is not null && (agentStop is null || agentStop.At < agentGate.At))
                {
                    // A completed foreground parent task is a native join even when
                    // child hook IDs are missing. A child shell result is not that join.
                    var parentTask = main.LastOrDefault(e => e.Event == "PreToolUse" && !e.Background &&
                        e.Tool is "Agent" or "Task" or "task" && e.At <= agentGate.At);
                    var joined = parentTask is null ? null : main.LastOrDefault(e => e.Event == "PostToolUse" &&
                        e.Tool == parentTask.Tool && (parentTask.Call.Length == 0 || e.Call == parentTask.Call) &&
                        e.At > agentGate.At && e.At >= (lastGateEnd?.At ?? agentGate.At));
                    if (joined is null || stop.At < joined.At) return null;
                }
                var since = new[] { stop.At, lastGateEnd?.At ?? stop.At, agentStop?.At ?? stop.At }.Max();
                return new(PaneState.Idle, BackgndReason.None, since, "main turn stopped; controlled resources and subagents completed");
            }
            var nativeTask = events.Any(e => e.Event == "PreToolUse" && e.Background && (e.Gate || e.Tool == "Monitor")) ||
                // A completed main turn with an exact native shell call and its
                // gate still alive establishes a detached shell even on Codex,
                // whose shell hook lacks a background flag.
                (scenario.Reason.HasFlag(BackgndReason.BackgroundTask) && main.Any(e => e.Event == "PreToolUse" && e.Gate));
            if (scenario.Id == "background-shell" && scenario.Target == PaneState.Working && nativeTask)
                return new(PaneState.Working, BackgndReason.None, gates.Max(e => e.At),
                    "runtime waits for its native async shells; controlled gate remains live");
            var nativeAgent = main.Any(e => e.Event is "PreToolUse" or "PostToolUse" && e.Background &&
                e.Tool is "Agent" or "Task" or "task" or "collaborationspawn_agent");
            var requestedKindsEstablished = (!scenario.Reason.HasFlag(BackgndReason.BackgroundTask) || nativeTask) &&
                (!scenario.Reason.HasFlag(BackgndReason.BackgroundAgent) || nativeAgent);
            if (requestedKindsEstablished && scenario.Target == PaneState.Backgnd)
            {
                var names = gates.Select(e => e.Name).ToHashSet();
                var reason = BackgndReason.None;
                if (names.Contains("shell") || names.Contains("monitor")) reason |= BackgndReason.BackgroundTask;
                if (names.Contains("agent")) reason |= BackgndReason.BackgroundAgent;
                if (reason == scenario.Reason)
                    return new(PaneState.Backgnd, reason, gates.Max(e => e.At) > stop.At ? gates.Max(e => e.At) : stop.At,
                        "main Stop, native background launch, and live helper gates");
            }
            return null;
        }

        var foreground = main.LastOrDefault(e => e.Event == "PreToolUse" && !e.Background &&
            (e.Gate || e.Tool is "Agent" or "Task" or "task" or "collaborationwait_agent"));
        if (foreground is not null && gates.Count > 0 &&
            !main.Any(e => e.At > foreground.At && e.Event == "PostToolUse" && (e.Call.Length == 0 || e.Call == foreground.Call)))
            return new(PaneState.Working, BackgndReason.None,
                gates.Max(e => e.At) > foreground.At ? gates.Max(e => e.At) : foreground.At,
                "foreground native tool pending and helper gate running");
        return null;
    }

    public static bool IsQuestion(string tool) => tool is "AskUserQuestion" or "ask_user" or "request_user_input";
}

public enum Outcome { Pass, Mismatch, Inconclusive, Unavailable, Unsupported }
public sealed record Sample(DateTimeOffset At, string Capture, PaneState Actual, BackgndReason Reason,
    EstablishedState? Expected, PaneState Monitor, string[] Events, string Pointer);
public sealed record CheckResult(string Name, Outcome Outcome, string Detail);

public static class Evaluation
{
    public static CheckResult Assess(string name, IReadOnlyList<Sample> samples, PaneState target,
        BackgndReason reason, bool supported, int stableSamples)
    {
        var eligible = samples.Where(s => s.Expected?.State == target && s.Expected.Reason == reason &&
            s.At - s.Expected.Since >= TimeSpan.FromSeconds(1)).ToList();
        if (eligible.Count < stableSamples)
            return new(name, Outcome.Inconclusive, $"Only {eligible.Count}/{stableSamples} independently established samples");
        if (!supported) return new(name, Outcome.Unsupported, "Live state established; profile has no fingerprint for this capability");
        var mismatches = eligible.Count(s => s.Actual != target || (target == PaneState.Backgnd && s.Reason != reason));
        var consecutive = 0;
        var worst = 0;
        foreach (var s in samples)
        {
            var established = s.Expected?.State == target && s.Expected.Reason == reason &&
                s.At - s.Expected.Since >= TimeSpan.FromSeconds(1);
            consecutive = established && (s.Actual != target || (target == PaneState.Backgnd && s.Reason != reason)) ? consecutive + 1 : 0;
            worst = Math.Max(worst, consecutive);
        }
        // Never turn a mixture of failed samples into a clean pass by taking only
        // the final frame. Short rendering transitions are explicit in the detail.
        return new(name, worst >= stableSamples ? Outcome.Mismatch : Outcome.Pass,
            $"{eligible.Count} established samples; {mismatches} mismatches; longest mismatch run {worst}");
    }

    public static int ExitCode(IEnumerable<CheckResult> checks)
    {
        var list = checks.ToList();
        return list.Any(c => c.Outcome == Outcome.Mismatch) ? 1 :
            list.Count == 0 || list.Any(c => c.Outcome != Outcome.Pass) ? 2 : 0;
    }
}
