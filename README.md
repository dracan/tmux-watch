# tmux-watch

After an agent CLI upgrade, ask your coding agent: **"Refresh agent detection."**
The [refresh-agent-detection skill](.claude/skills/refresh-agent-detection/SKILL.md)
runs the checks, investigates failures, fixes verified drift, validates, and
commits/pushes on main. Name an agent to focus it, or supply a previous report.
A clean run makes no tracked changes. It asks for help only when needed, such as
installing a missing CLI or logging in.

Explicit invocation: `$refresh-agent-detection` in Codex or
`/refresh-agent-detection` in Claude Code. Copilot has the matching repository
skill and prompt. Start a new agent session if the new skill is not discovered.
For manual commands and coverage limits, see the
[agent calibration harness](tools/TmuxWatch.Calibration/README.md).

To collect evidence on another machine, ask **"Dump agent diagnostics"**, or
invoke `$dump-agent-diagnostics` in Codex or `/dump-agent-diagnostics` in Claude
Code. The [repo-local skill](.claude/skills/dump-agent-diagnostics/SKILL.md) saves
a Markdown report, screen samples, pane metadata, versions, and detector context
under gitignored `.diagnostic-runs/`. Copilot has the matching skill and prompt.
Name a pane or agent to narrow collection. Review the files for client material,
then copy the whole run directory back for investigation. Collection is read-only
and does not launch test sessions or change detection.

A watcher that tells you which **coding-agent** sessions - **GitHub Copilot CLI**,
**Claude Code**, and **Codex CLI** - running inside **tmux** panes need your attention, and lets
you jump straight to them. It never types into a pane; watching is entirely
read-only.

When you run several agent sessions across tmux panes, it is easy to lose track
of which ones have stopped and are blocked waiting for you (a command/permission
prompt or a question). `tmux-watch` polls the panes read-only, classifies each
one against its agent's profile, and surfaces the ones that need you.

Agents are pluggable **profiles** (how to recognise the agent's panes and the
status-bar tokens that mark each state). Copilot, Claude Code, and Codex ship built in;
a new agent is a config change, not a code change.

## How it works

Each poll:

1. Enumerates panes once via `tmux lsp -a -F …` (read-only).
2. Matches each pane to an agent profile by foreground command
   (`copilot` / `claude` / `codex`), with an optional session-name backstop.
3. Captures each matched pane with `capture-pane -p` (read-only) and classifies it
   from its status bar using that agent's tokens (Copilot shown below; Claude Code
   uses its own - numbered `❯ N.` permission cursor, a live status line while working
   (a `(32s · …)` activity meter or a background-sub-agents wait, matched
   independently of the animated spinner glyph, plus a glyph + ellipsis fallback for
   the pre-meter moment), and the input-box mode line when idle):

   | State | Signal at the bottom of the pane |
   |-------|----------------------------------|
   | **WAITING** | numbered cursor `❯ 1.` **or** a footer with `↑/↓` + `esc to cancel` (covers command-approval *and* `ask_user`) |
   | **WORKING** | spinner glyph (`◎ ◉ ● ○`) + the word `Working`, **or** a spinner glyph + the action label + `esc cancel` footer (newer builds show the current action instead of `Working`) |
   | **DONE** | *derived, not a bottom-of-pane token:* the monitor promotes a pane to DONE on the WORKING→IDLE transition (a finished turn - "your move"); see below |
   | **BACKGND** | the agent's own background-task counter in the chrome below the composer (`· 1 shell ·`, `· 2 monitors ·`), with no selection prompt and no live spinner: the turn is over but the work it started is not; see below |
   | **IDLE** | the composer box - a `❯` prompt that is not the `❯ 1.` selection cursor (Copilot: input box + `/ commands · ? help · space hold to record`) |
   | **DEAD** | foreground command is no longer `copilot`, or the pane is dead |

   **DONE ("your move").** A finished turn drops the agent to the same idle input box
   as a long-untouched pane, so "just finished (e.g. an `openspec propose` completed -
   run `openspec apply` next)" is indistinguishable from "genuinely idle" by capture
   alone. The monitor derives **DONE** from the WORKING→IDLE transition instead: DONE
   sorts just below WAITING, rings the same bell on entry, and (with the pointer signal
   on) turns the pointer green. Acknowledge a DONE pane back to IDLE by switching to it
   (its number key) or pressing **`a`** on the focused row; acknowledgement is
   keystroke-driven, so a pane that already has focus never self-acknowledges.

   **BACKGND ("finished, but its work isn't").** When a turn ends while a background task
   the agent started is still running - a shell, or a monitor; Claude counts both in the
   same footer slot - the work isn't really done, so the pane shows a quiet **`backgnd`**
   instead of chiming, and the DONE announcement is *deferred*. It fires when the task
   exits, or after `backgroundGraceSeconds` (default 120) if the task never does, so a
   `npm run dev` can't swallow the chime forever. `backgnd` sorts below DONE and above
   `working`, raises no notification and no pointer cue, and cannot be acknowledged -
   there is nothing to acknowledge until it becomes DONE. A pane already in `backgnd` when
   the watcher starts stays silent by either route: no completed turn was observed, so
   there is nothing to announce.

