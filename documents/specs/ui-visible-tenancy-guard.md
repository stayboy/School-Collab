# Spec: Disable strict-tenant tools, buttons, and forms without visible tenancy

Status: Draft · Branch: `feature/ui-visible-tenancy-guard`
Related: [`global-tenant-filter.md`](./global-tenant-filter.md) (FR-1, FR-4, FR-19), [`grade-level-setup.md`](./grade-level-setup.md)

## 1. Problem

After the global-tenant-filter rollout, the Students Admin Blazor UI throws for
**search and CRUD** on the strict-tenant entities (`GradeLevel`, `Subject`,
`Period`, `Student`) whenever the signed-in user has **no visible tenancy**.

- **Search / list** pages throw during `OnInitializedAsync`:
  `ModuleDbContext.CurrentTenantId`
  (`src/SchoolCollab.Core/Data/ModuleDbContext.cs:25`) calls
  `_tenantProvider.GetTenantContext().TenantId`, which throws
  `SchoolCollab.Core.Tenancy.TenantContextRequiredException` when no tenant
  context is set. Every tenant-scoped query
  (`ListStudents`, `ListStudentsByGrade`, `ListGradeLevels`,
  `ListGradeLevelsForLanding`, `ListSubjects`, `ListPeriods`) hits this.
- **Create / CRUD** handlers throw the same exception via
  `tenantProvider.RequireTenantContext(...)` (spec FR-4): `CreateStudent`,
  `CreateGradeLevel`/`GetOrCreateGradeLevel`, `CreateSubject`/`GetOrCreateSubject`,
  `CreatePeriod`, plus update/delete on the same entities.

The user sees a red `ErrorBoundary` / `FluentMessageBar` "Something went wrong:
Tenant context is required…" instead of a usable page.

### 1.1 "Visible tenancy" definition

A user has **visible tenancy** iff their `tenant_id` claim parses to a non-empty
`Guid`:

```csharp
_isRealTenant = Guid.TryParse(tenantIdClaim, out var id) && id != Guid.Empty;
```

This logic already exists in exactly one place —
`Components/Pages/Students/GradeLevels/GradeLevelWizard.razor:471` — where it
gates the per-tenant "override" actions. `Guid.Empty` is the **system/default
tenant**; strict entities cannot be created or queried against it (FR-4), so a
system-tenant user has no visible tenancy for these tools.

## 2. Root cause (single)

The 11 affected pages call `StudentsApiClient`/`CodedValuesApiClient`
unconditionally in `OnInitializedAsync` and render create/edit forms with no
tenant guard. Only `GradeLevelWizard` checks the claim. The tenant comes from
the authenticated `tenant_id` claim (read via `AuthenticationStateProvider`); the
API derives `CurrentTenantId` from the same claim, so when the claim is absent
the server has no tenant context and throws.

## 3. Affected surfaces (the "tools, buttons, and forms")

All under `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/`:

| Page | Path | Surface | Throws on |
|------|------|---------|-----------|
| Students list | `Index.razor` | `LandingPage` (search + "+ New Student") | list load |
| Student create | `Create.razor` | `EditForm` submit | create + gender coded-value load |
| Student edit | `Edit.razor` | `EditForm` submit | load + update |
| Student detail | `Detail.razor` | view | load |
| Grade levels list | `GradeLevels/GradeLevels.razor` | `LandingPage` (search + "+ New Grade Level") | list load |
| Grade level wizard | `GradeLevels/GradeLevelWizard.razor` | wizard | create (already partly gated by `IsRealTenant`) |
| Grade level edit | `GradeLevels/Edit.razor` | `EditForm` | load + update |
| Subjects list | `Subjects/Subjects.razor` | `LandingPage` (search + "+ New Subject") | list load |
| Subject create | `Subjects/Create.razor` | `EditForm` | create |
| Subject edit | `Subjects/Edit.razor` | `EditForm` | load + update |
| Periods | `Periods.razor` | own "+ New Period" button + inline create form + grid | list load + create |

Out of scope (do **not** throw — global/hybrid): `CodedValue` browsing, `Tenant`
registry management (`TenantsApiClient`), feature-flag pages, outbox.

## 4. Design

### 4.1 Shared `VisibleTenantService` (new) — the rule + one-shot read

Centralize the FR-4 "real tenant" rule and the AsyncLocal-safe claim read in one
unit-testable place. The service does **only** the one-shot read; it does **not**
subscribe to `AuthenticationStateChanged` (only the wizard needs live updates — see
§4.5). One-shot is enough for the 10 list/form pages, which render once per load.

