## Why

tmux-watch only lists panes running a coding agent, so every other pane in the tmux
server (k9s, lazygit, an editor, a plain shell) is invisible and unreachable from the
watcher. The watcher already sits full-height in a docked terminal and is the natural
place to see and reach the whole session, but today the user has to leave it and use
tmux's own switcher for anything that is not an agent.

## What Changes

- Discovery exposes the panes that match no agent profile as an **inventory** alongside
  the matched agent panes, reusing the single existing `lsp -a` enumeration. These panes
  are never captured, classified, or notified on.
- The TUI gains an **Other panes** table, rendered between the agent table and the Paused
  table, listing one row per non-agent pane in tmux order (session, window index, pane
  index), with the pane's foreground process shown in the State column position.
- Two runtime toggles, both defaulting to on and both reset on each launch (matching the
  existing `w` wide-mode toggle):
  - `o` - show/hide the Other panes table entirely.
  - `c` - within that table, show/hide **companion panes**: non-agent panes that share a
    window with an agent pane. Only meaningful while `o` is on.
- A **highlighted row** driven by the up/down arrow keys, spanning all tables as one
  continuous list. Enter jumps to the highlighted pane. The highlight is anchored to a
  pane id, so it follows its pane as the agent table re-sorts, and falls back to the
  nearest surviving row when its pane disappears.
- **BREAKING (interaction)**: `p` (pause/resume) and `a` (acknowledge) now act on the
  highlighted row instead of the client-focused (`►`) pane. `►` remains as a read-only
  indicator of where tmux focus is.
- `p` works on non-agent rows too: a paused non-agent pane moves into the existing Paused
  table, which keeps its current column set - the State column shows the process name for
  non-agent rows and the classified state for agent rows.
- Row addressing extends past the current nine: digits `1`-`9` address the first nine rows
  as today, then shift+letter (`A`, `B`, `C`, ...) addresses rows ten onward.
- Jumping selects the **pane**, not just the window: `select-pane` is added to the tmux
  verb whitelist so a highlighted pane in a split is actually landed on. This also fixes
  the pre-existing case of two agent panes split within one window, where both rows jumped
  to the same place.
- The `In state` column shows time since last activity for non-agent rows (from tmux's
  activity timestamp) instead of a classified-state timer.
- `--once` includes the non-agent panes in its one-shot output.

## Capabilities

### New Capabilities

(None - this extends existing capabilities.)

### Modified Capabilities

- `pane-discovery`: non-agent panes are exposed as inventory rather than discarded, while
  the prohibition on capturing them is retained and strengthened.
- `attention-monitor`: the snapshot carries non-agent panes as inert data that never
  enters the capture / classify / state-machine / notification pipeline.
- `watcher-tui`: new Other panes table and toggles, highlighted-row navigation, `p`/`a`
  retargeted to the highlighted row, extended row addressing, pane-precise jumping, and a
  widened read-only guarantee that permits `select-pane`.

## Impact

- `src/TmuxWatch/Discovery/PaneDiscovery.cs` - return unmatched panes alongside matched.
- `src/TmuxWatch/Monitor/AttentionMonitor.cs`, `TrackedPane.cs` - snapshot pass-through.
- `src/TmuxWatch/Tui/WatcherApp.cs` - the bulk of the change (tables, toggles, highlight,
  key handling, addressing, jump).
- `src/TmuxWatch/Tmux/TmuxRunner.cs`, `ITmuxClient.cs` - add `select-pane`.
- `src/TmuxWatch/Discovery/PaneDiscovery.cs` format string + `Tmux/Pane.cs` - add the
  activity timestamp field.
- `src/TmuxWatch/Program.cs` - `--once` output.
- `AGENTS.md` - the read-only guarantee's wording must be widened from "the watcher's own
  client focus" to include selecting a pane within a window, which is server-visible state.
- Tests: `PaneDiscoveryTests`, `AttentionMonitorTests`, `PauseTests`, `TmuxRunnerTests`,
  plus new coverage for ordering, toggles, highlight anchoring, and addressing.
- No change to classification, notifications, or the pointer signal.
