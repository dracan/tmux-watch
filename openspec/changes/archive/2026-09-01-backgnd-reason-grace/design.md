## Context

`PaneClassifier.IsBackgnd` tests two configurable tokens - `BackgroundTaskPattern` (the
`· 2 shells · 1 monitor ·` footer segment) and `BackgroundAgentRowPattern` (the `◯` fleet
row) - and returns `bool`. `Classify` turns that into a bare `PaneState.Backgnd`, so which
fingerprint matched is computed and then discarded.

`AttentionMonitor.Promote`'s `Backgnd` arm consumes that bare state. It arms
`CompletionPending` on the WORKING → BACKGND edge, stamps `BackgndSince`, and promotes to
DONE once `now - BackgndSince >= _backgroundGrace` (`backgroundGraceSeconds`, default 120).
Because DONE is sticky across BACKGND - deliberately, so the promotion is not undone on the
next poll - the promotion is one-way until acknowledgement.

That timer is correct for a dev server and wrong for a detached sub-agent, which always
terminates and removes its own row. The observed failure: a pane 16 minutes into a
sub-agent's run had chimed at minute two and read `✓ DONE` ever since, while the parent
agent was in fact waiting to verify the sub-agent's work - not the user's move at all.

Constraints:

- The classifier is pure and fixture-tested; the reason must come from the capture alone.
- `Classify` is called from `AttentionMonitor`, from `Program.cs` (the `--once` and
  `--calibrate` paths), and from roughly thirty test call sites that compare its result
  directly against a `PaneState`.
- BACKGND's badge, priority rank and precedence position are settled and must not move.

## Goals / Non-Goals

**Goals:**

- Carry a reason out of the classifier alongside BACKGND, covering counter-only, agent-only
  and both.
- Gate the grace-period release on that reason: shells and monitors only.
- Re-base the grace window when a pane's reason narrows from agent-inclusive to
  counter-only, so a shell outliving a sub-agent gets a full grace period.
- Leave dev-server behaviour, Copilot behaviour, and every other release path untouched.

**Non-Goals:**

- Splitting BACKGND into two pane states, badges or ranks.
- Any config surface change. `backgroundGraceSeconds` keeps its name, default and meaning.
- Surfacing the reason in the TUI - no new column, badge or tooltip.
- Fixing the sticky-DONE display for the dev-server case, where a correctly promoted pane
  stops showing that a shell is still running. This change removes that symptom for
  sub-agents by never promoting them; the dev-server case is a separate presentation
  question.
- Any per-kind grace *duration*. The agent case gets no timer, not a longer one.

## Decisions

### Reason is a `[Flags]` enum, not a second `PaneState`

```csharp
[Flags]
public enum BackgndReason { None = 0, BackgroundTask = 1, BackgroundAgent = 2 }
```

Flags express the three specified cases plus the natural `None` for every non-BACKGND
state, and the gate reads as exactly what the spec says: `reason == BackgroundTask`.

*Alternative - a `Backgnd` / `BackgndAgent` state pair:* rejected. Every consumer that does
not care about the distinction - badge, rank, precedence, "needs you" aggregate, the TUI
sort - would have to match both members, and each is a place to forget one. The distinction
matters to exactly one caller.

*Alternative - a nullable "first matching token id" string:* rejected. It cannot say "both",
and the spec requires that case because a shell alongside a sub-agent must suppress the
timer.

`IsBackgnd` therefore stops short-circuiting: it must test both tokens even after the first
matches, so a pane carrying both is not reported as whichever was tested first.

### `Classify` keeps its `PaneState` signature; a sibling returns the pair

One implementation returns a `readonly record struct Classification(PaneState State,
BackgndReason Reason)`. `Classify` becomes a one-line delegation returning `.State`.

This keeps ~thirty existing test call sites and both `Program.cs` paths compiling untouched,
while `AttentionMonitor` opts in to the richer result. Because the two share a single
implementation there is no second code path to drift.

*Alternative - change `Classify` to return the record:* rejected as pure churn; the reason
has exactly one consumer.

