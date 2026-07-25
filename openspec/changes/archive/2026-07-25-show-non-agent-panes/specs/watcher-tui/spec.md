## ADDED Requirements

### Requirement: Other panes table

The system SHALL render a secondary **Other panes** table listing one row per non-agent
pane, positioned below the agent pane table and above the Paused table. Rows SHALL be
ordered by session name, then window index, then pane index (the multiplexer's own
order), so row positions and their assigned address keys remain stable across polls while
the agent table re-sorts. Each row SHALL show the pane's foreground process in the state
column position, the pane's window name, and the time since that window's last activity
in the time column. The table SHALL be omitted entirely when it would contain no rows.

#### Scenario: Non-agent panes listed in their own table

- **WHEN** the multiplexer has agent panes plus panes running `k9s`, `lazygit`, and a shell
- **THEN** the view shows the agent panes in the main table and the three non-agent panes in a separate Other panes table below it

#### Scenario: Process shown in place of classified state

- **WHEN** a non-agent pane's foreground command is `lazygit`
- **THEN** its row shows `lazygit` in the state column position, visually distinct from an agent row's classified state indicator

#### Scenario: Stable ordering across polls

- **WHEN** agent panes change state and the main table re-sorts between two polls
- **THEN** the Other panes rows keep the same relative order and the same address keys

#### Scenario: Activity time shown for non-agent rows

- **WHEN** a non-agent pane's window was last active some time ago
- **THEN** its row shows the elapsed time since that window's last activity in place of a time-in-state value

#### Scenario: Empty table omitted

- **WHEN** there are no non-agent panes to show
- **THEN** no Other panes table is rendered

### Requirement: Toggle visibility of non-agent panes

The system SHALL provide a keyboard toggle (the `o` key) that shows or hides the Other
panes table, and a second keyboard toggle (the `c` key) that includes or excludes
**companion panes** - non-agent panes sharing a window with at least one agent pane -
from that table. Both toggles SHALL default to enabled, SHALL take effect on the next
render without waiting for a poll, and SHALL be runtime-only state that resets to its
default on each launch. The `c` toggle SHALL have no observable effect while the Other
panes table is hidden.

#### Scenario: Both toggles default to on

- **WHEN** the watcher starts with agent panes, companion panes, and unrelated non-agent panes present
- **THEN** the Other panes table is shown and includes the companion panes

#### Scenario: Hiding the other panes table

- **WHEN** the user presses `o` while the Other panes table is shown
- **THEN** the table and all its rows disappear from the view immediately

#### Scenario: Excluding companion panes

- **WHEN** a window contains an agent pane and a shell pane, and the user presses `c`
- **THEN** the shell pane's row disappears from the Other panes table while non-agent panes in windows without an agent remain

#### Scenario: Companion toggle is inert while others are hidden

- **WHEN** the Other panes table is hidden and the user presses `c`
- **THEN** the view is unchanged

#### Scenario: Toggles reset on launch

- **WHEN** the user hides the Other panes table and then restarts the watcher
- **THEN** the table is shown again

### Requirement: Highlighted row navigation

The system SHALL maintain a single **highlighted row** spanning every visible row across
all rendered tables as one continuous list, moved with the up and down arrow keys. The
highlight SHALL be anchored to the highlighted pane's id, so it follows that pane as
tables re-sort between polls rather than staying at a fixed position. When the
highlighted pane disappears or is hidden by a toggle, the highlight SHALL fall back to
the nearest surviving visible row. The focus marker that indicates the multiplexer's own
current pane SHALL remain a passive indicator and SHALL NOT be a target for any action.

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

#### Scenario: Focus marker is not a target

- **WHEN** the multiplexer's current pane differs from the highlighted row and the user triggers a row action
- **THEN** the action applies to the highlighted row, not to the pane carrying the focus marker

### Requirement: Address any visible row by key

The system SHALL assign an address key to every visible row, numbered continuously in
render order across all tables: the digits `1` through `9` for the first nine rows, then
shift+letter (`A`, `B`, `C`, ...) for the tenth row onward. Pressing a row's address key
SHALL perform the switch action for that row. Address keys SHALL be uppercase-only beyond
the digits, so that lowercase command keys remain unambiguous.

#### Scenario: Digits address the first nine rows

- **WHEN** the view has twelve visible rows and the user presses `3`
- **THEN** the system switches to the pane on the third row

#### Scenario: Shift+letter addresses rows past the ninth

- **WHEN** the view has twelve visible rows and the user presses shift+`A`
- **THEN** the system switches to the pane on the tenth row

#### Scenario: Lowercase keys remain commands

- **WHEN** the user presses lowercase `a`, `c`, `o`, `p`, or `w`
- **THEN** the corresponding command runs and no row is addressed

#### Scenario: Unassigned address key does nothing

- **WHEN** the user presses an address key beyond the number of visible rows
- **THEN** nothing happens

### Requirement: Pause acts on the highlighted row and covers non-agent panes

The system SHALL pause or resume the **highlighted** row when the user presses `p`.
Pausing SHALL move the row into the Paused table and resuming SHALL return it to its
originating table. Non-agent rows SHALL be pausable as a decluttering action; because a
non-agent pane contributes no attention cue, pausing one SHALL NOT alter notifications or
the pointer signal. The Paused table SHALL hold both kinds of row using the same column
set as the other tables, with the state column showing the classified state for agent
rows and the foreground process for non-agent rows.

