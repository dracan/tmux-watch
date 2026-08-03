## MODIFIED Requirements

### Requirement: Highlighted row navigation

The system SHALL maintain a single **highlighted row** spanning every visible row across
all rendered tables as one continuous list, moved with the up and down arrow keys. The
highlight SHALL be anchored to the highlighted pane's id, so it follows that pane as
tables re-sort between polls rather than staying at a fixed position. When the
highlighted pane disappears or is hidden by a toggle, the highlight SHALL fall back to
the nearest surviving visible row.

The focus marker that indicates the multiplexer's own current pane SHALL NOT be the
target of any action that operates **on a row**: pausing, acknowledging, and switching all
apply to the highlighted row regardless of where the marker sits. The marker MAY be read
to determine the **session** a new window is created in, which acts on a session rather
than on the marked pane itself.

The highlight marker and the focus marker SHALL be rendered so that they are not
mistaken for one another, and the **focus marker SHALL carry the greater visual weight
of the two**. The focus marker reports a fact about the multiplexer that can change
without any keystroke the watcher sees - a pane switched with the multiplexer's own keys
moves it on the next poll - whereas the highlight marker reports only where the
watcher's own cursor sits. Giving the louder styling to the highlight invites the reader
to treat it as the authoritative statement about the current pane, which it is not. The
highlight marker SHALL nonetheless remain legible, since it names the target of the row
actions.

Where the same focus marker is rendered outside the live view - in one-shot and
calibration output - it SHALL use the same styling as the live view, so a single marker
does not mean different things in different output modes.

#### Scenario: Arrow keys move the highlight across tables

- **WHEN** the highlight is on the last row of the agent table and the user presses the down arrow
- **THEN** the highlight moves to the first row of the Other panes table

#### Scenario: Highlight follows its pane through a re-sort

- **WHEN** the highlighted pane changes state and the agent table re-sorts so that pane moves to a different row position
- **THEN** the highlight is still on that same pane

#### Scenario: Highlight recovers when its pane disappears

- **WHEN** the highlighted pane is closed and no longer appears in the view
- **THEN** the highlight moves to the nearest surviving visible row rather than being lost

#### Scenario: Highlight recovers when its row is hidden by a toggle

- **WHEN** the highlight is on a non-agent row and the user presses `o` to hide that table
- **THEN** the highlight moves to a still-visible row

#### Scenario: Focus marker is not a target for row actions

- **WHEN** the multiplexer's current pane differs from the highlighted row and the user pauses, acknowledges, or switches
- **THEN** the action applies to the highlighted row, not to the pane carrying the focus marker

#### Scenario: Focus marker supplies the session for a new window

- **WHEN** the multiplexer's current pane differs from the highlighted row and the user creates a new window
- **THEN** the window is created in the marked pane's session, and the marked pane itself is otherwise untouched

#### Scenario: Focus marker outweighs the highlight marker

- **WHEN** a row carries both the highlight marker and the focus marker
- **THEN** the focus marker is the more prominent of the two, and the highlight marker is the quieter

#### Scenario: One-shot output matches the live view

- **WHEN** the user runs the one-shot or calibration output and a pane carries the focus marker
- **THEN** that marker is styled as it is in the live view
