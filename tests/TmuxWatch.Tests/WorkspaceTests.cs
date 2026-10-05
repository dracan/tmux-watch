using TmuxWatch.Tmux;
using TmuxWatch.Tui;
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
    public void Latest_snapshot_is_chosen_by_the_timestamp_in_its_name()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tw-latest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Assert.Null(WorkspaceFiles.LatestSnapshot(directory));
            var older = Path.Combine(directory, "snapshots-20261003-135439-0219760-aaaa.json");
            var newer = Path.Combine(directory, "snapshots-20261004-090000-0000000-bbbb.json");
            File.WriteAllText(newer, "{}");
            File.WriteAllText(older, "{}");
            // A later write time does not outrank a later export.
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(1));
            File.WriteAllText(Path.Combine(directory, "snapshots-later.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "snapshots-20261004-090000-0000000-bbbb.json.1a2b.tmp"), "{}");
            var latest = WorkspaceFiles.LatestSnapshot(directory);
            Assert.Equal(newer, latest!.Path);
            Assert.Equal(new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero), latest.SavedAt);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Import_prompt_defaults_to_the_latest_export_and_keeps_clipboard_reachable()
    {
        var latest = new SavedSnapshot("/data/snapshots-20261004-090000-0000000-bbbb.json", DateTimeOffset.UtcNow);
        Assert.Equal((WatcherApp.ImportSource.File, latest.Path), WatcherApp.ResolveImport("", latest));
        Assert.Equal(WatcherApp.ImportSource.Clipboard, WatcherApp.ResolveImport("", null).Source);
        Assert.Equal(WatcherApp.ImportSource.Clipboard, WatcherApp.ResolveImport("Clipboard", latest).Source);
        Assert.Equal(WatcherApp.ImportSource.Json, WatcherApp.ResolveImport("{\"version\": 1}", latest).Source);
        Assert.Equal((WatcherApp.ImportSource.File, "/tmp/w.json"), WatcherApp.ResolveImport("/tmp/w.json", latest));
    }

    [Fact]
    public void Import_latest_without_a_saved_export_creates_nothing()
    {
        var calls = new List<string>();
        var service = new WorkspaceService(new TmuxRunner(args => { calls.Add(args[0]); return new(true, 0, "", ""); }),
            new(), new PauseStore(Path.Combine(Path.GetTempPath(), "tw-none-" + Guid.NewGuid().ToString("N")), _ => 42));
        var result = service.ImportLatest(Path.Combine(Path.GetTempPath(), "tw-missing-" + Guid.NewGuid().ToString("N")));
        Assert.False(result.Success);
        Assert.Contains("No saved export", result.Message);
        Assert.Empty(calls);
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

    [Theory]
    [InlineData("- a markdown bullet", "starts \"- a markdown bullet\"")]
    [InlineData("  \n", "it is empty")]
    public void Clipboard_without_a_snapshot_names_its_source_and_creates_nothing(string text, string detail)
    {
        var calls = new List<string>();
        var service = new WorkspaceService(new TmuxRunner(args => { calls.Add(args[0]); return new(true, 0, "", ""); }),
            new(), new PauseStore(), new MemoryClipboard { Text = text });
        var result = service.ImportClipboard();
        Assert.False(result.Success);
        Assert.StartsWith("Nothing created: The clipboard does not hold a workspace snapshot", result.Message);
        Assert.Contains(detail, result.Message);
        Assert.Null(result.Path);
        Assert.Empty(calls);
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
    [InlineData(1, 3)]
    [InlineData(0, 2)]
    public void Psmux_import_compacts_window_indexes_in_saved_order(int first, int second)
    {
        var root = Path.Combine(Path.GetTempPath(), "tw-psmux-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var calls = new List<string[]>();
        var windows = 0;
        string Pane(string target) => "%" + (5 + WorkspaceBackend.Number(target.Split(':')[1].Split('.')[0]));
        var runner = new TmuxRunner(args =>
        {
            calls.Add(args);
            var text = args[0] switch
            {
                "list-sessions" => "",
                "new-session" or "new-window" => $"@{++windows}|%{4 + windows}",
                "lsp" => Pane(args[2]) + "|0",
                "display-message" => args[^1] switch
                {
                    "#{window_index}" => "0",
                    "#{window_layout}" => WorkspaceTests.Layout("120x30,0,0," + Pane(args[3])[1..]),
                    "#{pane_current_path}" => root,
                    "#{pane_index}" => "0",
                    _ => "100",
                },
                "select-layout" or "select-pane" or "select-window" => "",
                _ => throw new InvalidOperationException("Unexpected operation: " + args[0]),
            };
            return new(true, 0, text, "");
        });
        SnapshotWindow Window(string id, int index, string name, bool active) => new(id, index, name,
            WorkspaceTests.Layout("120x30,0,0,1"), active, false, [new("%1", 0, root, "", false, true, false, null, null)]);
        var snapshot = new WorkspaceSnapshot(1, DateTimeOffset.UtcNow, "windows",
            [new("work", [Window("$0/@3", second, "later", true), Window("$0/@1", first, "earlier", false)])]);
        var service = new WorkspaceService(runner, new() { TmuxExecutable = "psmux" },
            new PauseStore(Path.Combine(root, "paused"), _ => 42));
        var result = service.ImportJson(snapshot.ToJson());
        try
        {
            Assert.True(result.Success, result.Message);
            Assert.Equal("earlier", calls.Single(c => c[0] == "new-session")[5]);
            var newWindow = calls.Single(c => c[0] == "new-window");
            Assert.Equal(("work:", "later"), (newWindow[3], newWindow[5]));
            Assert.DoesNotContain(calls, c => c[0] == "move-window");
            Assert.Equal("work:1", calls.Single(c => c[0] == "select-window")[2]);
            Assert.Contains($"work:{second} (later) restored as work:1", result.Message);
            Assert.Equal(first != 0, result.Message.Contains($"work:{first} (earlier) restored as work:0"));
        }
        finally
        {
            if (result.Path is not null) File.Delete(result.Path);
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("", "@1|%5", true)]
    [InlineData(" @1|%5 \r\n", "", true)]
    [InlineData("", "", false)]
    public void Creation_identity_falls_back_to_reading_the_target_and_never_crashes(string printed, string read, bool succeeds)
    {
        var root = Path.Combine(Path.GetTempPath(), "tw-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var runner = new TmuxRunner(args => new(true, 0, args[0] switch
        {
            "list-sessions" => "",
            "new-session" => printed,
            "lsp" => "%5|0",
            "display-message" => args[^1] switch
            {
                "#{window_id}|#{pane_id}" => read,
                "#{window_layout}" => WorkspaceTests.Layout("120x30,0,0,5"),
                "#{pane_current_path}" => root,
                _ => "0",
            },
            "select-layout" or "select-pane" or "select-window" => "",
            _ => throw new InvalidOperationException("Unexpected operation: " + args[0]),
        }, ""));
        var service = new WorkspaceService(runner, new() { TmuxExecutable = "psmux" },
            new PauseStore(Path.Combine(root, "paused"), _ => 42)) { CreationWait = TimeSpan.Zero };
        var result = service.ImportJson(WorkspaceTests.Example(root).ToJson());
        try
        {
            Assert.Equal(succeeds, result.Success);
            if (!succeeds) Assert.Contains("Partial restore retained", result.Message);
        }
        finally
        {
            if (result.Path is not null) File.Delete(result.Path);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Psmux_window_created_after_its_print_timeout_is_awaited()
    {
        var reads = 0;
        var backend = new WorkspaceBackend(new TmuxRunner(args => ++reads < 3
            ? new(true, 1, "", "can't find window: 1")
            : new(true, 0, "@2|%6\n", "")), new() { TmuxExecutable = "psmux" }) { CreationWait = TimeSpan.FromSeconds(5) };
        Assert.Equal("@2|%6", backend.CreatedIdentity("", "0:1"));
        Assert.Equal(3, reads);
    }

    [Fact]
    public void Psmux_pane_split_after_its_print_timeout_is_found_by_difference()
    {
        var listings = 0;
        var backend = new WorkspaceBackend(new TmuxRunner(args => new(true, 0, args[0] switch
        {
            "split-window" => "",
            "lsp" => ++listings < 4 ? "%5\n" : "%5\n%6\n",
            _ => throw new InvalidOperationException("Unexpected operation: " + args[0]),
        }, "")), new() { TmuxExecutable = "psmux" }) { CreationWait = TimeSpan.FromSeconds(5) };
        Assert.Equal("%6", backend.Split("0:1", "0:1.0", "C:\\work", true));
    }

    [Fact]
    public void Psmux_error_reply_with_success_status_fails_the_creation()
    {
        var backend = new WorkspaceBackend(new TmuxRunner(_ => new(true, 0, "ERROR: can't find window: 1\n", "")),
            new() { TmuxExecutable = "psmux" });
        var window = WorkspaceTests.Example().Sessions[0].Windows[0];
        var error = Assert.Throws<IOException>(() => backend.CreateWindow("work", window, window.Panes[0]));
        Assert.Equal("can't find window: 1", error.Message);
    }

    [Fact]
    public void Creation_that_never_appears_fails_with_what_was_returned()
    {
        var backend = new WorkspaceBackend(new TmuxRunner(_ => new(true, 1, "", "can't find window: 1")),
            new() { TmuxExecutable = "psmux" }) { CreationWait = TimeSpan.Zero };
        var error = Assert.Throws<InvalidDataException>(() => backend.CreatedIdentity("", "0:1"));
        Assert.Contains("can't find window: 1", error.Message);
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
