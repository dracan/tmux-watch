## Context

`PaneClassifier` is pure and stateless: capture in, `PaneState` out, via a fixed
precedence over per-profile tokens. `AttentionMonitor` holds the per-pane history and is
the only place DONE exists - it promotes a classified IDLE to DONE when the prior tracked
state was WORKING.

That split is the constraint this change works within. The new state has to be assigned
to one side or the other, and the deferred chime has to live on the stateful side without
leaking history into the classifier.

Constraints carried in from the explore session:

- No new column. The information rides in the existing state cell.
- If the agent is still working, nothing changes - a background shell during work is just
  work.
- Agent finished with a shell alive: quiet, then chime when the shell exits.
- A shell that never exits (`npm run dev`) must not swallow the chime forever.
- Verified with the user: the composer renders `❯` in vim **normal** mode as well as
  insert, so the composer anchor holds in both. (This could not be verified from the
  watcher itself - checking would mean typing into a pane, which tier 1 of the tmux
  boundary forbids outright.)

## Goals / Non-Goals

**Goals:**

- Classify the observed `jacktive` screen correctly instead of Unknown.
- Represent "finished, shell still running" as a first-class quiet state.
- Defer the finished-turn chime to when it is actually actionable.
- Remove the footer-slot dependency that caused the bug, so the next footer reshuffle
  does not reproduce it.

**Non-Goals:**

- Showing *what* the background shell is running, or how many. The count is a detection
  token, not displayed data.
- Any action on a background shell. Killing or inspecting one would need input into the
  pane - tier 1 forbids it, permanently.
- Applying the composer anchor to the Copilot profile. Copilot's `IdleHints` are not
  known to drift and are out of scope.
- A general "background activity" abstraction covering sub-agents. Background sub-agents
  already classify WORKING (the agent is blocked on them); background shells are the
  opposite case - the agent has handed the turn back.

## Decisions

### BACKGND is classifier-emitted, unlike DONE

DONE is monitor-derived because "the turn just finished" is a statement about *history* -
no single screen shows it. BACKGND is different: "not blocked, not working, shell alive"
is fully visible in one capture. Putting it in the classifier keeps the monitor's
derivation rule to the one thing that genuinely needs history.

It also means the fix and the feature are the same code. The token that broke IDLE
becomes the token that identifies the new state:

```
   footer with a background shell
   ──────────────────────────────
   -- INSERT -- ⏵⏵ auto mode on · 1 shell · ← for agents
                                  ▲
                                  └── displaced the idle hint  →  was Unknown
                                      is the BACKGND fingerprint →  now backgnd
```

### Precedence: WAITING, WORKING, BACKGND, IDLE, DEAD

The shell segment is present while the agent works too, so BACKGND must sit below
WORKING - exactly the same load-bearing precedence that already lets the ambient
`shift+tab to cycle` hint serve as an IDLE signal. Pane `0:6.0` in the calibration run
demonstrates the existing reliance: it classified WORKING while carrying the idle hint.

BACKGND sits above IDLE because with the composer anchor in place both would otherwise
match, and BACKGND is the more specific claim.

### IDLE anchors on the composer box, not footer tokens

```
  Claude Code pane, bottom region
  ═══════════════════════════════════════════════════════
   ● Wrote 3 files                       ┐
   ✻ Cooked for 16s · 1 shell running    │  TRANSCRIPT
     new task? /clear to save 294k       ┘  (frozen, may contain shell prose)
  ─────────────────────────────── jacktive ──  ┐
   ❯ commit this                               │  COMPOSER   ← the anchor
  ────────────────────────────────────────     ┘
     -- INSERT -- ⏵⏵ auto mode on · 1 shell ·  ┐  CHROME
     ● main / ◯ general-purpose ...            ┘  (footer, optional sub-agent panel)
```

The composer `❯` is present in every live state, in both vim modes, and Claude has no
reason to recycle it - it is the input caret, not an affordance hint. Matching it is
`^\s*❯` minus the numbered-selection form (`❯ 1.`), which is the WAITING cursor; the
precedence already resolves that, and excluding it explicitly makes the pattern
independently correct.

The consequence is that Unknown recovers its documented meaning: *no recognised agent
screen*, rather than *recognised screen whose current hint slot happens to be missing*.

Alternative considered: **add `auto mode on` to `IdleHints`.** One line, fixes today,
but it is the same bet on a different conditional slot - the mode segment changes with
the permission mode. Rejected.

Alternative considered: **make IDLE the residual** (matched an agent profile, not WAITING
or WORKING, therefore IDLE). Simplest, but it deletes the safety property the code relies
on: a mid-redraw capture, or one where the spinner line fell outside the 16-line window,
would classify IDLE instead of Unknown and flicker a working pane to a false DONE. Given
that WORKING is the fragile detector - the spinner animates through frames that cannot be
fully enumerated - IDLE must not be the catch-all. Rejected.

### The shell segment is matched only below the composer line

The naive pattern is a trap. Both of these appear in the *transcript*, above the composer,
and one of them is frozen permanently into the scrollback:

```
  Ran 1 shell command                        ← claude-working-current.txt, existing fixture
  ✻ Cooked for 16s · 1 shell still running   ← jacktive; survives the shell's exit
```

Matching either would pin the pane in BACKGND long after the shell died, replacing one
stuck state with another. The structural rule - **transcript above the composer, chrome
below it** - is what makes the match safe, and it falls straight out of the anchor the
IDLE hardening already introduces. The pattern additionally requires the middot separator
(`· N shell`) so it describes a footer segment rather than prose.

### Deferred DONE, with a grace period

```
        ┌──────────┐   shell alive   ┌──────────┐   shell exits   ┌──────┐
        │ WORKING  │ ──────────────▶ │ backgnd  │ ──────────────▶ │ DONE │ ♪
        └──────────┘  completion     └──────────┘                 └──────┘
              │        pending             │                          ▲
              │      (silent)              │  2 min elapsed           │
              │                            └──────────────────────────┘
              │  no shell                                          ♪ (grace)
              └────────────────────────────────────────────────────┘
```

`CompletionPending` is set when a pane leaves WORKING for BACKGND, and it is what makes
the two exits from BACKGND announce anything. Without it there is no way to distinguish
"this pane finished a turn while you were away" from "this pane has had a dev server up
since before the watcher started".

The 2-minute grace period is the answer to the unbounded case. It is deliberately long:
at the 2s poll it takes 60 consecutive polls, so a transient WORKING miss cannot reach it.
A pane that sustains a misclassification for two full minutes has a genuinely broken
WORKING detector, which is a problem the grace period neither causes nor conceals.

Once the grace period promotes a pane to DONE, DONE must persist while the classifier
keeps reporting BACKGND, not just IDLE - otherwise the next poll drops it straight back
to `backgnd` and the announcement is undone.

### First sight never chimes, by any path

The existing rule is that a pane first seen IDLE is not DONE: no observed transition, so
no completed turn to announce. BACKGND inherits it through `CompletionPending`, which
starts false. A pane first seen with a background shell running stays quiet whether the
shell exits or the grace period expires. This is what the user asked for, and it is the
same principle rather than a new exception.

### Lowercase `backgnd`, sorted above `working`

`StateMarkup` encodes urgency in casing: `● WAITING` and `✓ DONE` uppercase, `◐ working`
/ `○ idle` / `✗ dead` lowercase. BACKGND is deliberately quiet, so it renders lowercase.
At 7 characters it is the same width as `WAITING`, so no column reflows.

Priority order puts it directly above `working` - below the two "needs you" states,
above everything else, since a pane whose agent has finished is closer to needing the
user than one still mid-turn.

## Risks / Trade-offs

- **The composer can be pushed out of the scan window.** A sub-agent panel large enough
  to fill 16 non-blank lines below the composer would hide the anchor. In that situation
  WORKING matches first anyway (the pane is busy by definition), so the practical
  exposure is small. Worth a fixture if a large panel is ever observed.
- **The grace period can announce a still-working pane.** Only if WORKING is missed for
  120 consecutive seconds, which is a broken detector rather than a blip. Accepted.
- **`· N shell ·` is itself a footer token** and could drift like any other. The
  difference is failure mode: if it drifts, BACKGND degrades to IDLE - the pane chimes
  early rather than not at all. Losing the feature is preferable to losing the chime, and
  the composer anchor guarantees IDLE still matches.
- **Counter pluralisation.** Resolved during implementation by reading the shipped Claude
  Code binary (2.1.220) rather than waiting for a live two-shell pane:
  `o===1?"1 shell":`${o} shells`` - so `shells?` is exactly right, and the same form holds
  for monitors.

## Scope widened during implementation: monitors

Reading the shipped binary to settle pluralisation surfaced that the shell counter is one
entry in a joined list:

```js
o = r - n;                                  // r = all background tasks, n = monitors
if (o > 0) i.push(o === 1 ? "1 shell"   : `${o} shells`);
if (n > 0) i.push(n === 1 ? "1 monitor" : `${n} monitors`);
```

So the slot can read `· 2 shells · 1 monitor ·`, or `· 1 monitor ·` with no shells at all.
A monitor-only pane displaces the `(shift+tab to cycle)` hint exactly the same way.

Matching only shells would have left that pane looking plainly IDLE - not the Unknown bug
(the composer anchor still classifies it), but a miss of the state it should be in, and
one that would have chimed immediately instead of deferring. Since a monitor is a
background task the agent started and has not finished, it carries the same meaning for
this state, so the token now covers both and is named `BackgroundTaskPattern` rather than
`BackgroundShellPattern`. The separator-and-terminator rule is unchanged, so
`· 1 monitor still running` is prose and still does not match.

## Open Questions

None. The vim-normal-mode composer rendering was the one open item from explore and is
confirmed; pluralisation and the monitor case were both settled during implementation by
reading the shipped Claude Code binary rather than waiting for a live pane to produce
them.
