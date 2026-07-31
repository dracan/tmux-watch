## MODIFIED Requirements

### Requirement: Detect BACKGND (agent finished, background task running)

The system SHALL classify an agent pane as BACKGND when it is neither blocked on the user
nor working, but the agent CLI reports one or more background tasks of its own still
running. BACKGND is a **quiet** state: the agent has handed the turn back, but the work it
started has not fully finished, so the pane SHALL NOT be treated as needing the user.

BACKGND SHALL be produced by the stateless classifier from a single capture, not derived
from pane history, because the condition is fully visible on one screen.

Two independent fingerprints SHALL each be sufficient to produce BACKGND, both matched per
agent profile as configurable tokens:

1. A **background-task counter** in the agent's status chrome.
2. A **background-agent row** in the agent's status chrome - a row the agent CLI renders
   once per detached sub-agent that is still running.

The counter SHALL cover every kind of background task the agent reports in that slot, not
only shells. Claude Code renders shells and monitors into one footer segment (`· 2 shells ·
1 monitor ·`), and both carry the same meaning for this state - work the agent started
that has not finished. Recognising only one kind would leave a pane running the other
looking plainly IDLE.

The counter SHALL NOT be relied upon to detect a detached sub-agent. Agents are not
counted in that slot: a pane running a detached sub-agent with no shell or monitor of its
own renders the ordinary affordance hint there. Where a sub-agent's own shells or monitors
are aggregated into the parent pane's counter, the counter tracks that incidental resource
use rather than the sub-agent, and so MUST NOT be treated as coverage for it.

Both fingerprints SHALL be matched **only in the region below the composer prompt line**,
never in the transcript region above it. The transcript routinely contains prose that would
otherwise match - a tool-use line such as `Ran 1 shell command`, an end-of-turn line such
as `✻ Cooked for 16s · 1 shell still running` which remains frozen on screen after the task
has exited, and a delegation line such as `Running in the background as @name` which
survives the sub-agent for the remainder of the session. Any of these would pin the pane in
BACKGND indefinitely.

The background-agent row SHALL be identified by its row marker rather than by any trailing
activity meter. A newly launched agent's row carries no token counter, and its duration
format varies, so a meter-shaped token would miss the opening seconds of every agent.

A profile that configures neither token SHALL never produce BACKGND, and a profile that
configures only one SHALL be classified from that one alone, leaving its behaviour
otherwise unchanged.

#### Scenario: Finished turn with a background shell is BACKGND

- **WHEN** a Claude pane shows its composer box with no selection prompt and no live spinner line, and its footer reads `-- INSERT -- ⏵⏵ auto mode on · 1 shell · ← for agents`
- **THEN** the pane is classified as BACKGND, not IDLE and not Unknown

#### Scenario: Finished turn with a background monitor is BACKGND

- **WHEN** a Claude pane's chrome carries `· 1 monitor ·` (or `· 4 monitors ·`) with no shells counted, and neither WAITING nor WORKING is present
- **THEN** the pane is classified as BACKGND, on the same footing as a background shell

#### Scenario: Both kinds counted in one segment

- **WHEN** the footer reads `· 2 shells · 1 monitor ·`
- **THEN** the pane is classified as BACKGND

#### Scenario: Finished turn with a detached background sub-agent is BACKGND

- **WHEN** a Claude pane shows its composer box, a footer carrying no background-task counter (for example `-- INSERT -- ⏵⏵ auto mode on (shift+tab to cycle) · ← for agents`), and a fleet panel below that footer whose rows read `● main` followed by `◯ slow-sweep  Sweep every source file under … 2m 5s · ↓ 104.3k tokens`
- **THEN** the pane is classified as BACKGND, not IDLE

#### Scenario: Newly launched sub-agent with no activity meter is BACKGND

- **WHEN** a background-agent row has only just appeared and carries a bare elapsed time (`0s`) with no token counter
- **THEN** the pane is classified as BACKGND, because the row marker rather than the meter is the fingerprint

#### Scenario: Several background sub-agents are still one BACKGND

- **WHEN** the fleet panel lists more than one `◯` agent row
- **THEN** the pane is classified as BACKGND, exactly as for a single row

