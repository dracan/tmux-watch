# tmux-watch

Language for watching coding-agent sessions across tmux panes.

## Language

**Workspace snapshot**:
A saved description of all tmux panes and their session, window, and split
arrangement, used to restore a workspace after a reboot.

**Tmux session**:
A named group of tmux windows in a workspace. It is distinct from an agent
conversation that can be resumed inside a pane.

**Agent conversation**:
The coding agent's conversation with the user, whose identity may allow it to
be resumed after the original process exits.

**Resume target**:
The selected agent conversation associated with a pane in a workspace snapshot.
It is unknown when that conversation cannot be identified reliably, even if
the agent has other conversations open.

**Paused pane**:
A pane the user has set aside from active monitoring using `p`. Its paused
status is shared by watchers of the same tmux server, survives watcher restarts,
and is restored with the pane from a workspace snapshot.
_Avoid_: Pinned pane

**Agent table**:
The shared table of unpaused coding-agent panes, ordered by need for attention
regardless of which coding agent each pane uses.

**Paused table**:
The shared table of panes the user has paused, regardless of coding agent.

**Agent prefix**:
A short label identifying the coding agent: CC for Claude Code, GHCP for GitHub
Copilot, or CDX for Codex. Prefixes appear alongside window names only when that
table contains multiple coding-agent types; custom agents use their configured id.
