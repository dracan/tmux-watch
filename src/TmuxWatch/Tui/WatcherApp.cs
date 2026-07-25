using Spectre.Console;
using Spectre.Console.Rendering;
using TmuxWatch.Config;
using TmuxWatch.Detection;
using TmuxWatch.Monitor;
using TmuxWatch.Pointer;
using TmuxWatch.Tmux;

namespace TmuxWatch.Tui;

/// <summary>
/// One addressable row of the watcher. <see cref="Agent"/> is the classified view for a
/// watched agent pane and is null for a non-agent pane, which carries no state, no
/// in-state timer, and never reaches the attention pipeline.
/// </summary>
internal sealed record WatchRow(Pane Pane, TrackedPaneView? Agent)
{
    public string Id => Pane.Id;
    public bool IsAgent => Agent is not null;
}

/// <summary>
/// The rows of one frame, split by the table they render into. <see cref="All"/> is the
/// same rows in render order, which is what row numbering and the highlight walk over.
/// </summary>
internal sealed record WatchLayout(
    IReadOnlyList<WatchRow> Agent,
    IReadOnlyList<WatchRow> Other,
    IReadOnlyList<WatchRow> Paused)
{
    public static readonly WatchLayout Empty =
        new(Array.Empty<WatchRow>(), Array.Empty<WatchRow>(), Array.Empty<WatchRow>());

    public IReadOnlyList<WatchRow> All { get; } =
        Agent.Concat(Other).Concat(Paused).ToList();
}

/// <summary>
/// Live Spectre.Console view of watched agent panes, plus an optional table of the
/// non-agent panes. WAITING panes are sorted to the top of the agent table. A highlighted
/// row is moved with the arrow keys and is the target of the pause and acknowledge
/// actions; enter or a row's address key switches the terminal client to that pane using
/// the focus-only verbs. The TUI never sends input to a pane.
/// </summary>
public sealed class WatcherApp
{
    private readonly AttentionMonitor _monitor;
    private readonly ITmuxClient _tmux;
    private readonly WatchConfig _cfg;
    private readonly IPointerSignal _pointer;

    // Pane Ids the user has parked. Tracked here (not in the monitor) because it
    // is a view-only concern, independent of a pane's classified state. Holds both
    // agent and non-agent pane ids; for a non-agent pane it is purely decluttering,
    // since such a pane contributes no cue to mute.
    private readonly HashSet<string> _paused = new();

    // When false (the default) the wide-only columns (Path, Loc) are hidden so
    // the table fits a thin terminal split. Toggled at runtime with the w key.
    private bool _wideMode;

    // Show the non-agent panes (o), and within those the companion panes that share a
    // window with an agent pane (c). Both default on and, like _wideMode, are runtime
    // only - they reset to their default on every launch and have no config key.
    private bool _showOtherPanes = true;
    private bool _showCompanionPanes = true;

    // The highlighted row, anchored to a pane id so it follows its pane as the agent
    // table re-sorts. _highlightIndex is the last resolved position, used to fall back to
    // the nearest surviving row when that pane disappears or a toggle hides it.
    private string? _highlightedId;
    private int _highlightIndex;

    public WatcherApp(AttentionMonitor monitor, ITmuxClient tmux, WatchConfig cfg, IPointerSignal? pointer = null)
    {
        _monitor = monitor;
        _tmux = tmux;
        _cfg = cfg;
        _pointer = pointer ?? new NullPointerSignal();
    }

    /// <summary>Mutable per-frame state shared between the poll loop and key handling.</summary>
    private sealed class FrameState
    {
        public List<TrackedPaneView> AgentViews = new();
        public List<Pane> OtherPanes = new();
        public WatchLayout Layout = WatchLayout.Empty;
        public string? Error;
    }

