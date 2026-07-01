using TmuxWatch.Config;
using TmuxWatch.Detection;

namespace TmuxWatch.Tests;

public class PaneClassifierTests
{
    private static readonly PaneClassifier Classifier = new(WatchConfig.CopilotProfile());
    private static readonly PaneClassifier ClaudeClassifier = new(WatchConfig.ClaudeProfile());

    private static string Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", name);
        return File.ReadAllText(path);
    }

    [Theory]
    [InlineData("waiting-command-approval.txt", PaneState.Waiting)]
    [InlineData("waiting-ask-user.txt", PaneState.Waiting)]
    [InlineData("working.txt", PaneState.Working)]
    [InlineData("working-action-label.txt", PaneState.Working)]
    [InlineData("idle.txt", PaneState.Idle)]
    public void Classifies_copilot_fixtures(string fixture, PaneState expected)
    {
        var state = Classifier.Classify(Fixture(fixture), dead: false);
        Assert.Equal(expected, state);
    }

    [Theory]
    [InlineData("claude-waiting.txt", PaneState.Waiting)]
    [InlineData("claude-working.txt", PaneState.Working)]
    [InlineData("claude-idle.txt", PaneState.Idle)]
    // Current Claude Code build (status bar changed; "esc to interrupt" gone).
    [InlineData("claude-working-current.txt", PaneState.Working)]
    [InlineData("claude-working-subagents.txt", PaneState.Working)]
    [InlineData("claude-working-dot-frame.txt", PaneState.Working)]
    [InlineData("claude-waiting-slash-menu.txt", PaneState.Waiting)]
    [InlineData("claude-idle-after-work.txt", PaneState.Idle)]
    public void Classifies_claude_fixtures(string fixture, PaneState expected)
    {
        var state = ClaudeClassifier.Classify(Fixture(fixture), dead: false);
        Assert.Equal(expected, state);
    }

    [Fact]
    public void Claude_idle_is_not_idle_under_copilot_profile()
    {
        // Each agent's IDLE hint is its own; the copilot profile must not read
        // Claude's input-box mode line as idle.
        var state = Classifier.Classify(Fixture("claude-idle.txt"), dead: false);
        Assert.NotEqual(PaneState.Idle, state);
    }

    [Fact]
    public void Copilot_working_is_not_working_under_claude_profile()
    {
        // Copilot's "esc cancel" is not Claude's "esc to interrupt".
        var state = ClaudeClassifier.Classify(Fixture("working.txt"), dead: false);
        Assert.NotEqual(PaneState.Working, state);
    }

    [Fact]
    public void Lazygit_control_is_not_waiting()
    {
        // Another bordered TUI must not look WAITING.
        var state = Classifier.Classify(Fixture("lazygit-control.txt"), dead: false);
        Assert.NotEqual(PaneState.Waiting, state);
    }

    [Fact]
    public void Dead_when_dead_flag_set()
    {
        var state = Classifier.Classify(Fixture("working.txt"), dead: true);
        Assert.Equal(PaneState.Dead, state);
    }

    [Fact]
    public void Ask_user_footer_wording_differs_but_still_waiting()
    {
        // ask_user uses "select/confirm" not "navigate/select"; must still match.
        var text = "│ ❯ 1. Option A │\n│ ↑/↓ to select · enter to confirm · esc to cancel │";
        var state = Classifier.Classify(text, dead: false);
        Assert.Equal(PaneState.Waiting, state);
    }

    [Fact]
    public void Waiting_outranks_residual_working_text()
    {
        // A WAITING footer plus stale "Working" earlier resolves to WAITING.
        var text = "◎ Working\nmore output\n❯ 1. Yes\n↑/↓ to navigate · enter to select · esc to cancel";
        var state = Classifier.Classify(text, dead: false);
        Assert.Equal(PaneState.Waiting, state);
    }

    [Fact]
    public void Spinner_glyph_variant_still_working()
    {
        foreach (var glyph in new[] { "◎", "◉", "●", "○" })
        {
            var text = $"────\n {glyph} Working esc cancel   Claude Opus 4.8 · 1M context";
            Assert.Equal(PaneState.Working, Classifier.Classify(text, dead: false));
        }
    }

    [Fact]
    public void Action_label_footer_without_working_word_is_working()
    {
        // Newer Copilot footer: spinner + current action label + "esc cancel",
        // with no literal "Working" word.
        var text = "────\n ◉ Adding WidgetResource + SampleRequest contracts esc cancel   Claude Opus 4.8 · 1M context";
        Assert.Equal(PaneState.Working, Classifier.Classify(text, dead: false));
    }

    [Fact]
    public void Claude_working_marker_alone_is_working()
    {
        // Claude's "esc to interrupt" marker is sufficient without a known spinner glyph.
        var text = "some output\n✶ Cogitating… (3s · esc to interrupt)";
        Assert.Equal(PaneState.Working, ClaudeClassifier.Classify(text, dead: false));
    }

    [Fact]
    public void Claude_live_spinner_animation_stays_working()
    {
        // The action word is random and the glyph is an animation frame; the live
        // line stays WORKING through the cycle as long as the gerund+ellipsis (or
        // meter) qualifier holds. "esc to interrupt" is gone in the current build.
        foreach (var glyph in new[] { "✻", "✽", "✶", "✷", "✸", "✹", "✺", "✢", "✳", "∗" })
        {
            var ellipsis = $"more output\n {glyph} Enchanting…\n────\n❯\n────\n  -- INSERT --";
            Assert.Equal(PaneState.Working, ClaudeClassifier.Classify(ellipsis, dead: false));

            var meter = $"more output\n {glyph} Tinkering… (32s · ↓ 1.4k tokens)\n────\n❯\n────\n  -- INSERT --";
            Assert.Equal(PaneState.Working, ClaudeClassifier.Classify(meter, dead: false));
        }
    }

    [Theory]
    // The spinner animates through frames outside the asterisk whitelist (·, *).
    // The activity meter is glyph-independent, so the live line must still read
    // WORKING regardless of which frame the capture lands on.
    [InlineData("·")]   // U+00B7 middle dot
    [InlineData("*")]   // U+002A ASCII asterisk
    [InlineData("✻")]
    [InlineData("✢")]
    public void Claude_meter_line_is_working_for_any_leading_glyph(string glyph)
    {
        var text = $"● Running 1 shell command…\n {glyph} Doodling… (11s · ↓ 307 tokens)\n────\n❯\n────\n  -- INSERT --";
        Assert.Equal(PaneState.Working, ClaudeClassifier.Classify(text, dead: false));
    }

    [Fact]
    public void Claude_frozen_completed_line_is_not_working()
    {
        // A finished turn freezes a same-glyph line ("Crunched for 54s"): past-tense,
        // no ellipsis, no meter. It must not read as WORKING even though it starts
        // with a spinner glyph and stays on screen at the idle input box.
        var state = ClaudeClassifier.Classify(Fixture("claude-idle-after-work.txt"), dead: false);
        Assert.NotEqual(PaneState.Working, state);
        Assert.Equal(PaneState.Idle, state);
    }

    [Fact]
    public void Spinner_glyph_in_body_without_cancel_marker_is_not_working()
    {
        // A bullet glyph in scrollback must not be mistaken for the working footer.
        var text = "● Some earlier message line\nmore output\n/ commands · ? help · space hold to record";
        Assert.NotEqual(PaneState.Working, Classifier.Classify(text, dead: false));
    }
}
