## MODIFIED Requirements

### Requirement: Defer the completed-turn announcement while a background task runs

The monitor SHALL treat a pane that leaves WORKING for BACKGND as having an **unannounced
completed turn**, and SHALL hold the DONE promotion and its notification rather than
firing them immediately. The pane SHALL show as BACKGND and SHALL NOT contribute to any
"needs you" aggregate while held.

The held announcement SHALL be released by whichever comes first:

- the background task exiting, so the pane classifies IDLE - at which point the monitor
  promotes it to DONE and notifies exactly once; or
- a configurable **grace period** (default 120 seconds) elapsing while the pane remains
  BACKGND **for shells or monitors only** - at which point the monitor promotes it to DONE
  and notifies exactly once, even though a shell is still running.

The grace period exists so a background task that never exits (a long-running dev server)
cannot silently withhold the completed-turn announcement forever. It SHALL be long enough
that a transient misclassification cannot reach it.

**The grace period SHALL apply only while the pane's BACKGND reason is exclusively a
background-task counter.** While the reason includes a live background-agent row, no grace
timer SHALL run and the held announcement SHALL NOT be released by elapsed time, however
long the sub-agent runs.

The distinction is the termination guarantee, not the kind of work. A dev server may never
exit, so without a backstop its pane would never chime. A detached sub-agent always
terminates and removes its own row when it reports, so the pane releases itself and needs no
backstop. Applying the timer to a sub-agent announces a finished turn that has not finished:
the parent agent is typically waiting to verify the sub-agent's work and carry on, so it is
not the user's move, and because DONE persists across BACKGND the pane then misreports for
the remainder of the sub-agent's run.

A pane holding its announcement for a sub-agent SHALL still be released by every other path
already specified - the pane classifying IDLE once the row disappears, and a new turn
starting (WORKING), which clears the held announcement and arms a fresh one on its own
completion. These are the paths a sub-agent actually takes, so removing the timer removes a
backstop that was never the releasing mechanism for this case.

**When a pane's BACKGND reason narrows from agent-inclusive to counter-only** - a sub-agent
finishing while a shell it started keeps running - the monitor SHALL treat the grace period
as beginning at that moment rather than at the pane's original entry into BACKGND. A pane
that has been BACKGND longer than the grace period SHALL NOT be promoted instantly on the
poll where its reason narrows.

Once promoted by the grace period, a pane SHALL remain DONE while its classifier report is
BACKGND as well as while it is IDLE, so it does not fall back to BACKGND on the next poll
and undo the announcement. This SHALL hold regardless of any later change of reason: a pane
already promoted SHALL NOT be returned to BACKGND by a sub-agent starting.

A pane whose unannounced-completed-turn marker is not set SHALL NOT be announced by either
release path. The marker SHALL be cleared when the pane is announced, when it re-enters
WORKING (a new turn has begun and will set it again on completion), and when it enters
WAITING (that transition raises its own attention event).

#### Scenario: Finishing a turn with a shell running stays quiet

- **WHEN** a pane transitions from WORKING to BACKGND
- **THEN** the monitor records the pane as BACKGND, emits no attention event, and raises no notification

#### Scenario: Shell exiting releases the held announcement

- **WHEN** a BACKGND pane with an unannounced completed turn classifies IDLE because its background task exited
- **THEN** the monitor promotes it to DONE and emits exactly one attention event

#### Scenario: Grace period releases a shell that never exits

- **WHEN** a BACKGND pane whose reason is a background-task counter alone holds an unannounced completed turn and remains BACKGND for longer than the configured grace period
- **THEN** the monitor promotes it to DONE and emits exactly one attention event, without waiting for the shell to exit

#### Scenario: Grace period never releases a pane waiting on a sub-agent

- **WHEN** a BACKGND pane whose reason is a background-agent row holds an unannounced completed turn and remains BACKGND for many multiples of the configured grace period
- **THEN** the pane stays BACKGND, no attention event is emitted, and no notification is raised

#### Scenario: A sub-agent finishing releases the held announcement

- **WHEN** a BACKGND pane holding an unannounced completed turn for a sub-agent classifies IDLE because the agent row disappeared
- **THEN** the monitor promotes it to DONE and emits exactly one attention event, at the moment the sub-agent actually reported

#### Scenario: A sub-agent finishing into a new turn re-arms rather than announces

- **WHEN** a BACKGND pane holding an unannounced completed turn for a sub-agent transitions to WORKING because the parent agent resumed to verify the sub-agent's work
- **THEN** the held announcement is discarded, and the pane announces on the completion of that new turn instead

#### Scenario: A shell alongside a sub-agent suppresses the timer

- **WHEN** a BACKGND pane's reason names both a background-task counter and a background-agent row, and it holds an unannounced completed turn past the grace period
- **THEN** the pane stays BACKGND and emits no attention event, because the reason is not exclusively a counter

#### Scenario: The grace period restarts when a sub-agent finishes but a shell remains

- **WHEN** a pane has held an unannounced completed turn in BACKGND for longer than the grace period with a sub-agent running, and then its reason narrows to a background-task counter alone
- **THEN** the pane is not promoted on that poll, and is promoted to DONE only once a further full grace period elapses

#### Scenario: Grace-period promotion is not undone by continuing BACKGND reports

- **WHEN** a pane promoted to DONE by the grace period keeps classifying BACKGND on subsequent polls
- **THEN** the monitor keeps the pane in DONE and emits no further events

#### Scenario: Grace-period promotion is not undone by a sub-agent starting

- **WHEN** a pane promoted to DONE by the grace period is still BACKGND and a sub-agent row appears, changing its reason to include an agent row
- **THEN** the pane remains DONE and is not returned to BACKGND

#### Scenario: A new turn started from BACKGND re-arms the announcement

- **WHEN** a BACKGND pane with an unannounced completed turn transitions to WORKING and later back to BACKGND
- **THEN** the earlier held announcement is not emitted, and the new completed turn is held and released in its own right

#### Scenario: Grace period does not fire while the agent is working

- **WHEN** a pane holds a background task across a long WORKING turn
- **THEN** the pane is WORKING throughout, no grace-period timer runs, and no completed-turn event is emitted until the turn actually ends

#### Scenario: First sight of a sub-agent pane still announces nothing

- **WHEN** a pane is discovered for the first time already BACKGND with a background-agent row, and that sub-agent later finishes
- **THEN** no attention event is emitted, because no completed turn was observed
