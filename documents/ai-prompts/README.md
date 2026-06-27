# AI System Prompt Variants (reference archive)

Historical variants of the Coded Values AI system prompt
(`src/SchoolCollab.AI/Prompts/ai-system-prompt.md`), kept for reference.
**These files are not loaded at runtime** — they live outside the
`Prompts/` embedded-resource glob, so they are never embedded or served.
The active prompt is always the one under `src/SchoolCollab.AI/Prompts/`.

## Files

### `ai-system-prompt.full-modified.md`
The heavily-augmented prompt tried first (added a mandatory
`get_coded_value_by_code` "determine parent code FIRST" rule, a strict
two-turn write gate, required `Parent:` header lines, enumerated
per-attribute/per-child tool-call sequences, and emphatic MUST/NEVER/STOP
repetition).

**Retired because it caused timeouts** on the "yes" confirmation turn:
it mandated an extra read-only lookup on every turn, pushed all writes
(parent + per-attribute definitions + bulk + per-child `set_attribute`)
into the confirmation turn, and ballooned the system prompt — multiplying
round trips on a slow free-tier model. Kept as a "what not to do" reference.

### `ai-system-prompt.trimmed-speed.md`
The trimmed variant that keeps the original prompt's lightweight
two-turn confirm gate and eager parent creation, and adds only
speed-focused guidance:
- skip the `get_coded_value_by_code` pre-lookup (`create_bulk_values` /
  `update_coded_value` resolve the code server-side and skip duplicates),
- never web-search for standard reference data,
- keep descriptions short for large sets,
- prefer one `create_bulk_values` call,
- call `update_coded_value` directly when code + change are given,
- carry context across turns (recover the parent code from history on
  the confirmation turn; do not re-ask or re-look-up).

This is the basis of the active prompt committed in
`fix/coded-values-chat-history-and-timeout`.