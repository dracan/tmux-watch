using TmuxWatch.Tmux;
using TmuxWatch.Workspace;

namespace TmuxWatch.Tests;

public class WorkspaceTests
{
    internal static string Layout(string body) => $"{PaneLayout.Checksum(body):x4},{body}";
    internal static WorkspaceSnapshot Example(string directory = "/work") => new(1, DateTimeOffset.UtcNow, "unix",
        [new("work", [new("@1", 0, "build | watch", Layout("120x30,0,0,1"), true, false,
            [new("%1", 0, directory, "copilot", true, true, false, null, "unknown")])])]);

    [Fact]
    public void Json_round_trip_preserves_metadata_and_unknown_identity()
    {
        var snapshot = Example("/work/pipe | space");
        var restored = WorkspaceSnapshot.Parse(snapshot.ToJson());
        var pane = restored.Sessions.Single().Windows.Single().Panes.Single();
        Assert.Equal("/work/pipe | space", pane.Directory);
        Assert.True(pane.Paused);
        Assert.Null(pane.ConversationId);
        Assert.Contains("conversation unknown", ResumeGuidance.For(pane));
    }

    [Fact]
    public void Nested_layout_is_validated_and_pane_ids_are_remapped()
    {
        var source = PaneLayout.Parse(Layout("120x30,0,0{59x30,0,0,1,60x30,60,0[60x14,60,0,2,60x15,60,15,3]}"));
        var mapped = PaneLayout.Parse(source.Encode(new Dictionary<int, int> { [1] = 101, [2] = 200, [3] = 500 }));
        Assert.Equal(source.Shape, mapped.Shape);
        Assert.Equal(new int?[] { 101, 200, 500 }, mapped.Leaves.Select(l => l.PaneId));
    }

    [Theory]
    [InlineData("120x30,0,0{50x30,0,0,1,60x30,60,0,2}")]
    [InlineData("120x30,0,0{59x30,0,0,1,60x30,60,0,1}")]
    [InlineData("120x30,0,0,1;send-keys")]
    public void Malformed_layouts_are_rejected_even_with_valid_checksum(string body) =>
        Assert.Throws<InvalidDataException>(() => PaneLayout.Parse(Layout(body)));

    [Fact]
    public void Preflight_collects_missing_directories_and_rejects_unknown_schema()
    {
        var errors = WorkspaceService.Validate(Example() with { Version = 42 }, _ => false);
        Assert.Contains(errors, e => e.Contains("version"));
        Assert.Contains(errors, e => e.Contains("directory"));
    }

    [Fact]
    public void Pane_coverage_is_checked_before_creation()
    {
        var snapshot = Example();
        var session = snapshot.Sessions[0];
        session.Windows[0] = session.Windows[0] with { Layout = Layout("120x30,0,0,99") };
        Assert.Contains(WorkspaceService.Validate(snapshot, _ => true), e => e.Contains("cover"));
    }

    [Fact]
    public void Linked_windows_are_detected_even_when_backend_ids_differ()
    {
        var snapshot = Example();
        snapshot.Sessions[0].Windows[0] = snapshot.Sessions[0].Windows[0] with { Linked = true };
        Assert.Contains(WorkspaceService.Validate(snapshot, _ => true), e => e.Contains("Linked window"));
    }

    [Fact]
    public void Unknown_json_properties_cannot_smuggle_commands() =>
        Assert.Throws<System.Text.Json.JsonException>(() => WorkspaceSnapshot.Parse(Example().ToJson().Replace("\"version\": 1", "\"version\": 1, \"command\": \"touch something\"")));

    [Theory]
    [InlineData("#{pane_current_path}")]
    [InlineData("#(touch sentinel)")]
    [InlineData("work;send-keys")]
    [InlineData("work\nother")]
    public void Imported_format_and_command_syntax_is_rejected(string name)
    {
        var snapshot = Example();
        snapshot.Sessions[0] = snapshot.Sessions[0] with { Name = name };
        Assert.NotEmpty(WorkspaceService.Validate(snapshot, _ => true));
    }

