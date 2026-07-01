## Why

A Claude Code UI update changed the status bar, and the watcher now misclassifies
every Claude pane. Live `--calibrate` against real panes shows working sessions
reported as IDLE and a slash-command selection prompt reported as Unknown. Three
concrete regressions are responsible, and the current `claude` profile tokens plus
the fixed status-tail window can no longer see the signals.

## What Changes

- **WORKING detection no longer keys on `esc to interrupt`.** The live working line
  is now `<spinner-glyph> <Gerund>... (<dur> · <arrow> <n> tokens)` (e.g.
  `✻ Enchanting... (32s · ↓ 1.4k tokens)`) with the `esc to interrupt` marker
  removed. Detection moves to: a status line that begins with an asterisk spinner
  glyph (the existing animated set) AND is qualified as *live* by a trailing
  ellipsis `...` OR a live meter `(<n>s · `. The `Waiting for N background agent(s)
  to finish` line is also classified WORKING. Frozen end-of-turn lines such as
  `✻ Crunched for 54s` / `✻ Cooked for 1m 25s` (past-tense `<Word> for <dur>`, no
  ellipsis, no meter) MUST NOT be classified WORKING.
- **WAITING selection-footer matching becomes case-insensitive.** The footer is now
  `Enter to select · ↑/↓ to navigate · Esc to cancel`; the capitalised
  `Esc to cancel` no longer matches the case-sensitive `esc to cancel` marker.
- **The status scan widens beyond the last 6 non-blank lines.** The live spinner
  line now sits *above* the input box, a sub-agent panel can render *below* it
  (15-20 lines from the bottom), and the `❯ 1.` selection cursor sits ~11 lines up.
  The scan must reach these signals while preserving the existing precedence and the
  "no false positives from scrollback / non-agent TUIs" guarantees.
- New scrubbed, synthetic fixtures captured from live panes:
  `claude-working-current.txt`, `claude-working-subagents.txt`,
  `claude-waiting-slash-menu.txt`, and `claude-idle-after-work.txt` (negative
  control for the frozen completed line).

Non-goals: no change to the Copilot profile, the read-only tmux guarantee, or the
"tokens are data, not code" principle (new tokens stay in the `claude` profile and
remain overridable).

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `pane-state-detection`: the Claude Code WORKING and WAITING requirements change
  (new live-spinner WORKING fingerprint replacing the `esc to interrupt` marker,
  background-agents WORKING state, case-insensitive cancel-marker matching), and the
  classifier's scan region is widened so signals above the input box and below it
  (sub-agent panel) are in range without admitting stale scrollback matches.

## Impact

- `src/TmuxWatch/Config/WatchConfig.cs` - `ClaudeProfile()` tokens.
- `src/TmuxWatch/Config/AgentProfile.cs` - new fields/compiled patterns for the
  live-spinner WORKING fingerprint and case-insensitive cancel marker.
- `src/TmuxWatch/Detection/PaneClassifier.cs` - WORKING matcher, WAITING cancel
  marker, and scan-region width.
- `tests/TmuxWatch.Tests/` - new fixtures and classification assertions.
- `README.md` / `AGENTS.md` - the "provisional Claude tokens" note is updated to the
  verified current-build tokens.
