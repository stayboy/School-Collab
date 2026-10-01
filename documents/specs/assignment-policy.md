# Assignment policy — the durable contract

> **Status:** adopted (Round B1 close, 2026-09-30, owner decision **Q8**). This is the durable
> contract for the per-grade assignment policy: the **field set**, the **resolution** rule, and
> where each field is **enforced**. It is the source of truth for the behaviour; the findings and
> why-history stay in `documents/solution/assignment-policy-fields.md` (D1–D8, §9 Q3–Q11), which
> this file promotes and does not replace.
>
> Precedent: `documents/specs/notification-delivery-plan.md` (the notification-policy contract this
> feature deliberately mirrors).
>
> **Round split:** Round A (`assignment-policy-core`) shipped the data/protocol layer; **Round B1**
> (`assignment-policy-ui`) made the policy reachable and enforced (this contract's shipped state);
> **B2** is the *subtractive* sweep — it deletes the retired artefacts and adds no behaviour.

## 1. Field set

Every field lives on a shared `AssignmentPolicyFields` shape in `SchoolCollab.Core`
(`SchoolCollab.Core/AssignmentPolicies/`) and is mirrored as a real nullable column by both policy
entities. **Null = inherit** (grade) / **unset** (tenant); nothing is stored as JSON.

| Field | Type | Semantics |
|---|---|---|
| `SignatureRequirement` | `SignatureRequirementMode?` — `Disabled` \| `Optional` \| `Mandatory` | `Disabled` = a guardian signature is never required; `Optional` = pre-filled default the author may change; `Mandatory` = locked on (create wizard: checkbox disabled + explanatory tooltip). |
| `RequiresApprovalBeforePublish` | `bool?` | Publish is refused while `ApprovalStatus != Approved` when the effective value is true. |
| `MaxPrimaryContacts` | `int?` | Cap on **Primary-guardian** contacts included in one sendout. |
| `MaxCopyContacts` | `int?` | Cap on **other-guardian** contacts included in one sendout. |

Built-in defaults when nothing is configured: `Disabled`, approval not required, **both caps
uncapped**.

### 1.1 Storage and wire form

| Layer | Shape |
|---|---|
| Shared shape | `AssignmentPolicyFields` (nullable per field) + `EffectiveAssignmentPolicy` (resolved values + one `…FromOverride` flag per field) |
| Settings (`settings-db`) | `TenantAssignmentPolicy` — one row per tenant (the *global default*), `BaseTenantEntityWithAudit` + `IHasRowVersion` |
| Students (`students-db`) | `GradeAssignmentPolicy` — at most one row per grade (the *override*), same base |
| Wire | `SignatureRequirementMode` travels as its **name** (`"Optional"`) — a type-level `JsonStringEnumConverter` on the enum, so no per-host registration and no drift; the DB stores the name (`HasConversion<string>()`) |
| Endpoints | `GET/PUT /api/settings/assignment-policy` (204 = unset) • `GET/PUT /grade-levels/{id}/assignment-policy` (204 = inherit); the PUTs bind the four-field body and map a non-positive contact cap to **400** |
| Clients | `AssignmentPolicyApiClient` (Admin.Shared) • `StudentsApiClient.Get/UpsertGradeAssignmentPolicyAsync` (Students.Application) |

## 2. Resolution

`EffectiveAssignmentPolicy = grade ?? tenant ?? built-in`, per field, computed by the **pure**
`IEffectiveAssignmentPolicyResolver` in Core, with a `…FromOverride` flag per field (the flag drives
the "Grade override" vs "Inherit global" badge on grade detail).

- Assignments reach it through `IAssignmentPolicyResolver` (Assignments.Core) whose implementation
  (`AssignmentPolicyResolver`, Assignments.Api) reads the Settings + Students endpoints over HTTP.
  The interface is **fail-open**: any fetch failure degrades to "nothing configured", so the
  built-in defaults apply and neither create nor publish is blocked by policy.
- **A failed resolve can never turn approval ON** — it can only suppress the policy leg of the OR
  in §3.2.
- Read handlers that project derived policy state cache it **per (tenant, grade)** under
  `assignment-policy:effective:{tenant}:{grade}` (sentinel `none` for a null grade), tagged
  `assignments`, so one list read resolves the policy once per distinct grade. A policy change can
  therefore take up to the read cache's lifetime to appear on those surfaces; **enforcement is never
  stale** (publish/schedule resolve fresh).

## 3. Enforcement

### 3.1 Signature requirement — create wizard

- The create wizard resolves `GET /assignments/signature-default?gradeLevelId=…`
  (always **200**, fail-open), whose body is `{ requiresSignature, signatureMode }`:
  `signatureMode` is the effective `SignatureRequirement` (enum name) and `requiresSignature` is its
  legacy derivation `mode != Disabled`, so a consumer reading only the boolean still parses.
- `Optional` pre-ticks the author's checkbox; `Mandatory` pre-ticks it **and locks it** (disabled +
  explanatory tooltip, the AI-prompt `PromptLocked` precedent); `Disabled` leaves it unticked.
- The persisted assignment value is the **snapshot** taken at create. The `Mandatory` lock is scoped
  to the create wizard by owner decision **Q11**: `Edit.razor` keeps its unlocked checkbox, and
  closing that gap is a tracked follow-up (not B2).
- Fail-open: a transport failure leaves the checkbox at its prior value and **enabled** — an
  infrastructure failure never locks an author out of their own choice.

### 3.2 Approval before publish

- Effective value = `policy.RequiresApprovalBeforePublish` **OR**
  `FEATURE:RequireAssignmentApproval` (owner decision **D3**, live for the deploy window).
