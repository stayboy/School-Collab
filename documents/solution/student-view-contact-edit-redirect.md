# Student contact edit → Edit form + fix `/students/contacts` 404

## Goal

1. **Move contact editing off the student view page.** Today the student
   `Detail.razor` renders `<ContactsEditor OwnerType=Student>` inside the
   Profile card. That's a write surface (Add/Verify/Set primary/Remove)
   wedged into a read-only detail view. Users edit contacts in the
   `Edit.razor` form, where they already edit the student's other
   attributes (name, DOB, gender, guardians).

2. **Fix the 404 in the contacts API client.** The 404 messagebar
   ("Response status code does not indicate success: 404 (Not Found)")
   comes from the `<ContactsEditor>` calling
   `ListContactsAsync` against `/students/contacts?ownerType=...&ownerId=...`
   — a route that 404s. The actual API registers `/contacts` (NOT
   `/students/contacts`) on the contacts route group.

## Why

- **Edit vs. view separation.** The student view is a read-only review
  page. Editing belongs in the edit form. The contact information has
  the same read-only "what's on file" treatment as the address
  (display) and the per-row enrollment history.
- **404 was real.** The 404 isn't a UI cosmetic issue; it's a broken
  API contract. `StudentsApiClient` prefixes the contacts routes with
  `/students/`, but `StudentEndpoints.cs` registers the group as
  `/contacts`. So every list/add/verify/set-primary/remove call 404s
  and shows up as a red messagebar on the only place that uses
  `ContactsEditor` on the student side — the view page.

## Scope

| File | Change | Why |
|------|--------|-----|
| `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/Detail.razor` | **Remove** `<ContactsEditor OwnerType=Student>` from the Profile card. Remove the `FluentDivider class="profile-section-sep">` and the `<h4>Direct contact</h4>` sub-header that surrounded it. | View page is read-only. |
| `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/Edit.razor` | **Add** a "Direct contact" sub-section inside the existing `StudentFormFields` block, hosting `<ContactsEditor OwnerType=Student OwnerId="@Id" ShowSubscription="false" />`. | Edit form already hosts identity + guardians editing. |
| `src/Students/SchoolCollab.Students.Admin/Components/Students/StudentFormFields.razor` | No code change required. The `<ContactsEditor>` will live in the parent `Edit.razor`, not in `StudentFormFields` (which manages the field-level EditForm / DataAnnotationsValidator surface). | The contact list is not part of the validated student model — adding/removing a contact is its own API call, not a property of the form model. |
| `src/Students/SchoolCollab.Students.Admin/Services/StudentsApiClient.cs` | **Fix** the 9 contacts/subscription routes — drop the bogus `/students` prefix so `/contacts`, `/contacts/{id}`, `/contacts/{id}/verify`, `/contacts/{id}/set-primary`, `/contacts/subscribed`, `/contacts/{id}/subscribe`, `/contacts/{id}/unsubscribe` are used. | The real routes are registered in `StudentEndpoints.cs:39` as `app.MapGroup("/contacts")`. |
| `tests/SchoolCollab.Admin.Tests.Unit/StudentDetailSectionsTests.cs` | **Update** the assertions: `Profile_Card_Contains_Student_Own_Contacts_Editor` must be **removed** (the editor is no longer in the Profile card); assert the Direct contact sub-section is GONE. | Test must match the new view. |
| `tests/SchoolCollab.Admin.Tests.Unit/StudentDetailSectionsTests.cs` | **Add** a new test `Detail_Direct_Contact_Section_Is_Removed_From_View_Page` and `Profile_Card_Has_No_Direct_Contact_Subheader`. | Make the change intentional and regression-guarded. |
| `tests/SchoolCollab.Admin.Tests.Unit/EditContactEditorTests.cs` (new) | **Add** a new test class asserting `Edit.razor` renders `<ContactsEditor OwnerType="ContactOwnerType.Student" OwnerId="@Id" ShowSubscription="false" />`. | Confirm the move was completed. |
| `tests/SchoolCollab.Admin.Tests.Unit/StudentsApiClientRoutesTests.cs` (new) | **Add** a new test class asserting the 9 contact/subscription paths in `StudentsApiClient.cs` do NOT contain the broken `/students/contacts` prefix and DO contain the correct `/contacts` prefix. | Catch the 404 in tests, not in the browser. |

