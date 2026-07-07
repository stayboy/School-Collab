# Grade-Level Setup — Implementation Progress / Handoff

Companion to `documents/specs/grade-level-setup.md` (the locked spec).
This file tracks PR-by-PR execution state so a future session can resume.

**Spec:** `documents/specs/grade-level-setup.md` — 10-PR plan, all §11 questions resolved.
**Last updated:** All 10 PRs complete. Wizard UX fully redesigned (FluentWizard, FluentGrid, native FluentUI components, auto-add on subject picker selection, override gating on real tenant). Build green, tests passing.

---

## Current build / test state

- **Solution build: GREEN** (0 errors)
- **Tests: GREEN**
  - Assignments: 43 tests
  - Students: 36 tests (incl. 5 `CreateSubjectForGradeHandlerTests` + 2 `GetOrCreateSubjectHandlerTests`)
  - Core: 38, Settings: 334 (incl. rewritten `CodedValueOverrideHandlerTests` with `MutableTenantProvider`), Admin: 20, Architecture: 8

---

## PRs landed (build green, tests pass)

### PR 1 — Tenant registry + sample seeding + dev tenant switcher ✅
- `Settings.Core/Domain/Tenant.cs` — global `Tenant` entity, unique on `Name`
- `MigrationService/Seeding/TenantSeeder.cs` — seeds 'Hydeson School' + 'Little Legends'
- Dev tenant switcher (`DevTenantSwitcher.razor`) in admin header (Testing env only)
  - Moved to **far right** of the toolbar (after theme switcher and settings button)
  - Explicit `Width="180px"` on the `FluentSelect` to prevent overstretch
  - `IDialogService` to reset selection cleared (no spurious dialogs on select)

### PR 2 — Coded-value override endpoints + client methods ✅
- `PUT/DELETE /api/coded-values/{id}/override`
- `CodedValuesApiClient.UpsertOverrideAsync` / `RemoveOverrideAsync`
- **Default-tenant branch added**:
  - `UpsertCodedValueOverrideHandler` branches on `TenantContext.IsDefault`. For the default tenant (`Guid.Empty`), it calls `CodedValue.Update(name, description, displayOrder)` on the global coded value instead of creating a `TenantCodedValueOverride` row. Rationale: per-tenant overrides are meaningless without a real tenant
  - `RemoveCodedValueOverrideHandler` is a no-op for the default tenant (no override row to remove)
  - `TenantContext.IsDefault` helper added (preferred over `Guid.Empty` direct comparison)

### PR 3 — GradeLevel+Subject unique indexes, find-or-create, current-period landing query ✅
- Unique indexes on `CodedValueId` for both entities
- `PeriodOverlapException` + invariant enforcement
- `ListGradeLevelsForLanding` query/handler with current-period stats
- `GetOrCreateGradeLevel` command/handler for wizard's find-or-create save

### PR 4 — Assignment → GradeLevel + Subject migration ✅
- Domain: `SubjectCodedValueId`→`SubjectId`, `GradeCodedValueId`→`GradeLevelId`
- Contracts: renamed fields in DTOs
- UI: Create.razor cascading GradeLevel→Subject dropdowns; Edit.razor field renames
- Students: `ListSubjectsByGrade` endpoint + client
- Backfill: `AssignmentBackfillService` for cross-DB migration
- **Subjects are not period-bound** — `ListSubjectsByGrade` without a `periodId` returns all assignments across periods, not an empty array. Tests updated: `NoCurrentPeriod_ReturnsEmpty` → `NoCurrentPeriod_ReturnsAllAssignedSubjects`; `WithExplicitPeriodId_UsesProvidedPeriod` "no period → empty" assertion changed to "no period → returns all"

### PR 5 — Grade-coded-value dialog ✅
- `CodedValueDialog.razor` in `Admin.Shared.Components` (**renamed from `GradeCodedValueDialog`** — now generic for any coded value)
- `CodedValueDialogData` / `CodedValueDialogResult` types
- Create mode: creates new coded value under any parent (GRADE or SUBJECT)
- Override mode: upserts tenant-specific display name override
- Reset: removes override, reverts to global name
- **Wizard rebuilt on `FluentWizard`** to match the assignment wizard UX (3 steps: Grade & Subjects / Students / Review):
  - `StepperPosition.Top`, `StepSequence.Visited`, per-step `Label`/`Summary`/`IconPrevious`
  - Each step uses native `FluentGrid` for layout
  - Bottom action bar uses `<ButtonTemplate Context="stepIndex">` with **Back** / **Cancel** / **Continue** / **Save and Finish** (mirrors `Assignments/Create.razor`)
  - `ErrorBoundary` wraps the wizard; `_step1Error`/`_step2Error` provide per-step validation; `OnChange` handlers gate navigation
  - `DeferredLoading="true"` on the Review step
  - `@implements IDisposable` with `CancellationTokenSource` for safe load cancellation
