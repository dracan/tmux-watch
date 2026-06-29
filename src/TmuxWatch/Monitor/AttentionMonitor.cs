using TmuxWatch.Config;
using TmuxWatch.Detection;
using TmuxWatch.Discovery;
using TmuxWatch.Notifications;
using TmuxWatch.Pointer;

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
    private readonly WatchConfig _cfg;
    private readonly INotifier _notifier;
    private readonly IPointerSignal _pointer;
    private readonly TimeProvider _clock;
    private readonly IReadOnlyDictionary<string, PaneClassifier> _classifiers;

    private readonly Dictionary<string, TrackedPane> _tracked = new();

    public AttentionMonitor(
        PaneDiscovery discovery,
        Tmux.ITmuxClient tmux,
        WatchConfig cfg,
        INotifier notifier,
        TimeProvider? clock = null,
        IPointerSignal? pointer = null)
    {
        _discovery = discovery;
        _tmux = tmux;
        _cfg = cfg;
        _notifier = notifier;
        _pointer = pointer ?? new NullPointerSignal();
        _clock = clock ?? TimeProvider.System;
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
            var state = _classifiers.TryGetValue(pane.AgentId, out var classifier)
                ? classifier.Classify(capture.Ok ? capture.StdOut : null, pane.Dead)
                : PaneState.Unknown;

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

        // Level-triggered pointer cue: red while any pane has outstanding attention,
        // restored once none do. De-dup lives in the backend, so calling every tick is
        // cheap. Independent of the edge-triggered bell above.
        _pointer.SetWaiting(_tracked.Values.Any(t => t.AttentionOutstanding));

        return new MonitorSnapshot(SnapshotViews(), events, now, null);
    }

    private void RaiseIfAttention(TrackedPane tracked, PaneState previous, PaneState current, List<AttentionEvent> events)
    {
        if (current == PaneState.Waiting && previous != PaneState.Waiting)
        {
            tracked.AttentionOutstanding = true;
            var evt = new AttentionEvent(tracked.Pane, AttentionKind.EnteredWaiting);
            events.Add(evt);
            _notifier.Notify($"{AgentLabel(tracked.Pane.AgentId)} needs you", $"{tracked.Pane.Location} is waiting for input");
        }
        else if (current == PaneState.Idle && previous != PaneState.Idle)
        {
            // Leaving WAITING clears the outstanding flag.
            tracked.AttentionOutstanding = false;
            if (_cfg.NotifyOnIdle)
            {
                events.Add(new AttentionEvent(tracked.Pane, AttentionKind.EnteredIdle));
                _notifier.Notify($"{AgentLabel(tracked.Pane.AgentId)} idle", $"{tracked.Pane.Location} finished its turn");
            }
        }
        else if (current != PaneState.Waiting)
        {
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
