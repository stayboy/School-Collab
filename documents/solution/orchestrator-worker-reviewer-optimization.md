# Orchestrator-Worker-Reviewer skill optimization (speed + token usage)

**Date:** 2026-09-01
**Status:** Implemented
**Scope:** `.pi/skills/orchestrator-worker-reviewer/` (tiered rewrite), `AGENTS.md`, `documents/rounds/README.md`

## Findings (the why)

The 4-agent `orchestrator-worker-reviewer` skill took too long even for simple tasks. Cost-model analysis of the original SKILL.md (18,533 bytes) identified five cost drivers:

1. **Every task paid the full pipeline** — orchestrator-plan → worker → reviewer → orchestrator-accept → (UI tester), 5+ sequential cloud-model round-trips regardless of task size, each awaited serially.
2. **Three build/test passes per round** — worker (full), reviewer (full "independent" rebuild), parent (full rerun). On the 10-project solution this was the largest wall-clock cost after agent runs.
3. **Heavy payloads per hop** — the 18.5KB skill body was effectively re-digested per round; plans/reports passed verbatim down the chain; every child re-read source specs.
4. **Unbounded reviewer rework loop** — the tester loop was bounded (~2), but the reviewer P1 loop was not; worst-case rounds could reach ~15+ runs.
5. **Round overhead** — four separate round docs (`plan-*`, `review-*`, `acceptance-*`, `ui-tester-*`) multiplied file reads and hand-off paths.

A review of the initially proposed "tiering + build-dedup + compaction" refinement found two contract conflicts that shaped the robust final design: (a) the parent's authoritative build/test rerun is mandated by the skill's own Pitfalls ("do not trust child-reported counts") and must stay; (b) dropping the reviewer entirely for small fixes removes the anti-overwrite guard, so the lightweight tier needed a substitute scope check.

## Decision (approved consolidated plan)

- **Tiered execution** — Tier 0 (trivial: no skill, per AGENTS.md menu), Tier 1 (small behavioural fix: 1 worker run, parent plans + accepts, strict eligibility checklist), Tier 2 (behavioural no-UI: worker + static reviewer, ≤1 rework iteration), Tier 3 (feature/UI: full 4-agent pipeline, loops bounded at 2). Mid-round escalation: any agent discovering scope creep bumps the tier instead of forcing a light tier through.
- **Static reviewer** — diff-only verification (plan conformance + best-coding-practices check); never builds/tests. This removes the third full build/test pass, and makes the reviewer safe to run in parallel with the parent's authoritative build/test (no MSB3027 tree contention). The parent's mandated rerun stays but is incremental (worker already built) and tests scope to affected projects **plus `SchoolCollab.ArchitectureTests.Unit`** (repo-wide scanner — always included).
- **Token discipline** — single round doc (`round-<slug>.md`) + single frozen diff artifact (`diffs-<slug>.patch`) passed by path; compact per-role contracts split into `references/role-contracts.md` with structured output blocks (WORKER REPORT / REVIEW / UI TEST); model catalog split to `references/models.md`; plan is the single source of truth (children don't re-read specs); Cline teammates spawned once per session and reused across rounds; per-tier model strategy (Tier 1 = worker flash model only).
- **Acceptance-run elimination for Tiers 1–2** — the parent transcribes the verdict against plan-phase criteria; Tier 3 keeps the orchestrator-accept run because the tester-scope handover is genuinely orchestrator-owned.
- **Deterministic UI-round trigger** — `git diff --name-only` filter (`.razor`, `.razor.css`, `.css`, `.js`, `wwwroot/`, ApiClient/Blazor client projects); no judgment calls about when a tester pass is needed.
- **Bounded loops everywhere** — Tier 2 reviewer loop ≤1; Tier 3 reviewer ≤2, tester ≤2; tester rework re-verifies via the tester only (parent statically checks the rework diff; no reviewer re-run).

Expected effect on a simple task: ~5 sequential agent runs → 1; 3 build/test passes → 1 effective (worker full + parent incremental); hand-off payloads −60–70%. Tier 3 keeps its full independent-verification contract.

## Implementation Steps (the how)

| File | Change |
|---|---|
| `.pi/skills/orchestrator-worker-reviewer/SKILL.md` | Full rewrite as lean tiered core: tier table + Tier-1 eligibility checklist + escalation rule; single round-doc + patch-artifact scheme; static reviewer; deterministic UI trigger; tier-conditional Procedure with parallel parent-build; bounded loops; updated Pitfalls + tier-conditional Verification (18,533 → 13,868 bytes, and models/role-contracts content no longer loaded per round) |
| `.pi/skills/orchestrator-worker-reviewer/references/models.md` (new) | pi + clinepass id tables, substitution rules, per-tier model strategy, traceability header format |
| `.pi/skills/orchestrator-worker-reviewer/references/role-contracts.md` (new) | Compact one-paragraph contracts for orchestrator/worker/reviewer/ui-tester + structured output block formats (paste-in prompts) |
| `AGENTS.md` | "Feature/fix implementation — ask before starting" now offers the three-mode menu (solo / light round Tiers 1–2 / full four-agent Tier 3); docs-layout table row updated to the single round-doc scheme |
| `documents/rounds/README.md` | Rewritten for `round-<slug>.md` + `diffs-<slug>.patch`; tier-appropriate sections; sole-writer rule (orchestrator or parent) |

### Verification results

- New SKILL.md read end-to-end: frontmatter intact, all 228 lines verified, chunk seams clean, no duplication.
- `references/models.md` (2,654 bytes) and `references/role-contracts.md` (3,393 bytes) created and verified.
- AGENTS.md both edits verified via diff output (menu + docs-layout row).
- `documents/rounds/README.md` rewrite verified via diff output.
- `git status` change set is exactly the four targets above (+ `references/` dir); the in-flight Period feature work in `src/`/`tests/` was untouched.
- No `dotnet build` required: markdown-only change set (build rule covers `.cs`/`.razor`/`.csproj`/props/`appsettings*.json`).

### Residual risks (accepted)

1. Tier 1 has no independent reviewer — mitigated by the strict eligibility checklist, parent scope check, and the mid-round escalation rule.
2. Affected-project test scoping could miss cross-project regressions — mitigated by always including `SchoolCollab.ArchitectureTests.Unit` in the parent test set.
3. Parent-transcribed acceptance on Tiers 1–2 is a deliberate contract change from "document owner accepts" — preserved in substance via plan-phase criteria authored before implementation; Tier 3 keeps the full orchestrator ownership.

### Follow-ups (not in this change)

- Historical round docs under `documents/rounds/` still use the old `<kind>-<slug>.md` naming — they are ephemeral residue per the README and will disappear on the next bulk-trash; no migration needed.
- The `.pi/workflows/*.js` one-off round scripts reference the old phase names but are historical artifacts, not part of the skill.