The foreground-command match is extension-insensitive, so a Windows/psmux host
(`pane_current_command` reports `copilot.exe`) is detected the same as the bare
`copilot` command on Linux/macOS.

On native Windows, psmux can report a child tool such as `tgrep` instead of the
agent that owns the pane. When the command does not match, discovery checks the
live process tree rooted at `pane_pid`, using one read-only Windows process
snapshot per enumeration. A single verified agent owner keeps the pane in the
agent table while its tools run. Ownership is checked afresh each poll, stops at
other pane roots, and rejects ambiguous owners and invalid process lifetimes.
It also preserves ownership when an update renames an agent's running executable.
The reported command is retained for diagnostics; attention states still come
from the agent's screen. Calibration and normal monitoring share this discovery.

This fallback identifies configured agent executables, not agents hidden behind
a generic `node` process. Those still need a command match or the configured
session-name convention. If Windows cannot verify a process, discovery falls
back to command and naming matches. Unix hosts keep their existing discovery.

4. Drives a per-pane state machine and **notifies once** when a pane *enters*
   WAITING, and once when a pane *enters* DONE (both edge-triggered, not every poll).

> **Read-only guarantee:** tmux-watch never sends keystrokes to a pane. A pane's
> *content* is read-only, permanently - `send-keys` (and anything like it) cannot
> be invoked. The access layer permits read-only inspection, focus changes, and
> explicitly requested window creation or workspace import. Import creates shells
> and restores their arrangement; it never executes saved resume commands. Nothing
> is created, renamed, or destroyed by polling, and partial imports are never
> automatically deleted. The exact lifecycle boundary is documented in AGENTS.md.


## Prerequisites

- **.NET 10 SDK** (or matching runtime).
- **tmux** on `PATH`. (A `psmux` or other tmux-compatible host also works - set
  `tmuxExecutable` in config; see below.)
- **Copilot CLI** - detection tokens were verified against `v1.0.63`. The status-bar
  wording is version-specific; if a future Copilot version changes it, run
  `--calibrate` and override the tokens in a config file (see below).

### Codex CLI

Codex panes are discovered automatically from `codex` or `codex.exe`. The profile
recognizes approval menus and question editors as WAITING, timed live status lines
as WORKING, and the input composer as IDLE. The existing monitor promotes an
observed WORKING-to-IDLE transition to DONE and supports the same chime,
acknowledgement, and jump actions as the other agents.

Free-text questions and notes editors share the normal composer caret, and their
footer can also say `esc to interrupt`. Codex detection therefore checks the
submit-answer or submit-all footer before looking for a complete timed working
line. Old question footers above the last composer are ignored.

Core states were checked on Codex CLI **0.153.4** and **0.155.1**. Approval and
question fixtures also use pinned upstream renderer snapshots; see
[fixture provenance](tests/TmuxWatch.Tests/fixtures/codex-provenance.md).
Custom keybindings or later UI changes may require profile overrides.

Codex background terminals classify BACKGND only when the complete live terminal
control line sits immediately above the last composer. Detached agents have no
verified live indicator in the inspected UI, so their completion is not tracked.
A terminal count cannot establish whether an invisible subagent is also running.

Run `./calibrate.sh check --unattended` after agent upgrades to exercise the
supported live scenarios at both terminal widths. The report lists capability
exclusions. Use `./calibrate.sh run` for the full diagnostic catalog; see the
[harness guide](tools/TmuxWatch.Calibration/README.md) for scope and evidence.

## Usage

When a table contains multiple coding-agent types, its Window cells show **CC**
(Claude Code), **GHCP** (GitHub Copilot), or **CDX** (Codex) before the window name.
Custom agents use their configured profile id. A table containing just one agent
type shows no prefixes. The main and Paused tables decide independently; non-agent
panes neither count toward the decision nor receive a prefix. Prefixed window names
stay on one line and may be truncated in a narrow split.

