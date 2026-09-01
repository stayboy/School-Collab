# Implementation Plan — Draft Period Delete (round r1)

> **Spec:** [documents/specs/period-draft-delete.md](../specs/period-draft-delete.md) (FR-D1..D12, NFR-D1..D3, AC-D1..D10)
> **Round:** r1 · **Status:** OPEN · **Implementer:** worker
> **Mandatory pre-reads (worker):** `AGENTS.md`, `.github/copilot/rules/dotnet-best-practices.md` (+ backing skill `.github/skills/dotnet-best-practices/SKILL.md`). Honored skills: `dialog-ui`, `blazor-css-isolation`, `fluentui-icons`.
> **Build rule:** `dotnet build SchoolCollab.sln` after **every** code change; fix errors before continuing.

---

## 0. Verified code facts (orchestrator checked 2026-08-31)

All plan assumptions were re-verified against source before planning:

| Assumption | Evidence |
| --- | --- |
| `Period` lifecycle guards are Draft-only, `InvalidOperationException`-style internally | `src/Students/SchoolCollab.Students.Core/Domain/Period.cs` (`Update`, `Activate`, `Complete`) |
| `SetNextPeriod` exists but has **no production call sites**; `NextPeriodId` has a private setter, no clear method | `Period.cs`; grep across `src/` |
| `PeriodDeletedEvent` does not exist yet; `PeriodCompletedEvent(Guid, string)` is the observability parity target | `src/Students/SchoolCollab.Students.Core/Domain/Events/DomainEvents.cs` |
| `ParentPeriodId` FK is `OnDelete(DeleteBehavior.Cascade)`; `NextPeriodId` is a plain property with **no FK**; membership FKs are RESTRICT | `src/Students/SchoolCollab.Students.Core/Data/Configurations/PeriodConfiguration.cs` (~l.47–54) |
| `RepositoryBase<TEntity,TContext>` **already implements** `public virtual Task DeleteAsync(TEntity, CancellationToken)` (`Set.Remove` + one `SaveChanges`) — `IPeriodRepository` simply does not expose it | `src/SchoolCollab.Core/Data/Repositories/RepositoryBase.cs` |
| Named domain exceptions live one-per-file in `Domain/Exceptions/`; `PeriodGuardException` is mapped to 422 in routes at `PeriodRoutes.cs:142` | `src/Students/SchoolCollab.Students.Core/Domain/Exceptions/PeriodGuardException.cs`, `src/Students/SchoolCollab.Students.Api/Endpoints/PeriodRoutes.cs` |
| Commands are `sealed record ... : ICommand`; handlers `sealed class ... : ICommandHandler<T>`, constructor-injected, discovered by the existing Scrutor assembly scan (no DI edits) | `ActivatePeriod/ActivatePeriod.cs`, `ActivatePeriodHandler.cs`; `SchoolCollab.Students.Core/Extensions.cs` (~l.103–112) |
| `IIntegrationEventPublisher` pattern exists but is **not needed** here (FR-D7 asks only for a domain event) | `CompletePeriodHandler.cs` |
| Routes are grouped on a `RouteGroupBuilder` (`MapPeriodRoutes`); 204 = `Results.NoContent()`, 404 = `Results.NotFound()`, 422 = `Results.Json(new { ex.Message }, statusCode: 422)` | `PeriodRoutes.cs` (PUT/activate/complete groups) |
| Client wrappers throw `HttpRequestException(..., statusCode: response.StatusCode)` with the response body inline | `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs` (~l.1347–1384, `ActivatePeriodAsync` = l.1360) |
| Grid: `RowActionsUseMenuService="false"` ⇒ single-action rows render **labeled FluentButtons** (keyboard reachable); `RowAction.Callback(label, cb, FluentIcons.Delete)` precedent exists; `_draftSubPeriodCounts` already memoized | `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Periods.razor` (l.24–25, 85–93, 254–291); icon precedent `Settings/.../CodedValues/Index.razor:435` |
| `Edit.razor` hosts `SubPeriodsSection` + `PeriodForm`; currently has **no** `IDialogService` injection and **no** danger zone | `Components/Pages/Periods/Edit.razor` |
| `SubPeriodsSection` rows render Edit/Cancel buttons in a `subperiods-actions` cell; add a Delete button beside Edit | `Components/Pages/Periods/SubPeriodsSection.razor` |
| Handler test scope: EF **InMemory** (`StudentsTestScope` → `UseInMemoryDatabase`) — cascade works there **only for tracked dependents**; the guard must load sub-periods (it does, via `GetSubPeriodsAsync`, tracked) so client-cascade removes them | `tests/SchoolCollab.Students.Tests.Unit/StudentsTestScope.cs` |
| Integration tests run a **real Postgres** (Testcontainers) — true DB-level `ON DELETE CASCADE` verifiable | `tests/SchoolCollab.Students.Tests.Integration/ApiFactory.cs`, `PeriodWizardOpenTermGateTests.cs` (TRUNCATE + `x-tenant-id` header precedents) |
| bUnit harness: `BunitContext` + `ScriptedHandler` + `FakeAuth` + mocked `IDialogService` | `tests/SchoolCollab.Admin.Tests.Unit/PeriodEditPageTests.cs`, `PeriodsLandingGridTests.cs`; `tests/SchoolCollab.Students.Tests.Unit/PeriodFormSubPeriodsSectionTests.cs` |
| `PeriodDto.Status` is a **string** ("Draft"/"Active"/"Completed"/"Archived"), `Division` string | `src/Students/SchoolCollab.Students.Core/DTOs/PeriodDto.cs` |

