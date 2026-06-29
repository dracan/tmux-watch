## MODIFIED Requirements

### Requirement: Enumerate panes across all sessions

The system SHALL enumerate every pane across all multiplexer sessions using a read-only listing (`<multiplexer> lsp -a -F <format>`) and expose, for each pane, a stable pane id, session name, window index, pane index, foreground command, and dead flag. The multiplexer executable SHALL default to `tmux` and SHALL be overridable via configuration so a psmux (or other tmux-compatible) host remains supported. The enumeration format string and the read-only verb whitelist SHALL be identical across supported multiplexers.

#### Scenario: Multiple sessions enumerated

- **WHEN** the tmux server has several sessions and panes running
- **THEN** the system returns one record per pane, each including pane id (e.g. `%10`), session name, window index, pane index, foreground command, and dead flag

#### Scenario: tmux server unavailable

- **WHEN** the `tmux` CLI is missing from PATH or no server is running
- **THEN** the system returns an empty pane set and surfaces a clear, non-fatal error rather than crashing

#### Scenario: psmux host via configuration override

- **WHEN** the multiplexer executable is overridden to `psmux` in configuration
- **THEN** the system enumerates panes using `psmux` with the same format string and verb whitelist, requiring no other change

### Requirement: Stable pane identity across polls

The system SHALL key each watched pane by its tmux pane id so that the same pane is tracked consistently across successive polls, and SHALL detect when panes appear or disappear.

#### Scenario: Identity retained across polls

- **WHEN** the same pane id is present in two consecutive enumerations
- **THEN** the system treats both records as the same pane and preserves its tracked history

#### Scenario: Pane removed

- **WHEN** a pane id present in a previous enumeration is absent from the current one
- **THEN** the system marks that pane as gone and stops tracking it without affecting other panes
