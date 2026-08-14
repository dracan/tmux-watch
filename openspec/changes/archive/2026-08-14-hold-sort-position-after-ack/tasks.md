## 1. Configuration

- [x] 1.1 Add `AckHoldSeconds` to `WatchConfig` (double, default `5.0`) with an XML doc comment explaining that it is a settle time after acknowledgement and that `0` disables the hold

## 2. Hold record and ordering

- [x] 2.1 Add an `AckHold` record to `WatcherApp` carrying the pinned `Priority`, the pinned `EnteredAt`, and the `ReleaseAt` deadline
- [x] 2.2 Add a `_ackHolds` dictionary (pane id -> `AckHold`) alongside `_paused`, with a comment on why it is view state and not monitor state
- [x] 2.3 Extend `OrderAll` to take the hold map and substitute the pinned priority and `EnteredAt` for any held pane, keeping the method pure and its existing two-argument behaviour available to callers that hold nothing

## 3. Capturing a hold

- [x] 3.1 Add a static helper that records a hold for a pane from its pre-ack view and a `now`, returning without recording when `AckHoldSeconds` is `0`
- [x] 3.2 Call it from `Activate`, capturing the row's `State` and `EnteredAt` *before* `_monitor.Acknowledge` runs and recording only when `Acknowledge` returned true
- [x] 3.3 Call it from the `a` key handler on the same terms
- [x] 3.4 Confirm re-acknowledging a pane that is already held overwrites its hold rather than stacking

## 4. Releasing a hold

- [x] 4.1 Add a static `ExpireHolds(holds, presentPaneIds, now)` that drops holds past their deadline and holds for panes no longer present
- [x] 4.2 Call it once per tick in `Run`, immediately before `Rebuild`, and nowhere else, so no key handler can release a hold
- [x] 4.3 Remove a row's hold in the pause path so `p` moves the row immediately

## 5. Tests

- [x] 5.1 Ordering: a held pane sorts by its pinned priority, not its current state
- [x] 5.2 Ordering: a held pane keeps its position among same-rank panes (pinned `EnteredAt`, not the reset one)
- [x] 5.3 Expiry: a hold past its deadline is dropped; one before it is kept
- [x] 5.4 Expiry: a hold for an absent pane is dropped regardless of deadline
- [x] 5.5 Absolute hold: a pane classified WORKING, and a pane classified WAITING, both stay pinned until the deadline
- [x] 5.6 Capture: no hold is recorded when `AckHoldSeconds` is `0`, and none when the pane was not DONE
- [x] 5.7 Pause releases the hold
- [x] 5.8 Existing ordering, pause, highlight, and addressing tests still pass unchanged

## 6. Documentation

- [x] 6.1 Document the hold in `AGENTS.md` - why an acknowledged row does not move, and why the deadline is checked only in the poll loop - so it is not later read as a sorting bug
- [x] 6.2 Mention `ackHoldSeconds` where the other config keys are described

## 7. Verification

- [x] 7.1 `dotnet build` and `dotnet test` green
- [x] 7.2 `openspec validate hold-sort-position-after-ack --strict` passes
