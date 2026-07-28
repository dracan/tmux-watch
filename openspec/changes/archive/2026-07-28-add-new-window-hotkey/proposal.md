## Why

tmux-watch is the user's full-height dock onto the whole tmux server: it lists every pane,
agent or not, and jumps to any of them. But starting a *new* piece of work still means
leaving the watcher and using tmux's own key table. Creating a window is the one everyday
tmux action the dock cannot perform, and it is the action most often wanted from exactly
the place the watcher already occupies.

Doing this crosses a boundary the project has so far held absolutely: tmux-watch has only
ever changed **focus**. Creating a window changes tmux's **topology**. The boundary is
worth crossing - but it must be restated deliberately rather than quietly widened, in a
form that also admits the future rename / close / kill keys without needing another
rewrite.

## What Changes

- A new `n` key opens an **inline window-name prompt** rendered inside the existing live
  view, below the tables. The tables stay on screen and keep polling while the user types.
- The prompt is a cursor-aware line editor supporting printable characters, backspace,
  delete, left/right, home/end, `ctrl+w` (delete previous word), `ctrl+u` (clear line),
  enter to submit and esc to cancel. Its editing logic is a pure state machine over
  (text, cursor position) so it is unit-testable without a console.
  - Spectre's own `TextPrompt` is deliberately **not** used: it throws
    (`DefaultExclusivityMode`) when invoked inside an active `AnsiConsole.Live` display,
    so using it would require tearing the tables down and rebuilding them around the
    prompt.
- While the prompt is open it is **modal**: it consumes every keystroke, so the normal
  command and address keys do not fire.
- On submit the system creates a window in the **target session**, detached (`-d`), then
  jumps to it through the existing switch path so the focus marker moves in the same
  frame rather than at the next poll.
- The **target session** is the session of the pane carrying the focus marker (`►`),
  captured at the moment `n` is pressed - not re-resolved on submit, since polling
  continues while the user types and the marker may move. Because tmux tracks the active
  window and pane per session, more than one row can carry `►`; the tie-break prefers the
  highlighted row's session. With no marked pane, the highlighted row's session is used;
  with no rows at all, `n` does nothing.
- The typed name is passed only as the `-n` argument. An empty name omits `-n` entirely
  and lets tmux auto-name the window. No working directory is passed, so the new window
  starts in the session's default directory.
- `new-window` joins the permitted tmux verb set. The typed name MUST NOT reach the
  optional trailing shell-command argument of `new-window`, which would turn a text
  prompt into arbitrary command execution.
- **BREAKING (interaction)**: the focus marker (`►`) stops being purely passive. Every
  existing row action still targets the highlighted row; `n` is the first action that
  reads the marker, and it reads it as a *session* target rather than acting on the
  marked pane itself.
- The read-only guarantee is restated as three tiers rather than one rule:
  - **Inviolable** - never sends input to a pane; a pane's content is permanently
    read-only. No `send-keys`, paste-buffer, or `run-shell`.
  - **Focus** - `switch-client`, `select-window`, `select-pane`, as today.
  - **Lifecycle (new)** - may create, and in future rename or destroy, windows and panes,
    but only on an explicit keystroke naming a target, never automatically and never from
    the poll loop, and never with user text in a command position.
- Future `rename`, `close`, and `kill` keys are **out of scope** here, but the restated
  boundary is written to admit them; being destructive, they will additionally need a
  confirmation step that `n` does not.

## Capabilities

### New Capabilities

(None - this extends an existing capability.)

### Modified Capabilities

- `watcher-tui`: adds the new-window action and its inline prompt; replaces the read-only
  guarantee with the three-tier boundary; amends highlighted-row navigation so the focus
  marker is no longer specified as never being a target.

## Impact

- `src/TmuxWatch/Tui/WatcherApp.cs` - the `n` key, target-session resolution, prompt mode
  in the key loop, and rendering the prompt line into the live view.
- New `src/TmuxWatch/Tui/` line-editor type - the pure (text, cursor) state machine.
- `src/TmuxWatch/Tmux/TmuxRunner.cs`, `ITmuxClient.cs` - add `new-window` to the verb
  whitelist and expose a `NewWindow` operation.
- `tests/TmuxWatch.Tests/FakeTmuxClient.cs` - record created windows.
- `AGENTS.md` - restate the read-only guarantee as three tiers; add `n` to the key table.
- `README.md` - add `n` to its key table.
- Tests: new line-editor coverage, target-session resolution coverage, and a
  `TmuxRunnerTests` case that `new-window` is permitted while `send-keys` stays rejected.
- No change to discovery, classification, the attention monitor, notifications, or the
  pointer signal.
