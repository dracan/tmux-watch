using System.Diagnostics;
using System.Text;
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
/// A pane's sort position, frozen at the moment it was acknowledged and honoured until
/// <see cref="ReleaseAt"/>. Both halves of the sort key are captured, not just the rank:
/// acknowledging resets a pane's <c>EnteredAt</c>, and since ties break on that
/// descending, pinning the rank alone would still let the row jump to the top of its own
/// rank instead of down the table - the same defect at a smaller amplitude.
/// </summary>
internal readonly record struct AckHold(
    int Priority, DateTimeOffset EnteredAt, DateTimeOffset ReleaseAt);

/// <summary>
/// Live Spectre.Console view of watched agent panes, plus an optional table of the
/// non-agent panes. WAITING panes are sorted to the top of the agent table. A highlighted
/// row is moved with the arrow keys and is the target of the pause and acknowledge
/// actions; enter or a row's address key switches the terminal client to that pane using
/// the focus-only verbs. The TUI never sends input to a pane.
/// </summary>
public sealed class WatcherApp
{
    /// <summary>
    /// Floor for the poll interval, in milliseconds. Low enough that no sane
    /// <c>--interval</c> is altered, high enough that a zero or negative one cannot
    /// turn the wait loop into a busy spin over tmux. See <see cref="Run"/>.
    /// </summary>
    internal const int MinPollMs = 250;

    private readonly AttentionMonitor _monitor;
    private readonly ITmuxClient _tmux;
    private readonly WatchConfig _cfg;
    private readonly IPointerSignal _pointer;

    // Pane Ids the user has parked. Tracked here (not in the monitor) because it
    // is a view-only concern, independent of a pane's classified state. Holds both
    // agent and non-agent pane ids; for a non-agent pane it is purely decluttering,
    // since such a pane contributes no cue to mute.
    private readonly HashSet<string> _paused = new();

    // Panes whose sort position is frozen because they were just acknowledged. Kept here
    // for the same reason as _paused: where a row is drawn is a view concern, and the
    // monitor stays the sole authority on what a pane *is*. Acknowledge is therefore
    // untouched, so notifications, the pointer cue, and the state machine keep their
    // semantics for free.
    private readonly Dictionary<string, AckHold> _ackHolds = new();

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

    // The open name prompt, or null when there is none. While it is non-null the prompt
    // is modal: every keystroke goes to the editor and no command or address key fires.
    // The target session is captured when the prompt opens rather than resolved on
    // submit, because polling continues while the user types and the focus marker can
    // move under them - re-resolving would silently land the window elsewhere.
    private LineEditor? _prompt;
    private string _promptSession = "";

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
    /// never reach this method, so they cannot influence the cue. BACKGND is a quiet
    /// state and is likewise not a cue: a pane holding a background task has not asked
    /// for the user yet, and will announce itself as DONE when it has.
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

    /// <summary>
    /// The poll slice for a configured interval, floored at <see cref="MinPollMs"/> -
    /// clamped at the point of use like every other timing knob (see AttentionMonitor's
    /// constructor). Zero or negative makes the wait loop's body unreachable: no sleep,
    /// no key handling, and Tick() re-enters at once, forking an `lsp` plus a
    /// capture-pane per agent pane as fast as the process can. That is a pegged core and
    /// a watcher answering nothing but SIGINT, from `--interval 0` or a stray minus sign
    /// (both parse, neither was checked). NaN falls through the comparison, so it is
    /// mapped explicitly rather than left to cast to zero.
    /// </summary>
    internal static int PollMsFor(double seconds)
    {
        if (double.IsNaN(seconds))
            return MinPollMs;
        var ms = seconds * 1000;
        return ms >= MinPollMs ? (int)Math.Min(ms, int.MaxValue) : MinPollMs;
    }

