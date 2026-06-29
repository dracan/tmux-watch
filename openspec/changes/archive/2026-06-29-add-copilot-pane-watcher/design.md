## Context

`tmux-watch` watches GitHub Copilot CLI sessions running inside psmux panes and tells the user which sessions need attention. The motivation and capabilities are described in `proposal.md`; the normative behaviour is in `specs/`.

This design is grounded in live investigation against a real psmux server (v3.3.6) running Copilot CLI (`v1.0.63`). Key environmental facts were verified by capturing real panes read-only, not assumed:

- **psmux exposes no usable "output activity" signal in this build.** The `window_activity` format variable is frozen (it did not advance when panes produced output), and `monitor-activity` / `monitor-silence` flags are never set for a window that is the *current* window of its session — which every single-window Copilot session always is. These signals are therefore unusable.
- **Copilot CLI does not use the alternate screen.** `capture-pane -p` returns the scrollback with the prompt/status drawn inline at the bottom, so the bottom few lines are a reliable status bar.
- **Each Copilot state has a distinct, positive status-bar fingerprint** (verified across multiple live panes), so classification needs literal token matching, not fragile frame-diffing.

Verified fingerprints:

| State | Positive signal at bottom of `capture-pane -p` |
|-------|------------------------------------------------|
| WAITING (command approval) | `❯ 1. Yes` + footer `↑/↓ to navigate · enter to select · esc to cancel` |
| WAITING (`ask_user` choice) | numbered options + footer `↑/↓ to select · enter to confirm · esc to cancel` |
| WORKING | animated spinner `◎/◉/●/○` + word `Working` (e.g. `◎ Working   esc cancel`) |
| IDLE | input box `❯` + status `/ commands · ? help · space hold to record` |
| DEAD | foreground command ≠ `copilot`, or `pane_dead == 1` |
| (control) lazygit TUI | scored **0** WAITING matches — no false positive |

The two WAITING footers differ in wording (`navigate`/`select` vs `select`/`confirm`); only `esc to cancel` and the `❯<digit>.` cursor are common to both. The classifier must key on those invariants.

## Goals / Non-Goals

**Goals:**
- Reliably distinguish WAITING / WORKING / IDLE / DEAD for Copilot panes from read-only captures.
- Notify the user once, on the edge into WAITING, while looking at another window.
- Let the user jump the terminal focus to a pane that needs them.
- Stay strictly read-only toward watched panes (never inject input).
- Keep classification tokens configurable so a Copilot version bump is a config change, not a code change.

**Non-Goals:**
- Driving or automating Copilot sessions (no `send-keys` to watched panes).
- Supporting multiplexers other than psmux, or non-Copilot workloads, in this iteration.
- Parsing Copilot conversation content/semantics beyond the status-bar affordances.
- Cross-platform support beyond the Windows/PowerShell + psmux target.

## Decisions

### D1 — Detect via `capture-pane -p`, not activity/monitor signals
psmux activity timestamps and monitor flags proved inert for the target topology (see Context). `capture-pane -p` is the only dependable read primitive. *Alternative considered:* `window_activity`/`monitor-silence` polling — rejected because verified non-functional here.

### D2 — Classify on positive status-bar tokens, not frame-diff stability
Every state carries its own literal token (`Working`, `/ commands`, `esc to cancel`), so classification is direct string/regex matching on the bottom ~4 non-blank lines. Frame-stability hashing is retained only as an `UNKNOWN`-state tiebreaker. *Alternative considered:* hash the frame and infer "quiet = waiting" — rejected because (a) it can't tell IDLE from a paused WORKING pane, and (b) the pwsh prompt's volatile clock/RAM tokens poison hashing generally; Copilot's own status bar happens to be stable but token matching is simpler and more semantic.

### D3 — Key WAITING on invariants common to both prompt types
Match `❯\s*\d+\.` OR (a line containing both `↑/↓` and `esc to cancel`). Explicitly do **not** match only `enter to select`/`to navigate`, which are absent from `ask_user` prompts. This was the concrete trap a naive implementation would hit; it is encoded as a spec scenario.

