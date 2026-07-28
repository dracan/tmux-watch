## ADDED Requirements

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

## MODIFIED Requirements

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
