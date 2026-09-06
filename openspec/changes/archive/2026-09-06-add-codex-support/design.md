## Context

The existing AgentProfile drives discovery and a pure, bounded-tail classifier. The monitor already derives DONE and handles attention, acknowledgement, and navigation independently of agent identity.

Live Codex CLI 0.153.4 panes report the command `codex`, a U+203A composer, and a timed status line ending in `esc to interrupt`. Approval and question layouts were checked against OpenAI's public Codex snapshots at commit `6af345407d9c2a568da9d01b6c4b81a9e61495c0` (approval_modal_exec, approval_modal_patch, request_user_input freeform/options/notes/footer_wrap/multi_question_last). Those snapshots are supplemental evidence, not live captures of every state on the installed build.

## Goals / Non-Goals

**Goals:** Built-in discovery and reliable WAITING, WORKING, IDLE, and derived DONE; fixture coverage for approval, free-text questions, notes, queued input, stale transcript prose, and monitor transitions.

**Non-Goals:** Codex BACKGND classification, tracking detached agent completion, new tmux verbs, hooks or event-log integration, wider scan windows, or changes to existing agent behavior.

## Decisions

1. Add a built-in Codex profile with command `codex`, no naming backstop, and an input-caret regex excluding numbered choices. Command matching already supports `codex.exe`. Explicit non-empty profile lists keep replacing the defaults.
2. Add optional `WaitingChromePattern`, searched below the last composer or throughout the bounded tail when no composer exists. Codex question editors use the same caret as the normal composer, and an open notes editor sits below its numbered choices. Therefore cursor matching alone misses these forms. Match the submit-answer/submit-all footer, including wrapped footers and notes, plus approval confirmation footers. Existing whole-tail footer markers remain unchanged for other agents.
3. Add optional `WorkingLinePattern`, matched per line in the bounded tail. Codex requires a line-start status bullet, action label, elapsed time, and interrupt hint. The interrupt phrase alone also appears in free-text questions and prose, so the old marker-only facility is insufficient. Requiring literal `Working` would miss custom action labels. Matching only the line immediately before the composer would miss queued messages and status details.
4. Reuse existing precedence and state transitions. The classifier never produces DONE. Do not infer ongoing background work from transcript mentions; Codex DONE means the observed foreground turn returned to its composer, not that detached work has completed.
5. Keep tokens in profile data. All new pattern fields default to empty, preserving existing profiles. Fixtures use synthetic content with verified structural glyphs; no real transcript is committed.

## Risks / Trade-offs

- UI drift or remapped keys can invalidate the default footer tokens. Document the evidence version and allow profile overrides; missing signals use existing Unknown handling.
- The 16-nonblank-line window can lose signals in short panes or tall editors. Keep the bounded scan and test missing signals rather than widening it into transcript history.
- A verbatim reproduction of a full live status line can still resemble live UI within the scan window. Anchoring prevents ordinary prose and completed-turn banners from matching, while composer scoping excludes old waiting menus and question footers.
- Background and detached-agent lifecycles are outside scope. Test that incidental background prose does not classify as WAITING or WORKING, and document the foreground-turn meaning of DONE.

## Migration Plan

Default users gain Codex automatically after rebuilding. Users with explicit agent lists add the documented Codex profile. Rollback removes that profile from the list or reverts this change; no persistent state migration is needed.

## Open Questions

No blocking design decisions. Live approval/question verification on every Codex build remains a calibration task; this change combines installed-build IDLE/WORKING evidence with pinned upstream renderer snapshots for the other forms.
