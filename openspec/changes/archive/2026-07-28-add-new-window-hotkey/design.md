## Context

`WatcherApp.Run` wraps its entire poll loop in a single `AnsiConsole.Live(...).Start(...)`
callback. Every key handler runs inside that callback, and the live display holds
Spectre's exclusivity lock for its whole duration. Adding a text prompt has to work
within, or deliberately around, that structure.

The tmux access layer (`TmuxRunner`) enforces a verb whitelist that has so far contained
only read and focus verbs. `new-window` is the first verb that changes tmux topology, so
the change is as much a boundary decision as a feature.

Constraints carried in from the explore session:

- The watcher runs in its own terminal, not a tmux pane - but must not stop working if it
  ever runs in one.
- The user never runs multiple tmux sessions, so multi-session behaviour needs to be
  *deterministic*, not *featureful*.
- Hand-rolled keystroke handling is acceptable only if it is properly tested.

## Goals / Non-Goals

**Goals:**

- Create a tmux window from the watcher without leaving the live view.
- Full mid-string editing of the name, because losing it "feels like a bug".
- Keep the tricky part (keystroke handling) verifiable by unit test.
- Restate the read-only boundary once, in a form that admits future rename/close/kill.

**Non-Goals:**

- Rename, close, or kill actions. Out of scope; the boundary anticipates them but this
  change adds none.
- Choosing a working directory for the new window, or a session picker. The target is
  derived, not chosen.
- A general-purpose text input component. This editor exists for one field.
- Multi-session ergonomics beyond a deterministic tie-break.

## Decisions

### Inline prompt inside the live view, not `AnsiConsole.Prompt`

`TextPrompt` cannot run inside an active `Live` display. Spectre's
`DefaultExclusivityMode` throws *"Trying to run one or more interactive functions
concurrently"* - confirmed present in the shipped 0.57.0 assembly
(`DefaultExclusivityMode.CreateExclusivityException`). Using it would mean restructuring
`Run` into an outer loop that exits `Live`, prompts, and re-enters:

```
   TextPrompt route                    Inline route (chosen)
   ────────────────                    ─────────────────────
   while (!quit) {                     Live.Start(ctx =>
     Live.Start(ctx =>                   while (!cancelled) {
       ... 'n' sets pending, returns       Tick(); Render(ctx)
     )                                     HandleKeys(ctx)   ← 'n' flips
     if (pending) Prompt()  ← LOCK       }                     to prompt mode
   }                          RELEASED  )
```

The inline route was chosen because it keeps the tables on screen, keeps polling running
while the user types, requires no restructuring of `Run`, and makes esc-to-cancel natural
(`TextPrompt` reads a line and has no native cancel). The cost is owning the keystroke
handling.

*Alternative considered*: a third-party readline library. Rejected - it would still not
render inside a `Live` display, and it is a dependency for one text field.

### The editor is a pure state machine

The reason hand-rolling is acceptable is that the editing logic contains no console
interaction at all. It is a value type carrying `(Text, Cursor)` and a transition
`Apply(ConsoleKeyInfo) -> new state + outcome`, where the outcome is one of
`Editing` / `Submit` / `Cancel`. This is the same shape as the existing pure statics in
`WatcherApp` (`AddressKey`, `IndexForAddressKey`, `TogglePause`, `ResolveHighlightIndex`),
all of which are unit-tested without a console.

Supported keys: printable characters (insert at cursor), backspace, delete, left, right,
home, end, `ctrl+w`, `ctrl+u`, enter, escape. Everything else is ignored. Bounds are
clamped rather than throwing, so a stray key can never produce an invalid state.

*Alternative considered*: append/backspace only (~30 lines instead of ~60). Rejected -
being unable to fix a typo mid-string reads as a defect, and the cursor is what makes
`ctrl+w` and home/end nearly free once present.

### Prompt mode is modal

While the prompt is open the key loop routes every keystroke to the editor and nothing
else. This dissolves all key-conflict questions: digits, `p`, `o`, `a`, and the arrows are
all just characters or cursor movements during entry. The poll loop continues, so the
tables refresh underneath.

### Target session captured on keypress

Because polling continues during entry, the focus marker can move while the user types.
Resolving the target on submit would let the window land somewhere the user did not
intend, with no signal that it happened. The target is therefore captured when `n` is
pressed and displayed in the prompt line.

