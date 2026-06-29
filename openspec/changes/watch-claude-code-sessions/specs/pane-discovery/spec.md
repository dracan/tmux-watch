## MODIFIED Requirements

### Requirement: Identify agent panes

The system SHALL match each enumerated pane against an ordered set of configured **agent profiles** (e.g. Copilot, Claude Code) and treat the pane as a watched agent pane when it matches a profile. A pane matches a profile, in priority order, when (1) its foreground command equals the profile's configured command (extension-insensitive), OR (2) its session or window name matches the profile's configured naming convention, OR (3) it is identified by the profile's bounded content fingerprint (see "Bounded content-fingerprint identification"). A pane that matches no profile MUST be excluded from watching.

#### Scenario: Copilot pane matched by command

- **WHEN** a pane reports foreground command `copilot`
- **THEN** it is watched and matched to the `copilot` profile

#### Scenario: Claude pane matched by name convention

- **WHEN** a pane's foreground command is `node` and its session or window name matches the `claude` profile's configured convention
- **THEN** it is watched and matched to the `claude` profile

#### Scenario: Non-agent pane excluded

- **WHEN** a pane reports foreground command `lazygit`, `bash`, or any value matching no profile's command, convention, or content fingerprint
- **THEN** the pane is excluded from watching and is not captured

#### Scenario: First matching profile wins

- **WHEN** a pane could match more than one profile
- **THEN** the system assigns it to the first profile in configured order and does not double-watch it

### Requirement: Stable pane identity across polls

The system SHALL key each watched pane by its tmux pane id so that the same pane is tracked consistently across successive polls, SHALL detect when panes appear or disappear, and SHALL retain the pane's matched agent profile across polls.

#### Scenario: Identity retained across polls

- **WHEN** the same pane id is present in two consecutive enumerations
- **THEN** the system treats both records as the same pane, preserves its tracked history, and keeps its matched agent

#### Scenario: Pane removed

- **WHEN** a pane id present in a previous enumeration is absent from the current one
- **THEN** the system marks that pane as gone and stops tracking it without affecting other panes

## ADDED Requirements

### Requirement: Bounded content-fingerprint identification

The system SHALL be able to identify an agent pane by its captured content when command and name-convention matching do not apply, but SHALL only perform a content-fingerprint capture on panes whose foreground command is in the profile's configured candidate-host command set (default `node` for Claude Code). Panes outside every profile's candidate-host set MUST NOT be captured for identification.

#### Scenario: Node-hosted Claude pane self-identifies

- **WHEN** an unnamed pane reports foreground command `node` and its capture matches the `claude` profile's tokens
- **THEN** the pane is watched and matched to the `claude` profile

#### Scenario: Unrelated pane is not captured for identification

- **WHEN** a pane's foreground command (e.g. `vim`) is in no profile's candidate-host set and matches no command or convention
- **THEN** the system does not capture the pane and excludes it from watching

### Requirement: Expose matched agent identity

The system SHALL expose, for each watched pane, the id of the agent profile it matched, so downstream classification, the TUI, and the calibrate output can use the correct profile and label the pane's agent.

#### Scenario: Matched agent available downstream

- **WHEN** a pane is matched to a profile
- **THEN** the pane record carries that profile's id and the classifier is invoked with that profile's tokens