    /// <summary>
    /// Aggregate pointer cue over the non-paused agent panes: red (Waiting) if any needs
    /// an answer, else green (Done) if any finished its turn, else normal. Paused panes
    /// live in the secondary table and are deliberately excluded, so parking a pane
    /// clears its cue and resuming it re-arms it. WAITING outranks DONE. Non-agent panes
    /// never reach this method, so they cannot influence the cue.
    /// </summary>
    internal static PointerState AggregatePointerState(
        IReadOnlyList<TrackedPaneView> panes, IReadOnlySet<string> pausedIds)
    {
        var anyDone = false;
        foreach (var p in panes)
        {
            if (pausedIds.Contains(p.Pane.Id))
                continue;
            if (p.State == PaneState.Waiting)
                return PointerState.Waiting;   // red wins; no need to look further
            if (p.State == PaneState.Done)
                anyDone = true;
        }
        return anyDone ? PointerState.Done : PointerState.Normal;
    }

    /// <summary>Drive the level-triggered pointer cue from the non-paused agent panes.</summary>
    private void DrivePointer(IReadOnlyList<TrackedPaneView> agentViews) =>
        _pointer.SetState(AggregatePointerState(agentViews, _paused));

    public void Run(CancellationToken token)
    {
        var pollMs = (int)(_cfg.PollIntervalSeconds * 1000);

        // Start on a clean screen so the watcher is the only thing in the terminal,
        // regardless of how it was launched (go scripts, dotnet run, published binary).
        // Also wipes any build/restore output `dotnet run` may have printed.
        AnsiConsole.Clear();

        var frame = new FrameState();
        var initial = BuildView(WatchLayout.Empty, null, DateTimeOffset.UtcNow);

        AnsiConsole.Live(initial)
            .AutoClear(false)
            .Start(ctx =>
            {
                while (!token.IsCancellationRequested)
                {
                    var snapshot = _monitor.Tick();
                    frame.Error = snapshot.Error ?? frame.Error;
                    frame.AgentViews = snapshot.Panes.ToList();
                    frame.OtherPanes = snapshot.OtherPanes.ToList();

                    Rebuild(frame);
                    Render(frame, ctx);
                    DrivePointer(frame.AgentViews);

                    if (WaitAndHandleKeys(pollMs, frame, ctx, token))
                        break; // quit requested
                }
            });
    }

    /// <summary>
    /// Recompute the frame's rows and re-resolve the highlight against them. Called after
    /// every poll and after any key that changes what is visible.
    /// </summary>
    private void Rebuild(FrameState frame)
    {
        frame.Layout = BuildLayout(
            frame.AgentViews, frame.OtherPanes, _paused, _showOtherPanes, _showCompanionPanes);

        var index = ResolveHighlightIndex(frame.Layout.All, _highlightedId, _highlightIndex);
        _highlightIndex = index < 0 ? 0 : index;
        _highlightedId = index < 0 ? null : frame.Layout.All[index].Id;
    }

    private void Render(FrameState frame, LiveDisplayContext ctx)
    {
        ctx.UpdateTarget(BuildView(frame.Layout, frame.Error, DateTimeOffset.UtcNow));
        ctx.Refresh();
    }

    /// <summary>
    /// Split the frame's panes into the three tables. The agent table keeps the
    /// attention-priority order; the non-agent rows keep tmux's own order so their
    /// positions - and therefore their address keys - stay put between polls while the
    /// agent table re-sorts. Hiding the non-agent panes hides their paused rows too.
    /// </summary>
    internal static WatchLayout BuildLayout(
        IReadOnlyList<TrackedPaneView> agentViews,
        IReadOnlyList<Pane> otherPanes,
        IReadOnlySet<string> pausedIds,
        bool showOtherPanes,
        bool showCompanionPanes)
    {
        var orderedAgents = OrderAll(agentViews, pausedIds)
            .Select(v => new WatchRow(v.Pane, v))
            .ToList();

        var visibleOthers = showOtherPanes
            ? FilterOtherPanes(otherPanes, agentViews, showCompanionPanes)
                .Select(p => new WatchRow(p, null))
                .ToList()
            : new List<WatchRow>();

        return new WatchLayout(
            Agent: orderedAgents.Where(r => !pausedIds.Contains(r.Id)).ToList(),
            Other: visibleOthers.Where(r => !pausedIds.Contains(r.Id)).ToList(),
            Paused: orderedAgents.Where(r => pausedIds.Contains(r.Id))
                .Concat(visibleOthers.Where(r => pausedIds.Contains(r.Id)))
                .ToList());
    }