**Planned deviation from spec §8 wording (documented, not drift):** spec §8 says "no new repository method". The persistence surface necessarily changes by **exposing** two things on `IPeriodRepository`: (a) the *already-implemented* `RepositoryBase.DeleteAsync` (zero new logic — interface signature only), and (b) one small tracked query `GetDraftPeriodsLinkedToAsync` (FR-D6 housekeeping). Leaking `StudentsDbContext` into the handler would violate repo layering, so this is the minimal layering-correct interpretation. **No** per-row sub-period removal is implemented anywhere (FR-D3).

---

## 1. Domain — `Period.Delete()` + event (FR-D1, FR-D2, FR-D7)

**File:** `src/Students/SchoolCollab.Students.Core/Domain/Period.cs`

Add the public domain method after `Archive()`:

```csharp
/// <summary>
/// Guards this period as deletable (Draft-only, period-draft-delete.md FR-D2):
/// Active/Completed/Archived periods are referenced by operational data and
/// follow Complete -> Archive instead. Raises <see cref="PeriodDeletedEvent"/>
/// (FR-D7) for observability parity with PeriodCompletedEvent. Unlike
/// Activate/Complete there is no idempotent early-return: a deleted row is gone.
/// </summary>
public void Delete()
{
    if (Status != PeriodStatus.Draft)
        throw new PeriodNotDeletableException(
            $"Period '{Name}' cannot be deleted while its status is {Status}. " +
            "Only Draft periods can be deleted.");

    _domainEvents.Add(new PeriodDeletedEvent(Id, Name));
}
```

**File:** `src/Students/SchoolCollab.Students.Core/Domain/Events/DomainEvents.cs`

Add next to `PeriodCompletedEvent`:

```csharp
public sealed record PeriodDeletedEvent(Guid PeriodId, string Name) : IDomainEvent;
```

**Domain method for FR-D6** (needed to clear surviving links; `NextPeriodId` has a private setter):

```csharp
/// <summary>Defensive housekeeping (FR-D6): clears a dangling NextPeriodId link
/// left behind when the linked period was hard-deleted. No domain event — silent
/// hygiene; the link is future-proofing only (no handler sets it today).</summary>
public void ClearNextPeriod()
{
    if (NextPeriodId is null) return;
    NextPeriodId = null;
    UpdatedAt = DateTimeOffset.UtcNow;
}
```

## 2. `PeriodNotDeletableException` (FR-D2)