- The OR is evaluated **server-side** in three places: `PublishAssignmentCommandHandler` and
  `ScheduleAssignmentCommandHandler` (enforcement — refuse publish/schedule while
  `ApprovalStatus != Approved`), and the two assignment read handlers, which project the derived
  result onto `AssignmentSummaryDto.RequiresApproval` (Q6).
- The two approval **UI** surfaces (`Assignments/Index.razor`, `Assignments/Detail.razor`) read that
  DTO field with a plain `@if` — no client-side flag read, no `FeatureFlagGate`. The derived field
  keeps the D3 OR live through the deploy window (flag-ON tenants see today's surfaces; flag-OFF +
  policy-ON tenants gain them from the policy).
- The projection's **flag read is guarded** (try/catch ⇒ off): a Config outage degrades the surfaces
  to approval-off — the behaviour the replaced client-side read had — instead of failing the
  list/detail read.
- Flag retirement (the flag's remaining artefacts, including the write-points) belongs to **B2**.

### 3.3 Recipient contact caps (D4/D5)

`NotificationRecipientFilter.Apply(recipients, effectiveNotificationPolicy, effectiveAssignmentPolicy)`
is pure and static; at publish time it:

1. drops recipients whose channel is blocked,
2. orders by preferred-channel order (then by channel),
3. applies the two **role caps** per bucket, preserving that order —
   `MaxPrimaryContacts` for `Role == Primary`, `MaxCopyContacts` for other guardians,
4. then caps the whole sendout at the notification policy's `MaxNotifications` (**D5, unchanged**),
   so the smaller of the two limits wins for the sendout as a whole.

**Bucketing (D4 / §9.2 of the findings doc):** the bucket is keyed on
`AssignmentRecipient.OwnerType`, the authoritative discriminator. A `Student`-owned recipient
carries no guardian role **by construction** (`StudentGuardian.Role` is a non-nullable
`GuardianRole`), so it is exempt from **both** caps; a null role on a guardian-owned row is a data
defect, not a bucketing choice. The null-role test is retained only as a redundant net.

**Range rule (Q7).** A cap applies only when it is `>= 1`. Both write paths reject a non-positive
cap with `ArgumentOutOfRangeException`, which both routes map to **400**; and because rows written
before that rule may hold `0`/negative values, the filter **defensively treats a non-positive cap as
uncapped** (the `MaxNotifications is >= 0` posture) rather than wiping a sendout.

## 4. Grade-detail UI (D6)

- One **Assignment Policy** card on grade detail hosts `AssignmentPolicyEditor`: a four-row grid
  (one row per field) with the tenant-global value, the per-grade override or an `Inherit global`
  badge, and Edit / Reset row actions.
- **Edit** opens `AssignmentPolicyFieldEditDialog` — a `DialogShellBase` dialog with a
  `Global settings | This grade` split; **save writes each scope only if its value changed**, so
  opening and saving without edits never creates or mutates a policy. Reset clears one field's
  override, preserving the grade's other fields.
- This card **absorbed** the retired `GradeSignaturePolicyEditor` (and its test), so the signature
  requirement is configured in exactly one place.

## 5. Out of scope here

| Item | Owner |
|---|---|
| Deleting `FEATURE:RequireAssignmentApproval` from its artefacts (AppHost parameter + fan-out, Admin cold-start fallback, `MigrationService` seeder, Config `/config-flags` row) and retiring the D3 OR | **B2** |
| Dropping the two legacy `requires_signature_default` columns and the R2 compat members (`LegacyUpsert*Request`, the boolean client overloads, the `[JsonIgnore]` DTO mirrors, the server request records' legacy parameter) | **B2** |
| Unlocking/relocking the signature checkbox in `Edit.razor` (Q11 follow-up) | separate follow-up round |
| Cross-context cache invalidation for the derived read surfaces (up to one cache lifetime of staleness) | accepted risk (see the round doc R-B1-1) |

## 6. Verification surface

| Contract | Discriminating test |
|---|---|
| Field set round-trips + 204 semantics | `TenantAssignmentPolicyHandlerTests`, `GradeAssignmentPolicyHandlerTests`, `GradeAssignmentPolicyRoutesTests`, `AssignmentPolicyRoutesTests` (Settings API) |
| Merge + per-field override flags | `EffectiveAssignmentPolicyResolverTests` |
| HTTP fail-open + enum wire form | `AssignmentPolicyResolverTests` |
| Q7 — non-positive cap ⇒ 400 at both write paths; null accepted | `TenantAssignmentPolicyHandlerTests`, `GradeAssignmentPolicyHandlerTests`, `GradeAssignmentPolicyRoutesTests`, `AssignmentPolicyRoutesTests` |
| D4/D5 — role caps, student exemption, smaller cap wins, non-positive ⇒ uncapped | `NotificationRecipientFilterTests` |
| Q6/D3 — derived `RequiresApproval` OR, per-grade cache, fail-open, guarded flag read | `AssignmentRequiresApprovalProjectionTests`, `AssignmentIndexBunitTests`, `AssignmentDetailBunitTests` |
| D2 — wizard pre-fill + `Mandatory` lock, widened always-200 body | `AssignmentCreateBunitTests`, `AssignmentSignatureDefaultRouteTests` |
| D3 enforcement OR (publish/schedule) | `AssignmentApprovalPolicyReconciliationTests` |
| D6 — grid, dialog (per-scope writes, no-change save), card wiring | `AssignmentPolicyEditorTests`, `AssignmentPolicyFieldEditDialogTests`, `GradeLevelDetailPageTests` |
