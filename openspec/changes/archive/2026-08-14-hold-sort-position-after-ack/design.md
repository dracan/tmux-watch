## Context

See proposal.md - Why.

The mechanics that matter for the approach:

- `WatcherApp.OrderAll` is a pure static over `(panes, pausedIds)`, sorting by
  `paused`, then `Priority(State)`, then `EnteredAt` descending. It is unit-tested
  directly (`PauseTests`), with no console and no clock.
- `AttentionMonitor.Acknowledge` mutates the tracked pane: `Done -> Idle`,
  `AttentionOutstanding = false`, **and `EnteredAt = now`**. Both the rank and the
  tiebreak therefore change on an ack.
- `Activate` acknowledges, then calls `Rebuild` + `Render` inline, so the re-sort lands in
  the same frame as the keystroke.
- `Rebuild` is called from two places: once per poll in `Run`, and from several key
  handlers. Any release rule evaluated inside `Rebuild` would therefore fire on
  keystrokes as well as polls.
- `WatcherApp` has no clock abstraction; `AttentionMonitor` has its own.

## Goals / Non-Goals

**Goals:**

- Keep `OrderAll` pure and directly unit-testable, including the hold.
- Make "released only by a poll" a structural property of where the deadline is checked,
  not a condition someone can accidentally satisfy from a key handler.
- No new tmux calls; no change to the read-only boundary.

**Non-Goals:**

- Animating or transitioning the row when it does move.
- Holding position for any reason other than acknowledgement (arrivals, departures, and
  ordinary classification changes still reorder freely).
- Making the hold reason visible in the UI (no countdown, no marker on the row).

## Decisions

### The hold lives in the TUI, not the monitor

A hold is a view concern: it changes where a row is drawn, not what the pane is. The
monitor stays the authority on state, and `Acknowledge` is untouched, so notifications,
the pointer cue, and the state machine keep their current semantics for free. This also
matches how `_paused` is already held - view-only set in `WatcherApp`, deliberately not in
the monitor.

*Alternative considered:* recording pre-ack values on `TrackedPane`. Rejected - it puts
presentation state into the state machine and would make `AttentionMonitor` tests care
about ordering.

### Pin the whole sort key, not just the rank

The hold records `(Priority, EnteredAt, ReleaseAt)` captured **before** `Acknowledge` runs.
Pinning the rank alone would leave the row free to shuffle among its DONE peers, because
`Acknowledge` resets `EnteredAt` to now and the tiebreak is `EnteredAt` descending - the
row would jump to the top of its own rank instead of down the table. Same visible defect,
smaller amplitude.

`OrderAll` gains a third parameter (the hold map) and substitutes the pinned values for
any pane it holds. It stays pure and stays a total order.

*Alternative considered:* pinning the screen index. Rejected during exploration - a list
where one row ignores the sort is no longer consistently ordered, and two pinned rows can
contend for a slot.

### The deadline is checked in the poll loop, not in `Rebuild`

A separate `ExpireHolds(holds, now)` runs once per tick in `Run`, immediately before
`Rebuild`. `Rebuild` and `OrderAll` only ever *apply* whatever holds currently exist, so a
keystroke-driven rebuild structurally cannot release one. This is what makes "released
only on a poll at or after the deadline" hold by construction rather than by discipline.

Because `ExpireHolds` is a static taking `now`, the timing rules are testable without a
clock in `WatcherApp` and without a `TimeProvider` dependency.

`ExpireHolds` also drops holds for panes no longer present, so the map cannot accumulate
entries for closed panes.

### Capture on the acknowledgement, from both entry points

Both `Activate` and the `a` handler read the row's pre-ack `State` and `EnteredAt` from
the current view, call `Acknowledge`, and record a hold only if `Acknowledge` returned
true (so only genuine DONE -> IDLE transitions ever pin) and `AckHoldSeconds > 0`. A
second acknowledgement of the same pane - possible if it re-enters DONE while a hold is
still live - simply overwrites the hold with a fresh capture, which is correct.

### `TogglePause` removes the hold

Pause is the one keystroke whose intent *is* the movement, so it deletes the pane's hold
before the rebuild. This is the only key-driven release, and it is an explicit exception
rather than a hole in the previous rule: it releases the hold rather than checking its
deadline.

### `AckHoldSeconds` is a double, defaulting to 5

Matches the other timing keys (`pollIntervalSeconds`, `backgroundGraceSeconds`). `0`
disables and reproduces today's behaviour exactly, which is both the escape hatch and a
convenient way to keep any existing ordering test honest.

## Risks / Trade-offs

- **Sorted position and displayed state disagree for up to 5s** - a row can read IDLE
  while sitting among the DONE rows. → Accepted deliberately: this is the point of the
  change, the window is short and bounded, and the alternative (moving) is the reported
  defect. The badge is always truthful, so nothing on screen is wrong, only stale in its
  position.
- **A pane that goes WAITING during a hold sits one slot low** - it sorts as DONE rather
  than rising above other WAITING rows. → Bounded by the same 5s, and DONE is directly
  below WAITING so it is at most a small displacement. The chime and pointer cue are
  state-driven and fire immediately, so the urgent signal is not delayed, only its
  position.
- **Several holds can expire on the same tick**, moving multiple rows at once. → Only
  reachable by acknowledging several panes within one hold window; the movement is still
  poll-aligned and away from any keystroke.
- **A long `ackHoldSeconds` would make the table feel stale.** → Not clamped, but
  documented as a short settle time; the default and the `0` escape hatch cover the
  sensible range.
