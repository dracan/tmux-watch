## Context

Claude Code can now launch a sub-agent **detached**: the transcript prints
`⎿ Running in the background as @name`, the composer comes straight back to the user, and
a fleet panel renders below the footer listing the live agents. The classifier has no
signal for this, so the pane matches only the IDLE anchor (the composer box) and reads
IDLE.

That is the worst available answer. `AttentionMonitor.Promote` derives DONE from the
WORKING→IDLE edge, so a pane watched through the delegation turn announces "your move" at
the instant the agent starts. Worse, each incidental BACKGND→IDLE edge afterwards is read
as "the background work ended", releasing any held completion early.

Three live captures pin down the shape (`--calibrate` against a real pane, plus two
screenshots of the same session):

```
  transcript ─────────────────────────────────────────────────────────────
    ⎿  Running in the background as @slow-sweep      ← prose; outlives the agent

  ❯ what's it up to?                                 ← composer (IDLE anchor)
  chrome ─────────────────────────────────────────────────────────────────
      [Opus 5 (1M context)] todo | main
      ctx ... 5% | $0.55 | 0m 30s
      5hr ... 4% | 7day ... 5%
    -- INSERT -- ⏵⏵ auto mode on (shift+tab to cycle) · ← for agents
                                       └── no counter segment: agents never reach it

    ● main                                           U+25CF, always present
    ◯ slow-sweep  Sweep every source file under …    U+25EF, one per live agent
                                            2m 5s · ↓ 104.3k tokens
```

The state flickers into BACKGND intermittently, which disguises the bug as partial
coverage. It is not: Claude aggregates a sub-agent's *own* shells and monitors into the
main pane's footer counter, so the existing `BackgroundTaskPattern` fires only while the
sub-agent happens to be holding one. That is a proxy for the agent's incidental resource
use, sampled every poll - not a signal for the agent.

## Goals / Non-Goals

**Goals:**

- A pane whose turn has ended while a detached sub-agent runs classifies BACKGND.
- The signal releases itself when the agent finishes, with no history and no timer.
- The classifier stays pure and stateless; BACKGND stays a plain state with no payload.
- The blocked sub-agent form keeps classifying WORKING, unchanged.

**Non-Goals:**

- No per-kind grace period. A sub-agent may outlive `backgroundGraceSeconds` (120s) and
  chime mid-run; that is accepted (see Decisions).
- No new state, no change to precedence, `AttentionMonitor`, `TrackedPane`, the config
  schema, or the TUI.
- No detection of a background agent that is itself blocked on a prompt (Open Questions).

## Decisions

### The fleet-panel agent row is the fingerprint

Three candidate signals exist; only one is usable.

| Candidate | Verdict |
| --- | --- |
| Footer background-task counter (`· 1 shell ·`) | **Impossible.** Confirmed in three captures: agents never occupy that slot. The pane renders the ordinary `(shift+tab to cycle)` hint while an agent runs. |
| Transcript line `⎿ Running in the background as @name` | **Rejected.** It is frozen prose that survives the agent by the whole rest of the session - the identical trap as `✻ Cooked for 16s · 1 shell still running`, which the transcript/chrome split exists to exclude. |
| Fleet-panel row (`◯ name  …`) | **Chosen.** Rendered once per *live* agent, below the composer, and removed when the agent finishes. |

The panel sits below the composer, so the existing chrome region and the existing
last-match `FindComposerLine` need no change. That last-match rule is load-bearing here
and now confirmed against a real capture: the user's own earlier prompt is echoed into the
transcript as `❯ Launch a background agent named @slow-sweep…`, 18 non-blank lines above
the real composer.

### Anchor on the row glyph, not the activity meter

The row carries a trailing meter (`2m 5s · ↓ 104.3k tokens`) that superficially resembles
the WORKING meter. It is not usable as the anchor:

- It is **paren-less**, unlike the WORKING meter (`(32s · ↓ 1.4k tokens)`) - which is in
  fact why this bug exists rather than a spurious WORKING classification.
