## 1. State model

- [x] 1.1 Add `Done` to the `PaneState` enum (`Detection/PaneState.cs`) with a doc comment noting it is monitor-derived and never returned by the classifier ("turn finished, your move")
- [x] 1.2 Add a classifier unit test asserting `PaneClassifier.Classify` never returns `PaneState.Done` for any fixture (it only ever emits WAITING/WORKING/IDLE/DEAD/UNKNOWN)

## 2. Monitor: derive, notify, acknowledge

- [x] 2.1 In `AttentionMonitor.Tick`, promote a classified `Idle` to `Done` when the pane's prior tracked state was `Working`; leave IDLE unchanged for fresh panes and for WAITING→IDLE
- [x] 2.2 Keep a promoted pane in `Done` across subsequent idle polls until acknowledged or it re-classifies as WORKING/WAITING
- [x] 2.3 Emit an edge-triggered attention event once on entering `Done`, mirroring the WAITING edge; fire the same OS notification channel/cue
- [x] 2.4 Remove the `EnteredIdle` attention path so transitions into IDLE are silent (no separate idle event)
- [x] 2.5 Add `Acknowledge(paneId)` to the monitor that sets a `Done` pane back to `Idle` and clears its outstanding attention; ensure the D1 promotion does not re-fire until another WORKING→IDLE cycle
- [x] 2.6 Update `TrackedPane` / attention-kind types as needed (add a DONE attention kind, drop the idle one)
- [x] 2.7 Unit-test the monitor: WORKING→IDLE promotes to DONE; fresh IDLE and WAITING→IDLE stay IDLE; DONE persists across polls; entering DONE notifies once and not again; `Acknowledge` returns to IDLE and does not immediately re-promote

## 3. Remove notify-on-idle

- [x] 3.1 Delete the `NotifyOnIdle` config field (`Config/WatchConfig.cs`) and the `--notify-idle` flag parsing/help (`Program.cs`)
- [x] 3.2 Remove references in any sample config and confirm no code path still reads `notifyOnIdle`

## 4. Pointer signal: green for DONE

- [x] 4.1 Compute a second non-paused aggregate (any DONE) alongside the WAITING aggregate; drive the pointer red if any WAITING, else green if any DONE, else normal (changing only on aggregate transitions)
- [x] 4.2 Exclude paused panes from the DONE aggregate, matching the WAITING rule
- [x] 4.3 Ship a green cursor asset (arrow + I-beam) mirroring the red one, selected by the same `shapes` config; keep the degrade-to-no-op and crash-safe restore behaviour
- [x] 4.4 Unit-test the aggregate/precedence: first DONE → green; WAITING present → red wins; WAITING clears while DONE remains → red→green; last DONE acknowledged → normal; paused DONE excluded

## 5. TUI: render, sort, acknowledge

- [x] 5.1 Add a distinct DONE state indicator in the pane table (`Tui/WatcherApp.cs`), separate from IDLE
- [x] 5.2 Update `Priority` sort to WAITING, DONE, WORKING, IDLE, UNKNOWN, DEAD
- [x] 5.3 Make the switch-to-pane action (number key) call `Acknowledge` for a DONE target so jumping to it returns it to IDLE
- [x] 5.4 Add the `a` key: acknowledge the focused pane when it is DONE without switching focus; no-op otherwise; add it to the TUI help/header line
- [x] 5.5 Verify acknowledgement is keystroke-driven only (an already-focused pane entering DONE is not auto-acked)

## 6. Docs and verification

- [x] 6.1 Update README status table (add DONE row), options list (remove `--notify-idle`), pointer-signal section (green for DONE, red precedence), and config example (drop `notifyOnIdle`)
- [x] 6.2 Update AGENTS.md if it references the state set or the notify-idle option
- [x] 6.3 Run `dotnet build` and `dotnet test`; confirm the full suite passes
- [x] 6.4 Run `openspec validate add-done-pane-status` and confirm it is valid
