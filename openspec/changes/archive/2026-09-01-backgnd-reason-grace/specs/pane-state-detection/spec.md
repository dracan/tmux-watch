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
