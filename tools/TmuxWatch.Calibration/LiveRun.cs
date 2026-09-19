using System.Text.Json;
using TmuxWatch.Detection;

namespace TmuxWatch.Calibration;

public sealed class LiveRun(Options options)
{
    private readonly HashSet<string> reached = new();

    public async Task<int> Run(CancellationToken cancellation)
    {
        var scenarios = options.Scenarios.Length == 0 ? Scenario.All : options.Scenarios.Select(id =>
            Scenario.All.SingleOrDefault(s => s.Id == id) ?? throw new ArgumentException("Unknown scenario: " + id)).ToArray();
        var root = Report.PrivateDirectory(Path.Combine(options.Output, DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]));
        var report = new Report { Root = root, Mode = options.Command == "check" ? "supported automated checks; exclusions listed separately" : "full diagnostic catalog" };
        report.Replay.AddRange(MonitorReplay.Run(Path.Combine(AppContext.BaseDirectory, "fixtures")));
        report.Save();
        Console.WriteLine("Run evidence: " + root);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.DeadlineSeconds));
        var ct = deadline.Token;
        await using var tmux = new OwnedTmux();
        report.Socket = tmux.Socket;
        try
        {
            foreach (var required in new[] { "tmux", "python3", "timeout", "git", "bash" })
                if (Processes.Find(required) is null) throw new InvalidOperationException("Required executable missing: " + required);
            await tmux.Start(options.DeadlineSeconds + 300, ct);
            foreach (var id in options.Agents.Distinct())
            {
                var preflight = await AgentAdapter.Check(id, ct);
                if (!preflight.Available && preflight.Executable is not null && !options.Unattended)
                {
                    Console.WriteLine($"{id}: {preflight.Authentication}. Authenticate in another terminal, then press Enter to retry; enter skip to continue.");
                    if (await ReadLine(ct) != "skip") preflight = await AgentAdapter.Check(id, ct);
                }
                report.Agents.Add(preflight);
                Console.WriteLine($"{id}: {preflight.Version}; {preflight.Authentication}");
                if (!preflight.Available)
                {
                    foreach (var s in scenarios)
                        foreach (var width in options.Widths)
                        {
                            var key = Key(id, s.Id, width);
                            report.Coverage.Add(new(key, Outcome.Unavailable, preflight.Authentication));
                            reached.Add(key);
                        }
                    report.Save();
                    continue;
                }
                foreach (var scenario in scenarios.Select(s => s.ForAgent(id)))
                    foreach (var width in options.Widths.Distinct())
                    {
                        if (options.Command == "check" && scenario.CheckExclusion(id) is { } excluded)
                        {
                            report.Exclusions.Add(new(Key(id, scenario.Id, width), Outcome.Unsupported, excluded));
                            reached.Add(Key(id, scenario.Id, width));
                            Console.WriteLine($"Excluded {id}/{scenario.Id}/{width}: {excluded}");
                            report.Save();
                            continue;
                        }
                        for (var retry = 0; retry <= options.Retries; retry++)
                        {
                            ct.ThrowIfCancellationRequested();
                            var attempt = new Attempt
                            {
                                Agent = id, Scenario = scenario.Id, Width = width, Height = options.Height, Number = retry + 1,
                                Directory = Report.PrivateDirectory(Path.Combine(root, $"{id}-{scenario.Id}-{width}-{retry + 1}")),
                            };
                            report.Attempts.Add(attempt);
                            Console.WriteLine($"Running {id}/{scenario.Id} at {width}x{options.Height}, attempt {retry + 1}");
                            try
                            {
                                using var attemptDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                                attemptDeadline.CancelAfter(TimeSpan.FromSeconds(options.ScenarioSeconds));
                                await RunAttempt(tmux, preflight, scenario, attempt, report, attemptDeadline.Token);
                            }
                            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                            {
                                attempt.Checks.Add(new("attempt", Outcome.Inconclusive, "Scenario deadline expired"));
                            }
                            catch (Exception e) when (e is not OperationCanceledException)
                            {
                                attempt.Checks.Add(new("attempt", Outcome.Inconclusive, "Adapter error: " + e.GetType().Name));
                                Console.Error.WriteLine("  " + e.Message);
                            }
                            reached.Add(Key(id, scenario.Id, width));
                            report.Save();
                            foreach (var result in attempt.Checks) Console.WriteLine($"  {result.Name}: {result.Outcome} - {result.Detail}");
                            if (attempt.Checks.All(c => c.Outcome is Outcome.Pass or Outcome.Unsupported)) break;
                        }
                    }
            }
        }
        catch (OperationCanceledException) { report.Coverage.Add(new("run", Outcome.Inconclusive, "Run cancelled or overall deadline expired")); }
        catch (Exception e)
        {
            report.Coverage.Add(new("run", Outcome.Inconclusive, "Harness error: " + e.GetType().Name));
            Console.Error.WriteLine(e.Message);
        }
        finally
        {
            try { await tmux.DisposeAsync(); }
            catch (Exception e) { report.Coverage.Add(new("cleanup", Outcome.Inconclusive, "Private server cleanup failed: " + e.GetType().Name)); }
            foreach (var id in options.Agents.Distinct())
                foreach (var s in scenarios)
                    foreach (var width in options.Widths.Distinct())
                        if (!reached.Contains(Key(id, s.Id, width)))
                            report.Coverage.Add(new(Key(id, s.Id, width), Outcome.Inconclusive, "Not completed before run ended"));
            report.Finished = DateTimeOffset.UtcNow;
            report.Save();
            Console.WriteLine("Report: " + Path.Combine(root, "report.md"));
        }
        return report.ExitCode;
    }

    private async Task RunAttempt(OwnedTmux tmux, Preflight agent, Scenario scenario, Attempt attempt, Report report, CancellationToken ct)
    {
        var workspace = Path.Combine(attempt.Directory, "project");
        var session = $"{agent.Agent}-{report.Attempts.Count}";
        var launcher = await AgentAdapter.Prepare(agent.Agent, agent.Executable!, scenario, workspace,
            options.Models.GetValueOrDefault(agent.Agent), options.ScenarioSeconds + 60, ct);
        var pane = await tmux.Launch(session, workspace, launcher, attempt.Width, attempt.Height, ct);
        attempt.Attach = $"tmux -L {tmux.Socket} attach -t {session}";
        Console.WriteLine("  Inspect: " + attempt.Attach);
        Console.WriteLine("  Confirm uncertain state: ./calibrate.sh confirm " + Processes.Quote(workspace) + " " + scenario.Target + " " + scenario.Reason.ToString().Replace(", ", ","));
        var classifier = new PaneClassifier(AgentAdapter.Profile(agent.Agent));
        var probe = new MonitorProbe(agent.Agent);
        var began = DateTimeOffset.UtcNow;
        DateTimeOffset? targetCaptured = null;
        var recoveryStart = -1;
        var confirmed = false;
        var startupAttempts = 0;
        var freeformEditor = false;
        DateTimeOffset? lastAnswer = null;
        var answerCount = 0;
        string? answeredQuestionCall = null;
        DateTimeOffset? answeredQuestionAt = null;
        EvidenceEvent? receiptQuestion = null;
        DateTimeOffset? receiptSent = null;
        DateTimeOffset? editorOpened = null;
        var success = false;
        try
        {
            while (DateTimeOffset.UtcNow - began < TimeSpan.FromSeconds(options.ScenarioSeconds))
            {
                ct.ThrowIfCancellationRequested();
                var (text, dead) = await tmux.Capture(pane, ct);
                var now = DateTimeOffset.UtcNow;
                var events = Evidence.Read(workspace);
                // Trust handling is restricted to the disposable path and the known
                // workspace dialog. Never approve a generic tool permission here.
                if (startupAttempts < 3 && events.Count == 0 && KnownTrustDialog(agent.Agent, text) &&
                    await tmux.CurrentPath(pane, ct) == workspace)
                {
                    await Task.Delay(500, ct);
                    // Claude defaults to "No, exit" in this verified dialog.
                    if (text.Contains("\u276f No, exit", StringComparison.Ordinal))
                    {
                        await tmux.SendKey(pane, "Down", ct);
                        await Task.Delay(200, ct);
                    }
                    await tmux.SendKey(pane, "Enter", ct);
                    startupAttempts++;
                    attempt.Actions.Add(new(now, "Accepted known workspace trust dialog after checking owned pane cwd"));
                    await Task.Delay(500, ct);
                    continue;
                }
                // Login screens can include device codes. Do not persist them.
                if (LooksLikeLogin(text))
                {
                    if (options.Unattended)
                    {
                        attempt.Checks.Add(new("startup", Outcome.Unavailable, "Interactive login required; screen not saved"));
                        return;
                    }
                    Console.WriteLine("Complete login via the attach command above, detach with Ctrl-b d, then press Enter here.");
                    await ReadLine(ct);
                    continue;
                }
                var confirmationPath = Path.Combine(workspace, "confirmation.json");
                var confirmation = File.Exists(confirmationPath)
                    ? JsonSerializer.Deserialize<Confirmation>(File.ReadAllText(confirmationPath), Report.Json) : null;
                var expected = Evidence.Establish(scenario, events, dead, now, confirmation);
                var questionCall = events.LastOrDefault(e => e.Event == "PreToolUse" && Evidence.IsQuestion(e.Tool));
                if (scenario.Id == "question-freeform" && !freeformEditor && questionCall?.OptionCounts is [> 0] && QuestionReceipt.IsPending(questionCall, events) &&
                    ((expected?.State == PaneState.Waiting && now - expected.Since > TimeSpan.FromSeconds(1)) ||
                     (agent.Agent == "codex" && now - questionCall.At > TimeSpan.FromSeconds(2))))
                {
                    // Exercise the native editor, not just a menu whose question
                    // happens to request free text. These actions are adapter-specific.
                    if (agent.Agent == "claude")
                    {
                        for (var i = 0; i < questionCall.OptionCounts[0]; i++) await tmux.SendKey(pane, "Down", ct);
                        await tmux.SendKey(pane, "Enter", ct);
                        freeformEditor = true;
                    }
                    else if (agent.Agent == "codex")
                    {
                        await tmux.SendKey(pane, "Tab", ct);
                        freeformEditor = true;
                    }
                    if (freeformEditor)
                    {
                        editorOpened = now;
                        attempt.Actions.Add(new(now, "Selected native free-text editor for the pending question"));
                        await Task.Delay(500, ct);
                        continue;
                    }
                }
                if (expected?.State == scenario.Target && !expected.Source.StartsWith("operator:", StringComparison.Ordinal) &&
                    !scenario.TargetEstablished(events, freeformEditor)) expected = null;
                var actual = classifier.Inspect(text, dead);
                var monitor = probe.Tick(text, dead);
                var captureFile = Path.Combine(attempt.Directory, $"capture-{attempt.Samples.Count:D5}.txt");
                File.WriteAllText(captureFile, text);
                attempt.Samples.Add(new(now, captureFile, actual.State, actual.Reason, expected,
                    monitor.Panes.Single().State, monitor.Events.Select(e => e.Kind.ToString()).ToArray(), probe.Pointer.ToString()));
                if (attempt.Samples.Count % 10 == 0) report.Save();

                if (scenario.Id == "dead" && expected?.State == PaneState.Idle)
                {
                    // Native exit key; the owned launcher leaves a dead pane for capture.
                    await tmux.SendText(pane, "/exit", ct);
                    attempt.Actions.Add(new(now, "Requested native /exit after confirmed idle"));
                }
                if (receiptQuestion is not null && receiptSent is not null &&
                    !attempt.Checks.Any(c => c.Name == "target") &&
                    QuestionReceipt.Establish(attempt.Samples, receiptQuestion, editorOpened ?? receiptQuestion.At, receiptSent.Value, events))
                    attempt.Checks.Add(Evaluation.Assess("target", attempt.Samples, scenario.Target, scenario.Reason, true, options.StableSamples));
                var canVerifyAnswer = agent.Agent == "codex" && scenario.Id.StartsWith("question-") &&
                    questionCall is not null && questionCall.Call.Length > 0 && scenario.TargetEstablished(events, freeformEditor) &&
                    QuestionReceipt.IsPending(questionCall, events) &&
                    attempt.Samples.Count(s => s.At - (editorOpened ?? questionCall.At) > TimeSpan.FromSeconds(1)) >= options.StableSamples * 2;
                var targetSamples = attempt.Samples.Count(s => s.Expected?.State == scenario.Target &&
                    s.Expected.Reason == scenario.Reason && s.At - s.Expected.Since >= TimeSpan.FromSeconds(1));
                if (targetCaptured is null && (targetSamples >= options.StableSamples * 2 || canVerifyAnswer))
                {
                    targetCaptured = now;
                    var supported = scenario.Supported(agent.Agent);
                    if (targetSamples >= options.StableSamples * 2)
                        attempt.Checks.Add(Evaluation.Assess("target", attempt.Samples, scenario.Target, scenario.Reason, supported, options.StableSamples));
                    else
                    {
                        receiptQuestion = questionCall;
                        receiptSent = now;
                        attempt.Actions.Add(new(now, "Submitting a synthetic answer to verify the captured native request"));
                    }
                    if (scenario.Target is PaneState.Idle or PaneState.Dead) break;
                    // Release only after the independently established target was captured.
                    File.WriteAllText(Path.Combine(workspace, "release-all"), "release\n");
                    attempt.Actions.Add(new(now, "Released controlled helper gates"));
                    // Complete the synthetic native request so the agent emits its
                    // normal tool-completion and Stop lifecycle events. An interrupt
                    // does not emit Stop on every CLI version.
                    if (scenario.Target == PaneState.Waiting)
                    {
                        if (scenario.Id == "question-freeform") await tmux.SendText(pane, "synthetic", ct);
                        else await tmux.SendKey(pane, "Enter", ct);
                        lastAnswer = now;
                        answerCount = 1;
                        answeredQuestionCall = questionCall?.Call;
                        answeredQuestionAt = questionCall?.At;
                        attempt.Actions.Add(new(now, "Answered the synthetic native request"));
                    }
                    recoveryStart = attempt.Samples.Count;
                }
                if (recoveryStart >= 0)
                {
                    // Multiple-question tools can require one confirmation per
                    // field and a final submit. Continue only the same native call;
                    // never approve an unrelated operation during recovery.
                    if (scenario.Id.StartsWith("question-") && answerCount < 5 && (expected?.State == PaneState.Waiting || receiptQuestion is not null) &&
                        questionCall?.Call == answeredQuestionCall && questionCall is not null && questionCall.At == answeredQuestionAt &&
                        QuestionReceipt.IsPending(questionCall, events) &&
                        lastAnswer is not null && now - lastAnswer > TimeSpan.FromSeconds(2))
                    {
                        if (agent.Agent == "copilot" && scenario.Id == "question-multiple" && answerCount == 1)
                            await tmux.SendText(pane, "synthetic", ct);
                        else await tmux.SendKey(pane, "Enter", ct);
                        lastAnswer = now;
                        answerCount++;
                        attempt.Actions.Add(new(now, "Advanced the same pending multiple-field question"));
                    }
                    var recovery = attempt.Samples.Skip(recoveryStart).ToList();
                    if (recovery.Count(s => s.Expected?.State == PaneState.Idle && s.At - s.Expected.Since >= TimeSpan.FromSeconds(1)) >= options.StableSamples * 2)
                    {
                        attempt.Checks.Add(Evaluation.Assess("return-to-idle", recovery, PaneState.Idle, BackgndReason.None, true, options.StableSamples));
                        if (probe.Last!.Panes.Single().State == PaneState.Done)
                        {
                            var count = probe.Notifications;
                            var ack = probe.Acknowledge();
                            var after = probe.Tick(text);
                            var ok = ack && after.Panes.Single().State == PaneState.Idle && probe.Notifications == count && probe.Pointer.ToString() == "Normal";
                            attempt.Checks.Add(new("live-acknowledgement", ok ? Outcome.Pass : Outcome.Mismatch, "Acknowledged production monitor DONE and checked notification count/pointer"));
                        }
                        break;
                    }
                }
                if (!options.Unattended && !confirmed && targetCaptured is null && now - began > TimeSpan.FromSeconds(20) && expected?.State != scenario.Target)
                {
                    confirmed = true;
                    Console.WriteLine($"Inspect the owned pane. Is it currently {scenario.Target} for {scenario.Id}? Enter yes to confirm, or Enter to keep waiting.");
                    if (await ReadLine(ct) == "yes")
                        WriteConfirmation(workspace, scenario.Target, scenario.Reason, "interactive inspection");
                }
                if (dead && scenario.Target != PaneState.Dead) break;
                await Task.Delay(options.SampleMs, ct);
            }
            if (!attempt.Checks.Any(c => c.Name == "target"))
                attempt.Checks.Add(Evaluation.Assess("target", attempt.Samples, scenario.Target, scenario.Reason,
                    scenario.Supported(agent.Agent), options.StableSamples));
            else if (recoveryStart >= 0 && !attempt.Checks.Any(c => c.Name == "return-to-idle"))
                attempt.Checks.Add(new("return-to-idle", Outcome.Inconclusive, "No independently established settled return to IDLE"));
            success = attempt.Checks.All(c => c.Outcome == Outcome.Pass);
        }
        finally
        {
            File.WriteAllText(Path.Combine(workspace, "release-all"), "release\n");
            if (!success && options.RetainFailures && !ct.IsCancellationRequested)
            {
                Console.WriteLine("Retain this failed session for inspection until its lifetime limit? Type yes.");
                if (await ReadLine(ct) == "yes") tmux.Retain = true;
                else await tmux.Close(pane);
            }
            else await tmux.Close(pane);
        }
    }

    public static void WriteConfirmation(string workspace, PaneState state, BackgndReason reason, string note)
    {
        if (state is PaneState.Done || !Enum.IsDefined(state) ||
            (reason & ~(BackgndReason.BackgroundTask | BackgndReason.BackgroundAgent)) != 0 ||
            (state != PaneState.Backgnd && reason != BackgndReason.None))
            throw new ArgumentException("Confirm a classifier state and its applicable background reason, not monitor-derived DONE");
        if (!Directory.Exists(Path.Combine(workspace, "evidence")) || !File.Exists(Path.Combine(workspace, "launch-metadata.json")))
            throw new ArgumentException("Not a calibration workspace");
        var path = Path.Combine(workspace, "confirmation.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new Confirmation(state, reason, DateTimeOffset.UtcNow, note), Report.Json));
        File.Move(path + ".tmp", path, true);
    }

    public static bool LooksLikeLogin(string text) => text.Split('\n').Any(line =>
        line.Contains("device code", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Sign in to", StringComparison.OrdinalIgnoreCase) ||
        (line.Contains("/login", StringComparison.OrdinalIgnoreCase) &&
         !System.Text.RegularExpressions.Regex.IsMatch(line, @"Your login expires in [1-9]\d* days?", System.Text.RegularExpressions.RegexOptions.IgnoreCase)));

    public static bool KnownTrustDialog(string agent, string text) => agent switch
    {
        "claude" => text.Contains("Accessing workspace:", StringComparison.Ordinal) && text.Contains("Yes, I trust this folder", StringComparison.Ordinal),
        "codex" => text.Contains("Do you trust the contents of this directory?", StringComparison.Ordinal) && text.Contains("1. Yes, continue", StringComparison.Ordinal),
        "copilot" => text.Contains("Confirm folder trust", StringComparison.Ordinal) &&
            text.Contains("Do you trust the files in this folder?", StringComparison.Ordinal) &&
            text.Contains("1. Yes", StringComparison.Ordinal),
        _ => false,
    };

    private static string Key(string agent, string scenario, int width) => $"{agent}/{scenario}/{width}";
    private static Task<string?>? pendingInput;

    private static async Task<string> ReadLine(CancellationToken ct)
    {
        // Console.In can block synchronously and ignore ReadLineAsync cancellation.
        // Reuse one pending read so a timed-out prompt cannot steal the next reply.
        pendingInput ??= Task.Run(Console.ReadLine);
        try { return (await pendingInput.WaitAsync(ct) ?? "skip").Trim().ToLowerInvariant(); }
        finally { if (pendingInput.IsCompleted) pendingInput = null; }
    }
}
