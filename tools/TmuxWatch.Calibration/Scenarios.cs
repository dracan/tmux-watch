using TmuxWatch.Detection;

namespace TmuxWatch.Calibration;

public sealed record Scenario(string Id, PaneState Target, string Instruction, BackgndReason Reason = BackgndReason.None)
{
    public bool TargetEstablished(IReadOnlyList<EvidenceEvent> events, bool freeformEditor)
    {
        if (Id == "background-monitor")
            return events.Any(e => e.Event == "PreToolUse" && e.Tool == "Monitor");
        if (Target != PaneState.Waiting) return true;
        var call = events.LastOrDefault(e => e.Event is "PreToolUse" or "PermissionRequest" && e.Child.Length == 0);
        if (call is null) return false;
        if (Id == "approval-command") return call.Gate;
        if (Id == "approval-edit") return call.Tool is "Edit" or "Write" or "apply_patch" or "create" or "edit" or "str_replace_editor";
        var question = events.LastOrDefault(e => e.Event == "PreToolUse" && Evidence.IsQuestion(e.Tool));
        if (question is null || !Evidence.IsQuestion(call.Tool)) return false;
        return Id switch
        {
            "question-multiple" => question.QuestionCount >= 2,
            "question-freeform" => question.QuestionCount == 1 && (freeformEditor || question.OptionCounts is [0]),
            _ => question.QuestionCount == 1 && question.OptionCounts.Any(n => n >= 2),
        };
    }

    public string Prompt(string agent)
    {
        var question = agent switch { "claude" => "AskUserQuestion", "copilot" => "ask_user", _ => "request_user_input" };
        return "This is a disposable local UI calibration project. Perform only the requested synthetic operation. " +
            "Use the native tools, not a simulation of their output. Do not install anything, access external services, or modify files outside this project. " +
            "The repo-owned calibration-helper.py is already present and needs only Python's standard library. " +
            "Use each given helper command verbatim, without shell wrappers, extra commands, or an appended exit-code echo; tell any subagent the same. " +
            Instruction.Replace("QUESTION_TOOL", question) +
            (Id.StartsWith("question-") ? " The requested operation is only asking and receiving these answers. Do not inspect or execute the helper for this scenario." : "") +
            " After the requested operation completes, finish your turn with a short confirmation.";
    }

    public static readonly Scenario[] All =
    [
        new("idle", PaneState.Idle, "Reply with exactly READY, without using tools."),
        new("working", PaneState.Working, "Run `python3 calibration-helper.py gate foreground 120` in the foreground. Wait for it to finish; do not detach it."),
        new("approval-command", PaneState.Waiting, "Run `python3 calibration-helper.py gate approval 10` in the foreground, requesting native user approval for the command."),
        new("approval-edit", PaneState.Waiting, "Use the native file edit tool to change sample.txt to 'Synthetic revised sample', requesting native approval for the edit."),
        new("question-choice", PaneState.Waiting, "Use QUESTION_TOOL now to ask which synthetic color to choose, offering Amber and Blue. Wait for the answer."),
        new("question-freeform", PaneState.Waiting, "Use QUESTION_TOOL now to ask for a free-text synthetic project label. Wait for the answer."),
        new("question-multiple", PaneState.Waiting, "Use QUESTION_TOOL now with two questions in one call: a choice of Amber or Blue, and a free-text project label. Wait for both answers."),
        new("background-shell", PaneState.Backgnd, "Run `python3 calibration-helper.py gate shell 120` using your native background-shell option, and return your turn immediately while it runs.", BackgndReason.BackgroundTask),
        new("background-monitor", PaneState.Backgnd, "Use your native background monitor tool to monitor `python3 calibration-helper.py gate monitor 120`, and return your turn while the monitor remains active.", BackgndReason.BackgroundTask),
        new("background-agent", PaneState.Backgnd, "Launch one native background subagent whose task is to run `python3 calibration-helper.py gate agent 120` and wait for it to finish. Detach the subagent and return your own turn immediately.", BackgndReason.BackgroundAgent),
        new("blocked-agent", PaneState.Working, "Launch a subagent to run `python3 calibration-helper.py gate agent 120`. Keep your own turn blocked waiting for that subagent to complete.", BackgndReason.None),
        new("background-both", PaneState.Backgnd, "Start `python3 calibration-helper.py gate shell 120` as a native background shell, AND detach a native background subagent running `python3 calibration-helper.py gate agent 120`. Return your turn while both run.", BackgndReason.BackgroundTask | BackgndReason.BackgroundAgent),
        new("dead", PaneState.Dead, "Reply with exactly READY, without using tools."),
    ];
}
