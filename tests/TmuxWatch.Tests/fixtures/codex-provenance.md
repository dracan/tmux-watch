# Codex fixture provenance

All `codex-*.txt` fixtures contain synthetic project content. Structural UI glyphs
are preserved because classification depends on the captured terminal bytes.

Discovery (`codex`), idle composer (U+203A), working status (U+25E6 plus elapsed
time and interrupt hint), and completed-turn banner were inspected read-only on
local Codex CLI 0.153.4 panes on 2026-09-06. No real pane transcript is stored here.

Approval and question shapes are based on the public OpenAI Codex renderer
snapshots at commit `6af345407d9c2a568da9d01b6c4b81a9e61495c0`:

- [Command and edit approval snapshots](https://github.com/openai/codex/tree/6af345407d9c2a568da9d01b6c4b81a9e61495c0/codex-rs/tui/src/chatwidget/snapshots): `approval_modal_exec`, `approval_modal_patch`, and `status_widget_and_approval_modal`.
- [Question snapshots](https://github.com/openai/codex/tree/6af345407d9c2a568da9d01b6c4b81a9e61495c0/codex-rs/tui/src/bottom_pane/request_user_input/snapshots): `request_user_input_options`, `freeform`, `options_notes_visible`, `footer_wrap`, and `multi_question_last`.
- [Queued input snapshots](https://github.com/openai/codex/tree/6af345407d9c2a568da9d01b6c4b81a9e61495c0/codex-rs/tui/src/bottom_pane/snapshots): `status_and_queued_messages_snapshot` and `status_with_details_and_queued_messages_snapshot`.

Upstream snapshots supplement the local captures; they do not establish that
every prompt variant was observed live in 0.153.4. The stale-prompt fixture and
incidental background prose are regression constructions using these same shapes.

Codex BACKGND detection is not covered. DONE means an observed foreground turn
returned to the composer; it does not guarantee detached work has completed.
