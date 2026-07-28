# Agent guide: tmux-watch

Canonical instructions for any coding agent working in this repo (Copilot CLI,
Claude Code, ...). Per-agent files (`CLAUDE.md`, `.github/copilot-instructions.md`)
point here so there is a single source of truth.

## What this is

`tmux-watch` is a C# / Spectre.Console console app - read-only toward pane *content*,
see "The tmux boundary" below - that watches
coding-agent CLI sessions (GitHub Copilot CLI and Claude Code) running in **tmux**
panes, classifies each pane (WAITING / WORKING / IDLE / DEAD) from its captured
status bar, and surfaces the ones needing the user - with a jump-to-pane action.
The monitor also derives a **DONE** state ("turn finished, your move") from the
WORKING→IDLE transition; it is not a classifier signal (the classifier is stateless)
but a per-pane state-machine promotion, acknowledged back to IDLE by a keystroke.
Agents are pluggable `AgentProfile`s (`src/TmuxWatch/Config/`).

The TUI also lists the panes that run *no* agent, in a separate "Other panes" table.
These rows are inert inventory: never captured, classified, tracked, notified on, or
able to move the pointer cue. They exist so the watcher can double as a jump target
for the whole tmux server. They ride along on the enumeration discovery already
performs, so listing them costs no extra tmux call.

## Prerequisites

- **.NET 10 SDK**.
- **tmux** on PATH (a `psmux`/Windows host also works via the `tmuxExecutable` config key).

## Build / test / run

```sh
dotnet build
dotnet test                 # full suite
./go.sh                     # run the live TUI (go.ps1 on Windows/PowerShell)
./go.sh --once              # one-shot snapshot
./go.sh --calibrate         # classify all live panes (use to derive tokens)
```

## Keys in the live TUI

| Key | Action |
| --- | --- |
| up / down | Move the highlighted row (spans every table as one list) |
| enter | Switch to the highlighted row's pane |
| `1`-`9`, then shift+`A`-`Z` | Switch to that row directly (numbering is continuous across tables) |
| `a` | Acknowledge the highlighted row when it is a DONE agent pane |
| `n` | New window in the focused pane's session - prompts inline for a name, then jumps to it |
| `p` | Pause / resume the highlighted row (works on non-agent rows as decluttering) |
| `o` | Show / hide the Other panes table (default: shown) |
| `c` | Include / exclude companion panes - non-agent panes sharing a window with an agent (default: included) |
| `w` | Wide mode: show the Path and Loc columns |
| `q` / esc | Quit |

`p` and `a` act on the **highlighted** row, not on the pane tmux happens to have
focused. The `►` marker is never the target of a *row* action; `n` is the one key that
reads it, and reads it only for the **session** it names, not to act on the marked pane.
Because tmux tracks the active window and pane per session, several rows can carry `►` at
once; `n` breaks the tie toward the highlighted row's session. The `o`, `c`, and `w`
toggles are runtime-only and reset to their defaults on each launch - they have no config
key.

While the `n` prompt is open it is **modal**: every keystroke goes to the line editor
(`src/TmuxWatch/Tui/LineEditor.cs`), so no command or address key fires and `q` is just a
character. The editor is a pure `(text, cursor)` state machine so its whole key table is
unit-tested without a console - Spectre's own `TextPrompt` cannot be used here, as it
throws when invoked inside an active `AnsiConsole.Live` display.

## The tmux boundary (three tiers)

What tmux-watch may do to tmux is bounded in three tiers. The first is absolute; the
other two are narrow and enumerated. The tmux access layer
(`src/TmuxWatch/Tmux/TmuxRunner.cs`) enforces them with a verb whitelist - preserve this
by construction.

**1. Inviolable - a pane's content is read-only, permanently.** tmux-watch MUST NEVER
send input to a pane. `send-keys`, `paste-buffer`, `send-prefix`, `run-shell`, or
anything else that injects input or executes a command must never be added to the
whitelist or invoked. This tier never widens, for any feature, ever.

**2. Focus.** `switch-client`, `select-window`, `select-pane` - moving the watcher's own
client between sessions and windows, and selecting the active pane within a window.

`select-pane` is the one focus verb whose effect is *not* confined to the watcher's own
client: it changes a window's active pane, which other clients can observe. It is allowed
because rows are per-pane and a jump must land on the pane the row names rather than on
whichever pane that window last had active; it injects no input.

**3. Lifecycle.** tmux-watch may create - and, if such keys are ever added, rename or
destroy - windows and panes. Today the only lifecycle verb is `new-window` (the `n` key).
Every lifecycle action is bound by three rules:

- **Keystroke-driven only.** It happens in direct response to an explicit keypress naming
  its target. Never on a timer, never from the poll loop, never as a side effect of
  discovery or classification. This is what keeps a watcher a watcher: no future feature
  gets to reap dead panes on its own initiative.
- **User text never reaches a command position.** `new-window` accepts a trailing shell
  command; the typed window name goes to `-n` and nowhere else. `TmuxRunner.NewWindow`
  builds its own argument list precisely so no caller can append one.
- **Destructive verbs need a confirmation step.** `new-window` is additive, so it has
  none. A future `close`/`kill` would be the first destructive verb and must not ship
  without one.

The permitted set is therefore `lsp`, `capture-pane`, `display-message`, `switch-client`,
`select-window`, `select-pane`, and `new-window`. Adding to it needs the same scrutiny:
which tier it belongs to, why the tier's rules are satisfied, and a note here.

The **pointer signal** (`src/TmuxWatch/Pointer/`) recolours the OS mouse pointer
while a pane waits. This is a *different* boundary from the three tiers above:
it never touches a watched pane, but it does mutate the watcher's own OS
environment (global desktop pointer). It is on by default (disable via
`pointerSignal.enabled` in config) and must stay crash-safe (restore on exit and
unconditionally on startup) and a no-op on unsupported hosts; it must never become
a channel that reaches a pane.

## OpenSpec workflow

This repo uses OpenSpec for non-trivial changes. Changes live in
`openspec/changes/<name>/` (proposal.md, design.md, specs/, tasks.md).

- List / inspect: `openspec list`, `openspec validate <change>`.
- Invoke the bare `openspec` CLI (not `npx`/`pnpm dlx`); change names start with a letter.
- The propose / apply / archive / explore skills are available to both agents
  (see "Agent harness parity" below). Capture decisions in the artifacts; don't
  implement during explore.

## Detection tokens are data, not code

Classification keys on per-agent status-bar tokens held in `AgentProfile`s, not
hardcoded constants. They are agent/version-specific - verify against real captures
(`--calibrate`) and keep fixtures in `tests/TmuxWatch.Tests/fixtures/`. Claude Code
tokens are build-specific and have changed before (the current build's WORKING
detection keys on the live spinner line, not the dropped `esc to interrupt` marker);
re-run `--calibrate` and update the `claude` profile after a Claude Code upgrade.

## Conventions

- Match the surrounding code's style; keep the classifier pure and fixture-tested.
- Plain ASCII in generated text (no em-dashes or curly quotes).
- Don't commit `bin/`/`obj/` (gitignored) or any real captured pane content -
  fixtures must be synthetic or scrubbed.

## Agent harness parity

OpenSpec workflow skills are duplicated per agent: `.claude/skills` +
`.claude/commands/opsx` (Claude Code) and `.github/skills` + `.github/prompts`
(Copilot CLI). When you change a workflow skill for one agent, update the other
agent's equivalent so they stay in parity.
