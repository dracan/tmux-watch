using TmuxWatch.Config;
using TmuxWatch.Detection;

namespace TmuxWatch.Tests;

public class PaneClassifierTests
{
    private static readonly PaneClassifier Classifier = new(new WatchConfig());

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
        var state = Classifier.Classify(Fixture(fixture), commandIsCopilot: true, dead: false);
        Assert.Equal(expected, state);
    }

    [Fact]
    public void Lazygit_control_is_not_waiting()
    {
        // Even if mis-treated as copilot, another bordered TUI must not look WAITING.
        var state = Classifier.Classify(Fixture("lazygit-control.txt"), commandIsCopilot: true, dead: false);
        Assert.NotEqual(PaneState.Waiting, state);
    }

    [Fact]
    public void Dead_when_command_not_copilot()
    {
        var state = Classifier.Classify(Fixture("idle.txt"), commandIsCopilot: false, dead: false);
        Assert.Equal(PaneState.Dead, state);
    }

    [Fact]
    public void Dead_when_dead_flag_set()
    {
        var state = Classifier.Classify(Fixture("working.txt"), commandIsCopilot: true, dead: true);
        Assert.Equal(PaneState.Dead, state);
    }

    [Fact]
    public void Ask_user_footer_wording_differs_but_still_waiting()
    {
        // ask_user uses "select/confirm" not "navigate/select"; must still match.
        var text = "│ ❯ 1. Option A │\n│ ↑/↓ to select · enter to confirm · esc to cancel │";
        var state = Classifier.Classify(text, commandIsCopilot: true, dead: false);
        Assert.Equal(PaneState.Waiting, state);
    }

    [Fact]
    public void Waiting_outranks_residual_working_text()
    {
        // A WAITING footer plus stale "Working" earlier resolves to WAITING.
        var text = "◎ Working\nmore output\n❯ 1. Yes\n↑/↓ to navigate · enter to select · esc to cancel";
        var state = Classifier.Classify(text, commandIsCopilot: true, dead: false);
        Assert.Equal(PaneState.Waiting, state);
    }

    [Fact]
    public void Spinner_glyph_variant_still_working()
    {
        foreach (var glyph in new[] { "◎", "◉", "●", "○" })
        {
            var text = $"────\n {glyph} Working esc cancel   Claude Opus 4.8 · 1M context";
            Assert.Equal(PaneState.Working, Classifier.Classify(text, true, false));
        }
    }

    [Fact]
    public void Action_label_footer_without_working_word_is_working()
    {
        // Newer Copilot footer: spinner + current action label + "esc cancel",
        // with no literal "Working" word.
        var text = "────\n ◉ Adding WidgetResource + SampleRequest contracts esc cancel   Claude Opus 4.8 · 1M context";
        Assert.Equal(PaneState.Working, Classifier.Classify(text, true, false));
    }

    [Fact]
    public void Spinner_glyph_in_body_without_cancel_marker_is_not_working()
    {
        // A bullet glyph in scrollback must not be mistaken for the working footer.
        var text = "● Some earlier message line\nmore output\n/ commands · ? help · space hold to record";
        Assert.NotEqual(PaneState.Working, Classifier.Classify(text, true, false));
    }

    [Fact]
    public void Empty_capture_is_unknown_for_live_copilot()
    {
        var state = Classifier.Classify("", commandIsCopilot: true, dead: false);
        Assert.Equal(PaneState.Unknown, state);
    }

    [Fact]
    public void Configurable_tokens_are_honoured()
    {
        var cfg = new WatchConfig { IdleHints = new List<string> { "READY>>" } };
        var classifier = new PaneClassifier(cfg);
        Assert.Equal(PaneState.Idle, classifier.Classify("some output\nREADY>>", true, false));
        // default idle hint should no longer match
        Assert.NotEqual(PaneState.Idle, classifier.Classify("/ commands · ? help", true, false));
    }
}
