## Context

The watcher renders agent panes in a main table plus an optional Paused table, both
built from `MonitorSnapshot.Panes`. Discovery already enumerates *every* pane in the
tmux server with one `lsp -a -F` call and then discards the panes that match no agent
profile (`PaneDiscovery.DiscoverAgentPanes`). The data needed for this change is
therefore already being fetched and thrown away.

Two existing behaviours constrain the design:

- **The read-only guarantee.** `TmuxRunner` whitelists verbs and forbids anything that
  injects input. `AGENTS.md` currently states the only state change permitted is moving
  the *watcher's own client focus* (`switch-client`, `select-window`).
- **Targeting by client focus.** `p` and `a` act on `all.FirstOrDefault(v => v.Pane.IsFocused)`.
  `IsFocused` is `WindowActive && PaneActive`, both derived from a cross-session `lsp -a`,
  so it means "active pane of its own session's active window". With multiple sessions
  several rows can satisfy it at once and `FirstOrDefault` picks an arbitrary one.

The watcher normally runs outside tmux in a full-height terminal docked to the left,
which is why a busier default view is acceptable but a wide one is not.

## Goals / Non-Goals

**Goals:**

- Show every non-agent pane as a row, in its own table, toggleable at runtime.
- Make every row reachable by keyboard, including rows past the ninth.
- Keep the attention pipeline (capture, classify, state machine, notify, pointer)
  strictly agent-only.
- Keep the table narrow enough for a thin docked terminal.
- Land on the exact pane a row names, not merely its window.

**Non-Goals:**

- Scrolling or a viewport when the row list exceeds the terminal height. Accepted as a
  known gap; the watcher runs full-height and the case may not arise in practice.
- Classifying, capturing, or deriving any state for non-agent panes.
- Persisting toggle states across launches.
- Filtering non-agent panes by process (e.g. hiding plain shells).

## Decisions

### Inventory rides along with the existing enumeration

`PaneDiscovery` returns both the matched and unmatched panes from the single `lsp -a`
call it already makes, and `AttentionMonitor.Tick` passes the unmatched set straight
through onto `MonitorSnapshot` without touching it.

*Alternative considered:* have `WatcherApp` call discovery separately for the inventory.
Cleaner separation of concerns, but it costs a second `lsp -a` per poll for data already
in hand. Rejected on cost.

The pass-through field is documented as inert: no capture, no classifier, no
`TrackedPane` entry, no `AttentionEvent`, no contribution to `AggregatePointerState`.
Because non-agent panes never become `TrackedPane`s, they cannot influence the DONE
derivation or the notification path by construction rather than by convention.

### Companion panes are a filter, not a separate list

A **companion pane** is a non-agent pane whose `(SessionName, WindowIndex)` matches that
of at least one matched agent pane. The `c` toggle filters them out of the Other panes
table; it has no effect while `o` is off. Both default to on, so the out-of-the-box view
is every pane tmux knows about.

*Alternative considered:* a third table for companions. Rejected - it triples the table
count for a distinction that is a filter, not a different kind of thing.

### Row order: tmux order for the inventory

The agent table keeps its attention-priority sort. The Other panes table sorts by
session name, then window index, then pane index - tmux's own order. This matters more
than aesthetics: the highlighted row and the shift+letter addresses must not shuffle
between polls, and non-agent panes have no state to sort by anyway.

### The State column does double duty

Adding a Process column to every table would widen it past what a thin dock tolerates.
Instead the existing State column answers "what is this row": the classified state
(`WAITING`, `DONE`, ...) for agent rows, and the foreground process (`k9s`, `lazygit`,
`nvim`) for non-agent rows, styled distinctly so the two readings are not confused.

This keeps all three tables at an identical column set - `#`, `State`, `Window`,
`In state`, plus `Path`/`Loc` in wide mode - which is what lets the Paused table hold
both kinds of row without splitting into two tables.

*Alternative considered:* a union column set (`State` + `Process`). Rejected on width.

### Activity time is window-granular, and labelled as such

