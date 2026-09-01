## Why

The `backgroundGraceSeconds` release exists so a background task that never exits - a dev
server - cannot silently withhold the completed-turn chime forever. It is currently applied
to every BACKGND pane regardless of what is running, and that is wrong for the one kind of
background task that always terminates on its own: a detached sub-agent.

A parent agent waiting on a sub-agent has not handed the turn back to the user at all - it
is waiting to verify the sub-agent's work and carry on. Firing DONE 120 seconds in tells the
user it is their move when it is not, and because DONE is sticky across BACKGND and outranks
it, the row then reads `✓ DONE` for the rest of the sub-agent's run with nothing left on
screen saying background work is still going. Observed live: a pane 16 minutes into a
sub-agent's run, chimed and shown as DONE since minute two.

The classifier already knows which of the two fingerprints fired. It discards that and
returns a bare BACKGND, so the monitor cannot tell the two cases apart.

## What Changes

- The classifier's BACKGND result carries a **reason** distinguishing a background
  task counter (shells/monitors) from a background-agent row (detached sub-agents), and
  reports **both** when both are on screen. BACKGND itself remains one state with one badge
  and one precedence rank; only the reason is new.
- The grace-period release is **gated on the reason**. It applies only when the outstanding
  work is exclusively shells/monitors. A pane whose BACKGND includes a live sub-agent holds
  its announcement with no timer running, and is released the way it always could be - by
  the sub-agent finishing, which returns the pane to IDLE (chime) or to WORKING (a new turn,
  which re-arms and announces in its own right).
- The grace timer's start point is **re-based** when a pane's reason narrows from
  agent-inclusive to shells-only, so a dev server left behind by a finished sub-agent gets a
  full grace period from that moment rather than inheriting an already-expired one.
- No change to classification precedence, to the BACKGND badge, to first-sight silence, or
  to the dev-server behaviour the grace period was written for.

## Capabilities

### New Capabilities

None. Both affected capabilities already exist.

### Modified Capabilities

- `pane-state-detection`: the BACKGND requirement gains a reason on the classifier's result.
  Today the two fingerprints are stated as independently sufficient and interchangeable;
  they become independently sufficient but *distinguishable*, and a screen matching both
  reports both.
- `attention-monitor`: the deferred-announcement requirement gains the reason gate. The
  grace period narrows from "any BACKGND pane" to "a BACKGND pane whose outstanding work is
  only shells/monitors", and the timer re-bases when the reason narrows.

## Impact

- `src/TmuxWatch/Detection/PaneClassifier.cs` - `IsBackgnd` reports which token matched
  instead of a bare bool; `Classify` surfaces it.
- `src/TmuxWatch/Detection/PaneState.cs` (or a sibling) - the reason type. It must not
  become a new `PaneState` member: BACKGND stays one state for badge, rank and precedence.
- `src/TmuxWatch/Monitor/AttentionMonitor.cs` - `Promote`'s `Backgnd` arm gates the grace
  check on the reason and re-bases `BackgndSince` when the reason narrows.
- `src/TmuxWatch/Monitor/TrackedPane.cs` - remembers the last reason, to detect narrowing.
- `AGENTS.md` - the "grace period is deliberately *not* per-kind ... deferred, not
  foreclosed" paragraph describes exactly the decision being reversed and must be rewritten.
- Tests: `tests/TmuxWatch.Tests/PaneClassifierTests.cs` and `AttentionMonitorTests.cs`.
- No config schema change: `backgroundGraceSeconds` keeps its name, default and meaning; it
  simply governs a narrower set of panes. Nothing a user has configured changes behaviour.

**Out of scope**: the sticky-DONE badge in the dev-server case, where a pane correctly
promoted to DONE stops showing that a shell is still running. This change removes that
symptom for sub-agents (they never reach the grace release), and for a dev server the DONE
is genuinely correct - the turn is over and it is the user's move. Surfacing "DONE with work
still outstanding" is a presentation question, and a separate one.
