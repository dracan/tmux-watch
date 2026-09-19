using System.Text.Json;
using TmuxWatch.Calibration;
using TmuxWatch.Config;
using TmuxWatch.Detection;
using TmuxWatch.Notifications;
using TmuxWatch.Pointer;

try
{
    var options = Options.Parse(args);
    using var cancel = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
    if (options.PhysicalNotifications)
    {
        var pointer = PointerSignalFactory.Create(new WatchConfig());
        try
        {
            pointer.Restore();
            new BellNotifier().Notify("Calibration", "Physical notification smoke test");
            pointer.SetState(PointerState.Waiting);
            await Task.Delay(1500, cancel.Token);
            pointer.SetState(PointerState.Done);
            await Task.Delay(1500, cancel.Token);
        }
        finally { pointer.Restore(); }
    }
    switch (options.Command)
    {
        case "run": return await new LiveRun(options).Run(cancel.Token);
        case "preflight":
            var available = true;
            foreach (var agent in options.Agents)
            {
                var status = await AgentAdapter.Check(agent, cancel.Token);
                available &= status.Available;
                Console.WriteLine(JsonSerializer.Serialize(status, Report.Json));
            }
            return available ? 0 : 2;
        case "replay":
            var checks = MonitorReplay.Run(Path.Combine(AppContext.BaseDirectory, "fixtures"));
            foreach (var check in checks) Console.WriteLine($"{check.Name}: {check.Outcome} - {check.Detail}");
            return Evaluation.ExitCode(checks);
        case "list":
            foreach (var s in Scenario.All) Console.WriteLine($"{s.Id,-22} {s.Target,-8} {s.Reason}");
            return 0;
        case "confirm":
            if (options.Positionals.Length is < 2 or > 3) throw new ArgumentException("confirm WORKSPACE STATE [BACKGROUND_REASON]");
            LiveRun.WriteConfirmation(Path.GetFullPath(options.Positionals[0]), Enum.Parse<PaneState>(options.Positionals[1], true),
                options.Positionals.Length == 3 ? Enum.Parse<BackgndReason>(options.Positionals[2], true) : BackgndReason.None, "explicit operator confirmation");
            return 0;
        case "export":
            if (options.Positionals.Length != 3) throw new ArgumentException("export RUN_DIRECTORY REVIEWED_SCRUBBED_CAPTURE NAME");
            Console.WriteLine(Report.Export(options.Positionals[0], options.Positionals[1], options.Positionals[2]));
            return 0;
        case "help": case "--help": case "-h":
            Console.WriteLine("""
                Usage: ./calibrate.sh COMMAND [OPTIONS]
                Commands: preflight, list, run, replay, confirm, export
                run launches real agents and consumes model usage. No agents run by default.
                  --agents copilot,claude,codex     Select installed agents
                  --scenarios idle,working,...     Default: every scenario (see list)
                  --widths 120,70 --height 40      Test layouts
                  --scenario-seconds 90            Limit each attempt
                  --deadline-seconds 1800          Limit the entire run
                  --sample-ms 500 --stable-samples 6
                  --retries 1                      Keep every attempt in the report
                  --model agent=model              Optional per-agent model override
                  --unattended                     Skip operator confirmation/login assistance
                  --retain-failures                Offer bounded session retention (interactive)
                  --output .calibration-runs        Private run artifact directory
                  --physical-notifications         Optional bell/pointer smoke test
                Exit: 0 complete success; 1 mismatch; 2 incomplete/unavailable/unsupported.
                confirm WORKSPACE STATE [BACKGROUND_REASON]: label the current screen for 15s.
                export RUN_DIRECTORY REVIEWED_SCRUBBED_CAPTURE NAME: create a fixture candidate.
                """);
            return 0;
        default: throw new ArgumentException("Unknown command: " + options.Command);
    }
}
catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled"); return 2; }
catch (Exception e) { Console.Error.WriteLine(e.Message); return 2; }
