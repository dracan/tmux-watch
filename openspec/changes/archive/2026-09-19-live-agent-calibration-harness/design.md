## Context

The production classifier is a pure API, and AttentionMonitor accepts ITmuxClient, INotifier, and TimeProvider. Detection tokens are data. The production tmux runner must never gain input injection or automated lifecycle operations. All three profiles exist; background fingerprints currently exist only for Claude.

## Goals / Non-Goals

**Goals:** Run real interactive CLIs on tmux, collect independently labelled repeated captures, evaluate production classification and monitor behavior, cover native prompt variants and backgrounds, and preserve evidence of incomplete coverage. Run sequentially with bounded retries and optional human assistance. No external helper packages.

**Non-Goals:** psmux, unattended credential provisioning, modifying ordinary agent configuration, promising every model follows every scenario, or fabricating fixtures to make an unobserved UI pass.

## Decisions

1. A separate .NET executable owns a randomly named private tmux server and its pane identifiers. Every driver command explicitly targets that server; the production runner stays unchanged. Disposable launch scripts use quoted arguments and a process lifetime limit. Cleanup only targets owned resources.
2. Agent adapters write temporary local hook settings and launch interactive sessions using installed binaries and existing authentication. Hooks record a minimal event envelope, not tool inputs, transcripts, or credentials. Repo-owned Python standard-library helpers implement bounded execution gates and hook recording. Native tools still render the actual agent UI.
3. Scenarios cover idle, foreground work, native command/edit approvals, choice/freeform/multiple questions, background shell/monitor/subagent combinations, blocked subagents, and dead panes. Repeated standard/narrow captures test wrapping and spinner frames. Lifecycle events plus controlled helper facts label observations; tool entry alone does not establish a visible dialog. Optional operator confirmation resolves unsupported evidence paths. Production classification is never the expected-state oracle.
4. A separate monitor replay layer uses captured or existing scrubbed fixture screens and a controllable clock to check all derived transitions, grace behavior by background reason, first sight, acknowledgement, Unknown debounce, notification counts, and pointer aggregation. Reports distinguish replay coverage from live coverage. Live timelines also run through the production monitor.
5. Outcomes distinguish pass, mismatch, inconclusive, unavailable, and unsupported. Brief mismatching samples remain visible even if the stable window passes. Retries retain every attempt. A partial run cannot return a full-success exit code.
6. Reports and raw captures live under a gitignored run directory with restrictive permissions. Candidate export requires a reviewed source and preserves structural glyphs. It writes metadata and candidate files outside the fixture suite, never stages or commits them automatically.
7. Authentication preflight reads only status results and never persists login output. Interactive runs can pause for the operator; unattended runs continue with unavailable results. Copilot installation is performed by the user when requested. Physical bell/pointer smoke testing is opt-in with restoration in a finally block.

8. Copilot 1.0.86 forms have no numbered selection cursor and use a different cancel footer. Match their structural envelope: a column-zero horizontal rule, the fixed `Copilot needs information.` panel heading, body lines, and a closing column-zero rule at the end of the bounded tail. Indented rules inside text fields are body content. A body cannot cross another column-zero rule, preventing an old panel above a new composer from matching. This names the panel rather than incidental navigation affordances or field wording. Existing matchers operate one line at a time, so an optional empty-disabled `WaitingPanelPattern` is necessary; no history or tmux behavior enters the classifier.
9. Copilot notification hooks supply `elicitation_dialog` and `permission_prompt` evidence independently of capture text. Require a pending native tool and invalidate the label on completion or renewed input. PermissionRequest is unsuitable for Copilot because it fires before automatic permission rules. Read only field/option counts from requestedSchema, never field names or values. Missing or late notifications remain inconclusive. Question recovery compares tool-entry timestamps as well as optional call IDs, since Copilot hooks can omit IDs.

## Risks / Trade-offs

- Model noncompliance or missing hooks: inconclusive evidence and bounded retries, never inferred success from the classifier.
- Hook event before UI paint: settle interval and repeated observations; retain transition samples separately.
- Authentication, trust dialogs, and changed flags: adapter preflight plus optional operator attachment; no blind approval of unknown dialogs.
- Background resource lifetime: helper deadline and launcher timeout survive harness exit; normal cleanup releases gates and terminates the owned server.
- Existing background detection gaps: run the scenarios but identify unsupported profile capabilities explicitly.
- Real transcripts may contain private data despite synthetic prompts: keep raw artifacts private and gitignored; review before fixture promotion.

## Migration Plan

Add the tool and tests, exercise it against available authenticated CLIs, review captured defects, and add only supported token fixes. Document remaining inconclusive scenarios and exact tested versions. Rollback removes the development tool and any isolated profile changes without affecting watcher configuration.

## Open Questions

Live verification must establish which installed versions expose trustworthy hooks for native questions and background tasks. Any missing evidence remains explicit in the report.
