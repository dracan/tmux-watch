## ADDED Requirements

### Requirement: Non-agent panes are inert in the snapshot

The monitor SHALL carry the non-agent pane inventory through onto its per-tick snapshot
so a single poll serves both the attention view and the pane inventory. Inventory entries
MUST remain inert: the monitor MUST NOT capture them, classify them, create per-pane
state-machine entries for them, emit attention events for them, or let them influence the
aggregate pointer cue. Enumeration failures SHALL leave the inventory empty for that tick
without disturbing the retained agent-pane state.

#### Scenario: Inventory reaches the snapshot without classification

- **WHEN** a poll enumerates both agent panes and non-agent panes
- **THEN** the snapshot contains the classified agent panes and the non-agent inventory, and no capture or classification was performed for any inventory entry

#### Scenario: Non-agent panes raise no attention events

- **WHEN** non-agent panes appear, change their foreground process, or disappear between polls
- **THEN** the monitor emits no attention event and raises no notification for them

#### Scenario: Non-agent panes do not affect the pointer aggregate

- **WHEN** the inventory is non-empty and no agent pane is WAITING or DONE
- **THEN** the aggregate pointer state is normal

#### Scenario: Enumeration failure yields an empty inventory

- **WHEN** the multiplexer enumeration fails on a tick
- **THEN** the snapshot reports the error, carries an empty inventory, and retains the previously tracked agent-pane states
