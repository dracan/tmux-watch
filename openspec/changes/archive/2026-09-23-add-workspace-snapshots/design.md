## Context

The agreed interview scope is in docs/planning/workspace-snapshot.md. Existing
pane discovery is display-oriented and pause state is per TUI instance. Restore
must support native tmux and Windows/psmux without executing saved commands.

## Goals / Non-Goals

**Goals:** durable file plus clipboard export; explicit import; all panes and
nested splits; shared persistent pause state; honest resume identity; both hosts.

**Non-Goals:** process resurrection, scrollback backup, cross-OS path translation,
agent hooks, arbitrary shell execution, or guessing conversation IDs.

## Decisions

- Use a versioned JSON document with sessions, windows, pane metadata, saved layout,
  active selection, linked/zoom flags, and optional conversation IDs. Unknown identities carry reasons.
  A script export was rejected because imports must be data, never executable code.
- Put inventory, validation, layout parsing, and explicit restore orchestration in
  a workspace module. Keep constructors and polling free of lifecycle operations.
  Typed tmux methods construct fixed argument lists without a trailing command.
- Read free-text metadata separately from numeric inventory to preserve delimiters
  in paths and names. Verify inventory did not change during capture. Reject
  malformed metadata rather than silently omit panes.
- Parse saved layout trees and validate pane coverage before creation. Reconstruct
  shells and apply mapped layouts; compare resulting pane placement. Size rounding
  is acceptable. Detect unsupported linked/zoomed structures during preflight.
- Scope persistent pause records by owning multiplexer process identity (PID plus
  start time), pane,
  and host. This avoids trusting psmux's incompatible server identity fields.
  Runtime row and monitor keys also include the owning process because psmux
  reuses raw pane IDs across sessions. Fully qualify psmux capture/focus targets.
  Use atomic, per-pane files so independent writers cannot overwrite other rows.
- Conversation resolution is fail-closed. Existing metadata requires demonstrated
  agent/version-specific switch correctness before use. Unverified agents export
  null IDs with a reason. Never read transcripts or select by cwd/recency.
- Save JSON before copying to an OS clipboard adapter. Clipboard failure retains
  the file. Expose --export [path], --import <path|->, and --import-clipboard;
  use e for export and i for an inline import path/clipboard prompt in the TUI.
- CLI imports print a result report; TUI imports persist that report and show its
  path. Resume commands are generated only from validated IDs and known agents,
  never from executable text in the document.

## Risks / Trade-offs

- Psmux compatibility differs by build: validate native Windows round trips and
  fail preflight for unsupported structures instead of flattening layouts.
- Races remain after preflight: creation failures retain resources and report
  every completed step. Never delete user windows as rollback.
- Conversation metadata can be stale: return unknown unless an adapter is proven.
- Pause persistence failures: surface them; never silently claim a saved change.
- Snapshots contain private paths: keep files in per-user application data and
  exclude generated snapshots from source control.

## Migration Plan

Existing sessions default to unpaused until toggled. Future watcher restarts share
saved settings. No agent configuration changes or data migrations are required.
Snapshot schema versions are explicit; unknown versions fail before any mutation.

## Open Questions

No product decisions remain. Native backend behavior and optional metadata adapter
reliability are implementation validation gates, not promises inferred from docs.

## Verified backend differences

- Psmux numeric session targets under `-L` can receive its namespace prefix twice;
  use session names in scoped metadata targets.
- Psmux pane IDs are per session process. Resolve them to pane indexes inside a
  named window for lifecycle operations; qualify monitoring/focus targets too.
- Psmux uses contiguous zero-based window indexes. Reject other index arrangements
  in preflight on that backend; native tmux preserves nondefault indexes.
- Both backends assign restored layout leaves in pane-list order. Append temporary
  panes in that order, using a tiled arrangement to leave room for each split,
  then apply the saved layout and verify pane placement.
- Version-specific conversation metadata remains unverified. This release reports
  all detected agent conversation IDs as unknown; manually supplied UUIDs in an
  edited snapshot generate guidance, never executable import actions.
