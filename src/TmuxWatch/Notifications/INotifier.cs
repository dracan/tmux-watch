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
