## Why

`tmux-watch` watches **GitHub Copilot CLI** panes. The user now also runs **Claude Code** sessions in the same tmux server and wants the watcher to surface *both* - which Claude pane is blocked on a permission prompt, which is still working - in one view, with the same jump-to-pane action.

Today the tool is Copilot-only by construction: identity is `pane_current_command == copilot`, and every classification token (`Working`, `/ commands`, `esc cancel`) is a single flat set on `WatchConfig`. Claude Code's TUI uses different wording (`esc to interrupt`, the input-box mode line, its own spinner glyphs). The spike found that tmux reports `pane_current_command` as `claude`, so Claude is identified by a plain command match like Copilot. Bolting a second hardcoded agent onto the flat config would not scale and would entangle the two agents' tokens.

The structure of the classifier already generalises cleanly: WAITING/WORKING/IDLE/DEAD detection is the same shape for both agents - only the *tokens* and the *identity signal* differ. So the change is to lift the per-agent tokens into a **profile** and let discovery match a pane to a profile, rather than to special-case a second agent.

## What Changes

- **Introduce an `AgentProfile`**: a named bundle of identity rules (foreground command, optional session/window-name convention, optional content-fingerprint hosts) plus the classification tokens currently flat on `WatchConfig` (waiting cursor/footer, working spinner/cancel marker, idle hints). `WatchConfig` gains an ordered `Agents` list; the legacy flat Copilot fields become the built-in `copilot` profile's defaults.
- **Add a built-in `claude` profile** with provisional Claude Code tokens (`esc to interrupt`, `? for shortcuts`, Claude spinner glyphs) - to be verified against real captures (see spike) before being trusted, exactly as the Copilot tokens were.
- **Generalise pane identity to multiple agents**: for each enumerated pane, match it to the first profile by (1) foreground-command equality (covers both `copilot` and `claude`), else (2) session/window-name convention as a backstop. Panes matching no profile are not watched and are never captured. (A bounded content-fingerprint tier was planned for a `node`-hosted Claude but is unnecessary - and deferred - since the spike found Claude reports `claude`.)
- **Classify each pane with its matched profile's tokens** instead of one global token set. The `Classify()` precedence (WAITING -> WORKING -> IDLE -> DEAD) and the read-only guarantee are unchanged.
- **Carry the agent id through** discovery -> tracked pane -> TUI, and show an **Agent** column so a mixed Copilot+Claude tmux is legible; `--calibrate` reports the matched agent per pane.
- **Capture-and-fixture spike**: capture real Claude Code WAITING (permission prompt) / WORKING / IDLE panes in tmux, lock the tokens, and add them as classifier fixtures alongside the Copilot ones.

## Capabilities

### Modified Capabilities
- `pane-discovery`: Identify watched panes against an ordered set of **agent profiles** (Copilot, Claude Code) via command / name-convention / bounded content-fingerprint, and expose the matched agent id per pane - generalising the former Copilot-only identification.
- `pane-state-detection`: Classify a pane using **its matched agent profile's** tokens rather than a single global token set, and add Claude Code WAITING/WORKING/IDLE/DEAD fingerprints.
- `watcher-tui`: Show the matched **agent** per pane so Copilot and Claude sessions are distinguishable in one view.

## Impact

- **Code**: `Config/WatchConfig.cs` (new `AgentProfile` model + `Agents` list; flat tokens become the copilot profile), `Discovery/PaneDiscovery.cs` (per-profile matching + bounded content fingerprint), `Detection/PaneClassifier.cs` (parameterised by a profile), `Monitor/TrackedPane.cs` + `AttentionMonitor.cs` (carry agent id), `Tui/WatcherApp.cs` (agent column), `Program.cs` `--calibrate` output, and new test fixtures + tests for Claude.
- **Depends on** `port-psmux-to-tmux` (this is verified against real Claude panes in tmux).
- **Detection risk**: Claude tokens and the `node` host assumption are version/runtime-specific and must be verified by capture before the defaults are trusted; `--calibrate` is the tool for that. Captured fixtures double as regression tests.
- **Config compatibility**: an existing flat-token config keeps working as the copilot profile's overrides; adding Claude is additive.
- **Read-only guarantee**: unchanged - still no `send-keys` to any watched pane; the only new reads are bounded content-fingerprint captures of candidate-host panes.
