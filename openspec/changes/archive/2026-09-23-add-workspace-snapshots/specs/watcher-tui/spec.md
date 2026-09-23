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
naming its target or an explicit CLI workspace import. No lifecycle action SHALL ever be taken automatically, on a timer, or
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
- **THEN** it creates, renames, or destroys nothing; lifecycle changes occur only for explicit keys or CLI imports

#### Scenario: User text is never a command

- **WHEN** a lifecycle verb is invoked with user-entered text
- **THEN** that text occupies only a name, directory, or validated structural argument, never a command position

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

Pause settings SHALL persist across watcher restarts and be shared between instances
observing the same panes. Visibility toggles remain per-run.

#### Scenario: Pause survives restart
- **WHEN** a pane is paused and the watcher restarts while the pane remains alive
- **THEN** it remains paused

## ADDED Requirements

### Requirement: Workspace snapshot keys

The TUI SHALL offer e to export and i to open a modal import prompt accepting a
file path or clipboard input. Results SHALL show the saved snapshot or restore
report location and failures. Prompt input SHALL not activate command/address keys.

#### Scenario: Export key
- **WHEN** the user presses e outside a prompt
- **THEN** the workspace is saved and copied, with the result shown in the view

#### Scenario: Import cancelled
- **WHEN** the user opens import and presses escape
- **THEN** the prompt closes and no lifecycle action runs
