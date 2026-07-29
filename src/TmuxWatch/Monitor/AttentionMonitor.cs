using TmuxWatch.Config;
using TmuxWatch.Detection;
using TmuxWatch.Discovery;
using TmuxWatch.Notifications;
using TmuxWatch.Tmux;

namespace TmuxWatch.Monitor;

/// <param name="OtherPanes">
/// The non-agent inventory for this tick, carried through untouched so one poll serves
/// both the attention view and the pane list. These panes are inert: they are never
/// captured, classified, given a <see cref="TrackedPane"/> entry, notified on, or allowed
/// to influence the pointer aggregate. Empty when the enumeration failed.
/// </param>
public sealed record MonitorSnapshot(
    IReadOnlyList<TrackedPaneView> Panes,
    IReadOnlyList<AttentionEvent> Events,
    DateTimeOffset At,
    string? Error,
    IReadOnlyList<Pane> OtherPanes);

/// <summary>
/// Drives the discover → capture → classify → state-machine pipeline. Each
/// <see cref="Tick"/> performs one enumeration plus one read-only capture per
/// matched agent pane, classifies it with its agent's profile, updates the per-pane
/// state machine, and emits edge-triggered attention events (once on entering
/// WAITING, once on entering DONE). Holds state across ticks; the polling
/// cadence is owned by the caller.
///
/// BACKGND is quiet: entering it raises nothing. A turn that ends into a live background
/// shell has its DONE promotion - and so its chime - deferred until the shell exits or
/// the grace period expires. See <c>Promote</c>.
/// </summary>
public sealed class AttentionMonitor
{
    private readonly PaneDiscovery _discovery;
    private readonly Tmux.ITmuxClient _tmux;
    private readonly INotifier _notifier;
    private readonly TimeProvider _clock;
    private readonly IReadOnlyDictionary<string, PaneClassifier> _classifiers;
    private readonly int _missedEnumerationsBeforeDrop;
    private readonly int _unknownCapturesBeforeStale;
    private readonly TimeSpan _backgroundGrace;

    private readonly Dictionary<string, TrackedPane> _tracked = new();

    public AttentionMonitor(
        PaneDiscovery discovery,
        Tmux.ITmuxClient tmux,
        WatchConfig cfg,
        INotifier notifier,
        TimeProvider? clock = null)
    {
        _discovery = discovery;
        _tmux = tmux;
        _notifier = notifier;
        _clock = clock ?? TimeProvider.System;
        _missedEnumerationsBeforeDrop = Math.Max(1, cfg.MissedEnumerationsBeforeDrop);
        _unknownCapturesBeforeStale = Math.Max(1, cfg.UnknownCapturesBeforeStale);
        _backgroundGrace = TimeSpan.FromSeconds(Math.Max(0, cfg.BackgroundGraceSeconds));
        _classifiers = cfg.ResolveAgents()
            .ToDictionary(a => a.Id, a => new PaneClassifier(a, cfg.StatusLineCount));
    }

