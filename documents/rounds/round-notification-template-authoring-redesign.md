# Round — notification template authoring redesign

**Status:** OPEN — the close-out pass is accepted (2026-10-21); commit/PR pending the owner's word. The line flips to `CLOSED` + the PR number when the PR exists, per `documents/rounds/README.md`. · Mode: **Full four-agent round, Tier 3 — attended** for W1–W3, then a **Tier 2 light, attended** close-out pass (owner Q53, 2026-10-21) · Provider/model seats: pinned at kickoff per the `orchestrator-worker-reviewer` skill; the close-out pass ran its worker and reviewer passes **inline** (see "Seat deviation" below)
Base: `main` @ `f744bd01` · Branch: `feat/notification-template-authoring-redesign` (created at kickoff)
Spec: `documents/specs/notification-template-authoring-redesign.md` — source of truth; supersedes §3.10 and amends §3.1 / §3.7 / §5 of `documents/specs/assignment-notification-templates.md`

**Owner rulings (2026-10-20):** "all as recommended" — Q1–Q22 in the spec's §2 grill record. Highlights: WhatsApp is an authorable contact mode; landing `EntityGrid` with kind banner + owner column + constraints-count dialog; detail page with **exactly two `PanelSection`s** (Constraints, Preview) in the right rail; per-template audit; hybrid system/tenant store with adopt and publish-portal-wide; `PanelSection` moves to `Admin.Shared`.

**Patch:** `diffs-notification-template-authoring-redesign.patch`, frozen at acceptance from base `f744bd01` — the whole round (W1, W2, W3, the close-out fixes and the docs), generated at commit time so the patch and the commits carry the same content.

## Plan

### Workstreams (ordered; every workstream ends with the build gates)

**W1 — landing + create.**

1. **Move `PanelSection`** `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/PanelSection.razor(.css)` to `src/SchoolCollab.Admin.Shared/Components/` (namespace `SchoolCollab.Admin.Shared.Components`). Markup, class names, ids and the full-width-rule contract unchanged; `AssignmentAuthoring.razor` already imports that namespace. No Settings-local copy.
2. **`NotificationTemplateKindBanner.razor(.css)`** in `Settings.Application/Components/Pages/NotificationTemplates/` — accent strip + icon + label, `data-kind` attribute, four `BadgeColor` variants (Publish `Brand`, Reminder `Warning`, Completion `Success`, Overdue `Danger`).
3. **`NotificationTemplates/Index.razor`** — `LandingPage<TenantNotificationTemplateDto>`: Owner column (`System` badge / `OwnerName`), kind banner, contact-mode label, linked subject/excerpt, `N constraints` link (singular at 1) opening `TemplateConstraintsDialog`, `Tenant default` / `System default` badge at zero, Updated; toolbar type/mode/owner filters + client-side search; `RowActionsMenu` kebab (Open · Adopt · Publish portal-wide · Reset) using `HasKebabActions` + `ForceKebab`; `?grade=` filter via the moved `AppliesToGrade` rule; delete the old tabbed `NotificationTemplates.razor`.
4. **`TemplateEditor.razor(.razor.css)` + `TemplateFormModel.cs` + `TemplateEditorMode.cs`** — the two-column grid (`.template-editor-layout`, `minmax(0,1fr) minmax(320px,460px)`, stacks at 1400px); left "Message" plain section with the two dropdowns, subject, body editor / text area + length guidance, tag chips; right rail hosts the two `PanelSection`s in W2; audit section slot below.
5. **`Create.razor`** — `Mode="Create"`, enabled dropdowns, `?grade=` constraint prefill, `CreateAsync` then navigate to the new id; no Reset.
6. **API + client** — `GET /notification-templates/{id:guid}` (`TypedResults`, 404 outside scope) and `NotificationTemplateApiClient.GetByIdAsync(Guid)`.

**W2 — detail, constraints, preview, audit, WhatsApp.**

