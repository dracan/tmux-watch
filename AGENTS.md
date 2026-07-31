# Agent guide: tmux-watch

Canonical instructions for any coding agent working in this repo (Copilot CLI,
Claude Code, ...). Per-agent files (`CLAUDE.md`, `.github/copilot-instructions.md`)
point here so there is a single source of truth.

## What this is

`tmux-watch` is a C# / Spectre.Console console app - read-only toward pane *content*,
see "The tmux boundary" below - that watches
coding-agent CLI sessions (GitHub Copilot CLI and Claude Code) running in **tmux**
panes, classifies each pane (WAITING / WORKING / BACKGND / IDLE / DEAD) from its captured
status bar, and surfaces the ones needing the user - with a jump-to-pane action.
The monitor also derives a **DONE** state ("turn finished, your move") from the
WORKING→IDLE transition; it is not a classifier signal (the classifier is stateless)
but a per-pane state-machine promotion, acknowledged back to IDLE by a keystroke.
Agents are pluggable `AgentProfile`s (`src/TmuxWatch/Config/`).

**BACKGND** ("finished, but background work of its own is still running" - a shell, a
monitor, or a detached sub-agent) is the opposite: it *is* a classifier signal, because
"not blocked, not working, background task alive" is fully visible on one screen and needs
no history. It is quiet - no chime, no pointer cue - and it *defers*
the finished-turn announcement rather than cancelling it. A pane that goes WORKING→BACKGND
is marked as having an unannounced completed turn, released as DONE either when the task
exits (BACKGND→IDLE) or after a grace period (`backgroundGraceSeconds`, default 120) if
the task outlives it, so a `npm run dev` cannot swallow the chime forever. First sight
never announces: a pane discovered already in BACKGND completed no turn the watcher saw,
so neither exit fires - the same rule that keeps a freshly discovered IDLE pane out of
DONE.

Background **sub-agents** split across WORKING and BACKGND, and the line between them is
whether the agent can still take input:

- **Blocked** on them (`✻ Waiting for 2 background agents to finish` on the live status
  line) → WORKING. The composer is unusable; the pane is busy.
- **Detached** (the agent reports it is running in the background and hands the turn
  straight back) → BACKGND. The composer is free, so this is the same shape as a
  background shell: work outstanding, but the next move is nobody's until it reports.

Detached agents get their own fingerprint because they are invisible to the footer
counter: **agents are never counted in that slot.** A pane running one and nothing else
renders the ordinary `(shift+tab to cycle)` hint there. What the counter *does* pick up is
a sub-agent's own shells and monitors, aggregated into the parent pane's footer - which is
why this presented as a flicker rather than a clean miss. The pane read BACKGND only while
the sub-agent happened to be holding a shell, so the state was sampling the sub-agent's
incidental resource use, not the sub-agent. The real signal is the fleet panel Claude
renders below the footer: a `●` (U+25CF) `main` row that is always present, plus one `◯`
(U+25EF) row per live agent, removed when that agent reports. That removal is what makes
the fingerprint self-releasing - no timer, no history.

The grace period is deliberately *not* per-kind. A sub-agent always terminates, so unlike a
dev server it cannot swallow the chime forever; it can only outrun the 120s grace and chime
somewhat early. Splitting the timer would mean BACKGND carrying a reason through the state
machine, which is a real expansion for a cosmetic gain. Deferred, not foreclosed.

One ceiling worth knowing: the fleet panel costs one line per agent, and the classifier
scans the last 16 non-blank lines. A live capture put the composer 8 lines from the end
with one agent running, so there is roughly eight agents' headroom before the composer
falls out of the scan window and the pane classifies **Unknown**. Widening the scan is not
the fix, and a visible Unknown is the better failure. WAITING no longer reads transcript
cursors when a composer is on screen (see "Prefer structure over affordance hints"), but
this is exactly the case where one is *not*: with the composer off the top of the window
there is no split to apply, cursors are read wherever they land, and a stale `❯ 1.` in the
transcript would show a busy pane as blocking on you. Widening only makes that likelier.

The TUI also lists the panes that run *no* agent, in a separate "Other panes" table.
These rows are inert inventory: never captured, classified, tracked, notified on, or
able to move the pointer cue. They exist so the watcher can double as a jump target
for the whole tmux server. They ride along on the enumeration discovery already
performs, so listing them costs no extra tmux call.

## Prerequisites

- **.NET 10 SDK**.
- **tmux** on PATH (a `psmux`/Windows host also works via the `tmuxExecutable` config key).

## Build / test / run

```sh
dotnet build
dotnet test                 # full suite
./go.sh                     # run the live TUI (go.ps1 on Windows/PowerShell)
./go.sh --once              # one-shot snapshot
./go.sh --calibrate         # classify all live panes (use to derive tokens)
```

## Keys in the live TUI

