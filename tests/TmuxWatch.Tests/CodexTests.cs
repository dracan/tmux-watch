using TmuxWatch.Config;
using TmuxWatch.Detection;
using TmuxWatch.Discovery;
using TmuxWatch.Monitor;
using TmuxWatch.Notifications;

namespace TmuxWatch.Tests;

public class CodexTests
{
    private static readonly PaneClassifier Classifier = new(WatchConfig.CodexProfile());

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", $"codex-{name}.txt"));

    [Theory]
    [InlineData("approval-command", PaneState.Waiting)]
    [InlineData("approval-edit", PaneState.Waiting)]
    [InlineData("question-options", PaneState.Waiting)]
    [InlineData("question-freeform", PaneState.Waiting)]
    [InlineData("question-notes", PaneState.Waiting)]
    [InlineData("question-wrapped", PaneState.Waiting)]
    [InlineData("question-submit-all", PaneState.Waiting)]
    [InlineData("working", PaneState.Working)]
    [InlineData("working-queued", PaneState.Working)]
    [InlineData("working-terminal-summary", PaneState.Working)]
    [InlineData("idle", PaneState.Idle)]
    [InlineData("idle-after-work", PaneState.Idle)]
    [InlineData("idle-stale-prompts", PaneState.Idle)]
    public void Classifies_verified_screen_shapes(string fixture, PaneState expected) =>
        Assert.Equal(expected, Classifier.Classify(Fixture(fixture), dead: false));

    [Theory]
    [InlineData("\u2022", "Working", "0s")]
    [InlineData("\u25E6", "Analyzing", "1m 17s")]
    [InlineData("\u2022", "Waiting for a tool", "2h 3m 4s")]
    public void Live_work_does_not_depend_on_action_label_or_spinner_frame(string glyph, string label, string elapsed)
    {
        var screen = $"{glyph} {label} ({elapsed} \u2022 esc to interrupt)\n" + Fixture("idle");
        Assert.Equal(PaneState.Working, Classifier.Classify(screen, false));
    }

    [Theory]
    [InlineData("\u00b7 1 background terminal running")]
    [InlineData("\u00b7 2 background terminals running \u00b7 /ps to view")]
    public void Terminal_summary_requires_the_live_status_prefix(string suffix)
    {
        Assert.Equal(PaneState.Working, Classifier.Classify("\u25e6 Working (3s \u2022 esc to interrupt) " + suffix + "\n" + Fixture("idle"), false));
        Assert.Equal(PaneState.Idle, Classifier.Classify(suffix + "\n" + Fixture("idle"), false));
    }

    [Theory]
    [InlineData("approval-command")]
    [InlineData("question-freeform")]
    [InlineData("question-notes")]
    public void Blocking_input_takes_precedence_over_live_work(string fixture)
    {
        var screen = "\u25E6 Working (3s \u2022 esc to interrupt)\n" + Fixture(fixture);
        Assert.Equal(PaneState.Waiting, Classifier.Classify(screen, false));
    }

    [Theory]
    [InlineData("Press enter to confirm or esc to cancel")]
    [InlineData("Press enter to confirm or esc to go back")]
    [InlineData("enter to submit answer | esc to interrupt")]
    public void Footer_can_identify_a_prompt_when_its_choices_are_outside_the_tail(string footer) =>
        Assert.Equal(PaneState.Waiting, Classifier.Classify(footer, false));

    [Theory]
    [InlineData("The help text says esc to interrupt.")]
    [InlineData("The agent is Working on a background task.")]
    [InlineData("A background agent completed its task.")]
    [InlineData("Example: \u2022 Working (4s \u2022 esc to interrupt)")]
    [InlineData("\u2022 Worked for 4s")]
    public void Ordinary_transcript_prose_is_not_live_work(string prose) =>
        Assert.Equal(PaneState.Idle, Classifier.Classify(prose + "\n" + Fixture("idle"), false));

    [Fact]
    public void Typed_text_does_not_activate_waiting_or_working()
    {
        var screen = "\u203A explain enter to submit answer and esc to interrupt and \u203A 1. Yes";
        Assert.Equal(PaneState.Idle, Classifier.Classify(screen, false));
    }

