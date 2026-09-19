## Why

The initial harness exposes real gaps but its all-scenario report mixes missing UI capabilities with adapter failures. Close the reproducible detection and lifecycle gaps and provide a usable, repeatable validation path with explicit coverage boundaries.

## What Changes

- Recognize Copilot live background-wait activity and Codex visible background-terminal status from verified captures.
- Correlate parent/child lifecycle evidence and complete blocked-subagent recovery without guessing from screen tokens.
- Make scenario applicability and missing independent evidence explicit, with an automated supported-check mode alongside the full diagnostic catalog.
- Exercise current installed agents, preserve real evidence, and document the exact supported matrix and any UI-invisible states.
- Record the repository workflow: work, commit, and push on main unless explicitly requested otherwise.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `pane-state-detection`: Verified Copilot live wait signals and Codex background-terminal signals, with conservative treatment of invisible subagents.
- `live-agent-calibration`: Scoped lifecycle correlation, supported-check mode, explicit applicability, and refreshed live validation.

## Impact

AgentProfile data and narrowly scoped pure matching, the separate calibration tool and tests, synthetic fixtures, usage documentation, and AGENTS.md. No production tmux input capability or external helper dependency is added. Changes are committed and pushed directly to main.
