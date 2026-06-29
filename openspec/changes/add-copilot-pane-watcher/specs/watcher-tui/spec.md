## ADDED Requirements

### Requirement: Live status view of watched panes

The system SHALL present a live Spectre.Console view listing each watched Copilot pane with its session and window location, current classified state shown via a distinct visual indicator, and its time-in-state.

#### Scenario: States rendered with indicators

- **WHEN** watched panes are in differing states (WAITING, WORKING, IDLE, DEAD)
- **THEN** the view shows each pane with a state-specific indicator and updates as states change

#### Scenario: View refreshes with monitor

- **WHEN** the monitor reclassifies panes on a poll
- **THEN** the live view reflects the updated states without requiring user interaction

### Requirement: Prioritise panes needing attention

The system SHALL order or highlight panes so that those in the WAITING state are surfaced ahead of WORKING, IDLE, and DEAD panes.

#### Scenario: WAITING surfaced first

- **WHEN** at least one pane is WAITING and others are WORKING or IDLE
- **THEN** the WAITING pane(s) appear at the top of, or are visually emphasised in, the view

### Requirement: Switch to a selected pane

The system SHALL provide a keyboard action that switches the attached terminal client to a user-selected pane using psmux client-control verbs (`switch-client` and/or `select-window`).

#### Scenario: Jump to a waiting pane

- **WHEN** the user selects a WAITING pane and triggers the switch action
- **THEN** the system issues the psmux client-control command(s) to bring that pane's session and window into focus

### Requirement: Read-only guarantee toward watched panes

The system MUST NOT send keystrokes or input into any watched Copilot pane; the only psmux state changes it may cause are attaching/selecting the client's own focus (`switch-client`, `select-window`).

#### Scenario: No input is sent to Copilot

- **WHEN** the user interacts with the watcher TUI in any way
- **THEN** the system never issues `send-keys` (or any input-injecting verb) to a watched Copilot pane
