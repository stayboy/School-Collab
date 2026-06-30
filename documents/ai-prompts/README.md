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

### `ai-system-prompt.aggressive-trim.md`
A further-compressed variant that consolidates the four "use your own
knowledge / no web / be fast / minimise round trips" calls into a single
short paragraph, merges the overlapping "be concise / no JSON / no tool
names" rules, and moves field conventions into a compact table rather
than prose. Net effect: ~58 % shorter than the trimmed-speed baseline
(14 638 → 6 092 chars, ~3 659 → ~1 523 input tokens per round).

This is the active prompt committed in
`perf/trim-system-prompt-and-tool-list`. Pair it with **per-prompt tool
filtering** (see "Pattern for trimming prompts" below) — the two are
complementary, not alternatives.

---

## Pattern for trimming prompts

Trimming the system prompt is a recurring need: every extra 1 000 input
characters is ~250 tokens billed on **every round** of a multi-round
chat. After two or three rounds, prompt size dominates the per-chat
cost. Use this pattern when a prompt has grown organic redundancy.

### When to trim

Trim the active prompt when **any** of these signals fire:

- The prompt is **>5 000 chars (~1 250 tokens)** for a domain that
  doesn't justify it (most application-specific prompts fit comfortably
  in 2-3 KB).
- The same rule is repeated more than once across the prompt (e.g.
  "use your own knowledge", "no web search", "be fast", "minimise
  round trips" all live in the same paragraph).
- Two adjacent sections say the same thing in different words.
- The prompt has grown on the basis of "what not to do" notes that are
  no longer triggered by current model behaviour (delete them rather
  than rephrase — the `ai-system-prompt.full-modified.md` reference is
  there for a reason).

### Don't trim when

- The prompt is the **single source of truth** for a complex domain
  workflow (e.g. medical, financial, regulatory). Compress only with
  paired regression tests against the live model.
- The model already scores well on the workflow and you have no
  quantitative evidence the size is hurting it. Measure first — the
  gemma-3 → gemma-4 root-cause investigation used a probe to confirm
  the prompt was at fault before touching the file.

### How to trim safely

Follow these steps to avoid silently breaking model behaviour:

1. **Snapshot the original** as `*.original.md` next to the active
   prompt (or in `documents/ai-prompts/`). Don't rely on git history
   alone — the loader code is expected to fall back to it if the
   trimmed file is missing or malformed. The
   `ai-system-prompt.original.md` convention comes from
   `perf/trim-system-prompt-and-tool-list`.
2. **Update the loader's fallback chain** to prefer the trimmed file
   first and fall back to the original — this gives you a single-step
   rollback by deleting the trimmed copy.
3. **Compress overlapping rules into one paragraph**, not into
   shorter individual lines. Models read density; "you must not X, you
   must not Y, you must not Z" reads as one stronger rule than three
   weak ones.
4. **Move enumerated conventions into tables** (Code / Name / Description
   / DisplayOrder → four-row table) instead of prose.
5. **Drop "what not to do" notes** that no longer apply to the model
   you actually ship. They cost tokens on every round and yield nothing
   on the current generation.
6. **Pair the prompt trim with tool-list filtering**. A per-prompt
   tool filter (see `CodedValueAIService.SelectToolsForPrompt`) often
   saves more input tokens than the prompt trim itself, because the
   tool definitions ship the **names + descriptions + JSON-schema
   parameter shapes** of every `AIFunctionFactory.Create(...)` call.
7. **Add unit tests** for the tool filter
   (`CodedValueAIServiceToolSelectionTests`) and a hand-rolled probe
   (see `tools/ChatAsyncProbe` from earlier in this conversation) to
   verify the trim against the live model before committing.
8. **Measure before/after** on a representative prompt round-trip —
   response time, completion-without-timeout ratio, and qualitative
   behaviour on a few canonical cases.

### Recurring review

Revisit the prompt every 6-12 months or whenever:

- The default model changes (a new model may need a different
  operating contract).
- A new feature is added to the AI service that maps onto a capability
  not previously listed.
- Users report regressions that survive a tool-selection audit — often
  these trace back to a missing rule the trimmed prompt dropped.
