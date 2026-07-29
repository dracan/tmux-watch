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
    // Turn finished with one of Claude's own background shells still running. The footer
    // has dropped "(shift+tab to cycle)" for the shell count and the composer holds typed
    // text, so neither legacy idle hint is on screen - this used to classify Unknown.
    [InlineData("claude-backgnd.txt", PaneState.Backgnd)]
    // The composer renders the same in vim normal mode, so the anchor is mode-independent.
    [InlineData("claude-idle-normal-mode.txt", PaneState.Idle)]
    public void Classifies_claude_fixtures(string fixture, PaneState expected)
    {
        var state = ClaudeClassifier.Classify(Fixture(fixture), dead: false);
        Assert.Equal(expected, state);
    }

    [Fact]
    public void Classifier_never_returns_done()
    {
        // DONE is monitor-derived (a WORKING -> IDLE edge), never produced by the
        // stateless classifier. Every fixture, under both profiles, must classify to
        // something other than DONE.
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures"), "*.txt");
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.NotEqual(PaneState.Done, Classifier.Classify(text, dead: false));
            Assert.NotEqual(PaneState.Done, ClaudeClassifier.Classify(text, dead: false));
        }
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

    // ---- BACKGND and the composer anchor -----------------------------------

    private const string ShellFooter =
        "  -- INSERT -- ⏵⏵ auto mode on · 1 shell · ← for agents";

    private const string PlainFooter =
        "  -- INSERT -- ⏵⏵ auto mode on (shift+tab to cycle) · ← for agents";

    [Fact]
    public void Claude_idle_no_longer_depends_on_footer_hints()
    {
        // The regression this change exists for: a composer with typed text and a footer
        // carrying neither "? for shortcuts" nor "(shift+tab to cycle)". Under the old
        // hint-based IDLE this classified Unknown, which also made the pane ineligible
        // for DONE and so it never chimed.
        var text = $"● Earlier output\n────\n❯  commit this\n────\n{PlainFooter.Replace(" (shift+tab to cycle)", "")}";
        Assert.Equal(PaneState.Idle, ClaudeClassifier.Classify(text, dead: false));
    }

    [Fact]
    public void Claude_profile_carries_no_idle_hints()
    {
        // The composer anchor supersedes them; leaving hints configured would quietly
        // reintroduce the footer dependency this change removed.
        Assert.Empty(WatchConfig.ClaudeProfile().IdleHints);
    }

    [Fact]
    public void Working_outranks_background_shell()
    {
        // The shell segment stays in the chrome while the agent works, so precedence -
        // not the absence of the token - is what keeps this WORKING.
        var text = $"● Kicked off a build\n ✻ Enchanting… (32s · ↓ 1.4k tokens)\n────\n❯\n────\n{ShellFooter}";
        Assert.Equal(PaneState.Working, ClaudeClassifier.Classify(text, dead: false));
    }

    [Fact]
    public void Waiting_outranks_background_shell()
    {
        var text = $"╭──────────────────────╮\n│ ❯ 1. Yes             │\n╰──────────────────────╯\n────\n❯\n────\n{ShellFooter}";
        Assert.Equal(PaneState.Waiting, ClaudeClassifier.Classify(text, dead: false));
    }

    [Fact]
    public void Frozen_end_of_turn_shell_line_is_idle_not_backgnd()
    {
        // "· 1 shell still running" is frozen into the transcript and survives the shell
        // exiting. Matching it would pin the pane in BACKGND permanently - trading the
        // Unknown bug for a stuck-state bug.
        var text = $"✻ Cooked for 16s · 1 shell still running\n────\n❯  commit this\n────\n{PlainFooter}";
        Assert.Equal(PaneState.Idle, ClaudeClassifier.Classify(text, dead: false));
    }

    [Fact]
    public void Tool_use_shell_prose_is_idle_not_backgnd()
    {
        var text = $"● Ran 1 shell command\n────\n❯\n────\n{PlainFooter}";
        Assert.Equal(PaneState.Idle, ClaudeClassifier.Classify(text, dead: false));
    }

    [Fact]
    public void Shell_segment_above_the_composer_does_not_match()
    {
        // The position rule on its own: a segment shaped exactly like the footer one,
        // but quoted in the transcript, is not a live shell.
        var text = $"● Output quoting a footer: · 1 shell · ← for agents\n────\n❯\n────\n{PlainFooter}";
        Assert.Equal(PaneState.Idle, ClaudeClassifier.Classify(text, dead: false));
    }

    [Fact]
    public void Shell_prose_below_the_composer_does_not_match()
    {
        // The pattern rule on its own: even in the chrome, "still running" prose is not
        // the footer's counter segment.
        var text = $"────\n❯\n────\n{PlainFooter}\n  · 1 shell still running";
        Assert.Equal(PaneState.Idle, ClaudeClassifier.Classify(text, dead: false));
    }

    [Theory]
    // Claude renders the counter as `count === 1 ? "1 shell" : `${count} shells`` and the
    // same for monitors, joining both into one footer slot. All of these mean the agent
    // handed the turn back with work still running.
    [InlineData("  -- INSERT -- ⏵⏵ auto mode on · 1 shell · ← for agents")]
    [InlineData("  -- INSERT -- ⏵⏵ auto mode on · 2 shells · ← for agents")]
    [InlineData("  -- INSERT -- ⏵⏵ auto mode on · 3 shells")]        // segment ends the line
    [InlineData("  -- INSERT -- ⏵⏵ auto mode on · 1 monitor · ← for agents")]
    [InlineData("  -- INSERT -- ⏵⏵ auto mode on · 4 monitors · ← for agents")]
    [InlineData("  -- INSERT -- ⏵⏵ auto mode on · 2 shells · 1 monitor · ← for agents")]
    public void Background_task_counter_forms(string footer)
    {
        var text = $"● Earlier output\n────\n❯\n────\n{footer}";
        Assert.Equal(PaneState.Backgnd, ClaudeClassifier.Classify(text, dead: false));
    }

    [Fact]
    public void Monitor_prose_does_not_match_the_counter()
    {
        // Same separator-and-terminator rule as the shell form: prose is not a counter.
        var text = $"✻ Cooked for 9s · 1 monitor still running\n────\n❯\n────\n{PlainFooter}";
        Assert.Equal(PaneState.Idle, ClaudeClassifier.Classify(text, dead: false));
    }

    [Fact]
    public void Shell_exit_returns_the_pane_to_idle()
    {
        var backgnd = $"● Earlier output\n────\n❯  commit this\n────\n{ShellFooter}";
        var exited = $"● Earlier output\n────\n❯  commit this\n────\n{PlainFooter}";

        Assert.Equal(PaneState.Backgnd, ClaudeClassifier.Classify(backgnd, dead: false));
        Assert.Equal(PaneState.Idle, ClaudeClassifier.Classify(exited, dead: false));
    }

    [Fact]
    public void No_composer_box_is_unknown()
    {
        // Unknown keeps its documented meaning: no recognised agent screen - rather than
        // "recognised screen whose current hint slot happens to be missing".
        var text = "some scrollback\nmore scrollback\nnothing resembling agent chrome";
        Assert.Equal(PaneState.Unknown, ClaudeClassifier.Classify(text, dead: false));
    }

    [Fact]
    public void Numbered_selection_cursor_is_not_a_composer()
    {
        // The composer and the WAITING cursor share the `❯` glyph, so the anchor excludes
        // the numbered form explicitly rather than relying on precedence alone.
        var prompt = WatchConfig.ClaudeProfile().CompileIdlePrompt()!;
        Assert.DoesNotMatch(prompt, "❯ 1. Yes");
        Assert.DoesNotMatch(prompt, "  ❯ 2. No");
        Assert.Matches(prompt, "❯");
        Assert.Matches(prompt, "❯  commit this");
    }

    [Fact]
    public void Profile_without_a_background_task_token_never_produces_backgnd()
    {
        // Copilot configures neither anchor, so it keeps classifying via IdleHints and
        // BACKGND is unreachable for it.
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures"), "*.txt");
        foreach (var file in files)
            Assert.NotEqual(PaneState.Backgnd, Classifier.Classify(File.ReadAllText(file), dead: false));
    }
}
