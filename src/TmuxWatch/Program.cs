using Spectre.Console;
using TmuxWatch.Config;
using TmuxWatch.Detection;
using TmuxWatch.Discovery;
using TmuxWatch.Monitor;
using TmuxWatch.Notifications;
using TmuxWatch.Pointer;
using TmuxWatch.Tmux;
using TmuxWatch.Tui;

var options = CliOptions.Parse(args);
if (options.ShowHelp)
{
    CliOptions.PrintHelp();
    return 0;
}

var cfg = WatchConfig.Load(options.ConfigPath);
if (options.IntervalSeconds is { } iv) cfg.PollIntervalSeconds = iv;

var tmux = new TmuxRunner(cfg.TmuxExecutable);
var discovery = new PaneDiscovery(tmux, cfg);

if (options.Calibrate)
    return Calibrate(tmux, discovery, cfg);

var notifier = NotifierFactory.Create(cfg);
var pointer = PointerSignalFactory.Create(cfg);

// Crash-safety: unconditionally restore the normal pointer at startup, before the
// first tick, so a pointer left red by a prior abnormal exit self-heals on launch.
pointer.Restore();

var monitor = new AttentionMonitor(discovery, tmux, cfg, notifier);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// Restore on graceful shutdown so the desktop pointer never stays red after exit.
AppDomain.CurrentDomain.ProcessExit += (_, _) => pointer.Restore();

if (options.Once)
{
    var snap = monitor.Tick();
    PrintOnce(snap);
    return 0;
}

new WatcherApp(monitor, tmux, cfg, pointer).Run(cts.Token);
return 0;

static void PrintOnce(MonitorSnapshot snap)
{
    if (snap.Error is not null)
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(snap.Error)}[/]");
    // Same attention-first ordering as the live tables. Sorting by the enum's own value
    // would order by declaration, which is grouped for readability rather than urgency.
    foreach (var p in snap.Panes.OrderBy(p => WatcherApp.Priority(p.State)))
        AnsiConsole.MarkupLine($"{(p.Pane.IsFocused ? "[green]►[/]" : " ")} {Markup.Escape(p.Pane.Location)}\t{Markup.Escape(p.Pane.AgentId)}\t{Markup.Escape(p.Pane.DisplayName)}\t{p.State}\t{Markup.Escape(p.Pane.Command)}");
    if (snap.Panes.Count == 0 && snap.Error is null)
        AnsiConsole.MarkupLine("[grey]No agent panes found.[/]");

    // The non-agent panes carry no classified state - they are listed by process so the
    // one-shot output covers everything the live view shows.
    foreach (var pane in snap.OtherPanes
                 .OrderBy(p => p.SessionName, StringComparer.Ordinal)
                 .ThenBy(p => p.WindowIndex)
                 .ThenBy(p => p.PaneIndex))
        AnsiConsole.MarkupLine($"{(pane.IsFocused ? "[green]►[/]" : " ")} {Markup.Escape(pane.Location)}\t[grey]-[/]\t{Markup.Escape(pane.DisplayName)}\t[grey]other[/]\t{Markup.Escape(pane.Command)}");
}

// Self-test: classify every live pane and show the status tail, so tokens can be
// re-derived after an agent CLI upgrade.
static int Calibrate(TmuxRunner tmux, PaneDiscovery discovery, WatchConfig cfg)
{
    var all = discovery.EnumerateAll();
    if (!all.Ok)
    {
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(all.Error ?? "tmux error")}[/]");
        return 1;
    }

    var classifiers = cfg.ResolveAgents()
        .ToDictionary(a => a.Id, a => new PaneClassifier(a, cfg.StatusLineCount));

    var table = new Table().Border(TableBorder.Rounded);
    table.AddColumn("Pane");
    table.AddColumn("Command");
    table.AddColumn("Agent");
    table.AddColumn("State");
    table.AddColumn("Status tail");

    foreach (var pane in all.Panes)
    {
        var profile = discovery.MatchProfile(pane);
        var cap = tmux.CapturePane(pane.Id);
        var state = profile is not null && classifiers.TryGetValue(profile.Id, out var classifier)
            ? classifier.Classify(cap.Ok ? cap.StdOut : null, pane.Dead)
            : PaneState.Dead;

        var tail = cap.Ok
            ? string.Join(" / ", cap.StdOut.Replace("\r\n", "\n").Split('\n')
                .Select(l => l.TrimEnd()).Where(l => l.Trim().Length > 0).TakeLast(1))
            : "(capture failed)";
        if (tail.Length > 60) tail = tail[..60] + "…";

        table.AddRow(
            Markup.Escape(pane.Location),
            Markup.Escape(pane.Command),
            profile is null ? "[grey]—[/]" : $"[green]{Markup.Escape(profile.Id)}[/]",
            Markup.Escape(state.ToString()),
            Markup.Escape(tail));
    }

    AnsiConsole.Write(table);
    return 0;
}

sealed class CliOptions
{
    public string? ConfigPath { get; private set; }
    public double? IntervalSeconds { get; private set; }
    public bool Calibrate { get; private set; }
    public bool Once { get; private set; }
    public bool ShowHelp { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--config": o.ConfigPath = Next(args, ref i); break;
                case "--interval":
                    if (double.TryParse(Next(args, ref i), out var s)) o.IntervalSeconds = s;
                    break;
                case "--calibrate": o.Calibrate = true; break;
                case "--once": o.Once = true; break;
                case "-h" or "--help": o.ShowHelp = true; break;
            }
        }
        return o;
    }

    private static string? Next(string[] args, ref int i) => i + 1 < args.Length ? args[++i] : null;

    public static void PrintHelp()
    {
        AnsiConsole.WriteLine("tmux-watch - surface which agent panes (Copilot, Claude Code) need attention");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Usage: tmux-watch [options]");
        AnsiConsole.WriteLine("  --config <path>     Load JSON config (agents, interval, notifications)");
        AnsiConsole.WriteLine("  --interval <sec>    Poll interval override");
        AnsiConsole.WriteLine("  --calibrate         Print classification of all live panes and exit");
        AnsiConsole.WriteLine("  --once              Print one classification snapshot and exit");
        AnsiConsole.WriteLine("  -h, --help          Show this help");
    }
}