```sh
# Live TUI: watch all agent panes, sorted with WAITING at the top.
./go.sh

# One-shot snapshot (no TUI), useful for scripts.
./go.sh --once

# Self-test: classify every live pane and show its status tail.
./go.sh --calibrate
```

`go.sh` is a thin wrapper over `dotnet run --project src/TmuxWatch -- "$@"`; a
`go.ps1` is kept for a Windows/PowerShell host. You can also invoke `dotnet run`
directly.

### Keys

| Key | Action |
| --- | --- |
| up / down | Move the highlighted row (spans every table as one list) |
| enter | Switch to the highlighted row's pane |
| `1`-`9`, then shift+`A`-`Z` | Switch to that row directly (numbering is continuous across tables) |
| `a` | Acknowledge the highlighted row when it is a DONE agent pane (clears it back to IDLE without switching) |
| `n` | New window in the focused pane's session - prompts for a name, then jumps to it |
| `p` | Pause / resume the highlighted row (shared and persistent) |
| `e` | Save the workspace and copy its JSON to the clipboard |
| `i` | Import a workspace file; submit an empty path to use the clipboard |
| `o` | Show / hide the Other panes table (default: shown) |
| `c` | Include / exclude companion panes (default: included) |
| `w` | Wide mode: show the Path and Loc columns |
| `q` / `Esc` | Quit |

`p` and `a` act on the **highlighted** row, not on the pane tmux happens to have
focused. The `►` marker is never the target of a row action; `n` is the only key that
reads it, and only for the session it names.

Acknowledging a pane - by jumping to it or with `a` - would normally drop it four ranks
down the table, in the same frame as your keystroke and before tmux has reported anything.
So the row **keeps its position for `ackHoldSeconds`** (default 5) instead, and moves only
on a later poll. Its state, colour, and cue update immediately; only the position waits.
That keeps the address keys you just read off the screen pointing at the same panes for
the next few seconds. Set `ackHoldSeconds` to `0` to demote at once.

Pressing `n` opens a name prompt under the tables - the tables stay up and keep
refreshing while you type. It supports cursor editing (left/right, home/end, backspace,
delete) plus `ctrl+w` to delete the previous word and `ctrl+u` to clear the line. Enter
creates the window, `Esc` cancels. An empty name lets tmux name the window itself. While
the prompt is open every key goes to it, so `q` types a `q` rather than quitting.

Wide mode reveals the **Path** and **Loc** columns, which are hidden by default
so the table fits a thin terminal split.

Pausing parks a pane in a separate **Paused** table below the main list, so the
top table stays focused on the sessions you are actively working on. Bring it
back by highlighting it and pressing `p` again.

The `o`, `c` and `w` toggles reset on each launch. Pause settings are shared
between watchers of the same panes and survive watcher restarts. Recycled pane
IDs do not inherit old settings; imports transfer them onto the new panes.

### Save a workspace before reboot

Press `e`, or run:

```sh
./go.sh --export
./go.sh --export /path/to/workspace.json
```

Export includes every pane, even ones hidden or paused in the TUI, with session
and window names, split layouts, directories, agent types, and paused settings.
It saves readable JSON before copying the identical document to the clipboard.
If no clipboard helper works, the file is still saved and its path is reported.
An explicitly chosen existing file is not overwritten. Export also reports known
restore blockers, so you can resolve them before rebooting.

After reboot, run the watcher outside the sessions you want to restore, then
press `i` and enter the saved file path (or leave it empty for clipboard input).
Terminal commands also work:

```sh
./go.sh --import /path/to/workspace.json
./go.sh --import-clipboard
cat /path/to/workspace.json | ./go.sh --import -
```

Use the same options with `go.ps1` on Windows/psmux. Import recreates shells in
the saved directories, with the saved split arrangement and paused settings.
Pane sizes may scale or round. It provides manual resume guidance in a saved
report; it does not restart agents, dev servers, or other programs.

Conversation IDs are explicitly **unknown** until an agent/version has a verified
read-only resolver for its selected conversation. This release does not claim a
verified resolver for Copilot, Claude Code, or Codex. It installs no hooks and
reads no transcripts. A known UUID supplied in an edited snapshot produces manual
resume guidance for the corresponding agent; it is never executed by the watcher.
Agent conversation storage itself is not backed up by this feature.

Import stops before creation if names conflict, directories are missing, or the
snapshot has unsupported structure. Psmux requires contiguous window indexes
starting at zero. Linked or zoomed windows are currently
unsupported for import: unzoom before exporting, and restore linked windows
manually. Names and paths containing control characters, semicolons, or tmux
format expressions are rejected. If creation fails after preflight, completed
resources remain and the report explains the failure. Resolve those sessions
before retrying, because a repeated import will report the name conflict.

