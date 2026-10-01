# Assignment policy fields — findings and implementation record

**Status:** findings complete (owner decisions taken 2026-09-30); implementation tracked per round.
**Scope:** extend the half-built assignment policy (`ar-8-signature-defaults`) from a single signature
flag into the full per-grade assignment policy, with a grade-detail editor mirroring the notification
policy editor.

Owner brief: *"add assignment policy setup for managing assignments. Repo has this spec already
started. Add section to grade detail, similar to notification policy, displaying override for grade
level, tenant scoped and global scoped. assignments can default policy for requiring signature —
this option can be mandatory or optional. assignment can also be set to require approval before
published to contacts. assignment can also allow max allowed primary contacts to send to. also max
allowed copy contacts to send to. reconcile with already existing spec for this."*

---

## 1. What already exists — do NOT re-plan any of it

Round `ar-8-signature-defaults` shipped the **C1 prerequisite** half of this feature
(`documents/solution/assignment-request-implementation-details.md` §2 WS-C, round-table row 8):

| Piece | State |
|---|---|
| `TenantAssignmentPolicy` (Settings.Core) — one row per tenant, single field `RequiresSignatureDefault` (bool) | shipped; migration `20260910124751_AddTenantAssignmentPolicy` |
| `GradeAssignmentPolicy` (Students.Core) — one row per grade, `RequiresSignatureDefault bool?` (null = inherit) | shipped |
| Routes `GET/PUT /api/settings/assignment-policy` (204 = unset) and `GET/PUT /grade-levels/{id}/assignment-policy` | shipped (`AssignmentPolicyRoutes.cs`, `GradeLevelRoutes.cs`) |
| CQRS `AssignmentPolicies/*` (Settings) and `GradeAssignmentPolicies/*` (Students) | shipped |
| Clients `AssignmentPolicyApiClient` (Admin.Shared), `StudentsApiClient.Get/UpsertGradeAssignmentPolicyAsync` | shipped |
| Effective resolution `ISignatureDefaultResolver` / `SignatureDefaultResolver` (Assignments.Api → Settings + Students HTTP, fail-open) | shipped — returns a bare `bool`, merges itself |
| Grade-detail UI `GradeSignaturePolicyEditor` — compact card: tenant switch + tri-state override select | shipped (`Detail.razor:227`) |
| Spec: §2 Approver persona *"optional per school policy"* | the approval knob is **spec-consistent**; no field named yet |
| `Assignment.ApprovalStatus` (`Pending/Approved/Rejected`) | shipped, but gated by the **global feature flag** `FEATURE:RequireAssignmentApproval` (`Assignment.cs:91-95`) — not a policy |

**Absent today:** the shared *fields* shape, the *effective* shape with per-field override flags, a
pure merge resolver in Core, the approval policy field, the two contact caps, the signature
**mandatory** lock, and the grid editor.

## 2. The consolidation pattern to mirror (notification policy)

Notification policy is **two entities + shared shapes in `SchoolCollab.Core`** — not one entity, and
no separate "global" entity (the tenant row *is* global; unset = built-in default at publish time):

| Layer | Role | Location |
|---|---|---|
| `TenantNotificationPolicy` | tenant scope = "global default" | `Settings.Core/Domain/`, migration `20260805151925` |
| `GradeNotificationPolicy` | grade scope = override | `Students.Core/Domain/`, migration `20260805154141` |
| `NotificationPolicyFields` | shared nullable field list, **null = inherit**; mirrored by **both** entities as real columns (no JSON/owned type) | `SchoolCollab.Core/Notifications/` |
| `EffectiveNotificationPolicy` | resolved values **+ a per-field `…FromOverride` flag** (drives "Grade override" vs the "Inherit global" badge) | `SchoolCollab.Core/Notifications/` |
| `IEffectiveNotificationPolicyResolver` + impl | pure merge `grade ?? tenant ?? built-in`, computes the flags | `SchoolCollab.Core/Notifications/`; Assignments.Api wraps it in `NotificationPolicyResolver` (HTTP) |
| Per-scope DTOs / CQRS / routes | `TenantNotificationPolicyDto` (arrays non-null, ints nullable) vs `GradeNotificationPolicyDto` (all nullable) | Settings + Students |

