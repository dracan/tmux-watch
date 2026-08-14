# watcher-tui Specification

## Purpose
TBD - created by archiving change add-copilot-pane-watcher. Update Purpose after archive.
## Requirements
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

The system SHALL observe three tiers of obligation toward the multiplexer, as follows.

**Inviolable - pane content.** The system MUST NOT send keystrokes or input into any
pane. A pane's content is permanently read-only. No input-injecting verb (notably
`send-keys`, and equally paste-buffer or arbitrary command-running verbs) SHALL be
present in the permitted verb set, and any attempt to invoke one SHALL be rejected.

**Focus.** The system MAY change focus: attaching or selecting the client's own session
and window (`switch-client`, `select-window`) and selecting the active pane within a
window (`select-pane`). Pane selection is permitted despite being server-visible state
observable by other clients, because it is required to land on a specific pane and
injects no input.

**Lifecycle.** The system MAY create - and in future rename or destroy - windows and
panes. Every such action SHALL be taken only in direct response to an explicit keystroke
naming its target. No lifecycle action SHALL ever be taken automatically, on a timer, or
as any part of the poll loop. User-entered text SHALL NEVER be placed in a command
position of a lifecycle verb.

#### Scenario: No input is sent to a pane

- **WHEN** the user interacts with the watcher TUI in any way
- **THEN** the system never issues `send-keys` (or any input-injecting verb) to any pane

#### Scenario: Input-injecting verbs stay rejected

- **WHEN** any code path attempts to invoke `send-keys` through the multiplexer access layer
- **THEN** the call is rejected rather than executed

#### Scenario: Pane selection is permitted

- **WHEN** the user switches to a row whose pane is not its window's active pane
- **THEN** the system issues `select-pane` for that pane and no other state-changing verb

#### Scenario: Window creation is permitted

- **WHEN** the user invokes the new-window action
- **THEN** the create verb is accepted by the multiplexer access layer

#### Scenario: Polling never changes lifecycle

- **WHEN** the watcher polls, reclassifies panes, and finds dead or vanished panes
- **THEN** it creates, renames, or destroys nothing; the only lifecycle changes ever made are those a keystroke asked for

#### Scenario: User text is never a command

- **WHEN** a lifecycle verb is invoked with user-entered text
- **THEN** that text occupies only a name argument, never a command position

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

### Requirement: Address any visible row by key

The system SHALL assign an address key to every visible row, numbered continuously in
render order across all tables: the digits `1` through `9` for the first nine rows, then
shift+letter (`A`, `B`, `C`, ...) for the tenth row onward. Pressing a row's address key
SHALL perform the switch action for that row. Address keys SHALL be uppercase-only beyond
the digits, so that lowercase command keys remain unambiguous. Address keys SHALL NOT
fire while the name prompt is open.

#### Scenario: Digits address the first nine rows

- **WHEN** the view has twelve visible rows and the user presses `3`
- **THEN** the system switches to the pane on the third row

#### Scenario: Shift+letter addresses rows past the ninth

- **WHEN** the view has twelve visible rows and the user presses shift+`A`
- **THEN** the system switches to the pane on the tenth row

#### Scenario: Lowercase keys remain commands

- **WHEN** the user presses lowercase `a`, `c`, `n`, `o`, `p`, or `w`
- **THEN** the corresponding command runs and no row is addressed

#### Scenario: Unassigned address key does nothing

- **WHEN** the user presses an address key beyond the number of visible rows
- **THEN** nothing happens

#### Scenario: Addressing suspended during name entry

- **WHEN** the name prompt is open and the user presses a digit
- **THEN** the digit is inserted into the name and no row is switched to

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

### Requirement: Create a new window from the watcher

The system SHALL provide a keyboard action (the `n` key) that creates a new window in a
target session of the multiplexer. The window SHALL be created **detached**, so the
create itself does not move any client, and the system SHALL then jump to the new window
using focus-only verbs. Because a detached create does not make the new window current,
the system SHALL identify it by the window id the multiplexer reports for the create and
select that id; where a host reports no id, the jump SHALL degrade to switching to the
target session.

The new window has no row until the next poll enumerates it, so the system SHALL clear
the focus marker from every visible row of the target session at the moment of the jump,
leaving no row falsely claiming focus rather than waiting for the next poll to correct
it. Rows of other sessions SHALL be left untouched.

The name entered by the user SHALL be passed only as the window-name argument of the
create verb. It MUST NOT be passed in any command position, so that no typed text can be
executed. When the entered name is empty, the name argument SHALL be omitted entirely and
the multiplexer SHALL apply its own automatic naming. No working directory SHALL be
passed, so the new window starts in the target session's default directory.

The created window is ordinary inventory: it is discovered on the next poll like any
other pane and, running no agent, appears as a row in the Other panes table.

#### Scenario: New window created and jumped to

- **WHEN** the user presses `n`, types a name, and presses enter
- **THEN** the system creates a detached window with that name in the target session, then switches to that session and selects the newly created window by the id reported for it

#### Scenario: Window lands in the named session whatever it is called

