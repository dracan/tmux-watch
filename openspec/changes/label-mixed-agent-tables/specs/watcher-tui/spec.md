## ADDED Requirements

### Requirement: Conditional agent prefixes per table

The TUI SHALL keep all unpaused agent panes in one shared table with the existing urgency ordering. For each table independently, the Window cell SHALL show an agent prefix only when that table contains more than one distinct coding-agent type. Built-in prefixes SHALL be CC for Claude Code, GHCP for GitHub Copilot, and CDX for Codex. Custom agents SHALL use their configured profile id.

Only agent rows SHALL contribute to the count and receive prefixes. The main table and Paused table SHALL make this decision independently. The table SHALL retain its existing columns and navigation; prefixes SHALL not introduce line wrapping in the Window cell. Agent ids and window names SHALL be escaped for display, preserving focus indication.

#### Scenario: Mixed agents

- **WHEN** the main table contains Claude Code, GitHub Copilot, and Codex panes
- **THEN** their Window cells are prefixed with CC, GHCP, and CDX respectively

#### Scenario: One agent type

- **WHEN** all agent rows in a table use the same agent
- **THEN** none has an agent prefix, regardless of the number of panes

#### Scenario: Independent paused table

- **WHEN** the main table contains only Claude Code and the Paused table contains Copilot and Codex
- **THEN** only the Paused table shows agent prefixes

#### Scenario: Non-agent rows do not require differentiation

- **WHEN** a Paused table contains Claude Code panes and a shell
- **THEN** no prefixes appear, and the shell is never labelled as an agent

#### Scenario: Narrow window cell

- **WHEN** a mixed-agent table is rendered in a narrow split with long window names
- **THEN** the prefixed window names occupy one line per row, truncating as needed

#### Scenario: Custom profile and markup characters

- **WHEN** a custom agent shares a table with a built-in agent, and ids or window names contain markup brackets
- **THEN** the custom profile id is used as its prefix and all brackets display literally
