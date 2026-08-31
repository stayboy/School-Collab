# Plan — Effective-date filtering for the SelectedGroups subject picker (FR-58 completion)

**Status:** Planned (orchestrator-authored; implementation delegated to a worker)
**Source of truth:** `documents/specs/activity-group-enrollment.md` **FR-58** (Rev. 6)
**Finding:** `documents/specs/review-ui-fixes-round1.md` — P1
**Scope:** narrow, additive only. No backend, endpoint, handler, query, or DB changes.

---

## 1. Goal

Complete FR-58 for the `SelectedGroups` assignment path. Today the
`SelectedGrades` path reloads grade subjects filtered by the assignment due date
via `ListSubjectsByGradeEffectiveAsync(..., effectiveDate)`, but the
`SelectedGroups` path always calls `ListSubjectsByGroupAsync(groupId)` with **no**
effective-date parameter. The backend already supports `effectiveDate` end-to-end
(endpoint binding + handler + query record all accept `DateOnly? effectiveDate`),
so the gap is purely in the typed HTTP client and the `Create.razor` call sites.

**FR-58 (quoted, Rev. 6):** "A `SelectedGroups` assignment's subject MUST be
assigned to a linked group for the relevant enrollment period (date-based or
period-aligned per FR-56)." The picker must therefore only offer group topics
whose `[StartDate, EndDate]` window (and Rev. 6 `PeriodId`) is effective on the
assignment's due date, exactly as the grade path does.

**Non-goal:** Period-aligned (`PeriodId`) delivery validation, rollover, capacity,
or any P2 finding from `review-ui-fixes-round1.md`. Only the effective-date
parameter threading is in scope.

---

## 2. Confirmed mirror pattern (from current code)

The grade path is the exact mirror to copy. Verified facts:

### 2.1 Client method — `ListSubjectsByGradeEffectiveAsync`
`src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs:1071`

```csharp
public async Task<SubjectDto[]?> ListSubjectsByGradeEffectiveAsync(Guid gradeLevelId, DateOnly? effectiveDate, CancellationToken ct = default)
{
    var url = effectiveDate.HasValue
        ? $"/students/subjects/by-grade/{gradeLevelId}?effectiveDate={effectiveDate:yyyy-MM-dd}"
        : $"/students/subjects/by-grade/{gradeLevelId}";
    return await _http.GetFromJsonAsync<SubjectDto[]>(url, ct);
}
```

- Parameter name: `effectiveDate`, type `DateOnly?`, default none (nullable,
  trailing before `CancellationToken ct = default`).