| Key | Action |
| --- | --- |
| up / down | Move the highlighted row (spans every table as one list) |
| enter | Switch to the highlighted row's pane |
| `1`-`9`, then shift+`A`-`Z` | Switch to that row directly (numbering is continuous across tables) |
| `a` | Acknowledge the highlighted row when it is a DONE agent pane |
| `n` | New window in the focused pane's session - prompts inline for a name, then jumps to it |
| `p` | Pause / resume the highlighted row (works on non-agent rows as decluttering) |
| `o` | Show / hide the Other panes table (default: shown) |
| `c` | Include / exclude companion panes - non-agent panes sharing a window with an agent (default: included) |
| `w` | Wide mode: show the Path and Loc columns |
| `q` / esc | Quit |

`p` and `a` act on the **highlighted** row, not on the pane tmux happens to have
focused. The `►` marker is never the target of a *row* action; `n` is the one key that
reads it, and reads it only for the **session** it names, not to act on the marked pane.
Because tmux tracks the active window and pane per session, several rows can carry `►` at
once; `n` breaks the tie toward the highlighted row's session. The `o`, `c`, and `w`
toggles are runtime-only and reset to their defaults on each launch - they have no config
key.

While the `n` prompt is open it is **modal**: every keystroke goes to the line editor
(`src/TmuxWatch/Tui/LineEditor.cs`), so no command or address key fires and `q` is just a
character. The editor is a pure `(text, cursor)` state machine so its whole key table is
unit-tested without a console - Spectre's own `TextPrompt` cannot be used here, as it
throws when invoked inside an active `AnsiConsole.Live` display.

## The tmux boundary (three tiers)

What tmux-watch may do to tmux is bounded in three tiers. The first is absolute; the
other two are narrow and enumerated. The tmux access layer
(`src/TmuxWatch/Tmux/TmuxRunner.cs`) enforces them with a verb whitelist - preserve this
by construction.

**1. Inviolable - a pane's content is read-only, permanently.** tmux-watch MUST NEVER
send input to a pane. `send-keys`, `paste-buffer`, `send-prefix`, `run-shell`, or
anything else that injects input or executes a command must never be added to the
whitelist or invoked. This tier never widens, for any feature, ever.

**2. Focus.** `switch-client`, `select-window`, `select-pane` - moving the watcher's own
client between sessions and windows, and selecting the active pane within a window.

`select-pane` is the one focus verb whose effect is *not* confined to the watcher's own
client: it changes a window's active pane, which other clients can observe. It is allowed
because rows are per-pane and a jump must land on the pane the row names rather than on
whichever pane that window last had active; it injects no input.

**3. Lifecycle.** tmux-watch may create - and, if such keys are ever added, rename or
destroy - windows and panes. Today the only lifecycle verb is `new-window` (the `n` key).
Every lifecycle action is bound by three rules:

- **Keystroke-driven only.** It happens in direct response to an explicit keypress naming
  its target. Never on a timer, never from the poll loop, never as a side effect of
  discovery or classification. This is what keeps a watcher a watcher: no future feature
  gets to reap dead panes on its own initiative.
- **User text never reaches a command position.** `new-window` accepts a trailing shell
  command; the typed window name goes to `-n` and nowhere else. `TmuxRunner.NewWindow`
  builds its own argument list precisely so no caller can append one.
- **Destructive verbs need a confirmation step.** `new-window` is additive, so it has
  none. A future `close`/`kill` would be the first destructive verb and must not ship
  without one.

The permitted set is therefore `lsp`, `capture-pane`, `display-message`, `switch-client`,
`select-window`, `select-pane`, and `new-window`. Adding to it needs the same scrutiny:
which tier it belongs to, why the tier's rules are satisfied, and a note here.

The **pointer signal** (`src/TmuxWatch/Pointer/`) recolours the OS mouse pointer
while a pane waits. This is a *different* boundary from the three tiers above:
it never touches a watched pane, but it does mutate the watcher's own OS
environment (global desktop pointer). It is on by default (disable via
`pointerSignal.enabled` in config) and must stay crash-safe (restore on exit and
unconditionally on startup) and a no-op on unsupported hosts; it must never become
a channel that reaches a pane.

## OpenSpec workflow

This repo uses OpenSpec for non-trivial changes. Changes live in
`openspec/changes/<name>/` (proposal.md, design.md, specs/, tasks.md).

- List / inspect: `openspec list`, `openspec validate <change>`.
- Invoke the bare `openspec` CLI (not `npx`/`pnpm dlx`); change names start with a letter.
- The propose / apply / archive / explore skills are available to both agents
  (see "Agent harness parity" below). Capture decisions in the artifacts; don't
  implement during explore.

## Detection tokens are data, not code

Classification keys on per-agent status-bar tokens held in `AgentProfile`s, not
hardcoded constants. They are agent/version-specific - verify against real captures
(`--calibrate`) and keep fixtures in `tests/TmuxWatch.Tests/fixtures/`. Claude Code
tokens are build-specific and have changed before (the current build's WORKING
detection keys on the live spinner line, not the dropped `esc to interrupt` marker);
re-run `--calibrate` and update the `claude` profile after a Claude Code upgrade.