### D4 — Identify Copilot panes by foreground command, with a naming backstop
`pane_current_command == copilot` is the primary filter (verified to report `copilot` when in the foreground). A configurable session-name convention (e.g. `cop-*`) is an optional backstop for cases where the foreground process briefly differs. *Alternative considered:* naming only — rejected as more brittle and requiring user discipline.

### D5 — Edge-triggered per-pane state machine
Hold last state + entered-at per pane id; emit an attention event only on transition into WAITING. This converts a per-poll level signal into a single notification per occurrence and is what keeps the tool trustworthy. IDLE-edge events are optional/lower priority.

### D6 — Watcher runs as an external client, not inside a watched psmux pane
The jump action uses `switch-client` / `select-window`, which moves the attached client's focus. If the watcher lived inside a psmux pane it would relinquish its own screen on every jump and need a bounce-back key in every session. Running it as a separate terminal/client that talks to the server over the socket avoids this entirely. *Alternative considered:* watcher-in-a-pane with a global bounce-back binding — rejected as fiddlier UX.

### D7 — Shell out to the `psmux` CLI via `ProcessStartInfo` with `ArgumentList`
No native API exists; the CLI is the interface. Use `ArgumentList` (not a joined command string) to avoid quoting issues. Each poll does **one** `lsp -a -F` enumeration, then one `capture-pane -p` per Copilot pane, to bound process spawns at N+1 per tick.

### D8 — All match tokens are configuration, defaults baked in
WAITING/WORKING/IDLE patterns, spinner glyph set, poll interval, and notification channel are config with sensible defaults derived from the verified fixtures. A `--calibrate`/self-test mode that dumps the classification of currently-running panes helps users re-derive tokens after a Copilot upgrade.

## Risks / Trade-offs

- **Copilot status-bar wording changes between versions** → tokens are configurable (D8); ship a calibration/self-test mode and keep defaults in one place. Captured fixtures double as regression tests.
- **Capture width wraps/truncates lines** (observed: a 112-col pane jammed the close-border onto the option line) → match on substrings/tokens, never on whole-line equality or fixed columns.
- **A paused WORKING pane could look quiet** → because IDLE requires its *own* positive token and WORKING shows the `Working` word + spinner, a pause is still classified WORKING; "quiet" alone never means WAITING (encoded as a spec scenario).
- **Per-poll process spawning cost** → one enumeration + one capture per Copilot pane per tick, default interval 1–2s; fine for a handful of panes, revisit only if N grows large.
- **`ask_user` free-text option** (`5. Other (type your answer)`) and other free-text prompts → still carry the selection footer while a choice list is shown, so they classify WAITING; a purely free-text question with no list is an unverified edge (see Open Questions).
- **Accidental writes to a watched pane** → enforce read-only by construction: the psmux-call layer whitelists verbs and forbids `send-keys` against watched pane ids (TUI may only call `switch-client`/`select-window`).

## Migration Plan

Greenfield project; nothing to migrate. Rollout is simply building and running the console app against an existing psmux server. "Rollback" is not running it — it makes no persistent changes to panes or the server beyond focus changes the user explicitly triggers.

## Open Questions

- **Notification channel**: OS toast vs terminal bell/sound vs taskbar flash (or several, configurable)? Needs a Windows-appropriate default.
- **Notify on IDLE too, or WAITING only?** WAITING is clearly in scope; IDLE-edge notification is optional and may be noisy — default off, opt-in.
- **IDLE token stability**: confirmed from one fresh-session fixture (`/ commands · ? help · space hold to record`). Verify it is identical for a session that *finishes a turn* (not just a fresh start) and across repos/themes.
- **Pure free-text prompt affordance**: capture a Copilot free-text question (no numbered list) to confirm whether it should count as WAITING and what token identifies it.
- **Selection mechanism in the TUI**: index keys, arrow + enter, or click — minor, decide during `watcher-tui` implementation.
