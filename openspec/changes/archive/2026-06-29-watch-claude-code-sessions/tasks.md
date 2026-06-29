## 1. AgentProfile model (behaviour-preserving for Copilot)

- [x] 1.1 Add an `AgentProfile` type bundling identity (`id`, `command`, `sessionNameConvention`) and the classification tokens (waiting cursor/footer markers, working spinner glyphs/word/cancel marker, `workingMarkerSufficient`, idle hints) with compile helpers
- [x] 1.2 Add `WatchConfig.Agents` as an ordered list; ship a built-in `copilot` profile whose defaults equal the original flat tokens (`WatchConfig.CopilotProfile()`)
- [x] 1.3 Confirm `dotnet test` is green with the `copilot` profile (no behavioural change)

## 2. Claude Code profile

- [x] 2.1 Add a built-in `claude` profile (`WatchConfig.ClaudeProfile()`): shared `❯\s*\d+\.` cursor, `workingFooterCancelMarker` `esc to interrupt` with `workingMarkerSufficient`, Claude spinner glyph set, idle hint `shift+tab to cycle` / `? for shortcuts`, command `claude`
- [x] 2.2 Document each Claude token as provisional/calibrate-overridable in code comments

## 3. Multi-agent discovery

- [x] 3.1 Replace Copilot-only filtering with `MatchProfile`: match the first profile by command (extension-insensitive) across all profiles, then by session-name convention
- [~] 3.2 Bounded content-fingerprint matching - DEFERRED: the spike found tmux reports `pane_current_command` as `claude`, so command match suffices; content-fingerprinting for a `node`-hosted launcher is unnecessary and kept only as a documented future fallback
- [x] 3.3 Carry the matched `agentId` on the pane (`Pane.AgentId`, stamped by discovery) and keep it stable across polls

## 4. Profile-parameterised classification

- [x] 4.1 Change `PaneClassifier` to take the matched `AgentProfile`; keep the WAITING -> WORKING -> IDLE -> DEAD precedence and per-line invariant matching
- [x] 4.2 Update `AttentionMonitor` to build a classifier per profile and classify each pane with its agent's classifier; DEAD on the dead flag (a command that stops matching simply drops out of discovery)
- [x] 4.3 Carry `agentId` through to the view (via `Pane.AgentId` on `TrackedPaneView`)

## 5. Capture spike: verify and lock Claude tokens (CRITICAL PATH)

- [x] 5.1 Confirmed `pane_current_command` is `claude` on this WSL2 host (via `--calibrate`); `command = "claude"`
- [x] 5.2 Captured the live IDLE status tail and locked the IDLE token (`shift+tab to cycle`); WORKING (`esc to interrupt`) and WAITING (numbered cursor) remain best-effort/calibrate-overridable until a live capture of those states occurs
- [x] 5.3 Committed synthetic fixtures `claude-waiting.txt` / `claude-working.txt` / `claude-idle.txt` (real UI tokens, no real content) + classifier tests asserting each state under the `claude` profile
- [x] 5.4 Added cross-profile negative tests (Claude idle is not idle under copilot; copilot working is not working under claude)

## 6. TUI + calibrate surface the agent

- [x] 6.1 Added an **Agent** column to the live TUI and the `--once` snapshot
- [x] 6.2 `--calibrate` shows the matched agent per pane
- [x] 6.3 WAITING-first prioritisation and the jump action are agent-agnostic (covered by existing OrderAll/focus tests); confirmed against live mixed output

## 7. Verify end to end

- [x] 7.1 `dotnet test` passes (60 tests) with both profiles and the new Claude fixtures
- [~] 7.2 Live `--calibrate` matches all four live `claude` panes as agent `claude` and classifies them IDLE; a live Copilot pane was not available on this host (copilot CLI not installed)
- [x] 7.3 Read-only guarantee holds: no new captures beyond matched agent panes (content-fingerprint not implemented); no `send-keys`
- [x] 7.4 README updated with the agent-profile model, the `claude` profile, and how to calibrate Claude tokens
