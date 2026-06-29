using Spectre.Console;
using Spectre.Console.Rendering;
using TmuxWatch.Config;
using TmuxWatch.Detection;
using TmuxWatch.Monitor;
using TmuxWatch.Pointer;
using TmuxWatch.Tmux;

namespace TmuxWatch.Tui;

/// <summary>
/// Live Spectre.Console view of watched agent panes. WAITING panes are sorted
/// to the top. Number keys switch the terminal client's focus to a pane via the
/// read-only-safe client-control verbs; the TUI never sends input to a pane.
/// Pressing <c>p</c> pauses the focused pane, moving it to a separate "Paused"
/// table so the main table stays focused on the work in flight.
/// </summary>
public sealed class WatcherApp
{
    private readonly AttentionMonitor _monitor;
    private readonly ITmuxClient _tmux;
    private readonly WatchConfig _cfg;
    private readonly IPointerSignal _pointer;

    // Pane Ids the user has parked. Tracked here (not in the monitor) because it
    // is a view-only concern, independent of a pane's classified state.
    private readonly HashSet<string> _paused = new();

    // When false (the default) the wide-only columns (Path, Loc) are hidden so
    // the table fits a thin terminal split. Toggled at runtime with the w key.
    private bool _wideMode;

    public WatcherApp(AttentionMonitor monitor, ITmuxClient tmux, WatchConfig cfg, IPointerSignal? pointer = null)
    {
        _monitor = monitor;
        _tmux = tmux;
        _cfg = cfg;
        _pointer = pointer ?? new NullPointerSignal();
    }

    /// <summary>
    /// True when a pane needs the user and is NOT paused. The pointer signal mirrors
    /// this - paused panes live in the secondary table and are deliberately excluded,
    /// so parking a waiting pane clears the cue and resuming it re-arms it.
    /// </summary>
    internal static bool AnyActiveWaiting(IReadOnlyList<TrackedPaneView> panes, IReadOnlySet<string> pausedIds) =>
        panes.Any(p => p.AttentionOutstanding && !pausedIds.Contains(p.Pane.Id));

    /// <summary>Drive the level-triggered pointer cue from the non-paused panes.</summary>
    private void DrivePointer(IReadOnlyList<TrackedPaneView> all) =>
        _pointer.SetWaiting(AnyActiveWaiting(all, _paused));

    public void Run(CancellationToken token)
    {
        var pollMs = (int)(_cfg.PollIntervalSeconds * 1000);
        string? lastError = null;

        // Start on a clean screen so the watcher is the only thing in the terminal,
        // regardless of how it was launched (go scripts, dotnet run, published binary).
        // Also wipes any build/restore output `dotnet run` may have printed.
        AnsiConsole.Clear();

        var initial = BuildView(new List<TrackedPaneView>(), null, DateTimeOffset.UtcNow);

        AnsiConsole.Live(initial)
            .AutoClear(false)
            .Start(ctx =>
            {
                void Render(List<TrackedPaneView> all) =>
                    ctx.UpdateTarget(BuildView(all, lastError, DateTimeOffset.UtcNow));

                while (!token.IsCancellationRequested)
                {
                    var snapshot = _monitor.Tick();
                    lastError = snapshot.Error ?? lastError;
                    var all = OrderAll(snapshot.Panes, _paused);

                    Render(all);
                    DrivePointer(all);
                    ctx.Refresh();

                    if (WaitAndHandleKeys(pollMs, all, Render, ctx, token))
                        break; // quit requested
                }
            });
    }