    /// <summary>
    /// The non-agent panes to show, in tmux order (session, window index, pane index).
    /// A <em>companion</em> pane is a non-agent pane sharing a window with at least one
    /// agent pane; when companions are hidden those rows drop out, leaving only panes in
    /// windows that host no agent.
    /// </summary>
    internal static List<Pane> FilterOtherPanes(
        IReadOnlyList<Pane> otherPanes,
        IReadOnlyList<TrackedPaneView> agentViews,
        bool showCompanionPanes)
    {
        var agentWindows = new HashSet<(string Session, int Window)>(
            agentViews.Select(v => (v.Pane.SessionName, v.Pane.WindowIndex)));

        return otherPanes
            .Where(p => showCompanionPanes || !agentWindows.Contains((p.SessionName, p.WindowIndex)))
            .OrderBy(p => p.SessionName, StringComparer.Ordinal)
            .ThenBy(p => p.WindowIndex)
            .ThenBy(p => p.PaneIndex)
            .ToList();
    }

    /// <summary>
    /// Where the highlight sits this frame. The id wins when its pane is still visible,
    /// so the highlight follows its pane through a re-sort; otherwise it falls back to
    /// the nearest surviving row by position. Returns -1 when there is nothing to
    /// highlight.
    /// </summary>
    internal static int ResolveHighlightIndex(
        IReadOnlyList<WatchRow> rows, string? highlightedId, int lastIndex)
    {
        if (rows.Count == 0)
            return -1;

        if (highlightedId is not null)
        {
            for (var i = 0; i < rows.Count; i++)
                if (string.Equals(rows[i].Id, highlightedId, StringComparison.Ordinal))
                    return i;
        }

        return Math.Clamp(lastIndex, 0, rows.Count - 1);
    }

    /// <summary>
    /// The key that addresses the row at <paramref name="index"/> (0-based, in render
    /// order): digits 1-9 for the first nine rows, then shift+letter A-Z for rows ten
    /// onward. Empty when the row is past the addressable range.
    /// </summary>
    internal static string AddressKey(int index)
    {
        if (index < 0)
            return "";
        if (index < 9)
            return ((char)('1' + index)).ToString();
        var offset = index - 9;
        return offset < 26 ? ((char)('A' + offset)).ToString() : "";
    }

    /// <summary>
    /// The row index a keypress addresses, or -1 if the key is not an address key.
    /// Uppercase only beyond the digits, so lowercase keys stay available as commands.
    /// </summary>
    internal static int IndexForAddressKey(char keyChar)
    {
        if (keyChar is >= '1' and <= '9')
            return keyChar - '1';
        if (keyChar is >= 'A' and <= 'Z')
            return 9 + (keyChar - 'A');
        return -1;
    }