#### Scenario: The main fleet row alone is not BACKGND

- **WHEN** the chrome below the composer carries a `● main` row and no `◯` agent row, and no background-task counter
- **THEN** the pane is classified as IDLE, because the main row is present regardless of whether any agent is running

#### Scenario: Background task while the agent works is still WORKING

- **WHEN** a pane shows a live spinner line with an activity meter and its chrome also carries the background-task counter
- **THEN** the pane is classified as WORKING, because WORKING outranks BACKGND

#### Scenario: Blocked on sub-agents outranks the agent rows

- **WHEN** a pane shows the live spinner line `✻ Waiting for 2 background agents to finish` and the fleet panel below the composer lists those agents as `◯` rows
- **THEN** the pane is classified as WORKING, because WORKING outranks BACKGND and the agent cannot take input

#### Scenario: Blocking prompt while a background task runs is still WAITING

- **WHEN** a pane shows a numbered selection cursor and its chrome also carries the background-task counter
- **THEN** the pane is classified as WAITING, because WAITING outranks BACKGND

#### Scenario: Frozen transcript shell text does not produce BACKGND

- **WHEN** a pane is at its composer box with an earlier `✻ Cooked for 16s · 1 shell still running` line frozen in the transcript above the composer, and its chrome carries no counter
- **THEN** the pane is classified as IDLE, not BACKGND

#### Scenario: Frozen delegation prose does not produce BACKGND

- **WHEN** a pane's transcript above the composer contains `Running in the background as @slow-sweep` from a sub-agent that has since finished, and the chrome below the composer carries no agent row and no counter
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

#### Scenario: Sub-agent completion returns the pane to IDLE

- **WHEN** a detached sub-agent finishes and its row is removed from the fleet panel, leaving no agent rows and no background-task counter
- **THEN** the pane is classified as IDLE on the next capture, releasing any deferred completion through the existing BACKGND to IDLE transition

#### Scenario: Profile without a background-task token is unaffected

- **WHEN** a Copilot pane is classified using a profile that configures neither a background-task token nor a background-agent row token
- **THEN** the pane is never classified BACKGND and resolves to its existing state

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

The background-sub-agents WORKING qualifier covers only the form in which the agent is
**blocked** on its sub-agents and cannot take input. A sub-agent launched **detached** -
where the agent reports it as running in the background and returns the turn to the user -
MUST NOT be classified WORKING; it is BACKGND, because the composer is free and the agent
is not blocked. The two forms are distinguished by the live status line alone: the blocked
form renders it, the detached form does not.

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

The composer line SHALL be located by its **last** match on the screen, because the user's
own submitted prompts are echoed into the transcript with the same leading `❯` and would
otherwise be mistaken for the composer, placing the real chrome on the transcript side of
the split.

BACKGND is detected in the chrome below the composer line on either a background-task
counter or a background-agent row (see "Detect BACKGND"). Claude renders the latter as a
fleet panel below the status footer, listing a `main` row plus one row per live detached
sub-agent, each distinguished by its own row marker. The footer hint `← for agents` is
present whether or not any sub-agent is running and MUST NOT be treated as a signal.

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

#### Scenario: Claude with a detached background sub-agent is not WORKING

- **WHEN** a Claude pane's fleet panel lists a live sub-agent row but the screen carries no live status line, so the composer is free
- **THEN** the pane is NOT classified as WORKING, and is classified as BACKGND

#### Scenario: Frozen completed line is not WORKING

- **WHEN** a Claude pane is idle at the input box and an earlier `✻ Crunched for 54s` line (past-tense, no ellipsis, no meter) remains on screen
- **THEN** the pane is NOT classified as WORKING and is classified as IDLE

#### Scenario: Claude at the input box is IDLE

- **WHEN** a Claude pane shows its composer box with no selection prompt, no live spinner line, no background-task counter, and no background-agent row
- **THEN** the pane is classified as IDLE

#### Scenario: Echoed prompt in the transcript does not displace the composer

- **WHEN** a Claude pane's transcript contains an earlier submitted prompt echoed as `❯ Launch a background agent named @slow-sweep…` many lines above the real composer
- **THEN** the composer is located at the lower line, and chrome below it is still searched for the BACKGND fingerprints

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