    /// <summary>Returns true if the user asked to quit.</summary>
    private bool WaitAndHandleKeys(
        int pollMs,
        List<TrackedPaneView> all,
        Action<List<TrackedPaneView>> render,
        LiveDisplayContext ctx,
        CancellationToken token)
    {
        const int slice = 50;
        var elapsed = 0;
        while (elapsed < pollMs && !token.IsCancellationRequested)
        {
            try
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    if (key.Key is ConsoleKey.Q or ConsoleKey.Escape)
                        return true;
                    if (key.Key == ConsoleKey.P)
                    {
                        // Park (or resume) the focused pane. Re-order so it moves
                        // between the main and Paused tables immediately, and
                        // re-evaluate the pointer cue so pausing a waiting pane
                        // clears it (and resuming re-arms it) without waiting a tick.
                        if (ToggleFocusedPause(_paused, all))
                        {
                            all = OrderAll(all, _paused);
                            render(all);
                            DrivePointer(all);
                            ctx.Refresh();
                        }
                    }
                    else if (key.Key == ConsoleKey.W)
                    {
                        // Toggle wide mode, which shows/hides the Path and Loc
                        // columns. Re-render immediately so the change is visible.
                        _wideMode = !_wideMode;
                        render(all);
                        ctx.Refresh();
                    }
                    else if (char.IsDigit(key.KeyChar))
                    {
                        var index = key.KeyChar - '1';
                        if (index >= 0 && index < all.Count)
                        {
                            var target = all[index].Pane;
                            SwitchTo(target);
                            // Optimistically move the focus marker so it updates
                            // instantly instead of waiting for the next poll.
                            ApplyOptimisticFocus(all, target);
                            render(all);
                            ctx.Refresh();
                        }
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Console input redirected; ignore key handling.
            }

            Thread.Sleep(slice);
            elapsed += slice;
        }
        return false;
    }

    /// <summary>
    /// Toggles the paused flag of the currently focused (►) pane. Returns true if a
    /// focused pane was found and its paused state changed, so the caller can
    /// re-render; false when nothing is focused (a no-op).
    /// </summary>
    internal static bool ToggleFocusedPause(ISet<string> paused, IReadOnlyList<TrackedPaneView> all)
    {
        var focused = all.FirstOrDefault(v => v.Pane.IsFocused);
        if (focused is null)
            return false;

        var id = focused.Pane.Id;
        if (!paused.Remove(id))
            paused.Add(id);
        return true;
    }

    /// <summary>
    /// Marks <paramref name="target"/> as the focused pane in its session and
    /// clears focus from the other panes of that session, so exactly one row shows
    /// the marker until the next poll reconciles with tmux.
    /// </summary>
    internal static void ApplyOptimisticFocus(List<TrackedPaneView> ordered, Pane target)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            var view = ordered[i];
            var p = view.Pane;
            if (!string.Equals(p.SessionName, target.SessionName, StringComparison.Ordinal))
                continue;

