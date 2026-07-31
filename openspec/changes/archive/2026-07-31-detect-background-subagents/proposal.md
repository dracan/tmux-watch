## Why

A Claude Code pane that has handed the turn back while a **detached background sub-agent**
is still running classifies as IDLE. That is the one state it must not be: IDLE following
WORKING is exactly the DONE edge, so the watcher chimes and recolours the pointer the
moment the agent is launched - announcing "your move" at the start of the work rather than
at its end, and then staying silent when the agent actually reports back.

The pane sometimes flickers into the correct BACKGND state, which makes the bug look
intermittent. That is coincidence, not coverage: Claude aggregates a sub-agent's own
shells and monitors into the main pane's footer counter, so the pane reads BACKGND only
while the sub-agent happens to be holding one. Verified against live captures, the footer
counter slot never counts agents themselves - a pane with a live sub-agent and no shell
renders the ordinary `(shift+tab to cycle)` hint in that slot.

## What Changes

- Add a per-profile **background-agent row** token to `AgentProfile`, matched in the same
  chrome region below the composer that the existing background-task counter uses.
- Populate it for the `claude` profile with the fleet-panel agent row marker (a line
  beginning with `◯`, U+25EF), which the panel renders once per live background agent and
  which is distinct from the `●` (U+25CF) `main` row.
- Classify a pane BACKGND when either signal is present - the existing footer counter or a
  background-agent row - so the two are alternative fingerprints for the same state.
- Leave the DONE state machine, `backgroundGraceSeconds`, and the BACKGND precedence
  untouched. Agent rows leave the panel when the agent finishes, so the signal is
  self-releasing and the existing BACKGND→IDLE release fires at the right moment.
- Update `AGENTS.md`, whose current wording states that background sub-agents classify
  WORKING. That holds only for the *blocked* form (`Waiting for N background agents to
  finish`); the detached form hands the turn back and belongs in BACKGND.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `pane-state-detection`: BACKGND gains a second fingerprint - a background-agent row in
  the chrome - alongside the background-task counter, and the Claude profile's WORKING
  requirement is clarified to cover only the blocked sub-agent form.

## Impact

- `src/TmuxWatch/Config/AgentProfile.cs` - new pattern property and its compiler.
- `src/TmuxWatch/Config/WatchConfig.cs` - `claude` profile default; Copilot left empty.
- `src/TmuxWatch/Detection/PaneClassifier.cs` - `IsBackgnd` gains the second matcher.
- `tests/TmuxWatch.Tests/fixtures/` - a new scrubbed fixture for a detached background
  agent, plus a negative fixture proving transcript prose cannot trigger it.
- `AGENTS.md` - correct the sub-agent doctrine.
- No change to `AttentionMonitor`, `TrackedPane`, config schema, or the TUI.