    [Fact]
    public void Submitted_prompt_echo_does_not_expose_old_waiting_footer()
    {
        var screen = "\u203A Previous prompt\n" + Fixture("question-freeform") + "\n" + Fixture("idle");
        Assert.Equal(PaneState.Idle, Classifier.Classify(screen, false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unrecognized screen")]
    public void Missing_evidence_is_unknown(string? screen) =>
        Assert.Equal(PaneState.Unknown, Classifier.Classify(screen, false));

    [Fact]
    public void Dead_flag_wins_over_visible_ui() =>
        Assert.Equal(PaneState.Dead, Classifier.Classify(Fixture("working"), true));

    [Fact]
    public void New_optional_patterns_can_be_disabled()
    {
        var profile = WatchConfig.CodexProfile();
        profile.WaitingChromePattern = "";
        profile.WorkingLinePattern = "";
        var classifier = new PaneClassifier(profile);
        Assert.Equal(PaneState.Idle, classifier.Classify(Fixture("idle"), false));
        Assert.Equal(PaneState.Unknown, classifier.Classify("esc to interrupt", false));
    }

    [Fact]
    public void Default_inventory_includes_codex_and_existing_agents()
    {
        var fake = new FakeTmuxClient
        {
            ListOutput = "%1|s|0|0|codex|0\n%2|s|1|0|codex.exe|0\n%3|s|2|0|claude|0\n%4|s|3|0|copilot|0\n%5|s|4|0|bash|0",
        };
        var inventory = new PaneDiscovery(fake, new WatchConfig(), selfPaneId: "").DiscoverPanes();
        Assert.Equal(new[] { "codex", "codex", "claude", "copilot" }, inventory.AgentPanes.Select(p => p.AgentId));
        Assert.Equal("%5", Assert.Single(inventory.OtherPanes).Id);
        Assert.Empty(fake.CapturedPanes);
    }

    [Fact]
    public void Explicit_profiles_replace_defaults()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|codex|0" };
        var cfg = new WatchConfig { Agents = new() { WatchConfig.ClaudeProfile() } };
        var inventory = new PaneDiscovery(fake, cfg, selfPaneId: "").DiscoverPanes();
        Assert.Empty(inventory.AgentPanes);
        Assert.Single(inventory.OtherPanes);
    }

    private static AttentionMonitor Monitor(FakeTmuxClient fake)
    {
        var cfg = new WatchConfig();
        return new AttentionMonitor(new PaneDiscovery(fake, cfg, selfPaneId: ""), fake, cfg, new NullNotifier());
    }

    [Fact]
    public void Fresh_idle_is_quiet_then_an_observed_turn_finishes_and_can_be_acknowledged()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|codex|0" };
        fake.Captures["%1"] = Fixture("idle");
        var monitor = Monitor(fake);
        var first = monitor.Tick();
        Assert.Equal(PaneState.Idle, Assert.Single(first.Panes).State);
        Assert.Empty(first.Events);

        fake.Captures["%1"] = Fixture("working-queued");
        var working = monitor.Tick();
        Assert.Equal(PaneState.Working, Assert.Single(working.Panes).State);
        Assert.Empty(working.Events);

        fake.Captures["%1"] = Fixture("idle-stale-prompts");
        var done = monitor.Tick();
        Assert.Equal(PaneState.Done, Assert.Single(done.Panes).State);
        Assert.Equal(AttentionKind.EnteredDone, Assert.Single(done.Events).Kind);
        Assert.Empty(monitor.Tick().Events);

        Assert.True(monitor.Acknowledge("%1"));
        var acknowledged = monitor.Tick();
        Assert.Equal(PaneState.Idle, Assert.Single(acknowledged.Panes).State);
        Assert.Empty(acknowledged.Events);
    }

    [Theory]
    [InlineData("approval-command")]
    [InlineData("question-freeform")]
    [InlineData("question-notes")]
    public void Questions_announce_waiting_then_resumed_work_can_finish(string fixture)
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|codex|0" };
        fake.Captures["%1"] = Fixture("working");
        var monitor = Monitor(fake);
        monitor.Tick();

        fake.Captures["%1"] = Fixture(fixture);
        var waiting = monitor.Tick();
        Assert.Equal(PaneState.Waiting, Assert.Single(waiting.Panes).State);
        Assert.Equal(AttentionKind.EnteredWaiting, Assert.Single(waiting.Events).Kind);
        Assert.Empty(monitor.Tick().Events);

        fake.Captures["%1"] = Fixture("working");
        Assert.Empty(monitor.Tick().Events);
        fake.Captures["%1"] = Fixture("idle-after-work");
        Assert.Equal(AttentionKind.EnteredDone, Assert.Single(monitor.Tick().Events).Kind);
    }
}
