## Context

The classifier already splits a captured screen into **transcript above the composer** and
**chrome below it**, and matches both BACKGND fingerprints only in the chrome. That rule
exists because the transcript holds prose that outlived the thing it described. The WAITING
cursor had no such rule: `IsWaiting` ran `WaitingCursorPattern` (`❯\s*\d+\.`) against the
whole 16-line scan window joined into one string, and ran *before* the composer index was
resolved.

The failure mode is the mirror image of BACKGND's. BACKGND's prose was once true; WAITING's
never was. An agent that merely writes `❯ 1.` into its output - a recap, a diff, a quoted
screen - has not asked anybody anything. Because WAITING is first in the precedence chain,
such a pane never reached `IsIdle`, so the monitor never saw the WORKING -> IDLE edge and
never promoted the finished turn to DONE. The user saw a row claiming to block on them,
and got no chime for the turn that had actually finished.

The obvious fix - anchor the cursor to the chrome, as BACKGND is - raises one question that
the code alone cannot answer: **does a real prompt render the composer?** If a prompt box
stacks above a live composer, chrome-only matching would break genuine WAITING detection,
which is the worst possible regression for a tool whose job is to notice blocked panes.
A live calibration capture of an approval prompt would settle it directly; this change was
made without one, at the user's request, so the design leans on evidence already in the
repo and on a deliberate hedge.

## Goals / Non-Goals

**Goals:**

- Transcript prose containing a cursor-shaped string never produces WAITING.
- Genuine blocking prompts keep classifying WAITING with no loss of sensitivity.
- The rule matches the one BACKGND already uses, so the classifier has one split concept
  rather than two.

**Non-Goals:**

- Changing `WaitingCursorPattern`, any other token, or the precedence order.
- Making the WAITING footer signal position-aware (see Decisions).
- Raising the 16-line scan window or addressing the fleet-panel ceiling.
- Per-agent variation: Copilot configures no composer pattern, so it is unaffected by
  construction and stays whole-screen.

## Decisions

**Match the cursor below the composer, or anywhere when there is none.**
The two halves are one rule, expressed as a loop from `composer + 1` where `composer` is
`-1` when absent - the same idiom `IsBackgnd` already uses, so the degenerate case needs no
special-casing. The second half is what makes the change safe: all four WAITING fixtures
(`claude-waiting.txt`, `claude-waiting-slash-menu.txt`, `waiting-ask-user.txt`,
`waiting-command-approval.txt`) show a prompt box and **no composer line**, because the box
replaces the input box rather than stacking above it. A real prompt therefore finds no
split to apply and is read exactly as before. This is the evidence that substituted for the
live capture: four independently captured screens, none of which renders both.

Alternative considered: require the cursor to be within the last N lines. Rejected - it is
the same fragile bottom-anchoring the widened scan region was introduced to escape, and a
tall selection menu puts its cursor well above the footer.

Alternative considered: exclude lines that look like prose (leading `●`, sentence
punctuation). Rejected as unbounded pattern-guessing against an agent's freeform output,
where the structural signal is already available.

**Leave the WAITING footer check whole-screen.** Splitting it too would be more consistent,
and it is tempting for exactly that reason. It is not done, because the asymmetry is the
hedge against the one thing not verified: if some prompt form does render a composer above
its menu, the nav-marker-plus-cancel-text pair is the check still standing, and it covers
three of the four fixtures. The only form carrying no such footer is the bare permission
box - which is the form most clearly confirmed to replace the composer. The residual
exposure is prose containing both `↑/↓` and `esc to cancel` on one line, far less likely
than a bare `❯ 1.` and unchanged from today's behaviour.

**Exclude the composer line itself** (strictly `> composer`, not `>=`). A user typing
`why did ❯ 1. flag that pane` into the composer would otherwise flag their own pane. This
falls out of the position rule rather than being a separate guard.

**Resolve the composer index before the WAITING check.** `FindComposerLine` is pure and
already ran unconditionally for every non-WAITING, non-WORKING screen; hoisting it costs
one backwards scan of at most 16 lines on WAITING screens and lets all three
position-aware checks share one index.

## Risks / Trade-offs

**A real prompt form renders a composer above its menu, and its cursor stops matching.**
→ Mitigated by the whole-screen footer check, which catches every menu-style prompt
(three of four fixtures). Residual exposure is a hypothetical box that both stacks above
the composer *and* carries no nav/cancel footer, for which no evidence exists. A live
`--calibrate` capture of an approval prompt would retire this risk outright and is the
first thing to do if a WAITING miss is ever observed.

**Past the fleet-panel ceiling the composer falls off the top of the scan window, so
`composer == -1` and the whole screen is scanned for cursors again.** → Accepted, and
recorded in `AGENTS.md`. That pane is already degraded (it classifies Unknown today), and
the note previously arguing against widening the scan is updated rather than deleted: this
is now the specific case where widening would still hurt.

**One existing test asserted precedence using an unrealistic screen** - a permission box
stacked above a live composer, the very layout no real prompt produces. → It keeps its
assertion but gains the prompt's own footer, so it now exercises the check that would
actually save that screen instead of the one this change removes.

## Verification

Fixture and unit coverage cannot prove the composer question either way, so the change was
also confirmed against the live pane that produced the bug: with the offending `❯ 1.` text
still inside the 16-line scan window, `./go.sh --once` reports that pane as `Idle` where it
previously reported `Waiting`. Suite: 280 tests pass, up from 276.
