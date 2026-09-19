## Why

Refreshing detection after agent upgrades currently requires remembering harness commands and interpreting reports manually. A shared agent skill should carry the check, diagnosis, repair, validation, and delivery workflow from one request.

## What Changes

- Add `refresh-agent-detection` with one canonical workflow and discovery surfaces for Claude Code, Codex, and Copilot CLI.
- Select all agents by default, accept focused requests and existing reports, and preserve honest incomplete results.
- Repair verified classifier or harness drift through OpenSpec, scrubbed fixtures, and focused live verification before delivery on main.
- Document the invocation and review the skill statically without agent test runs, as requested.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `live-agent-calibration`: An agent-operated refresh workflow from preflight through verified repair and delivery, with a clean no-change path.

## Impact

Repository skill/prompt files and usage documentation only. No production classifier, tmux interface, harness executable, dependency installation, or live calibration run changes are required to create this skill.
