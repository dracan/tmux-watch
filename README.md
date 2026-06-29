# tmux-watch

A read-only watcher that tells you which **GitHub Copilot CLI** sessions running
inside **psmux** panes need your attention — and lets you jump straight to them.

When you run several Copilot sessions across psmux panes, it is easy to lose track
of which ones have stopped and are blocked waiting for you (a command-approval
prompt or an `ask_user` question). `tmux-watch` polls the panes read-only,
classifies each one, and surfaces the ones that need you.

## How it works

Each poll:

1. Enumerates panes once via `psmux lsp -a -F …` (read-only).
2. Filters to Copilot sessions (`pane_current_command == copilot`, with an optional
   session-name backstop).
3. Captures each Copilot pane with `capture-pane -p` (read-only) and classifies it
   from its status bar:

   | State | Signal at the bottom of the pane |
   |-------|----------------------------------|
   | **WAITING** | numbered cursor `❯ 1.` **or** a footer with `↑/↓` + `esc to cancel` (covers command-approval *and* `ask_user`) |
   | **WORKING** | spinner glyph (`◎ ◉ ● ○`) + the word `Working`, **or** a spinner glyph + the action label + `esc cancel` footer (newer builds show the current action instead of `Working`) |
   | **IDLE** | input box + `/ commands · ? help · space hold to record` |
   | **DEAD** | foreground command is no longer `copilot`, or the pane is dead |

The foreground-command match is extension-insensitive, so Windows panes
(`pane_current_command` reports `copilot.exe`) are detected the same as the bare
`copilot` command on Linux/macOS.

4. Drives a per-pane state machine and **notifies once** when a pane *enters*
   WAITING (edge-triggered, not every poll).

> **Read-only guarantee:** tmux-watch never sends keystrokes to a watched pane.
> The psmux access layer whitelists only `lsp`, `capture-pane`, `display-message`,
> `switch-client`, and `select-window`; `send-keys` (and anything like it) cannot
> be invoked.

## Prerequisites

- **.NET 10 SDK** (or matching runtime).
- **psmux** on `PATH` (verified against psmux v3.3.6).
- **Copilot CLI** — detection tokens were verified against `v1.0.63`. The status-bar
  wording is version-specific; if a future Copilot version changes it, run
  `--calibrate` and override the tokens in a config file (see below).

## Usage

```sh
# Live TUI: watch all Copilot panes, sorted with WAITING at the top.
dotnet run --project src/TmuxWatch -- 

# One-shot snapshot (no TUI), useful for scripts.
dotnet run --project src/TmuxWatch -- --once

# Self-test: classify every live pane and show its status tail.
dotnet run --project src/TmuxWatch -- --calibrate
```

In the TUI: press a **number key** to switch the terminal to that pane,
**`p`** to pause/resume the focused (`►`) pane, **`w`** to toggle wide mode,
and `q`/`Esc` to quit.

Wide mode reveals the **Path** and **Loc** columns, which are hidden by default
so the table fits a thin terminal split. It is per-run and starts disabled.

Pausing parks a pane in a separate **Paused** table below the main list, so the
top table stays focused on the sessions you are actively working on. Bring a pane
back by switching to it (its number still works) and pressing `p` again. Paused
state is per-run and is not persisted across restarts.

### Options

| Option | Description |
|--------|-------------|
| `--config <path>` | Load a JSON config (tokens, interval, notifications) |
| `--interval <sec>` | Poll interval override (default 2s) |
| `--notify-idle` | Also notify when a pane finishes a turn (goes IDLE) |
| `--calibrate` | Print classification of all live panes and exit |
| `--once` | Print one classification snapshot and exit |
| `-h`, `--help` | Show help |

## Configuration

All detection tokens and behaviour are configurable so a Copilot version bump is a
config change, not a code change. Pass `--config config.json`:

```json
{
  "pollIntervalSeconds": 2.0,
  "copilotCommand": "copilot",
  "sessionNameConvention": "^cop-",
  "notifyOnIdle": false,
  "notificationChannel": "bell",
  "waitingCursorPattern": "❯\\s*\\d+\\.",
  "waitingFooterNavMarker": "↑/↓",
  "waitingFooterCancelMarker": "esc to cancel",
  "workingSpinnerGlyphs": "◎◉●○",
  "workingWord": "Working",
  "workingFooterCancelMarker": "esc cancel",
  "idleHints": ["/ commands", "? help", "space hold to record"]
}
```

## Tests

```sh
dotnet test
```

The classifier is pure and tested against **real captured pane fixtures**
(`tests/TmuxWatch.Tests/fixtures/`): command-approval WAITING, `ask_user` WAITING,
WORKING, IDLE, and a lazygit control (must not be flagged).

## Topology note

The "switch to pane" action moves the attached client's focus. Run tmux-watch as a
**separate terminal/client** attached to the same psmux server rather than inside a
watched pane, so jumping to a pane doesn't move the watcher off its own screen.
