using TmuxWatch.Config;

namespace TmuxWatch.Notifications;

public interface INotifier
{
    /// <summary>Alert the user that a pane needs attention. Title/body are short.</summary>
    void Notify(string title, string body);
}

/// <summary>Writes a terminal bell to the watcher's own stdout. Never touches panes.</summary>
public sealed class BellNotifier : INotifier
{
    public void Notify(string title, string body) => Console.Out.Write('\a');
}

public sealed class NullNotifier : INotifier
{
    public void Notify(string title, string body) { }
}

public static class NotifierFactory
{
    public static INotifier Create(WatchConfig cfg) => cfg.NotificationChannel.ToLowerInvariant() switch
    {
        "none" => new NullNotifier(),
        _ => new BellNotifier(),
    };
}

/// <summary>
/// Queues notifications raised on the polling thread and replays them when
/// <see cref="Flush"/> is called on the render thread. The bell is written to the same
/// buffered stdout the live view repaints through, and that writer is not thread-safe,
/// so the poller must never write to it directly.
/// </summary>
public sealed class DeferredNotifier(INotifier inner) : INotifier
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string Title, string Body)> _pending = new();

    public void Notify(string title, string body) => _pending.Enqueue((title, body));

    public void Flush()
    {
        while (_pending.TryDequeue(out var n))
            inner.Notify(n.Title, n.Body);
    }
}
