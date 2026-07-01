# Agent guide: tmux-watch

Canonical instructions for any coding agent working in this repo (Copilot CLI,
Claude Code, ...). Per-agent files (`CLAUDE.md`, `.github/copilot-instructions.md`)
point here so there is a single source of truth.

## What this is

`tmux-watch` is a read-only C# / Spectre.Console console app that watches
coding-agent CLI sessions (GitHub Copilot CLI and Claude Code) running in **tmux**
panes, classifies each pane (WAITING / WORKING / IDLE / DEAD) from its captured
status bar, and surfaces the ones needing the user - with a jump-to-pane action.
The monitor also derives a **DONE** state ("turn finished, your move") from the
WORKING→IDLE transition; it is not a classifier signal (the classifier is stateless)
but a per-pane state-machine promotion, acknowledged back to IDLE by a keystroke.
Agents are pluggable `AgentProfile`s (`src/TmuxWatch/Config/`).

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

## The read-only guarantee (non-negotiable)

tmux-watch MUST NEVER send input to a watched pane. The tmux access layer
(`src/TmuxWatch/Tmux/TmuxRunner.cs`) whitelists only `lsp`, `capture-pane`,
`display-message`, `switch-client`, and `select-window`; `send-keys` (or anything
that injects input) must never be added or invoked. The only state change the tool
may cause is moving the *watcher's own* client focus. Preserve this by construction.

The optional **pointer signal** (`src/TmuxWatch/Pointer/`) recolours the OS mouse
pointer while a pane waits. This is a *different* boundary from the pane guarantee
above: it never touches a watched pane, but it does mutate the watcher's own OS
environment (global desktop pointer). Keep it that way - it must stay opt-in
(default off), crash-safe (restore on exit and unconditionally on startup), and a
no-op on unsupported hosts; it must never become a channel that reaches a pane.

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
