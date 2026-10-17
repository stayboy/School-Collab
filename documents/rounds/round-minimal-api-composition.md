# Round — minimal-api-composition

**Status:** CLOSED — verified; delivered on `feat/minimal-api-composition`, PR #363
**Tier:** 2 (light) · **Mode:** attended · **Provider:** clinepass
**Worker:** `cline-pass/deepseek-v4.1-flash` · **Reviewer:** `cline-pass/glm-5.3-flash` (substituted — the profile id `cline-pass/mimo-v2.6-flash` is not in this session's registry; still ≠ the worker's model)
**Round base:** `10e00ae3`
**Spec:** `documents/specs/minimal-api-composition.md` (D1–D7)
**Patch:** `documents/rounds/diffs-minimal-api-composition.patch` — 18 files, 1232 lines, regenerated after the parent's P2 hardening edits

## Plan

**Goal.** Make the repo's existing two-layer minimal-API composition the *written* policy, and give its
auth wiring a single write-point with a guard that cannot go vacuous — without changing any route URI,
HTTP verb, request/response shape or status code.

**Worker scope:** WP1 (helper), WP2 (migrate the guard blocks), WP3 (rewrite the bearer guard),
WP4 (composition guard), WP7 (OpenAPI surface guard). **Parent scope:** WP5 (governance docs),
WP6 (vendored skill + repo-owned delta skill).

**Acceptance criteria (as planned):** 1 policy-delegate identity · 2 null flag service ⇒ gate on ·
3 gateway-scheme sub-groups keep their policy · 4 zero hand-rolled guards · 5 `ConfigResolveRoutes`
exemption intact · 6 no route/DTO/status change · 7 guards non-vacuous · 8 new guards real and
non-vacuous · 9 build 0 errors + architecture tests 0 failures.

## Worker Report

Condensed from the worker's own report (full text: subagent artifact
`84693aea-…_worker_output.md`).

| WP | Status | Notes |
|---|---|---|
| WP1 | done | both overloads; default delegate character-identical; null flag service ⇒ gate on |
| WP2 | done | **12 files / 20 calls** — the spec's "13 files / 21 blocks" counted the exempt `AssignmentRoutes.cs` in-handler flag read and `ConfigResolveRoutes.cs`'s bool opt-in |
| WP3 | done | helper `OptIn` = 1; `Gate` counts 5 / 5 / 1×9 / 1; zero hand-rolled guards |
| WP4 | done | 3 assertions + `ReceiverExemptions` (1) + `LeafGroupExemptions` (5, each with a reason) |
| WP7 | done | Settings/Students: path coverage + non-empty `responses` + schema pin **0**; Assignments: defect pinned |
| WP5/WP6 | n/a | parent's scope, done by the parent |

Deviations accepted (all reviewed and approved during the round): `using Microsoft.AspNetCore.Builder;`
in the helper (the spec snippet does not compile without it); 12-not-13 arithmetic; `SettingsEndpointFiles`
8→9 (a pre-existing coverage gap — `NotificationTemplateEndpoints.cs` was never asserted);
`PortalSessionAdoptionArchitectureTests` re-anchored to the helper shape (counts unchanged at 3);
5 `LeafGroupExemptions`; csproj +3 `ProjectReference` and 2 CPM-resident packages.

**Trailing-slash finding (recorded, not absorbed).** A group-root route mapped at `/` under
`MapGroup("/contacts")` reaches the route table as `/contacts/`, while the generated document lists
`/contacts`. Any comparison of route templates against document paths must normalise both sides.

## Review

**VERDICT: CLOSED — no P1 findings.** (static review, diff + plan + worker report; reviewer did not build)

Verified independently by the reviewer: all 20 call sites preserve effective policy (15 Bearer-default,
2 gateway-scheme via the callback overload, 1 `AuthEndpointGroup` null-tolerant, `ConfigEndpoints`'s
bool-local guard proven equivalent); zero hand-rolled guards remain in `src/`; the three guards each have
a real tripwire; `LeafGroupExemptions` is neither over- nor under-inclusive (the five unlisted Auth leaf
files map nothing inline); the csproj adds no inline `Version=`; `PortalSessionAdoptionArchitectureTests`
was re-anchored, not loosened.

**P2 findings and dispositions:**

