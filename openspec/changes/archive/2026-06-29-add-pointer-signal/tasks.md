## 1. Config and assets

- [x] 1.1 Add a `PointerSignalConfig` (enabled default false, waitingCursorFile path, shapes) and a `PointerSignal` property on `WatchConfig`, with defaults in `WatchConfig.Load`
- [x] 1.2 Add a red arrow/I-beam cursor asset (`.cur`) to the project, packaged with the app, and resolve its default relative path at runtime
- [x] 1.3 Add tests for config parsing (enabled/disabled, default off, custom asset path/shapes)

## 2. Pointer signal seam

- [x] 2.1 Define `IPointerSignal` with level-triggered `SetWaiting(bool anyWaiting)` plus an idempotent `Restore()`, acting only on transitions of the boolean
- [x] 2.2 Implement `NullPointerSignal` (no-op) for disabled/unsupported hosts
- [x] 2.3 Add a `PointerSignalFactory` that selects the backend from config + host OS, mirroring `NotifierFactory`

## 3. Backends

- [x] 3.1 Implement `WindowsPointerSignal` via `user32` P/Invoke: `LoadCursorFromFile`, `SetSystemCursor` over `OCR_NORMAL` and `OCR_IBEAM` to set; `SystemParametersInfo(SPI_SETCURSORS, SPIF_SENDCHANGE)` to restore
- [x] 3.2 Implement `WslPointerSignal` that runs the equivalent set/restore via `powershell.exe -NoProfile -Command` (Add-Type wrapping the same user32 calls); set and restore are separate invocations
- [x] 3.3 Make every backend failure (missing asset, powershell.exe unavailable, exec policy) degrade to a logged no-op rather than throw

## 4. Monitor wiring

- [x] 4.1 Compute aggregate `anyWaiting = panes.Any(p => p.AttentionOutstanding)` after each `Tick` and drive `IPointerSignal.SetWaiting(...)` from it, leaving the existing bell path unchanged
- [x] 4.2 Construct the pointer signal in `Program.cs` from config/host and pass it into the monitor loop
- [x] 4.3 Add tests (with a fake `IPointerSignal`) asserting set on first-waiting, no re-apply while already waiting, restore when last waiting pane clears, and held while any pane still waits

## 5. Crash-safety

- [x] 5.1 Call `Restore()` unconditionally at startup, before the first tick
- [x] 5.2 Restore on graceful shutdown: hook `AppDomain.CurrentDomain.ProcessExit` and the existing `Console.CancelKeyPress` in `Program.cs`
- [x] 5.3 Add a test asserting startup performs an unconditional restore independent of pane state

## 6. Docs and parity

- [x] 6.1 Document the `pointerSignal` config (opt-in, global scope, red, crash-safe restore) in the README
- [x] 6.2 Confirm the read-only guarantee and tmux verb whitelist are untouched, and note the new "watcher mutates its own OS environment" boundary in AGENTS.md if warranted
- [x] 6.3 Run `dotnet build` and `dotnet test`; validate the change with `openspec validate add-pointer-signal --strict`