1. **`Detail.razor`** — `Mode="Edit"`; dropdowns disabled with the "fixed per template" reason; Owner chip; action bar (Reason / Save / Reset with confirm + reason); system row read-only in a tenant session with Adopt as the primary action; not-found state + Back.
2. **Constraints `PanelSection`** — `RowActionsMenu ForceKebab UseMenuService="false"`, one item per dimension (`FluentIcons.Add`); `ConstraintEditDialog` (`DialogShellBase`, dimension preset/locked, multi-pick, chips, dedupe on merge); dismissible chips staged in the draft; conflict warning scoped to kind × mode × **scope**; 409 surfaces in the error bar; empty state naming the default.
3. **`TemplateConstraintsDialog`** — read-only (`ReadOnlyDialogShell`), shared by the panel's "View all constraints" and the grid's count link.
4. **Preview `PanelSection`** — the existing `NotificationTemplatePreviewClient` path, Refresh, degraded note, `RichInstruction` for email, plain text otherwise; SMS/WhatsApp length guidance preserved (warn, never block).
5. **Per-template audit** — `TemplateAuditEntryDto.TemplateId`, optional `templateId` filter on the query + route, Detail grid filtered and ordered most recent first.
6. **WhatsApp** — remove the `Create` guard; add WhatsApp to the editor's channel set, the landing filters, and `GetApplicableNotificationTemplatesHandler.Channels`; subject stays null for non-email; no Assignments change.

**W3 — ownership.**

1. **Hybrid store** — `TenantNotificationTemplate` + `TemplateConstraint` implement `IHybridTenantEntity`; configurations move to `TenantOrGlobalEntityTypeConfigurationBase`; migration `MakeNotificationTemplateTenantNullable` (both tables, non-destructive up; down fails loudly if a NULL row exists).
2. **Scope-aware handlers** — `Create`/`Update` stamp the resolved scope (`Guid.Empty` ⇒ `null` under `SuppressTenantGuard()`, `Guid.Empty` rejected as a stored value); `403` on a scope mismatch for writes; `TenantNotificationTemplateDefaults` becomes per-scope.
3. **Owner projection** — `TenantId` + `OwnerName` on `TenantNotificationTemplateDto` and `ApplicableTemplateDto`; settings-db `Tenant.Name` join.
4. **Adopt / promote** — `POST …/{id}/adopt` (real tenant, system source, 409 on identical set) and `POST …/{id}/promote` (system scope only, cross-tenant read by id, 409 on identical set); both audited as `Created`, neither mutates the source.
5. **Precedence** — resolver payload carries `tenantId`; evaluator shadows identical system candidates, orders `specificity desc → tenant-before-system → CreatedAt → id`, keeps Q67 default-promotion **within a scope**; `PublishTemplateCandidateDto` + `IsSystem`/`OwnerName`.
6. **System-scope list read** — `includeTenantOwned=true` on `GET /notification-templates` (system scope only, `IgnoreQueryFilters()` + explicit `!IsDeleted`), so the promote picker has a source; ignored in a tenant session.

### Acceptance criteria

1. A tenant session never reads or writes another tenant's row; every `403` path in the spec §6 table is test-proven.
2. The landing shows both scopes with the correct owner, and the constraint-count link opens the same dialog as the panel.
3. Detail renders exactly two `PanelSection`s (Constraints above Preview, right rail) and the audit grid below is per-template.
4. Adopt/promote copy fields and constraints, never mutate the source, and return the new row id for navigation.
5. A WhatsApp template row round-trips create → list → preview → applicability and is selected for WhatsApp sends.
6. `dotnet build SchoolCollab.slnx` green; the **mobile head** build green (Admin.Shared changed); `dotnet test` 0 failures.
7. Docs: spec §10 as-built filled; a `documents/solution/notification-template-authoring-redesign.md` implementation record added.

### Gates and constraints

- Plan review before implementation (skill step 2b); UI tester **runs** (UI-heavy round).
- `dotnet build` after each code change; locks surfaced, never retried in a loop.
- Migration reviewed against `.github/copilot/rules/ef-migrations.md` before acceptance.
- No commit/push/PR/merge without an explicit owner instruction.

### Recorded decisions and risks

- **Promote is the one cross-tenant read** in this feature and is gated by system scope (`Guid.Empty`), not by a tenant claim; the tenant-facing list ignores `includeTenantOwned`. Recorded as a human-gated security decision (spec §6).
- **Adopt/promote are copies**, so a later system edit does not reach an adopted tenant row; re-adopt hits a 409 rather than duplicating.
- **Denormalised risk accepted:** `NotificationTemplateKindBanner` lives in Settings, not Admin.Shared, because only Settings renders it.
- The tabbed page's tests are **rewritten, not deleted-and-forgotten**: the same behaviours (preview degradation, length guidance, chips, conflict warning) must reappear in the new suites.

### Close-out pass — Tier 2 light, attended (owner Q53–Q61, 2026-10-21)