| # | Finding | Disposition |
|---|---|---|
| P2-1 | `BearerForwardingWiringArchitectureTests` doc comment said 13 files, assertion pins 12 | **Fixed** — comment now names 12 plus the exempt site |
| P2-2 | A *redundant extra* inline Bearer opt-in in a migrated file would pass every guard (the old per-file `OptInCount` caught it) | **Fixed** — `Count(OptIn).Should().Be(0)` added per migrated file; probe below proves it bites |
| P2-3 | `InlineMapCalls` omits `.MapPatch(` | **Fixed** — added |
| P2-4 | `NormalizePath` does not handle optional parameters (`{id?}`) — would fail, never silently pass | **Deferred** — safe direction; noted for the next editor |
| P2-5 | Spec §WP2 described `ConfigEndpoints.cs` as "Bearer (9)" when it was a `bool oidcEnabled` guard | **Fixed** in the spec |

**Best-practices check:** helper + tests comply with `.github/copilot/rules/dotnet-best-practices.md`
and CPM (no Never-list hits, XML docs present, no inline versions, minimal references).

## Acceptance

**Verdict: CLOSED.** Parent-run authoritative numbers (the only numbers this round trusts):

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors**, 281 warnings |
| `SchoolCollab.ArchitectureTests.Unit` | 126 / 0 failed |
| `SchoolCollab.Auth.Tests.Unit` | 224 / 0 failed (proves the null-flag-service "gate on" path) |
| `SchoolCollab.Assignments.Api.Tests.Unit` | 172 / 0 failed |
| `SchoolCollab.Students.Api.Tests.Unit` | 26 / 0 failed |
| `SchoolCollab.Settings.Api.Tests.Unit` | 24 / 0 failed |
| **Total** | **572 tests, 0 failures** |

**Non-vacuity probes (parent-run, each reverted; tree verified byte-identical to the patch afterwards):**

| Probe | Guard that fired |
|---|---|
| delete one `teachersGroup.RequireAuthIfOidc(...)` | `RequireAuthIfOidc_PerGroup_StudentsAssignmentsAndSettings` (4 ≠ 5) |
| re-introduce a hand-rolled flag guard | `NoHandRolledOidcFlagGuard_InMigratedEndpoints` |
| strip the helper's Bearer opt-in | `Helper_IsTheSingleBearerOptInWritePoint` (0 ≠ 1) |
| add a redundant inline Bearer opt-in (P2-2's fix) | `NoHandRolledOidcFlagGuard_InMigratedEndpoints` **and** `SettingsApi_DocumentCoversEveryMappedOperation` |

**Criteria:** 1–8 PASS (evidence in §Review and in the probe table); 9 PASS.

**Deviations from owner-approved spec text (all recorded, none silent):**
1. **D7 criterion (c) amended** — "every operation documents a response schema" was unachievable without
   the sweep D2 rejected. Replaced with a **pinned 0 baseline** (Settings 0/66, Students 0/146) whose
   message requires the number to be raised deliberately when handlers are converted.
2. **Assignments.Api pinned as a defect instead of degraded to path presence** — its OpenAPI document
   cannot be generated at all (pre-existing, see residual 1).
3. Arithmetic correction: **20 guard blocks across 12 files** migrated (not 21/13 — the 21st was the
   in-handler flag read in `AssignmentRoutes.cs`, and `ConfigResolveRoutes.cs` is the bool exemption).

**Residual / follow-ups:**
1. **Pre-existing defect, out of scope:** `Assignments.Api`'s OpenAPI document generation throws
   (`JsonException: The JSON value could not be converted to System.Guid`, from
   `JsonSchemaExporter.GetJsonSchemaAsNode` → `OpenApiDocumentService.GetJsonRequestBody`), so that host's
   *published* document is unavailable. `OpenApiSurfaceArchitectureTests` pins the failure — it goes red
   when the defect is fixed, and the pin is then deleted, not adjusted. Fixing it touches a request DTO
   (wire shape) ⇒ its own round. Recorded in the spec §8.
2. **Schema debt:** 0 of 212 documented operations declare a response schema; D2 (new/touched endpoints
   only) is the path. The pin forces the number up deliberately.
3. **Deferred by decision:** `AssignmentRoutes.cs` split (D5); `.http` artifacts (D6).
4. **Delivery:** committed on `feat/minimal-api-composition` (off `origin/main` at `d3328320`) as three
   commits + a docs commit, PR #363 to `main`. The round started on
   `feat/notification-templates-round-a`; that branch's PR #362 merged while the round was open, which
   dissolved the file dependency (`NotificationTemplateEndpoints.cs` now exists on `main`) and reverted
   the delivery to a plain `main`-based PR.
