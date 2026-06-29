## MODIFIED Requirements

### Requirement: Live status view of watched panes

The system SHALL present a live Spectre.Console view listing each watched agent pane with its **matched agent**, session and window location, current classified state shown via a distinct visual indicator, and its time-in-state.

#### Scenario: States rendered with indicators

- **WHEN** watched panes are in differing states (WAITING, WORKING, IDLE, DEAD)
- **THEN** the view shows each pane with a state-specific indicator and updates as states change

#### Scenario: Mixed agents are distinguishable

- **WHEN** both Copilot and Claude Code panes are being watched
- **THEN** each row shows which agent the pane is, so the two agents' sessions are told apart in one view

#### Scenario: View refreshes with monitor

- **WHEN** the monitor reclassifies panes on a poll
- **THEN** the live view reflects the updated states without requiring user interaction