**Mode:** Tier 2 — parent plans and accepts, one worker pass + one static diff-only reviewer pass.
**Seat deviation (recorded):** `spawn_agent` aborts in this CLI (handoff, 2026-10-21), so both passes
ran **inline** on the parent's model. The separation the skill exists for is preserved —
implementation first, diff frozen, then a review pass whose findings were written before any of them
was fixed — but no dispatched seat is claimed.

Acceptance criteria (each checked against the diff at `## Acceptance`):

| # | Criterion |
|---|---|
| C1 | The conflict warning compares **within the draft's scope**: Edit uses the row's scope, Create the **session's** (a create draft has no `TenantId`). A cross-scope identical set never warns; a same-scope one still does. |
| C2 | `RunAsync`'s success callback is `Func<T, Task>` and is awaited — no `async void` path remains in `Index.razor`. |
| C3 | `TemplateEditorPreviewTests` exists and pins the rendered subject/body, an unknown-tag literal, and the degraded note with Save still working. |
| C4 | The mirror read-only case is pinned: a tenant row in a system session offers no Save/Reset and puts `Publish portal-wide` first. |
| C5 | Zero `''` in the changed files (encoding preserved); no fabricated `OwnerName = "System"` / `TenantId = null` default remains in either DTO or either client mirror. |
| C6 | Spec §3.2 (Reset), §6 (two cross-tenant reads, with the owner sign-off clause), §8 (suite names, landing-row claim) match the code. |
| C7 | A `?grade=` arrival renders a named info bar whose button widens the view again. |
| C8 | The landing's owner assertion is exact, and the grade deep link has its own test. |
| C9 | The round doc carries `## Not run` with the owner's five click paths; `## UI Tester` says handed to owner. |
| C10 | `dotnet build SchoolCollab.slnx` and the mobile head: 0 errors; the affected suites green. |
| C11 | One tracking issue for the structural smells (Q56); #396 (the flake) stays separate. |

**Out of scope (Q43/Q56, ticketed):** the owner value object, the duplicated adopt/promote handler
bodies, splitting the 958-line `TemplateEditor`, the fixture's tenant default, and the review's
"Minor" note that adopt/promote enforce the per-scope default through the identical-set check.

## Worker Report

**W1 — implemented (2026-10-20).**

- **`PanelSection` relocated** to `SchoolCollab.Admin.Shared/Components/` (markup + css, class names and the
  full-width rule untouched). One markup consumer (`AssignmentAuthoring.razor`) already imported that
  namespace; `AssignmentAuthoringBunitTests` passes unchanged (**116/116**).
- **`NotificationTemplateKindBanner`** — four `BadgeColor` variants (Publish `Brand`, Reminder `Warning`,
  Completion `Success`, Overdue `Danger`), `data-kind` seam, no literal colours.
- **Landing** (`Index.razor`, route `/notification-templates`) — `LandingPage`/`EntityGrid` with type banner,
  contact-mode label, linked subject/excerpt, constraint count (link → the one read-only dialog; badge at
  zero) and Updated; type + mode filters; type/mode/owner: owner deferred (below); row action Open.
  Filtering extracted to **`TemplateLandingFilter`** (pure seam) after the toolbar proved unreachable in the
  bUnit host (SectionContent renders only against a live outlet; a routed page inside a test host rendered an
  empty tree). The rule is asserted directly instead of through a dropdown that only forwards to it.
- **`TemplateConstraintsDialog`** — the shared read-only list, opened from the count link, values resolved by
  the new **`TemplateConstraintLookups`** (one vocabulary shared with the editor).