- **Location:** `src/SchoolCollab.Admin.Shared/Services/VisibleTenantService.cs`
  (project `SchoolCollab.Admin.Shared`, already referenced via `_Imports.razor`
  `@using SchoolCollab.Admin.Shared.Services`).
- **DI:** Scoped lifetime (depends on `AuthenticationStateProvider`).
- **Surface:**

  ```csharp
  public sealed record TenantScope(bool IsRealTenant, Guid? TenantId, string? TenantName);

  public sealed class VisibleTenantService(
      AuthenticationStateProvider authStateProvider,
      ILogger<VisibleTenantService> logger)
  {
      // One-shot read: parse the tenant_id claim. IsRealTenant = non-empty Guid.
      // Reads claims only — MUST NOT touch the server-side ITenantProvider (AsyncLocal,
      // not reliably available in a Blazor Server circuit; see GradeLevelWizard comment).
      public async Task<TenantScope> GetScopeAsync(CancellationToken ct = default);
  }
  ```

- **No `ITenantProvider` dependency** — pure UI/claim read. It only mirrors the
  existing `GradeLevelWizard` claim check.
- Register in `SchoolCollab.Admin.Shared` DI extensions (alongside the
  `*ApiClient`s) so every Admin app gets it automatically.

**Minimal alternative (acceptable, not both):** if the team prefers zero new DI
types, a static extension `ClaimsPrincipal.IsRealTenant()`
(`Guid.TryParse(tenant_id) && != Guid.Empty`) centralizes the *rule* and is
unit-testable; each page then does its own one-line
`GetAuthenticationStateAsync()` fetch. This is fine for the 10 list/form pages.
Choose **one** approach for the whole codebase — do not mix a service and an
extension.

### 4.2 `LandingPage`-based list pages (Students, GradeLevels, Subjects) — no component change

`LandingPage.razor` already exposes `CreateEnabled`, `SearchEnabled`,
`EmptyMessage`, `Loading`, `Items`, `Error`. Per page:

1. Inject `VisibleTenantService` and cache `_isRealTenant` in
   `OnInitializedAsync` **before** any API call.
2. Gate the load: `if (!_isRealTenant) { _items = []; return; }` — never call the
   list API when there is no visible tenancy, so no throw.
3. Bind the levers:
   - `CreateEnabled="@_isRealTenant"` (hides "+ New …")
   - `SearchEnabled="@_isRealTenant"` (hides the search box)
   - `EmptyMessage` → a tenant-specific message when `!_isRealTenant`:
     `"Select a tenant to manage students."` / `"...grade levels."` /
     `"...subjects."`
4. `Subjects.razor` already sets `CreateEnabled` for grade-level gating — compose
   both: `CreateEnabled="@(_isRealTenant && _selectedGradeLevel is not null)"`.

No edits to `LandingPage.razor` are required.

### 4.3 Form pages (Create / Edit / Detail) — render a disabled state, not a throwing form

For `Students/Create.razor`, `Students/Edit.razor`, `Students/Detail.razor`,
`Subjects/Create.razor`, `Subjects/Edit.razor`, `GradeLevels/Edit.razor`:

1. Inject `VisibleTenantService`; resolve `_isRealTenant` first.
2. If `!_isRealTenant`, render a single `FluentMessageBar` (Intent `Warning`):
   `"You have no tenant assigned. Student/grade/subject/period records are
   tenant-scoped — pick a tenant to continue."` and **do not** render the
   `EditForm` / related-data dropdowns. This prevents both the create throw and
   the coded-value/grade-level dropdown loads that themselves throw.
3. If `_isRealTenant`, render the form as today. As defense-in-depth, also
   `Disabled="@(!_isRealTenant)"` the submit `FluentButton` so a stale render
   can never POST.

### 4.4 `Periods.razor` (standalone, not `LandingPage`)

1. Resolve `_isRealTenant` first; if false, skip `Api.ListPeriodsAsync`, set
   `_items = []`, show the same warning `FluentMessageBar`, and hide/disable the
   `+ New Period` button and create panel.
2. Gate `ShowCreatePanel` and `OnCreateAsync` on `_isRealTenant` (hard guard:
   `if (!_isRealTenant) return;`).

### 4.5 `GradeLevelWizard.razor` — keep live tracking, delegate the rule

