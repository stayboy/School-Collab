tier 3 **full** (UI round — deterministic UI trigger fires); provider `ollama-cloud`; orchestrator `ollama-cloud/glm-5.3-flash`; plan-review `ollama-cloud/glm-5.3`; worker `ollama-cloud/deepseek-v4.1-flash`; diff-review `ollama-cloud/kimi-k2.7-code`; ui-tester `ollama-cloud/minimax-m3`. Round base `93d340cc9297db07398a9f47f68445df4ff1c782` (`main`, HEAD).

**Tree is dirty at round start — by owner decision, not by accident.** Round A (`assignment-policy-core`) is deliberately uncommitted (owner Q1, 2026-09-30): its 66-file delta is frozen at `documents/rounds/diffs-assignment-policy-core.patch`. **B1's diff must be isolated from it.** Isolation base = full-tree plumbing snapshot `1c54220b8534ed17bf8c529f0a986a9cbc5b2178` (created with `git add -A` → `git write-tree` → `git commit-tree`; no branch, no ref, working tree untouched, index restored). It was re-taken a second time once the owner's Q9–Q11 decision record settled, so **B1's patch carries only B1's work** — not the parent's §9/§8.5 decision record. The parent freezes B1 as `git diff 1c54220b…` (plus `add -N` for B1's new files). Regenerate the base with the same three commands if it is ever GC'd. **No agent may stage, reset, or commit Round A's delta.**

**Plan-gate model id — recorded deviation (mapping, not a model change).** The skill names `ollama/glm-5.3:cloud` for the Tier-3-**full** plan gate. This profile exposes **only** the `ollama-cloud` provider — `ollama/…` does not resolve (verified against `~/.pi/agent/models-store.json`: 17 models there; `glm-5.3` = 753B FP8, `glm-5.3-flash` = 321B). The gate therefore runs on **`ollama-cloud/glm-5.3`**, the profile's non-flash (higher) GLM 5.3 — the skill's stated intent with the exact id that resolves. The owner's 2026-09-30 rule (a *lean* plan gate must never share the worker's model) is not exercised here: this is a full round.

**Owner decisions carried in (grill-me round 2, 2026-09-30 — "recommend as-is"; do NOT re-litigate):** Q3 full Tier 3 · Q5 **B1 = consumption + enforcement only** (the subtractive sweep is B2) · Q6 derived `RequiresApproval` on the assignment list/detail DTOs, resolved server-side · Q7 caps validated on the write path **and** defensively uncapped-when-non-positive in the filter · Q8 promote the durable contract to `documents/specs/assignment-policy.md` at B1 close. Authority: `documents/solution/assignment-policy-fields.md` §3 (D1–D8, pinned) and §9 (Q3–Q8 + the Round A verification finding).

# Round assignment-policy-ui (B1) — assignment policy UI + enforcement (NO retirement)

Round **B1 of 3** for `documents/solution/assignment-policy-fields.md` (D1–D8 pinned; §9 owner
decisions). Round A shipped the data/protocol layer uncommitted in this tree; B1 makes that
policy **reachable and enforced**. **B2 owns every deletion** (§8.5) — if B1 deletes a legacy
surface, it is doing B2's job.

**IN:** the grade-detail `AssignmentPolicyEditor` card + grid + field-edit dialog (`Global |
This-grade` scope) absorbing the signature row and retiring `GradeSignaturePolicyEditor`
**together with its test file** `tests/SchoolCollab.Admin.Tests.Unit/GradeSignaturePolicyEditorTests.cs`
(the deletion is **build-coupled** — once the component goes, that test project does not compile,
so it cannot be deferred to B2); the create-wizard pre-fill + `Mandatory` lock; the approval
surfaces moved off `FeatureFlagGate` onto the effective policy; the two recipient role caps
enforced in `NotificationRecipientFilter` with write-path validation.

**OUT (B2, not here):** deleting `FEATURE:RequireAssignmentApproval` from its remaining artefacts
(AppHost `Parameters:` + the `Program.cs:335/:498` fan-out, `SchoolCollab.Admin/appsettings.json:15`
cold-start fallback, the `MigrationService` seeder, and the Config `/config-flags` row); dropping
the two `requires_signature_default` columns; deleting the 9 R2 legacy compat members
(`LegacyUpsert*Request` + the bool `UpsertAsync`/`UpsertGradeAssignmentPolicyAsync` overloads,
both `[JsonIgnore]` `RequiresSignatureDefault` properties, and the server records' legacy
`RequiresSignatureDefault` parameter).

**Clarification — the two `Assignments.Application` UI gates are IN scope (the earlier wording in
this header was wrong).** IN-3 edits `Detail.razor:142` and `Index.razor:66/:163/:390` in place to
consume the server-derived `RequiresApproval`, so the flag *reference* leaves those two files. That is
**not** flag retirement: the flag constant, the Config row, the seeder, the AppHost parameter and the
Admin cold-start fallback all stay, and the server-side D3 OR stays live. Those artefacts become
caller-less once B1 lands — kept deliberately for the deploy window (R2), removed in B2.

## Plan

### (a) Goal

Make the Round A policy **reachable and enforced** (Q5 = consumption + enforcement only; the
subtractive sweep is B2): a four-row assignment-policy grid on grade detail (`Global | This-grade`
field-edit dialog) replacing the Guardian Signature card and its component + test file (build-coupled
deletion); the create wizard pre-filling from the effective `SignatureRequirement` and locking the
checkbox under `Mandatory`; the two approval surfaces gated by a **server-derived**
`RequiresApproval` on the assignment summary DTO (Q6) instead of the client-side flag read; and
`NotificationRecipientFilter` enforcing the two recipient role caps (D4/D5) with **Q7**
non-positive⇒400 write-path validation. No EF migration, no flag retirement, no compat-member
deletion.

### (b) Scope gates

**IN (B1) — four workstreams, planned to the call-site:**
1. `AssignmentPolicyEditor` (grid) + `AssignmentPolicyFieldEditDialog` (`Global | This-grade`) on
   grade detail, mirroring `GradeNotificationPolicyEditor`/`NotificationPolicyFieldEditDialog` —
   absorbing the signature row; `GradeSignaturePolicyEditor.razor` **and**
   `tests/SchoolCollab.Admin.Tests.Unit/GradeSignaturePolicyEditorTests.cs` deleted together
   (build-coupled — the Admin test project stops compiling once the component goes).
2. Create-wizard pre-fill from `EffectiveAssignmentPolicy.SignatureRequirement` + the `Mandatory`
   lock (checkbox disabled + explanatory tooltip, the `PromptLocked` precedent); the persisted
   assignment value stays the snapshot.
3. Approval surfaces (`Detail.razor:142`, `Index.razor:66/:163/:390`) moved off the client-side flag
   onto a derived `RequiresApproval` projected onto `AssignmentSummaryDto` by both read handlers
   (Q6); **the D3 OR stays live** — the derived field is `policy.RequiresApprovalBeforePublish ||
   flag`, resolved server-side.
