using Spectre.Console;
using TmuxWatch.Config;
using TmuxWatch.Detection;
using TmuxWatch.Discovery;
using TmuxWatch.Monitor;
using TmuxWatch.Notifications;
using TmuxWatch.Psmux;
using TmuxWatch.Tui;

var options = CliOptions.Parse(args);
if (options.ShowHelp)
{
    CliOptions.PrintHelp();
    return 0;
}

var cfg = WatchConfig.Load(options.ConfigPath);
if (options.IntervalSeconds is { } iv) cfg.PollIntervalSeconds = iv;
if (options.NotifyIdle) cfg.NotifyOnIdle = true;

var psmux = new PsmuxRunner(cfg.PsmuxExecutable);
var discovery = new PaneDiscovery(psmux, cfg);
var classifier = new PaneClassifier(cfg);

if (options.Calibrate)
    return Calibrate(psmux, discovery, classifier, cfg);

var notifier = NotifierFactory.Create(cfg);
var monitor = new AttentionMonitor(discovery, classifier, psmux, cfg, notifier);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

if (options.Once)
{
    var snap = monitor.Tick();
    PrintOnce(snap);
    return 0;
}

new WatcherApp(monitor, psmux, cfg).Run(cts.Token);
return 0;

static void PrintOnce(MonitorSnapshot snap)
{
    if (snap.Error is not null)
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(snap.Error)}[/]");
    foreach (var p in snap.Panes.OrderBy(p => p.State))
        AnsiConsole.MarkupLine($"{(p.Pane.IsFocused ? "[green]►[/]" : " ")} {Markup.Escape(p.Pane.Location)}\t{Markup.Escape(p.Pane.DisplayName)}\t{p.State}\t{Markup.Escape(p.Pane.Command)}");
    if (snap.Panes.Count == 0 && snap.Error is null)
        AnsiConsole.MarkupLine("[grey]No Copilot panes found.[/]");
}

// Self-test: classify every live pane and show the status tail, so tokens can be
// re-derived after a Copilot CLI upgrade.
static int Calibrate(PsmuxRunner psmux, PaneDiscovery discovery, PaneClassifier classifier, WatchConfig cfg)
{
    var all = discovery.EnumerateAll();
    if (!all.Ok)
    {
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(all.Error ?? "psmux error")}[/]");
        return 1;
    }

    var convention = cfg.CompileSessionConvention();
    var table = new Table().Border(TableBorder.Rounded);
    table.AddColumn("Pane");
    table.AddColumn("Command");
    table.AddColumn("Copilot?");
    table.AddColumn("State");
    table.AddColumn("Status tail");

    foreach (var pane in all.Panes)
    {
        var isCop = discovery.IsCopilot(pane, convention);
        var cap = psmux.CapturePane(pane.Id);
        var state = isCop
            ? classifier.Classify(cap.Ok ? cap.StdOut : null, true, pane.Dead)
            : PaneState.Dead;

        var tail = cap.Ok
            ? string.Join(" / ", cap.StdOut.Replace("\r\n", "\n").Split('\n')
                .Select(l => l.TrimEnd()).Where(l => l.Trim().Length > 0).TakeLast(1))
            : "(capture failed)";
        if (tail.Length > 60) tail = tail[..60] + "…";

        table.AddRow(
            Markup.Escape(pane.Location),
            Markup.Escape(pane.Command),
            isCop ? "[green]yes[/]" : "[grey]no[/]",
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
    public bool NotifyIdle { get; private set; }
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
                case "--notify-idle": o.NotifyIdle = true; break;
                case "-h" or "--help": o.ShowHelp = true; break;
            }
        }
        return o;
    }

    private static string? Next(string[] args, ref int i) => i + 1 < args.Length ? args[++i] : null;

    public static void PrintHelp()
    {
        AnsiConsole.WriteLine("tmux-watch - surface which Copilot panes need attention");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Usage: tmux-watch [options]");
        AnsiConsole.WriteLine("  --config <path>     Load JSON config (tokens, interval, notifications)");
        AnsiConsole.WriteLine("  --interval <sec>    Poll interval override");
        AnsiConsole.WriteLine("  --notify-idle       Also notify when a pane goes idle");
        AnsiConsole.WriteLine("  --calibrate         Print classification of all live panes and exit");
        AnsiConsole.WriteLine("  --once              Print one classification snapshot and exit");
        AnsiConsole.WriteLine("  -h, --help          Show this help");
    }
}
