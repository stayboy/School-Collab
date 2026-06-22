# Shared CodedValueDropdown component

## Context

Assignments (and other operational modules) need to reference coded values such as
subjects, grade levels, or assignment categories. A reusable dropdown component is
needed so every admin module does not re-implement coded-value lookup, API calling,
loading states, and tenancy-aware resolution.

## Decision

Create a shared `CodedValueDropdown` component in `SchoolCollab.Admin.Shared`.

To make the component truly shared, the existing Blazor `CodedValuesApiClient` was
moved from `SchoolCollab.CodedValues.Admin` into `SchoolCollab.Admin.Shared`. All
admin modules already reference `SchoolCollab.Admin.Shared`, so the client is now
available everywhere without adding new cross-module project references.

## Component design

Location: `src/SchoolCollab.Admin.Shared/Components/CodedValueDropdown.razor`

Parameters:

| Parameter | Type | Purpose |
|-----------|------|---------|
| `ParentCode` | `string` (required) | Parent coded value code whose children populate the dropdown |
| `Label` | `string?` | Label shown above the dropdown |
| `Placeholder` | `string?` | Placeholder text when no value is selected |
| `SelectedId` | `Guid?` | Two-way bound selected coded value id |
| `SelectedIdChanged` | `EventCallback<Guid?>` | Emitted when selection changes |
| `Disabled` | `bool` | Disables the dropdown |

Tenancy: the component calls `/coded-values/by-parent?parentCode={code}`. The API
uses `GetCodedValuesByParentHandler`, which resolves each child through
`ICodedValueResolver`. The resolver merges global blueprint values with tenant
overrides automatically, so the dropdown always shows the correct tenant-resolved
labels and disabled state.

## API addition

`CodedValuesApiClient.GetChildrenByParentCodeAsync(string parentCode, CancellationToken)`
was added to call the existing `/by-parent` endpoint using the `parentCode` query
parameter.

## Verification

- `dotnet build src/SchoolCollab.Admin` succeeds.
- Unit tests:
  - `CodedValuesApiClientNotFoundTests.Admin_GetChildrenByParentCodeAsync_CallsCorrectEndpoint`
  - `CodedValueDropdownTests.CodedValueDropdown_LoadsOptionsByParentCode`
  - `CodedValueDropdownTests.CodedValueDropdown_WithSelectedId_MarksOptionSelected`
  - `CodedValueDropdownTests.CodedValueDropdown_RefreshAsync_ReloadsCurrentParentCode`
- Test suites passing:
  - `SchoolCollab.CodedValues.Tests.Unit`: 216 passed
  - `SchoolCollab.Assignments.Tests.Unit`: 41 passed
  - `SchoolCollab.Admin.Tests.Unit`: 3 passed

## Usage example

```razor
@using SchoolCollab.Admin.Shared.Components

<CodedValueDropdown ParentCode="SUBJECTS"
                   Label="Subject *"
                   Placeholder="Select a subject"
                   @bind-SelectedId="_model.SubjectCodedValueId" />
```

The consumer only needs to provide the parent coded value code and bind to the
selected `Guid?` id. The component handles the rest, including tenant-resolved
option loading.

## Current consumers

- Assignment create wizard (`Assignments.Admin/Components/Pages/Assignments/Create.razor`):
  - `ParentCode="SUBJECTS"` bound to `_model.SubjectCodedValueId` (required)
  - `ParentCode="GRADES"` bound to `_model.GradeCodedValueId` (optional)