*Alternative - an `out BackgndReason` parameter:* rejected. It makes the pure classifier's
signature awkward at every call site that does not want the reason, which is all but one.

### The agent case gets no timer at all, rather than a longer one

The spec states the rule as a termination guarantee. A sub-agent removes its own row when it
reports, so the pane releases itself by classifying IDLE (chime) or WORKING (a new turn,
which re-arms and announces on its own completion). Those are the paths a sub-agent actually
takes; the timer was never the releasing mechanism for this case.

*Alternative - a longer agent-specific grace (say 30 minutes):* rejected. It adds a second
configurable duration and a second timer to reason about, to guard a failure mode that has
not been observed, and it reintroduces the same wrong announcement merely later. If a `◯`
row is ever found to outlive its agent, that is a classifier bug to fix at the fingerprint,
not to paper over with a timer.

### Narrowing re-bases `BackgndSince`; widening does not

`TrackedPane` gains `BackgndReason LastBackgndReason`. On each BACKGND tick:

1. If the pane was not previously BACKGND, stamp `BackgndSince = now` (unchanged).
2. Otherwise, if the previous reason included `BackgroundAgent` and the current does not,
   stamp `BackgndSince = now`. The shell is now the only thing outstanding and deserves a
   full grace period measured from the moment it became so.
3. Record the current reason.

Widening (a sub-agent appearing beside a running shell) needs no re-base: the gate already
suppresses the timer while the agent flag is set, and on the agent's exit rule 2 restarts the
window. The existing `tracked.State == PaneState.Done` early return stays ahead of the gate,
so a pane already promoted is never pulled back to BACKGND by a reason change.

`LastBackgndReason` is cleared wherever `BackgndSince` is cleared today (the IDLE, WORKING
and WAITING arms), so a pane re-entering BACKGND is treated as a fresh entry.

### Copilot is unaffected by construction

`CopilotProfile` leaves `BackgroundAgentRowPattern` empty, so its reason can only ever be
`BackgroundTask` and the gate is always satisfied. No Copilot behaviour changes, and the
existing test asserting that profile's pattern is empty already pins this.

## Risks / Trade-offs

- **A `◯` row that outlives its sub-agent would now suspend the chime indefinitely, where
  before the grace period would eventually fire.** → The row's self-release is the
  documented contract, and both existing BACKGND patterns are already matched below the
  composer precisely so frozen transcript prose cannot pin the state. The parent resuming
  work (WORKING) also clears the hold independently. A regression here would be visible as a
  pane stuck in BACKGND, which is a far more legible failure than a false DONE.

- **The scan-window ceiling interacts with this.** A pane running enough sub-agents to push
  the composer out of the 16-line scan classifies Unknown, and an Unknown pane announces
  nothing; with no timer, such a pane could hold its completed turn indefinitely. → Unknown
  already suppressed the announcement before this change; the timer only fired for panes
  still reading BACKGND. Behaviour for Unknown panes is therefore unchanged, and Unknown
  remains visible in the TUI.

- **`IsBackgnd` no longer short-circuits, so both regexes run on every chrome line of every
  BACKGND pane.** → Bounded by the 16-line scan region and a handful of panes per poll,
  against a 2-second interval. Immaterial next to the tmux round trips.

- **Two ways to call the classifier could drift.** → Mitigated structurally: `Classify` is a
  delegation to the record-returning implementation, not a parallel one.

## Migration Plan

None required. No persisted state, no config schema change, no user-visible key or default
changes. `backgroundGraceSeconds` continues to mean what it meant, governing a narrower set
of panes. Rollback is reverting the commit.

`AGENTS.md` carries a paragraph stating the opposite decision - "The grace period is
deliberately *not* per-kind ... a real expansion for a cosmetic gain. Deferred, not
foreclosed." That paragraph must be rewritten in the same commit: the gain was not cosmetic,
and the reason is now carried through the state machine.

## Open Questions

None blocking. One deferred: whether the TUI should distinguish "DONE, and a shell is still
running" from a plain DONE. That is the sticky-DONE display question this change explicitly
leaves out of scope, and it only ever applies to the dev-server case now.
