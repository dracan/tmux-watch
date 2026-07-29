## 1. Fixtures and calibration

- [x] 1.1 Capture the live `jacktive` screen and scrub it into a synthetic
      `tests/TmuxWatch.Tests/fixtures/claude-backgnd.txt` - composer with typed text, a
      footer reading `-- INSERT -- ⏵⏵ auto mode on · 1 shell · ← for agents`, and a frozen
      `✻ Cooked for 16s · 1 shell still running` line in the transcript above the composer
      (the line that must NOT be matched)
- [x] 1.2 Add `claude-idle-normal-mode.txt` - an idle composer in vim normal mode - to lock
      in that the composer anchor is mode-independent
- [x] 1.3 Confirm the plural footer form (`2 shells`?) against a live pane running two
      background shells; adjust the default pattern if it differs
- [x] 1.4 Re-run `./go.sh --calibrate` and confirm no pane classifies Unknown

## 2. Detection tokens

- [x] 2.1 Add `IdlePromptPattern` to `AgentProfile` - the composer prompt anchor; empty
      disables it and falls back to `IdleHints`
- [x] 2.2 Add `BackgroundTaskPattern` to `AgentProfile` - the below-composer background
      task counter; empty disables BACKGND for that profile
- [x] 2.3 Add the matching `CompileIdlePrompt()` / `CompileBackgroundTask()` helpers
      following the existing `CompileOrNull` style
- [x] 2.4 Set both on the `claude` profile in `WatchConfig.ClaudeProfile()`: the composer
      prompt as a `❯` at line start that is not the numbered selection cursor, and the
      task counter requiring the middot separator (`· N shell` / `· N monitor`)
- [x] 2.7 Cover **monitors** as well as shells - Claude joins both into the one footer
      slot (`· 2 shells · 1 monitor ·`), so matching only shells would leave a
      monitor-only pane looking plainly IDLE and chiming immediately instead of deferring
- [x] 2.5 Leave `CopilotProfile()` untouched, so it keeps classifying via `IdleHints`
- [x] 2.6 Update the `ClaudeProfile` doc comment: IDLE now keys on the composer box, and
      why the footer hints were abandoned

## 3. Classifier

- [x] 3.1 Add `Backgnd` to `PaneState` with an XML doc explaining it is quiet and
      classifier-emitted (contrast with monitor-derived `Done`)
- [x] 3.2 In `PaneClassifier`, locate the composer line within the scanned region and
      split it into transcript (above) and chrome (below)
- [x] 3.3 Add `IsBackgnd`: composer present AND the background-shell pattern matches a line
      **below** the composer line
- [x] 3.4 Change `IsIdle` to prefer the composer anchor when the profile configures one,
      falling back to `IdleHints` when it does not
- [x] 3.5 Insert BACKGND into the precedence between WORKING and IDLE
- [x] 3.6 Update the class-level doc comment with the new precedence and the
      transcript/chrome split rule
- [x] 3.7 Tests: BACKGND from the new fixture; WORKING still wins with a shell segment
      present; WAITING still wins; the frozen `1 shell still running` line and the existing
      `Ran 1 shell command` line in `claude-working-current.txt` do not produce BACKGND;
      normal-mode composer classifies; a capture with no composer is still Unknown
- [x] 3.8 Tests: every existing Claude fixture keeps its current classification under the
      composer anchor, and every Copilot fixture is unchanged

## 4. Monitor

- [x] 4.1 Add `CompletionPending` and a BACKGND-entry timestamp to `TrackedPane`
- [x] 4.2 Add `BackgroundGraceSeconds` (default 120) to `WatchConfig` with a doc comment
      covering the never-exiting-shell case and why the value is deliberately long
- [x] 4.3 Set `CompletionPending` when a pane transitions WORKING (or DONE) to BACKGND;
      clear it on announcement, on re-entering WORKING, and on entering WAITING
- [x] 4.4 Promote BACKGND to DONE when the pane classifies IDLE with `CompletionPending`
      set, notifying once through the existing path
- [x] 4.5 Promote BACKGND to DONE when it has been BACKGND longer than the grace period
      with `CompletionPending` set
- [x] 4.6 Extend DONE persistence to cover a classifier report of BACKGND as well as IDLE,
      so a grace-period promotion is not undone on the next poll
- [x] 4.7 Leave `CompletionPending` false on first sight, so no exit from BACKGND announces
- [x] 4.8 Confirm BACKGND does not set `AttentionOutstanding` and does not reach the
      pointer aggregate in `WatcherApp`
- [x] 4.9 Tests with a fake `TimeProvider`: working to backgnd is silent; shell exit chimes
      once; grace period chimes once; continuing BACKGND after promotion does not re-chime
      or flap; first-sight BACKGND never chimes by either path; backgnd to working to
      backgnd re-arms rather than double-firing; backgnd to waiting fires only WAITING

## 5. TUI

- [x] 5.1 Add `PaneState.Backgnd => "[...]⋯ backgnd[/]"` to `StateMarkup`, lower case per
      the quiet-state convention, with an indicator distinct from working and idle
- [x] 5.2 Insert `Backgnd` into `Priority` between `Done` and `Working`
- [x] 5.3 Confirm `a` remains a no-op on a BACKGND row (it is not DONE)
- [x] 5.4 Tests for the sort position and the rendered label

## 6. Documentation

- [x] 6.1 Document BACKGND in `AGENTS.md` alongside the DONE explanation, including that
      it is classifier-emitted while DONE is monitor-derived, and the deferred chime
- [x] 6.2 Note in `AGENTS.md` that Claude IDLE now anchors on the composer box, with the
      footer-slot recycling as the reason - the same class of drift as `esc to interrupt`
- [x] 6.3 Update `README.md` if it lists the states

## 7. Verification

- [x] 7.1 `dotnet build` clean
- [x] 7.2 `dotnet test` fully green
- [x] 7.3 `./go.sh --calibrate` against live panes: the background-shell pane reads
      `backgnd`, the others unchanged
- [ ] 7.4 Live check: let a background shell finish while the watcher runs and confirm the
      chime fires exactly once at shell exit
- [x] 7.5 Confirm no additional tmux calls per poll
