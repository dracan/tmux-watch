# live-agent-calibration Specification

## Purpose
Detect agent UI drift through isolated live tmux scenarios, independent state evidence, production monitor replay, and private versioned reports that preserve incomplete coverage.
## Requirements
### Requirement: Isolated live agent execution
The development harness SHALL run selected installed agents sequentially on a private tmux server with disposable projects, temporary settings, authentication preflight, configurable deadlines, and at most one retry by default. The production tmux whitelist MUST remain unchanged. Interactive authentication and failure inspection SHALL be optional; unattended runs SHALL continue past unavailable agents. Helpers and launched agents SHALL have bounded lifetimes, and normal exit SHALL clean up owned sessions unless retention is explicitly selected.

#### Scenario: Missing agent
- **WHEN** an agent is absent or cannot authenticate
- **THEN** it is reported unavailable and other selected agents still run

#### Scenario: Cancellation or timeout
- **WHEN** the run is cancelled or exceeds its deadline
- **THEN** partial evidence is saved and only harness-owned resources are cleaned up

### Requirement: Independent state evidence
Live scenarios SHALL exercise idle, working, native command/edit approval and choice/freeform/multiple-question prompts, background shells, monitors, detached and blocked subagents, combined background reasons, and dead panes for applicable agents. Expected states MUST come from helper/lifecycle evidence or explicit operator confirmation, never the production classifier. Unreached scenarios SHALL be inconclusive and unsupported profile capabilities SHALL be identified explicitly.

#### Scenario: Agent answers without invoking a question tool
- **WHEN** a question scenario produces prose without evidence of a blocking native question
- **THEN** the scenario is inconclusive rather than passing on an unrelated state

#### Scenario: Sustained work is Unknown
- **WHEN** independent evidence establishes foreground work and settled captures classify Unknown
- **THEN** the run reports a classifier mismatch and retains the evidence

### Requirement: State transitions and attention checks
The harness SHALL evaluate live captures through production classification and monitor APIs and SHALL provide deterministic replay checks for DONE, acknowledgement, background grace and reason narrowing, first sight, Unknown handling, notification counts, and pointer aggregation. Replay results MUST be distinguished from live state coverage. Physical notification testing SHALL be opt-in and restore pointer state on exit.

#### Scenario: Completed foreground turn
- **WHEN** verified working and idle captures are replayed through the monitor
- **THEN** DONE is asserted, a single completion notification is asserted, and acknowledgement clears the outstanding attention without another notification

#### Scenario: Agent background reason
- **WHEN** replay advances beyond the grace period with a background agent outstanding
- **THEN** the pane remains quiet until the applicable completion transition

### Requirement: Repeatable evidence reports
Runs SHALL sample multiple terminal widths repeatedly, record versions, dimensions, relevant settings, scenario evidence, and actual classifications, and distinguish transient samples from sustained mismatches. Every retry SHALL remain in the report. A run with incomplete or unsupported coverage MUST NOT report full success. Raw captures MUST be gitignored and credentials MUST NOT be included in preflight reports. Fixture export SHALL create reviewed candidates and provenance without automatically modifying the regression suite.

#### Scenario: Narrow terminal wraps a footer
- **WHEN** a scenario runs at the narrow width
- **THEN** captures preserve exact line breaks and glyphs and are evaluated using the production scan window

#### Scenario: Candidate export
- **WHEN** an operator supplies a reviewed scrubbed capture from a run
- **THEN** the harness creates a candidate and metadata outside the committed fixture suite

### Requirement: Correlated native lifecycle evidence
The harness SHALL distinguish parent and child lifecycle events using available session identifiers or matched native task completion. A child Stop MUST NOT establish parent IDLE. A native question result confirming the synthetic answer MAY establish the captured settled interval for that same pending request; tool entry alone SHALL remain insufficient.

#### Scenario: Child returns without a child hook ID
- **WHEN** a controlled child gate has ended, its foreground parent task has returned, and the parent turn has stopped
- **THEN** completion can be established without inventing a child identity

#### Scenario: Synthetic question answer is not received
- **WHEN** the pending question has no native result confirming the supplied answer
- **THEN** the unlabelled captures remain inconclusive

### Requirement: Explicit automated check scope
The harness SHALL provide an automated check mode with an explicit per-agent supported scenario selection and a separate full diagnostic catalog. Reports SHALL list exclusions and distinguish missing native capabilities from failures in exercised checks. Full diagnostic runs MUST preserve incomplete outcomes.

#### Scenario: Daily check excludes an unobservable capability
- **WHEN** the supported-check selection cannot verify a capability independently
- **THEN** its exclusion and reason are visible and the report does not claim that capability passed
