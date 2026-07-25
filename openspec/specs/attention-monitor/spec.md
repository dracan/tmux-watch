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

The system SHALL emit an attention event exactly once when a pane transitions INTO the WAITING state, and exactly once when a pane transitions INTO the DONE state, and SHALL NOT re-emit while the pane remains in that state. The system SHALL NOT emit a separate attention event for transitions into IDLE.

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

The monitor SHALL promote a pane the classifier reports as IDLE to the DONE state when the pane's immediately preceding tracked state was WORKING. The classifier SHALL remain stateless and SHALL NOT itself produce DONE; DONE is derived only from the monitor's per-pane state history. A pane whose classifier report is IDLE but whose preceding tracked state was anything other than WORKING (a freshly discovered pane, or a WAITING pane the user dismissed without further work) SHALL remain IDLE. Once promoted, a pane SHALL remain DONE while its classifier report stays IDLE, until it is acknowledged or it leaves IDLE (re-entering WORKING or WAITING).

#### Scenario: Finishing a turn promotes IDLE to DONE

- **WHEN** a pane's tracked state is WORKING and the next poll classifies it as IDLE
- **THEN** the monitor records the pane's state as DONE, not IDLE

#### Scenario: Fresh idle pane is not DONE

- **WHEN** a pane is first discovered already classifying as IDLE, having never been seen WORKING
- **THEN** the monitor records it as IDLE, not DONE

#### Scenario: Answered prompt returning to idle is not DONE

- **WHEN** a pane transitions from WAITING directly to IDLE without an intervening WORKING state
- **THEN** the monitor records it as IDLE, not DONE

#### Scenario: DONE persists until acknowledged or state changes

- **WHEN** a DONE pane keeps classifying as IDLE across subsequent polls and is not acknowledged
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

