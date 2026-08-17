# Plan: Contact Edit / Delete with Reason + Audit Log

**Date:** 2026-08-17  
**Status:** SPEC + IMPLEMENTATION  
**Replaces:** `docs/plans/2026-08-17-contacts-editor-inline-edit.md` (inline edit is rejected because it cannot collect a reason or persist an audit row).  
**Scope:**
- `src/SchoolCollab.Admin.Shared/Components/ContactsEditor.razor` + `.css`
- `src/SchoolCollab.Admin.Shared/Components/ContactChangeDialog.razor` + `.css` + model/result (new)
- `src/Students/SchoolCollab.Students.Core` domain / DTOs / CQRS / migration
- `src/Students/SchoolCollab.Students.Api/Endpoints/ContactRoutes.cs`
- `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs`
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Detail.razor` + `.css` (contact-history viewer)
- Unit tests

---

## 1. Goal

Every contact **edit** and **delete** in `ContactsEditor` must:

1. Collect a required **reason** from the operator.
2. Persist an append-only **audit row** recording the change, reason, actor, and before/after values.
3. Surface the history on the **student detail page** so administrators can see who changed what and why.

The UX is a **modal section switch** (dialog) — not inline — because a dialog gives room for the reason field, confirmation summary, and clear Save/Cancel affordances, and it mirrors the established `WithdrawEnrollmentDialog` / `StudentTransferDialog` reason pattern.

---

## 2. Why inline was rejected

Inline edit would have to squeeze a reason text area into a list row, which is cramped and has no good place for a confirmation summary. More importantly, the audit requirement means the UI must **pause the user** to collect a reason before the mutation is sent. A dialog is the repo's established pattern for this exact flow.

---

## 3. Backend audit model

### 3.1 New domain entity: `ContactAuditEntry`

`src/Students/SchoolCollab.Students.Core/Domain/ContactAuditEntry.cs`

```csharp
public enum ContactChangeKind { Updated = 0, Deleted = 1 }

public sealed class ContactAuditEntry : ITenantEntity, IEntity, IAuditableEntity
{
    private ContactAuditEntry() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ContactId { get; private set; }
    public ContactOwnerType OwnerType { get; private set; }
    public Guid OwnerId { get; private set; }
    public ContactChangeKind ChangeKind { get; private set; }

    // Before values (snapshot at the moment the change started)
    public ContactChannel PreviousChannel { get; private set; }
    public string PreviousValue { get; private set; } = default!;
    public string? PreviousLabel { get; private set; }
    public string? PreviousCountryCode { get; private set; }

    // After values (meaningful for Update; null/empty for Delete)
    public ContactChannel? NewChannel { get; private set; }
    public string? NewValue { get; private set; }
    public string? NewLabel { get; private set; }
    public string? NewCountryCode { get; private set; }

    public string Reason { get; private set; } = default!;
    public string ActorId { get; private set; } = default!;
    public string ActorDisplayName { get; private set; } = default!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ContactAuditEntry Create(...) { ... }
}
```

- Strict tenant-scoped (table `contact_audit_entries`).
- Append-only: never updated or deleted.
- Records both before and after values so the log is self-contained even if the original `Contact` row is later mutated.

### 3.2 EF configuration

`src/Students/SchoolCollab.Students.Core/Data/Configurations/ContactAuditEntryConfiguration.cs`

- Maps `contact_audit_entries`.
- Configures audit timestamps, tenant, indexes on `(TenantId, ContactId)` and `(TenantId, OwnerType, OwnerId, OccurredAt)`.
- `Reason` required, max 1000 chars.
- `ChangeKind` stored as string (max 30).

### 3.3 DbContext + migration

- Add `DbSet<ContactAuditEntry> ContactAuditEntries` to `StudentsDbContext`.
- Apply `ContactAuditEntryConfiguration` in `OnModelCreating`.
- Generate migration `AddContactAuditEntries`.

---

## 4. Commands + handlers

### 4.1 UpdateContact

```csharp
public sealed record UpdateContact(
    Guid Id,
    string Value,
    string? Label,
    string Reason) : ICommand
{
    public string? CountryCode { get; init; }
}
```

`UpdateContactHandler`:

1. Load the contact.
2. Snapshot previous values.
3. Call `contact.Update(...)`.
4. Record audit via new `ContactAuditor`.
5. Save.

> **Reason is required.** `Reason` is a non-nullable `string` on the command and
> on the audit entity; the handler records the audit row **before** mutating the
> contact so the `previous-*` columns capture the pre-change values.

### 4.2 DeleteContact

```csharp
public sealed record DeleteContact(
    Guid Id,
    string Reason) : ICommand;
