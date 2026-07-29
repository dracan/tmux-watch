## ADDED Requirements

### Requirement: Detect BACKGND (agent finished, background task running)

The system SHALL classify an agent pane as BACKGND when it is neither blocked on the user
nor working, but the agent CLI reports one or more background tasks of its own still
running. BACKGND is a **quiet** state: the agent has handed the turn back, but the work it
started has not fully finished, so the pane SHALL NOT be treated as needing the user.

BACKGND SHALL be produced by the stateless classifier from a single capture, not derived
from pane history, because the condition is fully visible on one screen. The fingerprint
is a background-task counter in the agent's status chrome, matched per agent profile as a
configurable token.

The counter SHALL cover every kind of background task the agent reports in that slot, not
only shells. Claude Code renders shells and monitors into one footer segment (`· 2 shells ·
1 monitor ·`), and both carry the same meaning for this state - work the agent started
that has not finished. Recognising only one kind would leave a pane running the other
looking plainly IDLE.

The counter SHALL be matched **only in the region below the composer prompt line**, never
in the transcript region above it. The transcript routinely contains prose that would
otherwise match - a tool-use line such as `Ran 1 shell command`, and, critically, an
end-of-turn line such as `✻ Cooked for 16s · 1 shell still running` which remains frozen
on screen after the task has exited and would pin the pane in BACKGND indefinitely.

A profile that configures no background-task token SHALL never produce BACKGND, leaving
its classification behaviour unchanged.

#### Scenario: Finished turn with a background shell is BACKGND

- **WHEN** a Claude pane shows its composer box with no selection prompt and no live spinner line, and its footer reads `-- INSERT -- ⏵⏵ auto mode on · 1 shell · ← for agents`
- **THEN** the pane is classified as BACKGND, not IDLE and not Unknown

#### Scenario: Finished turn with a background monitor is BACKGND

- **WHEN** a Claude pane's chrome carries `· 1 monitor ·` (or `· 4 monitors ·`) with no shells counted, and neither WAITING nor WORKING is present
- **THEN** the pane is classified as BACKGND, on the same footing as a background shell

#### Scenario: Both kinds counted in one segment

- **WHEN** the footer reads `· 2 shells · 1 monitor ·`
- **THEN** the pane is classified as BACKGND

#### Scenario: Background task while the agent works is still WORKING

- **WHEN** a pane shows a live spinner line with an activity meter and its chrome also carries the background-task counter
- **THEN** the pane is classified as WORKING, because WORKING outranks BACKGND

#### Scenario: Blocking prompt while a background task runs is still WAITING

- **WHEN** a pane shows a numbered selection cursor and its chrome also carries the background-task counter
- **THEN** the pane is classified as WAITING, because WAITING outranks BACKGND

#### Scenario: Frozen transcript shell text does not produce BACKGND

- **WHEN** a pane is at its composer box with an earlier `✻ Cooked for 16s · 1 shell still running` line frozen in the transcript above the composer, and its chrome carries no counter
- **THEN** the pane is classified as IDLE, not BACKGND

#### Scenario: Tool-use shell prose does not produce BACKGND

- **WHEN** a pane's transcript contains `Ran 1 shell command` above the composer and the chrome carries no counter
- **THEN** the pane is not classified as BACKGND

#### Scenario: Task prose is not a counter even in the chrome

- **WHEN** a line below the composer reads `· 1 monitor still running` rather than a counter segment terminated by a separator or the end of the line
- **THEN** the pane is not classified as BACKGND

#### Scenario: Task exit returns the pane to IDLE

- **WHEN** a BACKGND pane's background task exits and the chrome no longer carries the counter
- **THEN** the pane is classified as IDLE on the next capture

#### Scenario: Profile without a background-task token is unaffected

- **WHEN** a Copilot pane is classified using a profile that configures no background-task token
- **THEN** the pane is never classified BACKGND and resolves to its existing state

## MODIFIED Requirements

### Requirement: Deterministic classification precedence

The system SHALL apply classification signals in a fixed precedence — WAITING, then
WORKING, then BACKGND, then IDLE, then DEAD — so that a screen matching more than one
signal resolves to a single, predictable state.