Resolution order, all from data already on screen - no extra tmux call:

```
   1. the ► pane's session
      └─ if several rows carry ► (one per session, since tmux
         tracks window_active/pane_active per session):
         prefer the one matching the highlighted row's session
   2. no ► anywhere → the highlighted row's session
   3. no rows at all → do nothing
```

*Alternative considered*: `display-message -p '#{client_session}'`, which asks tmux
directly and is already a permitted verb. Rejected - it costs a tmux call per keypress and
only earns its keep in the multi-session case the user does not have. It remains the
upgrade path if that changes.

### Detached create, then explicit jump

`new-window` without `-d` both creates and switches the client. Splitting them with `-d`
plus an explicit jump gives three things: the create moves no client on its own; jumping
becomes an independent decision that can later be made optional; and behaviour is
identical whether or not the watcher itself runs inside tmux.

Two consequences surfaced during implementation and are worth recording:

- **`-d` means the new window is not current**, so switching to the session alone lands on
  whichever window was there before. The create therefore also passes `-P -F
  '#{window_id}'`, and the jump is `switch-client` followed by `select-window` on the
  reported id. A host that reports no id degrades to the session switch alone rather than
  selecting something wrong.
- **The optimistic focus update cannot mirror `Activate`'s.** `ApplyOptimisticFocus`
  rewrites the flags of an existing `Pane` record, and the new window has no record until
  the next poll enumerates it. What is achievable, and accurate, is the other half:
  clearing the marker from every row of the target session, since focus has demonstrably
  left all of them. The marker reappears on the new window's row at the next poll.

### Boundary restated as three tiers

"Close" and "kill" are on the horizon, so the rule cannot be "additive only". The line
that actually holds is *input, not topology* - a window manager legitimately creates and
destroys windows; what this tool must never do is type into one.

```
   ┌─ INVIOLABLE ────────────────────────────────────┐
   │ Never sends input to a pane.                    │
   │ No send-keys, no paste-buffer, no run-shell.    │
   │ A pane's CONTENT is read-only, permanently.     │
   ├─ FOCUS ─────────────────────────────────────────┤
   │ switch-client / select-window / select-pane     │
   ├─ LIFECYCLE (new) ───────────────────────────────┤
   │ May create - and later rename or destroy -      │
   │ windows and panes, but ONLY:                    │
   │   · on an explicit keystroke naming a target    │
   │   · never automatically, never from polling     │
   │   · user text never reaches a command position  │
   └─────────────────────────────────────────────────┘
```

"Never from polling" is the clause that keeps the watcher a watcher - no future feature
gets to reap dead panes on a timer.

### Name reaches `-n` and nothing else

`new-window` takes an optional trailing shell command. Any path that let typed text land
there would turn a text prompt into arbitrary execution. `NewWindow` on `ITmuxClient`
therefore takes a session and an optional name and constructs the argument list itself;
callers cannot supply extra arguments. `ProcessStartInfo.ArgumentList` handles quoting,
and because `-n` consumes its value, a name beginning with `-` is safe.

## Risks / Trade-offs

- **Hand-rolled editing diverges from readline habits** → the pure state machine is
  fixture-tested per key, and the field is a short window name displayed on screen before
  submission, so the blast radius of a mishandled key is a visible typo, not data loss.
- **`►` stops being purely passive**, contradicting a documented invariant → the invariant
  is narrowed rather than deleted: row actions still never target the marker; only the
  session is read from it. The spec records both halves explicitly.
- **`new-window` in the whitelist is a permanent widening** → mitigated by the tier
  structure, by `NewWindow` owning its own argument construction, and by a
  `TmuxRunnerTests` case asserting `send-keys` stays rejected alongside the new verb.
- **Polling continues during entry**, so the tables shift under the prompt while typing →
  accepted; it is preferable to a frozen view, and the captured target means the shifting
  cannot change where the window lands.
- **Non-ASCII input** via `Console.ReadKey` can be unreliable on some hosts → accepted;
  window names are conventionally ASCII, and unhandled characters are ignored rather than
  corrupting the buffer.
- **Multi-session tie-break is untested in real use** (the user runs one session) → it is
  covered by unit test rather than by field experience, and degrades to an on-screen,
  predictable choice rather than an arbitrary one.
