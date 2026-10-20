# Notification template authoring — landing grid, detail page, and system/tenant ownership

> **Status: adopted design, NOT implemented** — grill round 2026-10-20, owner answered **"all as
> recommended"** (Q1–Q22 below). This file is the **source of truth for the authoring surface**.
> It **supersedes §3.10** (management surface) of `documents/specs/assignment-notification-templates.md`
> and **amends** that file's §3.1 (the "no system-wide blueprint row" note), §3.7 (selection
> precedence) and §5 (API surface). Data model, renderer, tag vocabulary, constraints semantics and
> audit remain governed by `assignment-notification-templates.md` except where this file says
> otherwise. Execution mode: **Full four-agent round (Tier 3, attended)**, three workstreams — §9.

## 1. Findings (substrate inventory, 2026-10-20)

| Question | Answer found | Consequence |
|---|---|---|
| What ships today? | One tabbed master–detail page: `src/Settings/SchoolCollab.Settings.Application/Components/Pages/NotificationTemplates.razor` (1071 lines, route `/notification-templates`), two nested `FluentTabStrip`s (4 kinds × 2 channels), per-slot row inventory, inline editor, preview, audit | the page is **replaced**, not edited; its slot state machine (`SlotState`) and preview/tag logic move into a shared editor |
| What is the store? | `TenantNotificationTemplate` (Kind, Channel, Subject, Body, `xmin`) + `TemplateConstraint` children (Dimension, Value); `Kind`/`Channel`/`Dimension` stored as **int** (`HasConversion<int>()`); `(TenantId, Kind, Channel)` is a **non-unique** filtered index | adding a channel value needs **no enum migration**; making `TenantId` nullable is a plain `ALTER COLUMN … DROP NOT NULL` on both tables (no FK to `tenants` is configured) |
| Where is the WhatsApp block? | `TenantNotificationTemplate.Create` throws for `NotificationChannel.WhatsApp`; `GetApplicableNotificationTemplatesHandler` hardcodes `Channels = [Email, SMS]` and filters WhatsApp out | both are in scope for Q1 |
| Does the send path need work for WhatsApp templates? | `HttpNotificationTemplateResolver` reads the whole Settings list and filters `row.Channel == (int)channel`; `NotificationTemplateRenderer` routes every non-Email channel through `RenderSms` | **no Assignments work** to honour a WhatsApp row; the built-in default already covers the empty case |
| Can a tenant's authored SMS template reach WhatsApp today? | No — WhatsApp sends resolve channel `WhatsApp`, find no row, and render the built-in default | fixed as a side-effect of Q1 (recorded, not a separate goal) |
| Which stamps exist? | `PublishEmailTemplateId` / `PublishSmsTemplateId` only; WhatsApp is evaluator-resolved, never author-picked | the publish dialog stays two-slot; `PublishOptionsDto` gains owner info only |
| What API exists? | `GET /api/settings/notification-templates`, `GET /{kind}/{channel}` (default row, 204 = unset), `POST`, `PUT /{id}`, `DELETE /{id}?reason=`, `GET /applicable/{gradeLevelId}`, `GET /{kind}/{channel}/audit` | **no by-id read**, and the audit stream is keyed kind × channel |
| Does audit carry the row identity? | `TemplateAuditEntry.TemplateId` exists; `TemplateAuditEntryDto` does **not** expose it | per-template audit = DTO field + optional filter, **no backfill** |
| Where does `PanelSection` live? | `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/PanelSection.razor(.css)`; one markup consumer (`AssignmentAuthoring.razor`, which already has `@using SchoolCollab.Admin.Shared.Components`); tests address `.panel-section__*` classes only | must **move** to `SchoolCollab.Admin.Shared` — Settings may not reference Assignments.Application |
| What reusable UI exists? | `LandingPage<TItem>` (owns `EntityGrid`), `RowActionsMenu` (`ForceKebab`, `RowAction.Navigate/Callback`, destructive confirm), `Chip`, `DialogShellBase`/`DialogShellFooter`/`ReadOnlyDialogShell`, `RichInstructionEditor`/`RichInstruction`, `DropdownForEnum`/`DropdownComponent`, `FormRow`, `NotificationTemplateApiClient`, `NotificationTemplatePreviewClient`, `StudentsLookupClient`, `CodedValuesApiClient` | no new design-system component is needed except the kind banner and the two dialogs |
| What tenancy machinery exists for a blueprint layer? | `IHybridTenantEntity` (+ `TenantOrGlobalEntityTypeConfigurationBase`, filter `TenantId == CurrentTenantId OR TenantId == null`), proven by `CodedValue`; `CreateCodedValueHandler` writes `TenantId = null` under `SuppressTenantGuard()` when `CurrentTenantId == Guid.Empty` | the system layer is a **hybrid conversion** of the existing two tables (Q16), not a new table |
| How does the admin know the active tenant? | `VisibleTenantService` (claims `tenant_id`/`tenant_name`); the dev header switcher's "(system / no tenant)" maps to `Guid.Empty` | "currently selected tenant" needs no new plumbing; the API already receives the tenant via the propagation header |
| Where does a tenant's display name come from? | `Tenant.Name` in **settings-db** | the `OwnerName` join is local to the Settings context — no cross-context call |
| Which tests break? | `tests/SchoolCollab.Admin.Tests.Unit/NotificationTemplatesPageTests.cs` (9 tab/slot tests) + Settings handler/route/wire tests that assert the WhatsApp refusal | rewritten/split — §8 |
## 2. Settled decisions (grill record, 2026-10-20)