**New file:** `src/Students/SchoolCollab.Students.Core/Domain/Exceptions/PeriodNotDeletableException.cs` — mirror `PeriodGuardException.cs` exactly (sealed, single-message ctor, XML doc citing FR-D2/FR-D3 and the 422 mapping, namespace `SchoolCollab.Students.Core.Domain.Exceptions`). Do **not** add it to `DomainExceptions.cs`.

## 3. CQRS command + handler (FR-D1, FR-D3, FR-D4, FR-D5, NFR-D1, NFR-D2)

**New folder** `src/Students/SchoolCollab.Students.Core/CQRS/Periods/Commands/DeletePeriod/` with:

**`DeletePeriod.cs`**

```csharp
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.DeletePeriod;

public sealed record DeletePeriod(Guid Id) : ICommand;
```

**`DeletePeriodHandler.cs`** — style parity with `CompletePeriodHandler`:

```csharp
public sealed class DeletePeriodHandler(
    IPeriodRepository repository,
    HybridCache cache,
    ILogger<DeletePeriodHandler> logger) : ICommandHandler<DeletePeriod>
```

Flow (all pre-mutation checks first — NFR-D1 zero-partial-deletion):

1. `var period = await repository.GetAsync(command.Id, ct) ?? throw new PeriodNotFoundException(command.Id);`
   — tenant query filter makes other tenants' and already-deleted rows return null → 404 (FR-D5, NFR-D2, AC-D5).
2. `period.Delete();` — domain Draft-only guard (FR-D2) throws `PeriodNotDeletableException` (422) **before any reads/mutations** (AC-D1).
3. **FR-D3 sub-period guard (years only):** if `period.ParentPeriodId is null`, `var subs = await repository.GetSubPeriodsAsync(command.Id, ct);` and find the first `sp => sp.Status != PeriodStatus.Draft`; if found throw
   `new PeriodNotDeletableException($"Cannot delete academic year '{period.Name}': sub-period '{blocker.Name}' is {blocker.Status} and is still in use. A year can only be deleted while every sub-period is Draft.")`
   — aborts **before any removal** and names the blocking row (AC-D3). Do **not** loop-Remove sub-periods: this load also serves the client-cascade (see repo step) and the DB `ON DELETE CASCADE` handles the rest.
4. **FR-D6 housekeeping (SHOULD):** `foreach (var linked in await repository.GetDraftPeriodsLinkedToAsync(command.Id, ct)) linked.ClearNextPeriod();` — loaded tracked, nulled, persisted by the **same** SaveChanges below (non-Draft links untouched per EC-2).
5. **Single removal (NFR-D1):**

```csharp
try
{
    await repository.DeleteAsync(period, cancellationToken);
}
catch (DbUpdateConcurrencyException)
{
    throw new ConcurrencyException("Period", period.Id);
}
```

6. `await cache.RemoveByTagAsync("students", cancellationToken);` then `period.ClearDomainEvents();` and an informational log `"Period {Id} deleted"`. Constructor: no `IIntegrationEventPublisher` (FR-D7 is domain-event-only; no outbox contract added).

Scrutor discovers the handler automatically via `ICommandHandler<>` assembly scan — **no DI registration changes**.

## 4. Repository surface (minimal)

**File:** `src/Students/SchoolCollab.Students.Core/Data/Repositories/IPeriodRepository.cs`

1. Expose the inherited `RepositoryBase.DeleteAsync` (no implementation code — the base class already does `Set.Remove(entity); await Db.SaveChangesAsync();`):
   `Task DeleteAsync(Period period, CancellationToken cancellationToken = default);`
2. Add the FR-D6 query (tracked + tenant-filtered by the global filter; deliberately **not** `ExecuteUpdateAsync`, which the InMemory test provider does not support):

```csharp
/// <summary>
/// FR-D6 defensive housekeeping: surviving DRAFT periods whose NextPeriodId
/// points at the given (just-deleted) period id. Tracked, so clearing the link
/// and the delete itself persist in one SaveChanges (NFR-D1). Non-Draft links
/// stay untouched (EC-2: historical records).
/// </summary>
Task<Period[]> GetDraftPeriodsLinkedToAsync(Guid nextPeriodId, CancellationToken cancellationToken = default);
```

