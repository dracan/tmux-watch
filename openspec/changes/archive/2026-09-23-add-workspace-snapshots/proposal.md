## Why

A reboot discards the user's tmux or psmux workspace. A durable, importable
snapshot lets the user recreate its windows, directories, splits, and pause
settings and manually resume identified coding-agent conversations.

## What Changes

- Export all panes as versioned, readable JSON to a timestamped file and clipboard.
- Import files or pasted JSON through CLI commands and TUI keys on tmux and psmux.
- Recreate shells and split arrangements without injecting pane input or executing
  saved commands. Display manual resume guidance; unverified IDs remain unknown.
- Preflight conflicts, directories, schema, and structural support before creation;
  retain and report partial results if creation subsequently fails.
- **BREAKING**: pause settings become persistent and shared between watcher instances.
- Extend the lifecycle boundary to explicitly invoked imports while retaining the
  permanent pane-content boundary.

## Capabilities

### New Capabilities

- `workspace-snapshots`: durable export, clipboard delivery, validated shell-only
  import, conversation identity policy, and persistent pause state.

### Modified Capabilities

- `watcher-tui`: export/import keys and shared persistent pause settings.

## Impact

Adds snapshot, persistence, and clipboard modules; extends the narrow tmux access
layer, CLI, and TUI; updates boundary documentation and tests. Both native tmux
and native Windows/psmux require round-trip validation. No agent hooks or agent
configuration changes are introduced. Scope was confirmed in the design interview
recorded in `docs/planning/workspace-snapshot.md`.