- **`TemplateEditor`** + `TemplateFormModel` + `TemplateEditorMode` — the two-column layout
  (`minmax(0,1fr) minmax(320px,460px)`, stacks at 1400px), left "Message" section with the two dropdowns,
  subject, rich/text body, tag chips and length guidance; right rail with `PanelSection` **Constraints**
  (dismissible staged chips) above `PanelSection` **Preview** (real render path, degraded note). Save posts
  the whole draft once; SMS/WhatsApp bodies are never sanitized (the old page's rule, kept).
- **`Create.razor`** — `?grade=` prefill, enabled dropdowns, create-then-navigate to the new id.
- **API** — `GET /api/settings/notification-templates/{id:guid}` (`TypedResults`, 404) + `GetByIdAsync`.
- **Deleted** the tabbed page and its 9-test suite (behaviours re-homed: preview/length/chips/conflict → W2).

**Gates:** Settings.Application / Settings.Api / Admin host / Admin test project / Assignments.Application /
**mobile head** builds — all 0 errors. W1 suites **16/16**; `Settings.Api.Tests.Unit` **48/48**
(2 new by-id tests); `AssignmentAuthoringBunitTests` **116/116**. **Pending:** the full solution build and the
full `Admin.Tests.Unit` run both exceed the 30 s command cap and are deferred to the W4 acceptance gate.

**Deviations (recorded):** (1) the **Owner column and owner filter** are deferred to W3 — the DTO cannot tell
System from tenant until the hybrid store lands, and a column that says "tenant" for every row would lie;
(2) Adopt / Publish-portal-wide / Reset row actions are deferred to W3/W2 for the same reason plus the reason
prompt; (3) the **Preview panel shipped in W1** rather than W2 (it is part of the editor shell and the old
suite's preview assertions had to land somewhere); (4) a system-scope session's create path is not yet
special-cased — W3 owns scope.

**W2 — implemented (2026-10-20).**

- **Per-template audit** — `TemplateAuditEntryDto.TemplateId`, optional `templateId` on the query/route, `GetAuditAsync(kind, channel, templateId)`; the detail page's grid is narrowed to its own row (the reset's trail survives because the audit row keeps the id).
- **WhatsApp is authorable** — the `Create` guard is gone, the applicability handler serves 12 kind × channel slots, the subject stays email-only; tests flipped (`Create_AcceptsWhatsAppChannel_AndDropsItsSubject`, slots 8 → 12).
- **Constraints panel** — header `RowActionsMenu` kebab with one item per dimension, `ConstraintEditDialog` (dimension preset, multi-pick, chips, existing-value exclusion), staged merge with dedupe, scope-aware conflict warning, "View all constraints" reusing the shared read-only dialog.
- **Detail surface** — locked type/mode indicators with their reason, audited Reset (reason required, confirmed), audit section below the two-column layout, `Detail.razor` route host.
- **OpenAPI pin** raised **52 → 53 of 69** (measured, per `minimal-api-composition`): the new by-id route documents a schema.

**Defect the new tests caught (fixed before commit):** nine Razor attributes had been written as **literals** (`Value="_model.Body"`, `SelectedValue="_kindFilter"`, …) because the editor was authored through a line-based shell edit — the editor never bound its fields and the landing filters never applied. All nine fixed and re-asserted. This is the concrete cost of the class of edit this round used; the lesson is recorded for the front-door research doc.

**Seam corrections (recorded, per the `tdd` rule that seams are agreed first):**

1. the landing toolbar lives in a `SectionContent` and is unreachable in bUnit → filtering was extracted to the pure `TemplateLandingFilter` and asserted there (the page tests keep rows/actions);
2. a routed page cannot be rendered with a path parameter in a bare bUnit tree (no `Router`) → the detail tests render the **shared editor** directly, which is the component under test anyway;
3. a dialog rendered bare throws inside `FluentDialogInstance` → the dialog is covered by its **model** tests, and its invocation by the deferred browser pass.

**Gates:** Admin suites **25/25** (landing, create, detail, dialog-list, banner, filter, constraint model) · `Settings.Api.Tests.Unit` **49/49** · `ArchitectureTests` **142/142** · `Settings.Tests.Unit` **635/636** (the one failure is the pre-existing `CodedValueDropdown` order-dependent flake, filed as an issue) · builds 0 errors: Settings.Core/Api/Application, Admin.Shared, Admin host, **mobile head**.

**Deferred:** the browser pass (Q30 — the AppHost cannot serve here, the b3b Q91a precedent) and dialog *invocation* coverage.

**Queued last (owner order, 2026-10-20):** write `documents/solution/spec-and-ticket-skills-in-the-orchestrator.md` — the `/to-spec` · `/to-tickets` · `/implement-spec` front-door research and the proposed orchestrator section — **after** W3 and the W4 acceptance.

**W3 — implemented (2026-10-21).**

- **Hybrid store** — both entities implement `IHybridTenantEntity` (nullable `tenant_id`, the `CodedValue` shape), configurations moved to `TenantOrGlobalEntityTypeConfigurationBase`, migration `MakeNotificationTemplateTenantNullable` with a loud `Down()` guard. `Guid.Empty` is rejected at the factory, so the sentinel can never reach a row.
- **Scope-aware writes** — `NotificationTemplateOwnership` resolves the session tenant to a stored scope; Update/Delete throw `TenantNotificationTemplateScopeException` (403) on a scope mismatch; the default invariant is per scope.
- **Owner projection** — `TenantId` + `OwnerName` on both DTOs, joined from the settings-db `Tenants` table.
- **Adopt / promote** — two audited copy commands with 403/404/409 arms, a source that is never mutated, and an identical-set 409 (normalised, order- and case-insensitive).
- **One cross-tenant read** — `includeTenantOwned=true` in system scope (`IgnoreQueryFilters(["Tenant"])`), with `SuppressTenantGuard()` on the promote path; a tenant session's flag is ignored.
- **Send-time precedence** — shadowing, scope rank, Q67 promotion per scope; publish-picker owner marking.
- **UI** — owner column + filter, scope-aware default badge, scope-gated row actions (`Adopt`, `Publish portal-wide...`, `Reset...` through a new audited `TemplateResetReasonDialog`), and a **read-only** detail surface for a row outside the session's write scope with the copy action as its primary button.

**Defect the route tests caught before commit:** `[FromQuery] bool includeTenantOwned` made the parameter *required* in minimal APIs, so every plain list read answered 400. Bound as `bool?` (missing ⇒ false) instead; the pre-existing `GetAll_Returns200AndTheRows` test is what failed.

**Deviations (recorded):** the reset kebab item is not flagged `Destructive` (the reason dialog is the destructive confirmation — otherwise the operator confirms twice); the system session's landing *is* the promote picker, so §3.6's cross-tenant read has no separate dialog in v1.

**Gates (measured 2026-10-21):** Admin **730/730** · `Settings.Tests.Unit` **655/655** · `Settings.Api.Tests.Unit` **58/58** · `ArchitectureTests` **142/142** (OpenAPI pin raised 53 → **55 of 71**, measured) · `Assignments.Api.Tests.Unit` template/resolver slice **13/13** · `Assignments.Tests.Unit` **1385/1385** on re-run (one order-dependent flake, `Edit_RemovingAnActivityGroupEntry_SendsTheReducedTargetSetAndLinks`, in the first parallel-order run; nothing on the template surface).

### Close-out pass — worker report (2026-10-21, inline seat)

| Criterion | Change |
|---|---|
| C1 | `TemplateEditor.razor` — `ConflictWarning` filters siblings to the draft's scope (`draftIsSystem`); the doc comment records why the read stays wide |
| C2 | `Index.razor` — `RunAsync<T>` takes `Func<T, Task>` and awaits it; adopt/promote navigate through `NavigateToRowAsync` |
| C3 | `TemplateEditorPreviewTests.cs` (new) — rendered pair + unknown-tag literal; degraded note + Save still works |
| C4 | `NotificationTemplateDetailTests` — mirror read-only, cross-scope conflict, create-draft scope |
| C5 | 33 `''` → `'` in 9 files (encoding-preserving write); defaults removed from `TenantNotificationTemplateDto`, `ApplicableNotificationTemplatesDto` and both `NotificationTemplateApiClient` mirrors; 7 construction sites now pass the pair explicitly |
| C6 | Spec §3.2 / §6 / §8 amended |
| C7 | `Index.razor` — grade info bar + `ClearGradeFilter`; landing test covers filter + widening |
| C8 | `NotificationTemplatesLandingTests` owner assertion is exact |
| C9 | This doc: `## Not run`, `## UI Tester` |
| C10 | Gates measured by the parent, below |
| C11 | Issue **#397** |

**Compile fallout of C5 (expected, compiler-guided):** four test files depended on the removed defaults
(`TemplateLandingFilterTests`, `NotificationTemplateRoutesTests`,
`NotificationTemplateApplicabilityRouteTests`, `HttpNotificationTemplateResolverTests`); each now states
the row's scope at the construction site instead of inheriting "System". Named arguments are capitalised
(`TenantId:`, `OwnerName:`) — a lowercase first attempt failed to compile.

**Defect caught by the review pass, fixed before freeze:** R1 (the create-mode scope inversion).

## Review

### 4b — static diff review of W1–W3 (Tier 3, 2026-10-21)

Transcribed verbatim from the review pass (sub-agents unavailable in this CLI, so the two axes ran as
two separate passes). Character-level mojibake from the original transport is normalised; nothing else
is changed.

> **Fixed point:** `main` @ `f744bd01` (merge-base). **Commits:** `2e92ca6c` docs · `a3140736` W1 ·
> `16a6d18f` W2, plus the uncommitted W3 worktree. **Spec:**
> `documents/specs/notification-template-authoring-redesign.md` (+ the amendments to
> `assignment-notification-templates.md`). **Standards:** `AGENTS.md`,
> `.github/copilot/rules/{dotnet-best-practices,testing,ef-migrations,blazor-components,shared-constants}.md`,
> the `minimal-api-composition` / `dialog-ui` / `dropdown-ui` / `fluentui-*` skills, plus the Fowler
> smell baseline.
>
> #### Standards
>
> **HARD**
>
> 1. **`async void` in the landing's error wrapper.** `Index.razor:376` passes
>    `async _ => await ReloadAsync()` into `RunAsync<T>(…, Action<T> onSuccess, …)` (`:381`). An async
>    lambda converted to `Action<T>` is `async void` — the reload's exception is unobservable and the
>    reload isn't awaited before `ResetAsync` returns. Rule: `dotnet-best-practices.md` → Never:
>    "❌ `async void` outside top-level event handlers". Fix: `Func<T, Task> onSuccess`,
>    `await onSuccess(result)`.
> 2. **No live UI pass** (`testing.md` § "UI rounds need one live pass"): this round touches
>    `.razor`/`.razor.css` throughout and ships bUnit only. Declared deferred in the round doc, so it's
>    an owned process breach, not a surprise — but the rule isn't satisfied.
>
> **JUDGEMENT**
>
> 3. **Doubled apostrophes in prose — 26 occurrences across 9 changed files** (`Index.razor` 8,
>    `TemplateLandingFilter.cs` 6, `TemplateEditor.razor` 6, landing tests 4,
>    `TemplateResetReasonDialog.razor` 3, `TemplateFormModel.cs` 2, `NotificationTemplateApiClient.cs` 2,
>    filter tests 1, editor `.css` 1). All inside comments/doc-comments (no user-visible string), but the
>    diff literally reads `the repo''s landing shape`. Mechanical cleanup. *(Close-out note: the count is
>    actually **33** in those 9 files — the per-file list above sums to 33, the headline's 26 did not. The
>    sweep removed 33.)*
> 4. **Cross-tenant read without `SuppressTenantGuard()`** —
>    `GetTenantNotificationTemplatesHandler.cs:40` uses `IgnoreQueryFilters(["Tenant"])` alone, against
>    the Never-list line. The closest precedent (`ListProvisionalCodedValuesHandler.cs:21`) does exactly
>    the same, with the guard used only around writes — so rule text and precedent disagree; one should be
>    amended.
> 5. **Primitive Obsession / Duplicated Code (owner label).** A bare `string` is fabricated in three
>    places: `NotificationTemplateOwnership.SystemOwnerName`, `"Unknown tenant"`, and the DTO default
>    `OwnerName = "System"`. That default silently makes a *tenant* row claim System if a construction
>    site forgets the argument.
> 6. **Middle Man / two encodings of one fact** — `NotificationTemplateOwnership.OwnerId` (`:20`) and
>    `IsSystemScope` (`:23`); `Create` uses the first, adopt/promote the second. They can only drift.
> 7. **Duplicated Code** — the two copy handlers are ~40 near-identical lines each
>    (`Adopt…Handler.cs`, `Promote…Handler.cs`): load → checks → identity → copy → audit → save.
> 8. **Divergent Change** — `TemplateEditor.razor` is now 991 lines serving authoring, preview, audit,
>    constraints, scope read-only and copy actions. *(Measured at close-out: 958 lines.)*
> 9. **Weak assertion** — `NotificationTemplatesLandingTests` asserts `Contain("System")`, which
>    "System default" satisfies on its own.
> 10. **Fixture default** — `TemplateRow.TenantId = RealTenant` makes *system* rows the odd case in
>     tests; documented, but an easy way to stop exercising them.
>
> **Verified clean:** the new migration's `Down()` guard is legitimate (new, unapplied file;
> `.Designer.cs` untouched; `Down()` implemented); `TypedResults` + `ProblemHttpResult` matches
> `minimal-api-composition`; `PanelSection` count in the editor is exactly 2.
>
> **Standards: 10 findings (worst: `async void` via `Action<T>` in `Index.razor`).**
>
> #### Spec
>
> **(c) Implemented but wrong**
>
> 1. **§3.6 "conflict warning scoped to kind × mode × *scope*"** — `TemplateEditor.razor:796–817`
>    compares every sibling regardless of scope, and `LoadSiblingsAsync` (`:832`) reads the **unscoped**
>    list. Since W3 that list includes blueprint rows, so a tenant draft identical to a system row now
>    warns "publish-time selection cannot tell them apart" — false after §3.9 shadowing. The code's own
>    comment concedes it: *"Scope joins the comparison with W3."* The strongest finding in the review:
>    W3 widened the read and left this comparison behind.
> 2. **§6 "Cross-tenant read exists in exactly one place"** — there are two:
>    `GetTenantNotificationTemplatesHandler.cs:40` and `PromoteTenantNotificationTemplateHandler`
>    (by-id, `IgnoreQueryFilters` + guard). The security table names only the flag read; either the table
>    or the handler is wrong.
>
> **(a) Missing / partial**
>
> 3. **§3.2 "A `?grade=` arrival renders an info bar naming the filter"** — no info bar exists;
>    `Index.razor:153–187` only sets `_gradeFilter` and forwards it to Create.
> 4. **§8 `TemplateEditorPreviewTests` does not exist** and no preview assertion lives in the detail or
>    create suites (0 matches), yet §8 requires rendered output, unknown-tag literal, degraded note and
>    save-still-works. §8 also names `NotificationTemplateTenancyTests`; it shipped as
>    `NotificationTemplateOwnershipTests` (naming only).
> 5. **§3.2 "Reset … destructive"** — the kebab item is deliberately not `Destructive` (declared in §10)
>    because the reason dialog is the confirmation. The spec text still says destructive, so spec ≠ code
>    by omission of an amendment.
> 6. **§8 landing row** claims the suite pins "Reset is destructive and requires a reason"; the landing
>    tests only assert the item is offered — the reason-required behaviour is pinned in the detail suite
>    instead.
>
> **Minor:** adopt/promote enforce the per-scope default through the identical-set check rather than
> `EnsureNoCompetingDefaultAsync`; equivalent for unconstrained sources but implicit.
>
> **Spec: 6 findings (worst: the conflict warning compares across scopes after W3 widened the read).**
>
> No files were changed by this review. The two to fix before any PR: `RunAsync`'s `Action<T>` →
> `Func<T, Task>`, and the scope filter in `ConflictWarning`
> (`_siblings.Where(r => (r.TenantId is null) == _model.IsSystem)`), plus the `''` cleanup.