```

`DeleteContactHandler`:

1. Load the contact.
2. Snapshot previous values.
3. Call `contact.SoftDelete()`.
4. Record audit via `ContactAuditor`.
5. Save.

### 4.3 ContactAuditor service

`src/Students/SchoolCollab.Students.Core/Services/ContactAuditor.cs`

Mirrors `StudentTransferAuditor`: takes `IActorAccessor`, writes the audit row into the supplied `StudentsDbContext` so the row is persisted in the same transaction as the mutation.

---

## 5. API + client contract

### 5.1 Routes

`src/Students/SchoolCollab.Students.Api/Endpoints/ContactRoutes.cs`

- `PUT /contacts/{id}` body becomes:
  ```csharp
  internal record UpdateContactRequest(string Value, string? Label, string Reason)
  {
      public string? CountryCode { get; init; }
  }
  ```
- `DELETE /contacts/{id}?reason=...` — reason passed as query parameter (a body on DELETE is awkward and often dropped by proxies; query string is simple and explicit).

> **Reason is enforced at the route.** Both `PUT` and `DELETE` return `400 Bad
> Request` ("A reason is required.") when `reason` is missing or whitespace. The
> non-nullable `string Reason` on the command/DTO declares the contract; the
> route guard is the runtime safety net (JSON/query binding can still produce
> null for a non-nullable reference type).

### 5.2 Client contract

`src/Students/SchoolCollab.Students.Core/Contracts/IContactsClient.cs`

```csharp
Task UpdateContactAsync(Guid id, UpdateContactRequest req, CancellationToken ct = default);
Task DeleteContactAsync(Guid id, string reason, CancellationToken ct = default);
```

`UpdateContactRequest` gains `string Reason` (non-nullable). `DeleteContactAsync`
drops the `= null` default — callers must supply a reason.

### 5.3 History read endpoint

New query + handler:

```csharp
public sealed record ListContactAuditEntries(
    Guid? ContactId,
    ContactOwnerType? OwnerType,
    Guid? OwnerId,
    int Skip,
    int Take) : IQuery<ContactAuditEntryDto[]>;
```

Endpoint: `GET /contacts/audit?contactId=...&ownerType=...&ownerId=...&skip=...&take=...`

DTO:

```csharp
public sealed record ContactAuditEntryDto(
    Guid Id,
    Guid ContactId,
    string ChangeKind,
    string? PreviousChannel,
    string PreviousValue,
    string? PreviousLabel,
    string? PreviousCountryCode,
    string? NewChannel,
    string? NewValue,
    string? NewLabel,
    string? NewCountryCode,
    string Reason,
    string ActorId,
    string ActorDisplayName,
    DateTimeOffset OccurredAt);
