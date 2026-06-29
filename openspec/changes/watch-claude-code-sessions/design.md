## Context

The watcher classifies Copilot panes from read-only `capture-pane` output using a single flat set of tokens on `WatchConfig`, and identifies panes by `pane_current_command == copilot`. The user now also runs Claude Code in the same tmux server and wants both agents surfaced together.

Two facts shape the design:

1. **The classifier shape is agent-agnostic; only tokens differ.** WAITING (numbered `❯ N.` cursor or a nav+cancel footer), WORKING (spinner glyph + a cancel marker), IDLE (an idle hint), DEAD (command changed) are the same four tests for both agents. Copilot and Claude differ only in literal strings:

   | Signal              | Copilot                       | Claude Code (provisional)        |
   |---------------------|-------------------------------|----------------------------------|
   | WAITING cursor      | `❯\s*\d+\.`                   | `❯\s*\d+\.` (shared)             |
   | WORKING cancel mark | `esc cancel`                  | `esc to interrupt`               |
   | WORKING spinner     | `◎◉●○`                        | Claude's spinner glyph set       |
   | IDLE hint           | `/ commands · ? help`         | `? for shortcuts`                |

2. **Claude is not identifiable by foreground command.** The `claude` launcher is a Node script, so tmux reports `pane_current_command` as `node` (to be confirmed), which is too generic to match on. Claude needs a richer identity signal than Copilot does.

Provisional Claude tokens above are from prior knowledge of the Claude Code TUI, **not** from captures on this machine. They MUST be verified by a capture spike before the defaults are trusted - the same discipline that produced the Copilot fixtures.

## Goals / Non-Goals

**Goals:**
- Watch Copilot and Claude Code panes together in one view, with the same jump action.
- Make adding a third agent a config/profile change, not a code change.
- Preserve the filter-then-capture efficiency model and the read-only guarantee.
- Keep Claude tokens configurable and fixture-verified, not hardcoded-from-memory.

**Non-Goals:**
- Driving or automating either agent (no `send-keys`).
- Per-agent notification routing or distinct notification sounds (one channel for all).
- Parsing conversation content beyond the status-bar affordances.
- Auto-discovering arbitrary agents; the profile set is explicit/configured.

## Decisions

### D1 - Lift per-agent tokens into an `AgentProfile`; `WatchConfig` holds an ordered list
`AgentProfile` = `{ id, command?, sessionConvention?, candidateHostCommands[], waitingCursorPattern, waitingFooterNavMarker, waitingFooterCancelMarker, workingSpinnerGlyphs, workingWord, workingFooterCancelMarker, idleHints[] }`. `WatchConfig.Agents` is an ordered list; `copilot` and `claude` ship as built-in defaults. The existing flat token fields on `WatchConfig` map onto the `copilot` profile's defaults so an existing config keeps working. *Alternative considered:* a second flat block of `claude*` fields - rejected as not scaling past two agents and entangling the two token sets.

### D2 - Profile matching order: command, then name convention, then bounded content fingerprint
For each enumerated pane, find the first profile where:
1. `pane_current_command` equals the profile's `command` (extension-insensitive, as today) - free, covers Copilot; or
2. the session or window name matches the profile's `sessionConvention` - free, opt-in discipline; or
3. `pane_current_command` is in the profile's `candidateHostCommands` (default `["node"]` for Claude) AND a `capture-pane` of that pane matches the profile's own IDLE/WORKING/WAITING tokens - self-identifying, no discipline.

Step 3 is the only new capture cost, and it is bounded to plausible agent hosts, so a tmux full of shells/vim/lazygit is matched in step 1/2 for free and never captured. *Alternative considered:* content fingerprint for all panes - rejected because it inverts filter-then-capture into capture-everything. *Alternative considered:* name convention only - rejected as requiring discipline the user may not keep.

### D3 - `PaneClassifier` is parameterised by a profile, not by `WatchConfig`
`Classify(capture, profile, dead)` uses the profile's tokens; the precedence (WAITING -> WORKING -> IDLE -> DEAD) and the per-line invariant matching are unchanged. The classifier stays pure and fixture-testable, now per profile. The monitor selects the matched pane's profile before classifying.

### D4 - Agent id flows discovery -> tracked pane -> TUI
`Pane`/discovery result carries the matched `agentId`; `TrackedPane` keeps it; the TUI adds an **Agent** column and `--calibrate` prints it. The state machine and edge-triggered notifications are per pane id and unchanged - a Copilot pane and a Claude pane are just two tracked panes with different profiles.

### D5 - Claude tokens are provisional until a capture spike confirms them
Ship the `claude` profile defaults, but treat them as unverified. The spike: run real Claude Code sessions in tmux, `--calibrate` to dump the status tails, lock the WAITING (permission prompt) / WORKING / IDLE tokens, and commit captured fixtures (`fixtures/claude-*.txt`) with classifier tests, mirroring the Copilot fixtures. Only then are the defaults trusted. *Rationale:* design.md D2/D8 of the original build - classification keys on verified literal tokens, never assumptions.

## Risks / Trade-offs

- **`pane_current_command` for `claude` is assumed `node`** -> verify in the spike; if it is something else (e.g. `claude` via a native shim), step 1 covers it and the `candidateHostCommands` default just widens. Encoded as the first spike task.
- **Content-fingerprint false positives** (a non-Claude Node TUI showing `? for shortcuts`) -> unlikely given Claude-specific tokens, and bounded to `node` panes; the name convention (step 2) is the escape hatch for a user who wants certainty.
- **Claude spinner glyphs animate / differ by build** -> the WORKING test keys on `esc to interrupt` + any glyph from the configured set, not on a specific frame; calibration re-derives the set after a Claude upgrade.
- **Extra captures per poll for `node` panes** -> bounded to candidate hosts; for a handful of panes the cost is one extra capture each, same order as the existing per-agent capture.
- **Two agents, one notification channel** -> acceptable; the TUI's agent column tells them apart visually. Per-agent routing is a future change if needed.

## Migration Plan

Additive. Implement the profile model with `copilot` defaults first (behaviour identical to today, all existing fixtures green), then add the `claude` profile and matching, then run the capture spike to lock Claude tokens + fixtures. Rollback is removing the `claude` profile from `Agents` (or shipping only `copilot`), which restores Copilot-only behaviour exactly.

## Open Questions

- Does tmux report `pane_current_command == node` for a `claude` pane on this WSL2 host, or something else? (First spike task.)
- What is Claude Code's exact IDLE hint and permission-prompt footer wording in the current build? (Capture spike.)
- Should the content-fingerprint step require *two* matching token classes (e.g. idle hint AND prompt style) to further cut false positives, or is one class enough? Decide after seeing real captures.