The wizard is the **only** surface that must re-evaluate tenant mid-session (its
override buttons toggle if the tenant changes without a full reload). It
therefore **keeps** its `AuthenticationStateChanged` subscription and
`RefreshTenantFromAuthStateAsync` handler (lines 452, 463–482), but delegates the
*rule* to `VisibleTenantService` (or the extension) so `IsRealTenant` is defined
in one place:

```csharp
_isRealTenant = (await VisibleTenant.GetScopeAsync(ct)).IsRealTenant;
```

- Do **not** delete the `AuthenticationStateChanged` handler — the 10 list/form
  pages don't need it, but this page does.
- Existing `@if (IsRealTenant)` override-action render-gating (lines 57, 97, 187)
  stays unchanged.
- Add a top-level guard: if `!_isRealTenant`, block the wizard's create commit
  (the wizard creates a `GradeLevel` — strict, FR-4).

### 4.6 Navigation (optional, secondary defense)

Per-page gating is deep-link safe, so nav hiding is a nicety, not a requirement.
If desired, in the shared `NavMenu`/layout, hide the Students / Grade Levels /
Subjects / Periods entries when `!IsRealTenant` (resolve once via
`VisibleTenantService` in the layout). **Not required for the fix** — the per-page
guard is the contract.

### 4.7 AI tools (future-proofing, out of scope for this change)

The AI tool layer (`SchoolCollab.AI.Abstractions.IToolProvider`,
`CodedValuesToolProvider`) currently has no Students/strict-entity provider.
When one is added, its `CreateTools` must omit strict-entity create/search tools
when the caller has no visible tenancy — the same `IsRealTenant` concept, read
from the authenticated principal. Tracked here so the pattern is consistent with
spec FR-19 (Blazor-mediated writes) and doesn't regress when the AI layer grows.
**No code change in this spec.**

## 5. Requirements (FR)

| ID | Requirement |
|----|-------------|
| **VT-FR-1** | A shared `VisibleTenantService` (or a static `ClaimsPrincipal.IsRealTenant()` extension) MUST define the "real tenant" rule — `tenant_id` claim parses to a non-empty `Guid` — in exactly one unit-testable place, and MUST NOT touch the server-side `ITenantProvider`. The service exposes a one-shot `GetScopeAsync()` only; live `AuthenticationStateChanged` tracking is NOT part of the shared service (it stays only in `GradeLevelWizard`, §4.5). |
| **VT-FR-2** | Every list page (`Index`, `GradeLevels`, `Subjects`, `Periods`) MUST skip all tenant-scoped API calls when `!IsRealTenant` and render an explanatory empty state instead of throwing. |
| **VT-FR-3** | The `LandingPage` "+ New" button and search box MUST be hidden (`CreateEnabled=false`, `SearchEnabled=false`) when `!IsRealTenant`. |
| **VT-FR-4** | Every create/edit/detail form page MUST render a warning message and NOT render the form (nor load related dropdowns) when `!IsRealTenant`. The submit button MUST additionally be `Disabled` when `!IsRealTenant` as defense-in-depth. |
| **VT-FR-5** | `Periods.razor` MUST disable `+ New Period` and the create panel and skip the list load when `!IsRealTenant`. |
| **VT-FR-6** | `GradeLevelWizard` MUST derive the `IsRealTenant` *value* from the shared rule (§4.1) while keeping its own `AuthenticationStateChanged` live-update handler, and MUST block its create commit when `!IsRealTenant`. |
| **VT-FR-7** | No tenant-scoped API call may be issued from the Admin UI when `!IsRealTenant` (verified: zero such calls reach the API). |
| **VT-FR-8** | Global/hybrid surfaces (CodedValue browsing, Tenant registry, feature flags) MUST remain fully usable when `!IsRealTenant` — this change MUST NOT touch them. |

## 6. Acceptance criteria (AC)

1. Sign in as a user with **no `tenant_id` claim**. Open each of the 11 pages in
   §3. None throws; each shows a clear "select/assign a tenant" message (list
   pages) or warning bar (form pages). The browser network tab shows **zero**
   `/students`, `/students/grade-levels`, `/students/subjects`, `/students/periods`
   requests from those page loads.
2. On `Index`/`GradeLevels`/`Subjects`, the "+ New" button and the search box are
   absent when `!IsRealTenant`.
3. `Periods.razor` shows no `+ New Period` button and no create panel when
   `!IsRealTenant`.
4. Deep-linking directly to `/students/create`, `/students/subjects/create`,
   `/students/grade-levels/create`, `/students/{id}/edit` while `!IsRealTenant`
   shows the warning, not a throwing form.
