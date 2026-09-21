## Why

On native Windows, psmux can report Copilot's `tgrep` child as the pane command, dropping a live agent into Other panes. psmux 3.3.8 also supplies session creation time as window activity, making a newly opened pane appear hours old.

## What Changes

- Add a live Windows process-ownership fallback for agent discovery when the reported command does not match a profile.
- Resolve ownership afresh per enumeration, with pane boundaries, ambiguity and process-lifetime checks; never infer an agent from a helper's name.
- Treat native Windows/psmux window activity as unsupported until a reliable implementation is verified.
- Label non-agent tables with Command and Quiet for, and distinguish mixed paused rows without presenting process names as states.
- Use the same resolved agent identities in live, one-shot and calibration output.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `pane-discovery`: Windows process ownership fallback and unsupported activity timestamps.
- `watcher-tui`: Accurate headings and unavailable non-agent activity display.

## Impact

Discovery, the read-only Windows process adapter, multiplexer capability reporting, calibration and table rendering, tests, and README. No new packages, pane input, multiplexer verbs, or Copilot configuration changes. Unix discovery keeps command and naming matches.
