## Why

A finished Claude pane whose own transcript contained the string `❯ 1.` - in a recap, a
diff, a quoted screen, a paragraph of this repo's own docs - classified WAITING instead of
IDLE. WAITING outranks IDLE, so the pane never reached the WORKING -> IDLE edge that
promotes to DONE: no chime, no acknowledgement, and a row that looked like it was blocking
on the user while it sat idle. The bug was self-inflicted and reproduced live: the sentence
in `AGENTS.md` warning about stale `❯ 1.` cursors was itself enough to trigger it.

This is the same class of bug as the frozen `· 1 shell still running` text that BACKGND
already guards against, and the guard already exists in the classifier - the WAITING
cursor simply was not using it.

## What Changes

- The WAITING selection cursor is matched **only below the composer prompt line**, or
  anywhere on screen when no composer is present. Transcript prose above a live composer
  no longer produces WAITING.
- The composer line itself is excluded, so a cursor-shaped string the user is still typing
  cannot flag their own pane.
- The WAITING **footer** signal (nav marker + cancel text on one line) is deliberately
  left whole-screen as a safety net; the asymmetry is intentional and recorded.
- No configuration or token changes: `WaitingCursorPattern` is untouched, and the two
  BACKGND fingerprints, WORKING, IDLE, and the precedence order are all unchanged.
- Not breaking: every existing WAITING fixture classifies exactly as before, because a
  real prompt box replaces the composer rather than stacking above it.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `pane-state-detection`: the WAITING requirement gains a position rule for the selection
  cursor (chrome only, or the whole screen when no composer is rendered), matching the
  rule BACKGND already applies to its own fingerprints.

## Impact

- `src/TmuxWatch/Detection/PaneClassifier.cs` - `IsWaiting` becomes line-index aware and
  the composer index is resolved before the WAITING check rather than after it.
- `tests/TmuxWatch.Tests/PaneClassifierTests.cs` and a new fixture
  `claude-idle-cursor-prose.txt`.
- `AGENTS.md` - the composer-split section, plus the fleet-panel ceiling note, whose
  stated justification this change partly invalidates.
- No config, CLI, TUI, or tmux-boundary surface is touched.
