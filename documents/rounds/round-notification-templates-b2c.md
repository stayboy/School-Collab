# Round B2c — notification templates: matcher substrate

**Status:** PLAN COMPLETE — **HELD AT WORKER DISPATCH** (owner, 2026-10-19; resume scheduled after 18:00 GMT). The ≤2 gate bound is spent; the owner's word at resume stands in for a third gate run. Next step at resume: dispatch the worker per this plan, seat `ollama-cloud/deepseek-v4.1-flash:1017`, branch `feat/notification-templates-b2c` off `main`.
**Plan-review history:** iteration 1 — **REWORK** (4 P1, 5 P2, zero open decisions) → folded. Iteration 2 — **REWORK** (1 P1, 2 P2) — **the ≤2 bound is spent**: the P1 was repo-settled (the publish handler already holds Targets/`TopicId`, so `AssignmentPublishedContext` carries the evaluation dimensions — one clause, zero extra reads) and both P2s were wording (the candidates read is unordered — the evaluator owns the order; `Unreadable` moved out of the pure evaluator matrix), so all three are folded and the plan stands **without a third gate run** — the owner's word at resume stands in for it, per the bound rule.
Mode: Tier 3 **lean** (no UI — the editor page is B3's) · Provider: **pi** ·
Orchestrator/plan: `ollama-cloud/glm-5.3-flash` · Plan gate: `ollama/glm-5.3:cloud` ·
Worker: `ollama-cloud/deepseek-v4.1-flash:1017` · Diff gate: `ollama-cloud/deepseek-v4-pro:0813` ·
Acceptance (plan author): `ollama-cloud/deepseek-v4-pro:0813`
Branch: `feat/notification-templates-b2c` off `main` · Spec: `documents/specs/assignment-notification-templates.md` (§3.1.1/Q15/Q16, Q67 amendment)

Owner rulings folded: **Q63** (matcher split into B2c; index + writer atomic) · **Q65** (writer identity in this round — a hard dependency, not a preference) · **Q66** (unconstrained row = tenant default, spec Q15; the invariant is a typed conflict) · **Q67** (ties: default first, then oldest `CreatedAt` — `UpdatedAt` moves on edit and would be non-deterministic).

## Substrate inventory (taken before planning)

| Question | Answer found | Consequence |
|---|---|---|
| Does the store allow >1 live template per (tenant, kind, channel)? | **No** — unique index `(TenantId, Kind, Channel)` filtered `is_deleted = false` (`TenantNotificationTemplateConfiguration.cs:36-39`) | WS1 relaxes it to **non-unique** (same columns, same filter — pure index swap, no data migration) |
| Does the writer survive a second candidate? | **No** — `UpsertTenantNotificationTemplateHandler` reads `SingleOrDefaultAsync(x => x.Kind == … && x.Channel == …)`; a second live row makes **every upsert throw** | WS1 converts the writer to create/update/delete **by id**, atomically with the index |
| Is there an all-rows read the Assignments side can consume? | **Yes** — `GET /notification-templates` returns all rows **with constraints** (`NotificationTemplateRoutes.cs:30`, `GetTenantNotificationTemplates`) | WS3 consumes the existing list (filter client-side by kind+channel); **no new Settings route** |
| Does the wire carry the tie-break input? | **No** — the DTO carries `updatedAt` but not `createdAt` (`TenantNotificationTemplateDto`); Q67's tie-break needs `CreatedAt` | WS3 adds `createdAt` to the DTO (additive) |
| Which routes are keyed by (kind, channel) and become ambiguous? | `PUT /{kind}/{channel}` (`:52`), `DELETE /{kind}/{channel}` (`:73`), `GET /{kind}/{channel}` (`:40`) | WS1 re-shapes them. Consumers: Round A's 9 wire tests **plus** `SettingsResponsePayloadTests.ResetNotificationTemplate_400` (`tests/SchoolCollab.Settings.Api.Tests.Unit/SettingsResponsePayloadTests.cs:270-281`, the keyed DELETE + its pinned 400 message) — **all ten updated in the same commit**. The fifth keyed route, `GET /{kind}/{channel}/audit` (`:90`), **stays keyed**: per-key merged audit stream, the config-flags per-key precedent; per-candidate attribution via `TemplateId` lands with B3's editor only if it needs it |
| How does a no-context single read behave with candidates? | Today "the" row; with candidates a context-less read can only mean the **tenant default**; `GetTenantNotificationTemplateHandler.cs:19-22` is `SingleOrDefaultAsync`-shaped and throws on a second live row | WS1 narrows `GET /{kind}/{channel}` to the unconstrained default row (204 when none) and drops the single-or-default throw — recorded as an as-built amendment |

## Workstreams

**WS1 — store + writer identity (Settings, atomically with the index).**
1. Migration: the `(TenantId, Kind, Channel)` filtered unique index → **non-unique** (same filter). No data change.
2. New commands: `CreateTenantNotificationTemplate` (new row + constraints; `POST /notification-templates`), `UpdateTenantNotificationTemplate` (by id; `PUT /notification-templates/{id}`), `DeleteTenantNotificationTemplate` (by id, audited tombstone; `DELETE /notification-templates/{id}`). The old (kind, channel)-keyed `PUT`/`DELETE` routes are **removed** — no production consumer exists (the editor is B3's), and their shape cannot address candidates.
3. Every mutation keeps writing `TemplateAuditEntry` in the same transaction (the recorder's prev/next summary unchanged).
4. The singular `GET /notification-templates/{kind}/{channel}` narrows to the **unconstrained default row** (204 when none): `GetTenantNotificationTemplateHandler` drops its `SingleOrDefaultAsync` for a default-row query — the same throw-on-second-row hazard as the upsert, closed **in the same PR**. The OpenAPI surface pin (`OpenApiSurfaceArchitectureTests`, Settings.Api pinned at 66 operations, the removed keyed DELETE named in the pin) is re-measured and raised in the same commit.

**WS2 — the default invariant (Q66/Q15).** A template with an empty constraint set is the tenant default (spec Q15). Create/update **rejects** a second unconstrained row for the same (kind, channel) with a typed conflict (`TenantNotificationTemplateConflictException`, the CodedValues `DuplicateCodeException` precedent) → 409. Application-level (a DB constraint cannot express "no child rows"), so the check runs in the same transaction as the write and is covered by a discriminating test. **Accepted race, recorded:** two concurrent creates can both pass the pre-insert check (the DB unique constraint that used to enforce this is being relaxed) — admin-only surface, single-writer UI, so the residual window is accepted rather than serialized; a post-insert re-check is the fallback if it ever bites.

**WS3 — candidates read (Assignments side, no new Settings route).** `INotificationTemplateResolver` gains `ReadCandidatesAsync(kind, channel, tenantId, ct)` → `NotificationTemplateSlotRead`-style result whose `Found` payload is the **candidate list** (id, subject, body, constraints, `createdAt`) — **unordered**: the read has no assignment context, so the total order is the evaluator's (Q67: default first, then oldest `createdAt`); no resolver test pins an order nothing requires. Implemented off the existing `GET /notification-templates` list, filtered client-side to (kind, channel); one HTTP read; **fail-open** (`Unreadable` on outage/timeout/non-JSON/5xx — the B2a `NotificationTemplateSlotState` posture, reusing `Unreadable` so §3.3's "outage never blocks a publish" keeps holding); genuine cancellation propagates. `createdAt` is added to `TenantNotificationTemplateDto` (additive). `NullNotificationTemplateResolver` returns `NotSet` with an empty list.

**WS4 — the evaluator (pure, Assignments.Core).** `NotificationTemplateEvaluator.Select(candidates, assignmentContext)`:
1. **Applicable** = every constrained dimension intersects the assignment's context (grade level, stream, activity group, subject/topic, assignment type — the spec §3.1.1 dimensions; a template with constraints in dimensions the assignment lacks is **not** applicable).
2. **Order** by specificity — the count of **distinct constrained dimensions**, not the row count (spec §3.1.1/§8: three GradeLevel values on one candidate is ONE dimension; a 2-dimension candidate outranks it).
3. **Ties** (Q67): the unconstrained default first, then the **oldest `createdAt`**.
4. Empty candidates / none applicable → built-in default.
Pure and unit-tested over the full matrix; no I/O.

**WS5 — selection wired into the send paths.**
1. **Render-time selection — stamp-first for Publish, pure evaluation for sweeps (spec §3.7/§6).** The publish broadcast carries the assignment's live stamps **and its evaluation dimensions** into `AssignmentPublishedContext` at queue time — the record gains the Targets kinds, `TopicId` and assignment type, populated by the publish handler that already holds them (`PublishAssignmentCommandHandler.cs:127` Targets, `:164`/`:175` TopicId — **zero extra reads**; the broadcaster itself has no DbContext, `AssignmentNotificationBroadcaster.cs:92-98`, so the dimensions must ride the context). A stamp naming a **live candidate** for the (kind, channel) renders it (republish reuse, §3.7); a cleared/deleted/foreign stamp falls to **evaluation over candidates** (now implementable: the dimensions are in the context) and then the built-in default (never blocks, B2a posture). The E3 sweeps stay pure evaluation — they have no stamps, and their queuer already builds its own context. Unreadable (outage) ⇒ built-in default + the fail-open posture; `NotificationLog.TemplateId` records the winner (null = built-in).
2. **Stamp validation** (`ValidateRequestedStampAsync`): an explicit stamp must name a **live candidate for the (kind, channel)** — existence, not applicability (applicability is the dialog's pre-select guidance, not a 400 gate). `NotSet` ⇒ 400; `Unreadable` ⇒ accept with warning (B2a posture unchanged); no matching candidate id ⇒ typed 400.
3. **`publish-options`** (`PublishOptionsDto`, `ContractTypes.cs:702`): each channel's slot grows the **ordered applicable list** (id + constraint summary + `isDefault`) — the Q16 pre-select source (most-specific pre-selects; a tie pre-selects the default). The existing single-slot fields (`PublishChannelOptionsDto`, `ContractTypes.cs:709-719`) are **pinned to the tenant-default row** (mirroring the narrowed GET) so they stay meaningful with candidates; stored stamps unchanged.
4. **Dead seam removed:** with all four call sites re-pointed (broadcaster, `SweepNotificationQueuer`, `ValidateRequestedStampAsync`, `GetPublishOptionsHandler`), `ResolveAsync`/`ResolveSlotAsync`/`ReadSlotAsync` are caller-less and are **removed** from `INotificationTemplateResolver` in this round — the six test doubles are updated with them. No production consumer remains; a dead seam invites drift.

## Explicitly NOT in this round

The editor page, the grade-detail applicability card and `NotificationTemplateApiClient` (**B3**); the publish dialog (**B2b**); the §5 `applicable/{gradeLevelId}` **suggestion** endpoint (B3's grade card consumes it); per-recipient template splitting (deferred, spec Q15).

## Contract changes (all deliberate, all recorded)

| Change | Kind | Consumer impact |
|---|---|---|
| `PUT`/`DELETE /{kind}/{channel}` → by-id routes | **breaking** (no production consumer; Round A's 9 wire tests updated with the contract) | none |
| `GET /{kind}/{channel}` narrows to the default row | semantic narrowing (B2a's stamp validation moves to candidates; the render path moves to evaluation) | internal |
| `createdAt` on the DTO; ordered candidates in `publish-options` | additive | B2b/B3 read them |

## Tests

- **Evaluator matrix (discriminating):** per-dimension intersect/non-intersect; a constrained dimension the assignment lacks ⇒ not applicable; specificity ordering by **distinct dimensions** — a 1-dimension/3-value candidate **loses** to a 2-dimension candidate; default-first tie; oldest-`createdAt` tie; no candidates ⇒ built-in.
- **Store/writer:** create/update/delete by id + audit rows; second unconstrained default ⇒ **409** (discriminating); index swap leaves existing single-row tenants untouched (regression guard).
- **Candidates read:** wire shape incl. `createdAt`; outage ⇒ `Unreadable` and publish proceeds; genuine cancellation propagates.
- **Selection:** the **stamped** Publish case — a live stamp renders that template and records its id (republish reuse); a cleared/deleted/foreign stamp falls to evaluation then built-in, `TemplateId` null (§3.7); the **sweeps** stay pure evaluation — most-specific applicable template, id recorded; none applicable ⇒ built-in; a stamp naming a live but non-applicable candidate is **accepted** (pre-select guidance, not a gate); a stamp naming a dead id ⇒ 400.
- **Narrowed GET (discriminating):** default + constrained candidates ⇒ the GET returns the unconstrained row; constrained-only ⇒ 204; a second live row never throws.
- **`publish-options`:** ordered list, `isDefault`, tie ordering matches the evaluator; stored stamps unchanged.

## Risks

- The writer/index change is atomic in **one migration + one PR** — a partial merge leaves `SingleOrDefault`-shaped readers throwing.
- Round A's wire tests encode the removed routes; they must be updated in the same commit or CI is red for a lie.
- The evaluator's dimension matching depends on the assignment context shape (grade/stream/group/subject/type) — it must degrade to "not applicable" on unknown dimensions, never throw.

## Verification

`dotnet build SchoolCollab.slnx` 0 errors · Architecture · Assignments.Unit · Assignments.Api.Unit · Settings.Unit (+ wire tests) · Families.Tests.Unit (regression) · Students.Unit (regression).

## Acceptance — ACCEPTED (2026-10-19, `deepseek-v4-pro:0813`)

Criterion-by-criterion over the working-tree diff (33 modified + 10 new files) + named tests. **No worker report existed** (the implementing worker timed out at 30 min / 919k tokens), so every row is evidenced by `file:line` or a test method, never narrative.

| Criterion | Evidence |
|---|---|
| WS1a index non-unique, filter kept | `TenantNotificationTemplateConfiguration.cs` + `20261019181554_RelaxTenantNotificationTemplateUniqueness.cs` `Up()` |
| WS1b migration reversible, settings-db only | `Down()` restores `unique: true`; `TenantNotificationTemplateUniquenessMigrationTests.Down_RestoresTheUniqueIndex` |
| WS1c by-id create/update/delete + audit in one transaction | the three handlers each `auditor.Record(...)` → single `SaveChangesAsync`; `Create_WritesRowAndConstraints_AndACreatedAuditEntry`, `Update_ReplacesConstraints_AndRecordsBeforeAndAfter`, `Delete_RemovesRowAndConstraints_AndWritesADeletedAuditEntry` |
| WS1d keyed PUT/DELETE removed | `NotificationTemplateRoutes.cs` maps only `MapPost` / `MapPut("/{id:guid}")` / `MapDelete("/{id:guid}")`; `UpsertTenantNotificationTemplate*` deleted (−109) |
| WS1e singular GET = default row, no throw | `GetTenantNotificationTemplateHandler.cs` `!x.Constraints.Any()` + `FirstOrDefaultAsync`; `GetOne_ReturnsTheUnconstrainedRow_…`, `…_ReturnsNull_WhenOnlyConstrainedCandidatesExist`, `…_NeverThrows_WithASecondLiveRow` |
| WS1f OpenAPI pin re-measured | `OpenApiSurfaceArchitectureTests.cs` 67 operations (66→67, −2/+3) |
| WS1g every consumer updated | `SettingsResponsePayloadTests.ResetNotificationTemplate_400` re-pinned on by-id DELETE |
| WS2 one default per slot ⇒ 409, same transaction, race recorded | `TenantNotificationTemplateDefaults.EnsureNoCompetingDefaultAsync` + typed conflict; `Create_SecondDefaultForTheSameSlot_IsAConflict` (asserts no row/no audit left behind); race documented in the docstring |
| WS3a candidates off the existing list, unordered | `HttpNotificationTemplateResolver.ReadCandidatesAsync` filters the list client-side; `SeveralCandidates_AreReturnedUnordered` |
| WS3b `createdAt` on the wire | `TenantNotificationTemplateDto.CreatedAt` + `NotificationTemplateCandidate.CreatedAt`; `CandidateRows_MapIdSubjectBodyConstraintsAndCreatedAt` |
| WS3c fail-open + cancellation | resolver catch chain; `ServerError_…`, `Timeout_ReadsUnreadable…`, `MalformedBody…`, `NonJsonPayload…`, `Timeout_DoesNotSwallowGenuineCallerCancellation` |
| WS3d null resolver ⇒ `NotSet` + empty | `NullNotificationTemplateResolver.ReadCandidatesAsync` |
| WS4a/b/c evaluator pure, intersect, distinct-dimension specificity | `IsApplicable` grouping; `AConstrainedDimensionTheAssignmentLacks_IsNotApplicable`, `AnUnknownDimension_…NeverThrows`, `OneDimensionWithThreeValues_LosesToTwoDimensions` |
| WS4d tie at top ⇒ tenant default; no default ⇒ oldest; none ⇒ built-in | `Rank` promotion block; `TieAtTheTopSpecificity_FallsBackToTheTenantDefault`, `…_WithNoTenantDefault_PicksTheOldest`, `NoApplicableCandidate_SelectsNothing…` |
| WS5a/b stamps + dimensions on the context, zero extra reads; stamp-first Publish | `IAssignmentNotificationBroadcaster` record; `PublishAssignmentCommandHandler` passes `AssignmentTemplateContext.FromAssignment(assignment)`; `StampedCandidate_RendersTheStamp_NotTheMostSpecificMatch`, `DeadStamp_FallsToEvaluation_AndRecordsTheWinner`, `NothingApplicable_RendersTheBuiltInDefault_WithNoTemplateId` |
| WS5c sweeps pure evaluation, one query per assignment per pass, no `IgnoreQueryFilters` | `SweepNotificationQueuer.ResolveTemplateAsync`/`GetDimensionsAsync` + per-pass `CandidateCache`/`DimensionsCache`; `PureEvaluation_SelectsTheMostSpecific…` |
| WS5d stamp validation existence-only | `ValidateRequestedStampAsync` switch; `ExplicitStamp_NamingALiveButNonApplicableCandidate_IsAccepted`, `…_ThatIsNotTheLiveTemplate_IsRejected_400`, `…_DuringSettingsOutage_IsAcceptedAndPersisted` |
| WS5e publish-options ordered list + `isDefault`, single-slot fields on the default row | `GetPublishOptions.cs` `Rank(...)`; `EmailSlot_ListsOnlyTheApplicableCandidates_MostSpecificFirst`, `SingleSlotFields_ArePinnedToTheTenantDefaultRow` |
| WS5f dead `Resolve*Async` seam removed | grep: no remaining refs in `src`/`tests` (only the policy resolver's own `ResolveAsync`) |
| WS5g `TemplateId` = winner (null = built-in) | broadcaster returns `winner?.ToResolved()`; asserted on `NotificationLog` rows |
| Hygiene | only `src/**` + `tests/**` (+ this round doc); no `Version=` on `PackageReference`; `TypedResults` only; the round's one `InvalidOperationException` was **replaced** by the typed 400; no secrets; `models.json` untracked and excluded |

**Untested / undiscriminating:** none.

**Deviations recorded (owner-accepted):** `POST /notification-templates` → **200** with the created DTO (no honest `Location` without a by-id GET); by-id `DELETE` → **404** on absent/foreign id (a tombstone has no idempotent "already unset" state).

**Gate trail:** plan gate REWORK ×2 (bound spent, owner-approved; catches were repo-settled facts — dimensions already in hand, candidates read unordered) → diff gate REWORK ×1 (the tie rule: §3.7's "tie → tenant default" was a no-op) → parent fix + 2 discriminating tests → re-verify **PASS** (new test provably fails pre-fix) → acceptance **ACCEPTED**.

**Follow-ups:** B2b/B3 consume `PublishChannelOptionsDto.Templates` + `createdAt` (no production consumer yet); per-candidate audit attribution only if B3's editor needs it (audit stays keyed per (kind, channel)); the accepted concurrent-create race has no runtime mitigation beyond the evaluator's tie-break, with a post-insert re-check recorded as the fallback.

**Lasting regression guards:** (1) the evaluator's **default-promotion-on-top-tie** and (2) **distinct-dimension specificity** — both are easy to silently "simplify" back to first-match / row-count, and both are load-bearing matcher correctness.

## Verification (measured)

`dotnet build SchoolCollab.slnx` **0 errors** (17 pre-existing advisory warnings) · Architecture **129/0** · Assignments.Unit **1274/0** · Assignments.Api **188/0** · Settings **603/0** · Settings.Api **42/0** · Families **57/0** · Students **628/0** — **2921 tests, 0 failures**.
