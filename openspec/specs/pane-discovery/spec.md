# pane-discovery Specification

## Purpose
TBD - created by archiving change add-copilot-pane-watcher. Update Purpose after archive.
## Requirements
### Requirement: Enumerate panes across all sessions

The system SHALL enumerate every pane across all multiplexer sessions using a read-only listing (`<multiplexer> lsp -a -F <format>`) and expose, for each pane, a stable pane id, session name, window index, pane index, foreground command, dead flag, and the pane's window last-activity timestamp. The multiplexer executable SHALL default to `tmux` and SHALL be overridable via configuration so a psmux (or other tmux-compatible) host remains supported. The enumeration format string and the read-only verb whitelist SHALL be identical across supported multiplexers.

#### Scenario: Multiple sessions enumerated

- **WHEN** the tmux server has several sessions and panes running
- **THEN** the system returns one record per pane, each including pane id (e.g. `%10`), session name, window index, pane index, foreground command, and dead flag

#### Scenario: Window activity timestamp exposed

- **WHEN** a pane is enumerated
- **THEN** its record carries the last-activity timestamp of the window containing it, and a missing or unparseable value degrades to "unknown" rather than failing the enumeration

#### Scenario: tmux server unavailable

- **WHEN** the `tmux` CLI is missing from PATH or no server is running
- **THEN** the system returns an empty pane set and surfaces a clear, non-fatal error rather than crashing

#### Scenario: psmux host via configuration override

- **WHEN** the multiplexer executable is overridden to `psmux` in configuration
- **THEN** the system enumerates panes using `psmux` with the same format string and verb whitelist, requiring no other change

### Requirement: Stable pane identity across polls

The system SHALL key each watched pane by its tmux pane id so that the same pane is tracked consistently across successive polls, SHALL detect when panes appear or disappear, and SHALL retain the pane's matched agent profile across polls.

#### Scenario: Identity retained across polls

- **WHEN** the same pane id is present in two consecutive enumerations
- **THEN** the system treats both records as the same pane, preserves its tracked history, and keeps its matched agent

#### Scenario: Pane removed

- **WHEN** a pane id present in a previous enumeration is absent from the current one
- **THEN** the system marks that pane as gone and stops tracking it without affecting other panes

### Requirement: Identify agent panes

The system SHALL match each enumerated pane against an ordered set of configured **agent profiles** (e.g. Copilot, Claude Code) and treat the pane as a watched agent pane when it matches a profile. A pane matches a profile when (1) its foreground command equals the profile's configured command (extension-insensitive), OR (2) its session or window name matches the profile's configured naming convention. Command match is evaluated across all profiles before the naming backstop. A pane that matches no profile MUST be excluded from watching and MUST NOT be captured; it SHALL instead be surfaced as a non-agent inventory entry.

#### Scenario: Copilot pane matched by command

- **WHEN** a pane reports foreground command `copilot`
- **THEN** it is watched and matched to the `copilot` profile

#### Scenario: Claude pane matched by command

- **WHEN** a pane reports foreground command `claude`
- **THEN** it is watched and matched to the `claude` profile

#### Scenario: Claude pane matched by name convention

- **WHEN** a pane's foreground command is not `claude` but its session or window name matches the `claude` profile's configured convention
- **THEN** it is watched and matched to the `claude` profile

#### Scenario: Non-agent pane excluded from watching but surfaced as inventory

- **WHEN** a pane reports foreground command `lazygit`, `bash`, or any value matching no profile's command, convention, or content fingerprint
- **THEN** the pane is excluded from watching and is not captured, and it appears in the non-agent inventory instead

#### Scenario: First matching profile wins

- **WHEN** a pane could match more than one profile
- **THEN** the system assigns it to the first profile in configured order and does not double-watch it

### Requirement: Expose matched agent identity

The system SHALL expose, for each watched pane, the id of the agent profile it matched, so downstream classification, the TUI, and the calibrate output can use the correct profile and label the pane's agent.

#### Scenario: Matched agent available downstream

- **WHEN** a pane is matched to a profile
- **THEN** the pane record carries that profile's id and the classifier is invoked with that profile's tokens

### Requirement: Expose non-agent pane inventory

The system SHALL expose the enumerated panes that matched no agent profile as a
**non-agent inventory**, derived from the same single read-only enumeration used to find
agent panes, without issuing any additional multiplexer call. Each inventory entry SHALL
carry the pane's stable id, session name, window index, pane index, window name,
foreground command, working directory, and window last-activity timestamp. Inventory
entries MUST NOT be captured, classified, or tracked by the attention state machine.

#### Scenario: Non-agent panes returned alongside agent panes

- **WHEN** the multiplexer reports a mix of agent panes and panes running `k9s`, `lazygit`, and a shell
- **THEN** the system returns the agent panes as watched panes and the remaining panes as non-agent inventory entries, each carrying its foreground command

#### Scenario: Inventory costs no extra enumeration

- **WHEN** the system discovers agent panes and the non-agent inventory for one poll
- **THEN** it issues exactly one `lsp -a -F` call for both

#### Scenario: Inventory panes are never captured

- **WHEN** a poll produces a non-agent inventory
- **THEN** the system issues no `capture-pane` for any inventory entry

#### Scenario: Watcher's own pane excluded when running inside the multiplexer

- **WHEN** the watcher is itself running inside a multiplexer pane and that pane's id is available from the environment
- **THEN** that pane is omitted from the non-agent inventory

### Requirement: Built-in Codex discovery

The default agent set SHALL include a `codex` profile matching foreground command `codex`, including its extension-insensitive `codex.exe` form. It SHALL use the existing agent inventory and navigation. A non-empty configured agent list SHALL continue to replace the defaults.

#### Scenario: Codex joins the watched inventory
- **WHEN** default configuration enumerates panes running `codex`, `codex.exe`, `claude`, `copilot`, and a shell
- **THEN** the Codex panes are assigned agent id `codex`, the existing agents retain their profiles, and the shell remains an Other pane

#### Scenario: Explicit profiles remain authoritative
- **WHEN** a non-empty configured agent list omits Codex
- **THEN** Codex is not implicitly added to that list

