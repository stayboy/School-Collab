# Round — minimal-api-typed-responses-settings

**Status:** CLOSED — verified; delivered on `feat/minimal-api-typed-responses-settings`, PR #365
**Tier:** 2 (light) · **Mode:** attended · **Provider:** clinepass · **Owner override:** Tier 2 for an endpoint/contract change — the pattern and its traps were measured and reviewed in PR #364; this round repeats that template over 42 routes (spec D22)
**Worker:** `cline-pass/deepseek-v4.1-flash` (`worker`) · **Reviewer:** `cline-pass/glm-5.3-flash` (`reviewer`, read-only) — profile substitute (`cline-pass/mimo-v2.6-flash` absent from the registry); still ≠ the worker's model
**Round base:** `4fb36f28` (main, after PR #364). Dirty at start: the spec (the plan) plus two **other-session** round docs, never staged.
**Spec:** `documents/specs/minimal-api-typed-responses-settings.md` (D16–D23)
**Patch:** `documents/rounds/diffs-minimal-api-typed-responses-settings.patch` — 15 files, 1254 lines, `src/Settings/**` + `tests/**` only

## Plan

Finish the Settings host: convert the remaining **42 routes across 11 route files** to `TypedResults` with explicit
unions, name the 21 anonymous payloads, and raise the schema pin from 19 to the **measured** value — no route, verb,
status, body or `Location` change. Worker scope WP1–WP4/WP6; parent WP5 (docs).

## Worker Report

| Item | Result |
|---|---|
| WP1 | `ApiResponsePayloads.cs` created; `MessageResponse` moved out of `CodedValueResponsePayloads.cs`, which keeps `IdResponse` + `BulkCreateResponse`; CodedValue code compiled untouched (same namespace) |
| WP2 | 42/42 routes converted across 11 files; zero bare `Results.*` remain; `ConfigResolveRoutes` keeps its `WebApplication` receiver + `if (oidcEnabled)` gate; `ConfigFlag`'s `CreateFlagResponse(Guid Id)` and `EntityCodeRule`'s `GeneratedCodeResponse(string Code)` named locally; `EntityCodeRule`'s create reuses the shared `IdResponse` (deviation 2) |
| WP3 | Pin **50** (measured); message states `24 + 28 + 5 + 9 = 66` and `19 + 28 + 3 = 50` |
| WP4 | `SettingsResponsePayloadTests.cs` — 8 tests, exact body text + status (+ one `Location`) |
| WP6 | 66 operations before **and** after; paths/verbs identical; `content` 19 → **50**; 0 lost; 31 gained (list in the artifact) |

Deviations reported: (1) pin 50 not the predicted 52 — see Acceptance; (2) `IdResponse` reused for entity-code-rule
create instead of a new local record (byte-identical, asserted); (3) `TypedResults.Json` does not document its 400
(see Acceptance, resolved by the reviewer's finding); (4) cosmetic comments only.

## Review

**VERDICT: CLOSED — no P1 findings.** Static review of the frozen patch against plan + spec; no build, no test run.

Verified: arm table rebuilt from the patch and matched against §1.3 for **42/42 routes** — status sets, payload
expressions (including the interpolated entity-code message and the verbatim reason-guard string), `Created` locations,
catch-arm order/types; 85 `Results.*` sites converted 1:1. `MessageResponse`/`ErrorResponse` serialize identically
(`message`/`error`); the record move is namespace-neutral; `CreateFlagResponse`/`GeneratedCodeResponse`/`IdResponse`
property names match the anonymous objects they replace. The two 4-arm `IResult` handlers are exactly §1.3's arms with
arm-naming comments, and are the only new `IResult` handlers. Pin non-vacuous in both directions (generation failure
fails the test; a reverted handler drops 50 → 49). Scope: 15 files, no `documents/`, no `AGENTS.md`, no CPM change.

**P2 dispositions:**

| # | Finding | Disposition |
|---|---|---|
| P2-1 | Spec predicted 52; measured 50 | **Fixed** — spec §1.2/D18/§WP3/checklist corrected, with the reason (the two `IResult` 4-arm handlers cannot document arms) |
| P2-2 | Checklist's "`ErrorResponse`-backed schema" unsatisfied — and the gap was **not forced**: `BadRequest<ErrorResponse>` is wire-identical and *does* document the 400 | **Fixed in code** — both sites swapped to `TypedResults.BadRequest(new ErrorResponse(…))`; the two exact-body payload tests prove the wire is unchanged (see Acceptance) |
| P2-3 | Pin message's "9 + 2 = 11 body-less" clause isn't self-checkable against 50 | **Fixed** — the message now states `66 − 50 = 16` non-documenting, split 5 pilot + 11 this round (9 body-less + the 2 `IResult` handlers) |
| P2-4 | Pre-existing: nothing asserts `operationsChecked == 66` | **Deferred, recorded** — an operation dropped from a documented path would escape the pin; noted for the next touch of that test |

## Acceptance

**Verdict: CLOSED.** Parent-run authoritative numbers:

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| `SchoolCollab.ArchitectureTests.Unit` | 126 / 0 failed (pin = 50) |
| `SchoolCollab.Settings.Api.Tests.Unit` | 39 / 0 failed (31 + 8 new) |
| `SchoolCollab.Settings.Tests.Unit` | 585 / 0 failed |
| OpenAPI delta | 66 → 66 operations; no path/verb change; `content` 19 → 50; 0 lost |

**Criteria 1–9:** PASS. Settings is now a **complete** typed host: 50 of 66 operations document a schema; the 16 that
do not are 5 from the pilot and 11 here — 9 genuinely body-less routes plus the 2 four-arm handlers kept on `IResult`,
which cannot carry response types.

**Spec/plan corrections recorded (not silently rewritten):**
1. **52 → 50.** My §1.2 counted all five four-xx-only operations as contributing; two of them are the 4-arm `IResult`
   handlers, and a declared `IResult` documents nothing. 19 + 28 + **3** = 50. The reviewer independently confirmed the
   reasoning from the generated-document delta.
2. **`TypedResults.Json` documents nothing.** `JsonHttpResult<ErrorResponse>` carries no response metadata, so the two
   400 arms would have gone undocumented. The reviewer found the gap was avoidable: `TypedResults.BadRequest(new
   ErrorResponse(…))` is wire-identical (400, `application/json`, same `{"error":…}` body, same app `JsonOptions`) and
   documents the 400. Both sites now use it, and the two exact-body payload tests passing is the proof of wire identity.

**Residual / follow-ups:**
1. Students (146 operations) is the next host; it stays `0 of 146` and the pin will force the bump.
2. `Assignments.Api` still cannot generate its OpenAPI document (pre-existing, defect-pinned).
3. Schema-*content* assertions were deferred until this round landed — the document is now stable enough to pin
   `CodedValueDto`'s properties; not done here.
4. P2-4: `OpenApiSurfaceArchitectureTests` does not assert `operationsChecked == 66`.
5. **Delivered** on branch `feat/minimal-api-typed-responses-settings`, PR #365 (three commits: code, tests, docs+artifacts).
   The tree also carries two other-session round docs (`round-notification-templates-b1.md`, `plan-review-b1.packet.md`)
   that must never be staged — they did not enter any commit on this branch.
