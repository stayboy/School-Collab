# Notification template authoring — implementation record

Companion to the spec ([`documents/specs/notification-template-authoring-redesign.md`](../specs/notification-template-authoring-redesign.md)),
which remains the source of truth for the design and the grill record. This page records **how** the
change landed and **why** the non-obvious seams are where they are; the round log is
[`documents/rounds/round-notification-template-authoring-redesign.md`](../rounds/round-notification-template-authoring-redesign.md).

## What replaced what

| Before | After |
|---|---|
| One tabbed `NotificationTemplates.razor` page with a (kind, channel) editor | Landing `EntityGrid` + `/notification-templates/create` + `/notification-templates/{id}`, sharing one `TemplateEditor` |
| `TenantNotificationTemplate` strict-tenant (`tenant_id NOT NULL`) | **Hybrid** entity: `NULL` = system blueprint, real id = tenant-owned (the `CodedValue` shape) |
| One row per slot; the "default" was that row | Several live rows per slot per **scope**; the unconstrained one is that scope's default |
| No ownership surface | Owner column/filter, adopt (blueprint → tenant), publish portal-wide (tenant → blueprint) |
| `PanelSection` lived in Assignments | Moved to `SchoolCollab.Admin.Shared/Components/` and reused by the template rail |

## The seams that carry the design

1. **Scope is resolved once, from the tenant provider** — `NotificationTemplateOwnership.OwnerId`.
   `Guid.Empty` (the sentinel for "no tenant selected") maps to `null`; the entity factories reject
   `Guid.Empty` outright, so the sentinel can never be persisted. Every write handler resolves the scope
   from the same helper, which is what makes the 403 rules in spec §6 checkable rather than scattered.
2. **Reads are wider than writes.** The hybrid filter (`TenantId == current OR NULL`) lets a tenant
   session *see* blueprint rows — that is what makes adopt possible — so writes guard explicitly
   (`template.TenantId != ownerId` ⇒ `TenantNotificationTemplateScopeException` ⇒ 403). A 404 would be a
   lie: the caller can see the row.
3. **Exactly one cross-tenant read**, `GET …?includeTenantOwned=true`, honoured only in system scope and
   implemented with `IgnoreQueryFilters(["Tenant"])` (the named filter only — soft-deleted rows stay
   hidden) plus `SuppressTenantGuard()` on the write that follows in promote. A tenant session that sends
   the flag gets its normal scope back, which a test pins.
4. **Copies, not links.** Adopt and promote copy subject, body and every constraint row; the source is
   untouched, and an identical (normalised) constraint set in the target scope is a 409 rather than a
   second candidate that would make the matcher's tie-break ambiguous.
5. **Precedence is data, not code paths.** The resolver payload carries `tenantId`/`ownerName`; the
   evaluator drops a blueprint twin of a tenant row, then orders specificity → scope rank → `CreatedAt` →
   id, and applies Q67's default promotion **within** the tied row's scope. Nothing on the send path knows
   about "adopted" or "promoted" — only about scopes.
6. **The editor is read-only by scope, not by role.** `ReadOnly = _sessionIsSystem != _model.IsSystem`
   hides Save/Reset/reason and the constraint kebab, and swaps the primary button for the copy action that
   makes the row editable. One component therefore serves both directions (adopt and promote).

## Migration

`20261021052500_MakeNotificationTemplateTenantNullable` — `ALTER COLUMN tenant_id DROP NOT NULL` on
`tenant_notification_templates` and `template_constraints`. Additive and backward compatible (an existing
row keeps its tenant id; the old app reads it unchanged). `Down()` raises if any NULL row exists: a reversal
cannot decide which tenant a blueprint row belonged to, and stamping `Guid.Empty` would violate the
domain's own scope rule — so it fails loudly instead.

## Close-out pass (2026-10-21)

The static review of W1–W3 found two defects and six drift items; this pass closed them (round doc,
`## Review` → `## Acceptance`).

1. **The conflict warning compared across scopes.** W3 widened the editor's sibling read to include the
   system blueprint (that is what makes adopt possible), and the warning was never told. A tenant row
   identical to a blueprint row is §3.9 **shadowing**, not ambiguity. The fix filters to the draft's
   scope — and the interesting part is which scope: on Edit it is the row's, on **Create it is the
   session's**, because a create draft carries no `TenantId` and `IsSystem` is therefore `true` by
   construction. The first cut of the fix used the DTO and inverted the bug for every tenant author;
   `TheCreateDraft_ComparesWithinTheSessionsScope` now pins both directions.
2. **`async void` through `Action<T>`.** The landing's shared error wrapper took `Action<T>`, so the
   reset flow's reload was `async void` — unobservable and unawaited. It takes `Func<T, Task>` now.
3. **The preview had no test of its own.** `TemplateEditorPreviewTests` restores the coverage the
   deleted page suite owned: rendered subject/body, unknown-tag literal, degraded note with Save still
   working.
4. **The read-only mirror case was untested** (system session on a tenant row → Publish portal-wide).
5. **A fabricated default hid a scope.** `OwnerName = "System"` / `TenantId = null` on the DTOs meant a
   construction site that forgot the pair silently claimed the blueprint. Removing them is a
   compiler-guided sweep (four test helpers), and each site now states its scope.

Also in this pass: the spec's §3.2 Reset row, §6 security table (two cross-tenant reads, owner-signed)
and §8 verification names now match the code; the `?grade=` arrival renders the info bar §3.2 promised;
and the tenant-guard rule (`dotnet-best-practices`) was corrected — `SuppressTenantGuard()` is a
**save**-time guard, so a read-shaped cross-tenant access uses the named filter suppression plus
`!IsDeleted`, never the guard.

## Verification

| Gate | Result |
|---|---|
| `SchoolCollab.Admin.Tests.Unit` | **736 / 736** (730 + 6 close-out tests: 3 editor/detail, 2 preview, 1 grade bar) |
| `SchoolCollab.Settings.Tests.Unit` | 655 / 655 (19 new ownership tests) |
| `SchoolCollab.Settings.Api.Tests.Unit` | 58 / 58 (403/404/409 arms + the query flag) |
| `SchoolCollab.ArchitectureTests.Unit` | 142 / 142 (OpenAPI pin 53 → 55 of 71, measured) |
| `SchoolCollab.Assignments.Api.Tests.Unit` | **202 / 202** (whole project, including the 13-test template/resolver slice) |
| `SchoolCollab.Assignments.Tests.Unit` | 1385 / 1385 on re-run (one order-dependent flake in the first parallel-order run; passes in isolation) |
| `dotnet build SchoolCollab.slnx` + mobile head | 0 errors |

Not covered here: a browser pass (the AppHost cannot serve in this environment — handed to the owner
with five click paths, round doc `## UI Tester`) and dialog *invocation* in bUnit; the flows are covered
at handler, route and page-offer level instead.

