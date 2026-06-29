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
/// Copilot pane, updates the per-pane state machine, and emits edge-triggered
/// attention events (once on entering WAITING; optionally on entering IDLE).
/// Holds state across ticks; the polling cadence is owned by the caller.
/// </summary>
public sealed class AttentionMonitor
{
    private readonly PaneDiscovery _discovery;
    private readonly PaneClassifier _classifier;
    private readonly Tmux.ITmuxClient _tmux;
    private readonly WatchConfig _cfg;
    private readonly INotifier _notifier;
    private readonly TimeProvider _clock;

    private readonly Dictionary<string, TrackedPane> _tracked = new();

    public AttentionMonitor(
        PaneDiscovery discovery,
        PaneClassifier classifier,
        Tmux.ITmuxClient tmux,
        WatchConfig cfg,
        INotifier notifier,
        TimeProvider? clock = null)
    {
        _discovery = discovery;
        _classifier = classifier;
        _tmux = tmux;
        _cfg = cfg;
        _notifier = notifier;
        _clock = clock ?? TimeProvider.System;
    }

    public MonitorSnapshot Tick()
    {
        var now = _clock.GetUtcNow();
        var discovered = _discovery.DiscoverCopilotPanes();

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
            var commandIsCopilot = _discovery.CommandIsCopilot(pane.Command);
            var state = _classifier.Classify(
                capture.Ok ? capture.StdOut : null, commandIsCopilot, pane.Dead);

            if (!_tracked.TryGetValue(pane.Id, out var tracked))
            {
                tracked = new TrackedPane { Pane = pane, State = state, EnteredAt = now };
                _tracked[pane.Id] = tracked;
                RaiseIfAttention(tracked, PaneState.Unknown, state, events);
                continue;
            }

            tracked.Pane = pane;
            if (tracked.State != state)
            {
                var previous = tracked.State;
                tracked.State = state;
                tracked.EnteredAt = now;
                RaiseIfAttention(tracked, previous, state, events);
            }
        }

        // Drop panes that disappeared (resilience: pane vanished mid-watch).
        foreach (var goneId in _tracked.Keys.Where(k => !seen.Contains(k)).ToList())
            _tracked.Remove(goneId);

        return new MonitorSnapshot(SnapshotViews(), events, now, null);
    }

    private void RaiseIfAttention(TrackedPane tracked, PaneState previous, PaneState current, List<AttentionEvent> events)
    {
        if (current == PaneState.Waiting && previous != PaneState.Waiting)
        {
            tracked.AttentionOutstanding = true;
            var evt = new AttentionEvent(tracked.Pane, AttentionKind.EnteredWaiting);
            events.Add(evt);
            _notifier.Notify("Copilot needs you", $"{tracked.Pane.Location} is waiting for input");
        }
        else if (current == PaneState.Idle && previous != PaneState.Idle)
        {
            // Leaving WAITING clears the outstanding flag.
            tracked.AttentionOutstanding = false;
            if (_cfg.NotifyOnIdle)
            {
                events.Add(new AttentionEvent(tracked.Pane, AttentionKind.EnteredIdle));
                _notifier.Notify("Copilot idle", $"{tracked.Pane.Location} finished its turn");
            }
        }
        else if (current != PaneState.Waiting)
        {
            tracked.AttentionOutstanding = false;
        }
    }

    private List<TrackedPaneView> SnapshotViews() =>
        _tracked.Values.Select(t => t.ToView()).ToList();
}
