using System.Text.Json;
using TmuxWatch.Config;
using TmuxWatch.Pointer;

namespace TmuxWatch.Tests;

public class PointerSignalTests
{
    /// <summary>Records apply edges so the base-class transition logic can be asserted.</summary>
    private sealed class RecordingPointerSignal : PointerSignalBase
    {
        public int WaitingApplied { get; private set; }
        public int DoneApplied { get; private set; }
        public int NormalApplied { get; private set; }
        protected override void ApplyWaiting() => WaitingApplied++;
        protected override void ApplyDone() => DoneApplied++;
        protected override void ApplyNormal() => NormalApplied++;
    }

    // --- Config parsing (task 1.3) ---

    [Fact]
    public void Pointer_signal_is_on_by_default()
    {
        var cfg = new WatchConfig();
        Assert.True(cfg.PointerSignal.Enabled);
        Assert.Equal(new[] { "arrow", "ibeam" }, cfg.PointerSignal.Shapes);
    }

    [Fact]
    public void Omitting_pointer_signal_in_json_keeps_defaults()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "{ \"pollIntervalSeconds\": 3 }");
        try
        {
            var cfg = WatchConfig.Load(path);
            Assert.True(cfg.PointerSignal.Enabled);
            Assert.Equal("assets/waiting-cursor.cur", cfg.PointerSignal.WaitingCursorFile);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Pointer_signal_can_be_disabled_in_json()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "{ \"pointerSignal\": { \"enabled\": false } }");
        try
        {
            Assert.False(WatchConfig.Load(path).PointerSignal.Enabled);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Pointer_signal_parses_enabled_with_custom_asset_and_shapes()
    {
        var json = """
        {
          "pointerSignal": {
            "enabled": true,
            "waitingCursorFile": "custom/red.cur",
            "doneCursorFile": "custom/green.cur",
            "shapes": ["arrow"]
          }
        }
        """;
        var cfg = JsonSerializer.Deserialize<WatchConfig>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.True(cfg.PointerSignal.Enabled);
        Assert.Equal("custom/red.cur", cfg.PointerSignal.WaitingCursorFile);
        Assert.Equal("custom/green.cur", cfg.PointerSignal.DoneCursorFile);
        Assert.Equal(new[] { "arrow" }, cfg.PointerSignal.Shapes);
    }

    [Fact]
    public void Done_cursor_file_defaults_to_shipped_green()
    {
        Assert.Equal("assets/done-cursor.cur", new WatchConfig().PointerSignal.DoneCursorFile);
    }

    // --- Factory host/enabled selection (tasks 2.3 / 3.3) ---

    [Fact]
    public void Factory_returns_null_signal_when_disabled()
    {
        var cfg = new PointerSignalConfig { Enabled = false };
        Assert.IsType<NullPointerSignal>(PointerSignalFactory.Create(cfg, AppContext.BaseDirectory));
    }

    // --- Base transition logic (task 2.1) ---

    [Fact]
    public void Set_state_applies_only_on_transitions()
    {
        var p = new RecordingPointerSignal();

        p.SetState(PointerState.Waiting);   // edge: normal -> waiting
        p.SetState(PointerState.Waiting);   // no edge
        p.SetState(PointerState.Waiting);   // no edge
        Assert.Equal(1, p.WaitingApplied);
        Assert.Equal(0, p.NormalApplied);

        p.SetState(PointerState.Normal);    // edge: waiting -> normal
        p.SetState(PointerState.Normal);    // no edge
        Assert.Equal(1, p.WaitingApplied);
        Assert.Equal(1, p.NormalApplied);
    }

    [Fact]
    public void Set_state_transitions_between_waiting_and_done_directly()
    {
        var p = new RecordingPointerSignal();

        p.SetState(PointerState.Done);      // normal -> done (green)
        Assert.Equal(1, p.DoneApplied);

        p.SetState(PointerState.Waiting);   // done -> waiting (red), no restore needed
        Assert.Equal(1, p.WaitingApplied);

        p.SetState(PointerState.Done);      // waiting -> done (green) again
        Assert.Equal(2, p.DoneApplied);
        Assert.Equal(0, p.NormalApplied);   // colour swaps never route through normal
    }

    [Fact]
    public void Restore_forces_normal_and_resets_state()
    {
        var p = new RecordingPointerSignal();

        p.Restore();                        // unconditional restore even from initial state
        Assert.Equal(1, p.NormalApplied);

        p.SetState(PointerState.Waiting);   // applies again because Restore cleared the flag
        Assert.Equal(1, p.WaitingApplied);
    }
}