    public void Run(CancellationToken token)
    {
        var pollMs = PollMsFor(_cfg.PollIntervalSeconds);

        // Deliberately not disposed: disposing a writer over Console.OpenStandardOutput()
        // closes stdout itself, which would silently swallow anything written after Run
        // returns - a stack trace from an unhandled exception, most importantly. Flushing
        // is all that is needed, and the finally below does it.
        var buffered = BufferStdout();

        // Start on a clean screen so the watcher is the only thing in the terminal,
        // regardless of how it was launched (go scripts, dotnet run, published binary).
        // Also wipes any build/restore output `dotnet run` may have printed.
        AnsiConsole.Clear();

        var frame = new FrameState();
        var initial = BuildView(WatchLayout.Empty, null, DateTimeOffset.UtcNow);

        try
        {
            AnsiConsole.Live(initial)
                .AutoClear(false)
                .Start(ctx =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        var snapshot = _monitor.Tick();
                        // Each tick contacts tmux and reports authoritatively, so the
                        // snapshot replaces the error rather than merging with it -
                        // carrying the previous one forward left a resolved failure (and,
                        // once n existed, a one-off create failure) pinned to the caption
                        // for the rest of the session.
                        frame.Error = snapshot.Error;
                        frame.AgentViews = snapshot.Panes.ToList();
                        frame.OtherPanes = snapshot.OtherPanes.ToList();

                        // The only place a hold is released. Doing it here rather than in
                        // Rebuild is what ties the release to a poll: a row that has to
                        // move does so alongside news from tmux, never on a keystroke.
                        ExpireHolds(
                            _ackHolds,
                            frame.AgentViews.Select(v => v.Pane.Id),
                            DateTimeOffset.UtcNow);

                        Rebuild(frame);
                        Render(frame, ctx);
                        DrivePointer(frame.AgentViews);

                        if (WaitAndHandleKeys(pollMs, frame, ctx, token))
                            break; // quit requested
                    }
                });
        }
        finally
        {
            // The teardown Spectre writes on exit (cursor restore) is buffered too, so
            // the last flush has to happen after Live returns, however it returned.
            buffered?.Flush();
        }
    }

    /// <summary>
    /// Replaces <see cref="Console.Out"/> with a buffered, non-auto-flushing writer and
    /// returns it, or null if that is not possible.
    /// <para>
    /// This is what makes the view feel responsive. Spectre emits a repaint as several
    /// hundred small writes - one per styled segment, ~480 for this layout - and the
    /// default <c>Console.Out</c> on Unix auto-flushes, so each becomes its own write
    /// syscall. Over a WSL console bridge that is hundreds of round trips per frame, which
    /// is felt directly as lag while typing in the name prompt, since every keystroke
    /// repaints. Buffering turns a frame into one write; <see cref="Render"/> flushes.
    /// </para>
    /// <para>
    /// Console.SetOut only swaps the sink - the underlying handle is untouched - so
    /// Spectre's terminal detection and width probing are unaffected. It must happen
    /// before the first AnsiConsole use, which is why it is the first thing Run does.
    /// </para>
    /// </summary>
    private static StreamWriter? BufferStdout()
    {
        try
        {
            var writer = new StreamWriter(
                Console.OpenStandardOutput(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 64 * 1024)
            {
                AutoFlush = false,
            };
            Console.SetOut(writer);

            // Rebind Spectre onto the new sink. It captures Console.Out when its console
            // is first created, so without this the buffering would silently do nothing
            // if anything touched AnsiConsole earlier in startup. Detection still works:
            // the fresh console sees Writer == Console.Out and asks Console.IsOutputRedirected,
            // which reports on the real handle, and that is untouched by SetOut.
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings());

            return writer;
        }
        catch (IOException)
        {
            // No usable stdout (redirected oddly, closed); carry on unbuffered.
            return null;
        }
    }

    /// <summary>
    /// Recompute the frame's rows and re-resolve the highlight against them. Called after
    /// every poll and after any key that changes what is visible.
    /// <para>
    /// Deliberately applies the acknowledgement holds without ever expiring one: a key
    /// that rebuilds the view must not be able to release a hold, or the row would move
    /// on a keystroke after all - just a later one. Expiry belongs to the poll loop.
    /// </para>
    /// </summary>
    private void Rebuild(FrameState frame)
    {
        frame.Layout = BuildLayout(
            frame.AgentViews, frame.OtherPanes, _paused, _showOtherPanes, _showCompanionPanes,
            _ackHolds);

        var index = ResolveHighlightIndex(frame.Layout.All, _highlightedId, _highlightIndex);
        _highlightIndex = index < 0 ? 0 : index;
        _highlightedId = index < 0 ? null : frame.Layout.All[index].Id;
    }

    private void Render(FrameState frame, LiveDisplayContext ctx)
    {
        ctx.UpdateTarget(BuildView(frame.Layout, frame.Error, DateTimeOffset.UtcNow));
        ctx.Refresh();
        // One syscall per frame instead of one per styled segment; see BufferStdout.
        Console.Out.Flush();
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
        bool showCompanionPanes,
        IReadOnlyDictionary<string, AckHold>? ackHolds = null)
    {
        var orderedAgents = OrderAll(agentViews, pausedIds, ackHolds)
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
    /// The session a new window should be created in: the session of the pane carrying
    /// the focus marker. tmux tracks the active window and pane <em>per session</em>, so
    /// with several sessions more than one visible row can carry the marker; the tie
    /// breaks toward the highlighted row's session, which is the one the user is looking
    /// at. With no marked row at all the highlighted row's session is used, and with no
    /// rows there is nothing to target and the caller does nothing.
    /// <para>
    /// This reads only what is already on screen, so resolving costs no tmux call.
    /// </para>
    /// </summary>
    internal static string? ResolveTargetSession(
        IReadOnlyList<WatchRow> rows, string? highlightedId)
    {
        if (rows.Count == 0)
            return null;

        var highlighted = rows.FirstOrDefault(
            r => string.Equals(r.Id, highlightedId, StringComparison.Ordinal)) ?? rows[0];

        var focused = rows.Where(r => r.Pane.IsFocused).ToList();
        if (focused.Count == 0)
            return highlighted.Pane.SessionName;

        var preferred = focused.FirstOrDefault(r => string.Equals(
            r.Pane.SessionName, highlighted.Pane.SessionName, StringComparison.Ordinal));

        return (preferred ?? focused[0]).Pane.SessionName;
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

    /// <summary>What a keystroke means, once the modal state has been taken into account.</summary>
    internal enum KeyAction
    {
        Ignore,
        PromptInput,
        Quit,
        MoveUp,
        MoveDown,
        ActivateHighlighted,
        AddressRow,
        TogglePauseRow,
        ToggleWide,
        ToggleOthers,
        ToggleCompanions,
        AcknowledgeRow,
        OpenNewWindowPrompt,
    }

    /// <summary>
    /// What <paramref name="key"/> means right now. Pure, so the modal rule is testable:
    /// while the name prompt is open <em>every</em> keystroke is prompt input, which is
    /// what stops `q` quitting, esc exiting the app rather than the prompt, and digits
    /// jumping to a row, mid-name.
    /// </summary>
    internal static KeyAction ClassifyKey(ConsoleKeyInfo key, bool promptOpen)
    {
        if (promptOpen)
            return KeyAction.PromptInput;

        // Quit matches esc by named key but 'q' by *character*, not ConsoleKey.Q.
        // ConsoleKeyInfo.Key is the physical key, so shift+Q - the address key for row
        // 26, routine once Other panes lists a whole server - arrives as ConsoleKey.Q
        // too and would exit the watcher instead of jumping. Every other single-letter
        // command already matches on KeyChar, which is why only this one collided.
        if (key.Key == ConsoleKey.Escape)
            return KeyAction.Quit;
        if (key.Key == ConsoleKey.UpArrow)
            return KeyAction.MoveUp;
        if (key.Key == ConsoleKey.DownArrow)
            return KeyAction.MoveDown;
        if (key.Key == ConsoleKey.Enter)
            return KeyAction.ActivateHighlighted;

        return key.KeyChar switch
        {
            'q' => KeyAction.Quit,
            'p' => KeyAction.TogglePauseRow,
            'w' => KeyAction.ToggleWide,
            'o' => KeyAction.ToggleOthers,
            'c' => KeyAction.ToggleCompanions,
            'a' => KeyAction.AcknowledgeRow,
            'n' => KeyAction.OpenNewWindowPrompt,
            _ => IndexForAddressKey(key.KeyChar) >= 0 ? KeyAction.AddressRow : KeyAction.Ignore,
        };
    }

    /// <summary>Returns true if the user asked to quit.</summary>
    private bool WaitAndHandleKeys(
        int pollMs,
        FrameState frame,
        LiveDisplayContext ctx,
        CancellationToken token)
    {
        // Measured rather than accumulated from the nominal slice: Thread.Sleep overshoots,
        // so adding the requested figure stretched the poll interval well past pollMs -
        // most visibly with the short slice used while the prompt is open.
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < pollMs && !token.IsCancellationRequested)
        {
            var promptDirty = false;
            try
            {
                // Drain every keystroke that has arrived, rather than one per slice. One
                // key per sleep caps input at 20 characters a second and makes fast typing
                // in the name prompt arrive in visible bursts.
                while (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);

                    switch (ClassifyKey(key, _prompt is not null))
                    {
                        case KeyAction.PromptInput:
                            // Rendering is deferred to once per drain: a full table
                            // rebuild per keystroke is wasted work when several are
                            // already queued.
                            promptDirty |= HandlePromptKey(key, frame, ctx);
                            break;

                        case KeyAction.Quit:
                            return true;

                        case KeyAction.MoveUp:
                        case KeyAction.MoveDown:
                            // Walk the visible rows as one continuous list across all tables.
                            MoveHighlight(frame, key.Key == ConsoleKey.UpArrow ? -1 : 1);
                            Render(frame, ctx);
                            break;

                        case KeyAction.ActivateHighlighted:
                            Activate(frame, _highlightIndex, ctx);
                            break;

                        case KeyAction.AddressRow:
                            Activate(frame, IndexForAddressKey(key.KeyChar), ctx);
                            break;

                        case KeyAction.TogglePauseRow:
                            // Park (or resume) the highlighted row. Re-order so it moves
                            // between its table and the Paused table immediately, and
                            // re-evaluate the pointer cue so pausing a waiting pane
                            // clears it (and resuming re-arms it) without waiting a tick.
                            // A non-agent row has no cue, so this is purely decluttering.
                            // Passing the holds is what lets a paused row move at once:
                            // TogglePause releases its hold. See TogglePause.
                            if (TogglePause(_paused, HighlightedRow(frame)?.Id, _ackHolds))
                            {
                                Rebuild(frame);
                                Render(frame, ctx);
                                DrivePointer(frame.AgentViews);
                            }
                            break;

                        case KeyAction.ToggleWide:
                            // Toggle wide mode, which shows/hides the Path and Loc
                            // columns. Re-render immediately so the change is visible.
                            _wideMode = !_wideMode;
                            Render(frame, ctx);
                            break;

                        case KeyAction.ToggleOthers:
                            _showOtherPanes = !_showOtherPanes;
                            Rebuild(frame);
                            Render(frame, ctx);
                            break;

                        case KeyAction.ToggleCompanions:
                            // Inert while the other-panes table is hidden: Rebuild simply
                            // produces the same (empty) row set.
                            _showCompanionPanes = !_showCompanionPanes;
                            Rebuild(frame);
                            Render(frame, ctx);
                            break;

                        case KeyAction.AcknowledgeRow:
                            // Acknowledge the highlighted row if it is DONE, without
                            // switching to it - the keystroke is what clears it, so a pane
                            // that already holds focus is never auto-acknowledged.
                            // Re-render and re-drive the pointer so the green cue clears
                            // immediately.
                            var row = HighlightedRow(frame);
                            if (row is { IsAgent: true } && _monitor.Acknowledge(row.Id))
                            {
                                // Same hold as a jump: the badge flips to IDLE at once,
                                // but the row does not slide out from under the cursor.
                                RecordAckHold(
                                    _ackHolds, row.Agent!, _cfg.AckHoldSeconds,
                                    DateTimeOffset.UtcNow);
                                ApplyOptimisticAck(frame.AgentViews, row.Id);
                                Rebuild(frame);
                                Render(frame, ctx);
                                DrivePointer(frame.AgentViews);
                            }
                            break;

                        case KeyAction.OpenNewWindowPrompt:
                            // Open the name prompt against the session resolved right now.
                            // Nothing is created until submit, and nothing at all happens
                            // when there is no row to derive a session from.
                            if (ResolveTargetSession(frame.Layout.All, _highlightedId) is { } session)
                            {
                                _prompt = LineEditor.Empty;
                                _promptSession = session;
                                Render(frame, ctx);
                            }
                            break;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Console input redirected; ignore key handling.
            }

            if (promptDirty)
                Render(frame, ctx);

            // The sleep is what bounds how soon a keystroke is noticed, so it shortens
            // while the prompt is open - 50ms of latency per character is felt as lag,
            // and the tighter poll only runs while someone is actually typing.
            Thread.Sleep(_prompt is not null ? 8 : 50);
        }
        return false;
    }

    /// <summary>
    /// Feed one keystroke to the open name prompt. Submitting creates the window in the
    /// session captured when the prompt opened and jumps to it; cancelling closes the
    /// prompt without touching tmux at all. Returns true when the caller still owes a
    /// render - the editing case, which is coalesced to one redraw per batch of keys.
    /// Submit and cancel render here, since they are terminal and happen once.
    /// </summary>
    private bool HandlePromptKey(ConsoleKeyInfo key, FrameState frame, LiveDisplayContext ctx)
    {
        var (editor, outcome) = _prompt!.Value.Apply(key);
        var session = _promptSession;

        switch (outcome)
        {
            case LineEditorOutcome.Submit:
                var name = editor.Text.Trim();
                ClosePrompt();
                CreateWindow(session, name, frame, ctx);
                return false;

            case LineEditorOutcome.Cancel:
                ClosePrompt();
                Render(frame, ctx);
                return false;

            default:
                _prompt = editor;
                return true;
        }
    }

    private void ClosePrompt()
    {
        _prompt = null;
        _promptSession = "";
    }

    /// <summary>
    /// Create a window in <paramref name="session"/> and jump to it. The create is
    /// detached, so it moves no client by itself; tmux reports the new window's id, and
    /// the jump then goes through the same focus-only verbs a row switch uses. A blank
    /// name is passed as null, leaving tmux to name the window itself.
    /// </summary>
    private void CreateWindow(string session, string name, FrameState frame, LiveDisplayContext ctx)
    {
        if (CreateAndJump(_tmux, session, name) is { } error)
        {
            frame.Error = error;
            Render(frame, ctx);
            return;
        }

        // Clear a previous failure straight away rather than leaving it up until the next
        // poll overwrites it.
        frame.Error = null;

        // The new window holds focus now, but it has no row until the next poll enumerates
        // it, so no visible row should keep the marker. Clearing it across the target
        // session is the accurate in-frame reading; the poll reconciles the rest.
        ClearFocusMarker(frame.AgentViews, session);
        ClearFocusMarker(frame.OtherPanes, session);

        Rebuild(frame);
        Render(frame, ctx);
    }

    /// <summary>
    /// Create a window in <paramref name="session"/>, then jump to it with focus-only
    /// verbs. A blank name is sent as null so tmux applies its own naming; the name is
    /// never placed anywhere but the name argument. Returns null on success, or a message
    /// describing the failure. Nothing here is a lifecycle change beyond the one create.
    /// </summary>
    internal static string? CreateAndJump(ITmuxClient tmux, string session, string? name)
    {
        var created = tmux.NewWindow(session, string.IsNullOrWhiteSpace(name) ? null : name);
        if (!created.Ok)
            return created.StdErr.Trim() is { Length: > 0 } message
                ? message
                : $"Could not create a window in {session}.";

        tmux.SwitchClient(session);

        // The create was detached, so the new window is not the session's current one -
        // it has to be selected by the id tmux printed. Without an id (a host that does
        // not report one) the session switch alone is as far as the jump can go.
        var windowId = created.StdOut.Trim();
        if (windowId.Length > 0)
            tmux.SelectWindow(windowId);

        return null;
    }

    /// <summary>
    /// Drops the focus marker from every pane of <paramref name="session"/>, used when
    /// focus has moved somewhere no row represents yet.
    /// </summary>
    internal static void ClearFocusMarker(List<TrackedPaneView> ordered, string session)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            if (string.Equals(ordered[i].Pane.SessionName, session, StringComparison.Ordinal))
                ordered[i] = ordered[i] with { Pane = ordered[i].Pane with { WindowActive = false } };
        }
    }

    /// <summary>Same marker clearing, over the non-agent panes.</summary>
    internal static void ClearFocusMarker(List<Pane> panes, string session)
    {
        for (var i = 0; i < panes.Count; i++)
        {
            if (string.Equals(panes[i].SessionName, session, StringComparison.Ordinal))
                panes[i] = panes[i] with { WindowActive = false };
        }
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
        {
            // Freeze the position first, reading the row as it was before the ack, so the
            // pane you just addressed does not fall four ranks under your own keystroke.
            RecordAckHold(_ackHolds, row.Agent!, _cfg.AckHoldSeconds, DateTimeOffset.UtcNow);
            ApplyOptimisticAck(frame.AgentViews, row.Id);
        }

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
    internal static bool TogglePause(
        ISet<string> paused, string? id, IDictionary<string, AckHold>? ackHolds = null)
    {
        if (id is null)
            return false;

        if (!paused.Remove(id))
            paused.Add(id);

        // The one key that releases an acknowledgement hold rather than honouring it:
        // pausing *is* a request to move this row, so holding it back would defeat the
        // key rather than steady the view. Kept here, not in the key handler, so the rule
        // travels with the toggle and can be tested with it.
        ackHolds?.Remove(id);
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

    /// <summary>
    /// Freezes a pane's sort position at the moment it is acknowledged, for
    /// <paramref name="holdSeconds"/>. <paramref name="preAck"/> must be the view as it
    /// was <em>before</em> the acknowledgement, since that is the position being kept.
    /// <para>
    /// A hold for a pane that already has one simply replaces it, which is what a second
    /// acknowledgement should do: it can only happen if the pane re-entered DONE while
    /// the first hold was still live, and the fresh capture is then the accurate one.
    /// </para>
    /// </summary>
    internal static void RecordAckHold(
        IDictionary<string, AckHold> holds,
        TrackedPaneView preAck,
        double holdSeconds,
        DateTimeOffset now)
    {
        if (holdSeconds <= 0)
            return;

        holds[preAck.Pane.Id] = new AckHold(
            Priority(preAck.State), preAck.EnteredAt, now + TimeSpan.FromSeconds(holdSeconds));
    }

    /// <summary>
    /// Drops the holds that have run out, and any belonging to a pane that is no longer
    /// there so the map cannot accumulate entries for closed panes.
    /// <para>
    /// Called once per poll and nowhere else. That is the whole of the "released only by
    /// a poll" rule: no key handler can reach it, so the deadline cannot be satisfied by
    /// a keystroke, and the row's eventual move is never coincident with one.
    /// </para>
    /// </summary>
    internal static void ExpireHolds(
        IDictionary<string, AckHold> holds,
        IEnumerable<string> presentPaneIds,
        DateTimeOffset now)
    {
        if (holds.Count == 0)
            return;

        var present = new HashSet<string>(presentPaneIds, StringComparer.Ordinal);
        foreach (var id in holds.Keys.ToList())
        {
            if (!present.Contains(id) || holds[id].ReleaseAt <= now)
                holds.Remove(id);
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
    /// <para>
    /// A pane under an <see cref="AckHold"/> sorts by its pinned key rather than its
    /// current state, which is what keeps a just-acknowledged row still. Applying holds
    /// is all this does with them - it never expires one, so no rebuild driven by a
    /// keystroke can release a hold. See <see cref="ExpireHolds"/>.
    /// </para>
    /// </summary>
    internal static List<TrackedPaneView> OrderAll(
        IReadOnlyList<TrackedPaneView> panes,
        IReadOnlySet<string> pausedIds,
        IReadOnlyDictionary<string, AckHold>? ackHolds = null) =>
        panes
            .OrderBy(p => pausedIds.Contains(p.Pane.Id) ? 1 : 0)
            .ThenBy(p => Held(ackHolds, p) is { } h ? h.Priority : Priority(p.State))
            .ThenByDescending(p => Held(ackHolds, p) is { } h ? h.EnteredAt : p.EnteredAt)
            .ToList();

    /// <summary>The hold covering this pane, or null when it is free to sort normally.</summary>
    private static AckHold? Held(
        IReadOnlyDictionary<string, AckHold>? ackHolds, TrackedPaneView pane) =>
        ackHolds is not null && ackHolds.TryGetValue(pane.Pane.Id, out var hold) ? hold : null;

    /// <summary>
    /// Attention-first ordering, shared by the live tables and the one-shot output so the
    /// two never disagree. Not the enum's declaration order, which is grouped for
    /// readability and carries no urgency meaning.
    /// </summary>
    internal static int Priority(PaneState state) => state switch
    {
        PaneState.Waiting => 0,
        PaneState.Done => 1,
        // Not a "needs you" state and raises no notification, but a pane whose agent has
        // finished its turn is closer to needing the user than one still mid-turn.
        PaneState.Backgnd => 2,
        PaneState.Working => 3,
        PaneState.Idle => 4,
        PaneState.Unknown => 5,
        PaneState.Dead => 6,
        _ => 7,
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
            "tmux-watch - agent panes  (up/down = select · enter/key = switch · a = ack · n = new window · p = pause · o = others · c = companions · w = wide · q = quit)",
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

        // The prompt rides below the tables inside the same live view, so the tables stay
        // on screen and keep refreshing while the user types.
        if (_prompt is { } editor)
            parts.Add(BuildPromptLine(editor, _promptSession));

        return parts.Count == 1 ? main : new Rows(parts);
    }

    /// <summary>
    /// The name prompt as it appears under the tables: the captured target session (so
    /// the user can see where the window lands), the entered text with the cursor drawn
    /// in place, and the keys that close it.
    /// </summary>
    internal static IRenderable BuildPromptLine(LineEditor editor, string session) =>
        new Markup(
            $"[yellow]New window in[/] [bold]{Markup.Escape(session)}[/]  " +
            $"[grey]name ›[/] {Markup.Escape(editor.Before)}[invert]{Caret(editor)}[/]" +
            $"{Markup.Escape(CaretTail(editor))}   " +
            "[grey]enter = create · esc = cancel[/]");

    /// <summary>The character sitting under the cursor, or a space at end of line.</summary>
    private static string Caret(LineEditor editor) =>
        Markup.Escape(editor.After.Length > 0 ? editor.After[..1] : " ");

    /// <summary>What follows the cursor, once the caret has consumed its character.</summary>
    private static string CaretTail(LineEditor editor) =>
        editor.After.Length > 1 ? editor.After[1..] : "";

    internal static Table BuildRowTable(
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

        var showAgentLabels = rows.Where(row => row.IsAgent)
            .Select(row => row.Pane.AgentId).Distinct(StringComparer.Ordinal).Take(2).Count() > 1;

        foreach (var row in rows)
        {
            var pane = row.Pane;
            var focused = pane.IsFocused;
            var window = string.IsNullOrWhiteSpace(pane.WindowName) ? "—" : pane.WindowName;
            var windowCell = focused
                ? $"[bold underline]{Markup.Escape(window)}[/]"
                : Markup.Escape(window);
            if (showAgentLabels && row.IsAgent)
            {
                var label = pane.AgentId switch
                {
                    "claude" => "CC",
                    "copilot" => "GHCP",
                    "codex" => "CDX",
                    _ => pane.AgentId,
                };
                windowCell = $"[grey]{Markup.Escape(label)}[/] {windowCell}";
            }

            var address = number.TryGetValue(pane.Id, out var index) ? AddressKey(index) : "";
            var addressCell = address.Length == 0 ? "·" : address;
            // The focus marker shares the number column rather than taking a
            // dedicated one, keeping the table narrow for thin splits. The highlight
            // bar sits outside it so the two markers stay visually distinct.
            //
            // Focus is the louder of the two, deliberately. It reports tmux's own state,
            // which moves on the next poll when the user switches panes with tmux's keys -
            // no keystroke this app ever sees. The highlight only reports where this app's
            // cursor sits. Both jump paths (enter and the address keys) end by dragging the
            // cursor onto the row they activate, so the two agree after every in-app switch
            // and diverge only on an outside one; giving the cursor the heavier styling
            // taught the reader to trust it as the current-pane marker, which it is not.
            // Grey matches the table border on purpose: the cursor need only be findable
            // while the user is deliberately arrowing, and the two never share a cell.
            //
            // The focus marker is spaced off the address key; the highlight bar is not.
            // The arrow needs the gap because it reads as pointing *at* the key when
            // flush against it, and the solid bar does not.
            if (focused)
                addressCell = $"[yellow]►[/] {addressCell}";
            if (string.Equals(pane.Id, highlightedId, StringComparison.Ordinal))
                addressCell = $"[grey]▌[/]{addressCell}";

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

            var renderedCells = cells.Select(cell => (IRenderable)new Markup(cell)).ToArray();
            // Labels spend horizontal space, not an extra line per row in a thin split.
            // Keep normal column measurement so Window cannot crowd out State and age.
            if (showAgentLabels)
                renderedCells[2] = new SingleLineCell(renderedCells[2]);
            table.AddRow(renderedCells);
        }

        return table;
    }

    /// <summary>
    /// The state cell. Casing carries urgency: states that need the user are upper case,
    /// quiet ones lower case - so BACKGND, which defers its announcement rather than
    /// making one, renders lower case alongside working and idle.
    /// </summary>
    internal static string StateMarkup(PaneState state, bool attention) => state switch
    {
        PaneState.Waiting => "[yellow]● WAITING[/]",
        PaneState.Done => "[bold green]✓ DONE[/]",
        PaneState.Backgnd => "[cyan]⋯ backgnd[/]",
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
