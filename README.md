# tmux-watch

A read-only watcher that tells you which **coding-agent** sessions - **GitHub
Copilot CLI** and **Claude Code** - running inside **tmux** panes need your
attention, and lets you jump straight to them.

When you run several agent sessions across tmux panes, it is easy to lose track
of which ones have stopped and are blocked waiting for you (a command/permission
prompt or a question). `tmux-watch` polls the panes read-only, classifies each
one against its agent's profile, and surfaces the ones that need you.

Agents are pluggable **profiles** (how to recognise the agent's panes and the
status-bar tokens that mark each state). Copilot and Claude Code ship built in;
a new agent is a config change, not a code change.

## How it works

Each poll:

1. Enumerates panes once via `tmux lsp -a -F …` (read-only).
2. Matches each pane to an agent profile by foreground command
   (`copilot` / `claude`), with an optional session-name backstop.
3. Captures each matched pane with `capture-pane -p` (read-only) and classifies it
   from its status bar using that agent's tokens (Copilot shown below; Claude Code
   uses its own - numbered `❯ N.` permission cursor, `esc to interrupt` while
   working, and the input-box mode line when idle):

   | State | Signal at the bottom of the pane |
   |-------|----------------------------------|
   | **WAITING** | numbered cursor `❯ 1.` **or** a footer with `↑/↓` + `esc to cancel` (covers command-approval *and* `ask_user`) |
   | **WORKING** | spinner glyph (`◎ ◉ ● ○`) + the word `Working`, **or** a spinner glyph + the action label + `esc cancel` footer (newer builds show the current action instead of `Working`) |
   | **IDLE** | input box + `/ commands · ? help · space hold to record` |
   | **DEAD** | foreground command is no longer `copilot`, or the pane is dead |

The foreground-command match is extension-insensitive, so a Windows/psmux host
(`pane_current_command` reports `copilot.exe`) is detected the same as the bare
`copilot` command on Linux/macOS.

4. Drives a per-pane state machine and **notifies once** when a pane *enters*
   WAITING (edge-triggered, not every poll).

> **Read-only guarantee:** tmux-watch never sends keystrokes to a watched pane.
> The tmux access layer whitelists only `lsp`, `capture-pane`, `display-message`,
> `switch-client`, and `select-window`; `send-keys` (and anything like it) cannot
> be invoked.

## Prerequisites

- **.NET 10 SDK** (or matching runtime).
- **tmux** on `PATH`. (A `psmux` or other tmux-compatible host also works - set
  `tmuxExecutable` in config; see below.)
- **Copilot CLI** - detection tokens were verified against `v1.0.63`. The status-bar
  wording is version-specific; if a future Copilot version changes it, run
  `--calibrate` and override the tokens in a config file (see below).

## Usage

```sh
# Live TUI: watch all Copilot panes, sorted with WAITING at the top.
./go.sh

# One-shot snapshot (no TUI), useful for scripts.
./go.sh --once

# Self-test: classify every live pane and show its status tail.
./go.sh --calibrate
```

`go.sh` is a thin wrapper over `dotnet run --project src/TmuxWatch -- "$@"`; a
`go.ps1` is kept for a Windows/PowerShell host. You can also invoke `dotnet run`
directly.

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

Behaviour and all per-agent detection tokens are configurable, so an agent
version bump is a config change, not a code change. When no `agents` list is
given, the built-in `copilot` and `claude` profiles are used. Pass
`--config config.json` to override:

```json
{
  "tmuxExecutable": "tmux",
  "pollIntervalSeconds": 2.0,
  "notifyOnIdle": false,
  "notificationChannel": "bell",
  "statusLineCount": 6,
  "agents": [
    {
      "id": "copilot",
      "command": "copilot",
      "sessionNameConvention": "^cop-",
      "waitingCursorPattern": "❯\\s*\\d+\\.",
      "waitingFooterNavMarker": "↑/↓",
      "waitingFooterCancelMarker": "esc to cancel",
      "workingSpinnerGlyphs": "◎◉●○",
      "workingWord": "Working",
      "workingFooterCancelMarker": "esc cancel",
      "idleHints": ["/ commands", "? help", "space hold to record"]
    },
    {
      "id": "claude",
      "command": "claude",
      "workingSpinnerGlyphs": "✻✽✶✷✸✹✺",
      "workingWord": "",
      "workingFooterCancelMarker": "esc to interrupt",
      "workingMarkerSufficient": true,
      "idleHints": ["shift+tab to cycle", "? for shortcuts"]
    }
  ]
}
```

Set `tmuxExecutable` to `psmux` (or another tmux-compatible CLI) to run against a
different multiplexer host.

> **Claude Code tokens are provisional.** The `claude` IDLE token was verified
> against a live pane; the WORKING/WAITING tokens are best-effort. If a Claude
> Code build changes its status bar, run `--calibrate` against a live pane and
> override the affected tokens in the `claude` profile above.

## Tests

```sh
dotnet test
```

The classifier is pure and tested against **pane fixtures**
(`tests/TmuxWatch.Tests/fixtures/`): Copilot command-approval WAITING, `ask_user`
WAITING, WORKING, IDLE, a lazygit control (must not be flagged), and Claude Code
WAITING/WORKING/IDLE. The Copilot fixtures are real captures (scrubbed of content);
the Claude fixtures are synthetic shells built around the real status-bar tokens.

## Topology note

The "switch to pane" action moves the attached client's focus. Run tmux-watch as a
**separate terminal/client** attached to the same tmux server rather than inside a
watched pane, so jumping to a pane doesn't move the watcher off its own screen.
