## Why

This repo was authored on a machine that used **GitHub Copilot CLI** as its coding agent. It now also needs to be workable with **Claude Code**. The two need equivalent footing: each agent should pick up the same project guidance and the same OpenSpec workflow skills, without one being a second-class citizen.

Most of the plumbing already exists - `.claude/skills/*` and `.claude/commands/opsx/*` mirror `.github/skills/*` and `.github/prompts/*` (the skill bodies are byte-identical today). The gaps are small but real:

- **No top-level instruction file for either agent.** There is no `CLAUDE.md` (Claude Code) and no `AGENTS.md` / `.github/copilot-instructions.md` (Copilot CLI). An agent opening this repo gets no project overview, build/test commands, or the critical read-only constraint.
- **Duplicated skill surfaces can drift.** `.claude` and `.github` carry the same skills in two places with nothing recording that they must stay in parity.

This change closes those gaps. It is repo tooling only - no application behaviour, no `src/` changes.

## What Changes

- **Add a canonical `AGENTS.md`** at the repo root: project overview, build/test/run commands (`dotnet build`, `dotnet test`, `go.sh`/`go.ps1`), the OpenSpec workflow pointer, code conventions, and the non-negotiable **read-only guarantee** (never `send-keys` to a watched pane).
- **Add a thin `CLAUDE.md`** that defers to `AGENTS.md` (a one-line pointer / `@AGENTS.md` import) so Claude Code reads the same canonical content with zero duplication.
- **Add `.github/copilot-instructions.md`** as a thin pointer to `AGENTS.md` as well, so Copilot's instruction lookup resolves to the same source. (Copilot CLI also reads `AGENTS.md` directly; the pointer covers Copilot's other instruction paths.)
- **Record the `.claude` <-> `.github` skill parity** as a documented expectation in `AGENTS.md`, so a change to one surface is a reminder to update the other.

No spec changes to the product; this introduces one small repo-tooling capability describing the dual-agent harness.

## Capabilities

### New Capabilities
- `coding-agent-harness`: The repository provides equivalent instruction and skill surfaces for each supported coding agent (Copilot CLI, Claude Code), backed by a single canonical instruction source so guidance cannot drift between agents.

## Impact

- **New files**: `AGENTS.md` (canonical), `CLAUDE.md` (pointer), `.github/copilot-instructions.md` (pointer).
- **No code/behaviour change**: `src/` and `tests/` are untouched; the watcher is unaffected.
- **Relationship to other changes**: independent of `port-psmux-to-tmux` and `watch-claude-code-sessions`; `AGENTS.md` should reference the tmux/WSL2 host and the agent-profile model once those land, but does not block on them.
- **Maintenance**: documents the existing `.claude`/`.github` skill duplication so it stays in sync; does not de-duplicate it (out of scope).