Default snapshots and restore reports live under `tmux-watch` in the user's local
application-data directory (`~/.local/share` on Linux, `%LOCALAPPDATA%` on Windows).
Pause records live alongside them. Keep the snapshot file somewhere that survives
your reboot; clipboard contents alone are not a durable backup. This is intended
for restoring on the same machine, not translating paths between Windows and Unix.

### Other panes

Panes that are *not* running a coding agent are listed in a separate **Other
panes** table, so the watcher doubles as a jump target for the whole tmux server.
These rows are inert inventory - never captured, classified, tracked, notified
on, or able to move the pointer cue - and they ride along on the enumeration
discovery already performs, so listing them costs no extra tmux call. Their
**Command** column shows the reported foreground process. **Quiet for** shows
time since the *window's* last activity on supported hosts (the figure is shared
across a split). It is not the age of the pane or its process.

Native Windows/psmux displays `-` for this activity time. In psmux 3.3.8,
`window_activity` actually returns session creation time, so even a new window
can appear hours old. Activity remains unavailable for native Windows (including
the `tmux.exe` alias) and explicitly named psmux clients until reliable support
is verified. Agent **In state** timers are independent and continue working.

Paused tables containing only non-agent rows use the same headings. A mixed
paused table uses **State / Cmd** and **Age**, with a caption explaining the
different ages. Agent-only tables retain **State** and **In state**.

`c` controls whether **companion panes** - non-agent panes that share a window
with an agent - appear in that table.

### Options

| Option | Description |
|--------|-------------|
| `--config <path>` | Load a JSON config (tokens, interval, notifications) |
| `--interval <sec>` | Poll interval override (default 2s) |
| `--calibrate` | Print classification of all live panes and exit |
| `--once` | Print one classification snapshot and exit |
| `-h`, `--help` | Show help |

## Configuration

Behaviour and all per-agent detection tokens are configurable, so an agent
version bump is a config change, not a code change. When no `agents` list is
given, the built-in `copilot`, `claude`, and `codex` profiles are used. A non-empty
`agents` list replaces those defaults, so include every agent you want to watch. Pass
`--config config.json` to override:

```json
{
  "tmuxExecutable": "tmux",
  "pollIntervalSeconds": 2.0,
  "notificationChannel": "bell",
  "statusLineCount": 16,
  "backgroundGraceSeconds": 120.0,
  "ackHoldSeconds": 5.0,
  "pointerSignal": {
    "enabled": true,
    "waitingCursorFile": "assets/waiting-cursor.cur",
    "doneCursorFile": "assets/done-cursor.cur",
    "shapes": ["arrow", "ibeam"]
  },
  "agents": [
    {
      "id": "copilot",
      "command": "copilot",
      "sessionNameConvention": "^cop-",
      "waitingCursorPattern": "❯\\s*\\d+\\.",
      "waitingFooterNavMarker": "↑/↓",
      "waitingFooterCancelMarker": "esc to cancel",
      "workingSpinnerGlyphs": "◎◉●○",
      "workingWord": "Working",
      "workingFooterCancelMarker": "esc cancel",
      "idleHints": ["/ commands", "? help", "space hold to record"]
    },
    {
      "id": "claude",
      "command": "claude",
      "workingSpinnerGlyphs": "✻✽✶✷✸✹✺✢✳∗",
      "workingWord": "",
      "workingLiveSpinnerSufficient": true,
      "workingLiveEllipsisPattern": "…|\\.\\.\\.",
      "workingLiveMeterPattern": "\\(\\d+[smh][^)]*·",
      "workingBackgroundAgentsPattern": "Waiting for \\d+ background agent",
      "idlePromptPattern": "^\\s*❯(?!\\s*\\d+\\.)",
      "backgroundTaskPattern": "·\\s*\\d+\\s+(shells?|monitors?)\\s*(·|$)",
      "idleHints": []
    },
    {
      "id": "codex",
      "command": "codex",
      "waitingCursorPattern": "^\\s*\\u203A\\s*\\d+\\.",
      "waitingFooterNavMarker": "",
      "waitingFooterCancelMarker": "",
      "waitingChromePattern": "^\\s*(?:Press enter to confirm or esc to (?:cancel|go back)|(?:[^|\\r\\n]+\\|\\s*)*enter to submit (?:answer|all)(?:\\s*\\|[^\\r\\n]*)?)\\s*$",
      "workingSpinnerGlyphs": "",
      "workingWord": "",
      "workingFooterCancelMarker": "",
      "workingLinePattern": "^\\s*[\\u2022\\u25E6]\\s+[^()\\r\\n]+\\(\\d+[smh](?:\\s+\\d+[smh])*\\s+\\u2022\\s+esc to interrupt\\)\\s*$",
      "idlePromptPattern": "^\\s*\\u203A(?!\\s*\\d+\\.)"
    }
  ]
}
```