### 4b — static diff review of the close-out pass (Tier 2, 2026-10-21)

Same fixed point; the diff was frozen after the worker pass, then read without building or testing.

**Reviewer verdict: `P1` → one P1 found and fixed (rework iteration 1 of ≤1) → `PASS` on re-read.**

| # | Finding | Axis | Disposition |
|---|---|---|---|
| R1 | The first cut of the scope filter compared `row.IsSystem != _model.IsSystem`. Correct on Edit, **inverted on Create**: a create draft carries no `TenantId`, so `IsSystem` is `true` by construction — a tenant author would warn on blueprint twins and stay silent on its own tenant twins, the bug Q39 targets, mirrored. | Spec / behaviour | **Fixed** to `Mode == Create ? _sessionIsSystem : _model.IsSystem`, with `TheCreateDraft_ComparesWithinTheSessionsScope` pinning both directions. Re-read clean. |
| R2 | `ClearGradeFilter` drops the filter but leaves `?grade=` in the URL, so a reload re-applies it. | Spec §3.2 | Accepted by design (documented on the method): the button's promise is "show all now", not "rewrite the deep link". |
| R3 | The info bar renders above `LandingPage`'s title row, not inside its grid area — `LandingPage`'s only content slot renders **below** the grid, so above-the-title is the honest placement without widening the shared component. | UI | Accepted; recorded so the owner's live pass knows it is deliberate. |
| R4 | `LoadSiblingsAsync` still reads the unscoped list and filters locally. | Design | Accepted: one read serves both scopes, and narrowing it would add a round trip for a warning. |
| R5 | `TemplateEditorPreviewTests` covers the shared editor in Edit mode only, not the Create host. | Coverage | Accepted: `QueuePreview` is mode-independent and Create's own suite pins the draft→POST path. |

