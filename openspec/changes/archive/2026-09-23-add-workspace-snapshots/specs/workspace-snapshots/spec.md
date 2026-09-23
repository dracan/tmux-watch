## ADDED Requirements

### Requirement: Durable complete workspace export

The system SHALL export all panes, including ordinary shells, dead panes, and the
watcher, as versioned readable JSON with session/window names, split layout,
working directories, agent type, paused state, and resume identity or an explicit
unknown reason. It SHALL save a timestamped file or user-selected path before
copying the identical JSON to the clipboard. Export SHALL never capture transcripts.

#### Scenario: Clipboard unavailable
- **WHEN** clipboard delivery fails after saving
- **THEN** the saved file remains and the result identifies its path and the clipboard failure

#### Scenario: Hidden and paused panes included
- **WHEN** the TUI hides ordinary or paused panes
- **THEN** export still includes the complete multiplexer inventory

### Requirement: Verified selected conversation only

The system SHALL use read-only identity metadata only when it reliably identifies
the currently selected conversation for that agent version and process. It SHALL
otherwise export unknown, without hooks, configuration changes, transcript reads,
or inference from cwd, timestamps, or another conversation held by the process.

#### Scenario: Identity cannot be proven
- **WHEN** an agent's metadata cannot establish its selected conversation
- **THEN** export succeeds with unknown identity and no invented resume command

### Requirement: Validated explicit shell-only restore

The system SHALL import a file or pasted JSON through explicit CLI or TUI actions.
Before creating anything it SHALL validate schema, names, directories, conflicts,
pane coverage, and backend support for the structure. It SHALL restore shells in
saved directories, preserve session/window names, pane placement, and split
structure on tmux and Windows/psmux, allowing geometry to scale and round. It SHALL
restore paused settings and report manual resume guidance without executing it.
Imported text SHALL never become a command position or format expression.

#### Scenario: Preflight failure
- **WHEN** a session name exists, a directory is missing, or a structure is unsupported
- **THEN** import reports the problems and creates nothing

#### Scenario: Nested split restore
- **WHEN** a supported snapshot is imported into an otherwise non-conflicting server
- **THEN** both backends recreate its names, directories, pane placement, split structure, and pause settings

#### Scenario: Partial creation failure
- **WHEN** a multiplexer operation fails after creation begins
- **THEN** completed resources remain and the report identifies completed work and the failed step

#### Scenario: Snapshot contains executable-looking text
- **WHEN** a saved name or directory resembles a command
- **THEN** it is passed only as a name or directory value and no saved command executes

### Requirement: Persistent shared pause settings

The system SHALL persist pause settings and share them between watcher instances
observing the same panes. CLI export SHALL read the same settings. Records SHALL
not apply to unrelated panes after PID or pane-ID reuse. Import SHALL transfer
saved settings onto new identities. Persistence failures SHALL be visible.

#### Scenario: Another watcher observes a pause
- **WHEN** one watcher pauses a pane and another refreshes
- **THEN** both display that pane as paused and export records it as paused

#### Scenario: Recycled identity
- **WHEN** a pane identifier or PID is reused for a new process
- **THEN** the old pause record does not pause the new pane
