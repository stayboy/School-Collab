# Grade Detail — Notification & Delivery Display Redesign

## Goal
Rework the per-grade **Notification & Delivery** card on the grade-level Detail page from the current three-section layout (Effective grid + inline override editor + read-only tenant default) into a **single settings grid** with per-row Edit / Reset actions, edited via a **dialog** that can target either the **tenant-global default** or the **per-grade override**.

## Current State
- `Detail.razor` (lines 157–164) hosts a `Notification & Delivery` card with `<GradeNotificationPolicyEditor GradeLevelId="@Id" />`.
- `GradeNotificationPolicyEditor.razor` renders three sections:
  1. Effective (merged) policy grid — Setting | Effective value | Source
  2. Grade Overrides — a list of checkboxes/inputs + "Save overrides" button
  3. Tenant Global Default — read-only list
- It loads tenant default (`NotificationPolicyApiClient.GetAsync`) + grade override (`StudentsApiClient.GetGradeNotificationPolicyAsync`) and saves grade overrides via `UpsertGradeNotificationPolicyAsync`.
- `NotificationPolicyApiClient.UpsertAsync` exists but is unused in the UI (tenant default is read-only here; Settings page doesn't expose it either).

## Requirements → Design
| Requirement | Design |
|---|---|
| Single grid | One `FluentDataGrid`, one row per setting |
| Col 1 name / global value / grade override / actions | Drop 3-section layout for a 4-column grid |
| Actions: Edit, Clear/Reset | Edit → dialog; Reset → clear per-grade override (inherit global) |
| Edit opens a dialog for that setting's value | New `NotificationPolicyFieldEditDialog` |
| Dialog offers global-settings option + grade override | Scope selector (Global vs. This grade) |

## Changes

### 1. Rework `GradeNotificationPolicyEditor.razor` (keep filename — keeps `Detail.razor` wiring + `Detail_NotificationEditor_IsWired` test intact)
- Single 4-column `FluentDataGrid` per setting:
  - **Setting** — label
  - **Global settings** — tenant-default value (muted "Not set" when null)
  - **Grade override** — override value; "Inherit global" badge when null (preserves source semantics)
  - **Actions** — `Edit` button + `Reset` button
- `FieldDef` array (Key, Label, Kind) covering the 8 settings: preferred channels, blocked channels, max notifications, max reminders, reminder interval (hours), link validity (days), sendout time of day, sendout interval (minutes).
- Inject `IDialogService`. Keep `LoadAsync()` reading tenant + grade policies.
- **Edit** → open `NotificationPolicyFieldEditDialog` via `ShowShellDialogAsync<...>` with field metadata + current global/grade values + grade id + the raw policies (so the dialog can preserve other fields). Reload on non-null result.
- **Reset** → build `UpsertGradeNotificationPolicyRequest` from current grade override with only that field nulled → `Api.UpsertGradeNotificationPolicyAsync` → reload.

### 2. New `NotificationPolicyFieldEditDialog.razor` (Components/Students/)
- `@inherits DialogShellBase<EditModel, EditResult>` (same pattern as `TopicEditDialog` + `DialogShellFooter`).
- **EditModel**: FieldKey, Label, Kind, GradeId, current global value, current grade value, `Scope` enum, source tenant/grade policies.
- **Markup**: scope selector (Global vs. This grade) + kind-specific editor (channel checkboxes / `FluentNumberField` / time input) + `DialogShellFooter`.
- **SubmitAsync**:
  - Grade scope → `UpsertGradeNotificationPolicyAsync` with current grade override + edited field replaced.
  - Global scope → `SettingsApi.UpsertAsync` with current tenant default + edited field replaced.
- Returns `EditResult` so the editor reloads.

### 3. No backend / API changes
Reuses existing Get/Upsert for both tenant-global and per-grade policies. `NotificationPolicyApiClient.UpsertAsync` already supports the new global-edit path.

### 4. Tests
- **Rewrite** `GradeNotificationPolicyEditorTests.cs` for the new grid (columns, "Inherit global" badge, direct values) + reset behavior (verify a PUT nulls the field and preserves others). Existing tests reference old markup ("Effective Policy", "Save overrides", `.override-row`) and must be replaced.
- **New** `NotificationPolicyFieldEditDialogTests.cs`: render dialog, select scope, assert correct API call and that only the targeted field changes.
- Keep `Detail_NotificationEditor_IsWired` passing (filename/card unchanged).

### 5. Verification
`dotnet test tests/SchoolCollab.Admin.Tests.Unit --filter GradeNotificationPolicyEditor` + the Detail wiring test.

## Assumption
**Clear/Reset** resets the *per-grade override* field back to "inherit global". Editing/clearing the *global default* is done via the dialog's Global scope.

## Status
**Plan saved for review — implementation NOT started.** Awaiting approval of this plan before writing code.