**Standards axis (close-out diff):** no `async void` remains (`RunAsync` awaits a `Func<T, Task>`); no
`''` remains in the changed files; no fabricated owner default remains; every new test names the
behaviour and the spec clause. **Verdict: PASS.**

## Acceptance

**Verdict: ACCEPTED (CLOSED)** — Tier 2 light, plan authored and accepted by the parent (plan author),
2026-10-21. The reviewer's findings were adjudicated first (R1 fixed and re-read), then every criterion
was checked against the diff and the parent's authoritative numbers — not against the worker's prose.

| # | Criterion | Evidence | Result |
|---|---|---|---|
| C1 | Conflict warning scoped to the draft's scope | `TemplateEditor.razor` `ConflictWarning` (`draftIsSystem` = session on Create, row on Edit); `TheConflictWarning_ComparesOnlyWithinTheDraftsScope`, `TheCreateDraft_ComparesWithinTheSessionsScope`, and the existing same-scope test | met |
| C2 | `Func<T, Task>` awaited; no `async void` | `Index.razor` `RunAsync<T>` + `NavigateToRowAsync`; no `Action<T>` success callback remains | met |
| C3 | Preview suite exists | `TemplateEditorPreviewTests` — rendered pair + unknown-tag literal; degraded note + Save still works | met |
| C4 | Mirror read-only case | `ATenantRowInASystemSession_IsReadOnly_AndOffersPublishPortalWideInstead` | met |
| C5 | No `''` in code; no fabricated defaults | sweep 33 → 0 across the 9 files; 4 literal `''` remain only in this doc, where the defect string is quoted. Mojibake probe (`�`, `â€`, `Â§`) = 0 in every edited file; no BOM flip. Defaults removed from both DTOs and both client mirrors; all 7 construction sites now pass the pair explicitly | met |
| C6 | Spec matches code | §3.2 Reset row; §6 two-reads row + the owner sign-off sentence; §8 landing-row claim and `…OwnershipTests` name | met |
| C7 | Grade info bar | `Index.razor` bar + `ClearGradeFilter`; `AGradeDeepLink_NamesTheFilter_AndTheBarWidensTheViewAgain` | met |
| C8 | Exact owner assertion | badge list exact-text `System` + `.template-owner` equality `Northfield` | met |
| C9 | `## Not run` + handover | below | met |
| C10 | Builds + suites (parent-measured) | `dotnet build SchoolCollab.slnx` **0 errors**; mobile head **0 errors**; Admin **736/736** (730 → +6); Settings **655/655**; Settings.Api **58/58**; Architecture **142/142**; Assignments.Api **202/202** | met |
| C11 | Follow-up issue | **#397** (structural smells, checklist); **#396** (flake) untouched | met |