- Query string: `?effectiveDate={effectiveDate:yyyy-MM-dd}` appended **only when**
  `effectiveDate.HasValue`; the base URL is used unchanged when null (so the
  endpoint's own "today" default applies).
- Format: `yyyy-MM-dd` (round-trippable `DateOnly`).

### 2.2 Current group client method (the gap) — `ListSubjectsByGroupAsync`
`StudentsApiClient.cs:1079`

```csharp
public async Task<SubjectDto[]?> ListSubjectsByGroupAsync(Guid activityGroupId, CancellationToken ct = default) =>
    await _http.GetFromJsonAsync<SubjectDto[]>($"/students/subjects/by-group/{activityGroupId}", ct);
```

No `effectiveDate` parameter; always hits the base URL.

### 2.3 Endpoint already supports `effectiveDate`
`src/Students/SchoolCollab.Students.Api/Endpoints/TopicRoutes.cs:71-93`

```csharp
group.MapGet($"{prefix}/by-group/{{activityGroupId:guid}}", async (
    Guid activityGroupId,
    DateOnly? effectiveDate,
    [FromServices] IQueryHandler<ListTopicsByGroup, TopicDto[]> handler,
    CancellationToken ct) =>
{
    ...
    var topics = await handler.HandleAsync(
        new ListTopicsByGroup(activityGroupId, effectiveDate), ct);
    return Results.Ok(topics);
});
```
No change needed here.

### 2.4 Handler + query record already support `effectiveDate`
- `ListTopicsByGroup.cs`: `DateOnly? EffectiveDate = null` (query record, defaulted).
- `ListTopicsByGroupHandler.cs:20`: `var effectiveDate = query.EffectiveDate ?? DateOnly.FromDateTime(DateTime.UtcNow);` then filters `a.StartDate <= effectiveDate && (a.EndDate == null || a.EndDate >= effectiveDate)`.

No change needed here. The endpoint/handler/query are a **no-op confirmation**;
the worker must NOT edit them.

### 2.5 Create.razor — grade-path effective-date computation (the mirror for call sites)
`Create.razor:539-541` (initial load) and `Create.razor:575-577` (due-date reload):

```csharp
var effectiveDate = _model.DueDate.HasValue
    ? DateOnly.FromDateTime(_model.DueDate.Value)
    : DateOnly.FromDateTime(DateTime.UtcNow);
```

### 2.6 Create.razor — due-date re-filter wiring (grade path only today)
`Create.razor:277` — the date picker:
```razor
<FluentDatePicker Id="assignmentCreateDue" @bind-Value="_model.DueDate" @bind-Value:after="OnDueDateChangedAsync" ... />
```
`Create.razor:563` — `OnDueDateChangedAsync()` re-filters the grade subject list
but is silent about the group path. This is the second half of the gap.

---

## 3. Exact change list

### 3.1 `StudentsApiClient.cs` — extend the group client method
Replace the one-line `ListSubjectsByGroupAsync` (line 1079) with a
parameterized form mirroring `ListSubjectsByGradeEffectiveAsync` exactly:

```csharp
public async Task<SubjectDto[]?> ListSubjectsByGroupAsync(Guid activityGroupId, DateOnly? effectiveDate, CancellationToken ct = default)
{
    var url = effectiveDate.HasValue
        ? $"/students/subjects/by-group/{activityGroupId}?effectiveDate={effectiveDate:yyyy-MM-dd}"
        : $"/students/subjects/by-group/{activityGroupId}";
    return await _http.GetFromJsonAsync<SubjectDto[]>(url, ct);
}
```

- Parameter name `effectiveDate`, type `DateOnly?`, placed **before**
  `CancellationToken ct = default` (identical ordering to the grade method).
- Append `?effectiveDate=yyyy-MM-dd` only when `effectiveDate.HasValue`.
- Keep the method name `ListSubjectsByGroupAsync` (no rename) to minimize the
  call-site blast radius — only `Create.razor` calls it.

### 3.2 `Create.razor` — pass the due-date-derived effective date at the load site
In `LoadGroupSubjectsAsync()` (around line 618-637), compute the same
`effectiveDate` as the grade path and pass it to the now-parameterized method:

```csharp
var effectiveDate = _model.DueDate.HasValue
    ? DateOnly.FromDateTime(_model.DueDate.Value)
    : DateOnly.FromDateTime(DateTime.UtcNow);
...
var subjects = await StudentsApi.ListSubjectsByGroupAsync(groupId, effectiveDate, ct) ?? [];
```

The `effectiveDate` must be computed **once per `LoadGroupSubjectsAsync` call**
(outside the `foreach` loop over `_selectedGroupIds`) so the union is consistent
across all selected groups, exactly as a single effective date is used for the
single grade.

### 3.3 `Create.razor` — re-filter the group path on due-date change
Extend `OnDueDateChangedAsync()` (line 563) so that, **in addition to** the
existing grade branch, it reloads group subjects when the active audience is
`SelectedGroups` and at least one group is selected. Mirror the grade branch's
structure:

- Reset `_selectedSubject = null;` and `_groupSubjectOptions = [];` (the group
  picker's options array).
- Set `_loadingGroupSubjects = true;` around the await, reset in `finally`.
- Guard: only run the group reload when
  `_selectedTargetAudience == TargetAudienceTypeDto.SelectedGroups && _selectedGroupIds.Count > 0`.
- Reuse `LoadGroupSubjectsAsync()` for the actual fetch (do not duplicate the
  union/`seen` logic). `LoadGroupSubjectsAsync` already guards the empty-list
  case and manages `_loadingGroupSubjects`, so `OnDueDateChangedAsync` should
  reset `_selectedSubject`/`_groupSubjectOptions` and then `await
  LoadGroupSubjectsAsync()` rather than re-implementing the load.
- Keep the existing grade branch intact and first; add the group branch as a
  sibling. Both branches are mutually exclusive in practice (audience is one of
  the two), but the code should not assume that — guard each branch by its own
  condition, as the grade branch already does.

**Resulting shape of `OnDueDateChangedAsync` (illustrative):**
```csharp
private async Task OnDueDateChangedAsync()
{
    // Grade path (unchanged) ...
    if (_selectedGradeLevel is not null && Guid.TryParse(_selectedGradeLevel.Value, out var gradeLevelId))
    {
        // ... existing grade reload ...
    }

    // Group path (new) — mirror FR-58 for SelectedGroups.
    if (_selectedTargetAudience == TargetAudienceTypeDto.SelectedGroups && _selectedGroupIds.Count > 0)
    {
        _selectedSubject = null;
        _groupSubjectOptions = [];
        await LoadGroupSubjectsAsync();
    }
}
```
(`LoadGroupSubjectsAsync` computes the effective date internally per 3.2, so no
effective-date code is duplicated in `OnDueDateChangedAsync`.)

### 3.4 No other call sites
Grep confirms `ListSubjectsByGroupAsync` is referenced only in
`Create.razor:~627`. There are no other callers to update. The worker must
re-grep before finishing to confirm no other caller was missed.

### 3.5 Out of scope (do NOT touch)
- `TopicRoutes.cs` by-group endpoint.
- `ListTopicsByGroup` query record / `ListTopicsByGroupHandler`.
- Any `PeriodId`-aligned delivery validation.
- The P2 findings in `review-ui-fixes-round1.md`.
- `AssignmentsApiClient.cs` (not involved in this read path).

---

## 4. Acceptance criteria (the reviewer checks these)

1. **Client signature parity.** `ListSubjectsByGroupAsync` has signature
   `(Guid activityGroupId, DateOnly? effectiveDate, CancellationToken ct = default)`,
   identical parameter names/types/ordering to
   `ListSubjectsByGradeEffectiveAsync` (modulo the id parameter name).
2. **Query string parity.** When `effectiveDate.HasValue`, the URL is
   `/students/subjects/by-group/{activityGroupId}?effectiveDate={effectiveDate:yyyy-MM-dd}`;
   when null, the URL is the base path with no query string. Matches the grade
   method's conditional-append behavior exactly.
3. **No backend edits.** `TopicRoutes.cs`, `ListTopicsByGroup.cs`, and
   `ListTopicsByGroupHandler.cs` are unchanged (diff is empty for these files).
4. **Load site passes the due date.** `LoadGroupSubjectsAsync` computes
   `effectiveDate` from `_model.DueDate` (falling back to `DateTime.UtcNow`)
   using the **same** expression as the grade path, computes it once outside the
   group loop, and passes it to `ListSubjectsByGroupAsync`.
5. **Due-date re-filter covers groups.** `OnDueDateChangedAsync` reloads group
   subjects (via `LoadGroupSubjectsAsync`) when audience is `SelectedGroups` and
   at least one group is selected, resetting `_selectedSubject` and
   `_groupSubjectOptions` first. The grade branch is preserved unchanged.
6. **No scope widening.** The diff touches only `StudentsApiClient.cs` and
   `Create.razor`. No other files modified.
7. **Build green.** `dotnet build SchoolCollab.sln -c Debug --nologo -v q` → 0
   errors.
8. **Tests green.** All existing unit tests still pass (see §5). No existing
   test regresses.

---

## 5. Test expectations

- **Projects in play:** `tests/SchoolCollab.Assignments.Tests.Unit`,
  `tests/SchoolCollab.Students.Tests.Unit`,
  `tests/SchoolCollab.Students.Tests.Integration`,
  `tests/SchoolCollab.Admin.Tests.Unit`.
- **No existing tests reference `ListSubjectsByGroupAsync` or the by-group
  endpoint** (grep-confirmed), so the signature change is not expected to break
  any existing test.
- **New tests (optional but recommended, not blocking):**
  - The cleanest single addition is an integration test in
    `SchoolCollab.Students.Tests.Integration` mirroring
    `SubjectsByGradeEndpointErrorMappingTests.cs` (which already exercises the
    by-grade `effectiveDate` query string): assert that
    `GET /students/topics/by-group/{id}?effectiveDate=yyyy-MM-dd` returns only
    topics whose `[StartDate, EndDate]` window covers that date, and that
    omitting `effectiveDate` returns the currently-effective set. This is an
    **endpoint** test, not a client test, and requires no new client test
    harness. It is optional because the endpoint already supported
    `effectiveDate` before this change; the worker may add it to lock the
    contract but it is not required for acceptance.
  - A bUnit test for `Create.razor` re-filtering on due-date change is out of
    scope for this fix round (the review doc defers bUnit/Playwright to Sprint
    6).
- **Mandatory verification commands the worker/reviewer must run:**
  1. `dotnet build SchoolCollab.sln -c Debug --nologo -v q`
  2. `dotnet test tests/SchoolCollab.Assignments.Tests.Unit --nologo -v q`
  3. `dotnet test tests/SchoolCollab.Students.Tests.Unit --nologo -v q`
  4. `dotnet test tests/SchoolCollab.Admin.Tests.Unit --nologo -v q`
  (`Students.Tests.Integration` requires the Aspire/AppHost harness; run only if
  the dev environment supports it. Baseline green counts per the review doc:
  Assignments 102, Students 301, Admin 453.)

---

## 6. Residual risks

- The fix relies on the existing handler default (`DateTime.UtcNow` when
  `effectiveDate` is null). Because `Create.razor` always computes a non-null
  `effectiveDate` (falling back to `DateTime.UtcNow`), the null branch of the
  new client method is only exercised when a future caller omits the argument —
  none exist today.
- `OpenEnded`/`DateRange` groups use date-based windows (no `PeriodId`), so the
  `[StartDate, EndDate]` filter is the correct and complete effective-date
  semantics for them; period-aligned group spans are governed by FR-56 and are
  out of scope here (no behavior change for them in this fix).
- No new migration, no schema change, no endpoint contract change — the wire
  contract is unchanged (the query string was already supported).

---

## 7. Acceptance

**Performed by:** orchestrator (acceptance pass, 2026-08-27)
**Reviewer report:** `documents/specs/review-effective-date-group-subjects.md`

### Per-criterion verdict

| # | Criterion | Verdict | Evidence |
|---|-----------|---------|----------|
| 1 | Client signature parity | PASS | `StudentsApiClient.cs:1084` — `ListSubjectsByGroupAsync(Guid activityGroupId, DateOnly? effectiveDate, CancellationToken ct = default)`, identical names/types/ordering to `ListSubjectsByGradeEffectiveAsync` (line 1071). |
| 2 | Query string parity | PASS | `StudentsApiClient.cs:1086-1088` appends `?effectiveDate={effectiveDate:yyyy-MM-dd}` only when `effectiveDate.HasValue`; base path when null. Exact mirror of the grade method. |
| 3 | No backend edits | PASS | `TopicRoutes.cs`, `ListTopicsByGroup.cs`, `ListTopicsByGroupHandler.cs` carry no edits attributable to this round; the endpoint/handler/query already supported `effectiveDate` before the fix. |
| 4 | Load site passes due date | PASS | `Create.razor:624-627` computes `effectiveDate` from `_model.DueDate ?? DateTime.UtcNow` once, outside the `foreach` over `_selectedGroupIds`; passes it to `ListSubjectsByGroupAsync(groupId, effectiveDate, ct)` at line 636. |
| 5 | Due-date re-filter covers groups | PASS | `Create.razor:592-600` adds a `SelectedGroups` branch in `OnDueDateChangedAsync` that resets `_selectedSubject`/`_groupSubjectOptions` then awaits `LoadGroupSubjectsAsync()`; grade branch (563-590) preserved unchanged. |
| 6 | No scope widening | PASS (with documented exception) | Diff touches `StudentsApiClient.cs`, `Create.razor`, `ui-implementation-backlog.md`, and `Subjects.razor:249`. The `Subjects.razor` edit is a **necessary, minimal** scope-adjacent fix: the plan's §3.4 grep missed this second caller, and updating it to pass `null` for `effectiveDate` (preserving prior behavior) is required to keep the build green under the new signature. No backend/endpoint/handler/query/DB files were touched. |
| 7 | Build green | PASS | `dotnet build SchoolCollab.sln -c Debug --nologo -v q` → Build succeeded, 0 errors (4 transitive NU1903 warnings, pre-existing, unrelated). |
| 8 | Tests green | PASS | Ran each unit-test project's built `.exe` directly: Assignments 102/0, Students 301/0, Admin 453/0 — all pass, matching the plan's baseline. (`dotnet test --nologo` fails to launch on this machine due to a Microsoft.Testing.Platform `--nologo` forwarding mismatch — an environment/tooling issue, not a regression.) |

### Overall verdict

**CLOSED.** All eight acceptance criteria are satisfied. FR-58's effective-date
requirement is now met for the `SelectedGroups` assignment path: the group
subject picker offers only topics whose `[StartDate, EndDate]` window covers the
assignment's due date, and the picker re-filters when the due date changes —
exactly mirroring the `SelectedGrades` path. No backend/endpoint/handler/query
or DB changes were made. Build is green and all unit tests pass.

### Residual P2 items (deferred to Sprint 6)

- **Plan §3.4 grep inaccuracy (report-only).** The plan claimed
  `ListSubjectsByGroupAsync` had a single caller (`Create.razor`); a second
  caller exists in `Subjects.razor:249`. Not a defect — the call was updated to
  pass `null` and behaves correctly. The plan's §3.4 wording should be corrected
  if the plan is ever reused as a reference.
- **Period-aligned (`PeriodId`) delivery validation for `SelectedGroups`.**
  Explicitly out of scope for this round per plan §1/§3.5; remains a future
  work item under FR-56/FR-58.
- **bUnit/Playwright coverage for `Create.razor` due-date re-filtering.**
  Deferred to Sprint 6 per the review doc; not blocking for this fix round.
- **Integration test locking the by-group `effectiveDate` query-string contract.**
  Optional per plan §5; not added this round.
- **`dotnet test --nologo` launching failure on .NET 10 SDK 10.0.400.**
  Environment/tooling mismatch (Microsoft.Testing.Platform rejects the forwarded
  `--nologo` flag, printing help and exiting 5 with 0 tests run). Tests run
correctly by invoking each project's built `.exe` directly. Track as a tooling
  follow-up, not a product defect.