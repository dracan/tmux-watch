using TmuxWatch.Config;
using TmuxWatch.Detection;
using TmuxWatch.Discovery;
using TmuxWatch.Monitor;
using TmuxWatch.Notifications;

namespace TmuxWatch.Tests;

public class AttentionMonitorTests
{
    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public void Advance(TimeSpan d) => _now += d;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private const string Working = "────\n ◎ Working esc cancel   Claude Opus 4.8 · 1M context";
    private const string Waiting = "❯ 1. Yes\n↑/↓ to navigate · enter to select · esc to cancel";
    private const string Idle = "❯\n/ commands · ? help · space hold to record   Claude Opus 4.8";

    private static AttentionMonitor Build(FakeTmuxClient fake, out FakeClock clock, WatchConfig? cfg = null)
    {
        cfg ??= new WatchConfig();
        clock = new FakeClock(DateTimeOffset.UnixEpoch);
        // selfPaneId "" pins "not running inside tmux" so an ambient TMUX_PANE in the test
        // host cannot filter a pane out of the inventory under an assertion.
        var discovery = new PaneDiscovery(fake, cfg, selfPaneId: "");
        return new AttentionMonitor(discovery, fake, cfg, new NullNotifier(), clock);
    }

    [Fact]
    public void Entering_waiting_emits_one_event()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Working;
        var monitor = Build(fake, out var clock);

        var first = monitor.Tick();           // working
        Assert.Empty(first.Events);

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = Waiting;         // transition to waiting
        var second = monitor.Tick();

