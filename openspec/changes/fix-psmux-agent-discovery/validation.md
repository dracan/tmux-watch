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

## Follow-up: running executable renamed

The client later supplied valid process trees beneath both affected pane roots, but .NET reported an `.old-...` filename for the live Copilot processes while the process listing reported `copilot.exe`. The initial reader rejected this name mismatch, discarding the agent and its descendants despite unchanged process identity.

A native Windows harness reproduced the failure with one disposable copy of `cmd.exe` named `copilot.exe`, waiting on redirected stdin. Renaming it while running changed a fresh .NET `ProcessName` to `copilot.exe.old-123-456` while preserving its PID and start time. Before the fix, the real reader returned no owner; after the fix it returned Copilot. No installed agent executable was modified.

The regression was added before the fix and run with:

```sh
dotnet.exe vstest '\\wsl.localhost\Ubuntu\home\dan\code\tmux-watch\tests\TmuxWatch.Tests\bin\Debug\net10.0\TmuxWatch.Tests.dll' '/TestCaseFilter:FullyQualifiedName~Native_discovery_keeps_the_owner_when_its_running_executable_is_renamed'
```

It failed at the post-rename agent assertion (`Assert.Single() Failure: The collection was empty`). Removing only the current-name comparison made it pass. The test covers `apphost` and `tgrep` reported commands, continued ownership after rename, ownership removal after exit, and no pane captures. Start-time and liveness checks remain in production.

- Full Linux suite: 472 passed, 3 Windows-only tests skipped.
- Native Windows focused suite using the same three-class filter above: 36 passed, no skips.
- Original native rename harness rerun: exit 0, `Expected Copilot after rename; actual: copilot`.
- Build: zero warnings and errors.
- Strict OpenSpec validation, `git diff --check`, and ASCII checks of added lines: passed.

This verifies the reported filename mismatch on the local native Windows runtime. Confirmation on the affected client laptop remains external validation. The missing regression was a live executable rename through the real Windows adapter; the existing integration seam supports it without architectural changes.
