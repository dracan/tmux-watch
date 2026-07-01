## ADDED Requirements

### Requirement: Acknowledge a DONE pane without switching

The system SHALL provide a keyboard action (the `a` key) that acknowledges the focused DONE pane, returning it to IDLE and clearing its contribution to the DONE cue, without switching the client to that pane. Acknowledgement SHALL be driven by the keystroke, so a pane that already holds focus when it enters DONE is not auto-acknowledged. The action SHALL be a no-op when the focused pane is not DONE.

#### Scenario: Ack key clears the focused DONE pane

- **WHEN** the focused pane is DONE and the user presses `a`
- **THEN** the pane returns to IDLE, its DONE cue clears, and the client focus does not move

#### Scenario: Ack key on a non-DONE pane does nothing

- **WHEN** the focused pane is not DONE and the user presses `a`
- **THEN** no state change occurs

## MODIFIED Requirements

### Requirement: Live status view of watched panes

The system SHALL present a live Spectre.Console view listing each watched agent pane with its **matched agent**, session and window location, current classified state shown via a distinct visual indicator, and its time-in-state. The DONE state SHALL be rendered with its own distinct indicator, separate from IDLE.

#### Scenario: States rendered with indicators

- **WHEN** watched panes are in differing states (WAITING, DONE, WORKING, IDLE, DEAD)
- **THEN** the view shows each pane with a state-specific indicator and updates as states change

#### Scenario: DONE is visually distinct from IDLE

- **WHEN** one pane is DONE and another is IDLE
- **THEN** the two rows show different state indicators

#### Scenario: Mixed agents are distinguishable

- **WHEN** both Copilot and Claude Code panes are being watched
- **THEN** each row shows which agent the pane is, so the two agents' sessions are told apart in one view

#### Scenario: View refreshes with monitor

- **WHEN** the monitor reclassifies panes on a poll
- **THEN** the live view reflects the updated states without requiring user interaction

### Requirement: Prioritise panes needing attention

The system SHALL order or highlight panes so that those needing the user are surfaced ahead of the rest, using the priority order WAITING, DONE, WORKING, IDLE, UNKNOWN, DEAD. Both WAITING and DONE are "needs you" states and SHALL sort above WORKING and IDLE.

#### Scenario: WAITING surfaced first

- **WHEN** at least one pane is WAITING and others are DONE, WORKING, or IDLE
- **THEN** the WAITING pane(s) appear at the top of the view, above the DONE pane(s)

#### Scenario: DONE surfaced above working and idle

- **WHEN** a pane is DONE and others are WORKING or IDLE, with none WAITING
- **THEN** the DONE pane(s) appear at the top of the view, above WORKING and IDLE

### Requirement: Switch to a selected pane

The system SHALL provide a keyboard action that switches the attached terminal client to a user-selected pane using psmux client-control verbs (`switch-client` and/or `select-window`). Switching to a DONE pane SHALL acknowledge it, returning it to IDLE.

#### Scenario: Jump to a waiting pane

- **WHEN** the user selects a WAITING pane and triggers the switch action
- **THEN** the system issues the psmux client-control command(s) to bring that pane's session and window into focus

#### Scenario: Jump to a DONE pane acknowledges it

- **WHEN** the user selects a DONE pane and triggers the switch action
- **THEN** the system brings that pane into focus and the pane returns to IDLE (acknowledged)