tmux 3.4 exposes `#{window_activity}` (unix seconds) but no pane-level equivalent -
`#{pane_last_active}` renders empty. Non-agent rows therefore show time since their
*window's* last activity in the `In state` column, which means all panes in a split share
one figure. This is honest enough for the intended use ("that shell has been untouched
for hours") and is the only signal available; the column header stays `In state` since it
is answering the same question for both row kinds.

### The highlighted row is anchored to a pane id

The watcher tracks the highlighted pane by id, not by index. The agent table re-sorts
whenever a pane changes state, so an index-anchored highlight would slide onto a
different pane underneath the user. On each render the highlight resolves to that pane's
current row; if the pane is gone or newly hidden by a toggle, it falls back to the
nearest surviving visible row (previous index, clamped).

The highlight spans all visible tables as one continuous list, so up/down walks from the
agent table into the Other panes table and on into Paused, matching the existing
continuous row numbering.

`>` (the focus marker) is unchanged and remains a passive indicator of tmux's own
current pane. It is no longer a target for any action.

### `p` and `a` retarget to the highlighted row

Actions now have an explicit, user-chosen target instead of a derived and (across
sessions) ambiguous one. `a` remains a no-op unless the highlighted row is a DONE agent
pane. `p` gains meaning for non-agent rows: it moves the row into the Paused table, which
is purely a decluttering move since a non-agent pane has no cue to mute. `AggregatePointerState`
is unaffected either way, as non-agent panes never contribute to it.

### Addressing: digits, then shift+letter

Visible rows are numbered continuously in render order (agent, other, paused), exactly as
today. Rows 1-9 keep their digit keys. Row 10 onward is addressed by `A`, `B`, `C`, ...
Uppercase is thereby reserved for addressing, so all command keys (`o`, `c`, `p`, `a`,
`w`, `q`) stay lowercase; `Console.ReadKey` distinguishes the cases via `KeyChar`.

*Alternative considered:* pagination of the digit keyspace. Rejected as more machinery
and more modal state than a second flat keyspace.

### `select-pane` is added to the whitelist

Per-pane rows demand per-pane landing: without it, highlighting the `lazygit` pane of a
three-way split lands on whichever pane that window last had active. `SwitchTo` becomes
`switch-client` -> `select-window` -> `select-pane`.

This widens the guarantee. `select-pane` injects no input, but it changes the *window's*
active pane, which is server state other clients can observe - broader than "the
watcher's own client focus". `AGENTS.md` and the `watcher-tui` spec are updated to say
so explicitly rather than letting the implementation quietly outgrow its stated contract.
The prohibition that actually matters - never send input to a watched pane - is untouched,
and `send-keys` remains absent from the whitelist.

### Toggles are runtime-only

`o` and `c` follow the existing `w` (wide) toggle: in-memory, defaulting to on, reset on
every launch, with no config key. Consistency with the one precedent already in the TUI,
and no config surface to maintain.

### The watcher's own pane

The watcher is not normally run inside tmux, but when it is, `TMUX_PANE` names its own
pane id and that row is dropped from the inventory. Cheap, and avoids a confusing
self-referential row.

## Risks / Trade-offs

- **The view can overflow the terminal.** Spectre's `Live` renders whole tables with no
  scrolling, and the default view is now every pane. -> Accepted and deferred by
  decision; the watcher runs full-height. If it bites, a viewport that follows the
  highlight is the fix.
- **`select-pane` widens the stated guarantee.** -> Contained by whitelist (still no
  input-injecting verb), by updating `AGENTS.md` and the spec so the contract matches
  reality, and by a test asserting `send-keys` is still rejected.
- **`p`/`a` changing target is a behaviour change for existing muscle memory.** -> The
  old target was ambiguous across sessions; the new one is visible on screen before the
  key is pressed.
- **A busier default view dilutes the attention signal.** -> Agent rows keep the top of
  the list and the priority sort; non-agent rows sit in a separate, visually distinct
  table below, and `o` hides them in one keystroke.
- **Window-granular activity time can mislead** on a split window where one pane is busy
  and another idle. -> Documented; no pane-level source exists in tmux 3.4.
- **Uppercase addressing forecloses uppercase command keys.** -> Accepted; lowercase
  space is far from exhausted.
