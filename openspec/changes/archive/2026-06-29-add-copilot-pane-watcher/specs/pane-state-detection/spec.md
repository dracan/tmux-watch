## ADDED Requirements

### Requirement: Detect WAITING (blocked on user)

The system SHALL classify a Copilot pane as WAITING when its captured screen shows a blocking selection affordance, identified by EITHER a numbered selection cursor (a `❯` immediately followed by a digit and a period) OR a hint line containing both an up/down navigation marker (`↑/↓`) and the text `esc to cancel`. Detection MUST cover both command-approval prompts and `ask_user` prompts despite their differing footer wording, and MUST NOT rely solely on approval-specific phrases such as `enter to select` or `to navigate`.

#### Scenario: Command-approval prompt

- **WHEN** the captured screen contains `❯ 1. Yes` and a footer `↑/↓ to navigate · enter to select · esc to cancel`
- **THEN** the pane is classified as WAITING

#### Scenario: ask_user choice prompt

- **WHEN** the captured screen contains a numbered question and a footer `↑/↓ to select · enter to confirm · esc to cancel`
- **THEN** the pane is classified as WAITING

#### Scenario: Footer wording differences do not cause a miss

- **WHEN** a blocking prompt uses footer wording that omits `enter to select` and `to navigate` but retains `esc to cancel` and a numbered cursor
- **THEN** the pane is still classified as WAITING

### Requirement: Detect WORKING

The system SHALL classify a Copilot pane as WORKING when its status bar contains a working indicator: an animated spinner glyph (one of a configured set, e.g. `◎ ◉ ● ○`) immediately preceding the word `Working`.

#### Scenario: Streaming work in progress

- **WHEN** the status bar shows `◎ Working   esc cancel` alongside the model/context line
- **THEN** the pane is classified as WORKING

#### Scenario: Spinner animation does not change classification

- **WHEN** the spinner glyph changes between consecutive captures (e.g. `◎` to `◉`) while the word `Working` remains
- **THEN** the pane remains classified as WORKING

### Requirement: Detect IDLE (turn finished)

The system SHALL classify a Copilot pane as IDLE when an input box is present and the status bar shows an idle hint (a configured set of tokens such as `/ commands`, `? help`, `space hold to record`) and neither a WAITING affordance nor a WORKING indicator is present.

#### Scenario: Fresh or finished session at the input box

- **WHEN** the captured screen shows the input box and a status bar `/ commands · ? help · space hold to record` with no spinner and no selection footer
- **THEN** the pane is classified as IDLE

### Requirement: Detect DEAD

The system SHALL classify a pane as DEAD when its foreground command is no longer `copilot` or its dead flag is set.

#### Scenario: Copilot process exited

- **WHEN** a previously Copilot pane reports a foreground command other than `copilot` or a dead flag of `1`
- **THEN** the pane is classified as DEAD

### Requirement: Deterministic classification precedence

The system SHALL apply classification signals in a fixed precedence — WAITING, then WORKING, then IDLE, then DEAD — so that a screen matching more than one signal resolves to a single, predictable state.

#### Scenario: WAITING outranks residual scrollback

- **WHEN** a captured screen contains both a WAITING selection footer and residual WORKING text from earlier in the scrollback
- **THEN** the pane is classified as WAITING

### Requirement: No false positives from non-Copilot TUIs

The system SHALL only run state detection on panes identified as Copilot sessions, and other box-drawing TUIs MUST NOT be classified as WAITING.

#### Scenario: lazygit is not flagged

- **WHEN** a `lazygit` pane with its own bordered footer is captured
- **THEN** it is not classified as WAITING (and is excluded from watching entirely)

### Requirement: Version-tolerant configurable fingerprints

The system SHALL treat all status-bar and footer match tokens as configurable patterns rather than hardcoded constants, because they are specific to a Copilot CLI version.

#### Scenario: Patterns overridable via configuration

- **WHEN** a user supplies overriding match patterns for WAITING, WORKING, or IDLE detection
- **THEN** the system uses the supplied patterns in place of the defaults