`idlePromptPattern` is the composer anchor: when set it supersedes `idleHints` for that
profile, and it also splits the screen for `backgroundTaskPattern`, which is matched only
*below* the composer line. Copilot leaves both empty and keeps using `idleHints`.

`waitingChromePattern` is an optional blocking-footer regex searched below the
last composer, or throughout the scanned tail when no composer is present.
`workingLinePattern` is an optional regex matched against individual lines in
that tail. Both default to disabled; Codex uses them to distinguish question
editors from live timed work. The `\u203A` regex escape identifies Codex's caret.

`backgroundTaskBeforePromptPattern` is an optional complete-line regex matched
only on the last nonblank line immediately before the final composer. Codex uses
it for live terminal controls. Empty disables it; it never scans earlier prose.

Set `tmuxExecutable` to `psmux` (or another tmux-compatible CLI) to run against a
different multiplexer host.

### Pointer signal

`pointerSignal` turns the real Windows mouse pointer a **signal colour across the
whole desktop** for as long as any *non-paused* watched pane needs you - **red**
while any pane is WAITING, **green** while any pane is DONE (a finished turn) and none
is WAITING - restoring the normal pointer once nothing needs you. It is a persistent
ambient reminder that outlasts the one-shot bell, visible even when the terminal is
minimised. WAITING (red) takes precedence over DONE (green) when both are present. It
is **on by default**; set `enabled: false` to turn it off.

- **Paused panes are excluded.** Panes you have parked (pressed `p`, moved to the
  secondary table) do not trigger the pointer - so pausing a waiting pane clears
  the cue and resuming it re-arms it.

- **Scope is global.** Every application shows the red pointer while a pane waits.
  This is intentional - it is the whole point of the cue.
- **Out-of-band.** The pointer is changed via an OS call, not a terminal escape
  sequence, so it behaves identically whether tmux-watch runs inside or outside
  tmux/PSMUX. It works on native Windows (direct `user32` call) and under WSL
  (via `powershell.exe`); on any other host it is a no-op.
- **Crash-safe.** The normal pointer is restored on exit and, defensively, again
  on every startup - so a pointer left red by an abnormal exit self-heals on the
  next launch.
- `waitingCursorFile` and `doneCursorFile` are the shipped red and green arrow assets
  (relative paths resolve against the app directory); `shapes` chooses which pointer
  shapes to recolour (`arrow` covers other apps, `ibeam` covers the terminal's text
  area).

This only ever mutates the watcher's own OS environment; it does not touch
watched panes and does not affect the read-only guarantee.

> **Claude Code tokens are build-specific.** The `claude` WORKING/WAITING/IDLE
> tokens above were verified by `--calibrate` against a live pane, but Claude Code
> has changed its status bar before (an earlier build keyed WORKING on an
> `esc to interrupt` marker that the current build dropped). If a future build
> changes the status bar again, run `--calibrate` against a live pane and override
> the affected tokens in the `claude` profile above.
>
> This is also why IDLE anchors on the **composer box** rather than on a footer hint.
> Claude's footer segments are conditional and share slots: `? for shortcuts` shows only
> while the composer is empty, and `(shift+tab to cycle)` is displaced by the
> background-shell counter. A pane hitting both at once had no IDLE signal left and
> classified Unknown - which also made it ineligible for DONE, so it never chimed. The
> composer prompt is structure rather than an affordance hint, so it survives that churn.

## Tests

```sh
dotnet test
```

The classifier is pure and tested against **pane fixtures**
(`tests/TmuxWatch.Tests/fixtures/`): Copilot command-approval WAITING, `ask_user`
WAITING, WORKING, IDLE, a lazygit control (must not be flagged), and Claude Code
WAITING/WORKING/IDLE/BACKGND. The Copilot fixtures are real captures (scrubbed of
content); the Claude fixtures are synthetic shells built around the real status-bar
tokens. The BACKGND fixture deliberately carries a frozen `· 1 shell still running`
line in its transcript - the shell segment must be read from the chrome below the
composer only, since that transcript line survives the shell itself.

## Topology note

The "switch to pane" action moves the attached client's focus. Run tmux-watch as a
**separate terminal/client** attached to the same tmux server rather than inside a
watched pane, so jumping to a pane doesn't move the watcher off its own screen.
