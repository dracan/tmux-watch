## 1. Config: default to tmux

- [ ] 1.1 Rename `WatchConfig.PsmuxExecutable` -> `TmuxExecutable` and change the default from `"psmux"` to `"tmux"`
- [ ] 1.2 Update the README config example and any sample config JSON to use `tmuxExecutable`

## 2. Rename the access layer Psmux -> Tmux

- [ ] 2.1 Rename namespace `TmuxWatch.Psmux` -> `TmuxWatch.Tmux` and move `src/TmuxWatch/Psmux/` -> `src/TmuxWatch/Tmux/`
- [ ] 2.2 Rename types: `PsmuxRunner` -> `TmuxRunner`, `IPsmuxClient` -> `ITmuxClient`, `PsmuxResult` -> `TmuxResult`; keep the verb whitelist and method bodies unchanged
- [ ] 2.3 Update all call sites and `using`s: `Program.cs`, `Discovery/PaneDiscovery.cs`, `Monitor/AttentionMonitor.cs`, `Tui/WatcherApp.cs`, `Config`
- [ ] 2.4 Rename the test double `FakePsmuxClient` -> `FakeTmuxClient` and `PsmuxRunnerTests` -> `TmuxRunnerTests`, updating all test references

## 3. Linux path cleanup

- [ ] 3.1 Make `Pane.PathLabel` separator-agnostic (handle both `/` and `\`) so WSL2 `pane_current_path` values produce a correct last-segment label
- [ ] 3.2 Add/adjust a unit test asserting a POSIX path (e.g. `/home/dan/code/foo`) yields `foo`

## 4. Run scripts

- [ ] 4.1 Add `go.sh` (`#!/usr/bin/env bash`, runs `dotnet run --project src/TmuxWatch`) and mark it executable
- [ ] 4.2 Leave `go.ps1` in place for Windows/psmux hosts

## 5. Docs

- [ ] 5.1 Update README prerequisites: `tmux` on PATH (was psmux) and WSL2/Linux host (was Windows/PowerShell), noting the executable is overridable for a psmux host
- [ ] 5.2 Update the "read-only guarantee" and topology notes to say tmux, keeping the substance unchanged

## 6. Verify

- [ ] 6.1 `dotnet test` passes with the renamed `FakeTmuxClient` and all existing classifier fixtures unchanged
- [ ] 6.2 Run `dotnet run --project src/TmuxWatch -- --calibrate` against a live WSL2 tmux server and confirm panes enumerate, capture, and classify (Copilot panes if present; otherwise confirm enumeration of non-agent panes and a clean empty watched set)
