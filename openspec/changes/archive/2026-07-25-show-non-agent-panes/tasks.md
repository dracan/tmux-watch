## 1. Discovery and pane model

- [x] 1.1 Add the window last-activity timestamp to `PaneDiscovery.Format` and parse it in `Parse`, degrading to "unknown" when absent or unparseable
- [x] 1.2 Add the activity field to the `Pane` record plus a helper that renders elapsed time since activity
- [x] 1.3 Split discovery output so `DiscoverAgentPanes` also returns the unmatched panes as a non-agent inventory from the same single enumeration
- [x] 1.4 Exclude the watcher's own pane from the inventory when `TMUX_PANE` is set
- [x] 1.5 Extend `PaneDiscoveryTests` for the new field, the inventory split, the single-enumeration guarantee, and self-pane exclusion

## 2. Monitor pass-through

- [x] 2.1 Carry the inventory onto `MonitorSnapshot` as an inert list, with a comment stating it is never captured, classified, tracked, notified on, or fed to the pointer aggregate
- [x] 2.2 Leave the inventory empty on enumeration failure while retaining tracked agent state
- [x] 2.3 Extend `AttentionMonitorTests`: inventory reaches the snapshot uncaptured, raises no attention events, and does not affect the pointer aggregate

## 3. tmux access layer

- [x] 3.1 Add `select-pane`/`selectp` to the `TmuxRunner` verb whitelist and expose `SelectPane` on `ITmuxClient`
- [x] 3.2 Chain `select-pane` after `select-window` in the TUI switch action so a pane in a split is landed on precisely
- [x] 3.3 Extend `TmuxRunnerTests` to cover the new verb and assert `send-keys` is still rejected

## 4. TUI rows, tables and columns

- [x] 4.1 Build the non-agent row model (process, window name, location, activity time) and order it by session, window index, pane index
- [x] 4.2 Render the Other panes table between the agent table and the Paused table, omitting it when empty
- [x] 4.3 Reuse the state column for the process name on non-agent rows with distinct styling, and the time column for activity time
- [x] 4.4 Render agent and non-agent rows in a single Paused table sharing the same column set
- [x] 4.5 Update the main table title so it lists the new keys

## 5. Toggles

- [x] 5.1 Add the `o` toggle for the Other panes table, defaulting to on and runtime-only
- [x] 5.2 Add the `c` toggle for companion panes (non-agent panes sharing a window with an agent pane), defaulting to on and inert while `o` is off
- [x] 5.3 Re-render immediately on either toggle rather than waiting for the next poll
- [x] 5.4 Add tests for companion classification and for both toggles' filtering behaviour

## 6. Highlighted row and addressing

- [x] 6.1 Track the highlighted pane by id and resolve it to a row position on each render
- [x] 6.2 Move the highlight with the up/down arrow keys across all visible tables as one continuous list
- [x] 6.3 Fall back to the nearest surviving visible row when the highlighted pane disappears or is hidden by a toggle
- [x] 6.4 Render the highlight distinctly from the focus marker, leaving the focus marker as a passive indicator
- [x] 6.5 Assign address keys continuously in render order: digits 1-9, then shift+letter for row ten onward
- [x] 6.6 Switch to the highlighted row on Enter and to any row by its address key, for agent and non-agent rows alike
- [x] 6.7 Add tests for highlight anchoring through a re-sort, recovery when a pane vanishes or is hidden, and address key assignment past nine

## 7. Retargeted actions

- [x] 7.1 Retarget `p` from the focused pane to the highlighted row
- [x] 7.2 Allow pausing and resuming non-agent rows, with no effect on notifications or the pointer
- [x] 7.3 Retarget `a` to the highlighted row and make it a no-op on non-DONE and non-agent rows
- [x] 7.4 Update `PauseTests` and `WatcherPointerTests` for the new target and for paused non-agent rows

## 8. One-shot output and docs

- [x] 8.1 Include non-agent panes in the `--once` output with location, process, and window name
- [x] 8.2 Widen the read-only guarantee wording in `AGENTS.md` to cover `select-pane` as permitted, server-visible focus state, keeping the no-input prohibition explicit
- [x] 8.3 Document the new keys (`o`, `c`, arrows, Enter, shift+letter) and the retargeting of `p`/`a` in `AGENTS.md`

## 9. Verification

- [x] 9.1 Run `dotnet build` and `dotnet test` and fix any failures
- [x] 9.2 Run `./go.sh` against live panes to confirm the Other panes table, both toggles, highlight movement, and pane-precise jumping into a split
