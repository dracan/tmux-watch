## ADDED Requirements

### Requirement: Enumerate panes across all sessions

The system SHALL enumerate every pane across all psmux sessions using a read-only listing (`psmux lsp -a -F <format>`) and expose, for each pane, a stable pane id, session name, window index, pane index, foreground command, and dead flag.

#### Scenario: Multiple sessions enumerated

- **WHEN** the psmux server has several sessions and panes running
- **THEN** the system returns one record per pane, each including pane id (e.g. `%10`), session name, window index, pane index, foreground command, and dead flag

#### Scenario: psmux server unavailable

- **WHEN** the `psmux` CLI is missing from PATH or no server is running
- **THEN** the system returns an empty pane set and surfaces a clear, non-fatal error rather than crashing

### Requirement: Identify Copilot panes

The system SHALL classify a pane as a Copilot session when its foreground command is `copilot`, and MAY additionally treat a pane as a Copilot session when its session name matches a configured naming convention (backstop). Panes that are neither MUST be excluded from watching.

#### Scenario: Copilot pane is included

- **WHEN** a pane reports foreground command `copilot`
- **THEN** the pane is included in the watched set

#### Scenario: Non-Copilot pane is excluded

- **WHEN** a pane reports foreground command `lazygit`, `pwsh`, or any value other than `copilot` and its session name does not match the configured convention
- **THEN** the pane is excluded from the watched set

#### Scenario: Session-name backstop

- **WHEN** a pane's foreground command is not yet `copilot` but its session name matches the configured Copilot naming convention
- **THEN** the pane is included in the watched set

### Requirement: Stable pane identity across polls

The system SHALL key each watched pane by its psmux pane id so that the same pane is tracked consistently across successive polls, and SHALL detect when panes appear or disappear.

#### Scenario: Identity retained across polls

- **WHEN** the same pane id is present in two consecutive enumerations
- **THEN** the system treats both records as the same pane and preserves its tracked history

#### Scenario: Pane removed

- **WHEN** a pane id present in a previous enumeration is absent from the current one
- **THEN** the system marks that pane as gone and stops tracking it without affecting other panes
