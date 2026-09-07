# tmux-watch

Language for watching coding-agent sessions across tmux panes.

## Language

**Agent table**:
The shared table of unpaused coding-agent panes, ordered by need for attention
regardless of which coding agent each pane uses.

**Paused table**:
The shared table of panes the user has paused, regardless of coding agent.

**Agent prefix**:
A short label identifying the coding agent: CC for Claude Code, GHCP for GitHub
Copilot, or CDX for Codex. Prefixes appear alongside window names only when that
table contains multiple coding-agent types; custom agents use their configured id.