- A just-launched agent renders a bare `0s` with no separator and no token counter, so a
  meter-shaped pattern misses the first seconds of every agent.
- The duration format varies (`0s`, `2m 5s`), widening the pattern toward prose.

The row marker `◯` (U+25EF LARGE CIRCLE) at line start is structure: it is the panel's
per-agent bullet, distinct from `●` (U+25CF BLACK CIRCLE) on the `main` row. Both glyphs
were read from raw capture bytes rather than inferred from a screenshot. This follows the
"prefer structure over affordance hints" rule that moved IDLE onto the composer box.

Note that `← for agents` in the footer is **not** a signal: it is present with and without
live agents, in every capture taken.

### A separate profile key, not an alternation inside `BackgroundTaskPattern`

`BackgroundTaskPattern` is documented and tested as a *footer segment* - middot-delimited,
terminated by a separator or end of line. An agent row is a different structural object in
a different part of the chrome. Folding them together would force one regex to describe
both shapes and would make the existing pattern's careful "segment, not prose" framing
untrue.

A distinct `BackgroundAgentRowPattern` keeps each token independently overridable and
independently calibratable - relevant because Claude's footer and its fleet panel are
separate UI surfaces that will drift on separate schedules. The Copilot profile leaves it
empty, which (like the existing key) means that profile can never produce BACKGND from it.

`IsBackgnd` therefore becomes a disjunction over the same chrome lines: either token
matching is sufficient.

### Reuse the existing grace period unchanged

A background shell may never terminate, which is why `backgroundGraceSeconds` exists - a
`npm run dev` must not swallow the chime forever. A sub-agent always terminates, so that
rationale does not apply, and a 120s grace will chime mid-run for any agent that runs
longer (the captured one ran 10m 3s).

Distinguishing them would require BACKGND to carry *why* it is backgrounded - a reason on
`TrackedPane`, threaded from a classifier that currently returns a bare enum. That is a
real expansion of the state machine for a purely cosmetic gain, and the user has
explicitly accepted an occasional early chime. Deferred, not designed around: nothing here
forecloses adding it later.

### Precedence is unchanged

WORKING still outranks BACKGND, so the *blocked* form - `✻ Waiting for 2 background agents
to finish`, matched by `WorkingBackgroundAgentsPattern` - keeps classifying WORKING even
though the fleet panel is also on screen. That is correct: there the agent cannot take
input. The two forms are distinguished entirely by the live spinner line, with no new
precedence rule.

## Risks / Trade-offs

- **Fleet panel pushes the composer out of the scan window** → The panel costs one line
  per agent plus one for `● main`. In the live capture the composer sat 8 non-blank lines
  from the end with a single agent, leaving roughly 8 agents' headroom before it falls out
  of the 16-line tail, at which point `FindComposerLine` returns -1 and the pane goes
  Unknown. Left as-is deliberately: widening the scan would expose WAITING to stale `❯ 1.`
  cursors in the transcript, a worse failure than a visible Unknown. Recorded here so the
  ceiling is known rather than discovered.

- **A completed agent row lingering would pin the pane in BACKGND** → Verified against a
  live run that the row is removed on completion, along with the whole panel. This is the
  single assumption the design rests on; the negative case is covered by a fixture
  asserting that a finished-agent capture classifies IDLE.

- **Claude changes the panel glyph** → Same drift class as the vanished `esc to interrupt`
  marker. Mitigated the same way: the token is profile data, overridable in config, and
  `--calibrate` re-derives it. Failure mode is a return to today's behaviour, not a new
  one.

- **An early chime on agents longer than the grace period** → Accepted, per Decisions. The
  real completion still returns the pane to IDLE, so the row simply reads DONE somewhat
  early rather than never.

## Open Questions

- What does the main pane show when a *background* agent hits a permission prompt? If it
  stalls without surfacing anything, that is a pane genuinely blocking on the user with
  neither a WAITING footer nor any other tell - a quieter instance of this same class of
  bug. Out of scope here; worth a calibration pass of its own.
