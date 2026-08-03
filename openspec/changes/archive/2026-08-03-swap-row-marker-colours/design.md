## Context

The `#` column of every row table carries up to two markers, built in
`BuildRowTable` (`src/TmuxWatch/Tui/WatcherApp.cs`):

```
   ┌───────┬────────────┐
   │ 1     │ ◐ working  │   neither
   │ ►2    │ ○ idle     │   [green]►[/]   multiplexer's current pane
   │ ▌3    │ ● WAITING  │   [yellow]▌[/]  the watcher's highlight cursor
   │ ▌►4   │ ✓ DONE     │   both
   └───────┴────────────┘
```

They share a cell and sit one character apart, so they read as two flavours of one
fact. They are not: `►` is a live re-read of the multiplexer's own flags
(`Pane.IsFocused => WindowActive && PaneActive`), refreshed every poll and able to move
with no keystroke the watcher sees; `▌` is the watcher's own cursor and moves only on
`up`/`down` or on activation.

The two agree more often than their independence suggests. Both `enter`
(`ActivateHighlighted`) and the address keys (`AddressRow`) funnel into `Activate`,
which ends by setting `_highlightedId = row.Id` - so every in-app switch drags the
cursor onto the row that is about to take focus. The markers separate when the user
switches panes with the multiplexer's own keys, which is a routine part of the workflow.
The result is that the louder marker is right about focus most of the time by
coincidence, and wrong precisely when the user needs it.

The relevant deployment is the watcher running **outside** the multiplexer, in its own
terminal split. There, focusing the watcher is not a multiplexer focus change, so `►`
stays put on the last-selected pane and is always on screen. (Were the watcher a pane
inside the multiplexer, it would exclude itself from the inventory - see
`PaneDiscovery`'s self-pane handling - and `►` would vanish from every row whenever the
user was driving it. That regime is not what this change is tuned for, and the change
does not make it worse.)

## Goals / Non-Goals

**Goals:**

- Make the focus marker the more prominent of the two, since it is the one asserting
  something about the world outside the watcher.
- Keep the highlight cursor findable while arrowing, without it reading as a claim about
  the multiplexer.
- Keep every output mode agreeing on what the focus marker looks like.

**Non-Goals:**

- Changing either glyph, its position, or its meaning.
- Changing which marker any action targets - the row actions already act on the
  highlight and continue to.
- Solving the ragged `#` column (rows render as `1`, `►2`, `▌►4`, so the digits sit at
  different offsets). Real, but a separate concern from the colour confusion.
- Re-encoding the cursor onto a different channel, such as a full-row background band.
  Considered and rejected below.

## Decisions

**Focus marker `►` becomes yellow; highlight marker `▌` becomes grey.** The colour
swap alone inverts the salience, because yellow-on-a-solid-block was doing the work and
the block stays where it is. No glyph changes, so the diff is four markup strings.

*Alternative considered - move the cursor to a different channel entirely* (row-wide
background tint, so the cursor is a *region* and focus is a *mark*, making the confusion
structurally impossible). Rejected as disproportionate: Spectre styles cells rather than
rows, so every cell would need wrapping in `[on …]`, and the `Rounded` border's column
separators would stripe the band rather than let it read as one bar. Not foreclosed if
the colour swap proves insufficient.

**Grey is chosen deliberately despite matching the table border.** The table's
`BorderStyle` is already `Color.Grey`, so the cursor becomes the same colour as the `│`
separators near it. Brighter neutrals (`silver`, `grey70`) were considered to avoid
that. Grey was chosen anyway: the two never overlap in position, and a cursor that
matches the chrome is exactly right for a marker that only needs to be found while the
user is deliberately arrowing. Recessive is the goal, not merely quieter.

**Yellow is accepted for the focus marker despite already meaning WAITING.** Every
colour the marker column could borrow is already allocated in the State column - yellow
is WAITING and the `n` prompt, green is idle and DONE, blue is working, cyan is backgnd,
red is dead, grey is unknown and the borders. There is no unallocated colour with enough
weight, so a swap moves the collision rather than removing it. Yellow is the better
place for it: a focus marker genuinely *is* an attention signal, so sharing the
attention colour is defensible in a way the cursor sharing it was not. Green was no
cleaner - it collides with idle and DONE today.

**The one-shot and calibration paths change with the live view.** `Program.cs` renders
its own focus marker in two places and would otherwise keep the old green, leaving the
same glyph meaning different things across output modes.

## Risks / Trade-offs

- **Grey cursor is harder to spot on a busy screen** → Accepted deliberately; the cursor
  is only needed while arrowing, when the user is already looking for it. If it proves
  too quiet in use, `silver` or `grey70` is a one-word follow-up.
- **Yellow focus marker sits next to a yellow `● WAITING` cell on waiting rows, and may
  read as a mild alarm on a `working` row** → Accepted; the marker is a small glyph in a
  different column, and no other colour is both unallocated and heavy enough.
- **No automated coverage** → No test asserts on either glyph or colour, and
  `BuildRowTable` is private, so nothing in the suite guards this. Verification is
  visual, via `./go.sh` with at least one focused row and the cursor moved off it.
