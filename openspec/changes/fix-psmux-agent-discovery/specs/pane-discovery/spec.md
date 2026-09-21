## MODIFIED Requirements

### Requirement: Enumerate panes across all sessions

The system SHALL enumerate every pane across all multiplexer sessions using a read-only listing (`<multiplexer> lsp -a -F <format>`) and expose, for each pane, a stable pane id, session name, window index, pane index, foreground command, dead flag, and the pane's window last-activity timestamp. The multiplexer executable SHALL default to `tmux` and SHALL be overridable via configuration so a psmux (or other tmux-compatible) host remains supported. The enumeration format string and the read-only verb whitelist SHALL be identical across supported multiplexers.

#### Scenario: Multiple sessions enumerated

- **WHEN** the tmux server has several sessions and panes running
- **THEN** the system returns one record per pane, each including pane id (e.g. `%10`), session name, window index, pane index, foreground command, and dead flag

#### Scenario: Window activity timestamp exposed

- **WHEN** a pane is enumerated
- **THEN** its record carries the last-activity timestamp of the window containing it, and an unsupported, missing or unparseable value degrades to "unknown" rather than failing the enumeration

#### Scenario: tmux server unavailable

- **WHEN** the `tmux` CLI is missing from PATH or no server is running
- **THEN** the system returns an empty pane set and surfaces a clear, non-fatal error rather than crashing

#### Scenario: psmux host via configuration override

- **WHEN** the multiplexer executable is overridden to `psmux` in configuration
- **THEN** the system enumerates panes using `psmux` with the same format string and verb whitelist, requiring no other change

#### Scenario: Unsupported psmux activity

- **WHEN** native Windows or an explicitly named psmux client reports a session creation timestamp as window activity
- **THEN** discovery exposes unknown activity, including for a new pane in an older session and for psmux invoked as tmux.exe on Windows

#### Scenario: Real Unix activity remains available

- **WHEN** a supported Unix tmux client reports valid window activity
- **THEN** discovery preserves that value even when several windows share a timestamp

### Requirement: Identify agent panes

The system SHALL match each enumerated pane against an ordered set of configured **agent profiles** (e.g. Copilot, Claude Code) and treat the pane as a watched agent pane when it matches a profile. A pane matches a profile when (1) its foreground command equals the profile's configured command (extension-insensitive), OR (2) on native Windows, a fresh process snapshot establishes an unambiguous live agent owner rooted at the pane pid, OR (3) its session or window name matches the profile's configured naming convention. Command match is evaluated across all profiles before the Windows ownership fallback, followed by the naming backstop. Ownership matching SHALL use configured profile commands, stop at another pane root, reject independent multiple owners, and stop matching after the owner exits. It SHALL reject missing roots, invalid parent lifetimes, and dead panes. Snapshot failures SHALL preserve command and naming matches; no historical process match SHALL be retained. A pane that matches no profile MUST be excluded from watching and MUST NOT be captured; it SHALL instead be surfaced as a non-agent inventory entry.

#### Scenario: Copilot pane matched by command

- **WHEN** a pane reports foreground command `copilot`
- **THEN** it is watched and matched to the `copilot` profile

#### Scenario: Claude pane matched by command

- **WHEN** a pane reports foreground command `claude`
- **THEN** it is watched and matched to the `claude` profile

#### Scenario: Claude pane matched by name convention

- **WHEN** a pane's foreground command is not `claude` but its session or window name matches the `claude` profile's configured convention
- **THEN** it is watched and matched to the `claude` profile

#### Scenario: Non-agent pane excluded from watching but surfaced as inventory

- **WHEN** a pane reports foreground command `lazygit`, `bash`, or any value matching no profile's command, convention, or content fingerprint
- **THEN** the pane is excluded from watching and is not captured, and it appears in the non-agent inventory instead

#### Scenario: First matching profile wins

- **WHEN** a pane could match more than one profile
- **THEN** the system assigns it to the first profile in configured order and does not double-watch it

#### Scenario: Copilot remains recognised while its helper runs

- **WHEN** Windows reports `tgrep` but the live pane tree is shell -> copilot.exe -> tgrep.exe
- **THEN** the pane is matched to Copilot and its reported command is preserved for diagnostics

#### Scenario: Standalone tools and exited agents

- **WHEN** a pane runs a standalone helper or Copilot has exited leaving only an orphan helper
- **THEN** it receives no process-based agent match

#### Scenario: Running agent executable renamed during an update

- **WHEN** Windows reports `apphost` or `tgrep` and a live Copilot owner's executable has been renamed while running, leaving its snapshot launch name as `copilot.exe` but changing .NET `ProcessName`
- **THEN** discovery retains the Copilot match using the snapshot name, subject to the existing liveness and creation-time checks, and releases it after the process exits

#### Scenario: Ownership boundaries and ambiguity

- **WHEN** a pane contains another pane root or two independent agent owners
- **THEN** discovery does not borrow an agent from the other pane or guess between independent owners

#### Scenario: Process snapshot cost and freshness

- **WHEN** several panes need Windows ownership fallback in one enumeration
- **THEN** at most one fresh process snapshot is shared among those panes and no extra multiplexer command or pane capture is issued

#### Scenario: Unix behavior unchanged

- **WHEN** the watcher runs on Unix
- **THEN** it retains command and naming matches without inspecting local process trees

### Requirement: Expose matched agent identity

The system SHALL expose, for each watched pane, the id of the agent profile it matched, so downstream classification, the TUI, and the calibrate output can use the correct profile and label the pane's agent.

#### Scenario: Matched agent available downstream

- **WHEN** a pane is matched to a profile
- **THEN** the pane record carries that profile's id and the classifier is invoked with that profile's tokens

#### Scenario: Calibration shares resolved ownership

- **WHEN** a Windows process-based agent match is established during enumeration
- **THEN** live monitoring, one-shot output and calibration use that same agent identity
