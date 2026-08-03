## Why

The row-number column carries two markers that mean entirely different things - `▌` for
the watcher's own highlight cursor and `►` for the multiplexer's current pane - and the
cursor is rendered in the louder style of the two (a solid yellow half-block against a
small green arrow). Users read the loudest mark as the authoritative one and conclude
that the yellow bar names the active tmux pane, which it does not.

The misreading is reinforced by how the markers behave. Both `enter` and the address
keys route through the same activation path, which drags the highlight onto the row it
jumped to, so after any in-app switch the two markers sit on the same row and agree.
They diverge when the user switches panes with tmux's own keys - a routine part of the
workflow that moves the focus marker on the next poll and leaves the cursor where it
was. So the loud yellow bar is correct about focus most of the time by coincidence, and
silently wrong exactly when the user is relying on it.

## What Changes

- The highlight cursor marker `▌` renders in grey instead of yellow, so it recedes into
  the table chrome and reads as watcher UI rather than as a claim about tmux. It stays
  legible when the user is looking for it while arrowing.
- The multiplexer focus marker `►` renders in yellow instead of green, taking the visual
  weight that belongs to the marker actually asserting something about the outside world.
- The one-shot (`--once`) and calibration (`--calibrate`) output adopt the same yellow
  focus marker, so every output mode agrees on what the colour means.

No behaviour changes: both markers keep their existing glyphs, positions, and meanings,
and neither becomes a target for any action it was not already.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `watcher-tui`: the "Highlighted row navigation" requirement gains a statement that the
  highlight cursor and the focus marker must be visually distinguishable, with the focus
  marker carrying the greater visual weight of the two.

## Impact

- `src/TmuxWatch/Tui/WatcherApp.cs` - the two marker markup strings in `BuildRowTable`.
- `src/TmuxWatch/Program.cs` - the focus marker in the two one-shot output lines.
- No tests assert on either glyph or colour, and `BuildRowTable` is private, so nothing
  in the suite depends on the current styling. Verification is visual.
- No config, no keybindings, no tmux verbs. The tmux boundary is untouched.
