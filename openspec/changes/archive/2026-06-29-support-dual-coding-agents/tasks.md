## 1. Canonical AGENTS.md

- [x] 1.1 Write `AGENTS.md` at the repo root: project overview (what tmux-watch is), prerequisites (.NET 10, tmux on PATH), build/test/run commands (`dotnet build`, `dotnet test`, `./go.sh` / `go.ps1`)
- [x] 1.2 Include the OpenSpec workflow: changes live in `openspec/changes/`, validate with `openspec validate <id>`, and the propose/apply/archive/explore skills
- [x] 1.3 Include the non-negotiable read-only guarantee (never `send-keys` to a watched pane; verb whitelist) and key code conventions
- [x] 1.4 Document the `.claude/` <-> `.github/` skill parity expectation

## 2. Per-agent pointer files

- [x] 2.1 Add `CLAUDE.md` as a thin pointer to `AGENTS.md` (one line or `@AGENTS.md` import)
- [x] 2.2 Add `.github/copilot-instructions.md` as a thin pointer to `AGENTS.md`

## 3. Verify

- [x] 3.1 Confirm `CLAUDE.md`, `AGENTS.md`, and `.github/copilot-instructions.md` all resolve to the same guidance and contain no duplicated body content
- [x] 3.2 Sanity-check that the `.claude` and `.github` skill/prompt trees still match (no accidental divergence introduced by this change)
- [x] 3.3 Confirm `AGENTS.md` host/feature references are consistent with whatever of `port-psmux-to-tmux` / `watch-claude-code-sessions` has landed at merge time