- **WHEN** the target session's name is one that could also be read as a window index, such as `0`
- **THEN** the window is still created in that session, rather than at that index in whichever session the multiplexer currently considers active

#### Scenario: Jump degrades without a reported window id

- **WHEN** the multiplexer reports no window id for the create
- **THEN** the system switches to the target session and selects no window, rather than selecting the wrong one

#### Scenario: Stale focus marker cleared on creation

- **WHEN** a window is created in a session whose panes currently carry the focus marker
- **THEN** no row of that session shows the marker afterwards, while rows of other sessions keep theirs

#### Scenario: Creation touches no pane

- **WHEN** a window is created
- **THEN** no pane is captured, no pane is selected, and no input is sent anywhere

#### Scenario: Empty name defers to automatic naming

- **WHEN** the user presses `n` and presses enter without typing anything
- **THEN** the system creates the window with no name argument, leaving the multiplexer to name it

#### Scenario: Typed text never reaches a command position

- **WHEN** the user enters a name that looks like a shell command or an option
- **THEN** the text is passed only as the window-name argument and nothing in it is executed or interpreted as a flag

#### Scenario: New window appears as inventory

- **WHEN** a window created this way is enumerated on the next poll
- **THEN** it appears as a non-agent row in the Other panes table with no capture or classification performed on it

#### Scenario: Cancelling creates nothing

- **WHEN** the user presses `n`, types a name, and presses escape
- **THEN** no window is created and no multiplexer state changes

### Requirement: Target session for a new window

The target session for the new-window action SHALL be the session of the pane carrying
the focus marker. Because the multiplexer tracks an active window and active pane per
session, more than one visible row can carry the marker; in that case the system SHALL
prefer the marked pane whose session matches the highlighted row's session. When no
visible row carries the marker, the highlighted row's session SHALL be used. When there
are no visible rows at all, the action SHALL do nothing.

The target SHALL be captured at the moment the action is invoked and SHALL NOT be
re-resolved when the name is submitted, because polling continues while the user types
and the focus marker may move in the meantime. The captured target SHALL be displayed
alongside the prompt so the user can see where the window will be created.

#### Scenario: Focused pane's session is the target

- **WHEN** a single row carries the focus marker and the user creates a new window
- **THEN** the window is created in that row's session

#### Scenario: Multiple marked panes resolved by the highlight

- **WHEN** two sessions each have a marked pane and the highlighted row is in the second session
- **THEN** the window is created in the second session

#### Scenario: Falls back to the highlighted row

- **WHEN** no visible row carries the focus marker and the user creates a new window
- **THEN** the window is created in the highlighted row's session

#### Scenario: No rows means no action

- **WHEN** there are no visible rows and the user presses `n`
- **THEN** nothing happens

#### Scenario: Target is fixed when the action starts

- **WHEN** the user presses `n` and the focus marker moves to another session before the name is submitted
- **THEN** the window is still created in the session captured when `n` was pressed

#### Scenario: Target is visible while typing

- **WHEN** the name prompt is open
- **THEN** the prompt shows the session the window will be created in

### Requirement: Inline name prompt

The system SHALL collect the new window's name with a prompt rendered **inside the live
view**, below the tables, so the tables remain on screen and continue to refresh on each
poll while the user types. The prompt SHALL NOT tear down and rebuild the live display.

While the prompt is open it SHALL be modal: it consumes every keystroke, so no command
key, address key, or highlight movement fires. Enter SHALL submit the entered name and
escape SHALL cancel, and either SHALL close the prompt and return the view to its normal
key handling.

The prompt SHALL support editing the entered text at any position via a cursor:
printable characters insert at the cursor, backspace deletes the character before it,
delete removes the character after it, left and right move the cursor, home and end move
it to either end, `ctrl+w` deletes the word before the cursor, and `ctrl+u` clears the
line. The cursor position SHALL be visible in the rendered prompt.

The editing behaviour SHALL be implemented as a pure transformation of text and cursor
position by a keystroke, so it is testable without a console.

#### Scenario: Tables stay visible while typing

- **WHEN** the name prompt is open
- **THEN** the agent, Other panes, and Paused tables remain rendered and continue updating on each poll

#### Scenario: Prompt is modal

- **WHEN** the name prompt is open and the user types characters that are otherwise command or address keys
- **THEN** those characters are inserted into the name and no command runs and no row is addressed

#### Scenario: Typo fixed mid-string

- **WHEN** the user has typed a name, moves the cursor left past several characters, and presses backspace and then types a replacement character
- **THEN** the correction is applied at that position and the rest of the name is preserved

#### Scenario: Delete removes forward

- **WHEN** the cursor sits before a character and the user presses delete
- **THEN** that character is removed and the cursor does not move

#### Scenario: Word and line deletion

- **WHEN** the user presses `ctrl+w` with a multi-word name entered
- **THEN** the word before the cursor is removed; and pressing `ctrl+u` clears the whole name

#### Scenario: Cursor bounds are respected

- **WHEN** the cursor is at the start and the user presses left or backspace, or is at the end and presses right or delete
- **THEN** the name and cursor position are unchanged

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

