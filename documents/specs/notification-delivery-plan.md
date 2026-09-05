# Notification & Delivery - Policy Config + Effective Resolution + UI

> **Scope (confirmed):** policy config + effective-policy resolution + Notification &
> Delivery UI tab. **Delivery (actual email/SMS/WhatsApp sending) and the reminder
> worker are OUT of scope** (deferred to the §18 delivery feature).
>
> Drives the deferred **Notification & Delivery** item from
> `grade-level-detail-view-plan.md §9`. Existing foundation is reused:
> `Contact`/`ContactSubscription`/`ContactChannel`, `ContactRoutes` CQRS,
> `IAssignmentNotificationBroadcaster` (assignments), the grade-level Detail page.
>
> **Go-forward scope note (2026-09-03):** the shipped policy half of this plan is
> complete; the deferred delivery half is subsumed by
> `assignment-request-feature-spec.md` §5 as workstream WS-E (deep links, channel
> delivery + `NotificationLog`, reminder/overdue/archive worker) — see
> `documents/solution/assignment-request-go-forward-breakdown.md`.

## 1. Goals

- Introduce a per-tenant **global default** notification policy and optional
  per-grade **override** policies, with explicit **inheritance** semantics
  (non-null grade field overrides the tenant default; null inherits).
- Compute an **effective (merged) policy** for any grade and surface it in the UI,
  marking each field as either an explicit override or "uses global default".
- Apply the effective policy at **publish time** (filter blocked channels, apply
  preferred channel order, enforce per-send notification caps) — resolution only,
  no actual delivery.
- **Out of scope:** sending emails/SMS/WhatsApp, `AssignmentReminderWorker`,
  link-validity expiry enforcement.

## 2. Domain model

### `TenantNotificationPolicy` (Settings.Core) — one row per tenant (global default)
| Field | Type | Meaning |
|---|---|---|
| `TenantId` | Guid | partition key; one row per tenant |
| `PreferredChannelOrder` | `ContactChannel[]` | Email → SMS → WhatsApp preference |
| `BlockedChannels` | `ContactChannel[]` | channels never used for this tenant |
| `MaxNotifications` | int? | cap per sendout (null = no cap) |
| `MaxReminders` | int? | cap on reminders (unused this phase; field present) |
| `ReminderIntervalHours` | int? | (unused this phase; field present) |
| `LinkValidityDays` | int? | (unused this phase; field present) |
| `SendoutTimeOfDay` | TimeOnly? | (unused this phase; field present) |
| `SendoutIntervalMinutes` | int? | (unused this phase; field present) |

### `GradeNotificationPolicy` (Students.Core) — optional 1:1 per grade
- Same fields as `TenantNotificationPolicy` + `GradeLevelId` (PK).
- Semantics: **non-null field overrides tenant default; null inherits.**

### Effective-resolution service (Students.Core)
`IEffectiveNotificationPolicyResolver` (or a Settings/Students service):
- Input: `tenantId`, `gradeLevelId`.
- Output: an effective policy object with, per field, the resolved value **and**
  whether it came from the grade override or the tenant default.
- Merge rule: grade value where non-null, else tenant default.

## 3. CQRS / API surface

### Settings (TenantNotificationPolicy)
- `GetTenantNotificationPolicy` query (returns tenant default, or a default if none set).
- `UpsertTenantNotificationPolicy` command (`PUT`/`POST .../settings/notification-policy`).

### Students (GradeNotificationPolicy)
- `GetGradeNotificationPolicy(gradeLevelId)` query — returns the effective policy
  (resolved) + per-field source flags + raw grade override.
- `UpsertGradeNotificationPolicy(gradeLevelId)` command — partial update; null fields
  mean "inherit / clear to global default".

### Publish-time resolution (Assignments.Core)
- `PublishAssignmentCommandHandler` consults the effective policy (via a resolver
  dependency) and **filters/resolves the recipient set**: drop recipients whose only
  channels are `BlockedChannels`; order preferred channels; cap recipients per
  sendout at `MaxNotifications`. Broadcast still goes through the existing
  `IAssignmentNotificationBroadcaster` (no delivery).
- Assignments needs a lightweight interface to read the effective policy across
  projects (Students.Core resolver or a shared contract).

## 4. Admin client methods
- `GetTenantNotificationPolicyAsync` / `UpsertTenantNotificationPolicyAsync`.
- `GetGradeNotificationPolicyAsync(gradeLevelId)` / `UpsertGradeNotificationPolicyAsync`.

## 5. UI — third "Notification & Delivery" tab (grade-level Detail.razor)
- Effective (merged) policy view with per-field **"uses global default"** indicator
  when the grade has no override for that field.
- Per-grade override editor: set/clear each field (clear ⇒ inherit).
- Tenant default view/edit is out of tab scope unless trivial; keep it read-only
  reference in the tab (per-grade overrides only, matching "per-grade exceptions").

## 6. Stacked PR train (new stack rooted on main, dependent-base)
| # | Branch | Contents |
|---|--------|----------|
| 1 | `nd/1-tenant-policy` | `TenantNotificationPolicy` + EF config + migration + CQRS (Settings.Core) + tests |
| 2 | `nd/2-grade-policy` | `GradeNotificationPolicy` + migration + CQRS + **effective-resolution service** + tests (Students.Core) |
| 3 | `nd/3-publish-wiring` | Wire effective policy into `PublishAssignmentCommandHandler` (filter channels/order/cap) + admin client methods + tests |
| 4 | `nd/4-notification-ui` | Third tab in `Detail.razor` (effective policy + per-grade override editor) + bUnit tests |

> UI layer note: `Detail.razor` lives in the (unmerged) grade-level stack `#118`. The
> nd/4 UI layer is therefore based on that stack's top branch, not on nd/3.

## 7. Test plan
- **Unit (Settings/Students):** upsert idempotency; merge semantics (grade non-null
  overrides, null inherits); tenant isolation; effective-policy per-field source flags.
- **Unit (Assignments):** publish filters blocked-channel-only recipients; applies
  preferred order; caps at MaxNotifications; tenant policy fallback when no grade policy.
- **bUnit (Admin):** third tab renders effective policy; "uses global default"
  indicators; override editor set/clear round-trips.