- **Step 1 (Grade & Subjects)** layout:
  - Each section rendered as a **1/3 + 2/3 split via `FluentGrid`** (12-col system: `xs="12" md="4"` first column, `xs="12" md="8"` second column). Columns stack on mobile
  - **First column**: section header (icon + title on one row via `.wizard-split-header-title` flex row, subtitle left-aligned below) + picker + buttons stacked directly underneath
  - **Second column**:
    - **Grade section**: wide `FluentCard` showing the resolved name, code, "Overridden" badge, and "Reset to default" link (or `FluentMessageBar` empty-state when no grade is picked). No "Selected grade" label header
    - **Subject section**: the **assigned-subjects list** with Remove buttons, or `FluentMessageBar` empty-state when no subjects are assigned
  - A **sizeable 48px gap** separates the two sections
  - **Grade picker**: coded-value dropdown (no `Label` attribute — the section title already labels it) + **Override name** / **New grade** buttons stacked below
  - **Subject picker**: subject coded-value dropdown (no `Label` attribute) — picking a value **auto-adds the subject to the grade's assigned list** (no separate "Add to grade" button). The picker's `SelectedIdChanged` handler calls `GetOrCreateSubjectAsync` directly, appends the subject to `_assignedSubjects`, then resets the picker to `null` so the user can pick another subject immediately. A compact `FluentCard` chip under the picker confirms the most recent add ("✓ Mathematics added"). A **New subject** button opens the dialog to create a new subject coded value (the dialog's success path also triggers auto-add)
  - **Native FluentUI components** throughout: `FluentGrid`, `FluentGridItem`, `FluentCard`, `FluentLabel`, `FluentBadge`, `FluentMessageBar`, `FluentAnchor`, `FluentStack`, `FluentIcon` — no custom flex/background/padding rules in the CSS, only font-weight/size/color refinements
- **Step 2 (Students)**: student-enrollment grid (no current period → blocked with a warning)
- **Step 3 (Review)**: `review-card` with `review-grid`/`review-row` showing the resolved name, tenant-override badge, current period, and counts
- **Override gating on real tenant**:
  - `ITenantProvider` injected into the wizard
  - `IsRealTenant` is a **field** (not a computed property) set in `OnInitializedAsync` and refreshed in `OnAfterRender` (Blazor's diffing doesn't re-evaluate a property whose backing store — the singleton `TenantProvider`'s `AsyncLocal` — changes without a `[Parameter]` change or a state event)
  - The `Override name` button and `Reset to default` link on both grade and subject sections are wrapped in `@if (IsRealTenant)` — hidden in default-tenant mode
- `CodedValueDropdown.RefreshAsync()` is called after the dialog closes so the resolved name + `IsOverridden` flag are re-evaluated
- **Wizard save uses `GetOrCreateGradeLevelAsync`** (find-or-create by CodedValueId) with the resolved coded-value name and the coded value's own `Level`/`DisplayOrder` (not user-typed)
- **Current period** is derived client-side; step 2 is blocked with a warning when no current period exists
- **`CodedValueDto.IsOverridden`** flag added — set by `CodedValueResolver` and `GetCodedValueById` when an override contributed to the resolved name; the wizard uses it to show the "Overridden" badge
- **CSS extracted** to `GradeLevelWizard.razor.css` (sibling of the .razor file, auto-included by Razor SDK) — matches the assignment wizard's file layout

### PR 6 — GradeLevel + Subject edit/delete ✅
- Domain: `GradeLevel.Delete()` + `Subject.Delete()` methods
- Exceptions: `GradeLevelReferencedException` + `SubjectReferencedException`
- Commands: `DeleteGradeLevel` + `DeleteSubject` with referential integrity checks
- Endpoints: `DELETE /students/grade-levels/{id}` + `DELETE /students/subjects/{id}`
- Client: `DeleteGradeLevelAsync()` + `DeleteSubjectAsync()`
- UI: `GradeLevels/Edit.razor` + `Subjects/Edit.razor` pages

### PR 7 — Grade-Level landing page onto LandingPage ✅
- `GradeLevels.razor` rewritten onto `<LandingPage<GradeLevelLandingDto>>`
- Stats columns: Subjects/Students link to filtered pages with `gradeLevelId`/`periodId`
- Current-period column shows period name or "No current period"
- Tenant-resolved name overlay via `CodedValuesApi.GetChildrenByParentCodeAsync("GRADE")`
- Edit/Delete row actions with confirmation dialogs

### PR 8 — Subjects landing page onto LandingPage ✅
- `Subjects.razor` rewritten onto `<LandingPage<SubjectDto>>`
- **Mandatory GradeLevel filter** in `<ToolbarFilters>` (FluentSelect)
- Subjects loaded via `ListSubjectsByGradeAsync(gradeLevelId, periodId?)` — returns all assignments across periods when `periodId` is null
- Query param support: `?gradeLevelId={guid}&periodId={guid?}`
- Edit/Delete row actions with confirmation dialogs
- Empty state messages for no-grade-level and no-subjects conditions
- **`POST /students/subjects/for-grade` endpoint** + `CreateSubjectForGrade` command/handler (creates Subject + GradeSubjectAssignment for current period)
- **`POST /students/subjects/get-or-create` endpoint** + `GetOrCreateSubject` command/handler (find-or-create by CodedValueId, used by the wizard's auto-add flow)
- **`CreateSubjectForGradeAsync`** + **`GetOrCreateSubjectAsync`** client methods
- **`Subjects/Create.razor` page** (`+ New Subject` always carries `gradeLevelId`, creates Subject + GradeSubjectAssignment in one call)

### PR 9 — Deep-link filter contract (Students page) ✅
- `/students?gradeLevelId={guid}&periodId={guid?}` query params
- `ListStudentsByGrade` query/handler: returns students enrolled in grade for period
- `GET /students/by-grade/{gradeLevelId}?periodId={guid?}` endpoint
- `StudentsApiClient.ListStudentsByGradeAsync()` client method
- Students page: filter chip showing active grade filter with dismiss button
- GradeLevels landing: Subjects/Students counts deep-link to filtered pages

### PR 10 — Docs ✅
- `documents/solution/grade-level-setup.md` — feature documentation
- `documents/specs/grade-level-setup-progress.md` — progress tracker (this file)

---

## Remaining PRs

_All 10 PRs complete._

---

## Key files added/modified

### Backend
- `Students.Core/CQRS/Students/Queries/ListStudentsByGrade/` — query + handler
- `Students.Core/CQRS/Subjects/Queries/ListSubjectsByGrade/` — query + handler (period-optional)
- `Students.Core/CQRS/Subjects/Commands/CreateSubjectForGrade/` — command + handler
- `Students.Core/CQRS/Subjects/Commands/GetOrCreateSubject/` — command + handler (find-or-create by CodedValueId)
- `Students.Core/CQRS/GradeLevels/Commands/GetOrCreateGradeLevel/` — command + handler
- `Students.Core/CQRS/GradeLevels/Commands/DeleteGradeLevel/` — command + handler
- `Students.Core/CQRS/Subjects/Commands/DeleteSubject/` — command + handler
- `Students.Core/Domain/GradeLevel.cs` — `Delete()` method
- `Students.Core/Domain/Subject.cs` — `Delete()` method
- `Students.Core/Domain/Events/GradeLevelDeletedEvent.cs`, `SubjectDeletedEvent.cs`
- `Students.Core/Domain/Exceptions/GradeLevelReferencedException.cs`, `SubjectReferencedException.cs`
- `Students.Core/Domain/Exceptions/DomainExceptions.cs` — `NoCurrentPeriodException`
- `Students.Core/Data/Repositories/ISubjectRepository.cs` + `SubjectRepository.cs` — `GetByCodedValueIdAsync`
- `Students.Core/Data/Repositories/IPeriodRepository.cs` + `PeriodRepository.cs` — `GetCurrentPeriodAsync`
- `Students.Api/Endpoints/StudentRoutes.cs` — `/by-grade/{gradeLevelId}` endpoint
- `Students.Api/Endpoints/SubjectRoutes.cs` — `/by-grade/{gradeLevelId}` + `/for-grade` + `/get-or-create` endpoints
- `Students.Api/Endpoints/GradeLevelRoutes.cs` — DELETE + `/get-or-create` + `/landing` endpoints
- `Students.Admin/Services/StudentsApiClient.cs` — new client methods (`GetOrCreateGradeLevelAsync`, `GetOrCreateSubjectAsync`, `CreateSubjectForGradeAsync`, `ListStudentsByGradeAsync`, etc.)
- `Settings.Core/CQRS/CodedValues/Commands/UpsertCodedValueOverride/` — default-tenant branch
- `Settings.Core/CQRS/CodedValues/Commands/RemoveCodedValueOverride/` — default-tenant no-op
- `Settings.Core/Services/CodedValueResolver.cs` — sets `IsOverridden` flag
- `Settings.Core/DTOs/CodedValueDto.cs` — `IsOverridden` field
- `SchoolCollab.Core/Tenancy/ITenantProvider.cs` — `TenantContext.IsDefault` helper

### Frontend
- `Admin.Shared/Components/CodedValueDialog.razor` (renamed from `GradeCodedValueDialog`) — generic create/override dialog
- `Admin.Shared/Components/CodedValueDialogData.cs` (renamed) — dialog types
- `Admin.Shared/Components/Layout/SchoolCollabLayout.razor` — dev tenant switcher moved to far right; `.layout-header-stack` has `min-width: 0; overflow: visible;`; `.brand-page` truncates with ellipsis
- `Admin.Shared/Components/Layout/SchoolCollabLayout.razor.css` — same
- `Admin/Components/Layout/DevTenantSwitcher.razor` — `Width="180px"` on `FluentSelect`
- `Admin/Components/Layout/DevTenantSwitcher.razor.css` — removed redundant `min-width` rules
- `Students.Admin/Components/Pages/Students/GradeLevels/GradeLevels.razor` — LandingPage rewrite
- `Students.Admin/Components/Pages/Students/GradeLevels/Edit.razor` — grade level edit page
- `Students.Admin/Components/Pages/Students/GradeLevels/GradeLevelWizard.razor` — 3-step `FluentWizard`, `FluentGrid` 1/3 + 2/3 split, native FluentUI components, auto-add on subject picker selection, override gating on `IsRealTenant`
- `Students.Admin/Components/Pages/Students/GradeLevels/GradeLevelWizard.razor.css` — extracted CSS
- `Students.Admin/Components/Pages/Students/Subjects/Subjects.razor` — LandingPage rewrite with GradeLevel filter
- `Students.Admin/Components/Pages/Students/Subjects/Create.razor` — subject create page
- `Students.Admin/Components/Pages/Students/Subjects/Edit.razor` — subject edit page
- `Students.Admin/Components/Pages/Students/Index.razor` — added grade/period filter support

### Tests
- `Students.Tests.Unit/CreateSubjectForGradeHandlerTests.cs` — 5 tests
- `Students.Tests.Unit/GetOrCreateSubjectHandlerTests.cs` — 2 tests
- `Students.Tests.Unit/GetOrCreateGradeLevelHandlerTests.cs` — 2 tests
- `Students.Tests.Unit/ListGradeLevelsForLandingHandlerTests.cs` — 4 tests
- `Students.Tests.Unit/ListSubjectsByGradeHandlerTests.cs` — 4 tests (updated for period-optional behavior)
- `Students.Tests.Unit/StudentsTestScope.cs` — added `Subjects` + `GradeSubjectAssignments` repository properties
- `Settings.Tests.Unit/Handlers/CodedValueOverrideHandlerTests.cs` — rewritten with `MutableTenantProvider`; 8 tests covering real-tenant and default-tenant branches
- `Settings.Tests.Unit/Handlers/CodedValueOverrideHandlerTests.cs` — uses `MutableTenantProvider` so each test opts into either scenario

### Docs
- `documents/solution/grade-level-setup.md` — feature documentation
- `documents/specs/grade-level-setup-progress.md` — progress tracker (this file)

---

## Key decisions / gotchas

- **Cross-DB**: GradeLevel/Subject live in Students DB; Assignment in Assignments DB → **no physical FK**.
- **Delete is hard delete** with referential guard — throws if enrollments/assignments exist.
- **Subjects page requires GradeLevel filter** — no "all subjects" view; must select a grade first.
- **Create button on Subjects page** routes to `/students/subjects/create?gradeLevelId={id}`.
- **GradeLevels landing shows tenant-resolved names** via client-side join with coded-values API.
- **Students by grade** filters by active enrollments in the current (or specified) period.
- **CreateSubjectForGrade** derives the current period server-side via `IPeriodRepository.GetCurrentPeriodAsync()`; throws `NoCurrentPeriodException` if no period covers today.
- **Subjects are not period-bound** — `Subject` is a global entity; only the `GradeSubjectAssignment` linking it to a grade is period-scoped. `ListSubjectsByGrade` without a `periodId` returns all assignments across periods. This keeps the Subjects landing page useful even when no current period exists.
- **Wizard is 3 steps**: the original 4-step wizard (Basic Info → Subjects → Students → Summary) was collapsed by merging Basic Info + Subjects into a single step. Step order: **Grade & Subjects → Students → Summary**.
- **Wizard period derivation** is done client-side from `ListPeriodsAsync()` (same rule: `StartDate <= today <= EndDate`); steps 1–2 are blocked when no current period exists.
- **Wizard Step 1 is a read-only display, not a form**: the coded-value dropdown is the only input. The user cannot type a name that bypasses the override system — the only way to change the displayed name is the **Override** dialog (per-tenant) or picking a different coded value. `Level` and `DisplayOrder` are **never shown to the user** — they are mirrored metadata on `GradeLevel` derived from the coded value.
- **Auto-add on subject picker selection** (no separate "Add to grade" button): the picker's `SelectedIdChanged` handler calls `GetOrCreateSubjectAsync` directly, appends to `_assignedSubjects`, then resets the picker to `null` so the user can pick another subject immediately. Removes the friction of a separate confirmation step.
- **Override gated on real tenant**: the `Override name` button and `Reset to default` link are only shown when `IsRealTenant` is `true`. In default-tenant mode, the override handler rewrites the global `CodedValue` directly (no per-tenant row), so the per-tenant UI is meaningless there.
- **`IsRealTenant` is a field, not a computed property**: Blazor's diffing doesn't re-evaluate a property whose backing store (the singleton `TenantProvider`'s `AsyncLocal`) changes without a `[Parameter]` change or a state event. The field is set in `OnInitializedAsync` and refreshed in `OnAfterRender` to catch tenant switches (e.g., via the dev tenant switcher's `forceLoad: true` page reload).
- **`CodedValueDto.IsOverridden`** is set by the resolver (and `GetCodedValueById`) whenever a `TenantCodedValueOverride` contributed to the resolved name. The wizard uses it to render the "Overridden" badge and the "Reset to default" affordance.
- **Default-tenant override branch**: in the override handlers, when `TenantContext.IsDefault` is `true`, the `UpsertCodedValueOverride` handler calls `CodedValue.Update(name, description, displayOrder)` on the global coded value (preserving the existing `DisplayOrder`) instead of creating a `TenantCodedValueOverride` row. `RemoveCodedValueOverride` is a no-op. The `TenantContext.IsDefault` helper makes the intent explicit (preferred over `Guid.Empty` direct comparison).
- **Tenant switcher toolbar placement**: moved to the **far right** of the header toolbar (after theme switcher and settings button) with explicit `Width="180px"` on the `FluentSelect`. The `.layout-header-stack` has `min-width: 0; overflow: visible;` to prevent the stack from overflowing and to allow dropdown popups to render below the header. The `.brand-page` truncates with ellipsis (`max-width: 32ch; overflow: hidden; text-overflow: ellipsis;`) to prevent long page titles from pushing the right side off-screen.
- **Native FluentUI components**: the wizard uses `FluentGrid` + `FluentGridItem` (native 12-col grid), `FluentCard` + `FluentLabel` + `FluentBadge` + `FluentMessageBar` + `FluentAnchor` + `FluentStack` for layout and content. No custom flex/background/padding rules in the CSS — only font-weight/size/color refinements. The `Label` attribute was removed from both `CodedValueDropdown`s because the section title already labels them.
