## ADDED Requirements

### Requirement: Built-in Codex discovery

The default agent set SHALL include a `codex` profile matching foreground command `codex`, including its extension-insensitive `codex.exe` form. It SHALL use the existing agent inventory and navigation. A non-empty configured agent list SHALL continue to replace the defaults.

#### Scenario: Codex joins the watched inventory
- **WHEN** default configuration enumerates panes running `codex`, `codex.exe`, `claude`, `copilot`, and a shell
- **THEN** the Codex panes are assigned agent id `codex`, the existing agents retain their profiles, and the shell remains an Other pane

#### Scenario: Explicit profiles remain authoritative
- **WHEN** a non-empty configured agent list omits Codex
- **THEN** Codex is not implicitly added to that list
