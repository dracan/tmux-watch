## MODIFIED Requirements

### Requirement: Detect WAITING (blocked on user)

The system SHALL classify a Copilot pane as WAITING when its captured screen shows a blocking selection affordance, identified by EITHER a numbered selection cursor (a `❯` immediately followed by a digit and a period) OR a hint line containing both an up/down navigation marker (`↑/↓`) and the cancel text `esc to cancel`. Matching of the cancel text MUST be case-insensitive so a footer that capitalises it (e.g. `Esc to cancel`) still matches. Detection MUST cover both command-approval prompts and `ask_user` prompts despite their differing footer wording, and MUST NOT rely solely on approval-specific phrases such as `enter to select` or `to navigate`.

#### Scenario: Command-approval prompt

- **WHEN** the captured screen contains `❯ 1. Yes` and a footer `↑/↓ to navigate · enter to select · esc to cancel`
- **THEN** the pane is classified as WAITING

#### Scenario: ask_user choice prompt

- **WHEN** the captured screen contains a numbered question and a footer `↑/↓ to select · enter to confirm · esc to cancel`
- **THEN** the pane is classified as WAITING

#### Scenario: Footer wording differences do not cause a miss

- **WHEN** a blocking prompt uses footer wording that omits `enter to select` and `to navigate` but retains `esc to cancel` and a numbered cursor
- **THEN** the pane is still classified as WAITING

#### Scenario: Capitalised cancel text still matches

- **WHEN** a blocking prompt's footer reads `Enter to select · ↑/↓ to navigate · Esc to cancel`
- **THEN** the pane is still classified as WAITING

### Requirement: Detect Claude Code WAITING/WORKING/IDLE

The system SHALL detect the WAITING, WORKING, and IDLE states of a Claude Code pane from its captured screen using the `claude` profile's tokens. WAITING is detected on a numbered selection cursor (`❯` followed by a digit and a period) OR a selection footer carrying an up/down marker (`↑/↓`) together with a case-insensitive cancel marker (`esc to cancel`). WORKING is detected on a *live* status line carrying one of three qualifiers: a live activity meter (a parenthesised duration-and-token segment such as `(32s · ↓ 1.4k tokens)`), a report that the agent is waiting for one or more background sub-agents to finish, or - for the brief moment before the meter appears - an ellipsis (`...`) on a line that also begins with an asterisk spinner glyph (one of the configured animated set). The meter and the background-agents qualifiers MUST be matched independently of the line's leading glyph, because the spinner animation cycles through frames beyond any fixed glyph set (live captures include `·` and `*`); anchoring every live line on a glyph whitelist drops a large fraction of frames and flickers the pane to IDLE. A frozen end-of-turn spinner line that is past-tense (`<Word> for <duration>`, e.g. `Crunched for 54s`) with no ellipsis and no live meter MUST NOT be classified WORKING. IDLE is detected on the Claude idle hint at the input box when neither WAITING nor WORKING is present. The specific tokens are configurable and MUST be verified against real Claude Code captures before the defaults are relied upon.

#### Scenario: Claude permission prompt is WAITING

- **WHEN** a Claude pane's capture shows a numbered permission prompt (e.g. `❯ 1. Yes`) for a tool or command approval
- **THEN** the pane is classified as WAITING

#### Scenario: Claude tall slash-command menu is WAITING

- **WHEN** a Claude pane shows a multi-item selection menu whose `❯ 1.` cursor is many lines above the bottom and whose footer reads `Enter to select · ↑/↓ to navigate · Esc to cancel`
- **THEN** the pane is classified as WAITING

#### Scenario: Claude streaming work is WORKING

- **WHEN** a Claude pane's screen shows a live spinner line such as `✻ Enchanting... (32s · ↓ 1.4k tokens)` above the input box and no `esc to interrupt` marker is present
- **THEN** the pane is classified as WORKING

#### Scenario: Working line on an off-whitelist animation frame is WORKING

- **WHEN** a Claude pane's live line is captured on an animation frame whose leading glyph is outside the configured asterisk set (e.g. `· Doodling… (11s · ↓ 307 tokens)` or `* Doodling… (11s · ↓ 307 tokens)`)
- **THEN** the pane is still classified as WORKING, via the glyph-independent activity meter

#### Scenario: Claude waiting on background sub-agents is WORKING

- **WHEN** a Claude pane's screen shows a spinner line `✻ Waiting for 2 background agents to finish` with a sub-agent panel rendered below the input box
- **THEN** the pane is classified as WORKING

#### Scenario: Frozen completed line is not WORKING

- **WHEN** a Claude pane is idle at the input box and an earlier `✻ Crunched for 54s` line (past-tense, no ellipsis, no meter) remains on screen
- **THEN** the pane is NOT classified as WORKING and is classified as IDLE

#### Scenario: Claude at the input box is IDLE

- **WHEN** a Claude pane shows its input box and idle hint with no selection prompt and no live spinner line
- **THEN** the pane is classified as IDLE

#### Scenario: Claude spinner animation does not change classification

- **WHEN** the Claude spinner glyph cycles through the animated set between consecutive captures while the live line keeps its gerund + ellipsis or its meter
- **THEN** the pane remains classified as WORKING

## ADDED Requirements

### Requirement: Scan region reaches signals outside the bottom status lines

The system SHALL scan a region of the captured Claude screen large enough to include WAITING and WORKING signals that no longer sit on the last few non-blank lines, because the current Claude Code build renders the live spinner line above the input box, a sub-agent panel below it, and tall selection menus whose cursor sits well above the footer. Widening the scan MUST NOT introduce false positives from earlier scrollback: WORKING matching relies on the live-only qualifiers (ellipsis or meter, or the background-agents line) so that frozen completed lines are excluded, and the fixed precedence (WAITING, then WORKING, then IDLE, then DEAD) is preserved.

#### Scenario: Working line above the input box is detected

- **WHEN** a Claude pane's live spinner line sits above the input box and a sub-agent panel is rendered below it, placing the spinner line well outside the last six non-blank lines
- **THEN** the system still classifies the pane as WORKING

#### Scenario: Selection cursor above the footer is detected

- **WHEN** a Claude selection menu's `❯ 1.` cursor sits many lines above the bottom of the capture
- **THEN** the system still classifies the pane as WAITING

#### Scenario: Widened scan does not promote stale scrollback to WORKING

- **WHEN** the widened scan region includes an earlier frozen `✻ Crunched for 54s` line while the pane is idle at the input box
- **THEN** the pane is classified as IDLE, not WORKING
