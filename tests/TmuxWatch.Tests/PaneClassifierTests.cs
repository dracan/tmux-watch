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
    public void Spinner_glyph_in_body_without_cancel_marker_is_not_working()
    {
        // A bullet glyph in scrollback must not be mistaken for the working footer.
        var text = "● Some earlier message line\nmore output\n/ commands · ? help · space hold to record";
        Assert.NotEqual(PaneState.Working, Classifier.Classify(text, dead: false));
    }
}