BACKGND SHALL sit below WORKING because the background-task counter remains present in
the status chrome while the agent is working, and above IDLE because a pane matching both
is making the more specific claim.

#### Scenario: WAITING outranks residual scrollback

- **WHEN** a captured screen contains both a WAITING selection footer and residual WORKING text from earlier in the scrollback
- **THEN** the pane is classified as WAITING

#### Scenario: WORKING outranks BACKGND

- **WHEN** a captured screen shows a live working indicator and a background-task counter at the same time
- **THEN** the pane is classified as WORKING

#### Scenario: BACKGND outranks IDLE

- **WHEN** a captured screen shows the composer box (satisfying the IDLE anchor) and a background-task counter below it
- **THEN** the pane is classified as BACKGND

### Requirement: Detect Claude Code WAITING/WORKING/IDLE

The system SHALL detect the WAITING, WORKING, IDLE, and BACKGND states of a Claude Code
pane from its captured screen using the `claude` profile's tokens.

WAITING is detected on a numbered selection cursor (`❯` followed by a digit and a period)
OR a selection footer carrying an up/down marker (`↑/↓`) together with a case-insensitive
cancel marker (`esc to cancel`).

WORKING is detected on a *live* status line carrying one of three qualifiers: a live
activity meter (a parenthesised duration-and-token segment such as `(32s · ↓ 1.4k
tokens)`), a report that the agent is waiting for one or more background sub-agents to
finish, or - for the brief moment before the meter appears - an ellipsis (`...`) on a line
that also begins with an asterisk spinner glyph (one of the configured animated set). The
meter and the background-agents qualifiers MUST be matched independently of the line's
leading glyph, because the spinner animation cycles through frames beyond any fixed glyph
set (live captures include `·` and `*`); anchoring every live line on a glyph whitelist
drops a large fraction of frames and flickers the pane to IDLE. A frozen end-of-turn
spinner line that is past-tense (`<Word> for <duration>`, e.g. `Crunched for 54s`) with no
ellipsis and no live meter MUST NOT be classified WORKING.

IDLE is detected on the presence of the **composer box** - a prompt line beginning with
`❯` that is not the numbered selection cursor - when neither WAITING, WORKING, nor BACKGND
is present. IDLE MUST NOT be detected from status-footer affordance hints such as
`shift+tab to cycle` or `? for shortcuts`. Those segments are conditional: the shortcuts
hint renders only while the composer is empty, and the shift+tab hint is displaced
whenever the footer needs the space, including when a background shell is running - so a
pane with a background shell and typed text loses both at once and fails to classify. The
composer prompt is the input caret rather than an affordance hint, is present in every
live state, and renders identically in both vim insert and normal modes, so it is not
subject to the same slot recycling.

BACKGND is detected on a background-task counter in the chrome below the composer line
(see "Detect BACKGND").

The specific tokens are configurable and MUST be verified against real Claude Code
captures before the defaults are relied upon.

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

- **WHEN** a Claude pane shows its composer box with no selection prompt, no live spinner line, and no background-task counter
- **THEN** the pane is classified as IDLE

#### Scenario: Displaced shift+tab hint no longer breaks IDLE

- **WHEN** a Claude pane is at its composer box with text typed into it and a footer that carries neither `shift+tab to cycle` nor `? for shortcuts`
- **THEN** the pane is still classified (IDLE, or BACKGND when a background task is running), not Unknown

#### Scenario: Composer detected in vim normal mode

- **WHEN** a Claude pane's composer is in vim normal mode rather than insert mode
- **THEN** the composer prompt is still recognised and the pane classifies as IDLE or BACKGND rather than Unknown

#### Scenario: Claude spinner animation does not change classification

- **WHEN** the Claude spinner glyph cycles through the animated set between consecutive captures while the live line keeps its gerund + ellipsis or its meter
- **THEN** the pane remains classified as WORKING

#### Scenario: No composer box is Unknown

- **WHEN** a matched Claude pane's capture shows no composer prompt line and no WAITING or WORKING signal (for example the pane is in copy-mode or the UI has changed shape)
- **THEN** the pane is classified as Unknown, so the lost classification is visible rather than masked
