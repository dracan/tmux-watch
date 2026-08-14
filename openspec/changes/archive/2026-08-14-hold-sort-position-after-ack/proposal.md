## Why

Acknowledging a pane demotes it instantly. Pressing an address key or Enter on a DONE row
switches to that pane, acknowledges it (DONE -> IDLE, priority 1 -> 4, `EnteredAt` reset),
and re-sorts and repaints in the same frame - before the multiplexer has reported anything
new. The row the user just aimed at leaps down the table in direct response to their own
keystroke.

The cost is mostly perceptual - the user reports a double take on nearly every jump - but
it is occasionally a real misfire: address keys are positional and reassigned every frame,
so the second key of a `2`, deal, `3` triage burst is aimed at a layout that no longer
exists and can land on a pane mid-turn.

## What Changes

- A pane that is acknowledged SHALL keep its **pre-acknowledgement sort key** - both its
  priority rank and its previous `EnteredAt` - for a configurable hold period, so it does
  not move at the moment of the keystroke.
- The hold SHALL be released only on a **poll tick at or after** the deadline, never on a
  keypress-driven rebuild, so the eventual movement never coincides with a keystroke
  either.
- The hold SHALL be **absolute** for its duration: no subsequent classification releases
  it early, in either direction - neither a demotion to WORKING nor a promotion to
  WAITING.
- Pausing a held row (`p`) SHALL release the hold immediately, because that keystroke is
  an explicit request for exactly that movement.
- Only *position* is held. The state badge, its colour, the pointer cue, and notifications
  SHALL continue to reflect the acknowledged state immediately, so `a` still gives
  instant feedback.
- The highlight SHALL continue to track the row by pane id, so the cursor travels with the
  row when the hold releases.
- New config key `ackHoldSeconds` (default `5`); `0` restores today's instant demotion.

Not changing: the attention-first priority order itself, the Other and Paused tables,
acknowledgement semantics in the monitor, and notification behaviour.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `watcher-tui`: the "Prioritise panes needing attention" ordering gains a bounded hold on
  an acknowledged pane's sort key, and the switch and ack actions gain the requirement
  that they do not move the row they act on.

## Impact

- `src/TmuxWatch/Tui/WatcherApp.cs` - `OrderAll` (sort key pinning), `Activate` and the
  `a` key handler (recording a hold), `Rebuild` / `Run` (releasing only on poll ticks),
  `TogglePause` (releasing on pause).
- `src/TmuxWatch/Config/WatchConfig.cs` - new `AckHoldSeconds`.
- `tests/TmuxWatch.Tests/` - ordering tests for the hold, release, and override paths.
- `AGENTS.md` and the keys table - the hold is behaviour a future contributor would
  otherwise read as a sorting bug.
- No change to the multiplexer boundary: no new verbs, no new tmux calls.
