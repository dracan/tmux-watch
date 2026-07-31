## MODIFIED Requirements

### Requirement: Detect WAITING (blocked on user)

The system SHALL classify a Copilot pane as WAITING when its captured screen shows a blocking selection affordance, identified by EITHER a numbered selection cursor (a `❯` immediately followed by a digit and a period) OR a hint line containing both an up/down navigation marker (`↑/↓`) and the cancel text `esc to cancel`. Matching of the cancel text MUST be case-insensitive so a footer that capitalises it (e.g. `Esc to cancel`) still matches. Detection MUST cover both command-approval prompts and `ask_user` prompts despite their differing footer wording, and MUST NOT rely solely on approval-specific phrases such as `enter to select` or `to navigate`.

The selection cursor SHALL be matched **only in the region below the composer prompt line** where the pane's profile configures one and one is on screen, and anywhere on screen otherwise. An agent that has merely written a cursor-shaped string into its output - a recap, a diff, a quoted screen - is not blocked on anybody, and because WAITING outranks IDLE such prose would pin the pane in WAITING for as long as it stayed on screen, withholding the finished-turn promotion to DONE. The rule costs a genuine prompt nothing: a prompt box replaces the composer rather than stacking above it, so a real blocking prompt renders no composer, has no split to apply, and is matched wherever its cursor lands.

The composer prompt line itself SHALL be excluded from the cursor match, so that text a user is still typing cannot classify their own pane as WAITING.

The hint-line signal SHALL remain matched across the whole scanned region, and this asymmetry with the cursor is deliberate: it is the signal that still detects a blocking prompt should any prompt form render a composer above its menu. A profile that configures no composer prompt pattern SHALL be unaffected, matching the cursor across the whole scanned region as before.

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

#### Scenario: Prompt box with no composer is WAITING on the cursor alone

- **WHEN** a pane shows a bare permission box containing `❯ 1. Yes`, no composer prompt line, and no navigation or cancel hint line
- **THEN** the pane is classified as WAITING, because with no composer on screen there is no region to exclude

#### Scenario: Cursor-shaped prose above a live composer is not WAITING

- **WHEN** a pane's transcript contains the string `❯ 1.` as part of the agent's own output, a composer prompt line is rendered below it, and the chrome carries no navigation or cancel hint line
- **THEN** the pane is NOT classified as WAITING, and is classified from its remaining signals

#### Scenario: Finished turn is still promoted to DONE despite cursor-shaped prose

- **WHEN** a pane that was WORKING finishes its turn and its transcript contains the string `❯ 1.` above a live composer
- **THEN** the pane is classified as IDLE, so the WORKING to IDLE transition promotes the pane to DONE and announces the finished turn

#### Scenario: Cursor-shaped text typed into the composer is not WAITING

- **WHEN** the composer prompt line itself contains a cursor-shaped string, such as a user typing `why did ❯ 1. flag that pane`
- **THEN** the pane is NOT classified as WAITING

#### Scenario: Hint line is matched regardless of position

- **WHEN** a pane renders a selection menu whose cursor sits above a composer prompt line, and the menu carries a hint line containing both `↑/↓` and `esc to cancel`
- **THEN** the pane is still classified as WAITING on the strength of the hint line
