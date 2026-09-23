using System.Text.Json;
using System.Text.Json.Serialization;

namespace TmuxWatch.Workspace;

public sealed record WorkspaceSnapshot(
    int Version, DateTimeOffset CreatedAt, string Host, List<SnapshotSession> Sessions)
{
    public const int CurrentVersion = 1;
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 64,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json) + "\n";

    public static WorkspaceSnapshot Parse(string text)
    {
        if (text.Length > 16 * 1024 * 1024)
            throw new InvalidDataException("Snapshot exceeds the 16 MiB limit.");
        return JsonSerializer.Deserialize<WorkspaceSnapshot>(text, Json)
            ?? throw new InvalidDataException("Snapshot is empty.");
    }
}

public sealed record SnapshotSession(string Name, List<SnapshotWindow> Windows);
public sealed record SnapshotWindow(
    string SourceId, int Index, string Name, string Layout, bool Active,
    bool Zoomed, List<SnapshotPane> Panes, bool Linked = false);
public sealed record SnapshotPane(
    string SourceId, int Index, string Directory, string Agent, bool Paused,
    bool Active, bool Dead, string? ConversationId, string? IdentityNote);

public static class ResumeGuidance
{
    public static string For(SnapshotPane pane)
    {
        if (string.IsNullOrEmpty(pane.Agent)) return "Shell; no agent to resume.";
        if (!Guid.TryParseExact(pane.ConversationId, "D", out var id))
            return $"{pane.Agent}: conversation unknown. {pane.IdentityNote}".Trim();
        return pane.Agent switch
        {
            "copilot" => $"copilot --resume={id:D}",
            "claude" => $"claude --resume {id:D}",
            "codex" => $"codex resume {id:D}",
            _ => $"{pane.Agent}: resume conversation {id:D} manually.",
        };
    }
}