        Assert.Single(second.Events);
        Assert.Equal(AttentionKind.EnteredWaiting, second.Events[0].Kind);
    }

    [Fact]
    public void Remaining_waiting_does_not_repeat()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Waiting;
        var monitor = Build(fake, out _);

        monitor.Tick();                        // first sight: waiting -> event
        var again = monitor.Tick();            // still waiting -> no event

        Assert.Empty(again.Events);
    }

    [Fact]
    public void Leaving_waiting_clears_outstanding()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Waiting;
        var monitor = Build(fake, out _);

        monitor.Tick();
        fake.Captures["%1"] = Working;
        var snap = monitor.Tick();

        var view = Assert.Single(snap.Panes);
        Assert.False(view.AttentionOutstanding);
        Assert.Equal(PaneState.Working, view.State);
    }

    [Fact]
    public void Paused_working_does_not_emit_attention()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Working;
        var monitor = Build(fake, out _);

        monitor.Tick();
        var again = monitor.Tick();            // identical working frame

        Assert.Empty(again.Events);
    }

    [Fact]
    public void Fresh_idle_pane_is_idle_not_done()
    {
        // First sight has no history, so an already-idle pane is genuine IDLE, not a
        // just-finished turn - and it emits no attention event.
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Idle;
        var monitor = Build(fake, out _);

        var snap = monitor.Tick();

        var view = Assert.Single(snap.Panes);
        Assert.Equal(PaneState.Idle, view.State);
        Assert.Empty(snap.Events);
    }

    [Fact]
    public void Finishing_a_turn_promotes_to_done_and_notifies_once()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Working;
        var monitor = Build(fake, out var clock);

        monitor.Tick();                        // working

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = Idle;            // WORKING -> IDLE is a completed turn
        var done = monitor.Tick();

        var view = Assert.Single(done.Panes);
        Assert.Equal(PaneState.Done, view.State);
        Assert.True(view.AttentionOutstanding);
        Assert.Single(done.Events);
        Assert.Equal(AttentionKind.EnteredDone, done.Events[0].Kind);

        // Still idle on the next poll: stays DONE, no repeat event.
        clock.Advance(TimeSpan.FromSeconds(2));
        var again = monitor.Tick();
        Assert.Equal(PaneState.Done, Assert.Single(again.Panes).State);
        Assert.Empty(again.Events);
    }

    [Fact]
    public void Waiting_to_idle_is_idle_not_done()
    {
        // Answering a prompt with no intervening work returns to IDLE, not DONE.
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Waiting;
        var monitor = Build(fake, out _);

        monitor.Tick();                        // waiting
        fake.Captures["%1"] = Idle;
        var snap = monitor.Tick();

        Assert.Equal(PaneState.Idle, Assert.Single(snap.Panes).State);
    }

    [Fact]
    public void Acknowledging_done_returns_to_idle_and_does_not_repromote()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Working;
        var monitor = Build(fake, out _);

        monitor.Tick();                        // working
        fake.Captures["%1"] = Idle;
        monitor.Tick();                        // -> DONE

        Assert.True(monitor.Acknowledge("%1"));
        // The pane is now IDLE and, staying idle, must not bounce back to DONE.
        var after = monitor.Tick();
        var view = Assert.Single(after.Panes);
        Assert.Equal(PaneState.Idle, view.State);
        Assert.False(view.AttentionOutstanding);
        Assert.Empty(after.Events);
    }

    [Fact]
    public void Acknowledge_is_a_noop_when_pane_is_not_done()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Working;
        var monitor = Build(fake, out _);
        monitor.Tick();                        // working, not done

        Assert.False(monitor.Acknowledge("%1"));
        Assert.False(monitor.Acknowledge("%nonexistent"));
    }

    [Fact]
    public void Re_promotes_to_done_only_after_another_work_cycle()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Working;
        var monitor = Build(fake, out _);

        monitor.Tick();                        // working
        fake.Captures["%1"] = Idle;
        monitor.Tick();                        // -> DONE
        monitor.Acknowledge("%1");             // -> IDLE

        fake.Captures["%1"] = Working;
        monitor.Tick();                        // working again
        fake.Captures["%1"] = Idle;
        var snap = monitor.Tick();             // -> DONE again

        Assert.Equal(PaneState.Done, Assert.Single(snap.Panes).State);
    }

    [Fact]
    public void Transient_capture_failure_holds_state_and_timer()
    {
        // A pane WAITING for hours. A single failed/empty capture (host under load)
        // must not flap it to Unknown: the in-state timer stays put and no second
        // chime fires when the capture recovers.
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Waiting;
        var monitor = Build(fake, out var clock);

        var first = monitor.Tick();                // first sight: waiting -> one event
        Assert.Single(first.Events);
        var enteredAt = Assert.Single(first.Panes).EnteredAt;

        clock.Advance(TimeSpan.FromHours(2));
        fake.Captures["%1"] = "";                  // transient empty capture -> Unknown
        var blip = monitor.Tick();

        var heldView = Assert.Single(blip.Panes);
        Assert.Equal(PaneState.Waiting, heldView.State);   // state held
        Assert.Equal(enteredAt, heldView.EnteredAt);       // timer NOT reset
        Assert.Empty(blip.Events);                         // no chime

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = Waiting;             // capture recovers
        var recovered = monitor.Tick();

        var view = Assert.Single(recovered.Panes);
        Assert.Equal(PaneState.Waiting, view.State);
        Assert.Equal(enteredAt, view.EnteredAt);           // still the original timer
        Assert.Empty(recovered.Events);                    // no second chime
    }

    [Fact]
    public void Transient_capture_failure_does_not_miss_done()
    {
        // WORKING -> (capture blip) -> IDLE must still register as a completed turn:
        // holding the prior WORKING state through the blip preserves DONE detection.
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Working;
        var monitor = Build(fake, out _);

        monitor.Tick();                            // working
        fake.Captures["%1"] = "";                  // blip -> Unknown, held as Working
        Assert.Equal(PaneState.Working, Assert.Single(monitor.Tick().Panes).State);

        fake.Captures["%1"] = Idle;                // now genuinely idle
        var done = monitor.Tick();

        Assert.Equal(PaneState.Done, Assert.Single(done.Panes).State);
        Assert.Single(done.Events);
        Assert.Equal(AttentionKind.EnteredDone, done.Events[0].Kind);
    }

    [Fact]
    public void Classifies_pane_when_command_has_exe_extension()
    {
        // Windows reports "copilot.exe"; the monitor must still classify the
        // captured screen rather than treating the pane as Dead.
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot.exe|0" };
        fake.Captures["%1"] = Waiting;
        var monitor = Build(fake, out _);

        var snap = monitor.Tick();

        var view = Assert.Single(snap.Panes);
        Assert.Equal(PaneState.Waiting, view.State);
    }

    [Fact]
    public void Disappearing_pane_is_dropped_after_threshold()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Working;
        var monitor = Build(fake, out _);
        monitor.Tick();

        fake.ListOutput = "";                  // pane gone

        // An absent pane is hidden immediately (no phantom "needs you" row with a
        // dead jump target), while its entry is retained internally for the
        // default threshold of 3 consecutive absences.
        Assert.Empty(monitor.Tick().Panes);
        Assert.Empty(monitor.Tick().Panes);
        var snap = monitor.Tick();             // third absence: entry dropped
        Assert.Empty(snap.Panes);
        Assert.Null(snap.Error);

        // Proof the entry is gone: the id returning idle is first sight (IDLE),
        // not a held WORKING -> IDLE completion (DONE).
        fake.ListOutput = "%1|s|0|0|copilot|0";
        fake.Captures["%1"] = Idle;
        var back = monitor.Tick();
        Assert.Equal(PaneState.Idle, Assert.Single(back.Panes).State);
        Assert.Empty(back.Events);
    }

    [Fact]
    public void Transient_enumeration_gap_holds_all_panes_without_re_chiming()
    {
        // The reported bug: a single empty `lsp` result (host under load) must not wipe
        // every tracked pane and re-add them as "first sight" next tick - which reset
        // all in-state timers and re-fired the WAITING chime for unchanged panes.
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Waiting;
        var monitor = Build(fake, out var clock);

        var first = monitor.Tick();            // first sight: waiting -> one chime
        Assert.Single(first.Events);
        var enteredAt = Assert.Single(first.Panes).EnteredAt;

        clock.Advance(TimeSpan.FromHours(3));
        fake.ListOutput = "";                  // transient empty enumeration
        var gap = monitor.Tick();

        Assert.Empty(gap.Panes);               // hidden while absent (never a phantom row)
        Assert.Empty(gap.Events);              // no chime

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.ListOutput = "%1|s|0|0|copilot|0"; // enumeration recovers
        var recovered = monitor.Tick();

        var view = Assert.Single(recovered.Panes);
        Assert.Equal(PaneState.Waiting, view.State);
        Assert.Equal(enteredAt, view.EnteredAt);   // still the original timer
        Assert.Empty(recovered.Events);            // no second chime
    }

    [Fact]
    public void Reappearing_before_threshold_resets_absence_debounce()
    {
        // A pane that blips out then returns before the drop threshold must fully
        // reset its absence count: two later absences (which would cross the
        // threshold of 3 if the earlier blip had kept counting) must still be
        // tolerated as a transient gap, not end in a drop and a first-sight re-chime.
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Waiting;
        var monitor = Build(fake, out var clock);
        var first = monitor.Tick();             // first sight: one WAITING chime
        Assert.Single(first.Events);
        var enteredAt = Assert.Single(first.Panes).EnteredAt;

        fake.ListOutput = "";                   // absence 1 of 3
        monitor.Tick();
        fake.ListOutput = "%1|s|0|0|copilot|0"; // seen again -> count reset
        monitor.Tick();

        fake.ListOutput = "";                   // absences 1 and 2 of 3 (3 and 4 unreset)
        monitor.Tick();
        monitor.Tick();

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.ListOutput = "%1|s|0|0|copilot|0"; // recovers: must still be the same entry
        var back = monitor.Tick();

        var view = Assert.Single(back.Panes);
        Assert.Equal(enteredAt, view.EnteredAt); // original timer survived both blips
        Assert.Empty(back.Events);               // a drop + re-add would have re-chimed
    }

    [Fact]
    public void Persistent_unknown_surfaces_after_stale_threshold()
    {
        // The Unknown-hold is bounded: a pane that stops classifying entirely (token
        // drift after an agent upgrade, copy-mode, an unrecognized screen) must
        // eventually surface Unknown instead of freezing its stale state forever.
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Working;
        var monitor = Build(fake, out var clock,
            new WatchConfig { UnknownCapturesBeforeStale = 3 });
        monitor.Tick();                        // working

        fake.Captures["%1"] = "";              // classifies Unknown from here on
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(PaneState.Working, Assert.Single(monitor.Tick().Panes).State);
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(PaneState.Working, Assert.Single(monitor.Tick().Panes).State);

        clock.Advance(TimeSpan.FromSeconds(2));
        var stale = monitor.Tick();            // third consecutive Unknown: surfaced
        var view = Assert.Single(stale.Panes);
        Assert.Equal(PaneState.Unknown, view.State);
        Assert.False(view.AttentionOutstanding);

        fake.Captures["%1"] = Working;         // classification recovers
        Assert.Equal(PaneState.Working, Assert.Single(monitor.Tick().Panes).State);
        fake.Captures["%1"] = "";              // a fresh single blip is held again
        Assert.Equal(PaneState.Working, Assert.Single(monitor.Tick().Panes).State);
    }

    [Fact]
    public void Reused_pane_id_with_different_pid_is_first_sight_not_done()
    {
        // A tmux server restart inside the absence-debounce window can hand a
        // brand-new pane an old id (%-ids restart at %0). The pane pid disambiguates:
        // the newcomer must not inherit the old pane's WORKING history and be
        // promoted to a false DONE with a phantom "finished" chime.
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0|w|/p|0|0|100" };
        fake.Captures["%1"] = Working;
        var monitor = Build(fake, out _);
        monitor.Tick();                        // working, pid 100

        fake.ListOutput = "";                  // server gone for one tick
        monitor.Tick();

        fake.ListOutput = "%1|s|0|0|copilot|0|w|/p|0|0|200"; // same id, new process
        fake.Captures["%1"] = Idle;
        var snap = monitor.Tick();

        var view = Assert.Single(snap.Panes);
        Assert.Equal(PaneState.Idle, view.State);
        Assert.Empty(snap.Events);
    }

    [Fact]
    public void Server_failure_keeps_running_with_error()
    {
        var fake = new FakeTmuxClient { ListOutput = "%1|s|0|0|copilot|0" };
        fake.Captures["%1"] = Working;
        var monitor = Build(fake, out _);
        monitor.Tick();

        fake.Started = false;
        fake.ErrorMessage = "server gone";
        var snap = monitor.Tick();

        Assert.NotNull(snap.Error);
        Assert.Single(snap.Panes);             // prior state retained
    }

    // ---- BACKGND: the deferred completed-turn announcement -----------------

    private const string ClaudePane = "%1|s|0|0|claude|0";

    private const string ClaudeWorking =
        "● Earlier output\n ✻ Enchanting… (32s · ↓ 1.4k tokens)\n────\n❯\n────\n  -- INSERT --";

    private const string ClaudeBackgnd =
        "● Earlier output\n────\n❯\n────\n  -- INSERT -- ⏵⏵ auto mode on · 1 shell · ← for agents";

    private const string ClaudeIdle =
        "● Earlier output\n────\n❯\n────\n  -- INSERT -- ⏵⏵ auto mode on (shift+tab to cycle) · ← for agents";

    private const string ClaudeWaiting =
        "❯ 1. Yes\n↑/↓ to navigate · enter to select · esc to cancel";

    private static readonly TimeSpan PastGrace = TimeSpan.FromSeconds(121);

    [Fact]
    public void Finishing_a_turn_into_a_live_shell_is_silent()
    {
        var fake = new FakeTmuxClient { ListOutput = ClaudePane };
        fake.Captures["%1"] = ClaudeWorking;
        var monitor = Build(fake, out var clock);
        monitor.Tick();                        // working

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = ClaudeBackgnd;
        var snap = monitor.Tick();

        Assert.Equal(PaneState.Backgnd, Assert.Single(snap.Panes).State);
        Assert.Empty(snap.Events);             // the chime is deferred, not fired
        Assert.False(snap.Panes[0].AttentionOutstanding);
    }

    [Fact]
    public void Shell_exiting_releases_the_held_announcement_once()
    {
        var fake = new FakeTmuxClient { ListOutput = ClaudePane };
        fake.Captures["%1"] = ClaudeWorking;
        var monitor = Build(fake, out var clock);
        monitor.Tick();

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = ClaudeBackgnd;
        monitor.Tick();                        // silent

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = ClaudeIdle;      // shell exited
        var snap = monitor.Tick();

        Assert.Equal(PaneState.Done, Assert.Single(snap.Panes).State);
        Assert.Equal(AttentionKind.EnteredDone, Assert.Single(snap.Events).Kind);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(monitor.Tick().Events);   // and only once
    }

    [Fact]
    public void Grace_period_releases_a_shell_that_never_exits()
    {
        var fake = new FakeTmuxClient { ListOutput = ClaudePane };
        fake.Captures["%1"] = ClaudeWorking;
        var monitor = Build(fake, out var clock);
        monitor.Tick();

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = ClaudeBackgnd;
        monitor.Tick();

        clock.Advance(PastGrace);              // the shell is a dev server; it never exits
        var snap = monitor.Tick();

        Assert.Equal(PaneState.Done, Assert.Single(snap.Panes).State);
        Assert.Equal(AttentionKind.EnteredDone, Assert.Single(snap.Events).Kind);
    }

    [Fact]
    public void Grace_promotion_is_not_undone_by_continuing_backgnd()
    {
        var fake = new FakeTmuxClient { ListOutput = ClaudePane };
        fake.Captures["%1"] = ClaudeWorking;
        var monitor = Build(fake, out var clock);
        monitor.Tick();

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = ClaudeBackgnd;
        monitor.Tick();

        clock.Advance(PastGrace);
        monitor.Tick();                        // promoted to DONE, chimed

        // The shell is still running, so the classifier keeps reporting BACKGND. DONE has
        // to persist across it or the announcement undoes itself on the next poll.
        for (var i = 0; i < 3; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(2));
            var snap = monitor.Tick();
            Assert.Equal(PaneState.Done, Assert.Single(snap.Panes).State);
            Assert.Empty(snap.Events);
        }
    }

    [Fact]
    public void First_sight_backgnd_never_announces_on_shell_exit()
    {
        // A dev server that predates the watcher. No completed turn was observed, so its
        // shell exiting is not news.
        var fake = new FakeTmuxClient { ListOutput = ClaudePane };
        fake.Captures["%1"] = ClaudeBackgnd;
        var monitor = Build(fake, out var clock);
        monitor.Tick();                        // first sight: backgnd

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = ClaudeIdle;
        var snap = monitor.Tick();

        Assert.Equal(PaneState.Idle, Assert.Single(snap.Panes).State);
        Assert.Empty(snap.Events);
    }

    [Fact]
    public void First_sight_backgnd_never_announces_on_grace_expiry()
    {
        var fake = new FakeTmuxClient { ListOutput = ClaudePane };
        fake.Captures["%1"] = ClaudeBackgnd;
        var monitor = Build(fake, out var clock);
        monitor.Tick();

        clock.Advance(PastGrace);
        var snap = monitor.Tick();

        Assert.Equal(PaneState.Backgnd, Assert.Single(snap.Panes).State);
        Assert.Empty(snap.Events);
    }

    [Fact]
    public void A_new_turn_re_arms_rather_than_double_firing()
    {
        var fake = new FakeTmuxClient { ListOutput = ClaudePane };
        fake.Captures["%1"] = ClaudeWorking;
        var monitor = Build(fake, out var clock);
        monitor.Tick();

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = ClaudeBackgnd;   // turn 1 finishes, held
        monitor.Tick();

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = ClaudeWorking;   // user starts turn 2; turn 1's hold lapses
        Assert.Empty(monitor.Tick().Events);

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = ClaudeBackgnd;   // turn 2 finishes, held again
        Assert.Empty(monitor.Tick().Events);

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = ClaudeIdle;
        var snap = monitor.Tick();

        // Exactly one chime, for turn 2 - not two, and not zero.
        Assert.Equal(AttentionKind.EnteredDone, Assert.Single(snap.Events).Kind);
    }

    [Fact]
    public void Blocking_prompt_during_backgnd_fires_only_waiting()
    {
        var fake = new FakeTmuxClient { ListOutput = ClaudePane };
        fake.Captures["%1"] = ClaudeWorking;
        var monitor = Build(fake, out var clock);
        monitor.Tick();

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = ClaudeBackgnd;
        monitor.Tick();

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = ClaudeWaiting;   // the agent came back with a question
        var snap = monitor.Tick();

        Assert.Equal(AttentionKind.EnteredWaiting, Assert.Single(snap.Events).Kind);

        // The held completion is discarded, not queued behind the prompt: answering it
        // and returning to idle must not produce a second, stale chime.
        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = ClaudeIdle;
        Assert.Empty(monitor.Tick().Events);
    }

    [Fact]
    public void Real_capture_shapes_drive_the_deferred_chime_end_to_end()
    {
        // The inline screens above are hand-written. This runs the same sequence through
        // the actual fixture captures - real footer text, the non-breaking space after
        // the composer glyph, the frozen "· 1 shell still running" transcript line - so
        // the pipeline is exercised on the shape a live pane actually produces.
        static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

        var fake = new FakeTmuxClient { ListOutput = ClaudePane };
        fake.Captures["%1"] = Fixture("claude-working-current.txt");
        var monitor = Build(fake, out var clock);
        Assert.Equal(PaneState.Working, Assert.Single(monitor.Tick().Panes).State);

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = Fixture("claude-backgnd.txt");
        var held = monitor.Tick();
        Assert.Equal(PaneState.Backgnd, Assert.Single(held.Panes).State);
        Assert.Empty(held.Events);

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = Fixture("claude-idle.txt");   // the shell exited
        var released = monitor.Tick();
        Assert.Equal(PaneState.Done, Assert.Single(released.Panes).State);
        Assert.Equal(AttentionKind.EnteredDone, Assert.Single(released.Events).Kind);
    }

    [Fact]
    public void Backgnd_pane_cannot_be_acknowledged()
    {
        var fake = new FakeTmuxClient { ListOutput = ClaudePane };
        fake.Captures["%1"] = ClaudeWorking;
        var monitor = Build(fake, out var clock);
        monitor.Tick();

        clock.Advance(TimeSpan.FromSeconds(2));
        fake.Captures["%1"] = ClaudeBackgnd;
        monitor.Tick();

        Assert.False(monitor.Acknowledge("%1"));   // not DONE, so nothing to clear
    }
}
