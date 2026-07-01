## Context

`PaneClassifier` is a pure, stateless function: one capture in, one `PaneState` out,
with precedence WAITING → WORKING → IDLE → DEAD. A finished turn and a long-idle pane
render the identical idle input box, so the classifier cannot tell them apart - the
difference lives entirely in the WORKING → IDLE *transition*, which `AttentionMonitor`
already observes via its per-pane `TrackedPane.State` history.

Today the monitor edge-notifies on entering WAITING (always) and optionally on entering
IDLE (`NotifyOnIdle` / `--notify-idle`). The pointer signal drives a red pointer from
the aggregate of non-paused WAITING panes. The TUI sorts WAITING → WORKING → IDLE →
UNKNOWN → DEAD and switches focus to a pane on its number key (focus-only, clears
nothing).

This change adds a DONE status ("turn finished, your move"), surfaces it like a softer
WAITING, and retires the redundant `NotifyOnIdle`.

## Goals / Non-Goals

**Goals:**
- Distinguish a freshly finished pane from a long-idle one, as a first-class status.
- Keep the classifier pure and stateless; derive DONE only in the monitor.
- Surface DONE consistently across the three affected capabilities (monitor
  notification, pointer colour, TUI sort/indicator/ack) with a single acknowledgement
  model.
- Make acknowledgement deliberate so an already-focused pane never self-acks.

**Non-Goals:**
- No fast-turn robustness (a sub-poll WORKING that is never observed yields no DONE);
  accepted because the user is in the window when a turn is that fast.
- No change to how WORKING/WAITING/IDLE are detected from captures.
- No change to Copilot vs Claude detection tokens.

## Decisions

### D1: DONE is monitor-derived, expressed as a `PaneState` value

Add `Done` to the `PaneState` enum, but the *classifier never returns it*. In
`AttentionMonitor.Tick`, after classifying a pane, if the classifier result is `Idle`
and the pane's prior tracked state was `Working`, record `Done` instead. On subsequent
ticks the classifier keeps returning `Idle` while the tracked state is already `Done`,
so the pane stays `Done` until acknowledged (→ `Idle`) or it classifies as `Working` /
`Waiting` again.

Why an enum value rather than a `bool` flag beside `State` (like
`AttentionOutstanding`): the TUI already renders and sorts on `PaneState`, the pointer
already aggregates on it, and "just another value in the status column" is exactly the
user's mental model. A flag would force every consumer to special-case `Idle + flag`.
The cost - the enum now has a value the classifier never produces - is documented and
localised to the promotion site.

Alternatives considered:
- *Flag on `TrackedPane`* - rejected: spreads `Idle && Done` branching across TUI,
  pointer, sort, and notification.
- *Detect DONE in the classifier from the frozen `Crunched for 54s` line* - rejected:
  that line is level, not edge (it lingers in scrollback), so it cannot express
  recency; a stateless classifier would mark every worked-then-idle pane DONE forever.

### D2: Acknowledgement is keystroke-driven, cleared to IDLE, and one-shot per turn

The monitor exposes `Acknowledge(paneId)` that sets a `Done` pane back to `Idle`. It is
invoked only by an explicit TUI keystroke - the pane's number (which also switches
focus) or the new `a` key (focused row, no switch). Because acknowledgement is the
*action*, a pane that already holds focus when it turns DONE is not auto-acked; nothing
polls "is this pane focused."

An acknowledged pane is `Idle` with prior tracked state `Done` (not `Working`), so the
D1 promotion rule does not re-fire - it re-enters DONE only after a genuine new
WORKING → IDLE cycle. This is exactly the "acknowledged pane re-enters DONE only after
more work" requirement.

Alternatives considered:
- *Clear on focus-state* - rejected: an already-focused pane would self-ack the instant
  it finished, defeating the cue (the user's explicit worry).
- *Timeout decay* - rejected: arbitrary, and loses the "still needs me" signal if the
  user steps away.

### D3: Pointer aggregates DONE (green) with WAITING (red) precedence

Compute two non-paused aggregates: any WAITING, any DONE. Red if any WAITING; else green
if any DONE; else normal. This keeps the existing "changes only on aggregate
transitions" behaviour with a third resting colour. Paused panes are excluded from both
aggregates, matching today's waiting rule. Ship a green cursor asset alongside the red
one (arrow + I-beam), selected by the same `shapes` config.

Alternatives considered:
- *Blend / second cursor* - rejected: the OS pointer is a single shape; precedence is
  simpler and matches "red is more urgent."

### D4: Remove `NotifyOnIdle` / `--notify-idle` outright

DONE now always alerts on turn completion (same bell as WAITING) and persists as a
status + pointer, which is strictly more than the old one-shot idle bell offered. Keep
one alert path, not two overlapping ones. Delete the config field, the CLI flag, and the
`AttentionKind.EnteredIdle` event; the monitor's IDLE transitions become silent.

## Risks / Trade-offs

- [Sub-poll turn never observed as WORKING yields no DONE] → Accepted per Non-Goals; the
  user is in the window when a turn is that fast. `--calibrate` and the 2s default keep
  normal turns well within range.
- [Enum has a value the classifier never emits] → Localise promotion to one site in
  `AttentionMonitor`; document that `PaneClassifier` returns only WAITING/WORKING/IDLE/
  DEAD/UNKNOWN and never DONE. A classifier unit test asserts it never returns DONE.
- [A DONE pane that the user resolves directly in the terminal (types a new instruction)
  without touching the watcher] → It leaves IDLE for WORKING on the next poll, which
  clears DONE naturally; if they finish and sit idle again it re-promotes. No stuck
  state.
- [Removing `--notify-idle` is breaking for a config that sets `notifyOnIdle`] → Call it
  out in the proposal/README migration note; the field simply no longer exists.
- [Green cursor asset missing on a host] → Same degrade-to-no-op path as the red asset;
  the pointer signal is already opt-in and crash-safe.

## Migration Plan

Pure internal change plus one asset. Ship the enum value, the monitor promotion +
`Acknowledge`, the pointer green aggregate and asset, and the TUI sort/indicator/`a`
key/switch-acks-DONE together. Remove `notifyOnIdle` from config parsing and the
`--notify-idle` flag; update README/AGENTS status table and options list, and any sample
config. Rollback is reverting the commit. No data migration.

## Open Questions

- Exact green shade / cursor asset styling - pick a green mirroring the shipped red
  arrow; cosmetic, tunable later.
