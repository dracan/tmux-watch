## Context

`PaneClassifier` turns a captured screen into a state by matching the pane's
`AgentProfile` tokens, with precedence WAITING → WORKING → IDLE → DEAD. It currently
joins the last 6 non-blank lines (`TailNonBlank`, `_statusLineCount = 6`) into a
`statusText` and runs the matchers over that. The `claude` profile keys WORKING on
`WorkingFooterCancelMarker = "esc to interrupt"` with `WorkingMarkerSufficient = true`,
and WAITING on `❯\s*\d+\.` plus a case-sensitive `esc to cancel`.

A Claude Code build update broke all of this, verified against live panes via
`--calibrate`:
- The working line is now `<glyph> <Gerund>... (<dur> · <arrow> <n> tokens)` with no
  `esc to interrupt`; the meter sometimes has not appeared yet (`✶ Enchanting...`),
  and a sub-agent wait shows `✻ Waiting for N background agent(s) to finish` with
  neither ellipsis nor meter.
- The working line sits above the input box and a sub-agent panel can render below
  it, so it is 15-20 lines from the bottom - outside the 6-line tail.
- A finished turn freezes a same-glyph line `✻ Crunched for 54s` / `✻ Cooked for
  1m 25s` in scrollback (past-tense `<Word> for <dur>`), which must not read as
  WORKING.
- Selection menus are taller: the `❯ 1.` cursor sits ~11 lines up and the footer is
  `Enter to select · ↑/↓ to navigate · Esc to cancel` (capital `Esc to cancel`).

Constraints: read-only tmux guarantee is untouched (classification reads captures
only); "tokens are data" - new patterns live in the `claude` profile and stay
overridable; the classifier stays pure and fixture-tested.

## Goals / Non-Goals

**Goals:**
- Classify the current Claude Code build's WORKING, WAITING, and IDLE correctly.
- Distinguish the live spinner line from a frozen completed line by construction,
  not by relying on the line falling outside the scan window.
- Widen the scan so signals above the input box / below it (sub-agent panel) and
  tall menus are in range, without admitting stale scrollback as WORKING.
- Keep Copilot detection and existing fixtures green.

**Non-Goals:**
- No change to the Copilot profile or to discovery.
- No new config schema beyond the `claude` profile tokens (and any minimal field
  the live-spinner matcher needs).
- Not attempting to detect every possible future Claude UI; tokens stay verifiable
  via `--calibrate`.

## Decisions

### D1: WORKING = live qualifiers, the strongest of which are glyph-independent

A line is WORKING when it carries one of three "live" qualifiers:
1. a live meter segment `(<digits>s · ` (parenthesised duration that opens the token
   meter) - **sufficient on its own, with no leading-glyph requirement**; or
2. the background-agents phrasing `Waiting for <N> background agent` - **sufficient on
   its own**; or
3. an ellipsis (`...` or the single-char `…`) on a line that *also* begins (after
   whitespace) with an asterisk spinner glyph - the bare `<Gerund>…` moment before the
   meter appears.

**Why the meter must not be glyph-anchored (learned during calibration).** The first
cut required *every* live line to start with a glyph from a fixed asterisk set. But
the spinner animation cycles through frames that set does not (and cannot fully)
enumerate - live captures show `·` (U+00B7 middle dot) and `*` (U+002A ASCII asterisk)
as frames alongside `✻✢✶…`. On roughly a quarter of frames the leading glyph fell
outside the whitelist, the line failed the anchor, and the pane flickered to IDLE on
every poll that happened to land there. The meter `(\d+[smh] · … tokens)` is
distinctive and parenthesised (the frozen completed line and the sub-agent panel's
` 2m 18s · ↓ … tokens` are not), so it is safe to match anywhere on a line regardless
of the animation frame. Only qualifier (3), the brief meter-less gerund moment, keeps
the glyph anchor - there an ellipsis alone would also match truncated table cells or
prose.

