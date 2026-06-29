## Why

The existing alert (a one-shot terminal bell) is edge-triggered: it pings once when a
pane enters WAITING and then leaves no trace. When the user is heads-down in another
app - or has the terminal minimised - there is no *persistent* reminder that an agent
is still blocked on them, so waiting panes get forgotten. A recoloured mouse pointer is
an ambient, always-visible cue that lasts as long as the attention is outstanding.

## What Changes

- Add an opt-in **pointer signal**: while *any* watched pane is WAITING, the real
  Windows mouse pointer turns red across the whole desktop; when nothing is waiting,
  the normal pointer is restored.
- The signal is **level-triggered** off the monitor's aggregate state (any pane with
  outstanding attention), distinct from the existing edge-triggered bell - both can run.
- Single colour (red), global desktop scope. The pointer change is **out-of-band** (an
  OS call, not a terminal escape sequence), so it works identically whether tmux-watch
  runs inside or outside tmux/PSMUX.
- Two runtime backends selected by host OS: native Windows P/Invoke of `user32`
  (`SetSystemCursor` to set, `SystemParametersInfo(SPI_SETCURSORS)` to restore), and
  WSL/Linux by shelling out to `powershell.exe` which performs the same calls in the
  Windows session.
- Swap the arrow and I-beam pointer shapes using a shipped red `.cur` asset.
- **Crash-safety**: restore the normal pointer on graceful exit AND unconditionally on
  startup, so a pointer left red by a prior hard crash self-heals on next launch.
- Default **off**; enabled and tuned via config. This does not affect the read-only
  guarantee toward watched panes - it only mutates the watcher's own OS environment.

## Capabilities

### New Capabilities

- `pointer-signal`: A level-triggered, OS-level pointer recolour that mirrors the
  aggregate "any pane waiting" state, with cross-host backends, configurable opt-in,
  and crash-safe restore.

### Modified Capabilities

<!-- None. The attention-monitor already exposes per-pane outstanding-attention state;
     this change consumes that state without altering its requirements. -->

## Impact

- **New code**: a `pointer-signal` abstraction with Windows (P/Invoke) and WSL
  (`powershell.exe`) backends and a null/disabled backend; wiring from the monitor loop
  to drive it from aggregate state; startup and shutdown restore hooks.
- **Config**: new opt-in `pointerSignal` section (enabled flag, asset path, shapes),
  default off.
- **Assets**: a shipped red cursor file (`.cur`) packaged with the app.
- **Platform**: behaviour is Windows-session-specific; on unsupported hosts the signal
  degrades to a no-op. No change to the tmux verb whitelist or pane read-only guarantee.
