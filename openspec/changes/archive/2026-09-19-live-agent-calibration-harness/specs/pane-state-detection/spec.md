## ADDED Requirements

### Requirement: Codex working status with terminal summary
The Codex profile SHALL recognize its timed live working status when a background-terminal count is appended on the same line, including subsequent middot-delimited controls. The terminal count alone MUST NOT imply WORKING or BACKGND. The live status bullet, elapsed time, and interrupt qualifier SHALL remain required.

#### Scenario: Foreground work with terminal summary
- **WHEN** a foreground tool is still running and the live timed status line carries a background-terminal count
- **THEN** the pane classifies WORKING rather than falling through to its IDLE composer

#### Scenario: Counter without live work
- **WHEN** a pane has a terminal count but no complete timed working signal
- **THEN** that count alone does not classify WORKING or BACKGND

### Requirement: Copilot structured question panels
The Copilot profile SHALL recognize choice, free-text, and multiple-field ask-user panels by their full-width opening rule, fixed panel heading, and closing rule at the end of the scanned tail. A new optional `WaitingPanelPattern` SHALL match across lines and default to disabled. Matching SHALL remain bounded to the existing status scan and MUST NOT depend on field labels, numbered options, or footer wording.

#### Scenario: Structured form replaces the composer
- **WHEN** a live form has the bordered Copilot question-panel structure
- **THEN** the pane classifies WAITING for choice, free-text, and multiple-field variants

#### Scenario: Panel quoted in transcript
- **WHEN** an old panel or its heading appears above a subsequent composer or other live chrome
- **THEN** it does not trigger the panel matcher

#### Scenario: Pattern disabled
- **WHEN** WaitingPanelPattern is empty
- **THEN** the additional panel check is disabled and existing signals still apply