Rationale: the action word is random ("Tinkering"/"Enchanting"/"Forging") and the
glyph is an animation frame, so neither is a stable hook on its own. The frozen
completed line shares the glyph set, so glyph-at-line-start alone is ambiguous; the
ellipsis/meter qualifier is exactly what separates the live line from the past-tense
`<Word> for <dur>` completed line. The background-agents line has neither qualifier
and needs its own clause (product decision: a sub-agent-bound pane is WORKING).

Implementation: drop reliance on `esc to interrupt`. Remove `·` from the spinner
glyph set used for the start-of-line test (it is a middle dot that appears in meters
and footers and would make "starts with a glyph" meaningless); keep the asterisk
frames (`✻✽✶✷✸✹✺✢✳∗`). Add the live qualifiers as `claude`-profile patterns so they
stay data. Normalise both `…` and `...` when testing for the ellipsis.

Alternatives considered:
- *Glyph-at-line-start alone* - rejected: matches frozen completed lines (false
  WORKING on idle panes).
- *Meter only* - rejected: `✶ Enchanting...` and the background-agents line have no
  meter yet, so working panes would be missed.
- *Keep `esc to interrupt`* - rejected: the marker is gone from the build.

### D2: WAITING cancel marker matched case-insensitively

Match the nav-marker + cancel-marker footer with `StringComparison.OrdinalIgnoreCase`
for the cancel text, so `Esc to cancel` matches the configured `esc to cancel`. The
numbered-cursor branch (`❯\s*\d+\.`) is unchanged. Keeping the configured token
lowercase and comparing case-insensitively is less brittle than encoding the exact
capitalisation.

### D3: Widen the scan region instead of only the bottom 6 lines

Increase the scanned region so the live spinner line (above the box, with a panel
below) and a tall menu's cursor are included. Two viable shapes:
- (a) raise `_statusLineCount` to a value that covers the observed layouts
  (sub-agent case is ~20 lines from the bottom), or
- (b) scan the whole capture for the line-oriented matchers.

Chosen: scan a generously widened tail (large enough for the sub-agent layout) rather
than the entire scrollback, keeping the blast radius small while covering the
observed cases. Safety against stale scrollback comes from D1's live-only qualifiers
(a finished turn's line is past-tense and fails the WORKING test) and from precedence
(WAITING is tested first; a live selection menu outranks residual working text). The
exact width is a tuning constant validated by the fixtures; the matchers, not the
width, are what prevent false positives.

Alternatives considered:
- *Whole-capture scan for everything* - more exposure to scrollback artefacts (old
  `❯ 1.` menus, old working lines) for marginal benefit; the widened tail covers the
  real layouts. Revisit if a real capture exceeds it.

## Risks / Trade-offs

- [A future "live" line has neither ellipsis, meter, nor the background-agents
  phrasing] → It would read as IDLE. Mitigation: tokens are overridable and
  `--calibrate` is the documented way to catch this after an upgrade; the README
  note is updated to the current verified tokens and flagged as build-specific.
- [Widening the scan surfaces an old selection footer or working line from
  scrollback] → False WAITING/WORKING. Mitigation: WORKING uses live-only qualifiers
  that a completed line fails; a stale, already-answered menu is normally scrolled
  away. The `claude-idle-after-work.txt` fixture is the explicit negative control.
- [The `·` middle dot was part of the configured spinner set] → Removing it from the
  start-of-line glyph test changes Copilot behaviour only if shared; the spinner set
  is per-profile, so Copilot is unaffected. Verify Copilot fixtures stay green.
- [Ellipsis appears as `…` vs `...`] → Normalise/accept both in the qualifier.

## Migration Plan

Pure internal change; no data migration. Ship the profile-token and classifier
updates together with the new fixtures and tests, update the README/AGENTS note from
"provisional" to the verified current-build tokens, and confirm `--calibrate`
against the live panes reports WORKING/WAITING correctly. Rollback is reverting the
commit; tokens being data means a user can also override back via config.

## Open Questions

- Final value for the widened scan width (tuning constant) - set from the fixtures so
  the sub-agent layout is covered with margin; revisit only if a real capture is
  taller.
