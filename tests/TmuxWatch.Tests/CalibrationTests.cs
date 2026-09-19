using System.Text.Json;
using TmuxWatch.Calibration;
using TmuxWatch.Detection;

namespace TmuxWatch.Tests;

public sealed class CalibrationTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    private static Scenario Scenario(string id) => Calibration.Scenario.All.Single(s => s.Id == id);
    private static EvidenceEvent Event(string name, int second, string tool = "", bool gate = false, bool background = false, string child = "", string gateName = "") =>
        new() { Event = name, At = Start.AddSeconds(second), Tool = tool, Gate = gate, Background = background, Child = child, Name = gateName };

    [Fact]
    public void Child_session_stop_cannot_finish_parent_turn()
    {
        var events = new[] {
            Event("SessionStart", 0) with { Session = "parent" },
            Event("UserPromptSubmit", 1) with { Session = "parent" },
            Event("Stop", 2) with { Session = "child" },
        };
        Assert.Null(Evidence.Establish(Scenario("idle"), events, false, Start.AddSeconds(4)));
    }

    [Fact]
    public void Foreground_parent_task_result_joins_child_without_child_hook_ids()
    {
        var events = new[] {
            Event("PreToolUse", 1, "task") with { Call = "task" }, Event("GateStart", 2, gateName: "agent"),
            Event("GateEnd", 3, gateName: "agent"), Event("PostToolUse", 4, "task") with { Call = "task" }, Event("Stop", 5),
        };
        Assert.Equal(PaneState.Idle, Evidence.Establish(Scenario("blocked-agent"), events, false, Start.AddSeconds(6))!.State);
        Assert.Null(Evidence.Establish(Scenario("blocked-agent"), events.Where(e => e.Event != "PostToolUse").ToArray(), false, Start.AddSeconds(6)));
    }

    [Fact]
    public void Copilot_native_async_shell_remains_working_after_model_stop_until_gate_exits()
    {
        var scenario = Scenario("background-shell").ForAgent("copilot");
        var events = new[] { Event("PreToolUse", 1, "bash", gate: true, background: true),
            Event("GateStart", 2, gateName: "shell"), Event("Stop", 3) };
        Assert.Equal(PaneState.Working, Evidence.Establish(scenario, events, false, Start.AddSeconds(5))!.State);
        Assert.Null(Evidence.Establish(scenario, events.Skip(1).ToArray(), false, Start.AddSeconds(5)));
        Assert.Equal(PaneState.Idle, Evidence.Establish(scenario,
            events.Append(Event("GateEnd", 6, gateName: "shell")).ToArray(), false, Start.AddSeconds(8))!.State);
    }

    [Fact]
    public void Native_async_agent_result_establishes_background_and_requires_its_own_child_completion()
    {
        var events = new[] {
            Event("PreToolUse", 1, "Agent"), Event("PostToolUse", 2, "Agent", background: true),
            Event("PreToolUse", 3, "Bash", gate: true, child: "worker"),
            Event("GateStart", 4, gateName: "agent"), Event("Stop", 5),
        };
        Assert.Equal(PaneState.Backgnd, Evidence.Establish(Scenario("background-agent"), events, false, Start.AddSeconds(7))!.State);
        Assert.Null(Evidence.Establish(Scenario("background-agent"), events.Select(e => e with { Background = false }).ToArray(), false, Start.AddSeconds(7)));
        var ended = events.Append(Event("GateEnd", 8, gateName: "agent")).ToArray();
        Assert.Null(Evidence.Establish(Scenario("background-agent"), ended.Append(Event("SubagentStop", 9, child: "unrelated")).ToArray(), false, Start.AddSeconds(11)));
        Assert.Equal(PaneState.Idle, Evidence.Establish(Scenario("background-agent"), ended.Append(Event("SubagentStop", 9, child: "worker")).ToArray(), false, Start.AddSeconds(11))!.State);
    }

    [Fact]
    public void Question_receipt_requires_matching_native_answer_and_only_labels_its_interval()
    {
        var question = Event("PreToolUse", 1, "request_user_input") with { Call = "question" };
        List<Sample> samples = Enumerable.Range(0, 8).Select(i => new Sample(Start.AddSeconds(i), "capture", PaneState.Unknown,
            BackgndReason.None, null, PaneState.Unknown, [], "Normal")).ToList();
        var receipt = Event("PostToolUse", 7, "request_user_input") with { Call = "question", AnswerReceived = true };
        Assert.True(QuestionReceipt.IsPending(question, [question]));
        Assert.False(QuestionReceipt.IsPending(question, [question, Event("PreToolUse", 2, "other")]));
        Assert.False(QuestionReceipt.IsPending(question, [question, receipt]));
        Assert.False(QuestionReceipt.Establish(samples, question, Start.AddSeconds(2), Start.AddSeconds(6), [receipt with { Call = "other" }]));
        Assert.False(QuestionReceipt.Establish(samples, question, Start.AddSeconds(2), Start.AddSeconds(6), [receipt with { AnswerReceived = false }]));
        Assert.False(QuestionReceipt.Establish(samples, question, Start.AddSeconds(2), Start.AddSeconds(6), [Event("Stop", 3), receipt]));
        Assert.False(QuestionReceipt.Establish(samples, question, Start.AddSeconds(2), Start.AddSeconds(6), [Event("PreToolUse", 3, "other"), receipt]));
        Assert.True(QuestionReceipt.Establish(samples, question, Start.AddSeconds(2), Start.AddSeconds(6), [receipt]));
        Assert.Null(samples[0].Expected);
        Assert.Equal(PaneState.Waiting, samples[3].Expected!.State);
        Assert.Null(samples[7].Expected);
    }

    [Fact]
    public void Check_scope_exclusions_are_explicit_and_full_run_still_reports_unsupported()
    {
        Assert.NotNull(Scenario("background-monitor").CheckExclusion("copilot"));
        Assert.Null(Scenario("background-monitor").CheckExclusion("claude"));
        Assert.Null(Scenario("background-shell").CheckExclusion("codex"));
        Assert.True(Scenario("background-shell").Supported("codex"));
        Assert.Equal(PaneState.Working, Scenario("background-shell").ForAgent("copilot").Target);
        Assert.Equal(2, Evaluation.ExitCode([new("invisible", Outcome.Unsupported, "No live indicator")]));
    }

    [Fact]
    public void Upcoming_login_expiry_is_not_a_login_screen_but_actual_login_still_is()
    {
        Assert.False(LiveRun.LooksLikeLogin("Your login expires in 2 days - run /login to renew"));
        Assert.True(LiveRun.LooksLikeLogin("Please run /login"));
        Assert.True(LiveRun.LooksLikeLogin("Your login expires in 2 days - run /login to renew\nSign in to continue"));
        Assert.True(LiveRun.LooksLikeLogin("Enter device code"));
    }

    [Fact]
    public void Excluding_every_requested_live_check_cannot_produce_success()
    {
        var report = new Report { Root = "/unused", Finished = Start };
        report.Replay.Add(new("replay", Outcome.Pass, ""));
        report.Exclusions.Add(new("native-monitor", Outcome.Unsupported, "Unavailable tool"));
        Assert.Equal(2, report.ExitCode);
    }

    [Fact]
    public void Helper_running_without_foreground_evidence_is_inconclusive()
    {
        var events = new[] { Event("GateStart", 1, gateName: "foreground") };
        Assert.Null(Evidence.Establish(Scenario("working"), events, false, Start.AddSeconds(3)));
    }

    [Fact]
    public void Foreground_native_tool_and_live_gate_establish_work_independently_of_screen()
    {
        var events = new[] { Event("PreToolUse", 1, "Bash", gate: true), Event("GateStart", 2, gateName: "foreground") };
        Assert.Equal(PaneState.Working, Evidence.Establish(Scenario("working"), events, false, Start.AddSeconds(4))!.State);
        Assert.Null(Evidence.Establish(Scenario("working"), events.Append(Event("PostToolUse", 3, "Bash")).ToArray(), false, Start.AddSeconds(4)));
    }

    [Fact]
    public void Native_question_intent_is_not_proof_of_visible_question()
    {
        var events = new[] { Event("PreToolUse", 1, "AskUserQuestion") };
        Assert.Null(Evidence.Establish(Scenario("question-choice"), events, false, Start.AddSeconds(5)));
    }

    [Fact]
    public void Copilot_dialog_notification_requires_a_pending_tool_and_expires_on_completion()
    {
        var tool = Event("PreToolUse", 1, "ask_user");
        var dialog = Event("Notification", 2) with { Notification = "elicitation_dialog" };
        Assert.Null(Evidence.Establish(Scenario("question-choice"), [dialog], false, Start.AddSeconds(4)));
        Assert.Equal(PaneState.Waiting, Evidence.Establish(Scenario("question-choice"), [tool, dialog], false, Start.AddSeconds(4))!.State);
        Assert.Null(Evidence.Establish(Scenario("question-choice"), [tool, dialog, Event("PostToolUse", 3, "ask_user")], false, Start.AddSeconds(4)));
        Assert.Null(Evidence.Establish(Scenario("question-choice"), [tool, Event("PostToolUse", 2, "ask_user"), dialog with { At = Start.AddSeconds(3) }], false, Start.AddSeconds(4)));
    }

    [Fact]
    public void Question_variants_require_shape_evidence_not_just_waiting()
    {
        var choice = new EvidenceEvent { Event = "PreToolUse", Tool = "AskUserQuestion", QuestionCount = 1, OptionCounts = [2] };
        Assert.True(Scenario("question-choice").TargetEstablished([choice], false));
        Assert.False(Scenario("question-multiple").TargetEstablished([choice], false));
        Assert.False(Scenario("question-freeform").TargetEstablished([choice], false));
        Assert.True(Scenario("question-freeform").TargetEstablished([choice], true));
        Assert.True(Scenario("question-multiple").TargetEstablished([choice with { QuestionCount = 2, OptionCounts = [2, 2] }], false));
        Assert.False(Scenario("approval-edit").TargetEstablished([choice with { Tool = "Read" }], false));
    }

    [Fact]
    public void A_shell_named_monitor_does_not_establish_native_monitor_coverage()
    {
        var shell = Event("PreToolUse", 1, "bash", gate: true, background: true);
        Assert.False(Scenario("background-monitor").TargetEstablished([shell], false));
        Assert.True(Scenario("background-shell").TargetEstablished([shell], false));
        Assert.True(Scenario("background-monitor").TargetEstablished([shell with { Tool = "Monitor" }], false));
    }

    [Fact]
    public void Truncated_trust_dialog_is_recognized_but_normal_prose_is_not()
    {
        Assert.True(LiveRun.KnownTrustDialog("codex", "> You are in /truncated/path\nDo you trust the contents of this directory?\n1. Yes, continue"));
        Assert.False(LiveRun.KnownTrustDialog("codex", "A document says trust this folder."));
        Assert.False(LiveRun.KnownTrustDialog("claude", "Do you want to proceed?\n1. Yes"));
        Assert.True(LiveRun.KnownTrustDialog("copilot", "Confirm folder trust\nDo you trust the files in this folder?\n1. Yes"));
        Assert.False(LiveRun.KnownTrustDialog("copilot", "Approve shell command?\n1. Yes"));
    }

    [Fact]
    public void A_started_gate_clears_a_pending_permission_even_before_tool_completion()
    {
        var events = new[] { Event("PreToolUse", 1, "Bash", gate: true), Event("PermissionRequest", 2, "Bash"), Event("GateStart", 3, gateName: "foreground") };
        Assert.Equal(PaneState.Working, Evidence.Establish(Scenario("working"), events, false, Start.AddSeconds(5))!.State);
    }

    [Fact]
    public void Pending_permission_is_waiting_and_completion_clears_it()
    {
        var events = new[] { Event("PermissionRequest", 1, "Bash") };
        Assert.Equal(PaneState.Waiting, Evidence.Establish(Scenario("approval-command"), events, false, Start.AddSeconds(3))!.State);
        Assert.Null(Evidence.Establish(Scenario("approval-command"), events.Append(Event("PostToolUse", 2, "Bash")).ToArray(), false, Start.AddSeconds(3)));
    }

    [Fact]
    public void Child_stop_does_not_establish_parent_idle()
    {
        Assert.Null(Evidence.Establish(Scenario("idle"), [Event("Stop", 1, child: "child")], false, Start.AddSeconds(2)));
    }

    [Fact]
    public void Mixed_background_requires_both_live_gates_and_native_background_launch()
    {
        var events = new[] { Event("PreToolUse", 1, "Agent", background: true),
            Event("GateStart", 2, gateName: "agent"), Event("Stop", 3) };
        Assert.Null(Evidence.Establish(Scenario("background-both"), events, false, Start.AddSeconds(6)));
        var both = events.Append(Event("PreToolUse", 3, "Bash", gate: true, background: true)).Append(Event("GateStart", 4, gateName: "shell")).ToArray();
        var evidence = Evidence.Establish(Scenario("background-both"), both, false, Start.AddSeconds(6));
        Assert.Equal(BackgndReason.BackgroundTask | BackgndReason.BackgroundAgent, evidence!.Reason);
    }

    [Fact]
    public void Background_shell_does_not_establish_that_an_agent_is_detached()
    {
        var events = new[] { Event("PreToolUse", 1, "Bash", gate: true, background: true),
            Event("GateStart", 2, gateName: "shell"), Event("PreToolUse", 3, "Agent"),
            Event("GateStart", 4, gateName: "agent"), Event("Stop", 5) };
        Assert.Null(Evidence.Establish(Scenario("background-both"), events, false, Start.AddSeconds(7)));
    }

    [Fact]
    public void Confirmation_expires_and_is_invalidated_by_new_events()
    {
        var confirmation = new Confirmation(PaneState.Waiting, BackgndReason.None, Start, "reviewed");
        Assert.Equal(PaneState.Waiting, Evidence.Establish(Scenario("question-choice"), [], false, Start.AddSeconds(2), confirmation)!.State);
        Assert.Null(Evidence.Establish(Scenario("question-choice"), [], false, Start.AddSeconds(16), confirmation));
        Assert.Null(Evidence.Establish(Scenario("question-choice"), [Event("PostToolUse", 1)], false, Start.AddSeconds(2), confirmation));
    }

    [Fact]
    public void An_unknown_capture_cannot_pass_without_evidence_and_fails_when_work_is_established()
    {
        Sample Sample(int i, EstablishedState? evidence) => new(Start.AddSeconds(i + 3), "sample.txt", PaneState.Unknown,
            BackgndReason.None, evidence, PaneState.Unknown, [], "Normal");
        var unknown = Enumerable.Range(0, 8).Select(i => Sample(i, null)).ToList();
        Assert.Equal(Outcome.Inconclusive, Evaluation.Assess("work", unknown, PaneState.Working, BackgndReason.None, true, 3).Outcome);
        var evidence = new EstablishedState(PaneState.Working, BackgndReason.None, Start, "gate");
        var failed = Enumerable.Range(0, 8).Select(i => Sample(i, evidence)).ToList();
        Assert.Equal(Outcome.Mismatch, Evaluation.Assess("work", failed, PaneState.Working, BackgndReason.None, true, 3).Outcome);
    }

    [Fact]
    public void Missing_or_unsupported_coverage_and_failed_prior_attempts_never_return_full_success()
    {
        Assert.Equal(2, Evaluation.ExitCode([]));
        Assert.Equal(2, Evaluation.ExitCode([new("a", Outcome.Pass, ""), new("b", Outcome.Unsupported, "")]));
        Assert.Equal(1, Evaluation.ExitCode([new("attempt1", Outcome.Mismatch, ""), new("attempt2", Outcome.Pass, "")]));
    }

    [Fact]
    public void A_partial_report_cannot_claim_success_and_names_every_attempt()
    {
        var report = new Report { Root = "/unused" };
        var attempt = new Attempt { Agent = "claude", Scenario = "working", Width = 70, Number = 2, Directory = "/unused" };
        attempt.Checks.Add(new("target", Outcome.Pass, ""));
        report.Attempts.Add(attempt);
        Assert.Equal(2, report.ExitCode);
        Assert.Equal("claude/working/70/attempt-2/target", report.AllChecks().Single().Name);
        report.Finished = Start;
        Assert.Equal(0, report.ExitCode);
    }

    [Fact]
    public async Task Driver_rejects_foreign_panes_before_any_tmux_call()
    {
        await using var driver = new OwnedTmux();
        await using var second = new OwnedTmux();
        Assert.NotEqual(driver.Socket, second.Socket);
        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.SendText("%0", "anything", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.Close("%0"));
    }

    [Fact]
    public async Task Process_arguments_preserve_shell_metacharacters_as_data()
    {
        if (OperatingSystem.IsWindows()) return;
        const string value = "quotes ' and \" $HOME $(echo unintended) `echo unintended`\nnext line";
        var result = await Processes.Run("bash", ["-c", "printf '%s' " + Processes.Quote(value)]);
        Assert.Equal(value, result.Output);
    }

    [Fact]
    public async Task Cancellation_terminates_a_running_process_tree()
    {
        if (OperatingSystem.IsWindows()) return;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processes.Run("bash", ["-c", "sleep 30"], cancel.Token));
    }

    [Fact]
    public async Task Private_tmux_launcher_accepts_input_and_retains_dead_pane()
    {
        if (OperatingSystem.IsWindows() || Processes.Find("tmux") is null || Processes.Find("timeout") is null) return;
        var root = Path.Combine(Path.GetTempPath(), "tw-terminal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // This uses the real launch builder with a local fake CLI. No model or
            // authentication is involved, but terminal job control is real.
            var fake = Path.Combine(root, "fake-agent");
            File.WriteAllText(fake, "#!/bin/bash\nprintf 'READY\\n'\nread -r reply\nprintf 'RECEIVED:%s\\n' \"$reply\"\n");
            File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var workspace = Path.Combine(root, "project");
            var launcher = await AgentAdapter.Prepare("claude", fake, Scenario("idle"), workspace, null, 10, CancellationToken.None);
            await using var driver = new OwnedTmux();
            await driver.Start(15, CancellationToken.None);
            var pane = await driver.Launch("test", workspace, launcher, 80, 24, CancellationToken.None);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!(await driver.Capture(pane, deadline.Token)).Text.Contains("READY")) await Task.Delay(50, deadline.Token);
            await driver.SendText(pane, "literal $HOME", deadline.Token);
            (string Text, bool Dead) capture;
            do { await Task.Delay(50, deadline.Token); capture = await driver.Capture(pane, deadline.Token); } while (!capture.Dead);
            Assert.Contains("RECEIVED:literal $HOME", capture.Text);
            await driver.Close(pane);
            Assert.Throws<InvalidOperationException>(() => driver.RequireOwned(pane));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Hook_evidence_drops_sensitive_payload_and_gate_has_a_lifetime_limit()
    {
        if (OperatingSystem.IsWindows() || Processes.Find("python3") is null) return;
        var root = Path.Combine(Path.GetTempPath(), "tw-helper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var helper = Path.Combine(root, "calibration-helper.py");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "helper.py"), helper);
            var input = Path.Combine(root, "hook-input.json");
            File.WriteAllText(input, JsonSerializer.Serialize(new { tool_name = "Bash", tool_input = new { command = "secret-value" }, transcript_path = "/private/transcript" }));
            var result = await Processes.Run("bash", ["-c", $"python3 {Processes.Quote(helper)} hook PreToolUse working claude < {Processes.Quote(input)}"]);
            Assert.True(result.Ok, result.Error);
            var evidence = string.Join("\n", Directory.GetFiles(Path.Combine(root, "evidence")).Select(File.ReadAllText));
            Assert.DoesNotContain("secret-value", evidence);
            Assert.DoesNotContain("/private/transcript", evidence);
            File.WriteAllText(input, JsonSerializer.Serialize(new { toolName = "ask_user", toolArgs = new {
                requestedSchema = new { properties = new {
                    color = new { @enum = new[] { "private-choice-one", "private-choice-two" } },
                    label = new { type = "string", description = "private-description" }
                } }
            } }));
            var question = await Processes.Run("bash", ["-c", $"python3 {Processes.Quote(helper)} hook PreToolUse question-multiple copilot < {Processes.Quote(input)}"]);
            Assert.True(question.Ok, question.Error);
            var shape = Evidence.Read(root).Last();
            Assert.Equal(2, shape.QuestionCount);
            Assert.Equal(new[] { 2, 0 }, shape.OptionCounts);
            Assert.DoesNotContain("private-", string.Join("\n", Directory.GetFiles(Path.Combine(root, "evidence")).Select(File.ReadAllText)));
            File.WriteAllText(input, JsonSerializer.Serialize(new { tool_name = "request_user_input", tool_use_id = "private-call",
                session_id = "private-session", tool_response = new { answers = new { color = new { answers = new[] { "Default (Recommended)", "user_note: synthetic" } } } } }));
            var answered = await Processes.Run("bash", ["-c", $"python3 {Processes.Quote(helper)} hook PostToolUse question-choice codex < {Processes.Quote(input)}"]);
            Assert.True(answered.Ok, answered.Error);
            var receipt = Evidence.Read(root).Last();
            Assert.True(receipt.AnswerReceived);
            Assert.NotEqual("private-call", receipt.Call);
            Assert.NotEqual("private-session", receipt.Session);
            File.WriteAllText(input, JsonSerializer.Serialize(new { tool_name = "Agent", tool_response = new {
                isAsync = true, status = "async_launched", agentId = "private-child", prompt = "private-prompt"
            } }));
            var launched = await Processes.Run("bash", ["-c", $"python3 {Processes.Quote(helper)} hook PostToolUse background-agent claude < {Processes.Quote(input)}"]);
            Assert.True(launched.Ok, launched.Error);
            Assert.True(Evidence.Read(root).Last().Background);
            Assert.DoesNotContain("private-", string.Join("\n", Directory.GetFiles(Path.Combine(root, "evidence")).Select(File.ReadAllText)));
            await Processes.Run("python3", [helper, "gate", "bounded", "1"]);
            var events = Evidence.Read(root);
            Assert.Contains(events, e => e.Event == "GateStart");
            Assert.Contains(events, e => e.Event == "GateEnd");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Replay_checks_production_transitions_for_every_profile()
    {
        var results = MonitorReplay.Run(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        Assert.True(results.Count >= 13);
        Assert.All(results, r => Assert.Equal(Outcome.Pass, r.Outcome));
    }

    [Theory]
    [InlineData("--widths", "0")]
    [InlineData("--sample-ms", "0")]
    [InlineData("--retries", "-1")]
    [InlineData("--agents", "unknown")]
    public void Invalid_run_limits_are_rejected(string option, string value) =>
        Assert.Throws<ArgumentException>(() => Options.Parse(["run", option, value]));

    [Fact]
    public void Reviewed_candidate_preserves_exact_bytes_and_cannot_overwrite_a_fixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "tw-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "report.json"), "{}");
            var source = Path.Combine(root, "reviewed.txt");
            File.WriteAllText(source, "\u203a Synthetic\r\n\u2191/\u2193\n");
            var candidate = Report.Export(root, source, "question");
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(candidate));
            Assert.Throws<ArgumentException>(() => Report.Export(root, source, "../../fixtures/idle"));
            Assert.Throws<IOException>(() => Report.Export(root, source, "question"));
        }
        finally { Directory.Delete(root, true); }
    }
}
