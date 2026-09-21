## MODIFIED Requirements

### Requirement: Other panes table

The system SHALL render a secondary **Other panes** table listing one row per non-agent
pane, positioned below the agent pane table and above the Paused table. Rows SHALL be
ordered by session name, then window index, then pane index (the multiplexer's own
order), so row positions and their assigned address keys remain stable across polls while
the agent table re-sorts. Each row SHALL show the pane's foreground process in the state
column position, the pane's window name, and the time since that window's last activity
in the time column. Its headings SHALL read Command and Quiet for. Unsupported activity SHALL render as `-`. The table SHALL be omitted entirely when it would contain no rows.

#### Scenario: Non-agent panes listed in their own table

- **WHEN** the multiplexer has agent panes plus panes running `k9s`, `lazygit`, and a shell
- **THEN** the view shows the agent panes in the main table and the three non-agent panes in a separate Other panes table below it

#### Scenario: Process shown in place of classified state

- **WHEN** a non-agent pane's foreground command is `lazygit`
- **THEN** its row shows `lazygit` in the state column position, visually distinct from an agent row's classified state indicator

#### Scenario: Stable ordering across polls

- **WHEN** agent panes change state and the main table re-sorts between two polls
- **THEN** the Other panes rows keep the same relative order and the same address keys

#### Scenario: Activity time shown for non-agent rows

- **WHEN** a non-agent pane's window was last active some time ago
- **THEN** its row shows the elapsed time since that window's last activity in place of a time-in-state value

#### Scenario: Empty table omitted

- **WHEN** there are no non-agent panes to show
- **THEN** no Other panes table is rendered

#### Scenario: Unsupported activity is not a pane age

- **WHEN** the host cannot supply reliable activity for a newly created non-agent pane
- **THEN** the Quiet for cell shows `-` and no session age or first-seen age is substituted

## ADDED Requirements

### Requirement: Headings describe mixed paused rows

Agent-only tables SHALL retain State and In state. Non-agent-only tables SHALL use Command and Quiet for. A mixed paused table SHALL use State / Cmd and Age, with a caption explaining that agent ages are time in state and other ages are window inactivity.

#### Scenario: Paused rows of both kinds

- **WHEN** the paused table contains an agent and a non-agent row
- **THEN** its headings and caption distinguish process commands from classified states and explain each age
