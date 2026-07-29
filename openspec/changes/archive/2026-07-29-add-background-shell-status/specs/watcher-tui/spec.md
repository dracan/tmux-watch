## MODIFIED Requirements

### Requirement: Live status view of watched panes

The system SHALL present a live Spectre.Console view listing each watched agent pane with
its **matched agent**, session and window location, current classified state shown via a
distinct visual indicator, and its time-in-state. The DONE state SHALL be rendered with
its own distinct indicator, separate from IDLE. The BACKGND state SHALL likewise be
rendered with its own distinct indicator, separate from both WORKING and IDLE.

State labels SHALL follow the view's urgency convention: states that need the user
(WAITING, DONE) are rendered in upper case, and quiet states (BACKGND, WORKING, IDLE,
DEAD) in lower case. BACKGND is a quiet state and SHALL therefore render lower case,
so the view does not imply it is asking for the user.

The BACKGND label SHALL be no wider than the widest existing label, so adding it does not
reflow the state column.

#### Scenario: States rendered with indicators

- **WHEN** watched panes are in differing states (WAITING, DONE, BACKGND, WORKING, IDLE, DEAD)
- **THEN** the view shows each pane with a state-specific indicator and updates as states change

#### Scenario: DONE is visually distinct from IDLE

- **WHEN** one pane is DONE and another is IDLE
- **THEN** the two rows show different state indicators

#### Scenario: BACKGND is visually distinct from WORKING and IDLE

- **WHEN** one pane is BACKGND, another WORKING, and another IDLE
- **THEN** all three rows show different state indicators

#### Scenario: Quiet states render in lower case

- **WHEN** a pane is BACKGND and another is WAITING
- **THEN** the BACKGND row's label is lower case like `working` and `idle`, while the WAITING row's label remains upper case

#### Scenario: Mixed agents are distinguishable

- **WHEN** both Copilot and Claude Code panes are being watched
- **THEN** each row shows which agent the pane is, so the two agents' sessions are told apart in one view

#### Scenario: View refreshes with monitor

- **WHEN** the monitor reclassifies panes on a poll
- **THEN** the live view reflects the updated states without requiring user interaction

### Requirement: Prioritise panes needing attention

The system SHALL order or highlight panes so that those needing the user are surfaced
ahead of the rest, using the priority order WAITING, DONE, BACKGND, WORKING, IDLE,
UNKNOWN, DEAD. Both WAITING and DONE are "needs you" states and SHALL sort above BACKGND,
WORKING, and IDLE.

BACKGND SHALL sort immediately above WORKING: it is not a "needs you" state and raises no
notification, but a pane whose agent has finished its turn is closer to needing the user
than one still mid-turn.

#### Scenario: WAITING surfaced first

- **WHEN** at least one pane is WAITING and others are DONE, WORKING, or IDLE
- **THEN** the WAITING pane(s) appear at the top of the view, above the DONE pane(s)

#### Scenario: DONE surfaced above working and idle

- **WHEN** a pane is DONE and others are WORKING or IDLE, with none WAITING
- **THEN** the DONE pane(s) appear at the top of the view, above WORKING and IDLE

#### Scenario: BACKGND sorts below DONE and above working

- **WHEN** panes are in the states DONE, BACKGND, WORKING, and IDLE with none WAITING
- **THEN** the rows appear in the order DONE, BACKGND, WORKING, IDLE
