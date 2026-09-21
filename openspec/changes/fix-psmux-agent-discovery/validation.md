# Validation

## Reproduction before the fix

`dotnet test tests/TmuxWatch.Tests/TmuxWatch.Tests.csproj --filter FullyQualifiedName~WindowsDiscoveryTests --no-restore -m:1 -p:UseSharedCompilation=false`

Both initial regression tests failed: helper command `tgrep` produced no agent pane, and the unsupported timestamp produced `02:36:10` instead of unknown. The metadata replay uses scrubbed pane fields and a synthetic shell -> copilot.exe -> tgrep.exe process tree. It is not a capture of the client's actual process tree.

## After the fix

- Full Linux suite: `dotnet test --no-restore -m:1 -p:UseSharedCompilation=false`. 472 passed, 2 Windows-only tests skipped.
- Native Windows, .NET SDK 10.0.112, using `dotnet.exe vstest` on the portable test assembly with filter `FullyQualifiedName~WindowsProcessSnapshotTests|FullyQualifiedName~WindowsDiscoveryTests|FullyQualifiedName~ProcessSnapshotTests`: 35 passed, no skips. Includes the real Toolhelp32 adapter, current process identity, a disposable child process discovered through the production constructor, and removal of its ownership after exit.
- `dotnet build --no-restore -m:1 -p:UseSharedCompilation=false`: succeeded, zero warnings and errors.
- `git diff --check` and ASCII checks of generated text: passed.
- `openspec validate fix-psmux-agent-discovery --strict`: valid.

The fix adds no agent tokens or tmux verbs, does not capture inventory panes to identify them, and leaves stdout buffering unchanged. Native testing was on the local Windows host through WSL interop, not the affected client laptop. The actual client symptom still needs confirmation after updating its watcher; no Copilot or psmux configuration change is required.
