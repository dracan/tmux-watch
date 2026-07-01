## 1. Fixtures (already captured, verify in place)

- [x] 1.1 Confirm the four scrubbed fixtures exist and contain synthetic prose with byte-exact status bars: `claude-working-current.txt`, `claude-working-subagents.txt`, `claude-waiting-slash-menu.txt`, `claude-idle-after-work.txt`
- [x] 1.2 Add a failing test per fixture asserting the expected state (WORKING, WORKING, WAITING, IDLE) against the current classifier to lock in the regression

## 2. Profile tokens (data)

- [x] 2.1 In `AgentProfile.cs`, add the live-spinner WORKING fingerprint fields/patterns: an asterisk spinner glyph set for the start-of-line test, a live-ellipsis qualifier (accept both `…` and `...`), a live-meter qualifier (`(<digits>s · `), and a background-agents qualifier (`Waiting for <N> background agent`)
- [x] 2.2 Add compiled-helper(s) for the live-spinner matcher; keep them null/no-op when the patterns are empty so other profiles are unaffected
- [x] 2.3 In `WatchConfig.ClaudeProfile()`, remove reliance on `esc to interrupt`, drop `·` from the spinner glyph set used for the start-of-line test, and set the new live-spinner token defaults
- [x] 2.4 Keep all new tokens overridable via config (no hardcoded constants in the classifier)

## 3. Classifier

- [x] 3.1 In `PaneClassifier.IsWorking`, replace the `esc to interrupt` path with the live-spinner matcher: a line that starts (after whitespace) with a spinner glyph AND is qualified by ellipsis OR meter OR the background-agents phrasing; ensure frozen `<Word> for <dur>` lines do not match
- [x] 3.2 In `PaneClassifier.IsWaiting`, match the cancel marker case-insensitively (`OrdinalIgnoreCase`) so `Esc to cancel` matches; leave the numbered-cursor branch unchanged
- [x] 3.3 Widen the scanned region (raise `_statusLineCount` enough to cover the sub-agent layout, ~20 lines from bottom) so signals above the input box / below it and tall-menu cursors are in range; preserve WAITING→WORKING→IDLE→DEAD precedence
- [x] 3.4 Verify the widened scan does not promote the frozen completed line (negative control) to WORKING

## 4. Tests

- [x] 4.1 Turn the locked-in fixture tests from task 1.2 green
- [x] 4.2 Add a spinner-animation test: cycling the glyph while the live line keeps its gerund+ellipsis/meter stays WORKING
- [x] 4.3 Add an explicit negative test: `claude-idle-after-work.txt` classifies IDLE, not WORKING
- [x] 4.4 Confirm existing Copilot and Claude fixtures still pass (no regressions); run `dotnet test`

## 5. Calibrate + docs

- [x] 5.1 Run `./go.sh --calibrate` against live Claude panes and confirm WORKING / WAITING / IDLE now report correctly
- [x] 5.2 Update the README "provisional Claude tokens" note (and the AGENTS.md pointer if needed) to the verified current-build tokens, keeping the build-specific caveat
