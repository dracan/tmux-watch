## Context

The repo already carries OpenSpec workflow skills for two agents: `.github/skills` + `.github/prompts` (Copilot) and `.claude/skills` + `.claude/commands` (Claude Code), with identical skill bodies. What is missing is a top-level instruction file for either agent and a single source of truth for that guidance. This is a small, docs-only change; the only design question is how to avoid drift between the two agents' instruction files.

## Goals / Non-Goals

**Goals:**
- Both Copilot CLI and Claude Code pick up the same project guidance when opening this repo.
- One canonical instruction file; per-agent files are thin pointers.
- Capture the read-only guarantee and build/test/run commands where every agent will see them.

**Non-Goals:**
- De-duplicating the `.claude`/`.github` skill trees (kept as-is; parity is documented, not automated).
- Any change to application code, specs, or behaviour.
- Tool-specific config beyond the instruction files (no settings/hooks here).

## Decisions

### D1 - `AGENTS.md` is canonical; `CLAUDE.md` and `copilot-instructions.md` are pointers
`AGENTS.md` holds the full guidance. `CLAUDE.md` is a one-line pointer (or `@AGENTS.md` import, which Claude Code resolves) so Claude reads the canonical content. `.github/copilot-instructions.md` likewise points to `AGENTS.md`. Single source of truth, so the two agents can never be told different things. *Alternative considered:* two full standalone files - rejected for drift. *Alternative considered:* `CLAUDE.md` canonical - rejected because `AGENTS.md` is the cross-agent convention and is read by the widest set of tools.

### D2 - Keep the duplicated skill trees, document the parity expectation
`.claude` and `.github` skill/prompt copies stay as two trees (each agent looks in its own directory). `AGENTS.md` records that they must stay in parity, turning an invisible duplication into a documented maintenance note. *Alternative considered:* symlink one tree to the other - rejected as fragile across hosts (Windows/WSL2) and confusing in version control. *Alternative considered:* a sync script - over-engineered for four small files; revisit if the skill set grows.

### D3 - `AGENTS.md` content is concise and command-first
Overview (what tmux-watch is), prerequisites (.NET 10, tmux), build/test/run (`dotnet build`, `dotnet test`, `./go.sh`), the OpenSpec workflow (changes live in `openspec/changes/`, validate with `openspec validate <id>`), the read-only guarantee, and the `.claude`/`.github` parity note. Short enough that an agent reads it fully.

## Risks / Trade-offs

- **An agent that does not resolve pointer files** would miss the guidance -> both target agents do resolve their respective files (`CLAUDE.md`, `AGENTS.md`/`copilot-instructions.md`); `AGENTS.md` itself is plain content, so the canonical file always works even if a pointer is ignored.
- **Skill trees still drift** -> documented as a parity expectation (D2); accepted residual risk for four small files.
- **`AGENTS.md` references tmux/agent-profile features not yet merged** -> write it against the target state and land this change after (or alongside) the others, or keep host-specific lines minimal until then.

## Open Questions

- Does Copilot CLI on this setup prefer `AGENTS.md` or `.github/copilot-instructions.md`? Providing both (pointer) covers either; confirm during implementation and drop the redundant one if clearly unused.