```

---

## 6. UI

### 6.1 New shared dialog: `ContactChangeDialog`

`src/SchoolCollab.Admin.Shared/Components/ContactChangeDialog.razor` + `.razor.css`

- Inherits `DialogShellBase<ContactChangeModel, ContactChangeResult>`.
- Two modes:
  - **Edit**: renders the contact fields (channel dropdown, conditional country-code dropdown, value, label) + required reason text area.
  - **Delete**: renders a read-only summary of the contact + required reason text area + warning message.
- Footer: Cancel + Save/Delete.
- Model:
  ```csharp
  public sealed record ContactChangeModel(
      Guid ContactId,
      ContactChannel Channel,
      string Value,
      string? Label,
      string? CountryCode,
      ContactChangeMode Mode);

  public enum ContactChangeMode { Edit, Delete }

  public sealed record ContactChangeResult(
      ContactChannel? Channel,
      string? Value,
      string? Label,
      string? CountryCode,
      string Reason,
      bool IsDeleted);
  ```

### 6.2 ContactsEditor changes

`src/SchoolCollab.Admin.Shared/Components/ContactsEditor.razor`

- Replace the per-row Remove icon with **Edit** + **Remove** lightweight buttons (or keep Remove and add Edit).
- Clicking Edit or Remove opens `ContactChangeDialog` via `DialogService.ShowShellDialogAsync`.
- **Live mode:**
  - On confirmed edit: call `IContactsClient.UpdateContactAsync(..., reason)`.
  - On confirmed delete: call `IContactsClient.DeleteContactAsync(id, reason)`.
  - Then reload the contact list.
- **Buffered mode:**
  - On confirmed edit: mutate the in-memory `ContactModel` and raise `ContactsChanged`.
  - On confirmed delete: remove from the in-memory list and raise `ContactsChanged`.
  - No API call; no reason persisted yet (the parent flushes on save; the audit is created server-side during that flush).

> **Open question:** Should Buffered-mode edits/deletes also carry a reason and get audited? The current design says no — Buffered mode is for drafting contacts before the owner exists; the audit is written when the parent saves. If the user wants reasons for Buffered changes too, we can add a `Reason` field to `ContactModel` and include it in `ContactDraftRequest`, then have the server-side create/update handlers write audit rows. **Decision:** defer; only Live mode collects and audits reasons. The plan notes this explicitly so it can be revisited.

### 6.3 Student detail page: contact history viewer

`src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Detail.razor` + `.css`

Add a new **"Contact history"** section below the **Guardians** section (or after Direct contact on Edit; on Detail it should be near the guardians/contact surfaces).

- Header: "Contact history" with a count badge.
- Loads `ContactAuditEntryDto[]` for `OwnerType = Student, OwnerId = Id`.
- Renders a compact timeline/list:
  - Actor display name + occurred-at timestamp.
  - Change kind (Updated / Deleted).
  - Before → after value/channel/label.
  - Reason.
- Empty state: "No contact changes recorded yet."

Implementation options:
- Inline in `Detail.razor` (simplest; follows existing section pattern).
- New `StudentContactHistory.razor` component (cleaner if reused). **Decision:** inline for now; extract if reuse appears.

---

## 7. Tests

### 7.1 Students.Core unit tests

- `UpdateContactHandlerTests`: update records an audit row with before/after values + reason + actor.
- `DeleteContactHandlerTests`: soft-delete records an audit row with before values + reason + actor.
- `ListContactAuditEntriesHandlerTests`: filters by contact id / owner type + id, orders by `OccurredAt` descending.

### 7.2 Admin.Shared bUnit tests

- `ContactsEditorTests`:
  - Clicking Edit opens `ContactChangeDialog`.
  - Clicking Delete opens `ContactChangeDialog`.
  - Live mode: dialog reason flows to `IContactsClient.UpdateContactAsync` / `DeleteContactAsync`.
  - Buffered mode: edit/delete mutates the in-memory list (no API call).

> **STATUS: IMPLEMENTED (2026-08-17).** The `ContactsEditorTests` fake now
> records update/delete calls and the supplied reason. Six tests cover the
> spec: `LiveEdit_ClickingEdit_OpensContactChangeDialog`,
> `LiveDelete_ClickingDelete_OpensContactChangeDialog`,
> `LiveEdit_DialogReason_FlowsToUpdateContactAsync`,
> `LiveDelete_DialogReason_FlowsToDeleteContactAsync`, and Buffered edit/delete
> list-mutation tests. The dialog service is mocked (the
> `GradeNotificationPolicyEditorTests` pattern) so the reason-collection
> contract is exercised deterministically. The `ContactChangeDialog`'s own
> form-required-reason validation is covered by the dialog's `SubmitAsync`
> guard, not by these editor tests.

### 7.3 Students.Application tests

- `StudentDetailSectionsTests`:
  - Detail.razor renders the "Contact history" section.
  - Calls `IContactsClient` history endpoint for the student.

> **STATUS: IMPLEMENTED (2026-08-17).** Four source-level tests were added to
> `StudentDetailSectionsTests` (following the existing source-assertion pattern
> for Detail.razor): section + count badge, placement below Guardians,
> owner-scoped load (`ownerType: Student`, `ownerId: Id`), and the
> loading/error/empty states.

### 7.4 Students Playwright smoke tests

- `ContactAuditSmokeTests` (in `SchoolCollab.Students.Tests.Playwright`):
  - `StudentDetail_ShowsContactHistorySection` — the Detail page renders the
    "Contact history" heading.
  - `ContactEdit_OpensReasonDialog_AndPersistsAudit` — on the Edit page, add a
    contact via the UI, open the `ContactChangeDialog`, verify the reason is
    required (submit without it stays open), confirm with a reason, then verify
    the audit entry appears in the Detail page's Contact history.

> Requires the full AppHost running + a seeded student (TestAuth in dev, no
> login). Contacts are not seeded, so the round-trip test creates its own
> contact first. FluentUI dialog selectors may need adjustment on first run.

---

## 8. Implementation order

1. Backend domain + configuration + DbContext wiring.
2. EF migration (stop running API first).
3. Update commands / handlers / auditor.
4. DTO + query + endpoint for history read.
5. Update `IContactsClient` + `StudentsApiClient`.
6. Create `ContactChangeDialog`.
7. Update `ContactsEditor` to use the dialog.
8. Add history viewer to `Detail.razor`.
9. Add/update tests.
10. Build + run tests.

---

## 9. Open questions / decisions

1. **Buffered-mode audit:** deferred — only Live mode writes audit rows today. Revisit if the user wants draft-stage reasons.
2. **Delete endpoint shape:** `DELETE /contacts/{id}?reason=...` (query string) to avoid a request body.
3. **History viewer placement:** inline in `Detail.razor` below the Guardians section.
4. **Soft-deleted contacts in history:** audit rows reference contacts by id; the list can still display them even if the contact is soft-deleted.
5. **Reason is required (tightened 2026-08-17):** `Reason` is non-nullable across the stack — `UpdateContact`/`DeleteContact` commands, `ContactAuditEntry` entity, `ContactAuditor.Record`, `UpdateContactRequest` DTO (both the `Contracts` and `Endpoints` copies), and `IContactsClient.DeleteContactAsync` (the `= null` default was removed). The EF config + migration enforce `NOT NULL` on `contact_audit_entries.reason`, and both API routes return `400` when the reason is missing/whitespace.
6. **Outstanding review gaps (2026-08-17):** §7.2 and §7.3 tests were **added** —
   the only remaining open item is that the generated migration is **not yet
   applied** to the database (apply with `dotnet ef database update` once the
   API is stopped).
