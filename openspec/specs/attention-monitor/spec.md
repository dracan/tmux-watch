# attention-monitor Specification

## Purpose
TBD - created by archiving change add-copilot-pane-watcher. Update Purpose after archive.
## Requirements
### Requirement: Poll watched panes on an interval

The system SHALL poll the discovered Copilot panes on a configurable interval (default in the 1–2 second range), capturing each pane read-only and classifying its state on every tick.

#### Scenario: Interval respected

- **WHEN** the poll interval is configured to N seconds
- **THEN** the system re-enumerates and re-classifies watched panes approximately every N seconds

#### Scenario: Read-only capture only

- **WHEN** the system inspects a pane during a poll
- **THEN** it uses only read-only psmux verbs (`lsp`, `capture-pane -p`, `display-message -p`) and never sends input to the pane

### Requirement: Per-pane state machine

The system SHALL maintain the last known classified state for each watched pane id and the time at which the pane entered that state.

#### Scenario: State and time-in-state tracked

- **WHEN** a pane's classified state is unchanged across polls
- **THEN** the system retains the state and reports an increasing time-in-state

#### Scenario: State transition recorded

- **WHEN** a pane's classified state differs from its last known state
- **THEN** the system records the new state and resets time-in-state

### Requirement: Edge-triggered attention events

The system SHALL emit an attention event exactly once when a pane transitions INTO the
WAITING state, and exactly once when a pane transitions INTO the DONE state, and SHALL NOT
re-emit while the pane remains in that state. The system SHALL NOT emit a separate
attention event for transitions into IDLE, nor for transitions into BACKGND - BACKGND is a
quiet state that defers rather than raises attention.

#### Scenario: Entering WAITING notifies once

- **WHEN** a pane transitions from WORKING (or IDLE) to WAITING
- **THEN** the system emits a single attention event for that pane

#### Scenario: Entering DONE notifies once

- **WHEN** a pane transitions from WORKING to DONE (turn completed)
- **THEN** the system emits a single attention event for that pane

#### Scenario: Remaining in an attention state does not repeat

- **WHEN** a pane stays WAITING, or stays DONE, across multiple consecutive polls
- **THEN** no further attention events are emitted for that pane until it leaves that state

#### Scenario: Leaving the attention state clears the event

- **WHEN** a WAITING pane transitions to WORKING after the user responds, or a DONE pane is acknowledged
- **THEN** the system clears the pane's outstanding attention state

#### Scenario: Transitions into IDLE do not notify

- **WHEN** a pane enters IDLE by any path that is not promoted to DONE (a fresh idle pane, or WAITING to IDLE)
- **THEN** the system emits no attention event

#### Scenario: Transitions into BACKGND do not notify

- **WHEN** a pane enters BACKGND by any path, including directly from WORKING
- **THEN** the system emits no attention event at that transition

#### Scenario: A blocking prompt during BACKGND still notifies

- **WHEN** a BACKGND pane transitions to WAITING
- **THEN** the system emits a single WAITING attention event and the held completed-turn announcement is discarded rather than emitted separately

### Requirement: OS-level notification on attention events

The system SHALL raise a configurable operating-system notification (such as a sound, toast, or window flash) when an attention event fires, so the user is alerted while looking at another window. Entering DONE SHALL use the same notification channel and cue as entering WAITING.

#### Scenario: Notification raised on entering WAITING

- **WHEN** an attention event fires for a pane entering WAITING
- **THEN** the system triggers the configured OS notification channel

#### Scenario: Notification raised on entering DONE

- **WHEN** an attention event fires for a pane entering DONE
- **THEN** the system triggers the same configured OS notification channel used for WAITING

### Requirement: No mid-stream false attention

The system SHALL NOT report a WORKING pane as WAITING merely because its output briefly pauses; a pane is only WAITING when it matches the WAITING detection rules.

#### Scenario: Paused work is not flagged

- **WHEN** a WORKING pane produces no new output for a poll interval but still shows the `Working` spinner indicator
- **THEN** the pane remains WORKING and no attention event is emitted

### Requirement: Resilience to pane disappearance

The system SHALL continue running when a watched pane disappears or its server becomes unavailable mid-watch.

#### Scenario: Watched pane vanishes

- **WHEN** a watched pane id is no longer present during a poll
- **THEN** the system removes it from tracking and continues monitoring the remaining panes without error

### Requirement: Derive DONE from turn completion

The monitor SHALL promote a pane the classifier reports as IDLE to the DONE state when the
pane's immediately preceding tracked state was WORKING, and SHALL promote a pane reported
as IDLE or BACKGND to DONE when it carries an unannounced completed turn released as
described in "Defer the completed-turn announcement while a background task runs". The
classifier SHALL remain stateless and SHALL NOT itself produce DONE; DONE is derived only
from the monitor's per-pane state history.

A pane whose classifier report is IDLE but whose preceding tracked state was anything
other than WORKING (a freshly discovered pane, or a WAITING pane the user dismissed
without further work) SHALL remain IDLE.

**First sight carries no history, so no path out of BACKGND announces anything.** A pane
first seen in BACKGND has no observed completed turn: neither its shell exiting nor the
grace period expiring SHALL promote it to DONE or notify. This is the same principle that
keeps a freshly discovered IDLE pane out of DONE, applied to every exit from BACKGND.

Once promoted, a pane SHALL remain DONE while its classifier report stays IDLE or BACKGND,
until it is acknowledged or it leaves both (re-entering WORKING or WAITING).

#### Scenario: Finishing a turn promotes IDLE to DONE

