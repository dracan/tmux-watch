## ADDED Requirements

### Requirement: Done pointer level-triggered with waiting precedence

The system SHALL set the done (green) pointer while one or more non-paused watched panes are DONE and no non-paused pane is WAITING, and SHALL restore the normal pointer once no non-paused pane is DONE (and none is WAITING). Paused panes SHALL be excluded from the DONE aggregate, matching the waiting aggregate. When both a non-paused DONE pane and a non-paused WAITING pane are present, the waiting (red) pointer SHALL take precedence and the green pointer SHALL NOT be shown. The pointer SHALL reflect the aggregate state and SHALL change only on transitions of that aggregate.

#### Scenario: First DONE pane sets the green pointer

- **WHEN** the aggregate goes from no non-paused panes DONE (and none WAITING) to at least one non-paused pane DONE
- **THEN** the system sets the done (green) pointer

#### Scenario: Waiting takes precedence over done

- **WHEN** at least one non-paused pane is WAITING while one or more non-paused panes are DONE
- **THEN** the system shows the waiting (red) pointer and does not show the green pointer

#### Scenario: Waiting clears while done remains

- **WHEN** the last non-paused WAITING pane leaves WAITING while at least one non-paused pane is still DONE
- **THEN** the system switches the pointer from red to green

#### Scenario: Last DONE pane acknowledged

- **WHEN** the last remaining non-paused DONE pane is acknowledged (returns to IDLE) and no non-paused pane is WAITING
- **THEN** the system restores the normal pointer

#### Scenario: DONE pane is paused

- **WHEN** the only DONE pane is paused (parked into the secondary table)
- **THEN** the system does not show the green pointer for it, and resuming the pane re-arms the green pointer

### Requirement: Done pointer appearance

The system SHALL render the done state as a green pointer, replacing the standard arrow and the text I-beam shapes so the cue is visible both over other applications and over the terminal's text area, using a shipped cursor asset. The done (green) and waiting (red) appearances SHALL be distinct.

#### Scenario: Arrow and I-beam recoloured green

- **WHEN** the done pointer is set
- **THEN** both the arrow and the I-beam pointer shapes appear in green, distinct from the waiting red

## MODIFIED Requirements

### Requirement: Level-triggered from aggregate waiting state

The system SHALL set the waiting pointer while one or more non-paused watched panes
have outstanding attention (WAITING), and SHALL restore the normal pointer once no
non-paused pane has outstanding attention. Paused panes (those parked into the
secondary table) SHALL be excluded from the aggregate. The pointer SHALL reflect the
aggregate state, not any single pane, and SHALL change only on transitions of that
aggregate. The waiting (red) pointer SHALL take precedence over the done (green) pointer
when both a non-paused WAITING pane and a non-paused DONE pane are present.

#### Scenario: First pane enters WAITING

- **WHEN** the aggregate goes from no non-paused panes waiting to at least one non-paused
  pane waiting
- **THEN** the system sets the waiting (red) pointer

#### Scenario: Additional pane enters WAITING while already waiting

- **WHEN** a second pane enters WAITING while another is already WAITING
- **THEN** the system does not re-apply the pointer (no redundant OS calls) and the
  pointer remains the waiting colour

#### Scenario: Last waiting pane resolved

- **WHEN** the last remaining non-paused waiting pane leaves WAITING so no non-paused
  pane has outstanding attention
- **THEN** the system restores the normal pointer, unless a non-paused DONE pane remains,
  in which case it switches to the green pointer

#### Scenario: Waiting panes remain

- **WHEN** one of several waiting panes leaves WAITING but at least one non-paused pane is
  still WAITING
- **THEN** the pointer remains the waiting colour

#### Scenario: Waiting pane is paused

- **WHEN** the only waiting pane is paused (parked into the secondary table)
- **THEN** the system restores the normal pointer, and resuming the pane re-arms the
  waiting pointer
