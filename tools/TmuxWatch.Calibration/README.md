# Live agent calibration

Run real Claude Code, Codex, and GitHub Copilot CLI sessions in disposable tmux
projects to detect UI drift. The executable reuses the production classifier and
monitor. It also runs deterministic monitor checks using scrubbed fixtures.

Requirements: .NET 10, tmux, bash, git, Python 3, GNU timeout, and whichever agent
CLIs you select. Helpers use only Python's standard library. Install and
authenticate the agents yourself. Live runs consume your normal model usage.

From the repo root:

```sh
./calibrate.sh preflight
./calibrate.sh list
./calibrate.sh run
```

The default run selects all three agents, every scenario, 120- and 70-column
terminals, and one retry. It runs agents sequentially. Each attempt has a
90-second deadline; the overall deadline is 30 minutes. Unfinished coverage is
reported explicitly, so increase the overall deadline for a slow full sweep.
The defaults are limits, not a promise that every scenario will be reached.

For a focused check after upgrading an agent:

```sh
./calibrate.sh run --agents copilot --scenarios idle,working,question-choice,question-freeform,question-multiple
./calibrate.sh run --agents claude,codex --unattended --retries 0 --deadline-seconds 1800
./calibrate.sh run --agents codex --model codex=YOUR_MODEL --widths 120,70
```

Run `./calibrate.sh help` for sampling, sizing, and deadline options. No live
agent runs when the command is omitted. `preflight` does not make model calls.
Claude and Codex expose authentication status commands; Copilot authentication
is checked during interactive startup when no equivalent status command is
available. Login screens are not saved. Interactive runs can pause for login or
state confirmation; unattended runs mark unavailable/inconclusive and continue.

## Scenarios and evidence

The catalog exercises IDLE, WORKING, DEAD, command and edit approvals, choice,
freeform and multiple-question prompts, native background shells and monitors,
detached subagents, blocked subagents, and combined background reasons. After
capturing a target it releases controlled work or completes the synthetic prompt,
then checks the return to IDLE. A live DONE can be acknowledged through the
production monitor API.

Question scenarios also validate structural tool-argument metadata: the multiple
question case requires at least two fields, and the freeform case opens the native
text editor when the tool initially displays choices. Merely asking a question
about free text does not count as exercising the free-text UI. Driver actions are
recorded beside the capture timeline.

Each attempt starts a fresh project and agent process. Native tool hooks record
small allowlisted facts, while bounded helper programs record gate start/end
events. The harness never saves hook tool inputs, transcript paths, or login
output. A pending foreground tool plus its running helper establishes WORKING.
A completed main turn with no outstanding controlled resources establishes IDLE.
Native background launch evidence, a main Stop event, and live gates establish
background work. A helper exiting inside a subagent does not establish that the
subagent has reported; that requires its own completion evidence.

Native permission requests in sessions configured for user review establish a
blocking request after a settling interval. Copilot uses dialog notifications
(permissions and elicitation) alongside a pending tool; its earlier permission
request hook can precede automatic approval and is not a visible-dialog signal. A question-tool entry alone is
insufficient: where no independent pending-permission signal is available, the
harness asks for inspection or records an inconclusive scenario. This is useful
coverage information, not a passing classifier test.

Every attempt prints an attach command and a confirmation command. To inspect,
attach from another terminal, then detach with Ctrl-b d. An explicit label is
valid for 15 seconds and is invalidated by a new lifecycle event:

```sh
./calibrate.sh confirm /path/to/run/attempt/project Waiting None
./calibrate.sh confirm /path/to/run/attempt/project Backgnd BackgroundAgent
```

Only confirm what is actually visible, including the requested scenario variant
(for example, a free-text editor rather than a choice menu). An operator label can
establish that variant when hooks do not expose its shape. Requested scenario
intent is not proof.
Unattended runs can also accept this explicit confirmation file from a separate
interactive terminal; they never manufacture one themselves.

BACKGND fingerprints currently exist only for Claude. Other agents still run the
background scenarios, but established background states report Unsupported until
their profiles gain supported fingerprints. A model declining to use a native
monitor or background agent reports Inconclusive. Prompt wording and model
behavior are not guaranteed across upgrades.

## Reports and fixture candidates

Each run creates a private directory under `.calibration-runs/` containing
`report.json`, `report.md`, launch metadata, minimal hook evidence, and numbered
full-screen captures. The production 16-nonblank-line scan window is unchanged.
JSON samples include expected-state evidence, actual classification and background
reason, monitor state, attention events, and pointer aggregate. Versions and
effective models are recorded when supplied by CLI hooks; explicit model
overrides are recorded in launch metadata. An absent model field is unknown,
not an inferred version or model.

