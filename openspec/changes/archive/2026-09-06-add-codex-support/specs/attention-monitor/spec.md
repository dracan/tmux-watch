## ADDED Requirements

### Requirement: Codex shares the completed-turn lifecycle

Codex panes SHALL use the existing attention monitor: observed WORKING-to-IDLE transitions produce DONE and one attention event, first sight of IDLE produces none, and acknowledgement returns DONE to IDLE. A blocking question SHALL produce WAITING rather than a premature DONE. Codex DONE SHALL refer to the observed foreground turn; detached background completion is outside this profile's scope.

#### Scenario: Codex finishes an observed turn
- **WHEN** a discovered Codex pane is WORKING and then renders its idle composer
- **THEN** it becomes DONE, emits one completed-turn event, and repeated idle polls do not repeat the event

#### Scenario: Acknowledge Codex completion
- **WHEN** the user acknowledges a DONE Codex pane
- **THEN** it returns to IDLE without announcing the same completion again

#### Scenario: First sight is idle
- **WHEN** a Codex pane is discovered already IDLE
- **THEN** it remains IDLE and emits no completed-turn event

#### Scenario: Codex asks before continuing
- **WHEN** a WORKING Codex pane opens an approval or question editor
- **THEN** it becomes WAITING and emits a waiting event, not a completed-turn event
