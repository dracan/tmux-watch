## Context

The live harness already produces private evidence and runs bounded native scenarios. The missing piece is an agent workflow that the user can request without remembering commands. The repository shares Claude skills with Codex through a symlink and has separate Copilot discovery surfaces.

## Goals / Non-Goals

**Goals:** One discoverable refresh skill, focused execution, independent diagnosis, verified repair, honest results, and main delivery under the user's instructions.

**Non-Goals:** Scheduled runs, automatic CLI upgrades, new harness implementation, raw capture commits, or an agent evaluation campaign for this skill.

## Decisions

- Put the canonical skill under `.claude/skills/refresh-agent-detection`, preserving the existing Codex symlink. Give Copilot a matching metadata entry and a pointer to the canonical file, plus a prompt wrapper. This avoids maintaining two copies of a repair procedure.
- Allow discovery from natural-language refresh requests as well as explicit invocation. A command-only entry would keep the user's original memory burden.
- Keep preflight, evidence triage, and completion in the main skill. Disclose repair instructions only when a failure needs a code change. A clean run creates no tracked artifacts or commit.
- Use the installed OpenSpec CLI's artifact instructions for repairs. The workflow does not depend on a global wrapper plugin or an unavailable sync skill.
- Preserve the distinction between classifier drift, harness drift, unavailable tools, and unsupported UI states. Fixing a check cannot mean teaching its evidence source to agree with the classifier.
- Review frontmatter, paths, parity, commands, boundaries, and each workflow branch statically. The user explicitly requested no agent test runs, so skip the skill-creator's agent evaluation and feedback loop.

## Risks / Trade-offs

- Paid live runs can be slow: select named agents/scenarios, reuse supplied evidence, and rerun affected cases after repair rather than restarting every successful case.
- Tool UIs and hooks can drift: discover current harness options and native contracts when evidence fails; preserve incomplete outcomes.
- Cross-agent pointers can drift: verify their targets and matching discovery metadata before delivery.
- Static review cannot prove future agent adherence: disclose that limitation without claiming runtime validation.

## Review result

Static review checked the clean, detection-drift, harness-drift, unavailable, and focused-report branches. It tightened the checks-only boundary and separated absent-report build failures from classifier mismatches. Frontmatter validation passed for both skill entries; metadata parity, canonical/Codex paths, referenced files, documentation links, ASCII punctuation, and whitespace checks passed. OpenSpec strict validation passed. Agent test runs and live calibration were not performed, as requested; runtime adherence is not claimed.
