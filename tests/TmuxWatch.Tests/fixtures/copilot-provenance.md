# Copilot structured question fixtures

Observed on GitHub Copilot CLI 1.0.86 on 2026-09-19 in isolated tmux sessions.
The choice and free-text screens reproduced Unknown classification in 12/12
manually confirmed samples before the fix. Subsequent runs use native
elicitation notifications and schema field counts as independent evidence.

The three waiting-ask-question fixtures are synthetic panel-only reductions of
choice, free-text, and multiple-field screens. Field wording and terminal width
are synthetic; structural glyphs, indentation, panel heading, and footer shapes
match live captures. No raw transcript, path, account, or credential is included.
The panel heading plus column-zero borders is the fingerprint. Internal input
rules are indented, and the final border must end the scanned tail so a panel
above a later composer cannot match. No assertion is made about forms taller
than the existing 16-nonblank-line scan window.
