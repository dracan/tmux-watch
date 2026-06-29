# coding-agent-harness Specification

## Purpose
TBD - created by archiving change support-dual-coding-agents. Update Purpose after archive.
## Requirements
### Requirement: Canonical agent instruction source

The repository SHALL provide a single canonical instruction file (`AGENTS.md`) at the repo root containing the project overview, build/test/run commands, the OpenSpec workflow, code conventions, and the read-only guarantee. Per-agent instruction files SHALL defer to this canonical file rather than duplicate its content.

#### Scenario: Claude Code reads the canonical guidance

- **WHEN** a Claude Code session opens the repo and loads `CLAUDE.md`
- **THEN** `CLAUDE.md` directs it to `AGENTS.md`, so it receives the canonical project guidance

#### Scenario: Copilot CLI reads the canonical guidance

- **WHEN** a GitHub Copilot CLI session opens the repo
- **THEN** it resolves `AGENTS.md` (directly or via `.github/copilot-instructions.md`) and receives the same canonical project guidance

#### Scenario: Guidance cannot diverge between agents

- **WHEN** the project guidance is updated in `AGENTS.md`
- **THEN** both agents see the update with no second copy to maintain, because the per-agent files are pointers

### Requirement: Equivalent workflow skills per agent

The repository SHALL provide the OpenSpec workflow skills for each supported coding agent in that agent's own location (`.claude/` for Claude Code, `.github/` for Copilot CLI), and SHALL document that these surfaces must be kept in parity.

#### Scenario: Each agent finds its workflow skills

- **WHEN** either agent looks for the OpenSpec propose/apply/archive/explore workflow
- **THEN** it finds the corresponding skill/prompt in its own directory

#### Scenario: Parity expectation is recorded

- **WHEN** a contributor changes a workflow skill for one agent
- **THEN** `AGENTS.md` documents that the other agent's equivalent surface must be updated to match

