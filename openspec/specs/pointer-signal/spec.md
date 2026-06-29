# pointer-signal Specification

## Purpose
TBD - created by archiving change add-pointer-signal. Update Purpose after archive.
## Requirements
### Requirement: Opt-in pointer signal

The system SHALL provide a pointer signal that is disabled by default and enabled only
through configuration. When disabled, the system SHALL never alter the OS pointer.

#### Scenario: Disabled by default

- **WHEN** no pointer-signal configuration is supplied
- **THEN** the system does not change the mouse pointer under any pane state

#### Scenario: Enabled via configuration

- **WHEN** the pointer signal is enabled in configuration
- **THEN** the system drives the mouse pointer from the aggregate waiting state as
  described by the requirements below

### Requirement: Level-triggered from aggregate waiting state

The system SHALL set the waiting pointer while one or more non-paused watched panes
have outstanding attention (WAITING), and SHALL restore the normal pointer once no
non-paused pane has outstanding attention. Paused panes (those parked into the
secondary table) SHALL be excluded from the aggregate. The pointer SHALL reflect the
aggregate state, not any single pane, and SHALL change only on transitions of that
aggregate.

#### Scenario: First pane enters WAITING

- **WHEN** the aggregate goes from no non-paused panes waiting to at least one non-paused
  pane waiting
- **THEN** the system sets the waiting (red) pointer

#### Scenario: Additional pane enters WAITING while already waiting

- **WHEN** a second pane enters WAITING while another is already WAITING
- **THEN** the system does not re-apply the pointer (no redundant OS calls) and the
  pointer remains the waiting colour

#### Scenario: Last waiting pane resolved

- **WHEN** the last remaining non-paused waiting pane leaves WAITING so no non-paused
  pane has outstanding attention
- **THEN** the system restores the normal pointer

#### Scenario: Waiting panes remain

- **WHEN** one of several waiting panes leaves WAITING but at least one non-paused pane is
  still WAITING
- **THEN** the pointer remains the waiting colour

#### Scenario: Waiting pane is paused

- **WHEN** the only waiting pane is paused (parked into the secondary table)
- **THEN** the system restores the normal pointer, and resuming the pane re-arms the
  waiting pointer

### Requirement: Global, out-of-band pointer change

The system SHALL change the actual operating-system mouse pointer across the whole
desktop using an OS call, not a terminal escape sequence. The behaviour SHALL be
identical whether tmux-watch runs inside or outside tmux/PSMUX.

#### Scenario: Effect is desktop-wide

- **WHEN** the waiting pointer is set
- **THEN** the recoloured pointer is visible over every application, not only the
  watcher's terminal, and even when the terminal is minimised

#### Scenario: Independent of tmux nesting

- **WHEN** the pointer signal is exercised with tmux-watch running outside tmux and
  again running inside tmux
- **THEN** the pointer behaviour is the same in both cases

### Requirement: Cross-host backends

The system SHALL select a pointer backend appropriate to the host at runtime: a native
Windows backend that calls `user32` directly when running on Windows, and a backend
that performs the equivalent change in the Windows session via `powershell.exe` when
running under WSL/Linux. On a host where neither backend can drive the Windows pointer,
the signal SHALL degrade to a no-op rather than error.

#### Scenario: Native Windows host

- **WHEN** tmux-watch runs as a native Windows process
- **THEN** it changes the pointer through a direct OS call without spawning a shell

#### Scenario: WSL host

- **WHEN** tmux-watch runs under WSL/Linux
- **THEN** it changes the Windows-session pointer by invoking `powershell.exe`

#### Scenario: Unsupported host

- **WHEN** tmux-watch runs on a host where the Windows pointer cannot be reached
- **THEN** the pointer signal does nothing and the watcher continues without error

### Requirement: Waiting pointer appearance

The system SHALL render the waiting state as a red pointer, replacing the standard arrow
and the text I-beam shapes so the cue is visible both over other applications and over
the terminal's text area, using a shipped cursor asset.

#### Scenario: Arrow and I-beam recoloured

- **WHEN** the waiting pointer is set
- **THEN** both the arrow and the I-beam pointer shapes appear in the waiting colour

### Requirement: Crash-safe restore

The system SHALL restore the normal pointer when it shuts down gracefully, and SHALL
also unconditionally restore the normal pointer at startup so that a pointer left in the
waiting state by a prior abnormal termination is healed on the next launch.

#### Scenario: Graceful shutdown restores pointer

- **WHEN** tmux-watch exits normally while the waiting pointer is set
- **THEN** it restores the normal pointer before exiting

#### Scenario: Self-heal after a crash

- **WHEN** tmux-watch starts up
- **THEN** it restores the normal pointer first, so any pointer left red by a previous
  crash is cleared regardless of the current pane state

