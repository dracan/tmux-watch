## 1. Project scaffolding

- [x] 1.1 Create a C# console project (`tmux-watch`) targeting the Windows/.NET host and add the `Spectre.Console` package
- [x] 1.2 Add a config model (poll interval, Copilot session-name convention, notification channel, and overridable match patterns) with defaults from the verified fixtures
- [x] 1.3 Set up a test project and a `fixtures/` folder; commit the real captured pane outputs (WAITING command-approval, WAITING ask_user, WORKING, IDLE, lazygit-control) as classifier test inputs

## 2. psmux access layer (read-only)

- [x] 2.1 Implement a `psmux` runner using `ProcessStartInfo` + `ArgumentList`, returning stdout/exit code, with a guard that whitelists allowed verbs (`lsp`, `capture-pane`, `display-message`, `switch-client`, `select-window`) and forbids `send-keys` against watched panes
- [x] 2.2 Implement pane enumeration via a single `lsp -a -F` call, parsing pane id, session, window/pane index, foreground command, and dead flag (satisfies `pane-discovery`: enumerate)
- [x] 2.3 Handle psmux-missing / server-down by returning an empty set and surfacing a non-fatal error (satisfies `pane-discovery`: server unavailable)
- [x] 2.4 Implement read-only `capture-pane -p -t <id>` retrieval for a single pane

## 3. Copilot identification

- [x] 3.1 Filter enumerated panes to Copilot sessions by `pane_current_command == copilot`, with the configured session-name convention as a backstop (satisfies `pane-discovery`: identify + backstop)
- [x] 3.2 Key panes by pane id and reconcile appear/disappear across polls (satisfies `pane-discovery`: stable identity)

## 4. State classifier (pure, fixture-tested)

- [x] 4.1 Implement WAITING detection on invariants (`❯\s*\d+\.` OR a line containing both `↑/↓` and `esc to cancel`), explicitly not relying on `enter to select`/`to navigate` (satisfies `pane-state-detection`: WAITING incl. both prompt types + wording-difference scenario)
- [x] 4.2 Implement WORKING detection (configured spinner glyph immediately preceding `Working`) (satisfies WORKING scenarios)
- [x] 4.3 Implement IDLE detection (input box present + idle hint tokens, no WAITING/WORKING signal) (satisfies IDLE scenario)
- [x] 4.4 Implement DEAD detection (foreground command ≠ `copilot` or dead flag set) (satisfies DEAD scenario)
- [x] 4.5 Apply fixed precedence WAITING → WORKING → IDLE → DEAD, with a frame-stability tiebreaker only for UNKNOWN (satisfies precedence scenario)
- [x] 4.6 Make all tokens/patterns configurable; bind them from config (satisfies version-tolerant fingerprints scenario)
- [x] 4.7 Write unit tests asserting each committed fixture classifies to its expected state, including lazygit → not WAITING (satisfies no-false-positive scenario)

## 5. Attention monitor

- [x] 5.1 Implement the poll loop at the configured interval, doing one enumeration then one capture per Copilot pane, read-only only (satisfies `attention-monitor`: interval + read-only)
- [x] 5.2 Implement the per-pane state machine tracking last state and entered-at/time-in-state (satisfies state-machine scenarios)
- [x] 5.3 Emit an edge-triggered attention event once on transition into WAITING; suppress repeats while WAITING; clear on leaving WAITING; optional IDLE-edge event behind a config flag (satisfies edge-trigger scenarios + no-mid-stream-false-attention)
- [x] 5.4 Raise the configured OS notification on attention events (satisfies OS-notification scenario)
- [x] 5.5 Tolerate panes disappearing or the server going away mid-watch without crashing (satisfies resilience scenario)

## 6. Watcher TUI

- [x] 6.1 Render a Spectre.Console live view listing watched panes with session/window, state indicator, and time-in-state, refreshing as the monitor updates (satisfies `watcher-tui`: live view scenarios)
- [x] 6.2 Sort/emphasise WAITING panes ahead of WORKING/IDLE/DEAD (satisfies prioritisation scenario)
- [x] 6.3 Implement a keyboard action to switch focus to the selected pane via `switch-client`/`select-window` (satisfies switch-to-pane scenario)
- [x] 6.4 Verify by construction (and by test) that the TUI never issues input to a watched pane (satisfies read-only guarantee scenario)

## 7. Spikes / fixture gaps to close before/while building

- [x] 7.1 Capture an IDLE pane that has *finished a turn* (not just a fresh start) and confirm the idle hint tokens match across repos/themes; update defaults/fixtures if they differ
  - DONE: live panes `%1`/`%10` finished their turns and showed the *identical* idle tokens (`/ commands · ? help · space hold to record`) as a fresh session — IDLE detection is turn/repo-independent.
- [ ] 7.2 Capture a Copilot free-text question (no numbered list) to decide whether it counts as WAITING and what token identifies it; add a fixture + scenario if so
  - DEFERRED: cannot force a pure free-text prompt without sending input to a live pane, which violates the read-only guarantee. Known gap — a free-text question with no numbered list and no `esc to cancel` footer would currently classify as UNKNOWN. Close opportunistically when such a prompt occurs naturally.

## 8. Packaging & docs

- [x] 8.1 Add a `--calibrate`/self-test mode that prints the current classification of all live Copilot panes to ease re-deriving tokens after a Copilot upgrade
- [x] 8.2 Write a short README: prerequisites (psmux on PATH, Copilot version note), config options, and how to run
