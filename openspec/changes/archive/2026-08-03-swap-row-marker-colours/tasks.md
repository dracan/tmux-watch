## 1. Live view markers

- [x] 1.1 In `BuildRowTable` (`src/TmuxWatch/Tui/WatcherApp.cs`), change the focus marker from `[green]►[/]` to `[yellow]►[/]`
- [x] 1.2 In the same method, change the highlight marker from `[yellow]▌[/]` to `[grey]▌[/]`
- [x] 1.3 Update the comment above the two markers so it explains the weighting rule (focus is the louder marker because it reports the multiplexer's state, which moves without a watcher keystroke) rather than only that the two are kept distinct

## 2. One-shot and calibration output

- [x] 2.1 In `src/TmuxWatch/Program.cs`, change the focus marker from `[green]►[/]` to `[yellow]►[/]` in the agent-pane output line
- [x] 2.2 In `src/TmuxWatch/Program.cs`, change the focus marker from `[green]►[/]` to `[yellow]►[/]` in the non-agent-pane output line
- [x] 2.3 Confirm by search that no other call site renders either marker glyph

## 3. Verify

- [x] 3.1 Run `dotnet build` and confirm it succeeds
- [x] 3.2 Run `dotnet test` and confirm the suite still passes
- [x] 3.3 Run `./go.sh --once` and confirm the focus marker renders yellow
- [x] 3.4 Run `./go.sh`, arrow the cursor off the focused row, and confirm the yellow `►` reads as the more prominent marker while the grey `▌` stays legible against the table border