    public MonitorSnapshot Tick()
    {
        var now = _clock.GetUtcNow();
        var discovered = _discovery.DiscoverPanes();

        // On a hard server/CLI failure, keep prior state and report the error so
        // the watcher stays alive (resilience requirement). The inventory is view-only,
        // so unlike tracked pane state there is nothing worth retaining across a failed
        // enumeration - it goes empty for this tick.
        if (!discovered.Ok)
            return new MonitorSnapshot(
                SnapshotViews(), Array.Empty<AttentionEvent>(), now, discovered.Error, Array.Empty<Pane>());

        var events = new List<AttentionEvent>();
        var seen = new HashSet<string>();

        // Only the matched agent panes enter the pipeline below; discovered.OtherPanes is
        // never captured, classified, or tracked - it is passed straight to the snapshot.
        foreach (var pane in discovered.AgentPanes)
        {
            seen.Add(pane.Id);

            // capture is read-only; a failed capture leaves classification to
            // liveness facts (e.g. Unknown), never crashes the loop.
            var capture = _tmux.CapturePane(pane.Id);
            var classified = _classifiers.TryGetValue(pane.AgentId, out var classifier)
                ? classifier.Classify(capture.Ok ? capture.StdOut : null, pane.Dead)
                : PaneState.Unknown;

            // A pane id whose root process pid changed is a different pane wearing a
            // reused id (e.g. the tmux server restarted inside the absence-debounce
            // window and ids restarted at %0). Its tracked history belongs to the old
            // pane - treating the newcomer as a continuation could stitch a stale
            // WORKING into a false DONE - so it re-enters as first sight below.
            if (!_tracked.TryGetValue(pane.Id, out var tracked) ||
                tracked.Pane.Pid != pane.Pid)
            {
                // First sight has no history, so a pane that is already idle cannot be
                // a just-finished turn - it stays IDLE (never promoted to DONE).
                tracked = new TrackedPane { Pane = pane, State = classified, EnteredAt = now };
                _tracked[pane.Id] = tracked;
                RaiseIfAttention(tracked, PaneState.Unknown, classified, events);
                continue;
            }

            tracked.Pane = pane;
            tracked.MissedEnumerations = 0;   // seen this tick: reset the absence debounce

            // A capture that yields no recognizable signal (a failed, empty, or
            // mid-redraw capture under host load) is "no new information", not a real
            // transition. Hold the pane's established state and in-state timer rather
            // than flapping it to Unknown - which would reset every row's timer and, on
            // recovery, re-fire the WAITING/DONE chime for panes that never changed. It
            // would also break DONE detection by erasing the prior WORKING fact. DEAD
            // comes from the liveness flag (never Unknown), so it still propagates.
            // The hold is bounded: a pane that classifies Unknown for
            // UnknownCapturesBeforeStale consecutive ticks has genuinely lost
            // classification (token drift after an agent upgrade, copy-mode, an
            // unrecognized screen) and falls through to surface Unknown, rather than
            // freezing a stale state forever.
            if (classified == PaneState.Unknown &&
                ++tracked.ConsecutiveUnknowns < _unknownCapturesBeforeStale)
                continue;
            if (classified != PaneState.Unknown)
                tracked.ConsecutiveUnknowns = 0;

            var state = Promote(tracked, classified, now);

            if (tracked.State != state)
            {
                var previous = tracked.State;
                tracked.State = state;
                tracked.EnteredAt = now;
                RaiseIfAttention(tracked, previous, state, events);
            }
        }

        // Debounce disappearance: a pane missing from a single enumeration is retained
        // (state and in-state timer intact) rather than dropped, so a transient empty
        // or partial `lsp` result under host load does not wipe every tracked pane and
        // re-add them all as "first sight" next tick - resetting every timer and
        // re-firing the WAITING/DONE chime. Retention is internal only: SnapshotViews
        // hides absent panes, so a genuinely closed pane never lingers as a phantom
        // "needs you" row (with a dead jump target and an engaged pointer cue) while
        // the debounce runs out. Drop the entry once a pane has been absent for
        // MissedEnumerationsBeforeDrop consecutive ticks (a genuine close).
        foreach (var goneId in _tracked.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            var tracked = _tracked[goneId];
            if (++tracked.MissedEnumerations >= _missedEnumerationsBeforeDrop)
                _tracked.Remove(goneId);
        }

        return new MonitorSnapshot(SnapshotViews(), events, now, null, discovered.OtherPanes);
    }

