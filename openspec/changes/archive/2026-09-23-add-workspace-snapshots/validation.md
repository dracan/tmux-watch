# Validation

## Automated checks

- Build: `dotnet build --no-restore -m:1 -p:UseSharedCompilation=false` on
  .NET SDK 10.0.401. Zero warnings and errors.
- Full Linux suite with the isolated native tmux round trip enabled:
  `TMUX_WATCH_WORKSPACE_INTEGRATION=1 dotnet test --no-build --no-restore`.
  497 passed, three Windows-only tests skipped, 500 total. Native tmux 3.4.
- Native Windows .NET SDK 10.0.112, psmux reporting `tmux 3.3.6`:
  `dotnet.exe vstest` on the portable test assembly, with
  `TMUX_WATCH_WORKSPACE_INTEGRATION=1`, `TMUX_WATCH_TEST_EXECUTABLE` pointing to
  psmux, and filter
  `FullyQualifiedName~Workspace|FullyQualifiedName~WindowsDiscovery|FullyQualifiedName~WindowsProcessSnapshot|FullyQualifiedName~KeyRouting`.
  92 passed, no skips.
- Final focused workspace run after adding export-time restore-readiness warnings:
  26 passed, including the isolated tmux round trip and the new warning test.
- Strict OpenSpec validation and `git diff --check`: passed.
- Generated-text typography check: no em dashes, curly quotes, or typographic
  ellipses in added lines or new files.
- CLI help smoke check lists export, file/stdin import, and clipboard import.

## Round-trip evidence

Integration tests create uniquely named, isolated multiplexer sessions with no
agent commands or pane input. They export two sessions containing five shells,
a nested split layout, paths with spaces and ampersands, a window name containing
a pipe, and one paused pane. They restore under fresh test names and verify names,
directories, pause remapping, pane placement, and split structure. Native tmux
also preserves nondefault window indexes. A repeated import fails preflight and
creates no additional panes.

Other tests cover corrupted/mismatched layouts, invalid schema and fields,
missing directories, linked windows, unsafe format/command syntax, persistence
and process-identity reuse, duplicate raw IDs in different psmux sessions, modal
key routing, clipboard failure after durable save, and retained partial imports.
Input-injecting and destructive tmux verbs remain rejected.

## Limits

- No verified automatic conversation-ID adapter ships in this change. Export
  explicitly records unknown for detected agents. There are no hooks, agent
  configuration changes, or transcript reads. Known UUIDs in edited snapshots
  produce manual guidance only.
- Linked and zoomed window imports fail preflight. Psmux requires contiguous
  zero-based window indexes; geometry may scale and round on either backend.
- The Windows checks ran on the local Windows host through WSL interop, not on
  the user's client laptop. Its installed psmux build still needs real-use
  confirmation.
- Clipboard failure handling is tested with an injected adapter. The tests do
  not overwrite or inspect the user's actual desktop clipboard.
- Tests use synthetic metadata and disposable shells. No real pane captures or
  user snapshot files are committed.
