# Calibration harness boundary

This directory contains a development executable, separate from the production
watcher. The user authorized it to create and drive disposable agent sessions.
Keep every tmux mutation behind `OwnedTmux`, whose random socket and owned-pane
set prevent targeting an existing server or unrelated pane. Preserve the
production `TmuxRunner` whitelist and `ITmuxClient` interface.

When changing launch arguments, hooks, evidence rules, or cleanup, read
[README.md](README.md) and verify the real terminal regression test. A fake
process cannot reveal terminal job-control errors. Keep helper and agent
lifetimes bounded independently of the harness process.

Expected state must come from lifecycle/helper evidence or an explicit operator
label. Classifier output must never establish its own expected state. Preserve
inconclusive and unsupported outcomes when evidence is missing. Record adapter
failures separately from classifier defects.

Keep raw run data under the gitignored private run directory. Only reviewed,
scrubbed captures become fixtures; preserve structural glyphs and provenance.
