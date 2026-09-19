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

    /// <summary>
    /// Emptying a marker is how this codebase switches a token off - IsWorking guards its
    /// own that way, AgentProfile.CompileOrNull returns null for an empty pattern, and the
    /// shipped Claude profile empties WorkingFooterCancelMarker for exactly that reason.
    /// Applied to the waiting pair it used to do the opposite: string.Contains("") is
    /// always true, so every non-blank line matched and the pane pinned in WAITING - top
    /// priority, so it chimed, reddened the pointer, and never reached DONE.
    /// </summary>
    [Theory]
    [InlineData("", "")]
    [InlineData("\u2191/\u2193", "")]
    [InlineData("", "esc to cancel")]
    public void An_emptied_waiting_footer_marker_disables_the_check_rather_than_matching_everything(
        string nav, string cancel)
    {
        var profile = WatchConfig.ClaudeProfile();
        profile.WaitingFooterNavMarker = nav;
        profile.WaitingFooterCancelMarker = cancel;

        var idle = Fixture("claude-idle.txt");
        Assert.NotEqual(PaneState.Waiting, new PaneClassifier(profile).Classify(idle, dead: false));
    }

    [Theory]
    [InlineData("waiting-command-approval.txt", PaneState.Waiting)]
    [InlineData("waiting-ask-user.txt", PaneState.Waiting)]
    [InlineData("waiting-ask-question.txt", PaneState.Waiting)]
    [InlineData("waiting-ask-question-freeform.txt", PaneState.Waiting)]
    [InlineData("waiting-ask-question-multiple.txt", PaneState.Waiting)]
    [InlineData("working.txt", PaneState.Working)]
    [InlineData("working-action-label.txt", PaneState.Working)]
    [InlineData("idle.txt", PaneState.Idle)]
    public void Classifies_copilot_fixtures(string fixture, PaneState expected)
    {
        var state = Classifier.Classify(Fixture(fixture), dead: false);
        Assert.Equal(expected, state);
    }

    [Theory]
    [InlineData("waiting-ask-question.txt")]
    [InlineData("waiting-ask-question-freeform.txt")]
    [InlineData("waiting-ask-question-multiple.txt")]
    public void Copilot_question_panel_requires_live_boundaries_and_can_be_disabled(string fixture)
    {
        var form = Fixture(fixture);
        Assert.Equal(PaneState.Waiting, Classifier.Classify(form, false));
        Assert.NotEqual(PaneState.Waiting, Classifier.Classify("Copilot needs information.\nA quoted heading", false));
        Assert.Equal(PaneState.Idle, Classifier.Classify(form + Fixture("idle.txt"), false));
        Assert.NotEqual(PaneState.Waiting, Classifier.Classify(form + "\n" + new string('\u2500', 70) + "\n\u276f\n" + new string('\u2500', 70), false));
        Assert.NotEqual(PaneState.Waiting, ClaudeClassifier.Classify(form, false));
        Assert.NotEqual(PaneState.Waiting, new PaneClassifier(WatchConfig.CodexProfile()).Classify(form, false));
        var profile = WatchConfig.CopilotProfile();
        profile.WaitingPanelPattern = "";
        Assert.Equal(PaneState.Unknown, new PaneClassifier(profile).Classify(form, false));
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
    // Turn finished with a *detached* background sub-agent still running. The footer keeps
    // "(shift+tab to cycle)" and carries no counter - agents never reach that slot - so the
    // fleet panel row is the only evidence. This used to classify IDLE, which meant the
    // WORKING -> IDLE edge chimed at the moment of delegation instead of at completion.
    [InlineData("claude-backgnd-subagent.txt", PaneState.Backgnd)]
    // The same screen as a scrubbed real capture, so the glyphs are verified against real
    // terminal bytes rather than against a hand-written approximation of them.
    [InlineData("claude-backgnd-subagent-real.txt", PaneState.Backgnd)]
    // Same, one poll after launch: the row exists but has no token counter yet.
    [InlineData("claude-backgnd-subagent-fresh.txt", PaneState.Backgnd)]
    // The same session after the sub-agent reported: the panel is gone, but the delegation
    // line and the finished banner remain frozen in the transcript.
    [InlineData("claude-idle-after-subagent.txt", PaneState.Idle)]
    // A finished turn whose recap prose contains "❯ 1.". Read as a selection cursor it made
    // the pane WAITING, which outranks IDLE - so the turn never reached DONE.
    [InlineData("claude-idle-cursor-prose.txt", PaneState.Idle)]
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
        // The prompt keeps its own footer here, because the cursor alone is no longer a
        // signal this far up the screen - a box stacked *above* a live composer is not a
        // layout any real prompt renders (see Cursor_above_the_composer_does_not_match),
        // and if one ever does, this nav+cancel pair is what still catches it.
        var text = $"╭──────────────────────╮\n│ ❯ 1. Yes             │\n│ ↑/↓ to navigate · enter to select · esc to cancel │\n╰──────────────────────╯\n────\n❯\n────\n{ShellFooter}";
        Assert.Equal(PaneState.Waiting, ClaudeClassifier.Classify(text, dead: false));
    }

    [Fact]
    public void Cursor_above_the_composer_does_not_match()
    {
        // The bug this rule exists for: an agent that *wrote* "❯ 1." - in a recap, a diff,
        // a quoted screen - is not waiting on anyone. A live composer below the glyph is
        // the proof, since every real prompt box replaces the composer rather than
        // stacking above it. WAITING outranks IDLE, so this used to withhold DONE
        // silently for as long as the text stayed on screen.
        var text = $"● Widening the scan would expose WAITING to stale ❯ 1. cursors\n────\n❯\n────\n{PlainFooter}";
        Assert.Equal(PaneState.Idle, ClaudeClassifier.Classify(text, dead: false));
    }

    [Fact]
    public void Cursor_with_no_composer_on_screen_still_waits()
    {
        // The other half of the same rule: a prompt box takes the composer's place, so
        // there is no split to apply and the cursor is read wherever it lands. This is
        // the shape of all four WAITING fixtures, and of a bare permission prompt, which
        // carries no nav/cancel footer to fall back on.
        var text = "● Earlier output\n╭──────────────────────╮\n│ Do you want to proceed? │\n│ ❯ 1. Yes             │\n│   2. No              │\n╰──────────────────────╯";
        Assert.Equal(PaneState.Waiting, ClaudeClassifier.Classify(text, dead: false));
    }

    [Fact]
    public void Profile_without_a_composer_pattern_matches_the_cursor_anywhere()
    {
        // Copilot configures no composer prompt, so there is no split to apply and the
        // position rule is inert - the same screen that reads IDLE under Claude's profile
        // stays WAITING under one that cannot locate a composer.
        var text = $"● Widening the scan would expose WAITING to stale ❯ 1. cursors\n────\n❯\n────\n{PlainFooter}";
        Assert.Equal(PaneState.Idle, ClaudeClassifier.Classify(text, dead: false));
        Assert.Equal(PaneState.Waiting, Classifier.Classify(text, dead: false));
    }

    [Fact]
    public void Cursor_typed_into_the_composer_does_not_match()
    {
        // Falls out of the same position rule: the composer line is chrome, but it is not
        // *below* itself, so text the user is still typing cannot flag their own pane.
        var text = $"● Earlier output\n────\n❯ why did ❯ 1. flag that pane\n────\n{PlainFooter}";
        Assert.Equal(PaneState.Idle, ClaudeClassifier.Classify(text, dead: false));
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
        var verdict = ClaudeClassifier.Inspect(text, dead: false);
        Assert.Equal(PaneState.Backgnd, verdict.State);

        // Shells and monitors share the one fingerprint and are not told apart from each
        // other - including when both are counted in the same segment. What matters is that
        // no agent flag is set, because that is what admits the grace-period backstop.
        Assert.Equal(BackgndReason.BackgroundTask, verdict.Reason);
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

    // ---- BACKGND from a detached background sub-agent ----------------------

    private const string MainRow = "  ● main";

    private static string AgentRow(string name, string meter) =>
        $"  ◯ {name}  Synthetic sweep…{meter,40}";

    [Fact]
    public void Main_fleet_row_alone_is_not_backgnd()
    {
        // The panel's `main` row renders whether or not any agent is running, so it carries
        // no information. Only the per-agent bullet does. `●` and `◯` are near-identical on
        // screen, which is precisely why this is pinned by a test.
        var text = $"● Earlier output\n────\n❯\n────\n{PlainFooter}\n\n{MainRow}";
        Assert.Equal(PaneState.Idle, ClaudeClassifier.Classify(text, dead: false));
    }

    [Fact]
    public void Several_agent_rows_are_still_one_backgnd()
    {
        // BACKGND is a claim about the pane, not a count of what it is waiting on.
        var text = $"● Earlier output\n────\n❯\n────\n{PlainFooter}\n\n{MainRow}\n" +
                   $"{AgentRow("sweep-one", "2m 5s · ↓ 104.3k tokens")}\n" +
                   $"{AgentRow("sweep-two", "8s · ↓ 1.2k tokens")}";
        var verdict = ClaudeClassifier.Inspect(text, dead: false);
        Assert.Equal(PaneState.Backgnd, verdict.State);
        Assert.Equal(BackgndReason.BackgroundAgent, verdict.Reason);
    }

    [Fact]
    public void Delegation_prose_above_the_composer_does_not_match()
    {
        // The sub-agent's counterpart to the frozen shell line. This survives the agent by
        // the whole rest of the session, so matching it anywhere would pin the pane.
        var text = "  ⎿  Running in the background as @slow-sweep\n" +
                   $"────\n❯\n────\n{PlainFooter}";
        Assert.Equal(PaneState.Idle, ClaudeClassifier.Classify(text, dead: false));
    }

    [Fact]
    public void Agent_completion_returns_the_pane_to_idle()
    {
        // The row leaves the panel when the agent finishes, so the fingerprint releases
        // itself - no grace period and no history involved in getting back to IDLE.
        var running = $"● Earlier output\n────\n❯\n────\n{PlainFooter}\n\n{MainRow}\n" +
                      $"{AgentRow("slow-sweep", "2m 5s · ↓ 104.3k tokens")}";
        var finished = $"● Earlier output\n────\n❯\n────\n{PlainFooter}";

        Assert.Equal(PaneState.Backgnd, ClaudeClassifier.Classify(running, dead: false));
        Assert.Equal(PaneState.Idle, ClaudeClassifier.Classify(finished, dead: false));
    }

    [Fact]
    public void Blocked_on_subagents_outranks_the_agent_rows()
    {
        // Both forms of the same feature render the fleet panel; only the blocked form
        // renders the live status line, and there the agent cannot take input. The existing
        // WORKING-above-BACKGND precedence is what tells them apart - no new rule.
        var text = "✻ Waiting for 2 background agents to finish\n" +
                   $"────\n❯\n────\n{PlainFooter}\n\n{MainRow}\n" +
                   $"{AgentRow("sweep-one", "2m 5s · ↓ 104.3k tokens")}";
        Assert.Equal(PaneState.Working, ClaudeClassifier.Classify(text, dead: false));
    }

    [Fact]
    public void Agent_row_token_is_a_separate_key_from_the_counter()
    {
        // The two fingerprints must stay independently configurable: they describe
        // different UI surfaces (a footer segment vs a panel row) that drift on different
        // schedules. Copilot sets neither, which is what keeps BACKGND unreachable for it.
        var claude = WatchConfig.ClaudeProfile();
        Assert.NotEmpty(claude.BackgroundTaskPattern);
        Assert.NotEmpty(claude.BackgroundAgentRowPattern);
        Assert.NotEqual(claude.BackgroundTaskPattern, claude.BackgroundAgentRowPattern);
        Assert.Empty(WatchConfig.CopilotProfile().BackgroundAgentRowPattern);
    }

    // ---- The BACKGND reason ------------------------------------------------

    private const string ShellAndAgentFooter =
        "  -- INSERT -- ⏵⏵ auto mode on · 1 shell · ← for agents";

    [Fact]
    public void Newly_launched_agent_row_reports_the_agent_reason()
    {
        // A just-launched agent renders a bare elapsed time and no token counter, which is
        // why the bullet rather than the meter is the fingerprint. The reason must survive
        // that form too, or the opening seconds of every agent would admit the backstop.
        var text = $"● Earlier output\n────\n❯\n────\n{PlainFooter}\n\n{MainRow}\n" +
                   $"{AgentRow("slow-sweep", "0s")}";
        var verdict = ClaudeClassifier.Inspect(text, dead: false);
        Assert.Equal(PaneState.Backgnd, verdict.State);
        Assert.Equal(BackgndReason.BackgroundAgent, verdict.Reason);
    }

    [Fact]
    public void A_shell_alongside_an_agent_reports_both_reasons()
    {
        // The case that forbids short-circuiting. Whichever token is tested first, both
        // must be reported: the shell alone would admit the grace period and announce a
        // turn the parent agent has not finished.
        var text = $"● Earlier output\n────\n❯\n────\n{ShellAndAgentFooter}\n\n{MainRow}\n" +
                   $"{AgentRow("slow-sweep", "2m 5s · ↓ 104.3k tokens")}";
        var verdict = ClaudeClassifier.Inspect(text, dead: false);
        Assert.Equal(PaneState.Backgnd, verdict.State);
        Assert.Equal(BackgndReason.BackgroundTask | BackgndReason.BackgroundAgent, verdict.Reason);
    }

    [Fact]
    public void Copilot_profile_can_only_ever_report_the_counter_reason()
    {
        // Copilot leaves the agent-row token empty, so the agent flag is unreachable for it
        // and the grace-period gate is always satisfied. Its behaviour is unchanged by
        // construction rather than by a separate code path.
        var profile = WatchConfig.CopilotProfile();
        profile.BackgroundTaskPattern = @"·\s*\d+\s+(shells?|monitors?)\s*(·|$)";
        profile.IdlePromptPattern = @"^\s*❯(?!\s*\d+\.)";

        var text = $"● Earlier output\n────\n❯\n────\n{ShellFooter}\n\n{MainRow}\n" +
                   $"{AgentRow("slow-sweep", "2m 5s · ↓ 104.3k tokens")}";
        var verdict = new PaneClassifier(profile).Inspect(text, dead: false);

        Assert.Equal(PaneState.Backgnd, verdict.State);
        Assert.Equal(BackgndReason.BackgroundTask, verdict.Reason);
        Assert.Empty(WatchConfig.CopilotProfile().BackgroundAgentRowPattern);
    }

    [Fact]
    public void The_reason_does_not_alter_precedence()
    {
        // WORKING still outranks BACKGND whichever fingerprint is present, and a WORKING
        // verdict carries no reason - the reason is a property of BACKGND, not of the
        // screen.
        var text = "✻ Waiting for 2 background agents to finish\n" +
                   $"────\n❯\n────\n{ShellAndAgentFooter}\n\n{MainRow}\n" +
                   $"{AgentRow("sweep-one", "2m 5s · ↓ 104.3k tokens")}";
        var verdict = ClaudeClassifier.Inspect(text, dead: false);
        Assert.Equal(PaneState.Working, verdict.State);
        Assert.Equal(BackgndReason.None, verdict.Reason);
    }

    [Fact]
    public void Every_non_backgnd_verdict_reports_no_reason()
    {
        // The reason is meaningful only for BACKGND. Anything else leaking a flag would
        // give the monitor a gate to trip on a state that never reaches it.
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures"), "*.txt");
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var verdict in new[]
                     {
                         Classifier.Inspect(text, dead: false),
                         ClaudeClassifier.Inspect(text, dead: false),
                         ClaudeClassifier.Inspect(text, dead: true),
                     })
            {
                if (verdict.State != PaneState.Backgnd)
                    Assert.Equal(BackgndReason.None, verdict.Reason);
                else
                    Assert.NotEqual(BackgndReason.None, verdict.Reason);
            }
        }
    }

    [Fact]
    public void Transcript_prose_yields_no_reason_at_all()
    {
        // The position rule is what keeps frozen prose from pinning the state. It must keep
        // the reason clean too: a stale delegation line above the composer must not set the
        // agent flag on a pane that is merely IDLE.
        var text = "  ⎿  Running in the background as @slow-sweep\n" +
                   "  ◯ quoted agent row inside transcript prose\n" +
                   $"────\n❯\n────\n{PlainFooter}";
        var verdict = ClaudeClassifier.Inspect(text, dead: false);
        Assert.Equal(PaneState.Idle, verdict.State);
        Assert.Equal(BackgndReason.None, verdict.Reason);
    }

    [Theory]
    // The real captured shapes, not the hand-written screens above. These are the ones
    // that must yield an agent-only reason: the footer carries no counter (agents are
    // never counted there), so a pane whose only outstanding work is a sub-agent is
    // visible solely as the panel row - and it is exactly this case that must not admit
    // the grace-period backstop.
    [InlineData("claude-backgnd-subagent-real.txt")]
    [InlineData("claude-backgnd-subagent.txt")]
    [InlineData("claude-backgnd-subagent-fresh.txt")]
    public void Real_subagent_fixtures_report_the_agent_reason(string fixture)
    {
        var verdict = ClaudeClassifier.Inspect(Fixture(fixture), dead: false);
        Assert.Equal(PaneState.Backgnd, verdict.State);
        Assert.Equal(BackgndReason.BackgroundAgent, verdict.Reason);
    }

    [Fact]
    public void Real_shell_fixture_reports_the_counter_reason()
    {
        // The dev-server side of the split, from the same real-capture family. This one
        // must keep admitting the backstop, or a shell that never exits withholds the
        // chime forever - the behaviour the grace period exists for.
        var verdict = ClaudeClassifier.Inspect(Fixture("claude-backgnd.txt"), dead: false);
        Assert.Equal(PaneState.Backgnd, verdict.State);
        Assert.Equal(BackgndReason.BackgroundTask, verdict.Reason);
    }
}
