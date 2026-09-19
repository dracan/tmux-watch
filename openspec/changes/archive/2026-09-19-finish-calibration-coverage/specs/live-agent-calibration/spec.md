## ADDED Requirements

### Requirement: Correlated native lifecycle evidence
The harness SHALL distinguish parent and child lifecycle events using available session identifiers or matched native task completion. A child Stop MUST NOT establish parent IDLE. A native question result confirming the synthetic answer MAY establish the captured settled interval for that same pending request; tool entry alone SHALL remain insufficient.

#### Scenario: Child returns without a child hook ID
- **WHEN** a controlled child gate has ended, its foreground parent task has returned, and the parent turn has stopped
- **THEN** completion can be established without inventing a child identity

#### Scenario: Synthetic question answer is not received
- **WHEN** the pending question has no native result confirming the supplied answer
- **THEN** the unlabelled captures remain inconclusive

### Requirement: Explicit automated check scope
The harness SHALL provide an automated check mode with an explicit per-agent supported scenario selection and a separate full diagnostic catalog. Reports SHALL list exclusions and distinguish missing native capabilities from failures in exercised checks. Full diagnostic runs MUST preserve incomplete outcomes.

#### Scenario: Daily check excludes an unobservable capability
- **WHEN** the supported-check selection cannot verify a capability independently
- **THEN** its exclusion and reason are visible and the report does not claim that capability passed
