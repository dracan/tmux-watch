# pane-state-detection Specification

## Purpose
TBD - created by archiving change add-copilot-pane-watcher. Update Purpose after archive.
## Requirements
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

The system SHALL classify a pane as DEAD when its foreground command no longer matches its assigned agent profile (and the pane is not re-identified by that profile's name convention or content fingerprint) or its dead flag is set.

#### Scenario: Agent process exited

- **WHEN** a previously matched agent pane reports a foreground command that no longer matches its profile, or a dead flag of `1`
- **THEN** the pane is classified as DEAD

### Requirement: Deterministic classification precedence

The system SHALL apply classification signals in a fixed precedence — WAITING, then WORKING, then IDLE, then DEAD — so that a screen matching more than one signal resolves to a single, predictable state.

#### Scenario: WAITING outranks residual scrollback

- **WHEN** a captured screen contains both a WAITING selection footer and residual WORKING text from earlier in the scrollback
- **THEN** the pane is classified as WAITING

### Requirement: Version-tolerant configurable fingerprints

The system SHALL treat all status-bar and footer match tokens as configurable patterns held **per agent profile** rather than hardcoded constants or a single global set, because they are specific to each agent CLI and its version.

#### Scenario: Patterns overridable per agent

- **WHEN** a user supplies overriding match patterns for a specific agent profile's WAITING, WORKING, or IDLE detection
- **THEN** the system uses the supplied patterns for that profile's panes and leaves other profiles unaffected

### Requirement: No false positives from non-agent TUIs

The system SHALL only run state detection on panes matched to an agent profile, and other box-drawing TUIs MUST NOT be classified as WAITING.

#### Scenario: lazygit is not flagged

- **WHEN** a `lazygit` pane with its own bordered footer is captured
- **THEN** it is not classified as WAITING (and is excluded from watching entirely)

### Requirement: Classify using the pane's matched agent profile

The system SHALL classify each watched pane using the tokens of the agent profile the pane was matched to during discovery, applying the same fixed precedence (WAITING, then WORKING, then IDLE, then DEAD) for every profile.

#### Scenario: Copilot and Claude classified by their own tokens

- **WHEN** a Copilot pane and a Claude pane are both watched in the same poll
- **THEN** the Copilot pane is classified with the `copilot` profile's tokens and the Claude pane with the `claude` profile's tokens, each resolving to a single state

### Requirement: Detect Claude Code WAITING/WORKING/IDLE

The system SHALL detect the WAITING, WORKING, and IDLE states of a Claude Code pane from its captured status bar using the `claude` profile's tokens: WAITING on a numbered selection cursor (`❯` followed by a digit and a period, as shown on a permission prompt), WORKING on a spinner glyph together with the Claude working cancel marker (`esc to interrupt`), and IDLE on the Claude idle hint at the input box. The specific tokens are configurable and MUST be verified against real Claude Code captures before the defaults are relied upon.

#### Scenario: Claude permission prompt is WAITING

- **WHEN** a Claude pane's capture shows a numbered permission prompt (e.g. `❯ 1. Yes`) for a tool or command approval
- **THEN** the pane is classified as WAITING

#### Scenario: Claude streaming work is WORKING

- **WHEN** a Claude pane's status bar shows a spinner glyph alongside the working cancel marker `esc to interrupt`
- **THEN** the pane is classified as WORKING

#### Scenario: Claude at the input box is IDLE

- **WHEN** a Claude pane shows its input box and idle hint with no permission prompt and no working marker
- **THEN** the pane is classified as IDLE

#### Scenario: Claude spinner animation does not change classification

- **WHEN** the Claude spinner glyph changes between consecutive captures while `esc to interrupt` remains
- **THEN** the pane remains classified as WORKING