**File:** `.../Repositories/PeriodRepository.cs` — implement only (2):

```csharp
public async Task<Period[]> GetDraftPeriodsLinkedToAsync(Guid nextPeriodId, CancellationToken cancellationToken = default) =>
    await Db.Periods
        .Where(p => p.NextPeriodId == nextPeriodId && p.Status == PeriodStatus.Draft)
        .ToArrayAsync(cancellationToken);
```

## 5. API route (FR-D8) — 204 / 404 / 422, **no 409**

**File:** `src/Students/SchoolCollab.Students.Api/Endpoints/PeriodRoutes.cs`

Inside `MapPeriodRoutes`, after the `/periods/{id:guid}/complete` group, add `using SchoolCollab.Students.Core.CQRS.Periods.Commands.DeletePeriod;` and:

```csharp
group.MapDelete("/periods/{id:guid}", async (
    Guid id,
    [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<DeletePeriod> handler,
    CancellationToken ct) =>
{
    try
    {
        await handler.HandleAsync(new DeletePeriod(id), ct);
        return Results.NoContent();
    }
    catch (PeriodNotFoundException)
    {
        return Results.NotFound();
    }
    catch (PeriodNotDeletableException ex)
    {
        return Results.Json(new { ex.Message }, statusCode: 422);
    }
    catch (ConcurrencyException)
    {
        // EC-3 / NFR-D2: delete-after-delete or concurrent-edit delete is an
        // idempotent 404 — delete routes never return 409.
        return Results.NotFound();
    }
});
```

