## Why

Codex CLI panes currently appear as inert Other panes, so users cannot see when Codex needs approval or finishes a turn. Add built-in support for its core attention states alongside Copilot and Claude Code.

## What Changes

- Discover Codex by its foreground command, including the Windows executable form.
- Classify live approval and question prompts as WAITING, active turns as WORKING, and the input composer as IDLE using verified screen structure.
- Reuse the existing monitor's DONE transition, notifications, acknowledgement, and pane navigation.
- Add scrubbed or synthetic fixtures and regression coverage for stale transcript signals and state transitions.
- Document the supported Codex version and explicit profile override behavior. Codex BACKGND detection is outside this change.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `pane-discovery`: Include Codex among built-in agent profiles.
- `pane-state-detection`: Define Codex core state fingerprints and their structural boundaries.
- `attention-monitor`: Verify Codex uses the existing completed-turn lifecycle.

## Impact

Agent profile configuration, classifier extensions only where required by Codex's UI, fixtures and tests, and README documentation. No new runtime dependencies or tmux verbs. Explicit non-empty agent lists continue to replace built-in defaults.
