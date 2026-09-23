# Workspace snapshot interview

Status: implemented and archived after user confirmation of the complete scope.
Implementation and validation are recorded in
`openspec/changes/archive/2026-09-23-add-workspace-snapshots/`.

## Purpose

Save a tmux workspace before reboot and import it into tmux-watch afterward.
Requested metadata includes window name, coding agent, resumable conversation
identity where available, working directory, and paused state. The user's
original term "pinned" means the state toggled with `p`.

## Accepted decisions

- Include all tmux panes, including ordinary shells and paused panes.
- Export a durable file and copy the same importable contents to the clipboard.
- Preserve tmux session names, window names, and pane splits on restore.
- Provide both TUI keys and terminal commands for export and import.
- Preserve paused/unpaused state across export and import.
- Explicit TUI and CLI imports may create sessions, windows, and splits and
  restore their arrangement. This deliberately extends the existing
  keypress-only lifecycle rule to an explicit CLI import request.
- Restore shells in their saved working directories. Provide agent resume
  commands for the user to run manually; do not automatically launch agents
  or ordinary programs such as dev servers. The prohibition on pane input
  injection and command execution remains in force.
- Unknown conversation identity does not block export. Mark it unknown and
  report it; never infer an exact conversation from working directory alone.
- If a saved session name already exists, stop before creating anything and
  report the conflict. Do not merge, rename, overwrite, or duplicate it.
- Check all working directories before creation. If any are missing, stop and
  list them so the user can correct the paths first.
- Use readable JSON, with a timestamped file per export and an optional chosen
  path. Accept a file or pasted JSON for import.
- Copy the saved JSON to the clipboard. If copying fails, retain the file and
  report its location.
- Pause settings become persistent and shared between watchers of the same
  tmux server. A standalone export reads the same settings. Import transfers
  saved settings onto newly created pane identities.
- If creation fails after preflight, keep the partial restore and report exactly
  what succeeded and failed. Do not automatically delete created resources.
  Existing-session conflict checks still apply to retries.
- Both native tmux and native Windows/psmux are required in the first version.
  The user needs reboot recovery on a tmux home computer and a psmux client
  laptop. Native Windows support must not be deferred.
- Obtain conversation identity only through existing read-only metadata whose
  current-conversation association has been verified for the supported agent
  version. Otherwise export unknown. Do not install hooks, change agent
  configuration, or guess from stale metadata.
- Preserve split structure and pane placement while allowing pane sizes to
  scale and round. If a structural feature cannot be faithfully restored,
  stop during preflight rather than silently change the arrangement.
- Each pane has at most one resume target: the currently selected agent
  conversation. Other conversations held by the same agent process are out
  of scope. If the selected conversation cannot be distinguished reliably,
  mark the target unknown rather than choose another open conversation.

## Baseline verified during the interview

- Discovery already exposes session and window names, pane indexes, working
  directories, and agent type. Conversation resume identity is not collected.
- Current discovery does not capture enough topology to restore split layouts.
- The user-facing `p` action pauses a pane. No permanent pin feature exists.
  Pause state belongs to a running watcher and currently resets on restart.
- The tmux access layer permits `new-window`, but does not yet expose creating
  sessions, splitting panes, or restoring layouts.
- AGENTS.md currently restricts lifecycle operations to explicit keypresses and
  permanently prohibits injecting input or executing commands in panes. The
  accepted CLI import extends the lifecycle trigger rule only. Implementation
  must document the new allowed lifecycle operations in AGENTS.md while
  preserving the pane-content boundary.
- Every watcher has its own in-memory pause set, with no state publication or
  communication mechanism for a separate export process. The accepted shared,
  persistent pause settings deliberately change this existing behavior.
- Current platform documentation covers Linux/macOS tmux, WSL, and native
  Windows/psmux. Both tmux and psmux restore are now required; psmux layout
  compatibility remains a factual prerequisite to a concrete restore design.
- Linked windows can belong to several tmux sessions. Restoring the accepted
  workspace arrangement requires preserving shared window identity, not merely
  grouping panes by session name and window index.
- Existing discovery excludes only the current watcher's own pane. A complete
  snapshot requires a separate inventory path that includes it. Under the
  accepted all-pane and shell-only scope, watcher and dead panes are represented
  in the snapshot and restored as shells, without restarting their programs.

## Conversation identity evidence

Official references establish how to resume a known conversation:

- Copilot: `copilot --resume=SESSION-ID`.
  [CLI reference](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-command-reference)
