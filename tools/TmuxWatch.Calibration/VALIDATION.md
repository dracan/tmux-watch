# Validation record

Development validation on 2026-09-07 used .NET SDK 10.0.201, tmux 3.4,
Claude Code 2.1.263 (hook-reported model `claude-opus-5[1m]`), and Codex CLI
0.153.4 (hook-reported model `gpt-6-astra`).

Final verification on 2026-09-19: `dotnet build --nologo` passed with no warnings
or errors. The full suite passed
428 tests, including the real private-tmux terminal regression test and the
repo-owned hook/gate helper tests. Monitor replay passed all 13 grouped checks.
Physical speaker and desktop pointer testing was not enabled.

## Live observations

The full catalog ran against both installed agents at 120 and 70 columns. Focused
runs then verified adapter corrections. Raw reports remain private under
`.calibration-runs/`; none of their transcripts are committed.

| Area | Established coverage |
| --- | --- |
| Claude core | IDLE, WORKING, DEAD at both widths |
| Claude approvals | Native command and edit approval, return to IDLE, and live acknowledgement at both widths |
| Claude questions | Choice prompt at both widths; actual free-text editor and multiple-question submission at 120 columns |
| Claude background | Native shell and monitor, completion, and live acknowledgement at 70 columns; shell also at 120 |
| Codex core | IDLE, WORKING, DEAD at both widths |
| Codex approvals | Native command and edit approvals and completion at 120 columns |
| Codex blocked subagent | WORKING established by pending native wait call plus child helper gate at 70 columns |
| Codex background | Shell, agent, and combined background states reached at 70 columns and correctly reported Unsupported by the harness because the profile has no BACKGND fingerprints |

Each passing live target used 12 independently established samples with no
classification mismatches. Successful completion runs also recorded the
production monitor's acknowledgement behavior. Earlier adapter-development runs
are retained as diagnostics, not represented as clean validation runs.

## Defect fixed

The initial Codex working run reproduced 12/12 IDLE misclassifications while a
foreground helper was active. Its live timed status line appended a background
terminal summary. The profile now accepts that structured suffix while still
requiring the live status bullet, elapsed time, and interrupt qualifier. A
scrubbed fixture and negative counter-only tests cover the change. Subsequent
live working runs passed at both widths.

## Evidence limitations and adapter corrections

- Terminal input requires `timeout --foreground`. Claude's variadic
  `--allowedTools` also requires a `--` separator before the initial prompt.
  The real terminal regression test covers launch/input behavior.
- Codex truncates its narrow workspace trust path. The driver checks the owned
  pane's real working directory through tmux before accepting a known dialog.
- Codex PermissionRequest can accompany automatic review. Sessions now explicitly
  select the user reviewer. Codex does not accept a PreToolUse `ask` decision;
  its approval scenarios use native policy rules/read-only sandbox instead.
- A question about free text may still render a choice menu. Shape metadata and
  explicit native editor navigation now distinguish those variants.
- Claude's observed Agent tool did not supply evidence of a detached handoff in
  some requested background scenarios. A background shell cannot establish that
  an accompanying subagent is detached; those attempts remain Inconclusive. One
  earlier mixed-background mismatch was an oracle defect, corrected with a
  regression test. Completion evidence also matches the specific child identity.
- Codex native question screens were reached, but their tool-entry hooks alone
  do not establish the visible modal state. These require the supported operator
  confirmation path and remain Inconclusive in unattended validation.
- Some model runs declined a native monitor or encountered a connection error.
  Those results stay Inconclusive; they do not justify new classifier tokens.

## Copilot validation on 2026-09-19

The user installed and authenticated GitHub Copilot CLI 1.0.86. Its live UI
reported Claude Sonnet 5; its hooks did not report a model identifier, so report
metadata retains that distinction. No credentials or keyring setup belong to
this repository change.

Choice and free-text forms initially produced 12/12 Unknown classifications
under explicit inspection labels. The form has an unnumbered caret and a changed
cancel footer. The fix uses its bordered panel and fixed heading, not option
wording or navigation hints. Three synthetic fixtures cover choice, free-text,
and multiple-field forms, including stale-panel and cross-profile regressions.

The corrected question run passed all three variants at 120 and 70 columns,
including 12 independently established target samples, return to IDLE, and live
acknowledgement per attempt. The native elicitation notification plus field-count
metadata establishes these states without operator confirmation. Its private run
identifier is `20260919-142825-0925750e`.

Copilot's initial folder-trust screen required an adapter addition. The harness
checks the owned pane's working directory before accepting that specific dialog.
Its permission notification is used instead of PermissionRequest, which precedes
automatic permission rules. Tests ensure late notifications cannot reopen a
completed request. Copilot tool hooks omit call IDs, so question continuation
also checks the original tool-entry timestamp.

The background-monitor prompt caused Copilot to start a native async shell rather
than a Monitor tool. The initial sweep's Unsupported monitor entries therefore
established shell background state, not monitor coverage. A regression now
requires a native Monitor call for that scenario; superseded entries are retained
as adapter diagnostics. Background-shell coverage remains valid.

The remaining sweep (`20260919-143156-c5d61420`) established IDLE, WORKING,
command/edit WAITING, and background-shell states at both widths. Core and
approval target samples had no mismatches; working and approvals also passed
return-to-IDLE and live acknowledgement. Background shells report Unsupported
because the production Copilot profile has no BACKGND fingerprint.

Blocked-subagent WORKING targets passed at both widths. Completion remained
Inconclusive: Copilot's child tool hooks did not consistently include child IDs,
so the helper-to-subagent completion join could not be proved. Detached-agent
attempts likewise remained Inconclusive under the strict lifecycle oracle.
These are evidence limits, not new classifier fingerprints or passing coverage.

DEAD targets also passed at both widths. Combined shell/agent scenarios remained
Inconclusive for the same child-lifecycle evidence limitation. The sweep covered
every requested Copilot scenario and returned exit code 2 for its explicit
unsupported/inconclusive coverage, not a false all-green result.

| Copilot area | Final coverage at 120 and 70 columns |
| --- | --- |
| IDLE, WORKING, DEAD | Passed |
| Native command/edit approval | Passed, including completion and acknowledgement |
| Choice, free-text, multiple-field forms | Passed, including completion and acknowledgement |
| Background shell | Established; profile capability Unsupported |
| Blocked subagent | WORKING passed; completion Inconclusive |
| Detached agent and combined background | Inconclusive lifecycle evidence |
| Native monitor | Model substituted async shell; Inconclusive after oracle correction |

The corrected monitor rerun `20260919-144129-396e5154` reported Inconclusive
at both widths, as required. All validation sessions were cleaned up.