The report retains transient mismatches and every retry. A sustained mismatch
requires the configured number of consecutive established samples. Exit codes:

| Code | Meaning |
| --- | --- |
| 0 | Every executed check and requested coverage entry passed |
| 1 | At least one mismatch, including a failed earlier attempt |
| 2 | Inconclusive, unsupported, unavailable, cancelled, or incomplete coverage |

A pass with brief mismatching samples states their count. Consult the sample
timeline before accepting a flickering screen. Replay passes never fill gaps in
live coverage.

Raw captures are gitignored and can still contain private information from agent
startup configuration. Review and scrub a capture before exporting a candidate:

```sh
./calibrate.sh export /path/to/run /path/to/reviewed-scrubbed.txt waiting-ask-question
```

This creates `candidates/NAME.txt` and provenance metadata in the run directory.
It preserves exact bytes, refuses to overwrite an existing candidate, and never
edits or stages the test suite. Copy reviewed candidates into the fixture suite
and register an expected-state regression test as part of a normal code change.
If using `--output`, keep that directory private and outside tracked content.

## Isolation and cleanup

The development driver always specifies a randomly generated private tmux socket
and accepts input only for panes it created. It loads no ordinary tmux config.
The production watcher's whitelist is unchanged. The disposable project is a
convenience boundary, not a security sandbox for the agent process.

Agent settings are passed through CLI flags or written into disposable projects;
ordinary configuration files are not edited by the harness. Existing credentials
are reused. CLIs may persist their normal session history and workspace trust
decisions. Claude's known workspace dialog defaults to No, so the driver selects
Yes only in a recognized dialog after tmux confirms its owned pane's working
directory. This works when a narrow dialog truncates the path. Unknown
startup dialogs require inspection.

Codex uses vetted project hooks, an invocation-local hook trust override, user
approval review, and native exec-policy rules for command approval. File approval
uses read-only sandbox mode. Question scenarios enable the installed CLI's
`default_mode_request_user_input` feature for that invocation. Claude uses manual
permissions with a narrow helper-command allow rule outside approval scenarios.
Copilot uses project hooks, native dialog notifications, schema field counts,
and a helper-command allow rule. Its known folder-trust dialog is accepted only
after the same owned-path check. Managed policy or
unsupported CLI flags can prevent a scenario and remain visible as incomplete.

Helpers expire within 120 seconds by default (hard maximum 600). Each agent has a
separate launcher timeout. Normal completion and Ctrl-C release gates and close
owned panes; a private-server watchdog also enforces the overall lifetime.
`--retain-failures` offers interactive retention until those lifetime limits.
Unattended runs always clean up. Artifact directories remain for review; remove
them when finished with the evidence.

## Monitor replay and notifications

```sh
./calibrate.sh replay
./calibrate.sh replay --physical-notifications
dotnet test
```

Replay checks first sight, WORKING-to-IDLE/DONE, acknowledgement, WAITING
notifications, repeated-frame deduplication, DEAD, pause-aware pointer state,
Unknown debounce/recovery, background exit and grace release, combined reasons,
reason narrowing, and background-agent completion without a timer. It uses a
recording notifier and the production pointer aggregation function. Fixture
replay is deterministic and makes no model calls.

Physical smoke testing is optional: it rings the bell and changes the pointer
briefly, restoring it in a finally block. Unsupported pointer hosts remain a
no-op. Observe the physical output yourself; event assertions alone cannot
verify speakers or desktop pointer rendering. Live monitor observations use the
production default 120-second grace and 15-capture Unknown threshold; replay
uses a controlled clock with shorter configured thresholds to exercise deadlines.

The tests include a real private-tmux terminal round trip with a local fake CLI,
so regressions involving terminal input and `timeout --foreground` are caught
without model usage. That check requires tmux on Linux; ordinary classifier tests
remain usable on other hosts.

## Adapter references

Launch flags were checked against local Claude 2.1.263, Codex 0.153.4, and
Copilot 1.0.86 help.
Hook adapters follow [Claude's hook reference](https://code.claude.com/docs/en/hooks),
[Codex's hook reference](https://learn.chatgpt.com/docs/hooks), and
[Copilot's hook reference](https://docs.github.com/en/copilot/reference/hooks-reference).
Codex reviewer selection follows the
[configuration reference](https://learn.chatgpt.com/docs/config-file/config-reference).
Copilot launch options follow the
[CLI reference](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-command-reference).
Verify these surfaces when updating adapters; do not assume hook names, payloads,
or permission behavior are interchangeable across agents.
