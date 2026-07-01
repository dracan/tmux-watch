## ADDED Requirements

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

## MODIFIED Requirements

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
