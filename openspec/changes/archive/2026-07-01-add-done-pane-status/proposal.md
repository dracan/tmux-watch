## Why

When a coding agent finishes a turn it drops to an idle input box - the exact same
screen as a pane that has been sitting untouched for an hour. The watcher cannot tell
"just finished, your move" (e.g. an `openspec propose` completed and you can now run
`openspec apply`) from "genuinely idle," so a freshly finished pane is buried at IDLE
priority and the moment to act passes unnoticed. The distinction is not in the capture;
it is in the WORKING to IDLE transition the monitor already observes.

## What Changes

- Add a **DONE** pane status meaning "the agent finished a turn; your move." It is
  derived by the monitor, not the classifier: a pane the classifier reports as IDLE is
  promoted to DONE when its previous state was WORKING. The classifier stays stateless
  and unchanged (it never emits DONE).
- Surface DONE as one of the "needs you" states. Table sort order becomes
  **WAITING, DONE, WORKING, IDLE, UNKNOWN, DEAD**.
- Ring the bell on entering DONE, the same cue used for WAITING.
- Drive the **pointer signal green** while one or more non-paused panes are DONE. When
  both a WAITING and a DONE pane exist, red (WAITING) wins. Normal pointer when neither.
- **Acknowledge** a DONE pane back to IDLE by either pressing its number (the existing
  switch-to-pane action) or a new **`a`** key that clears the focused row without
  switching. Acknowledgement is driven by the keystroke, so a pane that already has
  focus when it turns DONE never self-acknowledges.
- **BREAKING**: Remove the `--notify-idle` flag and its `notifyOnIdle` config field.
  DONE replaces it - the "tell me when a turn finishes" cue is now a first-class,
  persistent status plus a pointer colour rather than a one-shot bell.

## Capabilities

### New Capabilities

<!-- None. DONE is a derived status expressed through existing capabilities. -->

### Modified Capabilities

- `attention-monitor`: derive DONE on the WORKING to IDLE edge; fire the attention
  event/notification on entering DONE; clear a pane from DONE back to IDLE on
  acknowledgement; remove the optional IDLE notification (`notifyOnIdle`).
- `pointer-signal`: add a green pointer for the aggregate DONE state, with WAITING (red)
  taking precedence when both are present.
- `watcher-tui`: render a DONE indicator, re-order the priority so DONE sits directly
  below WAITING, and add acknowledgement (switch-to-pane and a dedicated `a` key).

## Impact

- Code: `Detection/PaneState.cs` (new `Done` value), `Monitor/AttentionMonitor.cs` and
  `Monitor/TrackedPane.cs` (promotion, notification, acknowledgement), `Tui/WatcherApp.cs`
  (sort priority, indicator, `a` key, switch clears DONE), `Pointer/` aggregate + colour,
  `Config/WatchConfig.cs` and `Program.cs` (remove `notifyOnIdle` / `--notify-idle`).
- Config: `notifyOnIdle` key removed; a config that sets it must drop it.
- Docs: README/AGENTS status table and options list.