(FR-D8's "or belongs to another tenant" 404 comes free from the tenant query filter in `GetAsync`.)

## 6. Client wrapper (FR-D11)

**File:** `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs`

Insert directly **after `CompletePeriodAsync`** (~l.1384), mirroring `ActivatePeriodAsync` verbatim in shape:

```csharp
public async Task DeletePeriodAsync(Guid id, CancellationToken ct = default)
{
    var response = await _http.DeleteAsync($"/students/periods/{id}", ct);
    if (!response.IsSuccessStatusCode)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"DeletePeriod failed ({(int)response.StatusCode} {response.StatusCode}): {body}",
            inner: null,
            statusCode: response.StatusCode);
    }
}
```

## 7. Shared confirmation wording (FR-D9 baseline)

**New file:** `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodDeletePrompts.cs`
(namespace `SchoolCollab.Students.Application.Components.Pages.Periods`).

```csharp
/// <summary>
/// Single source of the Draft-period delete confirmation wording shared by the
/// Periods landing grid, the edit page danger zone, and SubPeriodsSection rows
/// (period-draft-delete.md FR-D9/D10/D12 — "same confirmation wording").
/// </summary>
public static class PeriodDeletePrompts
{
    public static string YearMessage(string name, int draftSubPeriodCount) =>
        $"Delete \"{name}\"? This permanently deletes the academic year and its " +
        $"{draftSubPeriodCount} draft sub-period{(draftSubPeriodCount == 1 ? "" : "s")} " +
        "that go with it. This cannot be undone.";

    public static string SubPeriodMessage(string name, string kindLabel) =>
        $"Delete \"{name}\"? This permanently deletes this {kindLabel}. " +
        "This cannot be undone.";
}
```

*Placement note (for reviewer):* feature-local wording builder shared by three components in the **same** folder — kept out of `SchoolCollab.Admin.Shared/Constants/` because that folder is for cross-feature UI constants (repo `shared-constants.md` scope); no enum/magic-string/flag concerns. Callers pass the kind label with the existing `GetKindLabel` pattern (grid + SubPeriodsSection each already own one).

Confirmation dialogs use `DialogService.ShowConfirmationAsync(message, "Delete", "Cancel")` and the `FluentIcons.Delete` icon everywhere (icon precedent: `CodedValues/Index.razor:435`), with `Title="Delete"` on row buttons per the repo's accessible-Title convention.

## 8. UI wiring

### 8.1 Landing grid — `Components/Pages/Periods/Periods.razor` (FR-D9, FR-D12, NFR-D3)

- `BuildPeriodActions`: inside the `period.Status == "Draft"` branch (both the `guarded` and plain-Activate paths), append `actions.Add(RowAction.Callback("Delete", () => OnDeleteAsync(period.Id), FluentIcons.Delete));`. Because `RowActionsUseMenuService="false"`, this renders a labeled, enabled `FluentButton` with `Title="Delete"` — a real tab stop (NFR-D3, AC-D9). Non-Draft statuses never reach the branch → no Delete action (AC-D8).
- New `OnDeleteAsync(Guid id)` beside `OnActivateAsync`, following its exact skeleton:
  1. guard `_disposed || _items is null`;
  2. resolve the row locally (`_items!.First(p => p.Id == id)`);
  3. message = `p.ParentPeriodId` is null → `PeriodDeletePrompts.YearMessage(p.Name, GetDraftSubPeriodCount(p.Id))` (memoized count, FR-D9); else → `PeriodDeletePrompts.SubPeriodMessage(p.Name, GetKindLabel(p))`;
  4. `ShowConfirmationAsync(message, "Delete", "Cancel")`; cancelled → return;
  5. `await Api.DeletePeriodAsync(id)`; `_error = null; await ReloadAsync();`
  6. `catch (Exception ex)` → log + `_error = ex.Message; StateHasChanged();` (standard error bar; EC-3 404s surface this way too).
- Note in-code: `GetDraftSubPeriodCount` counts only **Draft** subs — exactly the FR-D9 confirmation semantics.

### 8.2 Edit page danger zone — `Components/Pages/Periods/Edit.razor` (FR-D10) + `Edit.razor.css`

- Add `@inject IDialogService DialogService` and `@inject ILogger<Edit> Logger` is already present; inject nothing else.
- Render **after** `<PeriodForm />`, gated to Draft rows only:

```razor
@if (_period is { Status: "Draft" })
{
    <hr class="period-edit-separator" aria-hidden="true" />
    <section class="period-edit-danger" aria-label="Danger zone">
        <h3>Danger zone</h3>
        <p class="form-hint">Deleting a draft period cannot be undone.</p>
        <FluentButton Appearance="Appearance.Stealth"
                      IconStart="@FluentIcons.Delete"
                      Title="Delete period"
                      OnClick="OnDeleteAsync">Delete…</FluentButton>
    </section>
}
```

- `OnDeleteAsync`: must not run before `_period` is loaded (`_period is null → return`); message identical to the grid — for a top-level year fetch sub-periods on demand (`Api.ListSubPeriodsAsync(Id)`, count `Status == "Draft"`) so the FR-D9 count matches the grid without duplicating its memo maps; for a sub-period use `SubPeriodMessage(_period.Name, "Term"/"Semester" via _division)`. On confirm → `await Api.DeletePeriodAsync(Id)` (422/other failures → `_loadError`-style error bar; 404 → also treat as already-gone and navigate) → `Nav.NavigateTo("/students/periods")` on success.
- Styling: add `.period-edit-danger` rules to the existing `Edit.razor.css` (CSS isolation — no global stylesheet edits), reusing `_form-ink-red`/border tone consistent with danger affordances elsewhere; keep it minimal per `blazor-css-isolation`.

### 8.3 SubPeriodsSection rows — `Components/Pages/Periods/SubPeriodsSection.razor` (FR-D12, AC-D10)

- In the per-row actions `<span class="subperiods-actions">` (non-editing branch), next to the existing Edit button:

```razor
@if (p.Status == "Draft")
{
    <FluentButton Appearance="Appearance.Stealth" IconStart="@FluentIcons.Delete"
                  Title="Delete sub-period" OnClick="@(() => OnDeleteAsync(p))">Delete</FluentButton>
}
```

- New `OnDeleteAsync(PeriodDto p)`: confirmation via `DialogService.ShowConfirmationAsync(PeriodDeletePrompts.SubPeriodMessage(p.Name, GetKindLabel(p)), "Delete", "Cancel")` (inject `IDialogService`), then `await Api.DeletePeriodAsync(p.Id)` inside the section's existing try/catch error-bar pattern, then `await ReloadAsync(); await OnChanged.InvokeAsync();` (keeps the page in sync). Non-Draft rows render **no** Delete button (AC-D10).

## 9. NFR-D3 keyboard accessibility

Grid rows with `RowActionsUseMenuService="false"` render `RowAction`s as labeled `FluentButton`s (verified precedent: guarded Activate renders `fluent-button` + `title`), so the Delete action is a plain tab stop with an accessible name — not a disabled-button tooltip. No `disabled:` argument on the Delete row action. The bUnit test asserts an enabled `fluent-button` with `Title == "Delete"` (see test plan) and the r1-Follow UI-tester round re-validates SR.

## 10. Test plan

### 10.1 Handler + domain unit tests — new `tests/SchoolCollab.Students.Tests.Unit/PeriodDeleteHandlerTests.cs`

Harness: `StudentsTestScope` (EF InMemory, real `PeriodRepository`, `HybridCache`), ctor `new DeletePeriodHandler(s.Periods, s.Cache, NullLogger<DeletePeriodHandler>.Instance)`. Seed with `CreatePeriodHandler`/direct `Period.Create` + `s.Db.Periods.AddRange(...)` like `PeriodGuardAndAtomicCreateTests`.

| Test | AC / FR | Assertion |
| --- | --- | --- |
| `Delete_ActiveYear_ThrowsPeriodNotDeletable_RowUnchanged` | AC-D1 | `PeriodNotDeletableException`; `s.Db.Periods` still contains the row with `Status == Active` |
| `Delete_ActiveSubPeriod_ThrowsPeriodNotDeletable_RowUnchanged` | AC-D1/FR-D4 | same for a sub-period row |
| `Delete_DraftYear_With2DraftSubPeriods_RemovesAll3_OneUnitOfWork` | AC-D2, NFR-D1 | after `HandleAsync`, `s.Db.Periods.CountAsync(p => p.Id == year || p.ParentPeriodId == year) == 0`; exactly one handler call (no per-row removal); sub-periods were loaded tracked by the guard so EF client-cascade removes them |
| `Delete_DraftYear_WithActiveSub_BlockingSubNamed_NothingDeleted` | AC-D3, FR-D3 | throws `PeriodNotDeletableException`; `ex.Message` contains the Active sub's name and status; year + both subs still present |
| `Delete_DraftSubPeriod_RemovesOnlyTheRow_ParentRemains` | AC-D4, FR-D4 | sub gone, parent year row still Draft |
| `Delete_OtherTenantsPeriod_ThrowsPeriodNotFound` | AC-D5, FR-D5 | seed other-tenant row via `TenantAccessor` bypass; `PeriodNotFoundException` (route maps 404); no rows removed |
| `Delete_ReDeletedPeriod_ResolvesToNotFound` | NFR-D2 | second `HandleAsync` with same id throws `PeriodNotFoundException` (not an exception leak), after the first call succeeded |
| `Delete_DanglingDraftNextPeriodLink_IsNulled_NonDraftLinkUntouched` | AC-D6, FR-D6, EC-2 | seed Draft B with `SetNextPeriod(A.Id)` and a Completed C with `NextPeriodId = A.Id`; delete A → `B.NextPeriodId == null`, `C.NextPeriodId == A.Id` (historical record stays) |
| `Delete_Succeeding_EmitsPeriodDeletedEvent_SingleLog` | FR-D7 | observe `period.DomainEvents` before `HandleAsync`-internal clear **or** intercept via a stub `Period` — preferred: after `HandleAsync`, assert delete actually persisted and no exception; domain-event emission asserted in the pure-domain test below |
| Domain: `Period.Delete_OnActive_Completed_Archived_Throws` | FR-D2 | `PeriodNotDeletableException` messages reference Draft-only |
| Domain: `Period.Delete_OnDraft_AddsPeriodDeletedEvent` | FR-D7 | `DomainEvents.OfType<PeriodDeletedEvent>()` has exactly one entry with matching Id/Name |
| Domain: `Period.ClearNextPeriod_NullsLink` | FR-D6 | link set → cleared, `UpdatedAt` bumped; null link is a no-op |

### 10.2 Repository test (same file or adjacent)

`PeriodRepository.GetDraftPeriodsLinkedToAsync` returns only Draft rows linked to the target within the tenant filter (seed one Draft + one Completed linker).

### 10.3 Integration — new `tests/SchoolCollab.Students.Tests.Integration/PeriodDeleteEndpointTests.cs`

Harness per `PeriodWizardOpenTermGateTests` (`ApiFactory` + Testcontainers Postgres, `[DoNotParallelize]`, `TRUNCATE TABLE periods CASCADE` + `cache.RemoveByTagAsync("students")` in `TestInitialize`, `x-tenant-id` header for tenant A/B). Create rows via `POST /students/periods` (client) or direct DbContext seeding like the wizard test's `PutAsync`-style helpers.

| Test | AC | Assertion |
| --- | --- | --- |
| `Delete_DraftYear_204_ThenRepeat_404` | AC-D7, FR-D8 | `DELETE /students/periods/{id}` → 204; same call again → 404 |
| `Delete_UnknownId_404` | FR-D8 | random Guid → 404 |
| `Delete_OtherTenantDraftId_404_RowUntouched` | AC-D5 | row created under tenant A, `x-tenant-id: TestTenantB` delete → 404; tenant A still has the row |
| `Delete_ActivePeriod_422_WithMessage` | FR-D2 | 422 with `{ "message": ... }` body |
| `Delete_Year_ActiveSubPeriod_422_CascadeAborted` | AC-D3/FR-D3/NFR-D1 | 422 naming the blockee; **DB check**: year + Active sub + Draft sub all still present (zero partial deletions) |
| `Delete_Year_WithDraftSubs_DbCascadeRemovesAll` | AC-D2 | 204; direct SQL/DbContext check that sub-period rows are physically gone from Postgres (`ON DELETE CASCADE` — no EF-tracked shortcut) |
| `Delete_RemovedRow_IsGoneFromListAndTenantCacheFlushed` | FR-D5 (parity) | `GET /students/periods` no longer returns it |

### 10.4 bUnit tests — extend existing files (do not create new harnesses)

1. **`tests/SchoolCollab.Admin.Tests.Unit/PeriodsLandingGridTests.cs`** (`BunitContext` + `ScriptedHandler` + mocked `IDialogService` — follow the file's own conventions):
   - `Periods_RowActions_DraftRow_OffersDelete` — Draft row renders an enabled `fluent-button` with `Title == "Delete"` (AC-D8 + NFR-D3 reachable, not a disabled tooltip).
   - `Periods_RowActions_NonDraft_NoDelete` — Active/Completed/Archived rows expose no Delete (AC-D8).
   - `Periods_Delete_Confirm_CallsApiAndReloads` — `IDialogService.Setup(d => d.ShowConfirmationAsync(...))` returns confirmed dialog → assert `ScriptedHandler` received `DELETE /students/periods/{id}` and the list was re-fetched (FR-D9).
   - `Periods_Delete_Cancelled_DoesNotCallApi` (dialog-cancel path).
   - `Periods_Delete_YearConfirmation_NamesPeriodAndDraftSubCount` — the confirmation message for a Terms year contains the year name + "2 draft sub-periods" from the memoized `_draftSubPeriodCounts` map.
   - Keep `Periods_RowActions_CollapseToSingle` expectations intact (Draft rows now Activate+Delete: update its `labels` assertion if it pins exact label sets; re-run once before reporting if it flakes — known flaky under bUnit+FluentMenu on clean checkouts).
2. **`tests/SchoolCollab.Admin.Tests.Unit/PeriodEditPageTests.cs`** (extend):
   - `Edit_DraftPeriod_RendersDangerZoneDelete` (AC-D8 analog for FR-D10) and `Edit_NonDraftPeriod_HasNoDangerZone`;
   - `Edit_Delete_Confirm_NavigatesToPeriods` — confirmed dialog → `DELETE` call recorded → `Nav` to `/students/periods`;
   - `Edit_Delete_YearConfirmation_Wording_MatchesGrid` — same `PeriodDeletePrompts` message (share the static helper to prove wording parity).
3. **`tests/SchoolCollab.Students.Tests.Unit/PeriodFormSubPeriodsSectionTests.cs`** (extend, or new `PeriodSubPeriodsSectionDeleteTests.cs` reusing its harness):
   - `SubPeriodsSection_DraftRow_OffersDelete` (AC-D10), `SubPeriodsSection_ActiveRow_NoDelete`;
   - `SubPeriodsSection_Delete_Confirm_CallsApiAndRaisesOnChanged` — DELETE call recorded, `OnChanged` invoked, list re-fetched;
   - `SubPeriodsSection_Delete_UsesGridConfirmationWording`.

## 11. Out-of-scope guardrails (hard)

1. **Never touch** `Components/Pages/Periods/SubPeriodsListDialog.razor` (+ `.css`) — superseded; deletion is a separate cleanup PR.
2. **No soft delete** — no `IsDeleted`/`DeletedAt` on `Period`, no recycle-bin UI, no `ListDeleted`-style view (Students' soft-delete precedent does **not** carry over).
3. **No bulk/multi-row delete.**
4. **No feature flag** (spec decision 3 — hard invariant, not preference).
5. **No integration/outbox event, no MassTransit contract** — FR-D7 is satisfied by the `PeriodDeletedEvent` domain event alone; do not add `PeriodDeleted` to `SchoolCollab.Students.Contracts.Events` or touch consumers.
6. **No schema/migration changes** — the cascade FK is already declared in `PeriodConfiguration`; `NextPeriodId` stays FK-less.
7. **No per-row sub-period removal** in handler/repository (FR-D3 explicitly forbids re-implementing what the EF cascade already does).
8. **No 409 responses** on the delete route (EC-3: concurrency → 404).
9. Do not modify `Activate`/`Complete`/`Create`/`Update` handlers or routes; no edits to other bounded contexts; no edits to `documents/rounds/plan-*`/`acceptance-*` docs.
10. Do not rename or re-shape existing public APIs beyond the additions listed above.

## 12. Verification commands (worker)

1. `dotnet build SchoolCollab.sln -c Debug --nologo -v q` after every change; zero errors before proceeding.
2. `dotnet test tests/SchoolCollab.Students.Tests.Unit` — all new + existing green.
3. `dotnet test tests/SchoolCollab.Admin.Tests.Unit` — all new + existing green (rerun `Periods_RowActions_CollapseToSingle` once if it fails without a code cause).
4. `dotnet test tests/SchoolCollab.Students.Tests.Integration` — requires Docker (Testcontainers); if Docker is unavailable, mark these tests NOT RUN in the report — do not skip silently.
5. MSB3021/MSB3027 lock → stop lingering `dotnet` MSBuild node-reuse workers (`Get-CimInstance Win32_Process | Where-Object ... MSBuild.dll`), then retry **once**; if still locked, surface it, don't loop.

## 13. Residual risks (for reviewer/tester attention)

- **InMemory vs Postgres cascade:** unit tests prove the client-cascade path (tracked dependents); **only** the Postgres integration test proves the physical `ON DELETE CASCADE`. If integration tests cannot run locally (no Docker), AC-D2's DB-level evidence lands only in CI.
- FK RESTRICT backstop: a data-anomaly Draft sub-period with activity-group memberships would hard-fail at the DB with a 500 — acceptable per spec (Draft periods cannot accrue memberships), but worth a UI-tester glance at the error bar copy.
- The grid's `Period` rows also exist for sub-periods in the flat list; the plan uses the same Delete affordance there (FR-D12 wording for sub-rows) — confirm with the spec owner if the grid should restrict Delete to top-level years only (spec doesn't; the plain reading covers all Draft rows).