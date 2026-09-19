## ADDED Requirements

### Requirement: Agent-operated detection refresh
The repository SHALL expose a `refresh-agent-detection` skill to Claude Code, Codex, and Copilot CLI through a shared canonical workflow. The skill SHALL select the requested scope, run preflight and supported live checks, inspect supplied or new reports, distinguish detection defects from missing evidence, and carry verified repairs through OpenSpec, regression fixtures, validation, and authorized delivery on main. User limits SHALL take precedence over the default workflow.

#### Scenario: Clean refresh
- **WHEN** the selected checks pass without requiring a repair
- **THEN** the skill reports versions, coverage, exclusions, and report locations without changing tracked files or creating a commit

#### Scenario: Verified detection drift
- **WHEN** independent native evidence establishes a state that tmux-watch misclassifies
- **THEN** the skill creates or continues a relevant OpenSpec change, adds scrubbed regression coverage, repairs detection, reruns affected live cases at both widths, and completes authorized delivery

#### Scenario: Missing authentication or unavailable evidence
- **WHEN** an agent requires login or a scenario lacks independent state evidence
- **THEN** the skill requests only the necessary user action, continues independent checks where possible, and reports remaining coverage as incomplete rather than changing detection to manufacture success

#### Scenario: Existing report and focused request
- **WHEN** the user supplies a report or names one agent or state
- **THEN** the skill inspects that evidence and limits new runs to the relevant scope while accounting for affected shared behavior

#### Scenario: Equivalent agent entry points
- **WHEN** any supported coding agent loads the refresh workflow
- **THEN** it reaches the same canonical instructions, including production tmux boundaries and private capture handling