- Claude Code: `claude --resume SESSION-ID`.
  [CLI reference](https://code.claude.com/docs/en/cli-reference)
- Codex: `codex resume SESSION_ID` using an exact UUID.
  [CLI reference](https://learn.chatgpt.com/docs/developer-commands?surface=cli#codex-resume)

All three document conversation identifiers in session-start hook metadata:
[Copilot](https://docs.github.com/en/copilot/reference/hooks-reference),
[Claude Code](https://code.claude.com/docs/en/hooks), and
[Codex](https://learn.chatgpt.com/docs/hooks).

The scoped documentation review did not establish a retrospective lookup from
a live pane or process to its current conversation ID. Startup arguments alone
cannot prove current identity after an in-process conversation switch.

Hooks were considered as a prospective source of conversation identity, but
the accepted design uses existing read-only metadata and the unknown fallback.
Hooks are out of scope.

### Read-only alternatives investigated

- Claude has an existing per-process session registry under
  `~/.claude/sessions/`. Locally inspected metadata includes session and process
  identity fields. This is a promising candidate, but requires validation of
  process identity and conversation switches on supported versions. Upstream
  reports describe stale IDs after
  [`/clear`](https://github.com/anthropics/claude-code/issues/56766) and
  [`/resume`](https://github.com/anthropics/claude-code/issues/37737); the reviewed
  reports do not establish a fixed version.
- Copilot session locks may associate a process with an in-use conversation,
  but that does not establish foreground identity. One CLI can hold multiple
  conversations. Its manual `/session id` command exposes the current ID, but
  invoking it automatically would violate the pane-input boundary.
  [Copilot session documentation](https://docs.github.com/en/copilot/how-tos/copilot-cli/use-copilot-cli/work-with-multiple-sessions)
- Codex's documented app-server APIs expose loaded thread IDs, but do not
  establish a mapping to the foreground conversation of an arbitrary live pane.
  A newly started app server cannot answer for an existing CLI instance.
  [App-server documentation](https://learn.chatgpt.com/docs/app-server)
- Open-file paths and startup arguments are not sufficient evidence by
  themselves: files may close while idle, multiple conversations may be open,
  and startup identity may become stale after a switch.

No reliable resolver for every agent on both platforms has been established.
The accepted approach is version-specific read-only metadata inspection with
the unknown fallback. It requires no hooks, agent configuration changes, or
transcript-content collection. Acceptance of Q15 resolves the earlier Q12
preference for a less invasive alternative.

## Psmux restore evidence

The official argument reference documents session/window/split creation with
working directories. Its implementation also parses serialized tmux layouts,
although that support is omitted from the argument reference. Layout replay
assigns panes in leaf order, converts dimensions to integer percentages, and
resets the active pane. Invalid layouts silently fall back to even-horizontal.
The importer must therefore validate input, map pane order deliberately, restore
active selection separately, and verify the resulting arrangement.

Sources: [Argument reference](https://github.com/psmux/psmux/blob/master/docs/tmux_args_reference.md),
[layout implementation](https://github.com/psmux/psmux/blob/master/src/layout.rs#L1102).

Psmux metadata cannot be treated as interchangeable with native tmux metadata:
some linked-window fields are incomplete and some server identity fields have
different meanings. Linked-window sharing is not yet verified end to end.
Sources: [Format implementation](https://github.com/psmux/psmux/blob/master/src/format.rs),
[command implementation](https://github.com/psmux/psmux/blob/master/src/commands.rs#L2097).

These sources describe upstream master, not a verified client-laptop build.
Native Windows round-trip checks are required before claiming psmux support.
Both backends must restore ordinary nested splits, names, directories, pane
membership, and paused state. Exact cell sizes may differ due to terminal size
and psmux percentage rounding; the user accepts this while requiring preserved
split structure and pane placement.

## Implementation work and verification

- Capture the settled scope in OpenSpec before implementing this non-trivial
  change, following the repository workflow.
- Validate each conversation-ID adapter against process identity, idle state,
  conversation switches, and several conversations sharing a directory or
  process. An unverified adapter must return unknown.
- Verify native tmux and native Windows/psmux round trips, including nested
  splits, pane order, names, directories, active selection, and paused state.
  Unsupported structural features must be detected before creation.
- Design persistent pause identity and concurrent updates so stale server or
  pane identifiers cannot transfer pause state to unrelated panes.
- Choose and document the versioned JSON schema, default storage location,
  TUI keys, CLI syntax, and presentation of manual resume guidance. These are
  implementation details within the accepted behavior, not further product
  scope choices. Imported resume information is data, never executed code.
- Report unavailable resume targets without blocking the already agreed
  shell-only restore. Agent installation and agent conversation storage are
  not recreated by the snapshot.
- Verify conflict and directory preflight, clipboard failure, and partial
  creation failure behavior. Leave partial resources in place and report them.
- Update AGENTS.md for explicitly invoked CLI import and its lifecycle verbs,
  preserving the permanent pane-input boundary, and update user documentation
  for persistent/shared pause settings.

## Remaining interview checkpoint

The user confirmed the complete scope. The grilling workflow is complete.