            var updated = p with
            {
                WindowActive = p.WindowIndex == target.WindowIndex,
                PaneActive = p.Id == target.Id,
            };
            ordered[i] = view with { Pane = updated };
        }
    }

    private void SwitchTo(Pane pane)
    {
        // Focus-only: switch session then select the window. Never sends input.
        _tmux.SwitchClient(pane.SessionName);
        _tmux.SelectWindow(pane.WindowTarget);
    }

    /// <summary>
    /// Orders panes for display: paused panes sink below active ones, then within
    /// each group WAITING rises to the top and ties break by most-recently-entered.
    /// The result is a single list (active first, then paused) so a continuous,
    /// global row number can address any visible pane.
    /// </summary>
    internal static List<TrackedPaneView> OrderAll(
        IReadOnlyList<TrackedPaneView> panes, IReadOnlySet<string> pausedIds) =>
        panes
            .OrderBy(p => pausedIds.Contains(p.Pane.Id) ? 1 : 0)
            .ThenBy(p => Priority(p.State))
            .ThenByDescending(p => p.EnteredAt)
            .ToList();

    private static int Priority(PaneState state) => state switch
    {
        PaneState.Waiting => 0,
        PaneState.Working => 1,
        PaneState.Idle => 2,
        PaneState.Unknown => 3,
        PaneState.Dead => 4,
        _ => 5,
    };

    private IRenderable BuildView(List<TrackedPaneView> all, string? error, DateTimeOffset now)
    {
        var active = all.Where(p => !_paused.Contains(p.Pane.Id)).ToList();
        var paused = all.Where(p => _paused.Contains(p.Pane.Id)).ToList();

        // Continuous, global numbering across both tables (active first, then
        // paused), capped at 9 so a single key can address any visible row.
        var number = new Dictionary<string, int>();
        var n = 1;
        foreach (var p in all)
            number[p.Pane.Id] = n++;

        var main = BuildPaneTable(
            "tmux-watch — agent panes  (number = switch · ► = focused · p = pause/resume · w = wide · q = quit)",
            active, number, now, _wideMode);

        if (all.Count == 0)
            main.Caption = new TableTitle(error is null
                ? "No agent panes found."
                : $"[red]{Markup.Escape(error)}[/]");
        else if (error is not null)
            main.Caption = new TableTitle($"[red]tmux error: {Markup.Escape(error)}[/]");

        if (paused.Count == 0)
            return main;

        var pausedTable = BuildPaneTable(
            "Paused  (focus a row and press p to resume)", paused, number, now, _wideMode);
        return new Rows(main, pausedTable);
    }

    private static Table BuildPaneTable(
        string title,
        List<TrackedPaneView> panes,
        IReadOnlyDictionary<string, int> number,
        DateTimeOffset now,
        bool wideMode)
    {
        var table = new Table().Border(TableBorder.Rounded).Expand();
        // Soften the border to grey (Spectre styles the whole border together,
        // including row separators) so the lines read as quiet dividers rather
        // than full-on white. Row separators are only useful with 2+ rows.
        table.BorderStyle = new Style(Color.Grey);
        if (panes.Count > 1)
            table.ShowRowSeparators();
        table.Title = new TableTitle(title);
        table.AddColumn("#");
        table.AddColumn("State");
        table.AddColumn("Window");
        // Path and Loc are wide-only columns, hidden by default so the table
        // fits a thin terminal split.
        if (wideMode)
        {
            table.AddColumn("Path");
            table.AddColumn("Loc");
        }
        table.AddColumn("In state");

        foreach (var p in panes)
        {
            var focused = p.Pane.IsFocused;
            var window = string.IsNullOrWhiteSpace(p.Pane.WindowName) ? "—" : p.Pane.WindowName;
            var windowCell = focused
                ? $"[bold underline]{Markup.Escape(window)}[/]"
                : Markup.Escape(window);

            var idx = number.TryGetValue(p.Pane.Id, out var num) ? num : 0;
            var numCell = idx is >= 1 and <= 9 ? idx.ToString() : "·";
            // The focus marker shares the number column rather than taking a
            // dedicated one, keeping the table narrow for thin splits.
            if (focused)
                numCell = $"[green]►[/]{numCell}";

            var cells = new List<string>
            {
                numCell,
                StateMarkup(p.State, p.AttentionOutstanding),
                windowCell,
            };
            if (wideMode)
            {
                cells.Add(Markup.Escape(string.IsNullOrWhiteSpace(p.Pane.PathLabel) ? "—" : p.Pane.PathLabel));
                cells.Add(Markup.Escape(p.Pane.Location));
            }
            cells.Add(FormatDuration(p.TimeInState(now)));

            table.AddRow(cells.ToArray());
        }

        return table;
    }

    private static string StateMarkup(PaneState state, bool attention) => state switch
    {
        PaneState.Waiting => "[yellow]● WAITING[/]",
        PaneState.Working => "[blue]◐ working[/]",
        PaneState.Idle => "[green]○ idle[/]",
        PaneState.Dead => "[red]✗ dead[/]",
        _ => "[grey]? unknown[/]",
    };

    private static string FormatDuration(TimeSpan d)
    {
        if (d.TotalSeconds < 60) return $"{(int)d.TotalSeconds}s";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m {d.Seconds}s";
        return $"{(int)d.TotalHours}h {d.Minutes}m";
    }
}
