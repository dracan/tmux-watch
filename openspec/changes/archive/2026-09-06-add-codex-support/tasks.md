## 1. Profile and classification

- [x] 1.1 Add optional composer-scoped waiting footer and full working-line patterns, preserving existing profiles.
- [x] 1.2 Register the Codex profile with verified command, composer, approval/question, and working tokens.

## 2. Behavioral validation

- [x] 2.1 Add synthetic Codex fixtures with provenance for approvals, questions, working, and idle; cover stale prose, wrapped footers, notes, and queued input.
- [x] 2.2 Test default and explicit-profile discovery plus Codex WAITING and DONE notification/acknowledgement transitions.
- [x] 2.3 Run build and full tests, validate the OpenSpec change, and classify available live Codex panes read-only.

Validation: `dotnet build` passed with no warnings or errors; `dotnet test --no-build`
passed all 383 tests; `openspec validate add-codex-support --strict` passed.
Read-only live calibration identified two Codex panes as IDLE and one as WORKING.

## 3. Documentation

- [x] 3.1 Document built-in Codex support, profile override configuration, evidence versions, and foreground-turn scope of DONE.