**Architecture constraint:** the shared shape **must** live in `SchoolCollab.Core` — Settings,
Students and Assignments all reference Core, and no cross-context project references are allowed.

## 3. Owner decisions (2026-09-30)

| # | Decision |
|---|---|
| D1 | Mirror the notification pattern: shared `AssignmentPolicyFields` in Core + `EffectiveAssignmentPolicy` with per-field override flags + a pure resolver; extend the two existing entities (2 migrations). |
| D2 | Signature requirement is a three-state value: `Disabled` / `Optional` / `Mandatory` (Mandatory locks the author's create-wizard checkbox; Optional pre-fills but allows change). |
| D3 | `RequiresApprovalBeforePublish` becomes a **policy field**; its effective value is OR'd with `FEATURE:RequireAssignmentApproval` for one release, then the flag is retired (flag-governance artefacts updated in the same round). |
| D4 | "Primary" = contacts of the student's **Primary guardian** (`GuardianRoleDto`); "copy" = contacts of the other guardians. No schema change (contact-level `IsPrimary` was dropped on 2026-07-27). |
| D5 | The sendout-wide cap stays `MaxNotifications` (notification policy) — untouched. The two new knobs are per-role contact counts. |
| D6 | One new "Assignment Policy" card on grade detail (mirroring Notification & Delivery) **absorbs** the signature row; `GradeSignaturePolicyEditor` is retired. |
| D7 | Execution tier: **Tier 3** (schema + two contexts + endpoints + enforcement + UI). |
| D8 | Delivered in two rounds: **A** = policy core (no UI), **B** = UI + enforcement. |

## 4. Field set

| Field | Type | Semantics |
|---|---|---|
| `SignatureRequirement` | `SignatureRequirementMode?` | `Disabled` = never required; `Optional` = pre-filled default, author may change; `Mandatory` = locked on (checkbox disabled + tooltip, mirroring the AI-prompt `PromptLocked` precedent). |
| `RequiresApprovalBeforePublish` | `bool?` | publish is refused while `ApprovalStatus != Approved` when effective-true. |
| `MaxPrimaryContacts` | `int?` | cap on Primary-guardian contacts included in a sendout. |
| `MaxCopyContacts` | `int?` | cap on other-guardian contacts included in a sendout. |

Null on every field = inherit (grade) / unset (tenant). Back-compat migration maps the existing
`RequiresSignatureDefault` column: `false → Disabled`, `true → Optional`, `null → null`.

## 5. Resolution and enforcement

- **Resolution** — `EffectiveAssignmentPolicy` = `grade ?? tenant ?? built-in`, with a
  `…FromOverride` flag per field; computed by the pure Core resolver, fed by an HTTP-backed
  `IAssignmentPolicyResolver` in Assignments.Api (replacing `ISignatureDefaultResolver`).
- **Create wizard** — pre-fill `RequiresSignature` from `EffectiveAssignmentPolicy.SignatureRequirement`;
  `Mandatory` disables the checkbox with an explanatory tooltip; the persisted assignment value
  remains the snapshot.
- **Publish** — when effective `RequiresApprovalBeforePublish`, refuse publish unless
  `ApprovalStatus == Approved` (reuse the ar-5 approval flow; existing `ApproveAssignmentCommand`).
- **Recipients** — extend `NotificationRecipientFilter.Apply` (pure, unit-testable) to cap
  Primary-role and Copy-role contacts per sendout; `MaxNotifications` behaviour unchanged.

## 6. Round split

- **Round A (no UI):** Core fields/effective/resolver; extend both entities + 2 migrations; widen
  CQRS/DTOs/routes/clients; `ISignatureDefaultResolver` → `IAssignmentPolicyResolver`;
  approval-flag reconciliation; this doc + spec amendment; unit tests.
- **Round B (UI + enforcement):** `GradeAssignmentPolicyEditor` grid + field-edit dialog
  (Global | This-grade scope), new `Detail.razor` card, retire `GradeSignaturePolicyEditor`;
  wizard pre-fill + Mandatory lock; publish approval gate; recipient role caps; tests.

## 7. Risks / notes

- **Flag retirement** (D3) touches `documents/solution/feature-flag-workflow.md`, the
  `/config-flags` page data and `documents/configuration.md` §2 — one write-point per flag kind.
- **Role semantics** (D4) depend on `GuardianRoleDto` values; a student with no Primary guardian
  yields no primary-role recipients (the cap then applies to copy only).
- **`MaxNotifications` interaction**: a sendout-wide cap can make the role caps unreachable
  (whichever is smaller wins); this is intended, and both are enforced in the same filter.
- Back-compat: the existing `UpsertGradeAssignmentPolicyRequest(bool? RequiresSignatureDefault)`
  body widens — the Admin/Students clients are the only callers.

---

## 8. Round A implementation record (round `assignment-policy-core`, 2026-09-30)

Round A shipped the **policy core only** — no UI file in the round diff. Round B owns every UI and
enforcement surface (§6).

### 8.1 What shipped

| Layer | Change |
|---|---|
| Core shape | `SchoolCollab.Core/AssignmentPolicies/`: `SignatureRequirementMode` (`Disabled`/`Optional`/`Mandatory`), `AssignmentPolicyFields` (nullable input shape + `Empty`), `EffectiveAssignmentPolicy` (resolved values + four `…FromOverride` flags), `IEffectiveAssignmentPolicyResolver` + pure `EffectiveAssignmentPolicyResolver` (`grade ?? tenant ?? built-in`) |
| Settings | `TenantAssignmentPolicy` widened from `bool RequiresSignatureDefault` to the four nullable fields; `TenantAssignmentPolicyConfiguration` maps them as real nullable columns (`SignatureRequirement` via `HasConversion<string>()`); DTO / `UpsertTenantAssignmentPolicy` + handlers widened; `PUT /api/settings/assignment-policy` request record widened |
| Students | `GradeAssignmentPolicy` widened the same way (null per field = **inherit**); DTO / `UpsertGradeAssignmentPolicy` + `Get*` handlers widened; `PUT /grade-levels/{id}/assignment-policy` request record widened (grade-exists guard kept) |
| Migrations | `AddAssignmentPolicyFields` in **both** contexts (migration + Designer + updated snapshot), additive only, with the D8 back-compat backfill. Two EF scaffolds were **hand-corrected** — see 8.3 |
| Assignments | `IAssignmentPolicyResolver` (Core) + HTTP `AssignmentPolicyResolver` (Api) **replace** `ISignatureDefaultResolver` / `SignatureDefaultResolver` (both deleted, zero references in `src/`); DI swap; `/assignments/signature-default` internals rewired with an **unchanged wire contract** |
| Approval (D3) | `PublishAssignmentCommandHandler` + `ScheduleAssignmentCommandHandler` now gate on `effectivePolicy.RequiresApprovalBeforePublish \|\| FEATURE:RequireAssignmentApproval` |
| Clients | `AssignmentPolicyApiClient` (Admin.Shared) and `StudentsApiClient` (Students.Application) gained a fields-based upsert; the legacy boolean overloads remain (Round-B deletion) |
| Docs | this record; `documents/configuration.md` (§2 AppHost row + §5 runtime/introduced rows); `documents/solution/feature-flag-workflow.md` (retirement plan) |

### 8.2 Decisions taken inside the round (no D1–D8 change)

- **Enum wire form.** `SignatureRequirementMode` carries a **type-level**
  `JsonStringEnumConverter<SignatureRequirementMode>`, so it travels as its **name**
  (`"Optional"`) on every host and client without a per-host `Program.cs` registration — the drift
  class `ProgramJsonEnumConverterTests` guards against, and the `GeneratedQuestionType` precedent
  for a shared-project enum. Reading stays tolerant (the converter still accepts the numeric form).
  The DB stores the name (`HasConversion<string>()`) as D1 requires. (The neighbouring
  `NotificationChannel` travels numerically; the attribute is the stricter of the two options.)
- **Tenant compat value.** The Admin.Shared mirror's `RequiresSignatureDefault` is
  `SignatureRequirement is not null and not Disabled` — `null` (nothing configured) must read as
  *false*, the pre-widening behaviour, not as `SignatureRequirement != Disabled`.
- **No cap validation.** The two contact caps are stored as given; D5 puts enforcement (and hence
  any range rule) in Round B's recipient filter. Negative caps are therefore storable but
  meaningless today — Round B must validate them where they are enforced (or on the write path).

### 8.3 The two hand-corrected migrations (both plan-review findings, both invisible to a fresh-DB run)

1. **Settings — the legacy column must not be dropped *and* must stay INSERT-safe.** EF scaffolded
   `DropColumn("requires_signature_default")` (the entity no longer maps it), which was deleted by
   hand (R1: additive-only, an old binary keeps working through the deploy window). Because that
   column is `NOT NULL` with **no** database default, an unmapped column means the create path
   omits it and every first insert of a tenant policy row fails with Postgres **23502**; `Up()`
   therefore re-adds `DEFAULT false` and `Down()` removes it again.
2. **Students — EF scaffolded a `RenameColumn`** of `requires_signature_default` →
   `requires_approval_before_publish`. Left in place, every legacy signature boolean would have
   become an *approval* flag and the legacy column would have vanished. Replaced by hand with an
   `AddColumn` plus the explicitly three-valued backfill.

### 8.4 Verification

- `dotnet build SchoolCollab.slnx` — 0 errors.
- Unit suites (all green, final tree): Settings 535, Settings.Api 1, Students 616, Students.Api 13,
  Assignments 690, Assignments.Api 93, ArchitectureTests 72, **Admin 602** (the last is the R2
  discriminator: the untouched `GradeSignaturePolicyEditor` + its tests still send and parse the
  legacy boolean bodies).
- New discriminating tests: `EffectiveAssignmentPolicyResolverTests` (merge + flags + built-ins),
  `AssignmentApprovalPolicyReconciliationTests` (policy-OR-flag, including the fail-open case),
  `AssignmentPolicyResolverTests` (HTTP fail-open + the enum wire form),
  `AssignmentSignatureDefaultRouteTests` (the unchanged `{"requiresSignature": …}` contract at the
  route), `GradeAssignmentPolicyRoutesTests` (the grade PUT body binding the new field set, the
  enum's name form, null = inherit, and the legacy boolean mapping at the route),
  `UpsertAssignmentPolicyRequestTests` (the tenant PUT record's D8 legacy mapping),
  `TenantAssignmentPolicyDtoCompatTests` (the Admin mirror's derived boolean, so the untouched
  editor keeps reading the right value from the widened payload),
  `AssignmentPolicyFieldsMigrationTests` in both contexts (regex-read from the shipped migration
  files: the D8 mapping, the no-drop/no-rename posture, the INSERT-safety default, `Down()` as the
  inverse of `Up()`).
- `git diff --name-only` contains **no** `.razor` / `.razor.css` / `.css` / `.js` path.

### 8.5 The legacy surfaces kept on purpose — and which round deletes them

Split by owner decision **Q5** (2026-09-30, §9): Round B was divided into **B1** (consumption +
* *enforcement**) and **B2** (the subtractive sweep). Deleting `GradeSignaturePolicyEditor` is
**build-coupled** to its own test file, so both go in B1; everything else waits for B2.

**B1 deletes (with the component it belongs to):**

- `GradeSignaturePolicyEditor.razor` — retired by D6, absorbed by the new card
  (see §9).
- `tests/SchoolCollab.Admin.Tests.Unit/GradeSignaturePolicyEditorTests.cs` — the test project does
  not compile once the component is gone; Round A deliberately left this file untouched by keeping
  the legacy request bodies valid (its IN item 8), and B1 is where that licence ends.

**B2 deletes (caller-less after B1, kept deliberately for the deploy window — R2):**

- `LegacyUpsertAssignmentPolicyRequest` (Admin.Shared) + `LegacyUpsertGradeAssignmentPolicyRequest`
  (Students.Application) and the boolean `UpsertAsync` / `UpsertGradeAssignmentPolicyAsync`
  overloads.
- The `[JsonIgnore]` `RequiresSignatureDefault` compat properties on the two DTOs.
- The `RequiresSignatureDefault = null` legacy parameter on the **server** request records
  (`UpsertAssignmentPolicyRequest`, `UpsertGradeAssignmentPolicyRequest`).
- The `requires_signature_default` column in **both** tables (its default goes with it) and the
  `FEATURE:RequireAssignmentApproval` flag rows/artefacts across all 6 of its sites, once the D3 OR
  is retired.

### 8.6 Round B1 implementation record (round `assignment-policy-ui`, 2026-09-30)

Round **B1** is the **consumption + enforcement** half of Round B (owner decision **Q5**): it makes
the Round A policy reachable and enforced and deletes **nothing** except the retired
signature-editor component (and its test, which is **build-coupled** — the Admin test project stops
compiling once the component goes). The durable contract this round ships is promoted to
**`documents/specs/assignment-policy.md`** (owner decision **Q8**), so that file is now the source
of truth for the field set, the resolution rule and every enforcement site; this section records the
*how*.

#### 8.6.1 What shipped

| Workstream | Change |
|---|---|
| Grade-detail card (D6) | New `AssignmentPolicyEditor` grid (four rows × Global value \| Grade override \| Edit/Reset, `Inherit global` badge, scoped `.razor.css`) + `AssignmentPolicyFieldEditDialog` (`DialogShellBase`, `Global settings \| This grade` split, **writes each scope only if changed**) + `AssignmentPolicyFieldValueEditor` (Mode / Bool / Int with a clear-or-inherit sentinel for the two select kinds). The `Guardian Signature` card became the `Assignment Policy` card; `GradeSignaturePolicyEditor.razor` and `GradeSignaturePolicyEditorTests.cs` were deleted in the same change-set. |
| Wizard (D2) | `GET /assignments/signature-default` widened **additively** to `{ requiresSignature, signatureMode }` (the mode travels as its enum name; `requiresSignature` keeps the `mode != Disabled` derivation). `AssignmentsApiClient.GetSignatureDefaultAsync` resolves the mode (honouring the legacy boolean when a mode is absent), and `Create.razor` pre-fills from it — `Mandatory` additionally disables the checkbox with an explanatory tooltip. Fail-open: a resolve failure logs a warning, keeps the checkbox value and leaves it **enabled**. `Edit.razor` untouched (Q11). |
| Approval surfaces (Q6/D3) | `AssignmentSummaryDto` gained a trailing-optional `bool RequiresApproval = false`, projected by **both** read handlers as `policy.RequiresApprovalBeforePublish \|\| FEATURE:RequireAssignmentApproval`, resolved **server-side** with the per-grade policy cached under `assignment-policy:effective:{tenant}:{grade}` (sentinel `none`) and the **flag read guarded** (try/catch ⇒ off — a Config outage degrades to approval-off instead of failing the read). `Index.razor` and `Detail.razor` now read that DTO field with a plain `@if`: no client-side flag read, no `FeatureFlagGate`, no `_approvalRequired`. |
| Recipient caps (D4/D5/Q7) | `NotificationRecipientFilter.Apply` takes the effective assignment policy and applies `MaxPrimaryContacts` / `MaxCopyContacts` per role bucket (keyed on `AssignmentRecipient.OwnerType`, so `Student`-owned rows are exempt from both) *before* the untouched `MaxNotifications`; a non-positive cap is defensively uncapped. The single production caller (publish handler) passes the already-resolved policy — no ctor change. |
| Cap validation (Q7) | Both upsert handlers reject a non-positive `MaxPrimaryContacts`/`MaxCopyContacts` with `ArgumentOutOfRangeException` naming the field (null stays unset/inherit); the Settings PUT gained a `catch` arm and the Students PUT gained its second one, both ⇒ **400**. |
| Docs | `documents/specs/assignment-policy.md` (new, Q8) + this record. `documents/configuration.md` needed no change — Round A's flag rows already describe the reconciled posture. |

#### 8.6.2 Decisions taken inside the round (no D1–D8 change)

- **The derived field is a plain DTO field, not a gate component.** `FeatureFlagGate` was
  deliberately not reused on the approval surfaces: the semantic is now a server-resolved *merged*
  value, not a reactive client-side flag, and the derived field keeps the D3 OR alive through the
  deploy window.
- **Per-grade policy cache, not per-row.** The two read handlers resolve the effective policy once
  per **distinct** grade per list read (the DTO projection itself is cached for five minutes —
  accepted staleness, round risk R-B1-1).
- **Flag read guarded in the projection.** The resolver is fail-open, but the *flag* leg was not:
  an unguarded `IsEnabledAsync` would have turned a Config outage into a 500 on the assignment
  list/detail reads — a new availability coupling the replaced client-side read did not have
  (plan-review P2-3).
- **Cap bucketing keys on `OwnerType`**, with the null-role test kept only as a redundant net
  (§9.2): `Role is null` and `OwnerType.Student` are the same set, and the invariant is asserted in
  `NotificationRecipientFilterTests`.
- **The dialog's split layout is FluentUI-native** (`FluentGrid`/`FluentGridItem`), so that dialog
  needs no `.razor.css` of its own; the grid card carries its styles in a scoped
  `AssignmentPolicyEditor.razor.css` (no inline `<style>` block).

#### 8.6.3 Verification

- `dotnet build SchoolCollab.slnx` — 0 errors.
- Affected suites (all green, final tree): Admin 610 · Assignments 711 · Assignments.Api 94 ·
  Settings 538 · Settings.Api 9 · Students 619 · Students.Api 18 · ArchitectureTests green.
  (Round A's tree had Admin 602 · Assignments 690 · Assignments.Api 93 · Settings 535 · Settings.Api
  1 · Students 616 · Students.Api 13; the deltas are this round's new/removed tests, including the
  7 deleted `GradeSignaturePolicyEditorTests`.)
- New discriminating tests: `AssignmentPolicyEditorTests` + `AssignmentPolicyFieldEditDialogTests`
  (grid, per-scope writes, **zero PUTs on a no-change save**, load-failure bar),
  `AssignmentRequiresApprovalProjectionTests` (the OR, fail-open, once-per-distinct-grade, the
  guarded flag read), `AssignmentRequiresApproval` fixtures in the Index/Detail bUnit suites, the
  `AssignmentCreateBunitTests` `Mandatory` lock, `NotificationRecipientFilterTests` cap matrix, and
  the Q7 handler/route guards on both write paths.
- **No migration.** The four policy columns exist from Round A's two `AddAssignmentPolicyFields`
  migrations; B1's diff contains no `Migrations/` path and both `MigrationGuardTests` stay green.

#### 8.6.4 What B2 still owns

The subtractive sweep recorded in §8.5 — the `FEATURE:RequireAssignmentApproval` artefacts and the
D3 OR, both `requires_signature_default` columns, and the 9 R2 compat members. B1 kept every one of
them in place on purpose (they are caller-less but must survive the deploy window). The
`Edit.razor` signature-checkbox gap closed by **neither**: owner decision Q11 tracks it as a
separate follow-up round.

#### 8.6.5 Post-close defect fix (solo, 2026-10-01)

The owner reported a danger banner whose text was literally **"Error"** on the B1 policy edit dialog. The root
cause was **not** a failed policy write — it was a Razor binding mistake: `AssignmentPolicyFieldEditDialog.razor`
passed the shared footer's parameter as `Error="Error"`, and on a **`string`** parameter a bare attribute value
is a **string literal**. `DialogShellFooter` renders `@if (!string.IsNullOrWhiteSpace(Error))` → a danger
`FluentMessageBar`, so the literal painted a permanent banner reading `"Error"` on every open, success or
failure. The neighbouring `Saving="Saving"` is a `bool`, so its bare token binds the base property and works —
which is why the defect looked correct.

Fixed: the B1 dialog now binds `Saving="@Saving" Error="@Error"` (the form **9** other dialogs in this repo
use); the identical defect in `ActivityGroupCreateDialog` / `ActivityGroupEditDialog` (pre-dating B1 and outside
this feature) was fixed in the same pass; and the **source** of the mistake — the literal form documented in
`.github/skills/dialog-ui/SKILL.md`'s "Derived dialog shape" example — was corrected and given a
"Rules that catch people out" bullet. Guarded by
`AssignmentPolicyFieldEditDialogTests.Dialog_CleanOpen_RendersNoErrorBar`, with discrimination **proved** by
reintroducing the literal (the test fails on exactly that assertion) rather than asserted.

**Two error-path defects remain open** and are *not* addressed by this fix, both verified real: (i) the policy
PUT clients discard the server's named-field message — `EnsureSuccessStatusCode()` yields a contentless
`"…400 (Bad Request)"` even though both routes return `{ ex.Message }` (the correct pattern already exists in
`SetGradeLevelEnrollmentBlockedAsync`); (ii) the grade-detail card's error bar **replaces the whole grid**
(`AssignmentPolicyEditor.razor:24-27`), so a failed edit or reload blanks the policy editor instead of letting
the user retry.

**Sibling dialog and class-level guard (same day).** The owner then reported the same symptom on the
Notification & Delivery edit dialog, which carried the identical literal
(`NotificationPolicyFieldEditDialog.razor:64`) — so **four** dialogs had it in total. An exhaustive sweep found
that to be the last occurrence; the three bare `Saving="Saving"` forms were normalised too. Rather than a fourth
per-dialog assertion, the class is now guarded repo-wide by
`DialogShellFooterBindingArchitectureTests`, which scans every `.razor` file and fails on a literal passed to the
footer's `Error` (or the bare `Saving` form) — so a fifth instance cannot land. Guard discrimination proved by
reintroducing the literal (fails naming the exact path), then restoring.

## 9. Round B decisions (owner, 2026-09-30)

Recorded on the owner's confirmation of the grill-me round-2 frontier (**"recommend as-is"** — every
recommendation accepted). These settle Round B's scope; **they do not change D1–D8**, which stay
pinned.

| # | Decision |
|---|---|
| Q3 | Round B runs at **full Tier 3** (the deterministic UI trigger fires), so the round gets an orchestrator plan, an independent plan gate, an orchestrator-accept and a UI-tester pass. Round B is **not** "UI-only work" (the 2026-09-30 never-Tier-3 rule) — it carries enforcement, contract and migration risk. |
| Q4 | The 3 pre-existing live OpenRouter failures (§9.1) are recorded as an **out-of-scope environment failure**, not folded into B1/B2; the fix is scheduled as a **PR-time prerequisite**, because AGENTS.md's pre-flight requires 0 test failures before a PR is opened. |
| Q5 | Round B is **split**: **B1** = consumption + enforcement (new grade-detail card/grid/field dialog replacing `GradeSignaturePolicyEditor` + its test file, create-wizard pre-fill + `Mandatory` lock, approval surfaces moved off the flag, recipient role caps enforced and validated). **B2** = the subtractive sweep (§8.5), i.e. the 6 flag sites, both `requires_signature_default` columns and the 9 compat members. |
| Q6 | The approval surfaces learn the effective policy from a **derived `RequiresApproval` projected onto the assignment list/detail DTOs**, resolved server-side — Round A put `IAssignmentPolicyResolver` in `Assignments.Core`, so the read handlers can inject it and resolve per distinct grade. No new endpoint, no client-side policy logic. Accepted trade-off: a derived field on those DTOs plus a resolver call inside read handlers, with the existing fail-open posture preserved (a failed resolve can never turn approval ON). |
| Q7 | The two contact caps are validated **on the write path** (non-positive ⇒ `400`) **and** defensively treated as *uncapped* when non-positive inside `NotificationRecipientFilter`, matching the existing `MaxNotifications is >= 0` precedent. This also closes the D5 gap where the caps were storable but unenforced and unvalidated. |
| Q8 | At B1 close the **durable contract** (§4 field set + §5 resolution/enforcement) is promoted to **`documents/specs/assignment-policy.md`**, matching the `notification-delivery-plan.md` precedent and satisfying the `documents/rounds/README.md` precondition for trashing the round folder. This doc keeps the findings/why history. |
| Q11 | The `Mandatory` lock stays scoped to the **create wizard** (D2 unchanged). `Edit.razor` keeps its unlocked signature checkbox (`Edit.razor:88-89` → `AssignmentEditFormModel.RequiresSignature` → `Assignment.RequiresSignature`, gating `Detail.razor:325`), and closing that gap is tracked as a **separate follow-up** — explicitly **not** B2, which is deletion-only and therefore cannot host new behaviour. B1 must not touch `Edit.razor`. |

### 9.1 Verification finding carried out of Round A (owner Q2, 2026-09-30)

The repo-wide `dotnet test` — the CI-parity check Round A explicitly had not run — returned
**3118 tests · 3115 passed · 3 failed** (2m42s). Round A's own eight affected projects were all
0-failed (535 / 1 / 616 / 13 / 690 / 93 / 602 / 72).

- The 3 failures are all `SchoolCollab.Settings.Tests.Integration.CodedValueAIServiceLiveTests`
  (`ChatAsync_WithOpenRouter_InvokesListCategoriesToolEndToEnd`, `…_StreamsSimpleTextResponse`,
  `…_AddCountriesUnderCntry_ConfirmInsertsValues`) — live OpenRouter calls failing with a provider
  **HTTP 400**.
- **Not a Round A regression, provably**: every input to those tests lies outside Round A's delta —
  `src/AppHost/SchoolCollab.AppHost/appsettings.json` (which supplies `Parameters:openrouter-*`) is
  unmodified (last change `e04356bb`, 2026-09-25), the AI chat engine and the test project are
  untouched (the only AI file in the diff is a doc-comment rename), and the failure is outbound.
  The configured model id `google/gemma-4-31b-it` **is** valid upstream (verified against
  OpenRouter's live catalogue: 464 models, target present), so this is a local key/account posture
  rather than a wrong model id.
- **CI note** (correcting an earlier claim): `.github/workflows/ci.yml` does **not** run no-arg
  `dotnet test`; it runs `dotnet restore`, a no-arg Release build, then two **filtered** test steps
  (`~Tests.Unit`, `~Tests.Integration`), each with `--ignore-exit-code 8`. Per Microsoft's MTP exit
  table, `8` = "no tests discovered / all skipped" while `2` = "at least one test failure" — so CI's
  flag is correctly narrow and **does** fail on real test failures. These live tests are presumably
  invisible in CI because `OPENROUTER_API_KEY` is absent there and they self-skip as Inconclusive.
- **Policy consequence:** AGENTS.md's pre-flight ("`dotnet test` must complete with **0 failures**") cannot pass on this tree until those 3 tests are green or self-skipping — a PR-time blocker, not a
  code defect of this feature.

### 9.2 Recipient-role fact settled on the owner's challenge (2026-09-30)

The plan review raised a P2 asserting that a *guardian-owned* publish recipient could have a null role
and would thereby escape the copy cap. **That premise was wrong**, and the owner caught it. Settled
facts:

- `GuardianRole` **is** an enum — `Primary = 0, CC = 1` (`Students.Core/Domain/GuardianRole.cs`).
- `StudentGuardian.Role` is **non-nullable** (`StudentGuardian.cs:22`), so a guardian→student link
  always carries a role; `documents/solution/student-guardian-plan.md` §2 agrees ("Role (Primary/CC)
  is the only guardian classification").
- `AssignmentRecipient.Role` is `GuardianRole?` (`AssignmentRecipient.cs:32`) only because a recipient
  row's **owner may be a student** (`ContactOwnerType.Student`, `ContactOwnerType.cs:8`).
- Therefore **`Role is null` and `OwnerType.Student` are the same set** — exactly what B1 exempts.

Consequence for B1: the cap split keys on **`OwnerType`** (the authoritative discriminator), with
`Role is null` retained only as a redundant net, and the invariant is asserted in tests — a null role
on a *guardian-owned* row is a **data defect**, not a bucketing choice. No owner decision was needed;
the plan-review P2 is recorded as **retracted** in that round's `## Review`.
