## ADDED Requirements

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

The system SHALL emit an attention event exactly once when a pane transitions INTO the WAITING state, and SHALL NOT re-emit while the pane remains WAITING. Transitions into IDLE MAY emit a separate, lower-priority event when enabled.

#### Scenario: Entering WAITING notifies once

- **WHEN** a pane transitions from WORKING (or IDLE) to WAITING
- **THEN** the system emits a single attention event for that pane

#### Scenario: Remaining WAITING does not repeat

- **WHEN** a pane stays WAITING across multiple consecutive polls
- **THEN** no further attention events are emitted for that pane until it leaves WAITING

#### Scenario: Leaving WAITING clears the event

- **WHEN** a WAITING pane transitions to WORKING after the user responds
- **THEN** the system clears the pane's outstanding attention state

### Requirement: OS-level notification on attention events

The system SHALL raise a configurable operating-system notification (such as a sound, toast, or window flash) when an attention event fires, so the user is alerted while looking at another window.

#### Scenario: Notification raised on attention event

- **WHEN** an attention event fires for a pane entering WAITING
- **THEN** the system triggers the configured OS notification channel

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
