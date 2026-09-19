# Repair verified drift

Read this after the refresh skill identifies a repair. Keep the initial reports
intact and work from the current repository instructions.

## Plan the repair

Use the installed `openspec` CLI directly; this workflow needs no global wrapper
plugin. Inspect `openspec list --json` for a change belonging to this repair.
Reuse only a clearly matching change, leaving unrelated work untouched. Otherwise
create a descriptive letter-starting kebab-case name with `openspec new change NAME`.

Read `openspec status --change NAME --json`. For each ready artifact, obtain
`openspec instructions ARTIFACT --change NAME --json`, read its dependencies,
and write to its resolved path using the returned template. Capture the observed
versions, independent evidence, proposed structural token or adapter correction,
and validation scope. Refresh status until the apply requirements are complete.

Before implementation, read `openspec instructions apply --change NAME --json`
and every returned context file. Track tasks and update them as they complete.
This planning step belongs to a verified repair, not to a clean calibration run.

## Make the smallest supported correction

For detection drift, create a synthetic or scrubbed fixture preserving the actual
glyphs, indentation, and wrapping. Register the expected state and demonstrate
that the existing detection fails it before changing the matcher. Include relevant
negative, precedence, ordinary IDLE/WORKING, and cross-profile regression cases.

Prefer profile token changes in `src/TmuxWatch/Config/WatchConfig.cs`. Extend
`AgentProfile` or `PaneClassifier` only when existing fields cannot represent the
verified structure. New optional patterns default to disabled, and classification
stays pure with the production scan window and tmux whitelist intact. DONE remains
a monitor transition, not a classifier output.

For harness drift, read `tools/TmuxWatch.Calibration/AGENTS.md`, then inspect the
adapter, helper, evidence, driver, and scenario code involved. Check native CLI
help and current official documentation when contracts have changed. Add focused
regressions for incorrect or missing evidence. Preserve private-server ownership,
bounded lifetimes, minimal hook payloads, and cleanup. A harness repair must not
replace independent proof with classifier output, requested intent, or fabricated
operator confirmation. Keep UI settings used by scenarios explicit in reports
and documentation; do not hide a mismatch by silently changing the tested view.

Update fixture provenance and verified-version comments from actual captures.
Keep raw captures, credentials, and model transcripts out of tracked files. The
export command only creates a candidate; review its scrubbing before registering
it in the fixture suite.

## Verify and deliver

1. Run `dotnet build`, `dotnet test`, and `./calibrate.sh replay`. Complete any
   additional checks required by the changed files' instructions.
2. Rerun affected live scenarios at both default widths with the corrected build.
   Include completion/acknowledgement checks. Broaden to other profiles or states
   when shared changes affect them; avoid repeating an already successful full
   sweep without a new reason. After a harness repair, obtain fresh independent
   evidence rather than relabelling the old run as successful.
3. Inspect the new report, including transient mismatches, unattempted cases, and
   exclusions. Record the original failure and the exact confirming run. Update
   `tools/TmuxWatch.Calibration/VALIDATION.md` with the versions, scope, settings,
   results, and remaining limits. Update usage docs when behavior changes.
4. Complete the repair's OpenSpec tasks only after their checks pass. Run
   `openspec validate NAME --strict`, review the delta against the main specs,
   and use `openspec archive NAME --yes` to sync and archive the completed change
   under the requested end-to-end workflow. Validate the resulting specs with
   `openspec validate --specs --strict`. An unresolved required check leaves the
   repair incomplete; do not archive it as verified work.
5. Review the diff for unrelated edits and sensitive content; check whitespace
   with `git diff --check`. Stage only the task's reviewed files. Follow the
   repository's main commit/push policy and any narrower session instructions.
   If a required authorization is absent, finish the reviewable work before asking.
   Verify the final branch, working tree, and remote result. Report a failed push
   as pending delivery, never as successful publication.

If authentication, unavailable UI evidence, or a native capability blocks full
verification, retain the evidence and explain exactly what remains. Continue
independent work, but do not claim a complete refresh or silently weaken its scope.
