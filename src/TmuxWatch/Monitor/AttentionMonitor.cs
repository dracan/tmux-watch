using TmuxWatch.Config;
using TmuxWatch.Detection;
using TmuxWatch.Discovery;
using TmuxWatch.Notifications;

namespace TmuxWatch.Monitor;

public sealed record MonitorSnapshot(
    IReadOnlyList<TrackedPaneView> Panes,
    IReadOnlyList<AttentionEvent> Events,
    DateTimeOffset At,
    string? Error);

/// <summary>
/// Drives the discover → capture → classify → state-machine pipeline. Each
/// <see cref="Tick"/> performs one enumeration plus one read-only capture per
/// matched agent pane, classifies it with its agent's profile, updates the per-pane
/// state machine, and emits edge-triggered attention events (once on entering
/// WAITING; optionally on entering IDLE). Holds state across ticks; the polling
/// cadence is owned by the caller.
/// </summary>
public sealed class AttentionMonitor
{
    private readonly PaneDiscovery _discovery;
    private readonly Tmux.ITmuxClient _tmux;
    private readonly INotifier _notifier;
    private readonly TimeProvider _clock;
    private readonly IReadOnlyDictionary<string, PaneClassifier> _classifiers;
    private readonly int _missedEnumerationsBeforeDrop;

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
        _classifiers = cfg.ResolveAgents()
            .ToDictionary(a => a.Id, a => new PaneClassifier(a, cfg.StatusLineCount));
    }

    public MonitorSnapshot Tick()
    {
        var now = _clock.GetUtcNow();
        var discovered = _discovery.DiscoverAgentPanes();

        // On a hard server/CLI failure, keep prior state and report the error so
        // the watcher stays alive (resilience requirement).
        if (!discovered.Ok)
            return new MonitorSnapshot(SnapshotViews(), Array.Empty<AttentionEvent>(), now, discovered.Error);

        var events = new List<AttentionEvent>();
        var seen = new HashSet<string>();

        foreach (var pane in discovered.Panes)
        {
            seen.Add(pane.Id);

            // capture is read-only; a failed capture leaves classification to
            // liveness facts (e.g. Unknown), never crashes the loop.
            var capture = _tmux.CapturePane(pane.Id);
            var classified = _classifiers.TryGetValue(pane.AgentId, out var classifier)
                ? classifier.Classify(capture.Ok ? capture.StdOut : null, pane.Dead)
                : PaneState.Unknown;

            if (!_tracked.TryGetValue(pane.Id, out var tracked))
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
            if (classified == PaneState.Unknown)
                continue;

            // Derive DONE (monitor-only; the classifier never emits it): a classified
            // IDLE whose prior state was WORKING is a completed turn, and a pane already
            // in DONE stays DONE while it keeps classifying IDLE. Any other path into
            // IDLE (fresh pane above, or WAITING to IDLE) remains IDLE.
            var state = classified;
            if (classified == PaneState.Idle &&
                tracked.State is PaneState.Working or PaneState.Done)
                state = PaneState.Done;

            if (tracked.State != state)
            {
                var previous = tracked.State;
                tracked.State = state;
                tracked.EnteredAt = now;
                RaiseIfAttention(tracked, previous, state, events);
            }
        }

        // Debounce disappearance: a pane missing from a single enumeration is retained
        // (state and in-state timer intact) rather than dropped. A transient empty or
        // partial `lsp` result under host load would otherwise wipe every tracked pane
        // and re-add them all as "first sight" next tick - resetting every timer and
        // re-firing the WAITING/DONE chime. Only drop once a pane has been absent for
        // MissedEnumerationsBeforeDrop consecutive ticks (a genuine close).
        foreach (var goneId in _tracked.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            var tracked = _tracked[goneId];
            if (++tracked.MissedEnumerations >= _missedEnumerationsBeforeDrop)
                _tracked.Remove(goneId);
        }

        return new MonitorSnapshot(SnapshotViews(), events, now, null);
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

    private List<TrackedPaneView> SnapshotViews() =>
        _tracked.Values.Select(t => t.ToView()).ToList();

    /// <summary>Display label for an agent id, e.g. "copilot" → "Copilot".</summary>
    private static string AgentLabel(string agentId) =>
        string.IsNullOrEmpty(agentId)
            ? "Agent"
            : char.ToUpperInvariant(agentId[0]) + agentId[1..];
}
