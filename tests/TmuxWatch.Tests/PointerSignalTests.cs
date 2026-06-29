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
        public int NormalApplied { get; private set; }
        protected override void ApplyWaiting() => WaitingApplied++;
        protected override void ApplyNormal() => NormalApplied++;
    }

    // --- Config parsing (task 1.3) ---

    [Fact]
    public void Pointer_signal_is_off_by_default()
    {
        var cfg = new WatchConfig();
        Assert.False(cfg.PointerSignal.Enabled);
        Assert.Equal(new[] { "arrow", "ibeam" }, cfg.PointerSignal.Shapes);
    }

    [Fact]
    public void Omitting_pointer_signal_in_json_keeps_defaults_off()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "{ \"pollIntervalSeconds\": 3 }");
        try
        {
            var cfg = WatchConfig.Load(path);
            Assert.False(cfg.PointerSignal.Enabled);
            Assert.Equal("assets/waiting-cursor.cur", cfg.PointerSignal.WaitingCursorFile);
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
            "shapes": ["arrow"]
          }
        }
        """;
        var cfg = JsonSerializer.Deserialize<WatchConfig>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.True(cfg.PointerSignal.Enabled);
        Assert.Equal("custom/red.cur", cfg.PointerSignal.WaitingCursorFile);
        Assert.Equal(new[] { "arrow" }, cfg.PointerSignal.Shapes);
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
    public void Set_waiting_applies_only_on_transitions()
    {
        var p = new RecordingPointerSignal();

        p.SetWaiting(true);      // edge: normal -> waiting
        p.SetWaiting(true);      // no edge
        p.SetWaiting(true);      // no edge
        Assert.Equal(1, p.WaitingApplied);
        Assert.Equal(0, p.NormalApplied);

        p.SetWaiting(false);     // edge: waiting -> normal
        p.SetWaiting(false);     // no edge
        Assert.Equal(1, p.WaitingApplied);
        Assert.Equal(1, p.NormalApplied);
    }

    [Fact]
    public void Restore_forces_normal_and_resets_state()
    {
        var p = new RecordingPointerSignal();

        p.Restore();             // unconditional restore even from initial state
        Assert.Equal(1, p.NormalApplied);

        p.SetWaiting(true);      // applies again because Restore cleared the flag
        Assert.Equal(1, p.WaitingApplied);
    }
}