    /// <summary>Returns true if the user asked to quit.</summary>
    private bool WaitAndHandleKeys(
        int pollMs,
        FrameState frame,
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

                    if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
                    {
                        // Walk the visible rows as one continuous list across all tables.
                        MoveHighlight(frame, key.Key == ConsoleKey.UpArrow ? -1 : 1);
                        Render(frame, ctx);
                    }
                    else if (key.Key == ConsoleKey.Enter)
                    {
                        Activate(frame, _highlightIndex, ctx);
                    }
                    else if (key.KeyChar == 'p')
                    {
                        // Park (or resume) the highlighted row. Re-order so it moves
                        // between its table and the Paused table immediately, and
                        // re-evaluate the pointer cue so pausing a waiting pane
                        // clears it (and resuming re-arms it) without waiting a tick.
                        // A non-agent row has no cue, so this is purely decluttering.
                        if (TogglePause(_paused, HighlightedRow(frame)?.Id))
                        {
                            Rebuild(frame);
                            Render(frame, ctx);
                            DrivePointer(frame.AgentViews);
                        }
                    }
                    else if (key.KeyChar == 'w')
                    {
                        // Toggle wide mode, which shows/hides the Path and Loc
                        // columns. Re-render immediately so the change is visible.
                        _wideMode = !_wideMode;
                        Render(frame, ctx);
                    }
                    else if (key.KeyChar == 'o')
                    {
                        _showOtherPanes = !_showOtherPanes;
                        Rebuild(frame);
                        Render(frame, ctx);
                    }
                    else if (key.KeyChar == 'c')
                    {
                        // Inert while the other-panes table is hidden: Rebuild simply
                        // produces the same (empty) row set.
                        _showCompanionPanes = !_showCompanionPanes;
                        Rebuild(frame);
                        Render(frame, ctx);
                    }
                    else if (key.KeyChar == 'a')
                    {
                        // Acknowledge the highlighted row if it is DONE, without switching
                        // to it - the keystroke is what clears it, so a pane that
                        // already holds focus is never auto-acknowledged. Re-render and
                        // re-drive the pointer so the green cue clears immediately.
                        var row = HighlightedRow(frame);
                        if (row is { IsAgent: true } && _monitor.Acknowledge(row.Id))
                        {
                            ApplyOptimisticAck(frame.AgentViews, row.Id);
                            Rebuild(frame);
                            Render(frame, ctx);
                            DrivePointer(frame.AgentViews);
                        }
                    }
                    else if (IndexForAddressKey(key.KeyChar) is var index and >= 0)
                    {
                        Activate(frame, index, ctx);
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

    private WatchRow? HighlightedRow(FrameState frame) =>
        _highlightIndex >= 0 && _highlightIndex < frame.Layout.All.Count
            ? frame.Layout.All[_highlightIndex]
            : null;

    private void MoveHighlight(FrameState frame, int delta)
    {
        var rows = frame.Layout.All;
        if (rows.Count == 0)
            return;

        _highlightIndex = Math.Clamp(_highlightIndex + delta, 0, rows.Count - 1);
        _highlightedId = rows[_highlightIndex].Id;
    }

    /// <summary>
    /// Switch to the row at <paramref name="index"/>: move the client's focus to that
    /// pane, acknowledge it if it is a DONE agent pane (you have seen it), and reflect
    /// both optimistically so the view updates this frame instead of next poll.
    /// </summary>
    private void Activate(FrameState frame, int index, LiveDisplayContext ctx)
    {
        var rows = frame.Layout.All;
        if (index < 0 || index >= rows.Count)
            return;

        var row = rows[index];
        SwitchTo(row.Pane);

        if (row.IsAgent && _monitor.Acknowledge(row.Id))
            ApplyOptimisticAck(frame.AgentViews, row.Id);

        // Optimistically move the focus marker so it updates instantly instead of
        // waiting for the next poll. Both row kinds share the marker, so both lists
        // are updated.
        ApplyOptimisticFocus(frame.AgentViews, row.Pane);
        ApplyOptimisticFocus(frame.OtherPanes, row.Pane);

        // The highlight follows the row you jumped to.
        _highlightedId = row.Id;
        Rebuild(frame);
        Render(frame, ctx);
        DrivePointer(frame.AgentViews);
    }

    /// <summary>
    /// Toggles the paused flag of <paramref name="id"/>. Returns true when a row was
    /// given (so the caller can re-render), false when there is nothing highlighted.
    /// </summary>
    internal static bool TogglePause(ISet<string> paused, string? id)
    {
        if (id is null)
            return false;

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
            if (Refocus(view.Pane, target) is { } updated)
                ordered[i] = view with { Pane = updated };
        }
    }

    /// <summary>Same optimistic focus move, over the non-agent panes.</summary>
    internal static void ApplyOptimisticFocus(List<Pane> panes, Pane target)
    {
        for (var i = 0; i < panes.Count; i++)
        {
            if (Refocus(panes[i], target) is { } updated)
                panes[i] = updated;
        }
    }

    /// <summary>
    /// The pane with its focus flags rewritten for a switch to <paramref name="target"/>,
    /// or null when the pane belongs to another session and must be left alone. Because
    /// the switch now selects the pane as well as the window, exactly the target pane
    /// ends up active.
    /// </summary>
    private static Pane? Refocus(Pane pane, Pane target)
    {
        if (!string.Equals(pane.SessionName, target.SessionName, StringComparison.Ordinal))
            return null;

        return pane with
        {
            WindowActive = pane.WindowIndex == target.WindowIndex,
            PaneActive = pane.Id == target.Id,
        };
    }

    /// <summary>
    /// Reflects an acknowledgement in the local view immediately: a DONE pane becomes
    /// IDLE with no outstanding attention, so the row and pointer update this frame
    /// instead of waiting for the next poll to reconcile with the monitor.
    /// </summary>
    internal static void ApplyOptimisticAck(List<TrackedPaneView> ordered, string paneId)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            var view = ordered[i];
            if (view.Pane.Id == paneId && view.State == PaneState.Done)
                ordered[i] = view with { State = PaneState.Idle, AttentionOutstanding = false };
        }
    }

    private void SwitchTo(Pane pane) => SwitchTo(_tmux, pane);

    /// <summary>
    /// Focus-only jump: switch session, select the window, then select the pane so a row
    /// naming one pane of a split lands on that pane rather than on whichever pane the
    /// window last had active. Order matters - the pane selection must come last, after
    /// the window it belongs to is current. Never sends input.
    /// </summary>
    internal static void SwitchTo(ITmuxClient tmux, Pane pane)
    {
        tmux.SwitchClient(pane.SessionName);
        tmux.SelectWindow(pane.WindowTarget);
        tmux.SelectPane(pane.Id);
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
        PaneState.Done => 1,
        PaneState.Working => 2,
        PaneState.Idle => 3,
        PaneState.Unknown => 4,
        PaneState.Dead => 5,
        _ => 6,
    };

    private IRenderable BuildView(WatchLayout layout, string? error, DateTimeOffset now)
    {
        // Continuous, global numbering across all tables in render order, so a single
        // key can address any visible row.
        var number = new Dictionary<string, int>();
        var n = 0;
        foreach (var row in layout.All)
            number[row.Id] = n++;

        var main = BuildRowTable(
            "tmux-watch - agent panes  (up/down = select · enter/key = switch · a = ack · p = pause · o = others · c = companions · w = wide · q = quit)",
            layout.Agent, number, now, _wideMode, _highlightedId);

        if (layout.All.Count == 0)
            main.Caption = new TableTitle(error is null
                ? "No panes found."
                : $"[red]{Markup.Escape(error)}[/]");
        else if (layout.Agent.Count == 0 && error is null)
            main.Caption = new TableTitle("[grey]No agent panes found.[/]");
        else if (error is not null)
            main.Caption = new TableTitle($"[red]tmux error: {Markup.Escape(error)}[/]");

        var parts = new List<IRenderable> { main };

        if (layout.Other.Count > 0)
            parts.Add(BuildRowTable(
                "Other panes  (o = hide · c = companion panes)",
                layout.Other, number, now, _wideMode, _highlightedId));

        if (layout.Paused.Count > 0)
            parts.Add(BuildRowTable(
                "Paused  (highlight a row and press p to resume)",
                layout.Paused, number, now, _wideMode, _highlightedId));

        return parts.Count == 1 ? main : new Rows(parts);
    }

    private static Table BuildRowTable(
        string title,
        IReadOnlyList<WatchRow> rows,
        IReadOnlyDictionary<string, int> number,
        DateTimeOffset now,
        bool wideMode,
        string? highlightedId)
    {
        var table = new Table().Border(TableBorder.Rounded).Expand();
        // Soften the border to grey (Spectre styles the whole border together,
        // including row separators) so the lines read as quiet dividers rather
        // than full-on white. Row separators are only useful with 2+ rows.
        table.BorderStyle = new Style(Color.Grey);
        if (rows.Count > 1)
            table.ShowRowSeparators();
        table.Title = new TableTitle(title);
        table.AddColumn("#");
        // The state column does double duty so the table stays narrow enough for a thin
        // dock: the classified state for an agent row, the foreground process for a
        // non-agent row.
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

        foreach (var row in rows)
        {
            var pane = row.Pane;
            var focused = pane.IsFocused;
            var window = string.IsNullOrWhiteSpace(pane.WindowName) ? "—" : pane.WindowName;
            var windowCell = focused
                ? $"[bold underline]{Markup.Escape(window)}[/]"
                : Markup.Escape(window);

            var address = number.TryGetValue(pane.Id, out var index) ? AddressKey(index) : "";
            var addressCell = address.Length == 0 ? "·" : address;
            // The focus marker shares the number column rather than taking a
            // dedicated one, keeping the table narrow for thin splits. The highlight
            // bar sits outside it so the two markers stay visually distinct.
            if (focused)
                addressCell = $"[green]►[/]{addressCell}";
            if (string.Equals(pane.Id, highlightedId, StringComparison.Ordinal))
                addressCell = $"[yellow]▌[/]{addressCell}";

            var cells = new List<string>
            {
                addressCell,
                row.Agent is { } agent
                    ? StateMarkup(agent.State, agent.AttentionOutstanding)
                    : ProcessMarkup(pane.Command),
                windowCell,
            };
            if (wideMode)
            {
                cells.Add(Markup.Escape(string.IsNullOrWhiteSpace(pane.PathLabel) ? "—" : pane.PathLabel));
                cells.Add(Markup.Escape(pane.Location));
            }
            // An agent row times its classified state; a non-agent row has no state, so
            // it reports how long its window has been quiet instead.
            cells.Add(row.Agent is { } tracked
                ? FormatDuration(tracked.TimeInState(now))
                : pane.TimeSinceActivity(now) is { } idleFor
                    ? $"[grey]{FormatDuration(idleFor)}[/]"
                    : "[grey]—[/]");

            table.AddRow(cells.ToArray());
        }

        return table;
    }

    private static string StateMarkup(PaneState state, bool attention) => state switch
    {
        PaneState.Waiting => "[yellow]● WAITING[/]",
        PaneState.Done => "[bold green]✓ DONE[/]",
        PaneState.Working => "[blue]◐ working[/]",
        PaneState.Idle => "[green]○ idle[/]",
        PaneState.Dead => "[red]✗ dead[/]",
        _ => "[grey]? unknown[/]",
    };

    /// <summary>The state column's reading for a non-agent row: its foreground process.</summary>
    private static string ProcessMarkup(string command) =>
        string.IsNullOrWhiteSpace(command)
            ? "[grey]—[/]"
            : $"[grey]{Markup.Escape(command)}[/]";

    private static string FormatDuration(TimeSpan d)
    {
        if (d.TotalSeconds < 60) return $"{(int)d.TotalSeconds}s";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m {d.Seconds}s";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours}h {d.Minutes}m";
        return $"{(int)d.TotalDays}d {d.Hours}h";
    }
}
