## 1. tmux access layer

- [x] 1.1 Add `new-window` (and its `neww` alias) to `TmuxRunner.AllowedVerbs`
- [x] 1.2 Add `NewWindow(string sessionName, string? windowName)` to `ITmuxClient`, documenting that the name reaches only `-n` and never a command position
- [x] 1.3 Implement it in `TmuxRunner` as `new-window -d -t <session> [-n <name>]`, omitting `-n` when the name is null or empty, and building the argument list internally so callers cannot inject extra arguments
- [x] 1.4 Record created windows in `FakeTmuxClient` so tests can assert session and name
- [x] 1.5 Extend `TmuxRunnerTests` to assert `new-window` is permitted while `send-keys` remains rejected

## 2. Line editor

- [x] 2.1 Add a `LineEditor` type in `src/TmuxWatch/Tui/` holding `(Text, Cursor)` with no console dependency
- [x] 2.2 Implement `Apply(ConsoleKeyInfo)` returning the new state plus an outcome of Editing / Submit / Cancel
- [x] 2.3 Handle printable insert, backspace, delete, left, right, home, end; clamp at both bounds rather than throwing
- [x] 2.4 Handle `ctrl+w` (delete word before cursor) and `ctrl+u` (clear line)
- [x] 2.5 Handle enter (Submit) and escape (Cancel); ignore every other key
- [x] 2.6 Add `LineEditorTests` covering each key, both bounds, mid-string edits, and word/line deletion

## 3. Target session resolution

- [x] 3.1 Add a pure static resolving the target session from the layout's rows and the highlighted id: the focused row's session, tie-broken toward the highlighted row's session when several rows are focused
- [x] 3.2 Fall back to the highlighted row's session when no row is focused; return null when there are no rows
- [x] 3.3 Add tests for single-focus, multi-session tie-break, no-focus fallback, and empty-layout cases

## 4. TUI integration

- [x] 4.1 Add prompt state to `WatcherApp` (active editor plus the session captured at keypress)
- [x] 4.2 Handle `n` in the key loop: resolve and capture the target, open the editor, and do nothing when there is no target
- [x] 4.3 Route every keystroke to the editor while the prompt is open, ahead of all existing key handling, so the prompt is modal
- [x] 4.4 On Submit, call `NewWindow`, then jump to the new window via the existing `SwitchTo` path so the focus marker updates in-frame; close the prompt
- [x] 4.5 On Cancel, close the prompt without any tmux call
- [x] 4.6 Render the prompt line below the tables inside the live view, showing the captured target session, the entered text with a visible cursor, and the enter/esc hints
- [x] 4.7 Add the `n` hint to the agent table's title line

## 5. Documentation

- [x] 5.1 Add `n` to the key table in `AGENTS.md`
- [x] 5.2 Replace the read-only guarantee section in `AGENTS.md` with the three-tier boundary (inviolable / focus / lifecycle), keeping the pointer-signal note as its separate boundary
- [x] 5.3 Add `n` to the key table in `README.md`

## 6. Verification

- [x] 6.1 `dotnet build` clean
- [x] 6.2 `dotnet test` fully green
- [x] 6.3 Confirm no new tmux call is made per poll, only on the `n` keystroke
