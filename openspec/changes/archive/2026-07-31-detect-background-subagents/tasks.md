## 1. Profile token

- [x] 1.1 Add a `BackgroundAgentRowPattern` property to `AgentProfile`, documented as the
      second BACKGND fingerprint - a chrome row rendered once per live detached sub-agent -
      and explaining why it is a separate key from `BackgroundTaskPattern` rather than an
      alternation inside it.
- [x] 1.2 Add a `CompileBackgroundAgentRow()` compiler alongside `CompileBackgroundTask()`,
      returning null for an empty pattern so profiles that omit it are unaffected.
- [x] 1.3 Set the pattern on the `claude` profile in `WatchConfig.ClaudeProfile()` to match
      a fleet-panel agent row (leading `◯`, U+25EF, at line start after optional
      whitespace), with a comment recording that `●` (U+25CF) is the always-present `main`
      row and must not match, and that the trailing activity meter is deliberately not part
      of the token.
- [x] 1.4 Leave `CopilotProfile()` without the pattern and confirm nothing else needs to
      change for it.

## 2. Classifier

- [x] 2.1 Compile the new pattern in the `PaneClassifier` constructor.
- [x] 2.2 Extend `IsBackgnd` to match either the background-task counter or the
      background-agent row over the same below-composer chrome lines, and update its doc
      comment to state that agents never reach the footer counter slot, so the two tokens
      are alternatives rather than one refining the other.
- [x] 2.3 Update the class-level comment on `PaneClassifier` where it describes the
      transcript/chrome split, adding the frozen `Running in the background as @name`
      delegation line as a third example of prose the split excludes.

## 3. Fixtures and tests

- [x] 3.1 Add a synthetic fixture for a pane with a detached background sub-agent: composer
      box, a footer carrying `(shift+tab to cycle) · ← for agents` and no counter, and a
      fleet panel with a `● main` row plus one `◯` agent row with a trailing meter. Assert
      it classifies BACKGND.
- [x] 3.2 Add a fixture variant with a just-launched agent row carrying a bare `0s` and no
      token counter; assert BACKGND, proving the row marker rather than the meter is the
      fingerprint.
- [x] 3.3 Add a negative fixture: the same session after the sub-agent finished - the fleet
      panel gone, `Running in the background as @slow-sweep` still frozen in the transcript
      above the composer. Assert IDLE.
- [x] 3.4 Add a test that a chrome carrying only a `● main` row and no `◯` row classifies
      IDLE.
- [x] 3.5 Add a test that multiple `◯` rows still classify BACKGND.
- [x] 3.6 Add a test that the existing `claude-working-subagents` fixture - which has both
      the `Waiting for N background agents` spinner line and `◯` rows - still classifies
      WORKING, pinning the precedence.
- [x] 3.7 Add a test that a Copilot profile with no background-agent row token is
      unaffected.
- [x] 3.8 Run `dotnet test` and confirm the whole suite passes with no regressions in the
      existing BACKGND, IDLE, or WORKING cases.

## 4. Documentation

- [x] 4.1 Correct the sub-agent doctrine in `AGENTS.md`: background sub-agents classify
      WORKING only in the blocked form (`Waiting for N background agents to finish`); a
      detached sub-agent hands the turn back and classifies BACKGND.
- [x] 4.2 Record in `AGENTS.md` that the footer background-task counter never counts
      agents, so the fleet-panel row is a required second fingerprint rather than a
      convenience - and that a sub-agent's own shells and monitors are aggregated into the
      parent pane's counter, which is why the bug presented intermittently.
- [x] 4.3 Note the scan-window ceiling in `AGENTS.md`: the fleet panel costs one line per
      agent, leaving roughly eight agents' headroom before the composer falls out of the
      16-line tail and the pane classifies Unknown.
- [x] 4.4 Update the `ClaudeProfile()` doc comment to describe both BACKGND fingerprints.
