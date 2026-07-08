# Spec: Per-Subject Override in Grade-Level Wizard

> Status: **Implemented** — code changes complete, build green, tests passing.  
> Owner: Students.Admin + Settings.Api (no new backend entities).  
> Depends on: `documents/specs/grade-level-setup.md` (PRs 1–10 complete).

## 1. Context

The Grade-Level wizard (`/students/grade-levels/create`) already lets a user override the display name of the **selected grade** coded value and the **most recently added subject** via `CodedValueDialog`. However, the list of **all subjects already assigned to the grade** only shows a *Remove* button. Users cannot override or reset the display name of a subject that was added earlier without removing it and re-adding it.

This feature adds an inline override affordance to every subject row in the wizard’s assigned-subjects list, so each subject can be renamed per-tenant (or renamed globally in default-tenant mode) directly from the list.

## 2. Goal

Enable per-tenant display-name override and reset for **each subject already assigned to the grade** in the Grade-Level wizard, with a one-line-per-subject interaction pattern.

## 3. Functional Requirements

| ID | Requirement |
|----|-------------|
| FR-1 | The wizard MUST render an inline action for every subject in the assigned-subjects list that opens the existing `CodedValueDialog` in **Override** mode for that subject’s coded value. |
| FR-2 | The override action MUST only appear when a real tenant is in scope (`IsRealTenant == true`). In default-tenant mode the action MUST be hidden, because the override concept collapses to editing the global coded value (already handled by the *New subject* / *Override* chip flow). |
| FR-3 | When the user saves an override from the per-row dialog, the wizard MUST refresh the subject row so it displays the newly resolved name and the **Overridden** badge. |
| FR-4 | When the user removes an override from the per-row dialog, the wizard MUST refresh the subject row so it displays the original global name and removes the **Overridden** badge. |
| FR-5 | The existing *Remove* action on each row MUST remain available. |
| FR-6 | The override action MUST be disabled while the row is being refreshed (if async) to prevent duplicate submissions. |
| FR-7 | The per-row override action MUST reuse the existing `CodedValuesApiClient.UpsertOverrideAsync` / `RemoveOverrideAsync` client methods and the existing `CodedValueDialog` component; no new backend endpoint is required. |

## 4. Non-Functional Requirements

| ID | Requirement |
|----|-------------|
| NFR-1 | The change MUST keep the existing wizard layout and CSS classes; only the assigned-subjects list row template changes. |
| NFR-2 | The override dialog MUST be keyboard-accessible (same as today) and return focus to the triggering row action after close. |
| NFR-3 | No new JavaScript or custom JS interop is required. |
| NFR-4 | The change MUST compile with 0 errors and 0 new warnings. |

## 5. Acceptance Criteria

| ID | Criterion | Traces |
|----|-----------|--------|
| AC-1 | Given a real tenant is selected, when the Grade-Level wizard shows the assigned-subjects list, then each row displays the subject name, an *Override name* action, and a *Remove* action. | FR-1, FR-2 |
| AC-2 | Given the default tenant is selected, when the assigned-subjects list renders, then the *Override name* action is not shown on any row. | FR-2 |
| AC-3 | Given the user clicks *Override name* on a subject row, when the override dialog closes successfully, then the row shows the new resolved name and an **Overridden** badge. | FR-3 |
| AC-4 | Given the user resets an override from the per-row dialog, when the dialog closes successfully, then the row shows the original global name and the **Overridden** badge is removed. | FR-4 |
| AC-5 | Given the user clicks *Remove* on a subject row, when the action completes, then the subject is removed from the list exactly as before. | FR-5 |
| AC-6 | Given an override action is clicked, when the dialog is already open or a refresh is in progress, then the action is disabled to prevent duplicate submissions. | FR-6 |
| AC-7 | Given the feature is implemented, when `dotnet build` runs, then the solution builds with 0 errors and 0 new warnings. | NFR-4 |

## 6. Edge Cases

| ID | Case | Handling |
|----|------|----------|
| EC-1 | Subject coded value is deleted or becomes unavailable while the wizard is open. | The override dialog will return `null` or throw; the row refresh is skipped and the existing error message pattern (`_step1Error`) is used. |
| EC-2 | User overrides the same subject multiple times in one session. | Each override replaces the previous tenant override; the row refreshes to the latest resolved name. |
| EC-3 | User overrides a subject, removes it from the list, then re-adds it via the picker. | The re-added subject displays the current resolved name (override still applies) because `GetOrCreateSubjectAsync` + `CodedValueResolver` resolve it. |
| EC-4 | Network/API failure during override upsert or removal. | The dialog shows its own error message (existing behavior) and does not close; the wizard row remains unchanged. |
| EC-5 | Default-tenant mode with no override action visible. | Users can still edit the global coded value via the subject chip’s override action or the *New subject* flow; the per-row action is hidden only because the per-tenant concept is meaningless. |

## 7. UI/UX Design

### 7.1 Row template (assigned-subjects list)

Current row template (simplified):

```razor
<li>
    <span>@s.Name</span>
    <FluentButton Appearance="Appearance.Lightweight" OnClick="@(() => RemoveSubject(s))">Remove</FluentButton>
</li>
```

New row template:

```razor
<li class="assigned-subject-row">
    <span class="assigned-subject-name">@s.Name</span>
    @if (s.IsOverridden)
    {
        <FluentBadge Appearance="Appearance.Accent" class="grade-confirm-badge">Overridden</FluentBadge>
    }
    <span class="assigned-subject-actions">
        @if (IsRealTenant)
        {
            <FluentButton Appearance="Appearance.Lightweight"
                          Title="Override the default name for this tenant"
                          OnClick="@(() => OverrideSubjectAsync(s))"
                          Disabled="@(_overridingSubjectId == s.Id)">
                Override name
            </FluentButton>
        }
        <FluentButton Appearance="Appearance.Lightweight"
                      OnClick="@(() => RemoveSubject(s))"
                      Disabled="@(_overridingSubjectId == s.Id)">
            Remove
        </FluentButton>
    </span>
</li>
```

### 7.2 Subject DTO shape used by the wizard

The wizard already receives `SubjectDto` from `Api.GetOrCreateSubjectAsync`. `SubjectDto` must expose the same resolved-name and override fields as `CodedValueDto` so the row can display the badge and the dialog can be pre-populated. If `SubjectDto` does not currently carry `IsOverridden`, the backend/client contract must be extended (see §8).

## 8. API / Data Contracts

### 8.1 Existing APIs (no change)

- `PUT /api/coded-values/{id:guid}/override` → `CodedValueDto`
- `DELETE /api/coded-values/{id:guid}/override` → `204`

### 8.2 Subject DTO extension

Add `IsOverridden` to the **admin client** `SubjectDto` in `SchoolCollab.Students.Admin.Services`:

```csharp
public sealed record SubjectDto(
    Guid Id,
    Guid CodedValueId,
    string Code,
    string Name,
    int DisplayOrder,
    bool IsOverridden,   // NEW — true if the displayed name is currently overridden
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
```

The backend `SchoolCollab.Students.Core.DTOs.SubjectDto` **does not change**. Students.Core intentionally remains decoupled from the coded-value override resolver in Settings.Core; the override state is resolved client-side from the coded value when the subject is added or overridden.

JSON deserialization into the admin client DTO will default `IsOverridden` to `false` when reading from the API, and the wizard explicitly sets it from the `CodedValueDto` it already fetches.

### 8.3 Wizard method signature

```csharp
private async Task OverrideSubjectAsync(SchoolCollab.Students.Admin.Services.SubjectDto subject)
```

Implementation opens `CodedValueDialog` with:
- `Mode = "Override"`
- `ParentId = null` (not used in override mode)
- `CodedValue = new CodedValueDto(...)` built from the subject’s `CodedValueId`, `Code`, `Name`, `IsOverridden`, and `ParentId` mapped to the `SUBJECT` parent
- `HasOverride = subject.IsOverridden`

After a successful dialog result, call `CodedValuesApi.GetByIdAsync(subject.CodedValueId)` to refresh the resolved name and override flag, then update the local `SubjectDto` instance in `_assignedSubjects` (or replace it by reference).

## 9. Data Model

No new entities or migrations are required. The feature reuses:

- `TenantCodedValueOverride` (Settings.Core) for real-tenant overrides.
- `CodedValue` (Settings.Core) for default-tenant global renames.
- `Subject` (Students.Core) as the operational entity whose `CodedValueId` links to the override system.

## 10. Out of Scope

| Item | Reason |
|------|--------|
| Inline editing without a dialog | Keep the existing override/reset UX pattern; no new dialog design. |
| Bulk override of all subjects | One-line-per-row action is the requested scope. |
| Override display-name of the grade from the subject list | Grade override is already handled in the grade section above. |
| New backend endpoints or handlers | Existing `UpsertCodedValueOverride` / `RemoveCodedValueOverride` are sufficient. |
| Changing the subject picker auto-add behavior | Unchanged; auto-add still applies. |
| Changing the "last-added subject" chip override | Unchanged; keep as a confirmation affordance. |

## 11. Implementation Notes

### 11.1 Tenant propagation in Blazor Server

The wizard reads `tenant_id` from `AuthenticationStateProvider` instead of `ITenantProvider` because `ITenantProvider` is backed by `AsyncLocal` and does not reliably flow into a Blazor Server interactive circuit. The component also subscribes to `AuthenticationStateProvider.AuthenticationStateChanged` so in-place tenant switches (e.g., during bUnit tests) re-evaluate `_isRealTenant` and re-render. The subscription is removed in `Dispose`.

### 11.2 Files changed

| File | Change |
|------|--------|
| `src/Students/SchoolCollab.Students.Admin/Services/StudentsApiClient.cs` | Added `IsOverridden` to the client `SubjectDto` record. |
| `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/GradeLevels/GradeLevelWizard.razor` | Updated assigned-subjects list row template; added `_overridingSubjectId` guard; added `OverrideSubjectAsync` and shared `OpenSubjectOverrideDialogCoreAsync`; refactored chip override to use the shared core; set `IsOverridden` when adding a subject; subscribed to `AuthenticationStateProvider.AuthenticationStateChanged`. |
| `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/GradeLevels/GradeLevelWizard.razor.css` | Replaced `justify-content: space-between` on `.assigned-list li` with `gap` and added `.assigned-subject-actions` using `margin-left: auto` so the badge and actions lay out cleanly. |
| `tests/SchoolCollab.Admin.Tests.Unit/GradeLevelWizardTenancyTests.cs` | Updated existing tenancy tests to supply `AuthenticationStateProvider` (with `tenant_id` claims) instead of `ITenantProvider`; all 25 tests pass. |

### 11.3 Verification

- `dotnet build SchoolCollab.sln` — 0 errors, 0 new warnings.
- `dotnet test tests/SchoolCollab.Admin.Tests.Unit` — 25/25 passed.

A Playwright smoke test exercising the full per-row override interaction (open dialog, save override, row updates) is recommended as a follow-up.
