## Why

`tmux-watch` was built and verified on a Windows/PowerShell host against **psmux** (a tmux-compatible multiplexer). The project has since moved to a **WSL2/Linux** host where `psmux` is not installed and `tmux` is. The app cannot run as-is: its access layer shells out to a `psmux` executable that does not exist here.

The good news is that the port is almost entirely mechanical. The access layer already speaks tmux's own CLI: it enumerates with `lsp -a -F` using `#{pane_id}`, `#{pane_current_command}`, `#{pane_dead}` (tmux format variables), and the whitelisted verbs (`capture-pane -p -t`, `switch-client`, `select-window`, `display-message`) are tmux verbs verbatim. So pointing the runner at `tmux` instead of `psmux` is the substance of the change; the rest is naming and docs catching up to the project's real name.

This change makes no behavioural change to classification, the state machine, or the read-only guarantee. It is a host/multiplexer swap plus the rename that the project name (`tmux-watch`) has been promising since it was scaffolded.

## What Changes

- **Default the multiplexer executable to `tmux`** (was `psmux`) and rename the config key `PsmuxExecutable` → `TmuxExecutable`.
- **Full rename of the access layer** from `Psmux` to `Tmux`: namespace `TmuxWatch.Psmux` → `TmuxWatch.Tmux`, types `PsmuxRunner`/`IPsmuxClient`/`PsmuxResult` → `TmuxRunner`/`ITmuxClient`/`TmuxResult`, the test double `FakePsmuxClient` → `FakeTmuxClient`, and all call sites/`using`s so the code matches the `TmuxWatch` project name.
- **Add a bash run script** `go.sh` (`dotnet run --project src/TmuxWatch`) for the WSL2 host. **Keep `go.ps1`** for anyone still on a Windows/psmux host.
- **Clean up Windows path residue**: `Pane.PathLabel` currently rewrites `/` to `\` before taking the last segment; make it path-separator-agnostic so Linux `pane_current_path` values read naturally.
- **Update docs** (README, prerequisites) from "psmux on PATH / Windows-PowerShell host" to "tmux on PATH / WSL2 (Linux) host", noting the executable is still overridable via config so a psmux host keeps working.

The read-only verb whitelist, the classifier, the per-pane state machine, notifications, and the TUI are unchanged. Copilot remains the only watched agent in this change; adding Claude Code is a separate change (`watch-claude-code-sessions`).

## Capabilities

### Modified Capabilities
- `pane-discovery`: Enumerate and identify agent panes via **tmux** (default executable `tmux`, configurable) rather than a psmux-only default; the enumeration format and read-only verb whitelist are unchanged, and a psmux host remains supported by overriding the executable.

## Impact

- **Code**: `src/TmuxWatch/Psmux/*` (renamed to `Tmux/`), `Config/WatchConfig.cs` (key rename + default), `Discovery/PaneDiscovery.cs` and `Psmux/Pane.cs` (namespace + `PathLabel` cleanup), `Program.cs`, `Monitor/AttentionMonitor.cs`, `Tui/WatcherApp.cs`, and the corresponding test files/`FakePsmuxClient`.
- **External dependency**: `tmux` on PATH (was psmux); only read-only verbs plus the focus-control verbs are used, exactly as before.
- **Config compatibility**: the `PsmuxExecutable` JSON key is renamed; any existing config file must update that key. Greenfield/personal use, so no migration shim is warranted.
- **Host**: WSL2/Linux is now the default target; Windows/psmux stays runnable via the retained `go.ps1` and the configurable executable.
- **No behavioural change**: classification fixtures, the state machine, and the read-only guarantee are untouched.
