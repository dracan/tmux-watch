## Why

A live Claude pane running a background shell classifies as **Unknown**. Observed on
`0:4.0` (window `jacktive`) and reproduced with `--calibrate`: it was the only pane of
five with a background shell, and the only one that failed to classify.

The cause is a footer slot Claude Code recycles. Its status line composes segments, and
when a background shell exists the `(shift+tab to cycle)` segment is dropped to make room
for the shell count:

```
0:0.0  -- INSERT -- ⏵⏵ auto mode on (shift+tab to cycle) · ← for agents   → Idle
0:4.0  -- INSERT -- ⏵⏵ auto mode on · 1 shell · ← for agents              → Unknown
                                     ^^^^^^^^^^^^^^^^^^^ displaced
```

`shift+tab to cycle` is one of the `claude` profile's two `IdleHints`; the other,
`? for shortcuts`, renders only when the composer is empty. A pane with a background
shell *and* typed text loses both at once, so IDLE detection has nothing to match.

This is worse than a wrong label. DONE is derived solely from the WORKING to IDLE edge,
so a pane that can never reach IDLE **never fires the turn-finished chime**. Every pane
holding a background shell is silently exempt from the watcher's main job. The 30s
`UnknownCapturesBeforeStale` hold only delays surfacing the wrong answer.

Two things are wrong, and this change fixes both.

**The state model is missing a state.** "Agent finished, but background work of its own is still
running" is a real, distinct situation the user cares about, and today it has no
representation. It should be its own quiet state, and the finished-turn chime should be
*deferred* until the shell exits rather than fired immediately or lost entirely.

**IDLE is anchored on the wrong thing.** Every segment of Claude's footer is conditional:

| Segment | Absent when |
| --- | --- |
| `? for shortcuts` | composer has text |
| `(shift+tab to cycle)` | a background shell is running (this bug) |
| `⏵⏵ auto mode on` | permission mode is not auto |
| `-- INSERT --` | vim keybindings off |

Keying IDLE on a recyclable slot guarantees a repeat. This is already the second such
drift in the `claude` profile - `esc to interrupt` vanished out from under WORKING in
`update-claude-statusbar-detection`. The composer box (`❯` prompt between two rule lines)
is present in every live state, in both vim modes, and is not slot-recycled.

## What Changes

- A new **BACKGND** pane state: the agent is not blocked and not working, but background
  work it started is still running.
  - Classifier-detected, not monitor-derived (unlike DONE). The footer's background-task
    counter - the very token that displaced the idle hint - is its positive fingerprint.
  - Covers every kind of task Claude reports in that slot, not only shells. The shipped
    binary renders `count === 1 ? "1 shell" : `${count} shells`` and the same for
    **monitors**, joining both into one segment (`· 2 shells · 1 monitor ·`). Both mean
    the same thing here, and matching only shells would leave a monitor-only pane looking
    plainly IDLE.
  - Precedence becomes WAITING, WORKING, **BACKGND**, IDLE, DEAD. BACKGND sits below
    WORKING because the counter is present while the agent works too.
- **IDLE is re-anchored on the composer box** rather than footer hint tokens: a `❯`
  prompt line that is not a numbered selection cursor. The same anchor also scopes
  BACKGND matching (see below), so one structural fingerprint drives both.
- **The counter is matched only below the composer line.** Claude's transcript region sits
  above the composer and its chrome below, and the transcript routinely contains shell
  prose - `Ran 1 shell command`, and the frozen completion line
  `✻ Cooked for 16s · 1 shell still running` which would otherwise pin a pane in BACKGND
  forever after the shell exited.
- **DONE is deferred, not lost.** A pane going WORKING to BACKGND records an unannounced
  completed turn and stays quiet. It is announced when the shell exits (BACKGND to IDLE
  promotes to DONE and chimes), or after a **2-minute grace period** if the shell outlives
  it - so a pane running `npm run dev` is not silently withheld forever.
- **First sight still never chimes.** A pane first seen in BACKGND has no observed
  completed turn, so neither the shell exiting nor the grace period announces anything.
  This extends the existing "fresh idle pane is not DONE" rule to every path out of
  BACKGND.
- DONE persists across BACKGND as well as IDLE, so a pane promoted by the grace period
  while its shell still runs does not flap back to BACKGND on the next poll.
- TUI: rendered `backgnd` in lowercase, following the existing convention that attention
  states are uppercase (`WAITING`, `DONE`) and quiet states lowercase (`working`, `idle`,
  `dead`). Sort priority WAITING, DONE, **backgnd**, working, idle, unknown, dead.
- New profile tokens (`IdlePromptPattern`, `BackgroundTaskPattern`) and one config key
  (`BackgroundGraceSeconds`, default 120). Both profile tokens default empty for Copilot,
  which keeps its existing `IdleHints` path unchanged.

## Capabilities

### New Capabilities

(None - this extends three existing capabilities.)

### Modified Capabilities

- `pane-state-detection`: adds the BACKGND classification and its composer-scoped
  fingerprint; re-anchors Claude IDLE on the composer box; extends the precedence order.
- `attention-monitor`: adds BACKGND to the per-pane state machine, defers the DONE
  promotion and its chime through BACKGND, adds the grace-period fallback, and extends
  the first-sight rule.
- `watcher-tui`: renders BACKGND and places it in the priority order.

## Impact

- `src/TmuxWatch/Detection/PaneState.cs` - new `Backgnd` member.
- `src/TmuxWatch/Detection/PaneClassifier.cs` - composer-line location, BACKGND check,
  composer-anchored IDLE, extended precedence.
- `src/TmuxWatch/Config/AgentProfile.cs` - `IdlePromptPattern`,
  `BackgroundTaskPattern`, and their compiled helpers.
- `src/TmuxWatch/Config/WatchConfig.cs` - the `claude` profile's new tokens;
  `BackgroundGraceSeconds`.
- `src/TmuxWatch/Monitor/AttentionMonitor.cs`, `TrackedPane.cs` - the completion-pending
  flag, deferred promotion, grace-period timer.
- `src/TmuxWatch/Tui/WatcherApp.cs` - `StateMarkup` and `Priority`.
- `tests/TmuxWatch.Tests/fixtures/` - a scrubbed `claude-backgnd.txt` from the observed
  `jacktive` screen, plus a composer-in-normal-mode capture.
- `AGENTS.md`, `README.md` - document the new state.
- No change to discovery, the tmux access layer, notifications, or the pointer signal.
  BACKGND is quiet, so it contributes nothing to the pointer aggregate.
