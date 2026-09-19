## Context

The merged harness retains strict independent evidence but cannot finish some child lifecycles and question flows. Captures also show Copilot live background waiting and a Codex terminal counter above its composer. Work proceeds on main.

## Goals / Non-Goals

**Goals:** Close supported automation gaps, reproduce and fix visible classification gaps, and give daily users a clearly scoped check command. Preserve full diagnostic coverage and private reports.

**Non-Goals:** Infer invisible subagents from transcript prose, widen production tmux access, install dependencies, or claim every native capability exists on every CLI.

## Decisions

1. Correlate hashed session identifiers and native parent task completion. Missing child IDs do not justify treating every Stop as a main completion. Where a foreground parent task has completed after its controlled gate, that native result can prove the child returned without requiring a child hook ID.
2. Question input is an independent test stimulus. Where native dialog notifications are unavailable, preserve unlabelled captures, send only a synthetic answer to a pending known native question, and retroactively establish the settled interval only after its native result confirms receipt. Failed or declined responses remain inconclusive. Classification never determines labels.
3. Copilot background-shell waiting uses its live interrupt-bearing status line. Match observed structure in profile data. Confirm whether the runtime remains busy before assigning the scenario target; a model Stop alone is not an interactive-session handoff.
4. Codex terminal counts must be complete live status controls immediately above the last composer. Add an optional position-specific background pattern rather than searching transcript prose. Invisible detached agents remain an explicit UI limit; no transcript fingerprint is invented.
5. Add an explicitly scoped automated check command alongside the unchanged full diagnostic run. Record excluded capabilities with explanations and keep unsupported/inconclusive results visible in full runs. Daily checks must not silently promise full coverage.
6. Verify current installed versions with isolated sessions, retain synthetic fixtures and private raw reports, and document usable commands and limits. Record main as the repository's commit/push workflow.

7. Treat an upcoming Claude login-expiry reminder separately from an actual login screen. Preserve suppression of real login captures. Delay submission briefly after literal terminal input so native paste/burst buffering receives the answer before Enter.

8. Claude's native async Agent result (`isAsync=true`, `status=async_launched`) supplies background-launch evidence when the input schema omits a background flag. For deterministic UI coverage, the blocked-agent launcher uses native foreground-only mode; agent/mixed background scenarios hide the native post-turn banner to exercise the fleet-only view. Both settings are disposable and recorded. Preserve the default waiting-banner WORKING fingerprint rather than relabeling that screen to obtain a pass.

9. Interactive console reads use one reusable background read with cancellable waiting. The terminal's synchronous reader can ignore `ReadLineAsync` cancellation; it must not defeat scenario deadlines or create competing readers after a timeout.

## Risks / Trade-offs

- Native schemas drift: preserve sanitized structural facts and test stale/missing event cases.
- Render delay: retain settling and repeated samples; verified answers establish only the interval for their own pending call.
- Invisible agents: observable terminal counts cannot prove absence of other work. Document this limit rather than widening the capture or guessing.
- Model variability: retries remain bounded; unsupported native capabilities differ from a classifier regression.