5. Sign in as a user **with a real `tenant_id`**. All 11 pages behave exactly as
   before (create/search/CRUD work; no regression).
6. `GradeLevelWizard` override actions still appear only for a real tenant
   (behavior unchanged), now sourced from `VisibleTenantService`.
7. `dotnet build` = 0 errors; `dotnet test` (excluding live-AI + Playwright) =
   0 failures. Add at least one unit test for `VisibleTenantService`
   (`IsRealTenant` true for a non-empty Guid claim; false for null/`Guid.Empty`)
   and a bUnit test asserting a gated list page renders the empty-state message
   and issues no API call when `!IsRealTenant`.

## 7. Implementation order

1. `VisibleTenantService` + DI registration + unit test.
2. `Index.razor`, `GradeLevels.razor`, `Subjects.razor` (LandingPage lever —
   smallest blast radius, biggest win).
3. `Periods.razor` (standalone).
4. Form pages: `Students/Create`, `Students/Edit`, `Students/Detail`,
   `Subjects/Create`, `Subjects/Edit`, `GradeLevels/Edit`.
5. Refactor `GradeLevelWizard` onto `VisibleTenantService`; add create-commit
   guard.
6. bUnit test for one gated list page.
7. Pre-flight: `dotnet build`, `dotnet test`, targeted code review.

## 8. Adoption pattern for future requirements

Any future Admin UI feature (page, component, or AI tool) that reads or writes a
**strict-tenant** entity (`GradeLevel`, `Subject`, `Period`, `Student`, or any
new entity added to the §3.2 strict list in `global-tenant-filter.md`) MUST follow
this pattern. Hybrid (`CodedValue`) and Global entities are exempt.

**The pattern — copy this checklist into the new feature's task:**

1. **Classify the entity.** Confirm its tenancy in `global-tenant-filter.md` §3.2.
   If strict → apply this pattern. If hybrid/global → stop (these work without a
   visible tenant).
2. **Resolve tenancy once, up front.** In `OnInitializedAsync`, call
   `VisibleTenantService.GetScopeAsync()` (or the `ClaimsPrincipal.IsRealTenant()`
   extension) **before** any `*ApiClient` call. Cache `IsRealTenant` in a field.
3. **Gate every tenant-scoped API call.** `if (!IsRealTenant) { _items = []; return; }`
   (list) or render the warning instead of the form (create/edit). Never issue a
   tenant-scoped call when `!IsRealTenant`.
4. **Disable the affordances.** Bind `LandingPage.CreateEnabled` /
   `SearchEnabled` (or `Disabled` on a standalone button/submit) to `IsRealTenant`.
   Prefer render-gating (`@if (IsRealTenant)`) for actions that are semantically
   meaningless without a tenant, matching `GradeLevelWizard` lines 57/97/187.
5. **Message, don't throw.** Show an explanatory empty state / warning bar
   ("Select/assign a tenant to …") — never let the server
   `TenantContextRequiredException` reach the user.
6. **Live re-resolution only if needed.** Subscribe to `AuthenticationStateChanged`
   only if the surface must toggle without a full reload (rare —
   `GradeLevelWizard` is the current example). One-shot pages do not.
7. **Test.** Add a `VisibleTenantService`/extension unit test for the rule, and a
   bUnit test asserting the gated page renders the empty/warning state and issues
   **zero** tenant-scoped API calls when `!IsRealTenant`.

**When adding a new strict entity** (not just a page): also update
`global-tenant-filter.md` §3.2, add the `ITenantEntity` +
`TenantEntityTypeConfigurationBase<T>` filter (FR-1), the no-empty-creation guard
in its create handler (FR-4), and pass the `ValidateTenantFilters` build-time
audit (FR-14) — then apply the UI pattern above to every page that manages it.

**AI tools (when a Students/strict-entity `IToolProvider` is added):** its
`CreateTools` MUST omit strict-entity create/search tools when the caller's
principal has `!IsRealTenant`, reusing the same rule from §4.1. This keeps the AI
layer consistent with FR-19 and the UI guard.

## 9. Out of scope

- AI tool providers (§4.7) — tracked, no code in this change.
- Server-side tenant resolution / middleware — the API already throws correctly;
  the fix is purely client-side prevention.
- Nav menu hiding (§4.6) — optional; per-page gating is the contract.
- Changing `LandingPage.razor` — its existing `CreateEnabled`/`SearchEnabled`
  params already suffice; no component edit needed.