## ADDED Requirements

### Requirement: Classify Codex core states from profile data

The system SHALL provide a built-in Codex profile for WAITING, WORKING, and IDLE. A numbered U+203A selection cursor SHALL indicate WAITING below the last composer or anywhere in the bounded tail when no composer exists. The composer SHALL be a line-start U+203A caret excluding numbered selections.

An optional profile `WaitingChromePattern` SHALL detect blocking question and confirmation footer lines in the same composer-scoped region. Codex SHALL recognize submit-answer and submit-all footers, including free-text and notes editors, without requiring an interrupt marker on the same line.

An optional profile `WorkingLinePattern` SHALL match full live status lines containing a leading status bullet, an action label, elapsed time, and the interrupt hint. WAITING SHALL take precedence over WORKING, and WORKING over the composer-derived IDLE. New patterns SHALL default to disabled for existing profiles. The classifier SHALL never emit DONE and SHALL not add Codex BACKGND fingerprints.

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