    [Fact]
    public void Pause_records_are_shared_and_do_not_follow_process_reuse()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tw-pause-" + Guid.NewGuid().ToString("N"));
        try
        {
            long lifetime = 100;
            var first = new PauseStore(directory, _ => lifetime);
            var second = new PauseStore(directory, _ => lifetime);
            var pane = new Pane("%1", "work", 0, 0, "shell", false, Pid: 200, ServerPid: 100);
            first.Set(pane, true);
            Assert.True(second.IsPaused(pane));
            var other = pane with { Id = "%2", Pid = 201 };
            second.Set(other, true);
            first.Set(pane, false);
            Assert.True(first.IsPaused(other));
            Assert.False(second.IsPaused(pane));
            first.Set(pane, true);
            lifetime = 101;
            Assert.False(second.IsPaused(pane));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void Same_raw_pane_id_in_two_psmux_sessions_has_distinct_row_identity()
    {
        var first = new Pane("%1", "one", 0, 0, "shell", false, Pid: 10, ServerPid: 100);
        var second = first with { SessionName = "two", Pid = 20, ServerPid = 200 };
        Assert.NotEqual(first.Key, second.Key);
        var rows = TmuxWatch.Tui.WatcherApp.BuildLayout([], [first, second], new HashSet<string> { first.Key }, true, true);
        Assert.Single(rows.Paused);
        Assert.Equal("one", rows.Paused[0].Pane.SessionName);
        Assert.Single(rows.Other);
        Assert.Equal("two", rows.Other[0].Pane.SessionName);
    }

    [Fact]
    public void Resume_guidance_uses_only_known_command_and_uuid()
    {
        var pane = Example().Sessions[0].Windows[0].Panes[0] with { ConversationId = "4f761172-c8b8-4d9d-b9ca-2436ed76c417" };
        Assert.Equal("copilot --resume=4f761172-c8b8-4d9d-b9ca-2436ed76c417", ResumeGuidance.For(pane));
        Assert.Contains("unknown", ResumeGuidance.For(pane with { ConversationId = "id; command" }));
    }
}

public class WorkspaceFailureTests
{
    private sealed class MemoryClipboard : IWorkspaceClipboard
    {
        public string? Text;
        public string? Copy(string text) { Text = text; return null; }
        public string Read() => Text!;
    }

    [Fact]
    public void Export_warns_about_restore_blockers_before_reboot_without_losing_snapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "tw-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var runner = new TmuxRunner(args =>
            {
                var text = args[0] == "lsp" ? "$0|@1|%1|0|0|500|0|1|1|work" : args[^1] switch
                {
                    "#{session_name}" => "work",
                    "#{window_name}" => "zoomed",
                    "#{pane_current_path}" => root,
                    "#{pane_current_command}" => "claude",
                    "#{pid}|#{session_created}" => "777|123",
                    "#{window_layout}" => WorkspaceTests.Layout("120x30,0,0,1"),
                    "#{window_zoomed_flag}" => "1",
                    "#{window_linked}" => "0",
                    _ => throw new InvalidOperationException("Unexpected read"),
                };
                return new(true, 0, text, "");
            });
            var clipboard = new MemoryClipboard();
            var service = new WorkspaceService(runner, new(), new PauseStore(Path.Combine(root, "paused"), _ => 42), clipboard);
            var result = service.Export(Path.Combine(root, "snapshot.json"));
            Assert.True(result.Success, result.Message);
            Assert.Contains("Restore preflight warnings", result.Message);
            Assert.Contains("Unzoom", result.Message);
            Assert.Equal(File.ReadAllText(result.Path!), clipboard.Text);
            Assert.True(WorkspaceSnapshot.Parse(clipboard.Text!).Sessions[0].Windows[0].Zoomed);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Missing_directory_preflight_issues_no_lifecycle_command()
    {
        var calls = new List<string>();
        var runner = new TmuxRunner(args =>
        {
            calls.Add(args[0]);
            return new(true, 0, "", "");
        });
        var service = new WorkspaceService(runner, new(), new PauseStore());
        var result = service.ImportJson(WorkspaceTests.Example(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).ToJson());
        Assert.False(result.Success);
        Assert.Contains("Nothing created", result.Message);
        Assert.Equal(new[] { "list-sessions" }, calls);
    }

    [Fact]
    public void Late_failure_reports_created_window_without_deleting_it()
    {
        var calls = new List<string>();
        var runner = new TmuxRunner(args =>
        {
            calls.Add(args[0]);
            return args[0] switch
            {
                "list-sessions" => new(true, 0, "", ""),
                "new-session" => new(true, 0, "@1|%5\n", ""),
                "display-message" => new(true, 0, "0\n", ""),
                "select-layout" => new(true, 1, "", "synthetic layout failure"),
                _ => throw new InvalidOperationException("Unexpected operation: " + args[0]),
            };
        });
        var service = new WorkspaceService(runner, new(), new PauseStore());
        var result = service.ImportJson(WorkspaceTests.Example(Path.GetTempPath()).ToJson());
        try
        {
            Assert.False(result.Success);
            Assert.Contains("Created work:0", result.Message);
            Assert.Contains("Partial restore retained", result.Message);
            Assert.Contains("synthetic layout failure", result.Message);
            Assert.DoesNotContain(calls, c => c.StartsWith("kill"));
            Assert.NotNull(result.Path);
            Assert.Equal(result.Message, File.ReadAllText(result.Path!));
        }
        finally { if (result.Path is not null) File.Delete(result.Path); }
    }

    [Theory]
    [InlineData("send-keys")]
    [InlineData("run-shell")]
    [InlineData("kill-window")]
    [InlineData("respawn-pane")]
    public void Restore_extensions_keep_input_and_destruction_rejected(string verb)
    {
        var runner = new TmuxRunner(_ => throw new Exception("Must not execute"));
        Assert.Throws<InvalidOperationException>(() => runner.Run(verb));
    }
}