## What the user sees

**View page** (`/students/{id}`):
- Profile card now has: Student #, Full name, Date of birth, Gender,
  Status, Created, Updated — and stops. No "Direct contact" sub-section.
- The **Contacts section** still shows one card per linked guardian
  (`<GuardianContactsList>`) — that list is read-only and works.

**Edit page** (`/students/{id}/edit`):
- All existing fields (student number, name, DOB, gender, guardians)
  stay.
- New "Direct contact" sub-section below the validated fields,
  above the action buttons. Hosts the same shared
  `<ContactsEditor OwnerType=Student OwnerId=@Id ShowSubscription=false />`
  that previously sat in the Profile card. Add/Verify/Set primary/Remove
  work — the API 404 is fixed.

## How the 404 manifests today

`Detail.razor` line ~92:
```razor
<ContactsEditor OwnerType="ContactOwnerType.Student"
                 OwnerId="@Id"
                 ShowSubscription="false" />
```

`ContactsEditor.LoadAsync` calls `Api.ListContactsAsync(OwnerType, OwnerId, ct)`.
`StudentsApiClient.ListContactsAsync` (line 705) calls:
```csharp
_http.GetFromJsonAsync<ContactDto[]>($"/students/contacts?ownerType={ownerType}&ownerId={ownerId}", ct)
```

But the actual API route (from `ContactRoutes.cs` mapped under
`MapGroup("/contacts")` in `StudentEndpoints.cs:39`) is `/contacts?...`.

Result: `GetFromJsonAsync` throws `HttpRequestException` with
"Response status code does not indicate success: 404 (Not Found)."
`ContactsEditor.LoadAsync` catches it and sets `_error = ex.Message`,
which renders in the `<FluentMessageBar Intent="MessageIntent.Error">`
right above "No contacts yet."

## Verification

1. `dotnet build` — 0 errors.
2. `dotnet test` (Admin unit) — all pass, including:
   - new `EditContactEditorTests.cs` (~2 tests)
   - new `StudentsApiClientRoutesTests.cs` (~5 tests)
   - updated `StudentDetailSectionsTests.cs` (the `Profile_Card_Contains_Student_Own_Contacts_Editor`
     test is removed; new tests assert the section is gone)
3. Manual smoke:
   - Navigate to `/students/{id}` — Profile card has no Direct contact
     sub-section, no red messagebar.
   - Navigate to `/students/{id}/edit` — see the Direct contact
     sub-section, the existing contact list loads, Add/Verify/Set
     primary/Remove all work.

## Decision log

- **Edit form vs. dedicated contacts page.** A dedicated `/students/{id}/contacts`
  page was considered. Rejected: the contacts are conceptually part of
  the student identity record (along with name/DOB/gender/guardians),
  so editing them in the same `Edit.razor` matches the established
  pattern. A separate page would create a "where do I go to…" gap.
- **In `StudentFormFields` vs. in `Edit.razor`.** `StudentFormFields` is
  the `<EditForm>` host for the **validated** student model. Contacts
  are managed by their own API endpoints (add/verify/set-primary/remove)
  and not by the `UpdateStudentRequest` model, so they don't belong
  inside `<DataAnnotationsValidator>`. Render them in `Edit.razor`
  itself, alongside the form, but outside the EditForm.
- **No `ShowSubscription` on the student.** Students don't subscribe
  to their own contact info — subscriptions are a guardian/teacher
  feature. The student editor passes `ShowSubscription="false"`
  (same as the old view-page usage).
- **Guardians' contacts still edited on `GuardianDetail.razor`.** The
  `<GuardianContactsList>` is read-only on the student view by design
  (it's the spec, and the `GuardianContactsList` component is
  read-only). Guardian contact edits happen on the guardian's own
  detail page, not the student view or the student edit form.
