# Round — minimal-api-typed-responses

**Status:** CLOSED — verified; delivered on `feat/minimal-api-typed-responses`, PR #364
**Tier:** 2 (light) · **Mode:** attended · **Provider:** clinepass · **Owner override:** Tier 2 for an endpoint/contract change, because the design was fully settled in spec D8–D15 and the contract delta is provably additive (D14)
**Worker:** `cline-pass/deepseek-v4.1-flash` (`worker`) · **Reviewer:** `cline-pass/glm-5.3-flash` (`reviewer`, read-only) — substituted for the profile's `cline-pass/mimo-v2.6-flash`, absent from this session's registry; still ≠ the worker's model
**Round base:** `8c135e57` (main). Dirty at round start: `documents/specs/minimal-api-typed-responses.md` untracked (the spec is the plan) and `documents/rounds/round-notification-templates-b1.md` — **another session's file, not this round's**, excluded from the patch.
**Spec:** `documents/specs/minimal-api-typed-responses.md` (D8–D15)
**Patch:** `documents/rounds/diffs-minimal-api-typed-responses.patch` — 11 files, 1243 lines, `src/Settings/**` + `tests/**` only

## Plan

Goal: prove the typed-response pattern on one 24-endpoint suite — split `CodedValueRoutes.cs` into four URI-cluster
files, convert every handler to `TypedResults` with explicit unions, raise the schema pin to the measured value, with
no route, verb, body or handler-behaviour change. Full plan and the 9 acceptance criteria were written here before
dispatch; the spec carried the per-endpoint outcome table (§1.3) and the conversion rules (§3).

## Worker Report

Condensed (full text: subagent artifact `fcab65a2-…_worker_output.md`).

| WP | Result |
|---|---|
| WP1 spike | **Outcome A** — `TypedResults` + union alone emits schemas. 66 operations before and after; 0 → 2 operations with `content`. **No `.Produces<T>` anywhere.** |
| WP2 split | `CodedValueRoutes.cs` deleted; `CodedValueQueryRoutes.cs` (96), `CommandRoutes` (167), `OverrideRoutes` (60), `ProvisionalRoutes` (104). Orchestrator chains 6 mappers, one `RequireAuthIfOidc`, zero inline routes. |
| WP3 convert | **55/55** `Results.*` → `TypedResults.*`, arguments verbatim, both `Created` locations unchanged. `approve` is the only `IResult` (4 arms). No `ProblemDetails`. |
| WP4 pin | Settings **19** (measured), Students 0; message states 12 + 7 + 5 = 24 and is arithmetically checkable. Non-vacuity proven by reverting one handler (18 ≠ 19). |
| WP5 delta | 66 → 66 operations; paths/verbs unchanged; 0 → 19 with `content`; 0 lost; 47 non-CodedValue operations byte-identical; new schemas `IdResponse`, `MessageResponse`, `BulkCreateResponse`, `CodedValueDto`, `CodedValueAttributeDto`, `CodedValueAttributeDefinitionDto`; the spike's `Spike*` names gone. |

Deviations, all approved or reported: (1) three `internal sealed record` payloads + 5 wire-shape tests (approved Q1 —
anonymous types cannot be named in a union); (2) pin **19**, not the spec's 12 (approved Q2); (3) pin message wording
(approved Q3); (4) `approve` explicitly annotated `Task<IResult>`; (5) union arm order success-first, then 4xx
ascending; (6) the baseline document declared a fabricated `200` per operation (see Acceptance).

## Review

**VERDICT: CLOSED — no P1 findings.** Static review of the frozen patch against the plan + spec; the reviewer did not
build or run tests.

Verified independently: all 24 handlers compared against the deleted file's minus-lines (statuses, catch arms and both
`Created` locations byte-identical; cluster reordering proven wire-neutral by route precedence); the three payload
records serialise identically (`Message` ← anonymous member inference → `message`; `SkippedCodes` already an
`IReadOnlyList<string>` in `BulkCreateCodedValues`); the 5 assertions are real `TestServer` HTTP assertions on exact
body text, not self-fulfilling; the pin is threaded per host, guarded against a failed document and a zero-operation
document; the four new route files stay in-cluster with no `RequireAuthorization`/flag reads; scope contains nothing
outside `src/Settings/**` and `tests/**`.

The reviewer adjudicated the round's one contract question: the "17 operations changed from a fabricated 200" claim is
a **document correction only** — every handler return reproduces the pre-split arm set, so no client-visible status or
body changed.

**P2 dispositions:** (1) spec status row said "not implemented" while its checklist was complete → **fixed** (now
`Implemented`); (2) round doc sections stale → **fixed** (this document). Neither is a code finding.

## Acceptance

**Verdict: CLOSED.** Parent-run authoritative numbers:

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** (281 warnings) |
| `SchoolCollab.ArchitectureTests.Unit` | 126 / 0 failed |
| `SchoolCollab.Settings.Api.Tests.Unit` | 29 / 0 failed (24 pre-existing + 5 new) |
| `SchoolCollab.Settings.Tests.Unit` | 592 / 0 failed |
| **Total** | **747 tests, 0 failures** |

**Non-vacuity probe (parent-run, reverted):** `GET /deleted` reverted to untyped `Results.Ok` → pin assertion failed
**18 vs 19** with the full 12 + 7 + 5 message; restored → 126/126 green. Tree verified identical to the frozen patch.

**Criteria 1–9:** all PASS — 24 endpoints reproduced exactly, 6 route files, one guard call, `approve` the only
`IResult`, pin 19 measured, delta additive, build/tests green.

**Spec corrections recorded (not silently rewritten):**
1. §1.3 claimed "the other 12 return no body" — wrong: **7 operations write `{"message":…}` on a 400/409**, so the pin
   is 19, not 12. A 4xx arm *with a payload* documents `content`; the first draft counted arms as bodyless.
2. §WP4 carried the same wrong number and message text — both corrected to the measured split.

**Residual / follow-ups:**
1. **Scope still open:** the other 8 Settings orchestrators (42 operations), Students (146) and Assignments remain
   untyped — the pin reads **19 of 66** Settings, **0 of 146** Students, and it forces a deliberate bump per converting
   PR. Follow the proven pattern in `.github/skills/minimal-api-composition/SKILL.md` §3.
2. `Assignments.Api` still cannot generate its OpenAPI document (`JsonException` on `System.Guid`) — pinned by
   `OpenApiSurfaceArchitectureTests`; its own round (parent spec §7.1).
3. `AssignmentRoutes.cs` split (51 endpoints) remains deferred (parent spec D5).
4. **Delivered** — three commits on `feat/minimal-api-typed-responses` (code split, tests, docs), opened as
   PR [#364](https://github.com/stayboy/School-Collab/pull/364), not merged; this status line is the fourth
   commit. The tree also contained another session's
   `documents/rounds/round-notification-templates-b1.md`, which stayed out of this round's commits.
