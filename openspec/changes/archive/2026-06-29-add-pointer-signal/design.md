## Context

tmux-watch already classifies each watched pane and tracks, per pane, whether it has
outstanding attention (`TrackedPane.AttentionOutstanding`, surfaced on
`TrackedPaneView`). The only existing alert is `BellNotifier`, wired through the
edge-triggered `INotifier.Notify` seam fired from `AttentionMonitor.RaiseIfAttention`.
A bell is a one-shot ping; it cannot express "an agent is *still* waiting." This change
adds a persistent, ambient cue by recolouring the real OS mouse pointer for as long as
any pane is waiting.

Two deployment shapes exist and must both work:
- Native Windows: tmux-watch is a Windows process (e.g. against PSMUX). It can call
  `user32` directly.
- WSL2: tmux-watch is a Linux process. It cannot P/Invoke `user32`, but it can reach the
  same interactive Windows session by shelling out to `powershell.exe`.

tmux-watch may run inside or outside tmux/PSMUX; because the pointer change is an OS call
rather than a terminal escape sequence, tmux nesting is irrelevant.

## Goals / Non-Goals

**Goals:**
- A level-triggered pointer signal driven by the aggregate "any pane waiting" state.
- Global desktop scope; visible even when the terminal is minimised.
- Works identically inside/outside tmux and across native-Windows and WSL hosts.
- Crash-safe: never strand a red pointer across the desktop.
- Opt-in, default off; coexists with the existing bell.

**Non-Goals:**
- Severity/count encoding (amber-for-one, red-for-many) - single colour for now.
- Scoping the recolour to "only while over the terminal" - explicitly global.
- Changing the read-only-toward-panes guarantee or the tmux verb whitelist.
- macOS/Linux-desktop pointer support - the target is the Windows pointer.

## Decisions

### Recolour the real pointer via SetSystemCursor + a shipped asset

Windows has no "tint the pointer" API; a cursor is an image. The robust, Win10+Win11
path is `LoadCursorFromFile` on a shipped red `.cur`, then `SetSystemCursor(hCursor,
OCR_*)` for each shape we want to override. Restore is `SystemParametersInfo(
SPI_SETCURSORS, 0, NULL, SPIF_SENDCHANGE)`, which reloads the user's normal cursors from
the registry - a clean, scheme-agnostic revert that does not require us to remember the
prior cursors.

- Shapes: override `OCR_NORMAL` (arrow) and `OCR_IBEAM` (the shape shown over terminal
  text), since the terminal may itself be the foreground window.
- Alternative considered: Windows 11 accessibility coloured-pointer via the
  `HKCU\Software\Microsoft\Accessibility` registry keys + `WM_SETTINGCHANGE` broadcast.
  Rejected as primary: Win11-only and less documented. `SetSystemCursor` + asset works
  on Win10 and Win11 and is well specified. Could be a later enhancement.

### Level-triggered abstraction, not another INotifier

`INotifier` is edge-triggered (`Notify(title, body)`), the wrong shape for a state that
must be held and later cleared. Introduce a separate, level-triggered seam, e.g.
`IPointerSignal.SetWaiting(bool anyWaiting)`, that acts only on transitions of the
boolean it is given. The monitor loop computes the aggregate from existing state
(`panes.Any(p => p.AttentionOutstanding)`) after each `Tick` and calls `SetWaiting`. The
bell stays exactly as is; the two mechanisms are independent and can both be enabled.

- Alternative considered: extend `INotifier` with set/clear methods. Rejected - it
  conflates a fire-and-forget ping with held state and would complicate `BellNotifier`/
  `NullNotifier`.

### Two backends behind one seam, chosen by RuntimeInformation

- `WindowsPointerSignal`: direct `user32` P/Invoke. Selected when
  `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`.
- `WslPointerSignal`: builds the equivalent `SetSystemCursor`/`SPI_SETCURSORS` calls as
  an `Add-Type` + invoke script and runs `powershell.exe -NoProfile -Command ...`.
  Because the pointer change is global Windows-session state that persists after the
  invoking process exits, set and clear can be separate `powershell.exe` invocations.
- `NullPointerSignal`: when disabled or on an unsupported host - a no-op.
- A factory mirrors `NotifierFactory`, picking the backend from config + host.

### Crash-safety: restore on exit and unconditionally on startup

A stranded red pointer is desktop-global and survives the process, so two guards:
1. Graceful: restore on `ProcessExit` and on the already-wired `Console.CancelKeyPress`
   in `Program.cs`.
2. Self-heal: call restore unconditionally at startup *before* the first tick, so a
   pointer left red by a `kill -9` between set and the exit handler is cleared next
   launch regardless of current pane state. Restore is idempotent (`SPI_SETCURSORS` just
   reloads defaults), so an unconditional startup restore is safe even when nothing was
   stranded.

### Config surface, default off

A `pointerSignal` section: `enabled` (default false), `waitingCursorFile` (path to the
shipped `.cur`), and the set of `shapes` to override. Resolution lives with the other
`WatchConfig` loading. The shipped asset is packaged alongside the app and referenced by
a default relative path.

## Risks / Trade-offs

- **Stranded red pointer after a hard kill** → unconditional startup restore plus
  graceful-exit handlers; restore is idempotent so it is always safe to run.
- **Global scope surprises the user** (pointer red over every app) → this is the
  intended behaviour and the cue's whole value; gated behind an opt-in default-off flag.
- **`powershell.exe` latency on the WSL path** (process spawn per transition) → only
  fires on aggregate transitions, not every tick, so spawn cost is rare; acceptable.
- **`powershell.exe` not reachable / execution policy** on some WSL setups → backend
  failure degrades to a logged no-op; the watcher never crashes on a pointer error.
- **Cursor asset missing or unreadable** → treat as unsupported; degrade to no-op.
- **Overriding I-beam affects text selection cursor globally while waiting** → accepted;
  it reverts on restore and only the colour changes, not the shape's meaning.
- **No change to pane safety** → the tmux verb whitelist and read-only guarantee are
  untouched; this only mutates the watcher's own OS environment.