#### Scenario: Pausing the highlighted agent pane

- **WHEN** the highlighted row is an agent pane and the user presses `p`
- **THEN** that pane moves into the Paused table and stops contributing to the pointer cue

#### Scenario: Pausing a non-agent pane

- **WHEN** the highlighted row is a non-agent pane and the user presses `p`
- **THEN** that row moves into the Paused table, showing its foreground process in the state column, and the pointer cue is unchanged

#### Scenario: Mixed paused table

- **WHEN** both an agent pane and a non-agent pane are paused
- **THEN** both appear in a single Paused table with the same columns, each row's state column reading appropriately for its kind

#### Scenario: Resuming returns the row to its table

- **WHEN** the highlighted row is a paused non-agent pane and the user presses `p`
- **THEN** the row returns to the Other panes table in its multiplexer-ordered position

### Requirement: One-shot output includes non-agent panes

The one-shot snapshot output SHALL list the non-agent panes in addition to the classified
agent panes, identifying each non-agent entry by its location, foreground process, and
window name.

#### Scenario: One-shot lists both kinds of pane

- **WHEN** the user runs the watcher with the one-shot flag while agent and non-agent panes exist
- **THEN** the printed output contains a line for each agent pane with its classified state and a line for each non-agent pane with its foreground process

## MODIFIED Requirements

### Requirement: Switch to a selected pane

The system SHALL provide keyboard actions that switch the attached terminal client to a user-selected pane using multiplexer client-control verbs (`switch-client`, `select-window`, and `select-pane`). The selected pane SHALL be brought into focus precisely, so that a pane sharing a window with other panes is landed on rather than deferring to whichever pane that window last had active. The switch action SHALL be available both by pressing the row's address key and by pressing Enter on the highlighted row, and SHALL work for agent and non-agent rows alike. Switching to a DONE pane SHALL acknowledge it, returning it to IDLE.

#### Scenario: Jump to a waiting pane

- **WHEN** the user selects a WAITING pane and triggers the switch action
- **THEN** the system issues the multiplexer client-control command(s) to bring that pane's session, window, and pane into focus

#### Scenario: Enter switches to the highlighted row

- **WHEN** the user presses Enter while a row is highlighted
- **THEN** the system switches to that row's pane

#### Scenario: Split window lands on the named pane

- **WHEN** the selected row names a pane in a window containing several panes, and that pane is not the window's currently active pane
- **THEN** the system selects that specific pane, not merely its window

#### Scenario: Jump to a non-agent pane

- **WHEN** the user selects a non-agent row and triggers the switch action
- **THEN** the system brings that pane into focus with no capture, classification, or acknowledgement performed

#### Scenario: Jump to a DONE pane acknowledges it

- **WHEN** the user selects a DONE pane and triggers the switch action
- **THEN** the system brings that pane into focus and the pane returns to IDLE (acknowledged)

### Requirement: Read-only guarantee toward watched panes

The system MUST NOT send keystrokes or input into any watched agent pane. The only multiplexer state changes it may cause are focus changes: attaching or selecting the client's own session and window (`switch-client`, `select-window`) and selecting the active pane within a window (`select-pane`). Pane selection is permitted despite being server-visible state observable by other clients, because it is required to land on a specific pane and injects no input. No input-injecting verb (notably `send-keys`) SHALL be present in the permitted verb set, and any attempt to invoke one SHALL be rejected.

#### Scenario: No input is sent to an agent pane

- **WHEN** the user interacts with the watcher TUI in any way
- **THEN** the system never issues `send-keys` (or any input-injecting verb) to a watched agent pane

#### Scenario: Input-injecting verbs stay rejected

- **WHEN** any code path attempts to invoke `send-keys` through the multiplexer access layer
- **THEN** the call is rejected rather than executed

#### Scenario: Pane selection is permitted

- **WHEN** the user switches to a row whose pane is not its window's active pane
- **THEN** the system issues `select-pane` for that pane and no other state-changing verb

### Requirement: Acknowledge a DONE pane without switching

The system SHALL provide a keyboard action (the `a` key) that acknowledges the **highlighted** row when it is a DONE agent pane, returning it to IDLE and clearing its contribution to the DONE cue, without switching the client to that pane. Acknowledgement SHALL be driven by the keystroke, so a pane that already holds focus when it enters DONE is not auto-acknowledged. The action SHALL be a no-op when the highlighted row is not a DONE agent pane, including when it is a non-agent row.

#### Scenario: Ack key clears the highlighted DONE pane

- **WHEN** the highlighted row is a DONE pane and the user presses `a`
- **THEN** the pane returns to IDLE, its DONE cue clears, and the client focus does not move

#### Scenario: Ack key on a non-DONE pane does nothing

- **WHEN** the highlighted row is an agent pane that is not DONE and the user presses `a`
- **THEN** no state change occurs

#### Scenario: Ack key on a non-agent row does nothing

- **WHEN** the highlighted row is a non-agent pane and the user presses `a`
- **THEN** no state change occurs
