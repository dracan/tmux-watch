## Context

The reported Windows/psmux 3.3.8 pane lists a `tgrep` command and the same activity timestamp as older windows. Replaying these fields through discovery reproduces both symptoms. At psmux revision `66cf613`, `src/format.rs` uses the deepest process descendant as a command fallback and maps `window_activity` to `app.created_at` (also `session_created`). This watcher currently trusts both fields.

Sources: https://github.com/psmux/psmux/blob/66cf613/src/format.rs and https://github.com/microsoft/tgrep.

## Goals / Non-Goals

**Goals:**

- Recover live agent ownership when Windows reports a child helper.
- Preserve profile-driven matching and existing direct-command precedence.
- Never carry a process-based match across polls after the owner exits.
- Present unavailable activity honestly and use accurate table headings.

**Non-Goals:**

- Change psmux upstream, agent configuration, or classifier tokens.
- Infer detached work or attention state from process existence.
- Add tmux verbs or capture unknown panes for identification.

## Decisions

### One read-only process snapshot per Windows enumeration

Use Toolhelp32 to collect process ids, parent ids and executable basenames. Check creation times and current process names for processes reachable from the enumerated pane roots; unreadable or exited entries are omitted. Creation-time ordering prevents an orphan process from attaching to a newer process that reused its parent's pid. Keep this Windows adapter separate from a pure process-tree resolver so Linux tests exercise the same ownership rules with synthetic snapshots. No command lines, environment, process memory or pane contents are read.

The resolver starts at `pane_pid` and follows current parent links. It stops at the first configured agent on each branch, does not cross another enumerated pane root, and rejects multiple independent agent owners. It does not choose an owner by largest pid or recognize helper aliases. A dead pane cannot acquire an owner through this fallback. Rebuild the snapshot each poll; no historical ownership cache. A failed snapshot leaves direct-command and session-convention matching available.

Direct-command matching keeps precedence, followed by process ownership on Windows, then the existing session-name backstop. Stamp identities during enumeration so both normal discovery and calibration consume the same result. Unix does not inspect local processes for this purpose.

### Host capability for activity

Expose whether the multiplexer client supplies reliable window activity. Native Windows clients and explicitly named psmux executables do not currently qualify, including psmux exposed as `tmux.exe`. Normalize their activity field to unknown during enumeration. Do not invent a first-seen timer or infer support from equal timestamps: real Unix windows can share timestamps too. This is conservative across psmux versions until upstream support is verified, with no new user configuration required.

### Headings describe the row types

Agent-only tables retain State and In state. Non-agent-only tables use Command and Quiet for. Mixed paused tables use State / Cmd and Age, with a caption explaining each row's age. Missing non-agent activity renders as `-`. Preserve frame buffering and the single flush.

## Risks / Trade-offs

- Process trees do not prove terminal foreground ownership on Windows. Restrict fallback to one unambiguous live agent tree beneath the pane root; preserve the reported-command match when available. Node-hosted agents without an identifiable executable retain existing command/convention behavior.
- Processes can exit during enumeration. Discard unreadable identities, validate creation-time ordering, and resnapshot next poll. A snapshot remains a point-in-time observation, like pane enumeration itself.
- psmux may implement real activity later. Continue showing unknown until that capability is verified; keep normalization at the client/discovery boundary for a future change.
- Linux tests cannot execute Windows native calls. Add a Windows-only integration check for the native snapshot as well as portable tests of real discovery with injected snapshots, and disclose any unavailable native runtime validation.

## Migration Plan

No configuration migration. Build/test, deploy the updated watcher on Windows, and verify Copilot remains in the agent table while child tools run. Rollback is reverting this change. The upstream psmux installation is untouched.

## Open Questions

None blocking implementation. Live confirmation on the affected client remains external validation.
