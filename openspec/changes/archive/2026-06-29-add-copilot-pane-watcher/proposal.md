## Why

When running multiple GitHub Copilot CLI sessions across psmux panes, it is easy to lose track of which ones have stopped working and are blocked waiting for you (a command-approval prompt or an `ask_user` question). You end up manually cycling through panes to find the one that needs attention. A small watcher that reads pane contents and tells you *which* Copilot session needs you — and lets you jump straight to it — removes that polling-by-hand toil.

This is feasible today: live investigation against real psmux panes confirmed that each Copilot state (working / waiting-on-you / idle / dead) is reliably identifiable from read-only `capture-pane` output, with no false positives against other TUIs.

## What Changes

- Introduce `tmux-watch`, a standalone C# / Spectre.Console console app that watches psmux panes read-only and surfaces Copilot sessions needing attention.
- Enumerate panes via `psmux lsp -a -F …` and identify Copilot sessions (foreground command `copilot`, optionally a session-name convention as a backstop).
- Classify each Copilot pane from `capture-pane -p` output into **WAITING** (blocked on user — command approval *or* `ask_user`), **WORKING**, **IDLE** (turn finished), or **DEAD**, using validated status-bar fingerprints.
- Maintain a per-pane state machine and **edge-trigger** notifications on transitions into WAITING (and optionally IDLE), so each event notifies once rather than every poll.
- Render a live status table and let the user switch the attached client to a chosen pane (`switch-client` / `select-window`) via a hotkey.
- The tool is strictly **read-only** toward watched panes — it never sends keys to a Copilot session.

No breaking changes: this is a greenfield project in an otherwise empty repository.

## Capabilities

### New Capabilities
- `pane-discovery`: Enumerate psmux panes across all sessions and identify which are Copilot CLI sessions, exposing a stable per-pane identity (pane id, session/window, target command) for downstream classification.
- `pane-state-detection`: Classify a single Copilot pane's captured screen into WAITING / WORKING / IDLE / DEAD using positive status-bar fingerprints, robust to prompt-type wording differences and other TUIs.
- `attention-monitor`: Poll discovered panes on an interval, drive a per-pane state machine, and emit edge-triggered attention events (and OS-level notifications) when a pane begins needing the user.
- `watcher-tui`: Present a live Spectre.Console status view of watched Copilot panes and provide an action to switch the terminal to a selected pane.

### Modified Capabilities
<!-- None — greenfield project, no existing specs. -->

## Impact

- **New project/code**: C# console application (Spectre.Console) under this repository; no existing code to modify.
- **External dependency**: the `psmux` CLI (v3.3.6 verified) must be on PATH; only read-only verbs are used (`lsp`, `capture-pane -p`, `display-message -p`) plus client-control verbs (`switch-client`, `select-window`) for the jump action.
- **Coupling/risk**: classification relies on Copilot CLI status-bar text, which is version-specific (verified against Copilot `v1.0.63`); fingerprints must be configurable patterns, not hardcoded constants.
- **Platform**: Windows / PowerShell host (matches the psmux target environment).
