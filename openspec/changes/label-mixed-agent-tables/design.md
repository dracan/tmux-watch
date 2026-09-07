## Context

The user superseded an earlier separate-table design. All unpaused agent panes remain in one table. The shared row renderer also renders Paused and Other panes; each row already carries its matched AgentId.

## Goals / Non-Goals

Identify agents only where differentiation is necessary, using CC, GHCP, and CDX. Keep existing columns, urgency ordering, keyboard addresses, and single-line window cells. Do not add configuration or alter agent detection.

## Decisions

- Count distinct matched ids among agent rows in each rendered table. Only counts greater than one enable prefixes.
- Prefix only agent rows; shell rows in Paused do not count and receive no label.
- Use full configured ids for custom profiles, avoiding arbitrary abbreviations and collisions between custom ids.
- Escape labels and names as text. Preserve focus styling on the window name and render labels in grey.
- Prevent prefixed Window cells from wrapping in narrow layouts; long names may be truncated by Spectre. Tables with no labels retain their existing rendering.
- Verify visible output through the existing table construction boundary using a real Spectre console and a string output sink. Review against the starting HEAD and this spec before committing.

## Risks / Trade-offs

Prefixes consume some Window space when agents are mixed. Long names may be truncated to preserve row height. Custom ids may be longer than built-in labels.
