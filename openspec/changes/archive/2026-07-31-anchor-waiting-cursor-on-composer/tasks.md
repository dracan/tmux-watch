## 1. Classifier

- [x] 1.1 Hoist `FindComposerLine` above the WAITING check in `Classify`, so one resolved composer index is shared by the WAITING, BACKGND, and IDLE checks
- [x] 1.2 Make `IsWaiting` line-index aware: match the selection cursor from `composer + 1` onward, which scans the whole region when no composer is present and excludes the composer line itself
- [x] 1.3 Leave the hint-line (nav marker + cancel text) check matching the whole scanned region, and document the asymmetry as the deliberate safety net
- [x] 1.4 Update the class-level and `IsWaiting` doc comments so the transcript/chrome split reads as one shared rule rather than a BACKGND-only one

## 2. Tests

- [x] 2.1 Add a scrubbed fixture (`claude-idle-cursor-prose.txt`) of a finished turn whose recap prose contains `❯ 1.` above a live composer, asserted IDLE
- [x] 2.2 Add unit coverage: cursor above the composer is not WAITING; cursor with no composer on screen is still WAITING; cursor typed into the composer is not WAITING
- [x] 2.3 Rework `Waiting_outranks_background_shell`, whose screen stacked a prompt box above a live composer - a layout no real prompt renders - to assert the same precedence via the prompt's own hint line
- [x] 2.4 Confirm the Copilot profile is unaffected, since it configures no composer pattern and so keeps whole-region cursor matching
- [x] 2.5 Run the full suite

## 3. Verification against a live pane

- [x] 3.1 Confirm the offending `❯ 1.` prose is still inside the 16-line scan window of the pane that produced the bug
- [x] 3.2 Run `./go.sh --once` and confirm that pane now reports `Idle` where it previously reported `Waiting`

## 4. Documentation

- [x] 4.1 Extend the `AGENTS.md` composer-split section to cover the WAITING cursor, and record why the hint-line half is left whole-region
- [x] 4.2 Correct the fleet-panel ceiling note, whose stated reason against widening the scan this change partly invalidates, keeping the recommendation but narrowing it to the composer-off-screen case