4. `NotificationRecipientFilter.Apply` enforces the two role caps (D4 Primary=Primary-guardian
   contacts, Copy=other guardians'; student contacts uncapped); D5 `MaxNotifications` untouched,
   whichever cap is smaller wins; **Q7** non-positive caps ⇒ `400` at both upsert routes +
   defensively uncapped in the filter.
5. Docs: findings-doc B1 record; **Q8** spec promotion at close (`documents/specs/assignment-policy.md`).

**OUT (B2, must NOT appear in the diff):** deleting `FEATURE:RequireAssignmentApproval` from its
remaining sites (AppHost `Parameters:` + `Program.cs:335/:498` fan-out,
`SchoolCollab.Admin/appsettings.json:15` cold-start fallback, `MigrationService/Program.cs:415`
seeder, the Config `/config-flags` row); dropping either `requires_signature_default` column;
deleting the 9 R2 compat members (`LegacyUpsert*Request`, the bool
`UpsertAsync`/`UpsertGradeAssignmentPolicyAsync` overloads, both `[JsonIgnore]`
`RequiresSignatureDefault` properties, the two server request records' legacy parameter). The two
**UI gates** are consumed in place by IN-3 (the flag reference disappears from the two razor files —
that is exactly what B2's "6 sites" then lacks) but the flag constant, the Config row, the handlers'
D3 OR and the seeder stay. The D3 OR stays live this round.

### (c) Verified seams (read by the orchestrator from disk, cite-worthy)

| Seam | Evidence |
|---|---|
| Grid-card pattern to mirror | `src/Students/SchoolCollab.Students.Application/Components/Students/GradeNotificationPolicyEditor.razor` (grid + Edit/Reset actions + `Inherit global` badge); its test `tests/SchoolCollab.Admin.Tests.Unit/GradeNotificationPolicyEditorTests.cs:28` |
| Field-edit-dialog pattern to mirror | `…/Students/NotificationPolicyFieldEditDialog.razor` — `DialogShellBase` + `ShowShellDialogAsync`, Global\|This-grade split panels, save-writes-only-changed-sides; test `NotificationPolicyFieldEditDialogTests.cs`; field-value editor `NotificationPolicyFieldValueEditor.razor` |
| Card host to replace (single) | `…/Components/Pages/Students/GradeLevels/Detail.razor:221-228` — the `Guardian Signature` `FluentCard` hosting `<GradeSignaturePolicyEditor GradeLevelId="@Id" />`; grep confirms this is the component's **only** host — no nav/entry point is orphaned |
| Component + test to delete (build-coupled) | `…/Components/Students/GradeSignaturePolicyEditor.razor` (bool switch + tri-state select over the legacy wire) and `tests/SchoolCollab.Admin.Tests.Unit/GradeSignaturePolicyEditorTests.cs` |
| Grade policy client (already fields-shaped) | `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs:1020-1033` — `GetGradeAssignmentPolicyAsync` (204/404⇒null) + `UpsertGradeAssignmentPolicyAsync(UpsertGradeAssignmentPolicyRequest)` (record widened by Round A at :2258); legacy bool overload :1041 **stays** until B2 |
| Tenant policy client (already fields-shaped) | `src/SchoolCollab.Admin.Shared/Services/AssignmentPolicyApiClient.cs:40-58` — `GetAsync` (204⇒null) + `UpsertAsync(UpsertAssignmentPolicyRequest)`; legacy bool overload **stays** until B2 |
| Server endpoints for the card | Students `GET/PUT /grade-levels/{id}/assignment-policy` (`GradeLevelRoutes.cs:269-295`); Settings `GET/PUT /api/settings/assignment-policy` (`AssignmentPolicyRoutes.cs`) — **no new endpoint or client method needed** |
| Signature-default endpoint (wizard learns mode here) | `src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs:70-87` — always-200 fail-open `{ requiresSignature }`, `requiresSignature = mode != Disabled` pinned by `AssignmentSignatureDefaultRouteTests`; widened additively in B1 (IN-2) |
| Wizard pre-fill seam | `…/Assignments/Create.razor:206-208` (checkbox), `:487-491` (`_requiresSignature`), `:606-615` (tenant-default pre-fill on init, grade still null), `:727-745` (`ResolveSignatureDefaultAsync` on grade change, fail-open); client `AssignmentsApiClient.GetSignatureDefaultAsync` — its **only UI caller** is Create.razor (verified) |
| `Mandatory` lock precedent | `…/Assignments/QuestionGenerationSection.razor:16-17` — `Disabled="@PromptLocked" Tooltip="@(PromptLocked ? "Your organization has locked the AI prompt." : null)"`; page wiring `Create.razor:319-320/:665-666`, `Edit.razor:94-99` |
| Approval-surface gates being replaced | `…/Assignments/Detail.razor:142` (`<FeatureFlagGate Key="@FeatureFlagKeys.RequireAssignmentApproval">` around the approval panel); `…/Assignments/Index.razor:66` (column `@if (_approvalRequired)`), `:159-169` (page-level flag read), `:390` (row action `_approvalRequired && …`) |
| Derived-RequiresApproval projection seams | `AssignmentSummaryDto` (`ContractTypes.cs:86-125`, trailing-optional params precedent `AvailableFromUtc`/`ArchiveGraceDays`); both read handlers project inside `HybridCache.GetOrCreateAsync` state-tuple lambdas: `ListAssignmentsQueryHandler.cs`, `GetAssignmentByIdQueryHandler.cs` |
| Resolver + flag injectable in Core | `IAssignmentPolicyResolver` in `src/Assignments/SchoolCollab.Assignments.Core/Services/IAssignmentPolicyResolver.cs` (fail-open contract documented); `IFeatureFlagService`/`FeatureFlagKeys` already injected in a Core handler (`PublishAssignmentCommandHandler` ctor) |
| Recipient filter + single caller | `src/Assignments/SchoolCollab.Assignments.Core/Services/NotificationRecipientFilter.cs` (`MaxNotifications is >= 0` defensive precedent :44-45); only caller `PublishAssignmentCommandHandler.cs:63`, which already holds `assignmentPolicy` (:47) |
| D4 role split | `AssignmentRecipient.Role` (`src/Assignments/SchoolCollab.Assignments.Core/Domain/AssignmentRecipient.cs:31`, mirrored from `StudentGuardian`); `GuardianRole { Primary=0, CC=1 }` (`Students.Core/Domain/GuardianRole.cs`) ↔ `GuardianRoleDto` (`ContractTypes.cs:492-497`, Primary/CC by int value) |
| Q7 route precedent | Notification-policy PUT maps `ArgumentOutOfRangeException` ⇒ 400 (`GradeLevelRoutes.cs:265`); the Settings assignment-policy PUT currently has **no** try/catch (added in B1) |
| Caps stored-but-unvalidated today | `SetPolicy`/`SetOverride` store caps as given (`TenantAssignmentPolicy.cs`, `GradeAssignmentPolicy.cs`) — per Round A §8.2 the range rule lands in B1 |
| Test infra | `FakeHybridCache : HybridCache` + `FakeFeatureFlagService` patterns in `tests/SchoolCollab.Assignments.Tests.Unit`; scripted `HttpMessageHandler` + bUnit patterns in `Admin.Tests.Unit`; flake guidance `fix-flaky-bunit-fluentui-after-cascade` |

### (d) Expected files

| # | File | Role |
|---|---|---|
| 1 | `src/Students/SchoolCollab.Students.Application/Components/Students/AssignmentPolicyEditor.razor` | NEW — grid of the four fields × (Global value \| Grade override \| Actions) with Edit+Reset, mirroring `GradeNotificationPolicyEditor` |
| 2 | `…/Students/AssignmentPolicyEditor.razor.css` | NEW — scoped styles (no `<style>` block; `blazor-css-isolation`) |
| 3 | `…/Students/AssignmentPolicyFieldEditDialog.razor` | NEW — `DialogShellBase` dialog, `Global settings | This grade` split; save writes each scope **only if changed** |
| 4 | `…/Students/AssignmentPolicyFieldValueEditor.razor` | NEW — per-kind input: `Mode` (select: clear-sentinel + Disabled/Optional/Mandatory), `Bool` (select: clear-sentinel + Yes/No — a nullable bool, not a switch), `Int` (number field, blank ⇒ null) |
| 5 | `…/Pages/Students/GradeLevels/Detail.razor` | MODIFY — Guardian Signature card → `Assignment Policy` card hosting `<AssignmentPolicyEditor GradeLevelId="@Id" />` (D6) |
| 6 | `…/Students/GradeSignaturePolicyEditor.razor` | DELETE — absorbed by the new card (D6; §8.5 B1 list) |
| 7 | `tests/SchoolCollab.Admin.Tests.Unit/GradeSignaturePolicyEditorTests.cs` | DELETE — build-coupled with #6 |
| 8 | `tests/SchoolCollab.Admin.Tests.Unit/AssignmentPolicyEditorTests.cs` | NEW — bUnit, scripted HTTP: both scopes rendered per field, Inherit badge per unset field, save round-trip, load-failure error bar |
| 9 | `tests/SchoolCollab.Admin.Tests.Unit/AssignmentPolicyFieldEditDialogTests.cs` | NEW — Global-only, Grade-only, both-scope writes (`NotificationPolicyFieldEditDialogTests` pattern); no-change save writes nothing |
| 10 | `tests/SchoolCollab.Admin.Tests.Unit/GradeLevelDetailPageTests.cs` | MODIFY — card-wiring assertion swaps `GradeSignaturePolicyEditor` → `AssignmentPolicyEditor` (the `Detail_RendersSignaturePolicyCard` method at :1265-1276, component-name assert at :1270 — plan-review P2-6; :1256 belongs to the notification card) |
| 11 | `src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs` | MODIFY — `/signature-default` response widens additively: `{ requiresSignature, signatureMode }` (enum name via the type-level `JsonStringEnumConverter`); `requiresSignature` derivation unchanged |
| 12 | `src/Assignments/SchoolCollab.Assignments.Application/Services/AssignmentsApiClient.cs` | MODIFY — widen the private response record; `GetSignatureDefaultAsync` resolves the mode (only caller is #13 — verified) |
| 13 | `…/Assignments/Create.razor` | MODIFY — pre-fill `_requiresSignature = mode is Optional or Mandatory` at **both** existing call sites (init + grade change); `_signatureMandatory` disables the checkbox + tooltip |
| 14 | `tests/SchoolCollab.Assignments.Api.Tests.Unit/AssignmentSignatureDefaultRouteTests.cs` | MODIFY — `signatureMode` in the response; `requiresSignature` still derived |
| 15 | `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentCreateBunitTests.cs` | MODIFY — Optional pre-fill; Mandatory lock (disabled + tooltip, submitted create body still carries `requiresSignature: true`); resolve failure ⇒ checkbox stays enabled (fail-open) |
| 16 | `src/Assignments/SchoolCollab.Assignments.Contracts/ContractTypes.cs` | MODIFY — `AssignmentSummaryDto` gains a trailing **optional** `bool RequiresApproval = false` (positional constructions keep compiling; old JSON ⇒ `false`) |
| 17 | `…/Queries/ListAssignmentsQuery/ListAssignmentsQueryHandler.cs` | MODIFY — inject `IAssignmentPolicyResolver` + `IFeatureFlagService`; resolve per **distinct** `GradeLevelId` via the per-grade cache (e-4) — **the flag read is wrapped in try/catch defaulting `false`** (plan-review P2-3); project `RequiresApproval = policy.RequiresApprovalBeforePublish \|\| flag` into every summary |
| 18 | `…/Queries/GetAssignmentByIdQuery/GetAssignmentByIdQueryHandler.cs` | MODIFY — same projection for the single assignment's grade |
| 19 | `…/Assignments/Detail.razor` | MODIFY — `<FeatureFlagGate …>` (:142) → `@if (_item.RequiresApproval)`; no client flag logic remains |
| 20 | `…/Assignments/Index.razor` | MODIFY — drop the page-level flag read (:159-169) + `_approvalRequired`; the Approval column renders iff **any** loaded row has `RequiresApproval`; chips + the Submit-for-approval row action (:390) use the per-row `RequiresApproval` |
| 21 | `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentIndexBunitTests.cs` | MODIFY — the gate moves into the `MakeRow` fixture (row `RequiresApproval` in the DTO + `requiresApproval` in the mocked list JSON); flag-service registrations that existed only to feed the page gate are removed where the page no longer injects it |
| 22 | `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentDetailBunitTests.cs` | MODIFY — same fixture change for the detail panel |
| 23 | `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentRequiresApprovalProjectionTests.cs` | NEW — handler-level: once-per-distinct-grade resolution (per-grade cache keys), the OR cases, fail-open (built-in ⇒ DTO reflects only the flag ⇒ never ON from a failure) |
| 24 | `src/Assignments/SchoolCollab.Assignments.Core/Services/NotificationRecipientFilter.cs` | MODIFY — `Apply(recipients, EffectiveNotificationPolicy policy, EffectiveAssignmentPolicy assignmentPolicy)`; bucket by `AssignmentRecipient.Role`: `Primary` ⇒ `MaxPrimaryContacts`, `CC` ⇒ `MaxCopyContacts`; **split on `OwnerType`**, not on `Role`: `OwnerType.Student` rows are exempt from both caps (their `Role` is `null` *by construction* — `StudentGuardian.Role` is a non-nullable `GuardianRole`, so null role and student-owner are the same set; keep the `Role is null` check only as a redundant net); per-bucket `Take` **preserving the existing channel order**, then the untouched global `MaxNotifications` (D5 — the smaller cap wins); defensive: `is >= 1` ⇒ cap, non-positive/null ⇒ uncapped (Q7; `MaxNotifications is >= 0` precedent) |
| 25 | `…/Commands/PublishAssignmentCommand/PublishAssignmentCommandHandler.cs` | MODIFY — pass the already-resolved `assignmentPolicy` (:47) into `Apply` (:63); **no ctor change** |
| 26 | `tests/SchoolCollab.Assignments.Tests.Unit/NotificationRecipientFilterTests.cs` | MODIFY — both caps hit, one hit one miss, D5 interplay (smaller `MaxNotifications` wins), Role-null exemption, non-positive ⇒ uncapped, in-bucket order preserved |
| 27 | `src/Settings/SchoolCollab.Settings.Core/CQRS/AssignmentPolicies/Commands/UpsertTenantAssignmentPolicy/UpsertTenantAssignmentPolicyHandler.cs` | MODIFY — non-positive (`≤ 0`) `MaxPrimaryContacts`/`MaxCopyContacts` ⇒ `ArgumentOutOfRangeException` (null stays unset) |
| 28 | `src/Students/SchoolCollab.Students.Core/CQRS/GradeAssignmentPolicies/Commands/UpsertGradeAssignmentPolicy/UpsertGradeAssignmentPolicyHandler.cs` | MODIFY — same guard (null stays inherit) |
| 29 | `src/Settings/SchoolCollab.Settings.Api/Endpoints/AssignmentPolicyRoutes.cs` | MODIFY — the PUT gains try/catch mapping `ArgumentOutOfRangeException` ⇒ 400 |
| 30 | `src/Students/SchoolCollab.Students.Api/Endpoints/GradeLevelRoutes.cs` | MODIFY — the assignment-policy PUT gains the `catch (ArgumentOutOfRangeException)` ⇒ 400 arm (only that PUT; the notification PUT already has one) |
| 31 | `tests/SchoolCollab.Settings.Tests.Unit/Handlers/TenantAssignmentPolicyHandlerTests.cs` | MODIFY — 0/negative rejected on both caps; null accepted |
| 32 | `tests/SchoolCollab.Students.Tests.Unit/GradeAssignmentPolicyHandlerTests.cs` | MODIFY — same |
| 33 | `tests/SchoolCollab.Students.Api.Tests.Unit/GradeAssignmentPolicyRoutesTests.cs` | MODIFY — non-positive cap ⇒ 400 at the route |
| 34 | `tests/SchoolCollab.Settings.Api.Tests.Unit/AssignmentPolicyRoutesTests.cs` | NEW — thin TestHost route test: 400 on a non-positive cap, 200 on a valid fields body (mirrors `GradeAssignmentPolicyRoutesTests`) |
| 35 | `documents/specs/assignment-policy.md` | NEW — Q8 spec promotion at B1 close: the durable contract (§4 field set + §5 resolution/enforcement), the `notification-delivery-plan.md` precedent |
| 36 | `documents/solution/assignment-policy-fields.md` | MODIFY — append the B1 implementation record to §8 |
| — | VERIFY only (no edit expected): `AssignmentApprovalPolicyReconciliationTests.cs`, `PublishAssignmentApprovalGateTests.cs`, `ScheduleAssignmentCommandHandlerTests.cs` — their fakes return the built-in (uncapped) policy, so the `Apply` widening is behaviour-neutral for them; if a fixture asserts `Apply`'s old signature directly, update it in place and record the deviation | |

**36 expected files: 11 NEW, 23 MODIFY, 2 DELETE** (+1 VERIFY row).

### (e) Ordered implementation steps (worker can follow standalone)

1. Read: `documents/solution/assignment-policy-fields.md` (§3 D1–D8 + §9 Q3–Q8 pinned; §2/§4/§5/§6/§8);
   the seam files in (c); `.github/copilot/rules/dotnet-best-practices.md` +
   `.github/skills/dotnet-best-practices/SKILL.md`, `testing.md`, `blazor-components.md`,
   `section-card.md`; skills `dialog-ui`, `dropdown-ui`, `featureflags-tenant-gates`,
   `input-width-scale`, `blazor-css-isolation`, `test-dialog-opener-components`,
   `fix-flaky-bunit-fluentui-after-cascade`.
2. **Cap write-path (Q7)** — #27/#28 handlers guard non-positive caps (`≤ 0` ⇒
   `ArgumentOutOfRangeException`, naming the field), #29/#30 routes map it to `400`; tests #31–#34.   
   Null stays unset/inherit; the legacy bool upsert paths compile untouched.
3. **Recipient caps (D4/D5/Q7)** — widen `Apply` (#24): order, buckets, defensive branches exactly
   as in (d)#24; update the single caller (#25); extend `NotificationRecipientFilterTests` (#26).
4. **Derived `RequiresApproval` (Q6)** — widen `AssignmentSummaryDto` (#16, trailing optional), then
   both read handlers (#17/#18): inject `IAssignmentPolicyResolver` + `IFeatureFlagService` (add them
   to the `GetOrCreateAsync` state tuples), resolve per distinct `GradeLevelId` through a per-grade
   `HybridCache` entry — key `assignment-policy:effective:{tenantId}:{gradeLevelId}` (sentinel
   `none` for a null grade), cache options mirroring the handlers' existing `CacheOptions`
   (5-min expiration / 1-min local), tag `"assignments"` — and project
   `RequiresApproval = policy.RequiresApprovalBeforePublish \|\| await featureFlags
   .IsEnabledAsync(FeatureFlagKeys.RequireAssignmentApproval, ct)` — **that flag read must sit in its own try/catch defaulting to `false`** (plan-review P2-3): the *resolver* is fail-open, but the flag leg is not, and an unguarded call would turn a Config outage into a 500 on the assignments list/detail reads — a new availability coupling that today's client-side catch (`Index.razor:163-169`) prevents. The resolver is fail-open
   (transport failure ⇒ built-in policy ⇒ approval `false`), so a failed resolve can only ever
   suppress the policy leg — the gate can never turn ON because of a failure. Update the two UI
   surfaces (#19/#20): **the gate mechanism is no component at all** — the surface reads the derived
   DTO field with a plain `@if`. `FeatureFlagGate` is deliberately NOT reused for policy state: the
   semantic is now a server-resolved merged value, not a reactive client-side flag, and the derived
   field keeps the D3 OR alive through the deploy window (flag-ON tenants see today's surfaces;
   flag-OFF + policy-ON tenants gain them from the policy). Update bUnit fixtures + new projection
   tests (#21–#23).
5. **Editor card (D6)** — build #1–#4 mirroring the notification editor (dialog kinds: Mode/Bool/Int;
   blank sentinel = clear on the Global side, blank = inherit on the This-grade side; raw
   `FluentSelect` justified per dropdown-ui §1 — sentinel selection over a nullable value); swap the
   card on `GradeLevels/Detail.razor` (#5); **then** delete #6+#7 in the **same change-set**
   (build-coupled — the tree compiles only when both happen); component + dialog + page tests
   (#8–#10).
6. **Wizard (D2)** — widen the always-200 response (#11) additively (a consumer reading only
   `requiresSignature` keeps parsing), widen the client method (#12), wire pre-fill + Mandatory lock
   in `Create.razor` (#13) at **both** existing resolve call sites; failure ⇒ warning log + checkbox
   stays enabled (fail-open — never lock the author out on a transport failure). `Edit.razor` is
   untouched — the persisted value stays the snapshot (D2 scopes the lock to create). Tests
   (#14/#15).
7. Docs (#35/#36). `documents/configuration.md` needs **no** edit this round (Round A's §2/§5 flag
   rows already describe the reconciled posture); if the worker finds a row now materially wrong, it
   edits it and records the deviation.
8. Build rule: `dotnet build SchoolCollab.slnx` after every code-change batch (AGENTS.md), fix errors
   before continuing; then the affected test projects — `Admin.Tests.Unit`, `Assignments.Tests.Unit`,
   `Assignments.Api.Tests.Unit`, `Settings.Tests.Unit`, `Settings.Api.Tests.Unit`,
   `Students.Tests.Unit`, `Students.Api.Tests.Unit`, `ArchitectureTests` (the parent owns the
   authoritative numbers).
9. Do **not** stage/commit anything; do **not** add a migration; do **not** touch any B2 surface.

### (f) Acceptance criteria (numbered, with discriminating evidence)

1. **Grade-detail card lands; the signature card + its test are gone together.** Evidence: files
   #1–#5 present; `GradeSignaturePolicyEditor.razor` and `GradeSignaturePolicyEditorTests.cs` absent
   from the tree; grep across `src/` + `tests/` finds **zero** remaining **code** references
   (`Render<GradeSignaturePolicyEditor>`, component tags, `using`/type usage) — the one
   **doc-comment** mention at `tests/SchoolCollab.Settings.Tests.Unit/TenantAssignmentPolicyDtoCompatTests.cs:12`
   is a **declared residual, not a failure**: it describes the `[JsonIgnore]` compat member that
   deliberately stays until B2, and that file is not in (d) (plan-review P2-1); `GradeLevelDetailPageTests` wiring row updated; no
   nav/entry point broke (the component had exactly one host, verified in (c)).
2. **Global\|This-grade editing round-trips through the existing endpoints.** Discriminating tests:
   `AssignmentPolicyFieldEditDialogTests` — (a) Global-only save ⇒ exactly one
   `PUT /api/settings/assignment-policy`, fields-shaped body preserving the other three fields;
   (b) Grade-only save ⇒ exactly one `PUT /grade-levels/{id}/assignment-policy`, others preserved;
   (c) both scopes changed ⇒ both PUTs; (d) open + save with no edits ⇒ **zero** PUTs;
   (e) `AssignmentPolicyEditorTests`: the grid renders the tenant value in Global, the override or
   the `Inherit global` badge per field, and the load-failure path shows `FluentMessageBar`.
3. **No migration.** Evidence: the B1 diff contains no `Migrations/` or model-snapshot path; both
   contexts' `MigrationGuardTests` stay green in the worker's run. (Plan-review P2-5: a **negative
   scope gate** — proved by diff inspection *and* a behavioural guard, but it is not a new
   discriminating test.)
4. **Wizard pre-fill + Mandatory lock.** **Discriminating** (fails pre-B1): (b) `Mandatory` ⇒
   pre-ticked, **disabled**, tooltip rendered, and the submitted create body still carries
   `requiresSignature: true`. **Regression guards only** (already pass pre-B1 — `Create.razor:727-745`
   pre-ticks from the derived bool and fails open, and `AssignmentRoutes.cs:83-88` already derives
   `requiresSignature = mode != Disabled`; plan-review P2-2): (a) `Optional` ⇒ pre-ticked and
   enabled; (c) `Disabled` ⇒ unticked, enabled; (d) endpoint failure ⇒ checkbox at its prior value
   (fail-open). `AssignmentSignatureDefaultRouteTests` pins the additive wire: `{ requiresSignature,
   signatureMode }` with `requiresSignature == (mode != Disabled)`.
5. **Approval surfaces on the derived field; D3 OR live; fail-open.** Evidence: grep of the two
   razor files finds no `FeatureFlagGate`/`RequireAssignmentApproval` reference;
   `AssignmentRequiresApprovalProjectionTests`: (a) policy true + flag OFF ⇒ DTO `true`;
   (b) policy false + flag ON ⇒ `true` (deploy-window parity); (c) both off ⇒ `false`; (d) resolver
   failure + flag OFF ⇒ **`false`** — a failed resolve never turns approval ON; (e) two assignments
   sharing a grade resolve the policy **once** (per-grade cache key hit); (f) **flag-service throw ⇒
   `false`, and both pages still render** — the projection wraps `IsEnabledAsync` in try/catch
   defaulting false, so a Config outage degrades exactly like today (`Index.razor:163-169` catches and
   continues) instead of 500-ing the list/detail read (plan-review P2-3). bUnit: gating flows
   through `MakeRow`/JSON fixtures (`requiresApproval`), so column/chips/panel appear iff the DTO
   field is true.
6. **Recipient caps enforced (D4/D5).** Discriminating tests in `NotificationRecipientFilterTests` —
   (a) `MaxPrimaryContacts` trims only `Role == Primary` recipients, preserving the preferred-channel
   order; (b) same for `CC`/`MaxCopyContacts`; (c) **every `OwnerType.Student` row keeps `Role == null`
   and is consumed by neither cap** — plus a guard assertion on the invariant that a
   *guardian-owned* recipient always has a non-null `Role` (`GuardianRole` is an enum and
   `StudentGuardian.Role` is non-nullable, so a null role on a guardian-owned row is a **data
   defect**, not a bucketing choice); (d) a smaller `MaxNotifications` wins over either role cap;
   (e) null/non-positive caps behave as uncapped. The filter stays pure and static.
7. **Non-positive caps ⇒ 400 on both write paths.** Evidence: handler tests (#31/#32) prove the
   guard (`≤ 0` on either cap throws; null accepted); route tests (#33/#34) prove 400 on the wire
   and 200 on a valid fields body; the legacy bool upsert path still succeeds for
   `requiresSignatureDefault`-only bodies.
8. **B2 surfaces untouched.** Evidence (**negative scope gate** — diff inspection only, no behavioural
   test; plan-review P2-5): the diff edits none of — AppHost `Parameters:`+
   `Program.cs:335/:498` fan-out, `SchoolCollab.Admin/appsettings.json:15`,
   `MigrationService/Program.cs:415`, the Config `/config-flags` artifact, either
   `requires_signature_default` column — and deletes none of the 9 R2 compat members; the D3 OR in
   the two write handlers is unchanged.
9. **Hard gates hold.** Evidence: no cross-context `ProjectReference` added (ArchitectureTests
   green; the card uses the existing two clients only); no `.csproj` change at all expected and no
   `Version=` on any `PackageReference`; no secrets introduced; entities untouched.
10. **Build + tests green; docs written.** Evidence: `dotnet build SchoolCollab.slnx` 0 errors;
    the eight affected test projects 0 failures (parent owns the authoritative run);
    `documents/specs/assignment-policy.md` exists (Q8); the findings doc carries the B1 record.

### (g) Risks

- **R-B1-1 — approval-surface staleness.** `RequiresApproval` is baked into the DTO projection
  (5-min handler cache) on top of the per-grade policy cache (5-min). There is **no cross-context
  invalidation** — a Settings/Students policy change cannot clear an Assignments cache tag. A policy
  change can take up to ~5 min to appear on the list/detail surfaces; enforcement is never stale
  (the publish/schedule gates resolve fresh). Accepted — the flag had the same shape.
- **R-B1-2 — Mandatory fail-open window.** A transport failure during pre-fill leaves the checkbox
  unlocked, so an author could untick a signature the policy deems Mandatory; the persisted value is
  the snapshot and publish does not re-validate the mode. Accepted per the fail-open posture (never
  lock an author out on infrastructure failure); the next create re-locks.
- **R-B1-3 — stored-but-invalid caps.** Rows written before Q7 validation (Round A window or direct
  DB) may hold non-positive caps; the filter's defensive branch makes them uncapped rather than
  destructive. The 400 rule applies to new writes only. Accepted (Q7's explicit design).
- **R-B1-4 — `Edit.razor` signature checkbox stays unlocked.** D2 scopes the `Mandatory` lock to the
  create wizard, so an author can untick the snapshot afterwards: `Edit.razor:88-89` renders the same
  "Require guardian signature after completion" checkbox (`Id="assignmentEditRequiresSignature"`),
  loaded from `_item.RequiresSignature` (`:205`) and round-tripped on save (`:331`) into
  `Assignment.RequiresSignature` (`Assignment.cs:56`), which `Detail.razor:325` then gates
  `SignOffSection` on — so the policy's `Mandatory` intent is undoable per assignment. **Owner
  decision (Q11, 2026-09-30): agreed (a)** — the lock stays scoped to the create wizard (D2 unchanged)
  and closing this gap is tracked as a **separate follow-up** round, explicitly **not** B2 (B2 is
  deletion-only). **B1 must not touch `Edit.razor`** — recorded so the reviewer treats any edit there
  as out of scope.
- **R-B1-5 — bUnit cascade flakes.** The dialog tests drive multiple bound inputs before submit —
  the exact `fix-flaky-bunit-fluentui-after-cascade` case-2 shape. Tests must make each interaction
  observable before the next (no Change/Change/Submit chains).
- **R-B1-6 — `GuardianRole` mirror trust.** D4's split relies on `AssignmentRecipient.Role` being
  mirrored from `StudentGuardian` at recipient-resolution time (pre-existing code, not B1's delta).
  A guardian row with `Role = null` would fall outside the Primary bucket; `OwnerType.Student` rows
  are exempt via the explicit student test (AC6c).

### Constraints honoured (cited)

- **No cross-context `ProjectReference`** — the card consumes the existing `AssignmentPolicyApiClient`
  (Admin.Shared) + `StudentsApiClient` mirrors; the derived field is computed inside Assignments.Core
  against the Core-resident `IAssignmentPolicyResolver`.
- **CPM** — no new packages; every `PackageReference` stays versionless (no `.csproj` change
  expected).
- **Tenancy/entities untouched** — no entity or row-version change; policy rows stay on
  `BaseTenantEntityWithAudit` + `IHasRowVersion`.
- **No EF migration** — the four columns exist since Round A. Nothing in planning suggested one
  needs creating; if implementation appears to require one, that is a raised risk, not planned work.
- **Flag governance** — B1 consumes `FEATURE:RequireAssignmentApproval` read-side only; the OR lives
  server-side; one write-point per flag kind preserved (nothing added, moved, deleted — B2's job).
- **Round hygiene** — no agent stages/commits; the parent freezes the B1 diff against base
  `1c54220b…` (with `add -N` for new files); Round A's 66-file delta stays untouched.

---

## Worker Task Spec (compact)

ROLE: implement B1 exactly per **Plan (d)+(e)**. Four workstreams: (1) grade-detail
`AssignmentPolicyEditor` grid + `AssignmentPolicyFieldEditDialog` (`Global | This-grade`) replacing
the Guardian Signature card, deleting `GradeSignaturePolicyEditor.razor` **and**
`GradeSignaturePolicyEditorTests.cs` in the same change-set; (2) create-wizard pre-fill +
`Mandatory` lock via the additively-widened always-200 `/signature-default` response
(`{ requiresSignature, signatureMode }`), fail-open on failure; (3) server-derived `RequiresApproval`
(`policy \|\| flag`, per-grade HybridCache, fail-open — a failed resolve must never turn approval
ON) projected by **both** read handlers onto `AssignmentSummaryDto` and consumed with plain `@if` at
`Detail.razor:142`/`Index.razor:66/:390`; (4) the two role caps in `NotificationRecipientFilter.Apply`
(bucket by `Role`, student rows exempt, `MaxNotifications` untouched — smaller cap wins,
non-positive ⇒ defensively uncapped) + Q7 non-positive⇒400 guards in both upsert handlers mapped at
both routes. Then `documents/specs/assignment-policy.md` (Q8) + the findings-doc B1 record.

Read first, in order: `documents/solution/assignment-policy-fields.md` (D1–D8 + Q3–Q8 pinned — do
not re-litigate), this doc's (c) seam table, `.github/copilot/rules/dotnet-best-practices.md` +
`.github/skills/dotnet-best-practices/SKILL.md`, `.github/copilot/rules/testing.md`,
`.github/copilot/rules/blazor-components.md`, `.github/skills/dialog-ui/SKILL.md`,
`.github/skills/dropdown-ui/SKILL.md`, and the project skills `featureflags-tenant-gates`,
`input-width-scale`, `test-dialog-opener-components`, `fix-flaky-bunit-fluentui-after-cascade`.

Hard gates: **no EF migration**; no cross-context `ProjectReference`; no `Version=` on any
`PackageReference`; **no B2 surface touched** (§8.5 — the AppHost parameter + fan-out, the Admin
cold-start fallback, the `MigrationService` seeder, the Config row, both
`requires_signature_default` columns, the 9 compat members all stay; the D3 OR in the
publish/schedule handlers stays); no client-side policy logic on the approval surfaces (read the DTO
field only); entities untouched; **stage/commit/stash nothing**.
Build rule: `dotnet build SchoolCollab.slnx` after every code-change batch (AGENTS.md), fix errors
before continuing; then run the affected test projects listed in Plan (e) step 8.
Exit: all of Plan (f) AC1–AC10 demonstrable; report per AC with the discriminating evidence.

## Reviewer Task Spec (compact, static diff-only)

ROLE: verify the delivered B1 diff (frozen against base `1c54220b…`) against this round doc only.
Check: (1) files ⊆ Plan (d) (+ declared deviations recorded in the worker report); **no B2 path in
the diff** — the AppHost parameter + fan-out, `SchoolCollab.Admin/appsettings.json:15`, the
`MigrationService` seeder, the Config `/config-flags` artifact, both `requires_signature_default`
columns and all 9 R2 compat members unchanged and still present; (2) the D3 OR unchanged in `PublishAssignmentCommandHandler` +
`ScheduleAssignmentCommandHandler`; (3) no `Migrations/`/snapshot path in the diff and the migration
guards green in the worker report; (4) `RequiresApproval` derived **server-side** in both read
handlers as `policy \|\| flag`, cached per (tenant, grade), fail-open proven by test (resolver
failure + flag OFF ⇒ `false`) — **any client-side flag read left in the two razor files is a
blocker**; (5) the editor card uses only the existing clients/endpoints; global/grade upserts
preserve the other fields; no-change save writes nothing; the dialog is a `DialogShellBase` dialog
with no nested-dialog pattern; (6) `GradeSignaturePolicyEditor.razor` + its test deleted together
with zero remaining references; (7) the Mandatory lock is disabled+tooltip and fail-open on resolver
failure; the wizard never persists a policy value (snapshot semantics); (8) the filter splits by
`Role` with student rows exempt, `MaxNotifications` still applied, non-positive ⇒ uncapped; both
handlers reject non-positive and both routes map to 400; legacy bool upsert paths still work;
(9) no `.csproj`/CPM/secret change; entities untouched; UI conventions held (`DialogShellBase`, no
`<style>` blocks in markup, W-ladder widths, no raw `FluentSelect` without the sentinel
justification, `@key` on dynamic lists); (10) build + test outputs in the worker report are green —
cite them, do not run builds/tests yourself; (11) `documents/specs/assignment-policy.md` + the
findings-doc B1 record exist; (12) the **flag** read inside the projection is guarded (try/catch ⇒ false) so a Config outage cannot 500 the list/detail reads — absence is a **P1** (plan-review P2-3). Flag any diff outside (d) as a deviation to adjudicate.

## Worker Report

**Worker** `ollama-cloud/deepseek-v4.1-flash` (run `bcd90248`). **This pass crashed mid-flight** on a provider
`Connection error.` — exit 1 after ~20.7 min / 210 turns / 234 tool calls; a **transport** failure, not a
timeout, stall or design failure, and `resumeDisposition` was `resumable`. It died immediately after appending
§8.6 to the findings doc, **before emitting its report**. No work was lost: the parent froze a provisional
patch as insurance, re-ran the authoritative pass (**green**, below), then **resumed the same run** (revived as
`0cf5c1c5`, warm context) with a deliberately report-only brief — no rework, no re-verification, no builds. The
report below is that resumed pass's output. (Provenance recorded here because the pass was interrupted; note
this was **not** the skill's escalation path — escalation on `kimi-k2.7-code` is for stalled passes.)

**Changed files: 38** — NEW 9 · MODIFY 27 · DELETE 2. The plan expected 36 + a VERIFY row; the arithmetic
reconciles exactly as **36 + the approved csproj deviation + the broadcaster-fixture VERIFY row = 38**.

- **NEW** (9) — `AssignmentPolicyEditor.razor` + `.razor.css`, `AssignmentPolicyFieldEditDialog.razor`,
  `AssignmentPolicyFieldValueEditor.razor` (Students.Application); `AssignmentPolicyEditorTests.cs`,
  `AssignmentPolicyFieldEditDialogTests.cs` (Admin.Tests.Unit); `AssignmentRequiresApprovalProjectionTests.cs`
  (Assignments.Tests.Unit); `AssignmentPolicyRoutesTests.cs` (Settings.Api.Tests.Unit);
  `documents/specs/assignment-policy.md`.
- **DELETE** (2) — `GradeSignaturePolicyEditor.razor`; `GradeSignaturePolicyEditorTests.cs`.
- **MODIFY** (27) — *production 15*: `GradeLevels/Detail.razor` (Students UI), `AssignmentRoutes.cs`,
  `AssignmentsApiClient.cs`, `Create.razor`, `Assignments/Detail.razor`, `Assignments/Index.razor`,
  `ContractTypes.cs`, `ListAssignmentsQueryHandler.cs`, `GetAssignmentByIdQueryHandler.cs`,
  `PublishAssignmentCommandHandler.cs`, `NotificationRecipientFilter.cs`,
  `UpsertTenantAssignmentPolicyHandler.cs`, `AssignmentPolicyRoutes.cs`,
  `UpsertGradeAssignmentPolicyHandler.cs`, `GradeLevelRoutes.cs`; *tests 11*: `GradeLevelDetailPageTests.cs`,
  `AssignmentCreateBunitTests.cs`, `AssignmentIndexBunitTests.cs`, `AssignmentDetailBunitTests.cs`,
  `NotificationRecipientFilterTests.cs`, `AssignmentNotificationBroadcasterTests.cs`,
  `AssignmentSignatureDefaultRouteTests.cs`, `GradeAssignmentPolicyRoutesTests.cs`,
  `GradeAssignmentPolicyHandlerTests.cs`, `TenantAssignmentPolicyHandlerTests.cs`, the Settings.Api test
  `.csproj`; *docs 1*: `assignment-policy-fields.md` (§8.6.1–8.6.4).

**Build:** 0 errors — `dotnet build SchoolCollab.slnx` after **every** code-change batch (cap guards/routes →
filter → DTO + handlers → UI surfaces → card trio + deletions + page test → wizard route/client/Create + wizard
tests), each green; only pre-existing warnings.

**Tests** (worker's own runs vs the parent's authoritative pass):

| Project | Worker | Parent (authoritative) |
|---|---|---|
| Settings | 538/0 | **538/0** |
| Settings.Api | 9/0 | **9/0** |
| Students | 619/0 | **619/0** |
| Students.Api | 18/0 | **18/0** |
| Assignments | 711/0 at its last full run, then a filtered re-run of `AssignmentCreateBunitTests` 12/0 (the final `Create_MandatoryPolicy_LocksCheckboxAndSubmitsTrue` landed after the full run) | **712/0** |
| Assignments.Api | 94/0 | **94/0** |
| Admin | 610/0 | **610/0** |
| ArchitectureTests | not run | **72/0** |

The single-count difference on Assignments is explained exactly by the last test landing after the worker's full
run; the parent's **712/0** is the number of record. No repo-wide `dotnet test` was run — CI parity is the
parent's, and the 3 pre-existing live-OpenRouter failures remain the known out-of-scope blocker.

**Deviations from plan (all declared):**
1. `tests/SchoolCollab.Settings.Api.Tests.Unit/SchoolCollab.Settings.Api.Tests.Unit.csproj` gained
   `<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />` — approved **(a)**: no `Version` (CPM pins
   10.0.8), no `Directory.Packages.props` edit. The project had no `Microsoft.AspNetCore.App`/TestHost
   reference (`obj/project.assets.json` = `Microsoft.NETCore.App` only), so (d)#34 could not compile as
   written; the csproj's own header says the project exists to expose `Program` "for WebApplicationFactory
   use", so this completes its documented purpose.
2. `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentNotificationBroadcasterTests.cs` (`:129`/`:169`) updated
   in place for the widened `Apply` signature under (d)'s VERIFY row — the plan's "single caller" grep covered
   production only. Both fixtures now pass an uncapped policy, so their blocked-channel / `MaxNotifications`
   assertions and production behaviour are unchanged.
3. The dialog and value-editor have **no** `.razor.css` (not in (d)#3/#4): the `Global settings | This grade`
   split uses FluentUI `FluentGrid`/`FluentGridItem` plus the global `.dialog-container`/`.muted` utilities,
   because mirroring the notification dialog would mean copying its inline `<style>` block — which the repo's
   no-`<style>`-in-markup rule forbids.
4. `AssignmentPolicyFieldValueEditor` gained a `ClearLabel` parameter (default `Not set`; the dialog passes
   `Inherit global`) so one editor serves both the clear sentinel (global) and the inherit sentinel (grade) —
   beyond (d)#4's field list.
5. `ListAssignmentsQueryHandler`'s per-grade approval map is keyed by the grade **sentinel string** (shared with
   the cache key) rather than `Dictionary<Guid?, bool>` — the latter throws `ArgumentNullException` on the
   null-grade key, which AC5's sentinel test caught; grade-less assignments now project `false` instead of
   failing the read.
6. `AssignmentsApiClient.GetSignatureDefaultAsync` return type `Task<bool>` → `Task<SignatureRequirementMode>`
   per (d)#12, with a legacy-boolean fallback when `signatureMode` is absent; the two `AssignmentCreateBunitTests`
   call sites were updated inside (d)#15.
7. Recorded, **not** a deviation: `documents/configuration.md` left unedited (its §2/§5 flag rows remain
   accurate); `Edit.razor`, both `requires_signature_default` columns, the 9 R2 compat members, the AppHost
   parameter/fan-out, the Admin cold-start fallback, the `MigrationService` seeder, the Config `/config-flags`
   artefact and the D3 OR in the publish/schedule handlers are all untouched.

**AC mapping:** the worker reports AC1–AC10 all demonstrable with the discriminating test named per criterion —
AC2's `Dialog_NoChanges_WritesNothing` (zero PUTs) and the equal-value case, AC4's
`Create_MandatoryPolicy_LocksCheckboxAndSubmitsTrue`, AC5's four OR/fail-open cases plus flag-throw and
once-per-grade, AC6's bucket split with the `OnlyContain(r => r.Role != null)` guardian invariant, AC7's
DataRow 400s on both routes. **Deviations 5 and 6 are the ones worth the reviewer's scrutiny** — both were
caught by a test rather than assumed.

**Parent's authoritative pass (step 4a):** `dotnet build SchoolCollab.slnx` **0 errors** (17 pre-existing
warnings); **538 / 9 / 619 / 18 / 712 / 94 / 610 / 72 — all 0 failed**. Verified **no drift** between that pass
and the frozen patch.

**Patch frozen:** `documents/rounds/diffs-assignment-policy-ui.patch` — **38 files, +2899 / −630**, generated
as `git diff 1c54220b… -- src tests documents ':(exclude)documents/rounds'` (verified: zero round-doc paths
inside it).

## Review

### Plan review (step 2b)

Ran 2026-09-30 on `ollama-cloud/glm-5.3` — the profile's higher GLM 5.3 (see the header's id-mapping
note) — against the **plan only**, before any worker existed. **Verdict: ACCEPT — no P1.** This spends
the round's single plan-review iteration, so the worker is dispatchable on this plan.

Gates: UI **risk** · contract **none** · migrations **none** · secrets **none**.

The gate independently confirmed three load-bearing claims the plan depends on: `AssignmentSummaryDto`
ends in a trailing-optional `PublishedAt = null` (`ContractTypes.cs:~141`), so the planned trailing
`bool RequiresApproval = false` keeps every positional construction compiling and old/cached JSON ⇒
`false`; the additive `/signature-default` widening survives the client's Web-defaults `_jsonOptions`
(unknown members skipped), and the exact-body route tests are already in (d) #14; and **no EF
migration is required** — the four policy columns exist from Round A's two `AddAssignmentPolicyFields`
migrations while B1 touches only DTO/route/filter/UI code and no entity mapping.

Six P2s — all folded into `## Plan` as amendments **before dispatch**, without a second gate pass (the
iteration budget is spent and the verdict carried no P1):

| P2 | Finding | Folded as |
|---|---|---|
| 1 | AC1's "zero references" grep would false-fail on the doc-comment mention in `tests/SchoolCollab.Settings.Tests.Unit/TenantAssignmentPolicyDtoCompatTests.cs:12` (a file outside (d)) | AC1 scopes the grep to **code** references and pre-declares that mention as a residual |
| 2 | AC4's (a)/(c)/(d) cannot fail pre-B1 — `Create.razor:727-745` already pre-ticks from the derived bool and fails open, and `AssignmentRoutes.cs:83-88` already derives `requiresSignature = mode != Disabled` | AC4 re-labelled: only (b) and the route pin are discriminating; (a)/(c)/(d) are regression guards |
| 3 | The **flag** leg of the projection had no try/catch, so a Config outage would 500 the list/detail reads where today's `Index.razor:163-169` catch degrades to OFF | (d)#17, (e) step 4 and AC5(f) now require the flag read guarded (try/catch ⇒ false); reviewer check (12) makes its absence a P1 |
| 4 | `Role is null` recipients exempted from both caps, whereas D4's wording makes a null-role guardian an "other" (Copy) guardian | **RETRACTED — the gate's premise was wrong (owner challenge, 2026-09-30).** `GuardianRole` **is** an enum (`GuardianRole.cs`: `Primary = 0, CC = 1`) and `StudentGuardian.Role` is **non-nullable** (`StudentGuardian.cs:22`), so a *guardian-owned* recipient can never be null-role. `AssignmentRecipient.Role` is nullable (`AssignmentRecipient.cs:32`) only because a recipient row's owner may be a **Student** (`ContactOwnerType.Student`, `ContactOwnerType.cs:8`) — so `Role is null` and `OwnerType.Student` are the *same* set, which is exactly what the plan already exempts. Fix applied: key the split on **`OwnerType`** (the authoritative discriminator), keep `Role is null` as a redundant net, and pin the invariant with a guard assertion in AC6. **No owner decision was required.** |
| 5 | AC3 and AC8 are evidence-by-inspection rather than behavioural tests | Both re-labelled as negative scope gates |
| 6 | The signature-card wiring assert is `Detail_RendersSignaturePolicyCard` at `GradeLevelDetailPageTests.cs:1265-1276` (component-name assert :1270) — :1256 belongs to the notification card | (d)#10 corrected |

### Diff review

Ran 2026-09-30 on `ollama-cloud/kimi-k2.7-code` against the frozen patch
`documents/rounds/diffs-assignment-policy-ui.patch` (38 files, +2899/−630). **Verdict: PASS — no P1, no P2.**
Returned block, verbatim:

```
REVIEW
Verdict: PASS
P1:
P2:
Best-practices: No overwrites outside Plan (d); mandated UI skills honored (DialogShellBase dialog, no inline <style> blocks, w-9 width ladder, raw FluentSelect justified by the sentinel pattern); readable focused diff.
```

**Parent note on its evidentiary weight.** The block carries no per-check enumeration, so the parent independently
verified the load-bearing claims *before* accepting the PASS — none of the following is taken on the reviewer's
word: no `FeatureFlagGate`/`RequireAssignmentApproval`/`IFeatureFlagService` remains in either approval surface;
the folded plan-review **P2‑3** flag guard is present in **both** read handlers; no B2 path, no `Migrations/`
path, no added `Version=`, and a byte-identical `ScheduleAssignmentCommandHandler` (D3 OR intact); no remaining
**code** reference to `GradeSignaturePolicyEditor`; and the new `AssignmentRequiresApprovalProjectionTests` and
`NotificationRecipientFilterTests` are genuinely discriminating — all of AC5 (a)–(f) including the flag-throw
and once-per-distinct-grade cache cases, and all of AC6 (a)–(e) plus ordering and empty-set cases.

**Residual recorded from this pass (not a finding):** AC1 declared a *single* doc-comment residual
(`TenantAssignmentPolicyDtoCompatTests.cs:12`), but there are **8** — seven more on the retained B2 compat
surfaces (`AssignmentPolicyApiClient`, `Settings.Api/AssignmentPolicyRoutes`, `GradeLevelRoutes`,
`StudentsApiClient`, `GradeAssignmentPolicyDto`) plus one in the new `AssignmentPolicyEditor.razor` XML doc
explaining what it replaced. All are prose about deliberately-kept members; none is a code reference. The
plan's residual list was incomplete, and the deletion is pinned behaviourally by the
`NotContain("GradeSignaturePolicyEditor")` assertion at `GradeLevelDetailPageTests.cs:1275`.

## Acceptance

Adjudicated 2026-09-30 by the orchestrator against Plan (f), the authority doc §3 (D1–D8) + §9 (Q3–Q11), the diff-review `REVIEW` block (PASS, no P1, no P2), the parent's authoritative step-4a pass, and the parent's independent spot-checks (not re-derived here).

### AC1–AC10 checklist

| # | Criterion | Status | Evidence (named) |
|---|---|---|---|
| 1 | Grade-detail card lands; signature card + test gone together | **Satisfied** — with a recorded residual-list discrepancy | Files #1–#5 present in the patch; `GradeSignaturePolicyEditor.razor` + `GradeSignaturePolicyEditorTests.cs` deleted in the same change-set; spot-check: **zero remaining code references** to `GradeSignaturePolicyEditor` across `src/`+`tests/` — the behavioural pin is the `NotContain("GradeSignaturePolicyEditor")` assertion at `GradeLevelDetailPageTests.cs:1275`. **Discrepancy (recorded honestly, not a finding):** AC1 declared *one* doc-comment residual (`TenantAssignmentPolicyDtoCompatTests.cs:12`) but there are **8** — seven more on the retained B2 compat surfaces (`AssignmentPolicyApiClient`, `Settings.Api/AssignmentPolicyRoutes`, `GradeLevelRoutes`, `StudentsApiClient`, `GradeAssignmentPolicyDto`) plus one in the new `AssignmentPolicyEditor.razor` XML doc. All are prose about deliberately-kept members; none is a code reference. The plan's residual list was incomplete — carried in Residuals. |
| 2 | Global\|This-grade editing round-trips through the existing endpoints | **Satisfied** (behavioural) | `AssignmentPolicyFieldEditDialogTests` — Global-only ⇒ exactly one `PUT /api/settings/assignment-policy` preserving the other three fields; Grade-only ⇒ exactly one `PUT /grade-levels/{id}/assignment-policy`; both scopes ⇒ both PUTs; `Dialog_NoChanges_WritesNothing` ⇒ **zero PUTs**. `AssignmentPolicyEditorTests` — grid renders the tenant value per field, override or `Inherit global` badge, load-failure ⇒ `FluentMessageBar`. |
| 3 | No migration | **Satisfied** — **negative scope gate** (diff inspection + behavioural guard, not a new test) | Spot-check: **0** `Migrations/` paths in the 38-file frozen patch; the four policy columns exist since Round A's two `AddAssignmentPolicyFields` migrations; both `MigrationGuardTests` stay green inside the Settings/Students suite runs (no drift). |
| 4 | Wizard pre-fill + `Mandatory` lock | **Satisfied** (behavioural) | **Discriminating:** `Create_MandatoryPolicy_LocksCheckboxAndSubmitsTrue` (AC4-b — pre-ticked, disabled, tooltip, submitted body still carries `requiresSignature: true`); landed after the worker's full Assignments run, so the parent's **712/0** is the number of record. **Regression guards** (per plan-review P2-2, cannot fail pre-B1): Optional pre-ticked+enabled, Disabled unticked+enabled, resolve failure ⇒ checkbox at its prior value (fail-open). `AssignmentSignatureDefaultRouteTests` pins the additive wire `{ requiresSignature, signatureMode }` with `requiresSignature == (mode != Disabled)`. |
| 5 | Approval surfaces on the derived field; D3 OR live; fail-open | **Satisfied** (behavioural + scope gate) | Grep (scope gate): no `FeatureFlagGate`/`RequireAssignmentApproval`/`IFeatureFlagService` remains in `Assignments/Index.razor` or `Detail.razor` — clean. `AssignmentRequiresApprovalProjectionTests` carries **all of AC5 (a)–(f)** (verified discriminating, not taken on trust): policy-on/flag-off ⇒ true; policy-off/flag-on ⇒ true (deploy-window parity); both off ⇒ false; resolver fail-open ⇒ false (a failed resolve never turns approval ON); **flag-throw ⇒ false with the read still returning** (the folded plan-review P2-3 guard, spot-checked present in **both** `ListAssignmentsQueryHandler` and `GetAssignmentByIdQueryHandler`); once-per-distinct-grade (requested-grade set + a single cache key per grade); plus the null-grade sentinel path. bUnit: column/chips/panel flow through the `MakeRow`/JSON fixtures (`requiresApproval`). |
| 6 | Recipient caps enforced (D4/D5) | **Satisfied** (behavioural) | `NotificationRecipientFilterTests` carries **all of AC6 (a)–(e)** — `MaxPrimaryContacts` trims only Primary rows preserving channel order; same for CC; `OwnerType.Student` rows exempt from both caps with the guardian non-null-`Role` invariant guard (§9.2; the plan-review P2-4 amendment was **retracted**); smaller `MaxNotifications` wins (D5); null/non-positive ⇒ defensively uncapped (Q7) — plus ordering, one-cap-hit, and empty-set cases. Filter stays pure and static. |
| 7 | Non-positive caps ⇒ 400 on both write paths | **Satisfied** (behavioural) | Handler tests: `TenantAssignmentPolicyHandlerTests` + `GradeAssignmentPolicyHandlerTests` (`≤ 0` on either cap throws naming the field; null accepted). Route tests: `GradeAssignmentPolicyRoutesTests` + the new `AssignmentPolicyRoutesTests` (TestHost: 400 on a non-positive cap, 200 on a valid fields body; DataRow 400s on both routes). Legacy bool upsert path compiles and succeeds untouched. |
| 8 | B2 surfaces untouched | **Satisfied** — **negative scope gate** (diff inspection only, no behavioural test; per plan-review P2-5) | Spot-check: no B2 path in the 38-file patch — no AppHost `Parameters:`/`Program.cs:335/:498` fan-out, no `SchoolCollab.Admin/appsettings.json:15`, no `MigrationService` seeder, no Config `/config-flags` artefact, no `requires_signature_default` column, no `Edit.razor` (protected by owner Q11), none of the 9 R2 compat members deleted. `ScheduleAssignmentCommandHandler.cs` is **byte-identical** to the base and `PublishAssignmentCommandHandler` changes only its comment plus the widened `Apply` call — the D3 OR is intact. |
| 9 | Hard gates hold | **Satisfied** (scope gate + suite results) | ArchitectureTests **72/0** green — no cross-context `ProjectReference`; the card consumes only the two existing clients. Spot-check: **0** added `PackageReference … Version=` in the patch (CPM holds). The one `.csproj` change is the **declared, approved deviation 1** (`Settings.Api.Tests.Unit` + `Microsoft.AspNetCore.Mvc.Testing`, no `Version` — CPM pins 10.0.8) so that (d)#34 could compile. No secrets; entities untouched. |
| 10 | Build + tests green; docs written | **Satisfied** | Parent's authoritative step-4a pass: `dotnet build SchoolCollab.slnx` **0 errors** (17 pre-existing warnings); suites all 0 failed — Settings **538**, Settings.Api **9**, Students **619**, Students.Api **18**, Assignments **712**, Assignments.Api **94**, Admin **610**, ArchitectureTests **72** — verified with no drift against the frozen patch. `documents/specs/assignment-policy.md` exists (Q8); `assignment-policy-fields.md` carries the B1 record (§8.6). |

**P1s found by this accept pass:** none. The only discrepancy surfaced (AC1's under-counted doc-comment residuals) is a record-keeping gap, not a code reference — it does not reopen AC1, which is scoped to code references and pinned behaviourally by the `NotContain` assertion.

### Verdict

**CLOSED.** All ten criteria satisfied on the named evidence; diff review PASS (no P1, no P2) on the frozen patch; the plan-gate ACCEPT and its six folded P2 amendments (including the retracted P2-4) all verified in the delivered diff. The UI-trigger fired, so the UI-tester handover below is discharged by this section.

### Residuals carried forward

1. **CI parity not proven by this round** — no repo-wide `dotnet test` was run; parity is deferred to CI's own filtered `~Tests.Unit`/`~Tests.Integration` steps.
2. **PR-time blocker (out of scope):** the 3 pre-existing `CodedValueAIServiceLiveTests` live-OpenRouter failures (authority §9.1) must be green or self-skipping before any PR is opened — a known environment blocker, not a B1 defect.
3. **`Edit.razor` `Mandatory`-lock follow-up** (owner Q11-a): the signature checkbox on `Edit.razor` stays unlocked; tracked as a **separate follow-up round** — explicitly **not** B2, which is deletion-only. B1 correctly did not touch `Edit.razor`.
4. **AC1 doc-comment residual count is 8, not 1** (see checklist row 1): seven on the retained B2 compat surfaces plus one in the new `AssignmentPolicyEditor.razor` XML doc. All are prose about deliberately-kept members; they resolve when B2 deletes those members.
5. **Everything B2 still owns (§8.5, unchanged by B1):** the `FEATURE:RequireAssignmentApproval` artefacts across their remaining sites (AppHost `Parameters:` + the `Program.cs:335/:498` fan-out, `SchoolCollab.Admin/appsettings.json:15` cold-start fallback, `MigrationService/Program.cs:415` seeder, the Config `/config-flags` row — plus the D3 OR in the publish/schedule handlers once the flag retires); **both `requires_signature_default` columns**; and the **9 R2 compat members** (`LegacyUpsertAssignmentPolicyRequest`, `LegacyUpsertGradeAssignmentPolicyRequest`, the boolean `UpsertAsync`/`UpsertGradeAssignmentPolicyAsync` overloads, both `[JsonIgnore]` `RequiresSignatureDefault` properties, the two server request records' legacy `RequiresSignatureDefault` parameter). **The nine R2 compat members and the retired legacy columns must be deleted in B2** — they are caller-less after B1 and are kept solely for the deploy window (R2).
6. Accepted round risks **R-B1-1…R-B1-6** carry unchanged (DTO-projection staleness ≤ ~5 min, `Mandatory` fail-open window, stored-but-invalid legacy caps, the `Edit.razor` gap (= residual 3), bUnit cascade flake guidance, `GuardianRole` mirror trust).

### UI-tester scope handover (the UI trigger fired — required)

The tester's scope is **exactly these 14 entries** — it may not derive, expand, or add surfaces. Entries marked *(negative)* are absence checks; no new test artefacts for them.

**(i) Changed UI files**

- **H1 — `src/Students/…/Components/Students/AssignmentPolicyEditor.razor` (+ `.razor.css`)** (NEW): the grade-detail grid — four rows × (Global value | Grade override | Actions), Edit/Reset, `Inherit global` badge, load-failure `FluentMessageBar`. Exercise the grid rendering and the Edit/Reset actions.
- **H2 — `…/Students/AssignmentPolicyFieldEditDialog.razor`** (NEW): the `DialogShellBase` dialog with the `Global settings | This grade` split; save writes each scope only when changed. Exercise Global-only, Grade-only, both-scope, and no-change saves from the UI.
- **H3 — `…/Students/AssignmentPolicyFieldValueEditor.razor`** (NEW): the per-kind input (Mode select, Bool select, Int number field) with the clear sentinel (global side) vs `Inherit global` (grade side). Exercise sentinel selection and blank⇒null for `Int`; reachable only inside H2 (with H1).
- **H4 — `…/Pages/Students/GradeLevels/Detail.razor`** (MODIFY): host card swapped — the `Assignment Policy` card must render; the old `Guardian Signature` card must be **gone** *(negative; `GradeSignaturePolicyEditor` was deleted)*.

**(ii) Pages/dialogs/landing pages that render them**

- **H5 — Grade-detail page `/grade-levels/{Id}`** (Students): the **only** host of H1–H3 — rationale: verifying the card swap (H4) plus the grid/dialog behaviour needs exactly one page, reached from the grade list (H10).
- **H6 — `/assignments/create`** (create wizard): renders the signature checkbox wired in `Create.razor` — rationale: the wizard is where pre-fill and the `Mandatory` lock are observable.
- **H7 — `/assignments`**: rendering landing page for the new Approval column, chips, and the Submit-for-approval row action — rationale: the column renders iff **any** loaded row carries `RequiresApproval`.
- **H8 — `/assignments/{Id}`**: rendering landing page for the approval panel — rationale: the panel appears iff the single assignment's derived field is true.

**(iii) ApiClient methods they call**

- **H9 — `StudentsApiClient.GetGradeAssignmentPolicyAsync` / `UpsertGradeAssignmentPolicyAsync`** (fields-shaped overload): the card's grade-side read/save — rationale: exercises 204/404⇒null on load and the other-fields preservation on save (pairs with H1–H3).
- **H10 — `AssignmentPolicyApiClient.GetAsync` / `UpsertAsync`**: the dialog's global/tenant-side read/save — rationale: exercises the fields-shaped tenant body (pairs with H2); the legacy bool overload is out of scope (B2).
- **H11 — `AssignmentsApiClient.GetSignatureDefaultAsync`**: the wizard's mode resolution (`signatureMode`, legacy-boolean fallback) — rationale: drives both the pre-fill cases and the fail-open path on resolve failure (pairs with H6).

**(iv) Navigation entry points**

- **H12 — Grade list → grade detail (`/grade-levels/{Id}`)**: navigates to the card host — rationale: confirms no nav/entry point was orphaned (the component had exactly one host, verified in Plan (c)).
- **H13 — `/assignments/create` wizard, init + grade-change steps**: the two `ResolveSignatureDefaultAsync` call sites — rationale: pre-fill on wizard init (tenant default, grade still null) and re-resolution on grade change; resolve failure must leave the checkbox **enabled** (fail-open).
- **H14 — `/assignments` → `/assignments/{Id}` row click-through**: list row to detail — rationale: verifies the per-row `RequiresApproval` gates both the list surfaces (H7) and the detail panel (H8) consistently.

**In-scope minimum coverage recap (all present above):** grade-detail `Assignment Policy` card + `Global | This-grade` field dialog (H1–H5); the three new `AssignmentPolicy*` Students components (H1–H3); the create wizard's signature pre-fill/`Mandatory` lock on `/assignments/create` (H6, H11, H13); the approval surfaces on `/assignments` and `/assignments/{Id}` (H7, H8, H14). **Out of the tester's scope:** all B2 artifacts (the flag artefacts, `/config-flags`, both `requires_signature_default` columns, the compat members), `Edit.razor`, and any server-only behaviour — the tester tests the UI as delivered.

### Round hygiene

- Patch frozen at `documents/rounds/diffs-assignment-policy-ui.patch` — **38 files, +2899/−630** (`git diff 1c54220b…` + `add -N` for new files; re-verified by this pass: 38 `diff --git` headers, zero round-doc paths inside). The diff review ran on exactly this frozen patch.
- Nothing staged or committed by any agent this round; the working tree remains dirty by owner decision (Round A's 66-file delta uncommitted, frozen at `diffs-assignment-policy-core.patch`).
- Round A's delta and the parent's decision records (findings-doc §3/§9) are untouched; the findings-doc gained only the B1 record (§8.6) as planned.

## UI Tester

Ran 2026-09-30 on `ollama-cloud/minimax-m3` (read-only shell — no browser, static adversarial reading), scoped to the
14-entry handover. **Verdict: P2-only — no P1.**

```
UI TEST
Scope ack: Hunted exactly the 14 handed-over entries (H1–H14). Read the frozen patch and the in-tree new files:
AssignmentPolicyEditor.razor (+ .razor.css), AssignmentPolicyFieldEditDialog.razor,
AssignmentPolicyFieldValueEditor.razor, the modified GradeLevels/Detail.razor, Assignments/Create.razor,
Assignments/Index.razor, Assignments/Detail.razor, AssignmentsApiClient.cs, and the
AssignmentRequiresApprovalProjectionTests / AssignmentPolicyEditorTests / AssignmentPolicyFieldEditDialogTests
bUnit fixtures. Verified the EditResult / EditModel cast pattern in OnModelInitialized, the OnModelInitialized
null-as-cast path for unset scopes, the dialog footer error-bar / saving-spinner plumbing, the no-change save
test, the optimistic-flip test, and the Mandatory-lock test. Did not run a browser or build.
Verdict: P2-only
P1: (none)
P2:
- Assignments/Index.razor:111 — GridTemplateColumns = "minmax(180px,2fr) 1fr 1fr 1fr 1fr 1fr 1fr auto auto" is
  static (9 slots) while the Approval TemplateColumn at :64 is now data-driven (@if (AnyApprovalRequired)).
  When no loaded row carries RequiresApproval the conditional column is hidden but the 9-slot template is
  unchanged, leaving an empty 1fr column between Status and Due Date (header row 8 headers; data rows 8 cells;
  CSS grid lays them into the first 8 of 9 slots). Pre-existing under the flag-OFF branch (:107–110 notes the
  same accommodation); B1 turns the gate from flag to data, so tenants whose policy is OFF but whose flag was
  previously ON now also see it. Fix: derive the template at render time from AnyApprovalRequired.
- Assignments/Index.razor:188-318 (OnPublishAsync, OnUnpublishAsync, OnCloseAsync, OnSubmitForApprovalAsync,
  OnDeleteAsync, OnDuplicateAsync) — every row-action handler mutates _items[idx] for the optimistic flip /
  rollback but never _searchResults. While the user is searching (DisplayItems = _searchResults, :96–98) the
  displayed row never updates: the user clicks Submit for Approval, the call completes, the chip still reads
  "Not submitted" until the search is cleared. Pre-existing pattern (publish/unpublish behaved the same), but
  B1 introduces the new chip surface whose point is the optimistic flip the spec called out. Fix: patch the
  matching _searchResults entry in each handler, guarded on _searchResults is not null.
Out-of-round observations:
- Assignments/Detail.razor:111 — the Publish button renders unconditionally for any Draft, even one whose
  derived _item.RequiresApproval = true and ApprovalStatus != Approved; the server-side D3 OR will 400 the
  publish, but the UI doesn't disable or hide it and the user walks the whole PublishDialog first. Asymmetric
  with Index.razor:378, which swaps Publish for Submit-for-approval on the equivalent Draft row.
- The 400 body from PUT /api/settings/assignment-policy and PUT /grade-levels/{id}/assignment-policy (the Q7
  non-positive-cap rejection) is not surfaced in the dialog error bar — EnsureSuccessStatusCode() throws the
  generic "Response status code does not indicate success: 400 (Bad Request)." instead of the handler's
  named-field message. Same pattern as the legacy GradeSignaturePolicyEditor; no regression.
```

### Parent adjudication

Both P2s were verified independently against the tree (not taken on the tester's word) and are **accepted as a
bounded rework** — one worker pass, `Index.razor` only, then a static parent scope-check plus the affected
suites. Rationale: both are user-visible on B1's new surface, and **P2-1 bites the default state** — policy OFF
means the Approval column is hidden, which is what nearly every tenant sees today, so the misaligned grid would
ship to the majority case, not an edge case. P2-2 is narrower (only during an active search) but it defeats the
optimistic flip that is the new chip's entire purpose.

**Out-of-round observations: carried, not reworked.** Obs-1 (the unconditional Publish button on the detail page)
is a pre-existing asymmetry that B1 neither introduced nor was scoped to close — it belongs with the owner's
`Edit.razor` follow-up (Q11-a) or a B2-adjacent UI item, and I am **not** silently widening B1 for it. Obs-2 (the
generic 400 message) is a pre-existing pattern with no regression; recorded as a residual for the Q7 surface.

### Rework (iteration 1 of ≤2) — COMPLETE

**Worker pass** `5fd8c5e6` (`ollama-cloud/deepseek-v4.1-flash`), hard-limited to the two permitted files — and it
respected that limit exactly:

- **FIX 1 (P2-1)** — the fixed 9-slot static is replaced by two `static readonly LandingGridSettings` (8-slot
  no-approval, 9-slot with-approval) selected by `private LandingGridSettings GridSettings =>
  AnyApprovalRequired ? _gridSettingsWithApproval : _gridSettingsNoApproval;`, with the binding switched from
  `@_gridSettings` to `@GridSettings`. Header, cells and template now share one predicate in **both** states; the
  stale "grows by one when the flag is on" comment is gone; two statics, no per-row rebuild.
- **FIX 2 (P2-2)** — two null-guarded `Array.FindIndex`-by-Id helpers (`SetSearchResult`, `RemoveSearchResult`)
  mirror the `_items` effect into `_searchResults` across all six handlers: 8 `SetSearchResult` call sites
  (optimistic stamp + rollback for publish/unpublish/close/submit-for-approval), `RemoveSearchResult` on
  delete's success branch, and a search-set refresh on duplicate's reload. Handlers were not restructured.
- **+5 tests** in `AssignmentIndexBunitTests.cs`: `Index_NoRowRequiresApproval_TemplateOffersOneSlotPerRenderedColumn`
  and `Index_ApprovalRow_TemplateOffersOneSlotPerRenderedColumn` (P2-1, negative + positive halves),
  `Index_SearchActive_SubmitForApproval_FlipsTheDisplayedRow`, `Index_SearchActive_Delete_RemovesTheDisplayedRow`,
  `Index_SearchActive_Duplicate_RefreshesTheDisplayedRows` (P2-2's three branches).
- **Discrimination proved, not asserted** — the worker temporarily forced the pre-fix behaviour (9-slot template,
  no-oped helpers, duplicate refresh disabled), built, ran, and observed **exactly those four tests fail**, then
  restored from a byte-identical backup and re-ran green. The parent independently confirmed the restore is
  faithful: the rev2 file **set** is identical to rev1 (38 paths, no additions, no losses), no probe residue
  remains, and both fixes carry the shape the tester specified.
- Declared deviations: `OnDuplicateAsync` reloads rather than substituting a row, so it refreshes the search set
  instead of patching an entry; delete mirrors via a remove-helper (matching the `Where(...).ToArray()` it already
  does); helpers rather than 12 inlined `FindIndex` blocks; 5 tests rather than the 2 named shapes (testing.md
  rule 5 — the delete/duplicate branches and P2-1's positive half would otherwise be untested); the search is
  driven through `LandingPage.SearchTextChanged` because the search box renders inside a `SectionContent` with no
  outlet in a bare `Render<IndexPage>` (the file's own `StatusFilterHost` note documents this).

**Parent re-verification (step 7 for tester-finding rework — parent scope-check, no reviewer re-run):**
`dotnet build SchoolCollab.slnx` **0 errors**; suites — Settings **538/0**, Settings.Api **9/0**, Students
**619/0**, Students.Api **18/0**, Assignments **717/0** (+5 vs rev1), Assignments.Api **94/0**, Admin **610/0**,
ArchitectureTests **72/0**. Scope check: only the two permitted files changed vs rev1; no B2 surface, no
`Detail.razor`, no `Edit.razor`.

**Patch revision:** the round patch is re-frozen as **rev2** — same **38 files**, now **+3138 / −637** (rev1 was
+2899/−630; the delta is exactly the two P2 fixes in `Index.razor` plus its test file). Rev1 was the artifact the
diff review passed; rev2 = rev1 + this rework, verified by the parent scope-check, the re-run suites and the
worker's revert/restore discrimination proof. B1 changed **no scope** as a result of the tester pass.

### Tester re-verify (post-rework) — PASS

Ran 2026-09-30 on `ollama-cloud/minimax-m3` (read-only), scoped narrowly to the two P2 fixes and the two files
they touched — the other 12 handover entries were not re-hunted and the two carried out-of-round observations
were not re-opened.

```
UI TEST
Scope ack: Re-verify of exactly the two P2 fixes — Index.razor's per-render GridSettings (P2-1) and the
search-set mirroring helpers + 5 new tests (P2-2) — and the two files they touched:
src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Index.razor and
tests/SchoolCollab.Assignments.Tests.Unit/AssignmentIndexBunitTests.cs. Did not re-hunt the 12 other handover
entries, did not re-open Detail.razor:111 or the generic-400 observation.
Verdict: PASS
P1:
P2:
Out-of-round observations: none
```

**Worth noting: this pass checked the fix's *completeness*, not merely its presence.** It enumerated every
`_searchResults` mutation site — `SetSearchResult` (:207), `RemoveSearchResult` (:213), the clear-search reset
(:498), the two inside `OnSearchValueChanged` (:511/:514), and the duplicate reload path (:370) — and confirmed
that **no handler mutates `_searchResults` without mirroring `_items`**, including the rollback paths (so a
failed call cannot leave the searched view showing an optimistic value that `_items` already reverted). It also
verified the new tests are non-vacuous by tracing `Index_SearchActive_SubmitForApproval_FlipsTheDisplayedRow`
through its `requiresApproval: true` fixtures, the optimistic flip on the click target, and the title-filter
hiding of the second row — confirming the `Pending` badge assertion genuinely proves the search set was updated.

### Final round status — CLOSED

One rework iteration of the ≤2 budget. **No P1 at any gate:** plan gate **ACCEPT** (after folding six P2s),
diff review **PASS** (no P1, no P2), UI tester **P2-only** → bounded rework → tester re-verify **PASS**. The
worker pass was interrupted once by a provider connection error and recovered by resuming the same run for its
report (see `## Worker Report`); the implementation itself never needed re-doing.

Round folder is now eligible for its documented ephemeral-residue disposal (`documents/rounds/README.md`): the
durable outcomes live in `documents/specs/assignment-policy.md` (§1–§6, the Q8 contract) and
`documents/solution/assignment-policy-fields.md` (§8.6 the B1 record, §8.6.4 what B2 owns, §9 the owner
decisions). The two carry-forwards are homed and cannot be orphaned: **B2's inventory** and the
**`Edit.razor` Q11 follow-up** are both listed in the spec's §5 out-of-scope table and in findings §8.6.4.

### Post-close defect fix (solo, 2026-10-01)

The owner reported a **danger banner whose text was literally "Error"** on the policy edit dialog.

**Root cause — no HTTP call was failing.** `AssignmentPolicyFieldEditDialog.razor` passed the shared footer's
parameter as `Error="Error"`. `DialogShellFooter` declares `[Parameter] public string? Error` and renders
`@if (!string.IsNullOrWhiteSpace(Error)) { <FluentMessageBar Intent="MessageIntent.Error">@Error</FluentMessageBar> }`.
On a **string** parameter a bare attribute value is a **string literal**, so the footer painted a permanent red
bar reading `"Error"` on **every** open, success or failure. The neighbouring `Saving="Saving"` is a `bool`, so
its bare token binds the base's **property** and works — which is precisely why the defect hid in plain sight.

Consequence for this round's record: the failure was **cosmetic**, not a policy-save failure, so the earlier
investigation of the PUT paths (paths, DI, base addresses, live DB rows, enum wire form) was chasing the wrong
target — those checks were sound and all negative, but they were answering a question the symptom had not asked.
The tester's carried **Obs-2** (the Q7 `400` body not reaching the error bar) is a **separate, still-open**
concern; see "Still open" below.

**Fix — three dialogs, one character each:**
- `AssignmentPolicyFieldEditDialog.razor:59` → `Saving="@Saving" Error="@Error"` (the form **9** other dialogs
  in this repo already use).
- `ActivityGroupCreateDialog.razor:92` and `ActivityGroupEditDialog.razor:82` carried the **same defect since
  before B1**; fixed in the same solo pass and recorded here because they are **outside B1's scope**.
- **The defect's source was fixed too:** `.github/skills/dialog-ui/SKILL.md` documented the literal form in its
  "Derived dialog shape" example — which is what the B1 worker followed — and now binds `@Saving`/`@Error`, with
  a new "Rules that catch people out" bullet explaining the trap. The skill file sits outside this patch's path
  scope (`src tests documents`), exactly like the parent-side `.pi/skills` change.

**Regression guard:** `AssignmentPolicyFieldEditDialogTests.Dialog_CleanOpen_RendersNoErrorBar` asserts a clean
open renders no `MessageIntent.Error` bar. **Discrimination proved, not asserted:** with the literal
reintroduced the test fails on exactly that assertion; restored, it passes (`1/0`).

**Verified:** build **0 errors**; Admin.Tests.Unit **611/0** (+1 = the new guard); Settings 538/0 · Settings.Api
9/0 · Students 619/0 · Students.Api 18/0 · Assignments 717/0 · Assignments.Api 94/0 · ArchitectureTests 72/0.
**Patch re-frozen as rev3** — **40 files, +3171/−639** (rev2 plus the two ActivityGroup dialogs; nothing lost).

**Still open (unchanged by this fix, both real):** (i) a *genuine* non-2xx on either policy PUT still surfaces as
`EnsureSuccessStatusCode()`'s contentless `"…400 (Bad Request)"`, because `AssignmentPolicyApiClient.UpsertAsync`
and `StudentsApiClient.UpsertGradeAssignmentPolicyAsync` discard the named-field body the routes already return
— the same file contains the correct pattern in `SetGradeLevelEnrollmentBlockedAsync`; (ii) the **card's** error
bar replaces the whole grid (`AssignmentPolicyEditor.razor:24-27`), so a failed edit or reload blanks the policy
editor instead of letting the user retry.

**Follow-up pass (same day — the owner reported the sibling dialog).** The Notification & Delivery edit dialog
(`NotificationPolicyFieldEditDialog.razor:64`) carried the **identical** `Error="Error"` literal; fixed to
`Saving="@Saving" Error="@Error"`. An exhaustive repo-wide sweep — the first one had been truncated at 20 hits,
which is exactly why this instance was missed the first time — confirmed it was the **last** occurrence, and also
normalised the three remaining bare `Saving="Saving"` forms (`NotificationPolicyFieldEditDialog`,
`EnrollmentExceptionsDialog`, `JoinGroupsDialog`). Those bind correctly (`Saving` is a `bool`) but they are what
makes the literal look plausible, so they go with the convention the skill now states.

**Class-level guard instead of a fourth per-dialog assertion.** `DialogShellFooterBindingArchitectureTests` scans
every `.razor` file under `src/` (stripping `@* … *@` comments) and fails on any literal passed to the footer's
`Error`, plus the bare `Saving` form — the repo's established source-scanning pattern
(`DotNetBestPracticesArchitectureTests`, `AppHostStartupFlagWiringArchitectureTests`). The defect had been found
one dialog at a time (four of them); this makes a fifth impossible to land. **Discrimination proved**: with the
literal reintroduced in the notification dialog the guard fails naming that exact path; restored, it passes.
ArchitectureTests **73/0** (+1).

**Patch re-frozen as rev4** — **44 files, +3309/−642** (rev3 + the three dialogs + the new guard; nothing lost).
Matrix: Settings 538/0 · Settings.Api 9/0 · Students 619/0 · Students.Api 18/0 · Assignments 717/0 ·
Assignments.Api 94/0 · Admin 611/0 · ArchitectureTests 73/0; build **0 errors**.

**One anomaly recorded rather than explained:** a build in the middle of the verification sequence reported
`6 Error(s)` while the identical command immediately before and after it reported **0** with no `error ` lines in
the log, and that run's output was piped to a grep rather than captured. It is therefore logged as
**unreproduced**, not as a lock or a transient — I cannot prove which it was.