**Residuals (accepted, none blocking):** R2 (the deep link keeps `?grade=` after the bar's button), R3
(the bar's placement above the title), R4 (the unscoped sibling read), R5 (preview covered in Edit
mode); the live UI pass is handed over per Q45/Q61; the browser pass cannot run in this environment.

## Not run

| Step | Reason | Owner |
|---|---|---|
| Live UI pass over landing → adopt → read-only detail → publish portal-wide → reset-with-reason | The AppHost cannot serve in this environment (recorded in the W1/W2/W3 rounds too) | Owner — the five click paths are in `## UI Tester` |
| `dotnet test` over the whole solution | Only the four affected suites were run plus the solution build; the CI `Build & Test` job runs the full matrix on the PR | Owner/CI at PR time |

## UI Tester

_Handed to the owner (Q45): no dispatched tester seat in this environment._ Suggested path:

1. `/notification-templates?grade=<any grade id>` → the info bar names the grade filter; "Show all
   templates" widens the list again; the create link carries the grade.
2. In a **tenant** session, open a `System` row → read-only, no Save/Reset, primary action
   **Adopt to this tenant** → lands on the new tenant row.
3. In a **system** session, open a tenant row → read-only, primary action **Publish portal-wide**.
4. Either row → **Reset to built-in default…** → the reason dialog refuses an empty reason, then the
   row disappears and the audit trail records the reason.
5. Create a tenant row whose constraint set matches a **tenant** sibling → the conflict warning shows;
   make it match a **blueprint** sibling instead → no warning.

### Wait-cost (read at close)

| Wait calls | Commands over 90 s | Per-seat tokens |
|---|---|---|
| 0 | 0 over 90 s — five test runs and three builds were detached (`Start-Process`) and polled; the longest finished in ~46 s | not measurable inline (no dispatched seats; the parent ran both passes) |