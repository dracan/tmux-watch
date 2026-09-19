## Why

Agent UI changes have left Copilot working and question screens classified as Unknown. Synthetic fixtures alone cannot reveal new screen shapes, so developers need repeatable live captures with evidence independent of the classifier under test.

## What Changes

- Add an on-demand development harness for Claude, Codex, and Copilot on an isolated tmux server with disposable projects.
- Exercise core states, native approvals and questions, background tasks and agents, completion, acknowledgement, and notifications across terminal widths.
- Use repo-owned bounded helpers, independent lifecycle evidence, and optional operator confirmation. Distinguish mismatches, inconclusive scenarios, unavailable agents, and unsupported detection.
- Record versioned local reports and captures; produce reviewable fixture candidates without committing raw transcripts.
- Bound runs and retries, preserve ordinary agent configuration, and support authentication preflight, inspection, and cleanup.
- Fix detection defects only when reproduced evidence supports the token change.

## Capabilities

### New Capabilities

- `live-agent-calibration`: Controlled live scenarios, evidence capture, classifier and monitor evaluation, and regression candidate export.

### Modified Capabilities

- `pane-state-detection`: Recognize the live Codex timed status line when it carries a background-terminal summary, reproduced with independent foreground gate evidence on 0.153.4; recognize the bordered Copilot 1.0.86 structured question panel reproduced during live validation.

## Impact

A separate .NET development executable and tests reuse production classifier and monitor APIs. The watcher's tmux whitelist remains unchanged. Live runs require installed, authenticated agent CLIs and consume model usage. No third-party helper package is required. Existing unrelated working-tree edits are excluded from the commit.