**Prefer structure over affordance hints.** Claude's status footer composes conditional
segments, and it recycles their slots: `? for shortcuts` renders only while the composer
is empty, and `(shift+tab to cycle)` is displaced whenever the footer needs room -
including by the background-shell counter. Claude IDLE used to key on those two hints, and
a pane that hit both conditions at once had no IDLE signal left, classified Unknown, and
so became ineligible for DONE and never chimed. IDLE now anchors on the **composer box**
(`IdlePromptPattern`: a `❯` at line start that is not the `❯ 1.` selection cursor), which
is the input caret rather than a hint - present in every live state, identical in vim
insert and normal modes, and not a slot the agent has reason to reuse. This is the same
class of drift as the vanished `esc to interrupt`; when adding a token, ask whether it
names structure or an affordance, and reach for structure.

That anchor also splits the screen: **transcript above the composer, chrome below it.**
Both BACKGND patterns are matched only below, because the transcript keeps prose that
either never meant a live task (`Ran 1 shell command`) or has outlived one - `· 1 shell
still running` frozen there after the shell exited, `Running in the background as @name`
frozen there for the rest of the session after the sub-agent reported. Matching those
would pin a pane in BACKGND permanently - trading the Unknown bug for a stuck-state one.
The composer is found by the **last** `❯` on screen for the same reason: the user's own
submitted prompts are echoed into the transcript with the identical glyph, and taking the
first match would put the real chrome on the transcript side of the split.

The **WAITING cursor** (`WaitingCursorPattern`) is matched below the split too, and the
reason is the mirror image: not prose that outlived a live task, but prose that was never
one. An agent that *writes* `❯ 1.` - in a recap, a diff, a quoted screen, this very
paragraph - is not waiting on anybody, and because WAITING outranks IDLE the pane was
pinned there for as long as the text stayed on screen: no DONE, no chime, and a row that
looked like it was blocking on you. The position rule costs a genuine prompt nothing,
because the prompt box **replaces** the composer rather than stacking above it (true of
all four WAITING fixtures) - so a real prompt finds no composer, no split to apply, and
the cursor is read wherever it lands. The two halves are one rule: below the composer, or
anywhere when there is none.

The footer half of WAITING (`↑/↓` + `esc to cancel` on one line) is deliberately **left**
whole-screen, and the asymmetry is the safety net. Splitting it too would gain little -
that pair is far less likely than a `❯ 1.` to show up in prose - and would forfeit the
one check still standing if some prompt does render a composer above its menu. The only
form carrying no such footer is the bare permission box, which is the form most clearly
confirmed to replace the composer.

`BackgroundTaskPattern` deliberately covers **both** kinds of background task Claude
reports in the footer slot - `· 2 shells · 1 monitor ·` - not shells alone. A monitor-only
pane displaces the `(shift+tab to cycle)` hint identically, and matching one kind but not
the other would leave it looking plainly IDLE and chiming immediately instead of
deferring. When Claude adds a third kind to that list, it belongs in this pattern too.

`BackgroundAgentRowPattern` is the second BACKGND fingerprint and stays a **separate key**
rather than an alternation inside the first. They are different objects on different UI
surfaces - a middot-delimited footer segment versus a row in a panel - so one regex
describing both would make the first pattern's careful "segment, not prose" framing untrue,
and the two will drift on separate Claude release schedules. It matches the `◯` bullet
rather than the row's trailing meter (`2m 5s · ↓ 104.3k tokens`): that meter is paren-less
unlike `WorkingLiveMeterPattern` - which is exactly why such a pane fell through to IDLE
instead of tripping WORKING - and a just-launched agent renders a bare `0s` with no
separator at all, so a meter-shaped token would miss the opening seconds of every agent.
Structure again, not decoration. Note that `← for agents` in the footer is **not** a
signal: it renders whether or not any agent is running.

## Buffered stdout is load-bearing

`WatcherApp.Run` replaces `Console.Out` with a non-auto-flushing `StreamWriter` and
flushes once per frame in `Render`. This is not a micro-optimisation: Spectre emits a
repaint as several hundred small writes (~480 for the default layout, measured), and the
stock `Console.Out` auto-flushes, so each becomes its own write syscall. Over a WSL
console bridge that is hundreds of round trips per frame - unnoticeable at the 2s poll,
but felt directly as lag in the `n` prompt, where every keystroke repaints. CPU cost of
building and laying out the view is under 2ms and was never the bottleneck.

If you touch the render path: keep the flush, and keep `BufferStdout` running before the
first `AnsiConsole` use (it rebinds `AnsiConsole.Console` so ordering cannot silently
defeat it). Terminal detection is unaffected because `Console.SetOut` swaps only the sink,
not the underlying handle.

## Conventions

- Match the surrounding code's style; keep the classifier pure and fixture-tested.
- Plain ASCII in generated text (no em-dashes or curly quotes).
- Don't commit `bin/`/`obj/` (gitignored) or any real captured pane content -
  fixtures must be synthetic or scrubbed.

## Agent harness parity

OpenSpec workflow skills are duplicated per agent: `.claude/skills` +
`.claude/commands/opsx` (Claude Code) and `.github/skills` + `.github/prompts`
(Copilot CLI). When you change a workflow skill for one agent, update the other
agent's equivalent so they stay in parity.
