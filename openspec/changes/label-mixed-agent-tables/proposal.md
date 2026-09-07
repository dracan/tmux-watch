## Why

Mixed coding-agent panes are hard to distinguish in a narrow tmux-watch split. Identify their agent without adding columns, separate tables, or a second line to each row.

## What Changes

- Prefix Window cells with CC (Claude Code), GHCP (GitHub Copilot), or CDX (Codex) only when the table contains multiple agent types.
- Decide independently for the main and Paused tables. Other panes neither count as agents nor receive a prefix.
- Preserve shared urgency ordering and navigation. Use the configured id for custom agents.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `watcher-tui`: Conditional agent identification within the existing Window cell.

## Impact

Table rendering, focused rendering tests, README, and the glossary from the design discussion. No discovery, classification, notification, or tmux-boundary changes.