- **WHEN** a pane the monitor last saw WORKING is classified IDLE on the current poll
- **THEN** the monitor records the pane's state as DONE, not IDLE

#### Scenario: Fresh idle pane is not DONE

- **WHEN** a pane is discovered for the first time already classifying IDLE
- **THEN** the monitor records it as IDLE, not DONE

#### Scenario: Answered prompt returning to idle is not DONE

- **WHEN** a pane transitions from WAITING to IDLE without an intervening WORKING
- **THEN** the monitor records it as IDLE, not DONE

#### Scenario: Fresh BACKGND pane never announces

- **WHEN** a pane is discovered for the first time already classifying BACKGND, and its shell later exits or the grace period elapses
- **THEN** the monitor records it as IDLE (or keeps it BACKGND) and emits no attention event, because no completed turn was observed

#### Scenario: DONE persists until acknowledged or state changes

- **WHEN** a DONE pane keeps classifying as IDLE or BACKGND across subsequent polls and is not acknowledged
- **THEN** the monitor keeps the pane in DONE, and it leaves DONE only on acknowledgement or when it next classifies as WORKING or WAITING

### Requirement: Acknowledge a DONE pane

The system SHALL provide an acknowledgement operation that returns a DONE pane to IDLE and clears its contribution to the aggregate DONE cue (notification and pointer). Acknowledgement SHALL be driven by an explicit user action, never by a pane merely holding client focus, so a pane that already has focus when it enters DONE is not auto-acknowledged. An acknowledged pane SHALL NOT re-enter DONE until it next completes another WORKING to IDLE cycle.

#### Scenario: Acknowledgement returns a pane to IDLE

- **WHEN** a DONE pane is acknowledged
- **THEN** the monitor records the pane as IDLE and it no longer contributes to the DONE aggregate

#### Scenario: Already-focused pane is not auto-acknowledged

- **WHEN** a pane that currently holds client focus transitions into DONE and no acknowledgement action is taken
- **THEN** the pane remains DONE

#### Scenario: Acknowledged pane re-enters DONE only after more work

- **WHEN** an acknowledged (now IDLE) pane is polled again while still idle
- **THEN** it stays IDLE, and it becomes DONE again only after it is next seen WORKING and then IDLE

### Requirement: Non-agent panes are inert in the snapshot

The monitor SHALL carry the non-agent pane inventory through onto its per-tick snapshot
so a single poll serves both the attention view and the pane inventory. Inventory entries
MUST remain inert: the monitor MUST NOT capture them, classify them, create per-pane
state-machine entries for them, emit attention events for them, or let them influence the
aggregate pointer cue. Enumeration failures SHALL leave the inventory empty for that tick
without disturbing the retained agent-pane state.

#### Scenario: Inventory reaches the snapshot without classification

- **WHEN** a poll enumerates both agent panes and non-agent panes
- **THEN** the snapshot contains the classified agent panes and the non-agent inventory, and no capture or classification was performed for any inventory entry

#### Scenario: Non-agent panes raise no attention events

- **WHEN** non-agent panes appear, change their foreground process, or disappear between polls
- **THEN** the monitor emits no attention event and raises no notification for them

#### Scenario: Non-agent panes do not affect the pointer aggregate

- **WHEN** the inventory is non-empty and no agent pane is WAITING or DONE
- **THEN** the aggregate pointer state is normal

#### Scenario: Enumeration failure yields an empty inventory

- **WHEN** the multiplexer enumeration fails on a tick
- **THEN** the snapshot reports the error, carries an empty inventory, and retains the previously tracked agent-pane states

### Requirement: Defer the completed-turn announcement while a background task runs

The monitor SHALL treat a pane that leaves WORKING for BACKGND as having an **unannounced
completed turn**, and SHALL hold the DONE promotion and its notification rather than
firing them immediately. The pane SHALL show as BACKGND and SHALL NOT contribute to any
"needs you" aggregate while held.

The held announcement SHALL be released by whichever comes first:

- the background task exiting, so the pane classifies IDLE - at which point the monitor
  promotes it to DONE and notifies exactly once; or
- a configurable **grace period** (default 120 seconds) elapsing while the pane remains
  BACKGND - at which point the monitor promotes it to DONE and notifies exactly once, even
  though a shell is still running.

The grace period exists so a background task that never exits (a long-running dev server)
cannot silently withhold the completed-turn announcement forever. It SHALL be long enough
that a transient misclassification cannot reach it.

Once promoted by the grace period, a pane SHALL remain DONE while its classifier report is
BACKGND as well as while it is IDLE, so it does not fall back to BACKGND on the next poll
and undo the announcement.

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

- **WHEN** a BACKGND pane with an unannounced completed turn remains BACKGND for longer than the configured grace period
- **THEN** the monitor promotes it to DONE and emits exactly one attention event, without waiting for the shell to exit

#### Scenario: Grace-period promotion is not undone by continuing BACKGND reports

- **WHEN** a pane promoted to DONE by the grace period keeps classifying BACKGND on subsequent polls
- **THEN** the monitor keeps the pane in DONE and emits no further events

#### Scenario: A new turn started from BACKGND re-arms the announcement

- **WHEN** a BACKGND pane with an unannounced completed turn transitions to WORKING and later back to BACKGND
- **THEN** the earlier held announcement is not emitted, and the new completed turn is held and released in its own right

#### Scenario: Grace period does not fire while the agent is working

- **WHEN** a pane holds a background task across a long WORKING turn
- **THEN** the pane is WORKING throughout, no grace-period timer runs, and no completed-turn event is emitted until the turn actually ends
