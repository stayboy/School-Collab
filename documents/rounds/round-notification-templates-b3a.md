# Round B3a — notification templates: applicability endpoint + shared client

**Status:** PLANNING · Mode: Tier 3 **lean** (no UI — the editor page and grade card are B3b) · Provider: **pi**
Orchestrator/plan: `ollama-cloud/glm-5.3-flash` · Plan gate: `ollama/glm-5.3:cloud` ·
Worker: `ollama-cloud/deepseek-v4.1-flash:1017` · Diff gate: `ollama-cloud/deepseek-v4-pro:0813` · Acceptance: `deepseek-v4-pro:0813`
Branch: `feat/notification-templates-b3a` off `main` (`72973144`) · Spec: `documents/specs/assignment-notification-templates.md` §3.11, §5

Owner rulings folded: **Q79** (B3 before Spec B) · **Q80** (B3 split: B3a substrate, B3b the two UI surfaces) · **Q81** (the AppHost bring-up diagnosis is **B3b's step 0**, not this round; the rest of the residuals stay in an end-of-scope round).

**Plan-review history:** iteration 1 — REWORK (2 P1, 5 P2): my order rule said "constraint **count**" where §3.1.1 says **distinct dimensions**, and claimed the Q67 default-first promotion was part of the card's order (it is a *selection* rule; the `IsDefault` highlight carries it) → folded. Iteration 2 — REWORK (1 P1, 2 P2): my justification "the store persists raw un-normalized values" was false — `TemplateConstraint.Create` **trims at write**, so the whitespace half of the WS3 fixture was unreachable and vacuous; the variant is now **casing only** (which does discriminate: `Trim()` preserves case, `ToString("D")` is lowercase) → folded. **The ≤2 bound is spent**; the round proceeds to the worker on the owner's Q80 approval, with no third gate run.

## Substrate inventory (taken before planning)

| Question | Answer found | Consequence |
|---|---|---|
| Does the applicability endpoint exist? | **No** — `NotificationTemplateRoutes.cs` maps list / get-default / POST / PUT-by-id / DELETE-by-id / audit, and nothing under `applicable/` | WS1 adds it |
| What are the **real** template routes? | `GET /notification-templates` · `GET /{kind}/{channel}` · `POST /` · `PUT /{id:guid}` · `DELETE /{id:guid}` · `GET /{kind}/{channel}/audit` (`NotificationTemplateRoutes.cs:35,47,61,84,115,141`) | the client targets these, **not** the spec §5 table (see the divergence row) |
| Is spec §5 current? | **No** — it still documents `PUT …/{kind}/{channel}` (upsert) and `DELETE …/{kind}/{channel}`; B2c replaced both with by-id routes (`round-notification-templates-b2c.md`, WS1) | the client must follow the **code**; the spec table is corrected by a **doc-layer PR** (§ L3 forbids `documents/specs/**` in a code round) — recorded here as an as-built correction |
| Template wire shape? | `TenantNotificationTemplateDto(Id, Kind, Channel, Subject, Body, Constraints, CreatedAt, UpdatedAt)`; `TemplateConstraintDto(Id, Dimension, Value)`; `Create…Request` / `Update…Request` / `TemplateConstraintRequest` (`DTOs/TenantNotificationTemplateDto.cs`) | reuse them; only the applicability DTOs are new |
| Client precedent? | `public sealed class AssignmentPolicyApiClient(HttpClient http)` — absolute paths (`/api/settings/…`), base address `https+http://settings-api` from DI, tenant carried by `TenantPropagationDelegatingHandler` (`Admin.Shared/Services/AssignmentPolicyApiClient.cs:64`) | `NotificationTemplateApiClient` mirrors it exactly |
| Where is it registered? | `Settings.Application/ModuleServices.cs:78` (`services.AddHttpClient<AssignmentPolicyApiClient>(…)`) | WS2 registers there; `Students.Application` registration is **B3b's** (it does not register it today) |
| Dimensions? | `TemplateConstraintDimension { GradeLevel = 0, Stream = 1, ActivityGroup = 2, Subject = 3, AssignmentType = 4 }` | the applicability filter keys on `GradeLevel` only |
| Can Settings resolve display names? | `Settings.Core/Services/CodedValueResolver.cs` exists | constraints are returned with a resolved `Display`, falling back to the raw value |
| Route-shape hazard? | `/notification-templates/applicable/{guid}` **matches both patterns** — the existing route's `{kind}`/`{channel}` are unconstrained (an enum **model binding** failure is a post-match 400, not a route constraint), so "applicable" and the guid fit them too | **pattern precedence** (literal segment beats parameter) is what selects the literal route; the enum binding only explains what a mis-selected match would have returned. WS3's paired resolution test is the real pin — it fails if either half is stolen |

## Workstreams

**WS1 — the endpoint (Settings).**
1. `GET /api/settings/notification-templates/applicable/{gradeLevelId:guid}` → `TypedResults.Ok<ApplicableNotificationTemplatesDto>`, tenant-scoped through the existing global filter, mapped before the `/{kind}/{channel}` route in `NotificationTemplateRoutes.cs`. The Settings OpenAPI surface pin (`OpenApiSurfaceArchitectureTests`, `SettingsSchemaPin = 51`, asserted against 67 operations) is **re-measured and raised in the same PR** — the B2c precedent made that an explicit WS1 step and the same omission would redden architecture tests.
2. Query `GetApplicableNotificationTemplates(gradeLevelId)`:
   - take the tenant's live rows; **applicability filter** = the `GradeLevel` constraint is **absent** *or* **contains** this grade id, compared **trimmed and `StringComparison.OrdinalIgnoreCase`** against `gradeLevelId.ToString("D")`. The ground is **the store trims at write and preserves case** (`TemplateConstraint.Create` does `Value = value.Trim()`, and both write paths route through it), so the stored value is already the id text — but its **casing** is whatever the author sent, while `Guid.ToString("D")` is lowercase; the matcher compares `OrdinalIgnoreCase`, so the card must too or a hand-authored uppercase id would match at publish time and disappear from the card. The read-side trim is defensive only (a non-store writer); other dimensions never filter (they are *displayed*, per §3.11 — a template constrained to another stream still "can apply" to this grade in principle);
   - group into the **authorable slots**: 4 kinds (Publish / Reminder / Completion / Overdue) × 2 channels (**Email**, **SMS**) = 8 slots, each present even when empty so the card renders the trio uniformly. A WhatsApp-channel template **cannot exist** — `TenantNotificationTemplate.Create` throws on `NotificationChannel.WhatsApp` and the route maps that to a 400 — so the query's WhatsApp exclusion is **defensive only** and no fixture can reach it through the API;
   - order within a slot by **distinct constrained dimensions descending, then `CreatedAt` ascending, then `Id`** (specificity = count of *distinct* dimensions, never the row count: three GradeLevel values on one candidate is **one** dimension — §3.1.1). This is a **display order mirroring the matcher's sort keys**, and the plan is explicit that it is not the matcher's *selection* rule: Q67's default-first promotion on a top tie is deliberately **not** replicated, because the card's `IsDefault` highlight carries that meaning instead (and a pure count-desc sort would otherwise put the default, at count 0, last — which is what the highlight is for);
   - `IsDefault` = the row with no constraints (at most one per slot, enforced by B2c's 409).
3. New DTOs in `Settings.Core/DTOs`: `ApplicableNotificationTemplatesDto(IReadOnlyList<ApplicableTemplateSlotDto> Slots)`; `ApplicableTemplateSlotDto(Kind, Channel, IReadOnlyList<ApplicableTemplateDto> Templates)`; `ApplicableTemplateDto(Guid Id, string? Subject, bool IsDefault, IReadOnlyList<ApplicableConstraintDto> Constraints)`; `ApplicableConstraintDto(TemplateConstraintDimension Dimension, string Value, string Display)` — `Display` resolved via `CodedValueResolver` when the dimension is code-backed, `AssignmentType` from the enum name, raw `Value` as the fallback.
4. No migration, no schema change, no cross-context reference.

**WS2 — `NotificationTemplateApiClient` (Admin.Shared).**
Methods, absolute `/api/settings/notification-templates…` paths, primary-constructor `(HttpClient http)` exactly like the precedent:
`ListAsync()` · `GetDefaultAsync(kind, channel)` (**204 → null**) · `CreateAsync(request)` · `UpdateAsync(id, request)` · `DeleteAsync(id, reason)` · `GetAuditAsync(kind, channel)` · `GetApplicableAsync(gradeLevelId)`.
Failure posture: **throw** on non-success (an admin surface must show the error; fail-open is the *render* path's posture, not the editor's) — the single exception is the documented 204 on `GetDefaultAsync`. Register in `Settings.Application/ModuleServices.cs` beside the existing `AddHttpClient<AssignmentPolicyApiClient>`.

**WS3 — tests.**
- **Route resolution (the trap):** `/notification-templates/applicable/{guid}` reaches the applicability handler **and** `/notification-templates/{kind}/{channel}` still reaches the default-row handler for a real kind — both asserted in one test, so a future route insertion cannot silently steal either.
- **Applicability (discriminating):** a grade-constrained template whose value is this grade appears; a value differing only by **casing** (an uppercase form of the same id) still appears and `IsDefault` stays false — the fixture a plain `Ordinal` compare fails, and the only variant a store-reachable fixture can express, since `TemplateConstraint.Create` **trims** at write and the entity's setters are private; one constrained to a *different* grade is excluded; **each slot's own unconstrained row appears in that slot flagged `IsDefault`** (one default belongs to one kind × channel — it is not repeated across slots); a stream-constrained row **appears** (other dimensions display, never filter); display values resolve for code-backed dimensions and fall back to the raw value when unknown; the response always carries exactly **8 Email/SMS slots**, and no WhatsApp slot exists (asserted on the slot shape — the store throws on a WhatsApp row, so no fixture is needed or reachable).
- **Order (discriminating):** one candidate with **three GradeLevel values** (one dimension) must rank **below** a candidate constraining **two distinct dimensions** — the fixture that a row-count sort fails and a distinct-dimension sort passes.
- **Tenant isolation:** another tenant's rows never appear.
- **Client wire:** each method's path/method/body; `GetDefaultAsync` maps 204 → null and a 200 → the DTO; a 4xx/5xx **throws**; `GetApplicableAsync` hits the applicability path.
- **Spec divergence guard:** a test asserting the client's `UpdateAsync`/`DeleteAsync` paths contain `{id}`, not `{kind}/{channel}` — so the stale spec table cannot creep back in through a future edit.

## Explicitly NOT in this round

The editor page, the grade-detail card and the `Students.Application` client registration (**B3b**); the AppHost bring-up diagnosis (**B3b step 0**, Q81); the B2b browser pass, per-candidate audit attribution and the concurrent-create re-check (end-of-scope residuals round); any `documents/specs/**` edit (doc-layer PR per L3); Spec B.

## Risks

- **Two same-shape routes.** Both patterns match the URL; **pattern precedence** (literal beats parameter) selects the literal route, and a future route insertion or binding change could make `/applicable/{guid}` fall through to the default-row handler silently — the paired resolution test is the pin.
- **Ordering duplicated across contexts.** Settings restates the matcher's *sort keys* without the evaluator; the risk is drift. Mitigation: the comment names §3.1.1 and explicitly excludes the Q67 selection rule, and the discriminating fixture (three-values-one-dimension vs two-dimensions) fails any row-count implementation.
- **Display resolution is best-effort.** An unresolvable code value must render the raw value, never throw and never blank the card.

## Verification

`dotnet build SchoolCollab.slnx` 0 errors · `SchoolCollab.Settings.Tests.Unit` · `SchoolCollab.Settings.Api.Tests.Unit` · `SchoolCollab.ArchitectureTests.Unit` · `SchoolCollab.Students.Tests.Unit` (regression — it consumes Admin.Shared) · **MAUI head build** — required by the standing rule when a shared component changes: `dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj -f net10.0-windows10.0.19041.0 -m:1 -p:BuildInParallel=false` (WS2 edits `SchoolCollab.Admin.Shared`). Client-wire tests live in **`SchoolCollab.Settings.Tests.Unit`**, the existing home for Admin.Shared client tests (the `CodedValuesApiClient…` precedent).

## Acceptance — ACCEPTED (2026-10-20, `deepseek-v4-pro:0813`)

Criterion-by-criterion over the working-tree diff + named tests; nothing accepted on narrative.

| Criterion | Evidence |
|---|---|
| WS1 endpoint | `NotificationTemplateRoutes.cs:45-59` — `GET /notification-templates/applicable/{gradeLevelId:guid}` → `TypedResults.Ok<ApplicableNotificationTemplatesDto>`, registered **before** `/{kind}/{channel}` (`:63`). `GetApplicableNotificationTemplatesHandler.AppliesToGrade` (`:145-157`): `gradeConstraints.Count == 0 \|\| Any(string.Equals(c.Value.Trim(), gradeLevelId, OrdinalIgnoreCase))`, `gradeLevelId = …ToString("D")` (`:67`); other dimensions never filter |
| WS1 slots / default | 8 slots via `Kinds`(4) × `Channels`(Email, SMS) nested loop (`:33-52`); `IsDefault = Constraints.Count == 0`; WhatsApp `.Where(… != WhatsApp)` marked **defensive only** (the entity throws, `TenantNotificationTemplate.cs:60-63`) |
| WS1 order | `.OrderByDescending(DistinctDimensionCount).ThenBy(CreatedAt).ThenBy(Id)` (`:84-89`); `DistinctDimensionCount` = `.Select(Dimension).Distinct().Count()` (`:161`); header + DTO comments state the Q67 promotion is deliberately absent and carried by the `IsDefault` highlight |
| WS1 display | `ResolveDisplayAsync` (`:168-197`) — `AssignmentType`/non-Guid → raw; else `codedValueResolver.ResolveAsync(...).Name`; null or blank name → raw fallback; never throws, never blanks |
| WS1 pin | `OpenApiSurfaceArchitectureTests.cs:59` 51→52, message "51 + 1 = 52 of 68"; Architecture 129/0 |
| WS2 client | `NotificationTemplateApiClient.cs` — primary ctor, `BasePath = "/api/settings/notification-templates"`, seven methods, `EnsureSuccessStatusCode`, 204→null on get-default only; registered with `TenantPropagationDelegatingHandler` |
| WS3 tests | 21 new: 1 paired route-resolution, 10 handler (casing kill, distinct-dimension kill, 8-slot shape, `IsDefault`-per-slot, display fallback, tenant isolation…), 10 client wire incl. the by-id guard and the seven-method non-success sweep |
| Hygiene | no `documents/specs/**`, `.pi/skills/**`, `AGENTS.md`, migration, UI, `Version=`, new `InvalidOperationException`; the stale §5 divergence deferred to a doc-layer PR |

**Untested / undiscriminating:** none.

**Deviations recorded:** the Q67 promotion is deliberately absent from the display order (documented, `IsDefault` carries it) · `GetDefaultAsync` returns `null` on a 200-with-no-body (P2, report-only; matches the `AssignmentPolicyApiClient` precedent, unreachable through the real endpoint) · the `Admin.Shared` enum mirrors have **no automated drift guard**.

**Residuals confirmed parked (Q81):** AppHost bring-up diagnosis → **B3b step 0**; the B2b browser pass, per-candidate audit attribution and the concurrent-create re-check → **end-of-scope residuals round**.

**Follow-ups:** enum-mirror drift guard (or shared-kernel move) as its own small round/architecture test; the doc-layer §5 correction to by-id routes; B3b itself.

**Patterns to carry forward:** the order key is **distinct constrained dimensions, never row count** (three `GradeLevel` values = one dimension), and the whitespace half of the casing fixture was retired because `TemplateConstraint.Create` **trims at write** — casing is the only store-reachable variant, so the test that survives is the one that can actually fail.

## Verification (measured, canonical layout)

`dotnet build SchoolCollab.slnx` **0 errors** · Settings **623/0** · Settings.Api **43/0** · Architecture **129/0** (pin 52 of 68) · Students **628/0** · MAUI head (`-m:1 -p:BuildInParallel=false`) **0 errors**. The canonical build required an owner-authorized AppHost tree teardown (`taskkill /PID 36972 /T /F`) because a supervised `Settings.Api.exe` held its own output binaries; corroborating relocated runs matched, with only fixed-depth path assumptions failing under a relocated base directory.
