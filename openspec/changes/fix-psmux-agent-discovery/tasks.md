## 1. Reproduce

- [x] 1.1 Add failing discovery and rendering regressions for the reported helper command and invalid activity age.

## 2. Implement

- [x] 2.1 Add the read-only Windows snapshot adapter and pure ownership resolver with lifetime, ambiguity and pane-boundary checks.
- [x] 2.2 Integrate fresh shared ownership resolution into discovery and calibration, with lifecycle and failure regressions.
- [x] 2.3 Suppress unsupported activity at the client/discovery boundary and correct non-agent and mixed-table headings.

## 3. Validate and document

- [x] 3.1 Document Windows discovery, activity support and the limits of process ownership.
- [x] 3.2 Run focused regressions, full tests, build and OpenSpec validation; exercise the Windows adapter where available and record any validation limits.

## 4. Follow-up: executable renamed during update

- [x] 4.1 Reproduce the reported stable process identity with a renamed executable in a native Windows regression.
- [x] 4.2 Preserve snapshot command identity across executable renames while retaining liveness and creation-time guards.
- [x] 4.3 Validate the regression on Windows, run the full suite, and update the design and validation notes.
