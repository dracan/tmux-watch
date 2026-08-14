## MODIFIED Requirements

### Requirement: Prioritise panes needing attention

The system SHALL order or highlight panes so that those needing the user are surfaced
ahead of the rest, using the priority order WAITING, DONE, BACKGND, WORKING, IDLE,
UNKNOWN, DEAD. Both WAITING and DONE are "needs you" states and SHALL sort above BACKGND,
WORKING, and IDLE.

BACKGND SHALL sort immediately above WORKING: it is not a "needs you" state and raises no
notification, but a pane whose agent has finished its turn is closer to needing the user
than one still mid-turn.

Ordering SHALL be evaluated against each pane's current state except while that pane is
under an acknowledgement hold, in which case its pre-acknowledgement sort position applies
for the duration of the hold (see "Hold an acknowledged pane's sort position").

#### Scenario: WAITING surfaced first

- **WHEN** at least one pane is WAITING and others are DONE, WORKING, or IDLE
- **THEN** the WAITING pane(s) appear at the top of the view, above the DONE pane(s)

#### Scenario: DONE surfaced above working and idle

- **WHEN** a pane is DONE and others are WORKING or IDLE, with none WAITING
- **THEN** the DONE pane(s) appear at the top of the view, above WORKING and IDLE

#### Scenario: BACKGND sorts below DONE and above working

- **WHEN** panes are in the states DONE, BACKGND, WORKING, and IDLE with none WAITING
- **THEN** the rows appear in the order DONE, BACKGND, WORKING, IDLE

## ADDED Requirements

### Requirement: Hold an acknowledged pane's sort position

Acknowledgement SHALL NOT move the acknowledged row. When a pane is acknowledged - whether
by switching to it or by the ack key - the system SHALL retain that pane's
pre-acknowledgement sort position for a hold period of `ackHoldSeconds` (default 5). The
retained position SHALL include both the pane's priority rank and its time-in-state
ordering, so the row neither changes rank nor reorders among panes of the same rank.

The hold exists to keep the view from moving in response to the user's own keystroke, so
it SHALL be released only on a poll of the multiplexer occurring at or after the hold
deadline. Rebuilds triggered by a keystroke SHALL NOT release a hold, so the eventual
movement never coincides with a keypress either.

The hold SHALL be absolute for its duration: no classification observed while it is in
force releases it early, in either direction. A pane that leaves the "needs you" states
and one that re-enters them are both held until the deadline.

Pausing a held row SHALL release its hold immediately, because that keystroke is an
explicit request for exactly that movement.

The hold SHALL govern position only. The pane's displayed state, its colour, its
contribution to the pointer cue, and notification behaviour SHALL all reflect the
acknowledged state immediately, so acknowledgement is visibly confirmed without the row
moving.

Setting `ackHoldSeconds` to `0` SHALL disable the hold, demoting an acknowledged pane at
once.

#### Scenario: Switching to a DONE pane does not move its row

- **WHEN** the user presses the address key of a DONE row while other panes are WORKING and IDLE
- **THEN** the client switches to that pane, the row's displayed state becomes IDLE, and the row stays in its position with every row's address key unchanged

#### Scenario: Ack key does not move the row either

- **WHEN** the highlighted row is DONE and the user presses `a`
- **THEN** the row's displayed state becomes IDLE and the row stays in its position

#### Scenario: Held row keeps its place among panes of the same rank

- **WHEN** an acknowledged pane is held and other DONE panes are on screen
- **THEN** the held row remains in the same position relative to those DONE rows that it occupied before acknowledgement

#### Scenario: Release happens on a poll at or after the deadline

- **WHEN** the hold deadline has passed and the next poll of the multiplexer completes
- **THEN** the pane is ordered by its current state and the row takes its new position

#### Scenario: Keystrokes before the deadline do not release the hold

- **WHEN** the user moves the highlight or toggles a table's visibility while a hold is in force
- **THEN** the view rebuilds with the held row still in its retained position

#### Scenario: A real state change does not release the hold early

- **WHEN** a held pane is classified WORKING before its hold deadline
- **THEN** the row does not move until the first poll at or after the deadline

#### Scenario: A promotion to WAITING does not release the hold early

- **WHEN** a held pane is classified WAITING before its hold deadline
- **THEN** the row does not move until the first poll at or after the deadline, while its displayed state, pointer cue, and notification reflect WAITING immediately

#### Scenario: Pausing releases the hold at once

- **WHEN** the user presses `p` on a held row
- **THEN** the row moves into the Paused table immediately

#### Scenario: The highlight follows the row when the hold releases

- **WHEN** a held row is the highlighted row and its hold releases
- **THEN** the highlight remains on that same pane in its new position

#### Scenario: Hold disabled by configuration

- **WHEN** `ackHoldSeconds` is `0` and the user acknowledges a DONE pane
- **THEN** the row is reordered by its acknowledged state in the same frame