| # | Decision | Owner answer |
|---|---|---|
| Q1 | **WhatsApp becomes an authorable contact mode** (a first-class third slot): drop the `Create` guard, add WhatsApp to the authoring channel set and to the grade-card slot list. No migration; the resolver is already channel-keyed and the renderer already renders every non-email channel as plain text. Publish keeps its two stamps, so WhatsApp sends stay evaluator-resolved. Rejected: an alias that writes the SMS row (two rows meaning one thing), and a disabled option (ignores the ask). | as recommended |
| Q2 | The **type** and **preferred mode of contact** dropdowns are editable on Create and **read-only indicators on Detail**, with the reason "Type and contact mode are fixed per template — create a new one". No move operation. | as recommended |
| Q3 | **Add-new is a page** (`/notification-templates/create`), reached from the landing's `+ New Template` — the `+ New Assignment` precedent. Not a dialog: the body editor, two dropdowns, tag chips and the constraint rail do not fit one. | as recommended |
| Q4 | **Landing grid**: Type banner · Owner · Contact mode · Template (linked subject/excerpt) · Constraints (`N constraints` link, or a `Tenant default` badge) · Updated. Toolbar: type filter, contact-mode filter, owner filter, search over subject + plain-text body (client-side). | as recommended |
| Q5 | **Landing row actions**: the subject cell links to detail *and* the kebab carries Open · Adopt to this tenant · Publish portal-wide… · Reset to built-in default… (destructive, confirm + reason). Reset is also on Detail. | as recommended |
| Q6 | **One kind banner component** (`NotificationTemplateKindBanner`): accent strip + icon + label, four variants on verified `BadgeColor` members — Publish `Brand`, Reminder `Warning`, Completion `Success`, Overdue `Danger` — used by the grid **and** the detail header. No hard-coded hex, no new tokens. | as recommended |
| Q7 | **Constraint add = the Constraints panel's header kebab**, one item per dimension ("Add grade levels", "Add streams", "Add activity groups", "Add subjects", "Add assignment types"). Each opens a dialog **preset to that dimension** where several values of that one dimension can be picked; each pick becomes its own constraint row, deduped by `(dimension, value)`, rendered as a chip. | as recommended |
| Q8 | **Viewing constraints uses one shared read-only dialog** (not a chip click — `Chip` in badge mode has no click handler): `TemplateConstraintsDialog` opens from the panel kebab's "View all constraints" **and** the grid's "N constraints" link. Chips stay inline and dismissible. | as recommended |
| Q9 | **Constraints are staged and saved once**: the dialog/chips mutate only the draft; Save persists subject + body + the whole set in one `PUT`, one optional reason, one `TemplateAuditEntry`. Reset stays a separate audited action. | as recommended |
| Q10 | **Rail order**: inside the right rail, **Constraints above Preview**; the left column is a plain titled "Message" section (not a `PanelSection`) carrying the dropdowns, subject, body editor and tag chips. | as recommended |
| Q11 | **One shared `TemplateEditor`** hosts Create and Detail (`Mode` = Create \| Edit, `TemplateFormModel` draft); `Index`/`Create`/`Detail` are thin route hosts. | as recommended |
| Q12 | The **grade-detail deep link survives**: `?grade=<id>` filters the landing rows and is forwarded to Create, which prefills a `GradeLevel` constraint (spec §3.11's one-click promise). | as recommended |
| Q13 | **Add a by-id read**: `GET …/notification-templates/{id:guid}` → DTO \| 404, plus `NotificationTemplateApiClient.GetByIdAsync(Guid)`. | as recommended |
| Q14 | **Move `PanelSection` into `SchoolCollab.Admin.Shared`** (markup + css, unchanged class names and rule contract). Move, never copy. | as recommended |
| Q15 | **Audit is per-template**: `TemplateAuditEntryDto` gains `TemplateId`; `GetTemplateAuditEntries` and its route gain an optional `templateId` filter; Detail shows only that row's history. No backfill. | as recommended |
| Q16 | **The store becomes hybrid**: `TenantNotificationTemplate` **and** `TemplateConstraint` implement `IHybridTenantEntity` (`tenant_id` nullable; `NULL` = system/portal-wide, visible to every tenant via the hybrid filter). One store, no second table. `Guid.Empty` stays invalid. | as recommended |
| Q17 | **Only a system-scope session may write a portal-wide template**: `CurrentTenantId == Guid.Empty` ⇒ `TenantId = null` under `SuppressTenantGuard()` (the `CreateCodedValueHandler` rule); a real-tenant session attempting a system write gets a typed **403**. `System` starts empty — built-in code defaults remain the final fallback; nothing is seeded. The one-unconstrained-default rule becomes **per scope**. | as recommended |
| Q18 | **Adopt and publish are one-way copies**: system → current tenant ("Adopt to this tenant"), tenant → system ("Publish portal-wide…", system scope only). Neither mutates the source, neither propagates later edits, and an identical existing copy is a reported conflict. | as recommended |
| Q19 | **Resolution precedence**: candidates = tenant ∪ system; a tenant row **shadows** a system row with an identical constraint set; then specificity; on an equal-specificity tie a **tenant** row wins. Assignments' candidate payload gains scope; the evaluator gains one tie-break key. | as recommended |
| Q20 | **Owner display is local**: list DTO gains `TenantId` (nullable) + `OwnerName` (`"System"`, else `Tenant.Name`); the grid shows a `System` badge or the tenant name; **system rows are read-only in a tenant session** (no Save/Reset; primary action is Adopt). | as recommended |
| Q21 | **Delivery**: one **Full four-agent Tier 3, attended** round in three workstreams (W1 landing + banner + owner + create + by-id; W2 detail + constraints + preview rail + audit + WhatsApp; W3 hybrid store + adopt/promote + precedence). | as recommended |
| Q22 | **Spec destination**: this file, + one-line amendments to `assignment-notification-templates.md` §3.1, §3.7, §3.10, §5, + a `documents/solution/` implementation record after the round. | as recommended |

**Recorded reversal.** `assignment-notification-templates.md` §3.1 and the `TenantNotificationTemplate`
doc-comment state that "there is no separate system-wide blueprint row — built-in defaults live in the
renderer". Q16 reverses that: a **system** layer now exists as `tenant_id IS NULL` rows. The built-in
code defaults are **not** removed; they remain the terminal fallback when no tenant or system template
applies or the Settings read fails (§3.9).
## 3. Design

### 3.1 Routes and page composition

The tabbed page is **deleted** and replaced by a folder of thin route hosts (the `CodedValues/` precedent):
`src/Settings/SchoolCollab.Settings.Application/Components/Pages/NotificationTemplates/`.

| Route | File | Surface |
|---|---|---|
| `/notification-templates` | `Index.razor` | Landing grid (`LandingPage<TenantNotificationTemplateDto>` → `EntityGrid`). `?grade=<id>` filters. |
| `/notification-templates/create` | `Create.razor` | Thin host: `<TemplateEditor Mode="Create" />`. `?grade=<id>` prefills a `GradeLevel` constraint. |
| `/notification-templates/{Id:guid}` | `Detail.razor` | Thin host: `<TemplateEditor Mode="Edit" Id="@Id" />` + the action bar. |

`NavMenu.razor`'s existing `/notification-templates` entry and the `Settings.razor` hub card are unchanged
(same href, same label). `NotificationTemplatesCard.razor`'s "Manage templates" link
(`/notification-templates?grade={id:D}`) is unchanged.

### 3.2 Landing page

`<LandingPage TItem="TenantNotificationTemplateDto">` with `Title="Notification Templates"`,
`CreateLabel="+ New Template"`, `CreateRoute="/notification-templates/create"` (the landing's own create
contract — no `OnCreate` dialog), `RowActions`, `RowActionsAriaLabel="Template actions"`,
`RowActionsUseMenuService="false"`, `SearchText`/`SearchTextChanged`, `GridSettings`.

| Column | Rendering |
|---|---|
| Type | `<NotificationTemplateKindBanner Kind="context.Kind" />` |
| Owner | `context.TenantId is null` → `<FluentBadge Color="BadgeColor.Informational">System</FluentBadge>`; else `context.OwnerName` |
| Contact mode | `Email` / `SMS` / `WhatsApp` (`Channel` label map; SMS keeps its existing casing) |
| Template | `FluentLink` → `/notification-templates/{id}`; label = `Subject` when the mode is Email and it is non-empty, else the plain-text body excerpt (`RichInstructionSanitizer.ToPlainText`, 80 chars + ellipsis) |
| Constraints | `Constraints.Count == 0` → `<FluentBadge Color="BadgeColor.Success">Tenant default</FluentBadge>` (label `System default` on a system row); else a `FluentLink` labelled `N constraint`/`N constraints` (singular at 1) that opens `TemplateConstraintsDialog` for that row |
| Updated | `UpdatedAt.ToLocalTime()` formatted `yyyy-MM-dd HH:mm` |

**Toolbar filters** (`<ToolbarFilters>`, `DropdownComponent` wrappers, never raw selects):
All types / Publish / Reminder / Completion / Overdue; All modes / Email / SMS / WhatsApp; All owners /
System / This tenant. Search is client-side over subject + plain-text body (the `ConfigFlags/Index`
posture). A `?grade=` arrival renders an info bar naming the filter and keeps every row reachable that
`AppliesToGrade` allows (the existing rule, moved into `NotificationTemplateScopeRules`).

**Row actions** (`RowActionsMenu`, `ForceKebab` computed from `HasKebabActions` over all rows):

| Action | Visible when | Behaviour |
|---|---|---|
| Open | always | `RowAction.Navigate` → `/notification-templates/{id}` |
| Adopt to this tenant | row is system **and** the session has a real tenant | confirm → `POST …/{id}/adopt` → navigate to the returned new row |
| Publish portal-wide… | row is tenant-owned **and** the session is in system scope | confirm → `POST …/{id}/promote` → navigate to the returned system row |
| Reset to built-in default… | row is writable in the current scope | destructive + `ReasonPromptDialog` → `DELETE …/{id}?reason=` → reload |

### 3.3 Template editor layout (Detail and Create share it)

`TemplateEditor.razor(.razor.css)` — one component, two modes. Layout mirrors
`.authoring-layout--cq` (`AssignmentAuthoring.razor.css:141`):

```css
.template-editor-layout {
    display: grid;
    grid-template-columns: minmax(0, 1fr) minmax(320px, 460px);
    grid-template-areas: "message rail";
    gap: 16px;
    align-items: start;
}
.template-editor-layout > #template-editor-message { grid-area: message; }
.template-editor-layout > #template-editor-rail    { grid-area: rail; display: flex; flex-direction: column; gap: 16px; }
@media (max-width: 1400px) {
    .template-editor-layout { grid-template-columns: minmax(0, 1fr); grid-template-areas: "message" "rail"; }
}
```

| Region | Content |
|---|---|
| Left — `#template-editor-message` | A plain titled `<section>` ("Message") — **not** a `PanelSection`: Type dropdown + Preferred mode of contact dropdown (`FormRow` + `DropdownForEnum`/`DropdownComponent`, W9 width), subject (`FluentTextInput`, email only), body (`RichInstructionEditor` for Email; `FluentTextArea` + the existing 140/160-char guidance for SMS and WhatsApp), tag chips (click-to-insert, unchanged set) |
| Right rail — `#template-editor-rail` | **`PanelSection` "Constraints"** (kebab + chips + conflict warning), then **`PanelSection` "Preview"** (exact render path, Refresh, degraded note) |
| Below the grid (full width) | Audit trail section — a plain `<section>` with its own heading and `FluentDataGrid` (**not** a `PanelSection`: exactly two exist on this surface) |

Test seams: `template-editor-message`, `template-editor-rail`, `template-constraints-title` / `-actions`
(kebab), `template-preview-title`, `template-preview-body`, `template-audit-grid`. `PanelSection`'s own
contract (full-width rule under title **and** actions) is inherited unchanged.

### 3.4 Create page

`TemplateEditor Mode="Create"`: Type and Preferred mode of contact are **enabled** dropdowns; Save calls
`CreateAsync` and navigates to `/notification-templates/{returnedId}`; Reset is absent. With
`?grade=<id>` the draft starts with `GradeLevel = <id>` in the constraint set (and the rail shows it as a
chip). A system-scope session creates a **system** row (the API infers scope — §5); the form shows an
"Owner: System" chip so the operator sees what they are creating.

### 3.5 Detail page

`TemplateEditor Mode="Edit"`: the two dropdowns are **disabled** with the reason text; Owner chip in the
header (`System` / tenant name). Action bar under the grid: `Reason` input (recorded in the audit trail),
`Save` (primary), `Reset to built-in default…` (destructive → confirm + reason). A tenant session opening a
**system** row renders the editor read-only (no Save/Reset) with `Adopt to this tenant` as the primary
action. A missing/foreign id renders "Template not found" + Back (the `ConfigFlagDetail` posture).

No unsaved-changes navigation guard in v1 (recorded; the Assignments authoring guard is not reused here).

### 3.6 Constraints

**Value encodings** (unchanged — the matcher's): `GradeLevel` = grade-level id (`D` format),
`Stream` = stream coded-value id, `ActivityGroup` = activity-group id, `Subject` = topic id,
`AssignmentType` = the enum **name** (`Digital`/`SemiManual`/`Manual`). Lookups: `StudentsLookupClient`
(grade levels / topics / activity groups) + `CodedValuesApiClient` (`Streams` children); a lookup failure
disables **adding** constraints and shows the existing warning, never drops stored values.

**Interactions.**

1. The Constraints `PanelSection` header carries `RowActionsMenu ForceKebab UseMenuService="false"` with one
   item per dimension, all carrying `FluentIcons.Add` (the Targets-&-Audience precedent: the glyph names the
   action). A disabled editor disables every item with its reason.
2. Each item opens `ConstraintEditDialog` (`DialogShellBase<ConstraintEditModel, ConstraintEditResult>`)
   preset to that dimension (`CategoryLocked` — the dimension selector is hidden, as `TargetsAndAudienceDialog`
   does). The operator appends values; each pick renders as a dismissible `Chip` inside the dialog; Submit
   returns the picked list.
3. The editor merges the result into the draft, deduping by `(Dimension, Value)` (case-insensitive, trimmed).
   Chips in the rail dismiss on the chip, staged only.
4. Empty state: "No constraints — this template is the {tenant|system} default for its type and contact mode."
5. **Conflict warning** (existing rule, now scope-aware): two writable templates of the same Kind × Channel
   **and the same scope** carrying an identical normalised constraint set → warning bar in the rail. A
   tenant default and a system default are not a conflict (they are different scopes).
6. Saving a set that collides with the one-default-per-scope rule returns **409** and renders in the editor's
   error bar (the API is the authority; the warning is advisory).
### 3.7 Ownership model (hybrid tenancy)

`TenantNotificationTemplate` and `TemplateConstraint` both become `IHybridTenantEntity`
(`TenantId` nullable). Their configurations move from `TenantEntityTypeConfigurationBase` to
`TenantOrGlobalEntityTypeConfigurationBase`, which installs the named `Tenant` filter
`TenantId == CurrentTenantId OR TenantId == null`. `Guid.Empty` remains invalid on a stored row.

| Scope | `CurrentTenantId` | Rows visible through the store filter | Writes |
|---|---|---|---|
| Tenant | real Guid | that tenant's rows **+** system rows (`NULL`) | stamp `TenantId = current` |
| System | `Guid.Empty` | system rows (`NULL`) only | stamp `TenantId = null` under `SuppressTenantGuard()` |

`TenantNotificationTemplate.Create`/`Update` gain a `Guid? tenantId` (or the handlers pass the resolved
scope), so no caller can stamp `Guid.Empty`. Constraint rows are built with the **parent's** tenant id
(the `Create` factory already builds them; it now receives the scope).

`TenantNotificationTemplateDefaults.EnsureNoCompetingDefaultAsync` becomes scope-aware: it compares only
within the row's scope, so one unconstrained **tenant** default and one unconstrained **system** default may
coexist; the conflict message names the scope ("for this tenant" / "for the system blueprint").

**Owner projection.** `TenantNotificationTemplateDto` gains `Guid? TenantId` and `string OwnerName`
(`"System"` when null, else `Tenant.Name` joined in settings-db — `Tenant` is a settings-db entity, so the
join is local). `GetTenantNotificationTemplatesHandler` performs that join once for the list and the by-id
read. `ApplicableTemplateDto` gains the same pair so the grade card can mark system templates.

### 3.8 Adopt and publish portal-wide

| Command | Route | Gate | Effect |
|---|---|---|---|
| Adopt | `POST /api/settings/notification-templates/{id}/adopt` | caller has a **real tenant**; source row is **system** | copies subject/body + every constraint row into the caller's tenant; returns the new `TenantNotificationTemplateDto` (200). An identical constraint set already present in that tenant ⇒ **409** ("already adopted / identical constraint set"). Audited as `Created`. |
| Publish portal-wide | `POST /api/settings/notification-templates/{id}/promote` | caller is in **system scope** (`Guid.Empty`) | copies the source tenant row into system scope (`TenantId = null`); returns the new DTO. An identical system-side set ⇒ **409**. The source row is **not** modified. Audited as `Created` with `TenantId = null`. |

The promote path is the one deliberate cross-tenant read in this feature and is gated by scope, not by a
tenant claim: a system-scope session has no tenant, so it cannot leak into a tenant's own session view.
Both commands are one-way copies — later edits on either side do not propagate; re-adopting is explicit and
hits the 409 when a copy already exists.

### 3.9 Resolution precedence at send time

The candidate set becomes **(tenant ∪ system)** for the requested kind × channel. `HttpNotificationTemplateResolver`
already reads the whole Settings list and filters by channel, so it needs only the new `tenantId` field in its
wire mirror. `NotificationTemplateEvaluator` gains:

1. **Shadowing** — a system candidate whose normalised constraint set equals a tenant candidate's is dropped
   (the tenant row *is* the override of that blueprint).
2. **Order key** — `specificity desc`, then **scope rank `asc`** (tenant = 0, system = 1), then `CreatedAt asc`,
   then `TemplateId`.
3. **Tie rule (Q67, preserved per scope)** — when the top two share specificity **and** scope rank, and that
   specificity is > 0, the unconstrained default **of the same scope** is promoted to the front; with no such
   default the oldest `CreatedAt` wins.

Publish stamps still win first (a stamped id resolved against the candidate list); the built-in defaults
remain the terminal fallback when nothing applies or the Settings read is unreadable (the existing fail-open
contract). `PublishTemplateCandidateDto` gains `bool IsSystem` and `string? OwnerName`, so the publish dialog's
picker can show where a template came from.

### 3.10 WhatsApp as an authorable mode

- `TenantNotificationTemplate.Create` no longer throws for `NotificationChannel.WhatsApp`; the subject stays
  null for every non-email channel (`Update` already enforces this).
- The authoring channel set is `Email`, `SMS`, `WhatsApp` (`Kinds`/`Channels` sets in the editor, the landing
  filters, and `GetApplicableNotificationTemplatesHandler.Channels`).
- Send behaviour needs **no** Assignments change: the resolver reads channel = WhatsApp, the renderer renders
  the plain-text path, and the short link is the `/l/{code}` form (already channel-driven).
- No WhatsApp publish stamp: the publish dialog keeps Email + SMS, and WhatsApp selection is constraint-driven.
- The grade card gains a `WhatsApp` row per kind (it is a real authorable slot now).

### 3.11 `PanelSection` relocation

Move `PanelSection.razor` + `PanelSection.razor.css` from
`src/Assignments/…/Components/Pages/Assignments/` to `src/SchoolCollab.Admin.Shared/Components/` (namespace
`SchoolCollab.Admin.Shared.Components`). Class names (`panel-section`, `panel-section__header/__title/__rule/__actions`),
the full-width-rule contract and the ids callers pass are unchanged, so `AssignmentAuthoring` (already importing
that namespace) and `AssignmentAuthoringBunitTests` keep compiling and passing. Record the move in this spec and
in the implementation record; do **not** create a Settings-local copy.

### 3.12 Audit trail

- `TemplateAuditEntryDto` gains `Guid TemplateId`.
- `GetTemplateAuditEntries` gains an optional `Guid? TemplateId`; the route gains `?templateId=`.
- The Detail page's audit grid is filtered to the row on screen, most recent first, columns When / Actor /
  Change / Reason (the `/config-flags` precedent).
- System-row mutations write `TemplateAuditEntry.TenantId = null` (already nullable).

### 3.13 Kind banner

`NotificationTemplateKindBanner.razor(.css)` in the Settings application project (it is a Settings surface,
not a design-system primitive): a compact strip with a leading accent bar, a `FluentIcon` and the kind label;
root carries `data-kind="{Kind}"` for assertions. Colour comes from the verified `BadgeColor` set (Publish
`Brand`, Reminder `Warning`, Completion `Success`, Overdue `Danger`) so light/dark themes resolve through the
library — no literal hex in the css (the `PanelSection` surface lesson).

## 4. Data model and migrations

| Database | Change | Migration |
|---|---|---|
| settings-db | `tenant_notification_templates.tenant_id` → **nullable**; `template_constraints.tenant_id` → **nullable** | `MakeNotificationTemplateTenantNullable` (follows `.github/copilot/rules/ef-migrations.md`) |
| settings-db | `(TenantId, Kind, Channel)` filtered index unchanged (a NULL-able column is valid in a Postgres index); no new index | in the same migration's model snapshot |
| settings-db | no column for owner: it is derived (`TenantId` + the `tenants` join) | — |
| settings-db | `TemplateAuditEntry.TenantId` already nullable | — |

No backfill: every existing row already carries its real tenant id and keeps it; system rows only appear
when a system-scope session creates one. A down-migration restores `NOT NULL` and fails loudly if any
`NULL` row exists (the correct posture — silently deleting blueprints is not acceptable).

**DTO / contract changes** (mirrored in `SchoolCollab.Settings.Core.DTOs` and the Admin.Shared client):
`TenantNotificationTemplateDto` + `TenantId`, `OwnerName`; `TemplateAuditEntryDto` + `TemplateId`;
`ApplicableTemplateDto` + `TenantId`, `OwnerName`; new `ConstraintEditRequest`-free surface (constraints keep
riding `TemplateConstraintRequest`). Assignments side: `PublishTemplateCandidateDto` + `IsSystem`, `OwnerName`;
the resolver's `TenantNotificationTemplatePayload` mirror + `tenantId`.

## 5. API surface

All routes live in `NotificationTemplateRoutes.cs` (`/api/settings/notification-templates`), `TypedResults`,
`RequireAuthIfOidc` on the group (unchanged).

| Endpoint | Behaviour |
|---|---|
| `GET /notification-templates` | rows visible in the current scope (+ `TenantId`/`OwnerName`). In **system scope** an optional `includeTenantOwned=true` (platform-admin read) additionally returns tenant-owned rows, read with `IgnoreQueryFilters()` **plus** an explicit `!IsDeleted` predicate — the promote picker's source. Rejected/ignored for a tenant session. |
| `GET /notification-templates/{id:guid}` | **new** — one row by id within the visible scope, `404` otherwise |
| `GET /notification-templates/{kind}/{channel}` | unchanged (default row, 204 = unset) |
| `GET /notification-templates/applicable/{gradeLevelId:guid}` | unchanged shape + owner fields; slots now include WhatsApp |
| `POST /notification-templates` | creates in the **current scope** (tenant ⇒ `TenantId = current`; `Guid.Empty` ⇒ `TenantId = null` under `SuppressTenantGuard()`); `409` on the per-scope default conflict |
| `PUT /notification-templates/{id:guid}` | replaces subject/body/constraints of a writable row; `403` when the row's scope does not match the session (a tenant session may not edit a system row, a system session may not edit a tenant row); `409` per-scope default conflict |
| `DELETE /notification-templates/{id:guid}?reason=` | tombstone in the current scope, audited; same `403` rule |
| `POST /notification-templates/{id:guid}/adopt` | **new** — system → current tenant copy (§3.8) |
| `POST /notification-templates/{id:guid}/promote` | **new** — tenant → system copy, system scope only (§3.8) |
| `GET /notification-templates/{kind}/{channel}/audit?templateId=` | merged stream, optionally narrowed to one row |
## 6. Security and tenant isolation

| Rule | Enforcement | Discriminating test |
|---|---|---|
| A tenant session never reads another tenant's row | the hybrid `Tenant` filter (`TenantId == current OR NULL`) | list + by-id tests under two tenant contexts |
| A tenant session never writes a system row | handlers resolve the scope from `ITenantProvider`; `403` on a scope mismatch (never a silent stamp) | `PUT`/`DELETE` on a system row from a tenant session ⇒ 403, row unchanged |
| A tenant session never writes for another tenant | `TenantId` is always the session's own; no route takes a tenant id | create/update tests |
| Only system scope promotes | `POST …/promote` re-checks `CurrentTenantId == Guid.Empty` | tenant-session promote ⇒ 403 |
| Only a real tenant adopts | `POST …/adopt` requires `CurrentTenantId != Guid.Empty` | system-session adopt ⇒ 403 |
| Cross-tenant read exists in exactly one place | `GET /notification-templates?includeTenantOwned=true`, system scope only | tenant session passing the flag ⇒ flag ignored (own scope only) |
| `Guid.Empty` is never a stored tenant | `Create`/`Update` reject it; the scope resolver maps it to `null` under `SuppressTenantGuard()` | handler unit test |

Tenant isolation is a **human-gated** area per `AGENTS.md`: any change to the table above after this spec is
adopted needs owner sign-off, not agent inference.

## 7. Out of scope (v1)

- A real SMS/WhatsApp provider (the `LogAndSkipSmsSender` stub stays), and a WhatsApp publish stamp/picker slot.
- Live overlay semantics between a system row and a tenant row (adopt is a one-way copy; no propagation).
- An approval workflow for promotions (system scope *is* the gate).
- Seeding/CSV-importing system templates (System starts empty; built-in code defaults remain the fallback).
- An unsaved-changes navigation guard on the template editor.
- Per-recipient template splitting and any change to the publish dialog's two-stamp model.
- Mobile screens for template authoring.

## 8. Verification surface

Stack: MTP Standard (`.github/copilot/rules/testing.md`). bUnit hosts follow the existing
`NotificationTemplatesPageTests` fixture (fake `HttpMessageHandler` + the four clients + `AddFluentUIComponents`).

| Contract | Tests |
|---|---|
| Landing renders one row per template with the kind banner, owner badge/name, mode label and the constraint count (`0` ⇒ default badge; `1` ⇒ singular); filters narrow rows; search matches subject **and** body | `NotificationTemplatesLandingTests` (bUnit) |
| The `N constraints` link opens the shared read-only dialog with dimension + display value; the panel's "View all constraints" opens the same component | `TemplateConstraintsDialogTests` |
| Row actions: Open navigates; Adopt is offered only for system rows in a tenant session; Publish portal-wide only for tenant rows in system scope; Reset is destructive and requires a reason | `NotificationTemplatesLandingTests` |
| Create page: enabled dropdowns, grade prefill, `CreateAsync` then navigation to the new id, Reset absent | `NotificationTemplateCreateTests` |
| Detail: dropdowns disabled with the reason; chips dismiss and are staged (no HTTP until Save); Save sends subject + body + the whole set once with the reason; conflict warning for an identical set in the same kind × mode **and scope**, and no warning across scopes; system row read-only in a tenant session | `NotificationTemplateDetailTests` |
| Editor keeps the existing preview behaviour (rendered output, unknown tag literal, degraded note, save still works) and the SMS/WhatsApp length guidance (warn, never block) | `TemplateEditorPreviewTests` |
| Audit grid is filtered to the row on screen and ordered most recent first | `NotificationTemplateDetailTests` |
| Banner maps each kind to its variant and `data-kind` | `NotificationTemplateKindBannerTests` |
| WhatsApp rows are creatable, listable, previewable and applicable (no throw); grade-card slots include WhatsApp | Settings handler + `ApplicableNotificationTemplatesHandlerTests` |
| Hybrid store: system + tenant rows both visible to a tenant; only system rows in system scope; per-scope default conflict; `Guid.Empty` rejected | `NotificationTemplateHandlerTests`, `NotificationTemplateTenancyTests` |
| Adopt/promote: copy fields + constraints, 409 on an identical copy, source untouched, 403s per §6 | `NotificationTemplateRoutesTests` (API) + handler tests |
| By-id route: 200 in scope, 404 foreign/unknown | `NotificationTemplateRoutesTests` |
| Audit `templateId` filter narrows the stream and the DTO carries the id | `NotificationTemplateRoutesTests` + query handler test |
| Precedence: shadowing, scope tie-break, Q67 default-promotion per scope, stamps still win, built-in fallback intact | `NotificationTemplateEvaluatorTests` (extended) |
| Wire mirrors stay in step (`TenantId`/`OwnerName`/`IsSystem`) | `NotificationTemplateWireEncodingTests` |
| `PanelSection` still renders title + actions + full-width rule in Assignments | existing `AssignmentAuthoringBunitTests` (unchanged assertions) |

**Build gates.** `dotnet build SchoolCollab.slnx` after every change; **plus**
`dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj -f net10.0-windows10.0.19041.0 -m:1 -p:BuildInParallel=false`
whenever `SchoolCollab.Admin.Shared` changes (this round moves a component into it). `dotnet test` with 0
failures before any PR. The CI `Mobile (MAUI)` job stays informational.

## 9. Execution plan

**Mode:** Full four-agent round (Tier 3, **attended**) — one `round-notification-template-authoring-redesign.md`
+ one `diffs-notification-template-authoring-redesign.patch` in `documents/rounds/`. The plan is independently
reviewed before implementation; no-UI steps run lean only where the skill's § "Tier 3 lean" applies — this round
is UI-heavy, so the **UI tester runs**.

| WS | Scope | Exit criteria |
|---|---|---|
| **W1 — landing + create** | `PanelSection` move (Q14); `NotificationTemplateKindBanner`; `Index.razor` (grid, filters, owner column, row actions); `Create.razor` + `TemplateEditor` (Create mode, message column, right rail shell, tag chips); `GET /{id}` + `GetByIdAsync`; `?grade=` filter + prefill | landing/create tests green; build gates green |
| **W2 — detail + constraints + audit + WhatsApp** | `Detail.razor` (Edit mode, read-only meta dropdowns, action bar); constraints kebab + `ConstraintEditDialog` + chips + `TemplateConstraintsDialog`; preview `PanelSection`; per-template audit (`TemplateId` + filter); WhatsApp guard removal + channel sets; conflict warning | detail/dialog/editor tests green; audit + WhatsApp tests green |
| **W3 — ownership** | hybrid entities + configurations + migration; scope-aware handlers and `403`s; `OwnerName` join; adopt/promote commands + routes; evaluator shadow/tie-break + payload fields; publish-options owner fields | tenancy/adopt/promote/precedence tests green; migration reviewed against `ef-migrations.md` |

Order: **W1 → W2 → W3** (each ends with the build gates). Docs in the same PR: this spec's as-built section,
the §3.1/§3.7/§3.10/§5 amendments to `assignment-notification-templates.md`, `documents/configuration.md` only if a
parameter changes (none expected), and a `documents/solution/notification-template-authoring-redesign.md`
implementation record.

## 10. As-built

_Not implemented. Filled at round acceptance with: deviations, test totals, gate results, and the wait-cost
numbers required by `documents/rounds/README.md`._