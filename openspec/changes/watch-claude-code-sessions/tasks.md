## 1. AgentProfile model (behaviour-preserving for Copilot)

- [ ] 1.1 Add an `AgentProfile` type bundling identity (`id`, `command`, `sessionConvention`, `candidateHostCommands`) and the classification tokens currently flat on `WatchConfig` (waiting cursor/footer markers, working spinner glyphs/word/cancel marker, idle hints)
- [ ] 1.2 Add `WatchConfig.Agents` as an ordered list; ship a built-in `copilot` profile whose defaults equal today's flat tokens, and bind the flat config fields onto it for back-compat
- [ ] 1.3 Confirm `dotnet test` is fully green with only the `copilot` profile (no behavioural change yet)

## 2. Claude Code profile

- [ ] 2.1 Add a built-in `claude` profile with provisional tokens (`waitingCursorPattern` shared `❯\s*\d+\.`, `workingFooterCancelMarker` `esc to interrupt`, Claude spinner glyph set, idle hint `? for shortcuts`) and `candidateHostCommands: ["node"]`
- [ ] 2.2 Document each Claude token as provisional/calibrate-overridable in config comments and README

## 3. Multi-agent discovery

- [ ] 3.1 Replace `IsCopilot`/`CommandIsCopilot` with profile matching: for each pane, return the first profile matched by command (extension-insensitive), then session/window-name convention
- [ ] 3.2 Add bounded content-fingerprint matching: only capture panes whose command is in a profile's `candidateHostCommands`, and match them against that profile's tokens; never capture panes outside every candidate-host set for identification
- [ ] 3.3 Carry the matched `agentId` on the pane/discovery record and keep it stable across polls

## 4. Profile-parameterised classification

- [ ] 4.1 Change `PaneClassifier.Classify` to take the matched `AgentProfile` (not the whole `WatchConfig`); keep the WAITING -> WORKING -> IDLE -> DEAD precedence and per-line invariant matching
- [ ] 4.2 Update `AttentionMonitor` to look up each pane's profile and classify with it; DEAD when the command no longer matches the assigned profile (and not re-identified)
- [ ] 4.3 Carry `agentId` through `TrackedPane`

## 5. Capture spike: verify and lock Claude tokens (CRITICAL PATH)

- [ ] 5.1 Confirm what `pane_current_command` tmux reports for a real `claude` pane on this WSL2 host; adjust `command`/`candidateHostCommands` if it is not `node`
- [ ] 5.2 Run real Claude Code sessions and `--calibrate` to capture WAITING (permission prompt), WORKING, and IDLE status tails; lock the exact tokens into the `claude` profile defaults
- [ ] 5.3 Commit captured fixtures `tests/.../fixtures/claude-waiting.txt`, `claude-working.txt`, `claude-idle.txt` and add classifier tests asserting each matches the expected state with the `claude` profile
- [ ] 5.4 Add a negative test: a Copilot fixture classified with the `claude` profile (and vice versa) does not produce a false WAITING

## 6. TUI + calibrate surface the agent

- [ ] 6.1 Add an **Agent** column to the live TUI view and to the `--once` snapshot
- [ ] 6.2 Show the matched agent in `--calibrate` output per pane
- [ ] 6.3 Confirm WAITING-first prioritisation and the jump action work across mixed Copilot/Claude panes

## 7. Verify end to end

- [ ] 7.1 `dotnet test` passes with both profiles and the new Claude fixtures
- [ ] 7.2 Live run in a tmux server with both a Copilot pane (if available) and a Claude pane: both appear, classify correctly, and notify once on entering WAITING
- [ ] 7.3 Confirm read-only guarantee holds: the only captures added are bounded content-fingerprint reads of candidate-host panes; no `send-keys` to any watched pane
- [ ] 7.4 Update README with the agent-profile model, the `claude` profile, and how to calibrate Claude tokens after an upgrade