    /// <summary>
    /// Derive the tracked state from this tick's classification plus the pane's history.
    /// DONE is monitor-only (the classifier never emits it) and is reached by two routes:
    ///
    /// - directly, when a classified IDLE follows WORKING - a turn that ended with nothing
    ///   still running; and
    /// - deferred, when a turn ended into a live background task (WORKING → BACKGND).
    ///   That completion is held silent and released either when the shell exits (BACKGND
    ///   → IDLE) or when the grace period expires, whichever comes first.
    ///
    /// Any other path into IDLE (a fresh pane, or WAITING → IDLE) remains IDLE, and any
    /// path out of BACKGND without <see cref="TrackedPane.CompletionPending"/> announces
    /// nothing - which is what keeps first sight silent.
    /// </summary>
    private PaneState Promote(TrackedPane tracked, PaneState classified, DateTimeOffset now)
    {
        switch (classified)
        {
            case PaneState.Backgnd:
                if (tracked.State != PaneState.Backgnd)
                    tracked.BackgndSince = now;

                // A turn that ends into a live shell defers its announcement rather than
                // firing it. Only WORKING arms this: entering BACKGND from IDLE (a shell
                // the user started work for earlier) completed no turn we saw.
                if (tracked.State == PaneState.Working)
                    tracked.CompletionPending = true;

                // Once promoted, DONE persists across BACKGND as well as IDLE. Without
                // this a grace-period promotion would drop straight back to BACKGND on
                // the very next poll and undo its own announcement.
                if (tracked.State == PaneState.Done)
                    return PaneState.Done;

                // Grace-period release: the shell has outlived the deferral, so announce
                // rather than let a dev server swallow the chime forever.
                if (tracked.CompletionPending &&
                    now - (tracked.BackgndSince ?? now) >= _backgroundGrace)
                {
                    tracked.CompletionPending = false;
                    return PaneState.Done;
                }

                return PaneState.Backgnd;

            case PaneState.Idle:
                // Direct completion, a DONE pane still sitting idle, or the deferred
                // completion released by the shell exiting.
                if (tracked.State is PaneState.Working or PaneState.Done ||
                    (tracked.State == PaneState.Backgnd && tracked.CompletionPending))
                {
                    tracked.CompletionPending = false;
                    tracked.BackgndSince = null;
                    return PaneState.Done;
                }

                tracked.BackgndSince = null;
                return PaneState.Idle;

            case PaneState.Working:
                // A new turn has begun; it will arm its own completion when it ends.
                tracked.CompletionPending = false;
                tracked.BackgndSince = null;
                return PaneState.Working;

            case PaneState.Waiting:
                // WAITING raises its own attention event, so any held completion is
                // discarded rather than queued up to fire separately afterwards.
                tracked.CompletionPending = false;
                tracked.BackgndSince = null;
                return PaneState.Waiting;

            default:
                return classified;
        }
    }

    /// <summary>
    /// Acknowledge a DONE pane, returning it to IDLE and clearing its outstanding
    /// attention so it drops out of the DONE cue (notification already fired; pointer
    /// re-evaluated by the caller). Driven only by an explicit user action, so a pane
    /// that merely holds focus is never auto-acknowledged. The pane will not re-enter
    /// DONE until it next completes another WORKING to IDLE cycle. Returns true if a
    /// DONE pane was actually cleared.
    /// </summary>
    public bool Acknowledge(string paneId)
    {
        if (_tracked.TryGetValue(paneId, out var tracked) && tracked.State == PaneState.Done)
        {
            tracked.State = PaneState.Idle;
            tracked.AttentionOutstanding = false;
            tracked.EnteredAt = _clock.GetUtcNow();
            return true;
        }
        return false;
    }

    private void RaiseIfAttention(TrackedPane tracked, PaneState previous, PaneState current, List<AttentionEvent> events)
    {
        // WAITING (blocked) and DONE (finished, your move) are both "needs you" states:
        // each notifies once on entry with the same cue and sets the outstanding flag.
        if (current == PaneState.Waiting && previous != PaneState.Waiting)
        {
            tracked.AttentionOutstanding = true;
            events.Add(new AttentionEvent(tracked.Pane, AttentionKind.EnteredWaiting));
            _notifier.Notify($"{AgentLabel(tracked.Pane.AgentId)} needs you", $"{tracked.Pane.Location} is waiting for input");
        }
        else if (current == PaneState.Done && previous != PaneState.Done)
        {
            tracked.AttentionOutstanding = true;
            events.Add(new AttentionEvent(tracked.Pane, AttentionKind.EnteredDone));
            _notifier.Notify($"{AgentLabel(tracked.Pane.AgentId)} finished", $"{tracked.Pane.Location} finished its turn");
        }
        else if (current != PaneState.Waiting && current != PaneState.Done)
        {
            // Any other transition (into WORKING, IDLE, DEAD, ...) clears the flag.
            tracked.AttentionOutstanding = false;
        }
    }

    // Panes mid-absence-debounce are tracked but not shown: their state is kept in
    // case the enumeration blip recovers, but rendering (and the pointer cue and jump
    // actions derived from the views) must not offer a pane that is not currently
    // listed by tmux.
    private List<TrackedPaneView> SnapshotViews() =>
        _tracked.Values
            .Where(t => t.MissedEnumerations == 0)
            .Select(t => t.ToView())
            .ToList();

    /// <summary>Display label for an agent id, e.g. "copilot" → "Copilot".</summary>
    private static string AgentLabel(string agentId) =>
        string.IsNullOrEmpty(agentId)
            ? "Agent"
            : char.ToUpperInvariant(agentId[0]) + agentId[1..];
}
