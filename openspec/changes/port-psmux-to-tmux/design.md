## Context

`tmux-watch` was implemented and verified against **psmux** on a Windows/PowerShell host (see `add-copilot-pane-watcher`). It now needs to run on a **WSL2/Linux** host where only `tmux` is present. psmux is a tmux-compatible multiplexer: the access layer already uses tmux's CLI surface, so this is a host/multiplexer swap and a rename, not a redesign. Classification, the state machine, notifications, the TUI, and the read-only guarantee are out of scope here and unchanged.

## Goals / Non-Goals

**Goals:**
- Run unchanged behaviour against `tmux` by default on a WSL2/Linux host.
- Rename the access layer from `Psmux` to `Tmux` so the code matches the `TmuxWatch` project name.
- Keep a psmux/Windows host runnable via configuration and the retained `go.ps1`.
- Preserve the read-only guarantee and all classification behaviour exactly.

**Non-Goals:**
- Watching Claude Code (separate change `watch-claude-code-sessions`).
- Dual coding-agent harness files like `CLAUDE.md`/`AGENTS.md` (separate change `support-dual-coding-agents`).
- Any change to fingerprints, the state machine, or notification channels.

## Decisions

### D1 - Swap the default executable, keep it configurable
`WatchConfig.PsmuxExecutable` (default `"psmux"`) becomes `TmuxExecutable` (default `"tmux"`). Because the executable is still a config field, a psmux host keeps working by setting it back to `"psmux"`. The verb whitelist and the `lsp -a -F <format>` enumeration are unchanged because tmux and psmux share that CLI. *Alternative considered:* auto-detect tmux-vs-psmux on PATH - rejected as needless; an explicit default plus override is clearer.

### D2 - Full rename `Psmux` -> `Tmux`, not an alias
Namespace `TmuxWatch.Psmux` -> `TmuxWatch.Tmux`; `PsmuxRunner`/`IPsmuxClient`/`PsmuxResult` -> `TmuxRunner`/`ITmuxClient`/`TmuxResult`; `FakePsmuxClient` -> `FakeTmuxClient`; `PsmuxRunnerTests` -> `TmuxRunnerTests`. This is mechanical churn but removes the standing mismatch between the project name and its internals, and it pays off in the next change where the access layer is referenced heavily. *Alternative considered:* leave the `Psmux` names and only flip the default - rejected per the explicit decision to do a full rename.

### D3 - Make `Pane.PathLabel` separator-agnostic
The current `PathLabel` rewrites `/` to `\` and splits on `\` - Windows-flavoured logic. On WSL2 `pane_current_path` is a POSIX path (`/home/dan/code/foo`). Take the last segment using both separators (or `Path`-based splitting) so the label reads naturally on Linux while still handling Windows paths. Behavioural impact is limited to the displayed label, which has no fixtures.

### D4 - Keep both run scripts
Add `go.sh` (`#!/usr/bin/env bash`; `dotnet run --project src/TmuxWatch`). Keep `go.ps1` so a Windows/psmux host still has its launcher. Neither script is load-bearing; they are conveniences.

## Risks / Trade-offs

- **tmux reports a slightly different `pane_current_command`/format value than psmux did** -> the format string uses standard tmux variables; verify on a live WSL2 tmux during implementation (`--calibrate` against real panes), exactly as the Copilot tokens were originally verified. Low risk: these variables are long-standing tmux built-ins.
- **Config-key rename breaks an existing config file** -> only the `PsmuxExecutable` key changes; documented in the proposal. Personal/greenfield use, so no back-compat shim.
- **Windows path label vs Linux** -> handled by D3; only affects a cosmetic column.

## Migration Plan

Rename in place, flip the default, add `go.sh`, update README. Verify by running `dotnet test` (fixtures and the renamed `FakeTmuxClient` must pass unchanged) and a live `--calibrate` against a WSL2 tmux server. Rollback is setting `TmuxExecutable` back to `psmux` (and the code rename is irrelevant to behaviour).

## Open Questions

- Does WSL2 `tmux` report `pane_current_command` as `copilot` for a Copilot pane the same way psmux did? Moot for this change (Copilot may not be installed here), but it is the first thing the next change must verify when adding Claude.
