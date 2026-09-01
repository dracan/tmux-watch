## 1. Carry the reason out of the classifier

- [x] 1.1 Add `[Flags] enum BackgndReason { None = 0, BackgroundTask = 1, BackgroundAgent = 2 }` in `src/TmuxWatch/Detection/`, with an XML comment stating why it is a flags enum rather than a pane state: BACKGND keeps one badge, one rank and one precedence position, and exactly one consumer cares about the distinction.
- [x] 1.2 Add `readonly record struct Classification(PaneState State, BackgndReason Reason)` alongside it, documenting that `Reason` is `None` for every state other than `Backgnd`.
- [x] 1.3 Change `PaneClassifier.IsBackgnd` to return `BackgndReason`, testing **both** tokens over the chrome region without short-circuiting, so a screen carrying a counter and an agent row reports both flags.
- [x] 1.4 Make the record-returning method the single implementation of `Classify`, and reduce the existing `PaneState Classify(string?, bool)` to a one-line delegation returning `.State`, so no parallel code path exists.
- [x] 1.5 Confirm `src/TmuxWatch/Program.cs` (the `--once` and `--calibrate` paths) still compiles against the `PaneState` overload with no edit.

## 2. Classifier tests

- [x] 2.1 Extend the existing BACKGND fixtures/tests in `tests/TmuxWatch.Tests/PaneClassifierTests.cs` to assert the reason: counter-only for the shell, monitor, and `· 2 shells · 1 monitor ·` cases; agent-only for the fleet-row cases (including the bare `0s` row and the multi-row case).
- [x] 2.2 Add a fixture carrying **both** a background-task counter and a `◯` agent row below the composer, and assert the reason has both flags set - the case that must not short-circuit.
- [x] 2.3 Assert a Copilot-profile pane with a counter reports `BackgroundTask` alone, pinning that the empty `BackgroundAgentRowPattern` leaves Copilot behaviour unchanged.
- [x] 2.4 Assert the reason does not disturb precedence: a screen with a live spinner line and a `◯` row still classifies WORKING.
- [x] 2.5 Assert every non-BACKGND classification reports `BackgndReason.None`.

## 3. Gate the grace period on the reason

- [x] 3.1 Add `BackgndReason LastBackgndReason` to `src/TmuxWatch/Monitor/TrackedPane.cs`, documenting that it exists to detect the reason *narrowing*, and reset it everywhere `BackgndSince` is cleared today (the IDLE, WORKING and WAITING arms).
- [x] 3.2 Have `AttentionMonitor` classify with the record-returning method and thread the reason into `Promote`.
- [x] 3.3 In `Promote`'s `Backgnd` arm, re-base `BackgndSince = now` when the pane was already BACKGND and its previous reason included `BackgroundAgent` while the current reason does not, then record the current reason. Keep the existing fresh-entry stamp and the `tracked.State == PaneState.Done` early return ahead of the gate.
- [x] 3.4 Gate the grace-period release on `reason == BackgndReason.BackgroundTask`, so an agent-inclusive reason runs no timer however long it lasts. Leave the IDLE, WORKING and WAITING release paths untouched.

## 4. Monitor tests

- [x] 4.1 Assert a counter-only BACKGND pane with a held turn still promotes to DONE after the grace period, and exactly once - the dev-server case must not regress.
- [x] 4.2 Assert an agent-only BACKGND pane with a held turn stays BACKGND across many multiples of the grace period, emitting nothing.
- [x] 4.3 Assert a pane holding for a sub-agent promotes to DONE with exactly one event when it classifies IDLE, and instead discards the hold and re-arms when it goes WORKING.
- [x] 4.4 Assert a both-flags pane past the grace period stays BACKGND and emits nothing.
- [x] 4.5 Assert the re-base: a pane held past the grace period with an agent running is not promoted on the poll where its reason narrows to counter-only, and is promoted only after a further full grace period.
- [x] 4.6 Assert a pane already promoted by the grace period stays DONE when a `◯` row later appears, and is not pulled back to BACKGND.
- [x] 4.7 Assert first sight is still silent: a pane discovered already BACKGND with an agent row emits nothing when that agent finishes.

## 5. Documentation and verification

- [x] 5.1 Rewrite the `AGENTS.md` paragraph beginning "The grace period is deliberately *not* per-kind" - it records the decision this change reverses. State the new rule as the termination guarantee (a dev server may never exit and needs the backstop; a sub-agent always terminates and releases itself), and note that the agent case gets no timer rather than a longer one.
- [x] 5.2 Update the BACKGND description in `AGENTS.md` and the `PaneState.Backgnd` XML comment to mention that the state carries a reason and what it is for.
- [x] 5.3 Run `dotnet build` and `dotnet test` clean.
- [x] 5.4 Verify against the live pane that prompted this. **Done, with one gap recorded.** Live and confirmed: the real pane classified `Backgnd` from its `◯` row with no counter in the footer (so the reason is agent-only), and a pane showing an agent row *and* a live spinner correctly classified `Working`, confirming precedence on a real screen. Also run: the real `AttentionMonitor` against the real tmux server for 167s (80 ticks), which held `Backgnd` throughout and emitted zero events. **That last run does not discriminate the fix** - the probe discovered the pane already in BACKGND, so first sight left `CompletionPending` false and the grace branch was unreachable regardless of the gate. Catching a live `WORKING → BACKGND` edge into a detached sub-agent needs a pane that transitions while the probe watches, which could not be arranged. The gate and the re-base are proven deterministically instead, by fake-clock tests over the real fixture captures, each mutation-checked to fail when its logic is removed.
