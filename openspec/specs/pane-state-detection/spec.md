# pane-state-detection Specification

## Purpose
TBD - created by archiving change add-copilot-pane-watcher. Update Purpose after archive.
## Requirements
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

**The classifier SHALL report, alongside the BACKGND state, a `reason` naming which of the
two fingerprints matched.** The reason SHALL distinguish three cases: the counter alone,
the agent row alone, and both present on one screen. Matching SHALL NOT stop at the first
fingerprint found, because the two are not mutually exclusive on screen and a consumer that
treats them differently needs to know when both apply.

The reason exists because the two fingerprints describe background work with different
termination guarantees. A shell or monitor may run indefinitely - a dev server never exits
on its own - whereas a detached sub-agent always terminates and removes its own row when it
reports. Consumers that need a backstop against work which never finishes SHALL be able to
apply it to the first kind without applying it to the second.

The reason SHALL NOT become a distinct pane state. BACKGND remains one state with one
badge, one priority rank and one position in the classification precedence, whatever its
reason. Ordering the two fingerprints against each other, or splitting BACKGND in the
precedence chain, is explicitly NOT part of this requirement.

The reason SHALL be derived from the capture alone, carrying no history, so the classifier
stays stateless and fixture-testable.

#### Scenario: Finished turn with a background shell is BACKGND

- **WHEN** a Claude pane shows its composer box with no selection prompt and no live spinner line, and its footer reads `-- INSERT -- ⏵⏵ auto mode on · 1 shell · ← for agents`
- **THEN** the pane is classified as BACKGND, not IDLE and not Unknown, with a reason naming the background-task counter alone

#### Scenario: Finished turn with a background monitor is BACKGND

- **WHEN** a Claude pane's chrome carries `· 1 monitor ·` (or `· 4 monitors ·`) with no shells counted, and neither WAITING nor WORKING is present
- **THEN** the pane is classified as BACKGND, on the same footing as a background shell, with a reason naming the background-task counter alone

#### Scenario: Both kinds counted in one segment

- **WHEN** the footer reads `· 2 shells · 1 monitor ·`
- **THEN** the pane is classified as BACKGND, with a reason naming the background-task counter alone, because shells and monitors share that one fingerprint

#### Scenario: Finished turn with a detached background sub-agent is BACKGND

- **WHEN** a Claude pane shows its composer box, a footer carrying no background-task counter (for example `-- INSERT -- ⏵⏵ auto mode on (shift+tab to cycle) · ← for agents`), and a fleet panel below that footer whose rows read `● main` followed by `◯ slow-sweep  Sweep every source file under … 2m 5s · ↓ 104.3k tokens`
- **THEN** the pane is classified as BACKGND, not IDLE, with a reason naming the background-agent row alone

#### Scenario: A sub-agent and a shell together report both reasons

- **WHEN** a Claude pane's chrome carries both a background-task counter (`· 1 shell ·`) and a fleet panel listing at least one `◯` agent row
- **THEN** the pane is classified as BACKGND with a reason naming both fingerprints, not just whichever was tested first

#### Scenario: Newly launched sub-agent with no activity meter is BACKGND

- **WHEN** a background-agent row has only just appeared and carries a bare elapsed time (`0s`) with no token counter
- **THEN** the pane is classified as BACKGND, because the row marker rather than the meter is the fingerprint

#### Scenario: Several background sub-agents are still one BACKGND

- **WHEN** the fleet panel lists more than one `◯` agent row
- **THEN** the pane is classified as BACKGND with the background-agent-row reason, exactly as for a single row

#### Scenario: The main fleet row alone is not BACKGND

- **WHEN** the chrome below the composer carries a `● main` row and no `◯` agent row, and no background-task counter
- **THEN** the pane is classified as IDLE, because the main row is present regardless of whether any agent is running

#### Scenario: A profile with no agent-row token reports only the counter reason

- **WHEN** a pane is classified with an agent profile that configures a background-task counter but no background-agent row token, such as the Copilot profile, and its chrome carries a counter
- **THEN** the pane is classified as BACKGND with a reason naming the counter alone, and the profile's behaviour is otherwise unchanged

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

#### Scenario: A non-BACKGND verdict carries no reason

- **WHEN** a pane is classified as WAITING, WORKING, IDLE, DEAD or Unknown, including a screen that carries a background-agent row but is WORKING because a live spinner line outranks it
- **THEN** the reported reason is empty, because the reason is a property of BACKGND rather than of the screen

### Requirement: Classify Codex core states from profile data

The system SHALL provide a built-in Codex profile for WAITING, WORKING, and IDLE. A numbered U+203A selection cursor SHALL indicate WAITING below the last composer or anywhere in the bounded tail when no composer exists. The composer SHALL be a line-start U+203A caret excluding numbered selections.

An optional profile `WaitingChromePattern` SHALL detect blocking question and confirmation footer lines in the same composer-scoped region. Codex SHALL recognize submit-answer and submit-all footers, including free-text and notes editors, without requiring an interrupt marker on the same line.

An optional profile `WorkingLinePattern` SHALL match full live status lines containing a leading status bullet, an action label, elapsed time, and the interrupt hint. WAITING SHALL take precedence over WORKING, and WORKING over the composer-derived IDLE. New patterns SHALL default to disabled for existing profiles. The classifier SHALL never emit DONE. Codex background-task detection SHALL require the separate complete live terminal-status fingerprint; detached agents without a live UI indicator SHALL not be inferred.

#### Scenario: Command and edit approvals
- **WHEN** Codex shows a live command or file-edit approval with numbered choices and confirmation footer
- **THEN** it is WAITING

#### Scenario: Free-text or notes question
- **WHEN** Codex shows a free-text answer or notes editor with a submit-answer or submit-all footer
- **THEN** it is WAITING even though the editor resembles the composer and may include an interrupt hint

#### Scenario: Wrapped question footer
- **WHEN** the submit action and interrupt action occupy separate footer lines
- **THEN** the question remains WAITING

#### Scenario: Active turn with custom label and queued input
- **WHEN** Codex shows a timed live status line with its interrupt hint, with any action label and optional queued input or detail lines before the composer
- **THEN** it is WORKING

#### Scenario: Old menus and footer prose
- **WHEN** numbered selections or question footers remain above the last live composer after a turn
- **THEN** they do not cause WAITING

#### Scenario: Completed turn and incidental background prose
- **WHEN** Codex shows its composer below a completed-turn banner or prose about background work, with no live working or waiting signal
- **THEN** it is IDLE

#### Scenario: Missing screen evidence
- **WHEN** a live Codex pane has an empty capture or no recognized core state signal
- **THEN** it is Unknown rather than guessing IDLE

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

### Requirement: Live activity and terminal status drift
Copilot SHALL recognize verified interrupt-bearing background-wait status as live activity. Codex SHALL recognize a complete live background-terminal control line immediately above its composer as a background task. Matching SHALL remain profile-driven, stateless, and bounded. Transcript prose and invisible agent state MUST NOT establish a background fingerprint.

#### Scenario: Copilot waits for background execution
- **WHEN** Copilot renders its live background-wait status with the interrupt control
- **THEN** the pane remains WORKING rather than Unknown

#### Scenario: Codex terminal status next to composer
- **WHEN** a complete background-terminal control line immediately precedes the last composer and no higher-precedence signal exists
- **THEN** the pane is BACKGND with the background-task reason

#### Scenario: Old count in transcript
- **WHEN** a terminal count appears in prose or above intervening transcript content
- **THEN** it does not establish BACKGND
