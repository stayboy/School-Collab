# Spec: Grade-Level Landing Counts + Dialog-Based Strand/Lesson Management

> Status: **Draft / Plan**
> Owner: Students + Admin contexts
> Supersedes (partially): the dialog-based create/edit portions of
> `grade-level-simplified-management.md` §4–§5 (dialog `GradeLevelCreateDialog`/
> `GradeLevelEditDialog` are replaced by routable `Create.razor`/`Edit.razor`
> pages). The landing-page aggregation §7 of `grade-level-setup.md` is
> **extended** (two new count columns), not rebuilt. The strand-as-coded-value
> model (§0.3 of the simplified-management spec) is **retained** — this spec
> concerns `TopicStrand`/`TopicLesson` (curriculum strands/lessons under a
> `Topic`), which are a *separate* concern from grade strands.
> Depends on: `coded-values-architecture.md`, `multi-tenant-coded-values.md`,
> `landing-page-wrapper.md`, `ui-visible-tenancy-guard.md`,
> `subject-to-topic-polymorphism.md`

## 0. Decisions locked in this revision

1. **Landing gains two count columns: Strands and Lessons.** The existing
   `TopicCount` (effective topics) and `StudentCount` (current-period) columns
   stay. `StrandCount` and `LessonCount` are added to `GradeLevelLandingDto`
   and surfaced as two new `TemplateColumn`s on `GradeLevels.razor`.
2. **`StrandCount` = sum of strands across the grade's effective topics.**
   Computed server-side as `db.TopicStrands.Count(ts => effectiveTopicIds.Contains(ts.TopicId))`,
   where `effectiveTopicIds` is the set of topic ids from the same date-effective
   `GradeTopicAssignment` filter already used for `TopicCount`.
3. **`LessonCount` = the full topic lesson catalog for the grade's effective
   topics** (not strand-filtered, not period-bound). Computed server-side as
   `db.TopicLessons.Count(tl => effectiveTopicIds.Contains(tl.TopicId))`.
   A lesson belongs to a topic; the count is the total lessons under every
   effective topic of the grade.
4. **Strands/Lessons anchors open dialogs (option (a): combined list dialog).**
   Clicking the Strands count on a landing row opens `StrandsDialog`, a single
   non-routable dialog that lists **all strands across the grade's effective
   topics**, grouped by topic name. Clicking the Lessons count opens
   `LessonsDialog`, the analogous combined dialog for lessons. There is **no
   per-topic picker** — the dialog shows every effective topic's strands (or
   lessons) in one scrollable list, each topic rendered as a section header
   with its strands/lessons beneath.
5. **Create/Edit move from dialogs to routable pages.**
   `/students/grade-levels/create` and `/students/grade-levels/{id}/edit`
   become real `@page` components (`GradeLevels/Create.razor`,
   `GradeLevels/Edit.razor`). The existing `GradeLevelCreateDialog.razor` /
   `GradeLevelEditDialog.razor` (in `Components/Students/`) are **deleted**.
   `GradeLevelFormFields.razor` is **extended** with the topics multi-select
   grid (absorbed from the deleted dialogs). The landing's `+ New` button
   navigates to the create route; the row kebab `Edit` action navigates to the
6. **Strand/lesson CRUD reuses existing backend routes — no new routes.** The
   `TopicRoutes.cs` already maps `GET /topics/{topicId}/strands`,
   `POST /topics/strands`, `PUT /topics/strands/{id}`,
   `DELETE /topics/strands/{id}`, `GET /topics/{topicId}/lessons`,
   `POST /topics/lessons`, `PUT /topics/lessons/{id}`,
   `POST /topics/lessons/{id}/strand`, `DELETE /topics/lessons/{id}` (and the
   `/subjects` deprecated alias). The CQRS handlers (`CreateTopicStrand`,
   `UpdateTopicStrand`, `RemoveTopicStrand`, `CreateTopicLesson`,
   `UpdateTopicLesson`, `RemoveTopicLesson`, `AssignLessonStrand`) all exist.
   The **only backend change** is the two new count sub-queries in
   `ListGradeLevelsForLandingHandler` + the DTO extension.
7. **API client gap: strand/lesson methods are absent.** `StudentsApiClient.cs`
   has topic methods (`ListTopicsAsync`, `ListGradeTopicsByGradeAsync`,
   `AssignGradeTopicAsync`, `RemoveTopicAssignmentAsync`) but **no** strand or
   lesson list/create/update/remove methods. This spec adds them (plus the
   request records).
8. **Folder convention.** Non-routable dialogs live in `Components/Students/`.
   `StrandsDialog.razor` / `LessonsDialog.razor` are created there (replacing
   the deleted `GradeLevelCreateDialog.razor` / `GradeLevelEditDialog.razor`
   in-place, keeping the folder's "dialog component" role). Routable pages
   live in `Components/Pages/Students/GradeLevels/` alongside `GradeLevels.razor`.

## 1. Goal

A tenant admin can, from the Grade Levels landing page:

- **See four counts per grade row**: Topics (existing), Strands (new), Lessons
  (new), Students (existing) — all for the current period / effective date.
- **Open a combined Strands dialog** from the Strands count anchor to view,
  create, edit, and delete strands across all the grade's effective topics in
  one place.
- **Open a combined Lessons dialog** from the Lessons count anchor to view,
  create, edit, delete, and strand-assign lessons across all the grade's
  effective topics in one place.
- **Create and edit grade levels via routable pages** (not dialogs), with the
  topics multi-select grid on the same form as the validation fields.

## 2. Scope / context summary

### What exists (kept, unchanged)

- `GradeLevelLandingDto` (Core + Admin mirror) with `TopicCount`,
  `StudentCount`, `CurrentPeriodId`, `CurrentPeriodName`, `MinAge`, `MaxAge`,
  `AllowedGenderCodedValueId`.
- `ListGradeLevelsForLandingHandler` — derives current period server-side,
  caches by period, computes `TopicCount` (date-effective) and `StudentCount`
  (current-period, tenant-scoped).
- `GradeLevels.razor` landing — renders Name, Age range, Gender, Topics,
  Students, Period columns; kebab Edit/Delete; `+ New` currently opens a dialog.
- `TopicRoutes.cs` — full strand/lesson CRUD route surface.
- Strand/lesson CQRS commands/queries + handlers.
- `TopicStrandDto`, `TopicLessonDto`, `TopicDto` DTOs.
- `StudentsApiClient` topic methods: `ListTopicsAsync`,
  `ListGradeTopicsByGradeAsync`, `AssignGradeTopicAsync`,
  `RemoveTopicAssignmentAsync`.
- `GradeLevelFormFields.razor` — shared form fields (grade picker, name,
  level, display order, age range, allowed gender) used by the dialog-based
  create/edit. Has `GradeLevelFormModel` but **no topics grid** yet.

### What's new (this spec)

| Layer | Change |
|-------|--------|
| Core DTO | Add `StrandCount`, `LessonCount` to `GradeLevelLandingDto` |
| Core handler | Add two count sub-queries to `ListGradeLevelsForLandingHandler` |
| Admin DTO mirror | Add `StrandCount`, `LessonCount` to Admin `GradeLevelLandingDto` |
| API client | Add strand/lesson list/create/update/delete/assign methods + request records |
| Landing page | Add Strands + Lessons columns with dialog-opening anchors; change `+ New` and Edit to navigate |
| Create/Edit pages | New `GradeLevels/Create.razor`, `GradeLevels/Edit.razor` (routable) |
| Form fields | Extend `GradeLevelFormFields.razor` with topics grid (absorbed from dialogs) |
| Dialogs | New `StrandsDialog.razor`, `LessonsDialog.razor` in `Components/Students/` |
| Delete | `GradeLevelCreateDialog.razor`, `GradeLevelEditDialog.razor` |

### What's removed

- `GradeLevelCreateDialog.razor` and `GradeLevelEditDialog.razor` — replaced
## 3. Data model & DTOs

### 3.1 `GradeLevelLandingDto` (Core + Admin mirror)

```csharp
public sealed record GradeLevelLandingDto(
    Guid Id,
    Guid CodedValueId,
    string Name,
    int TopicCount,
    int StrandCount,       // NEW — sum across effective topics' strands
    int LessonCount,       // NEW — full lesson catalog for effective topics
    int StudentCount,
    Guid? CurrentPeriodId,
    string? CurrentPeriodName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int? MinAge = null,
    int? MaxAge = null,
    Guid? AllowedGenderCodedValueId = null);
```

Both the Core DTO (`SchoolCollab.Students.Core/DTOs/GradeLevelLandingDto.cs`)
and the Admin mirror (`StudentsApiClient.cs` nested record) gain the two new
`int` positional fields. The Admin mirror must stay shape-compatible with the
Core DTO for `GetFromJsonAsync` deserialization.

### 3.2 Existing DTOs (unchanged, referenced)

```csharp
public sealed record TopicStrandDto(
    Guid Id, Guid TopicId, string Name, string? Description,
    int DisplayOrder, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record TopicLessonDto(
    Guid Id, Guid TopicId, Guid? StrandId, string Name, string? Description,
    DateOnly? StartDate, DateOnly? EndDate, bool IsOpenEnded,
    int DisplayOrder, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record TopicDto(
    Guid Id, Guid? CodedValueId, string? Code, string Name, string? Description,
    int DisplayOrder, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
```

### 3.3 New API client request records (Admin)

```csharp
public sealed record CreateTopicStrandRequest(
    Guid TopicId, string Name, string? Description, int DisplayOrder);

public sealed record UpdateTopicStrandRequest(
    string Name, string? Description, int DisplayOrder);

public sealed record CreateTopicLessonRequest(
    Guid TopicId, string Name, string? Description,
    DateOnly? StartDate, DateOnly? EndDate, int DisplayOrder);

public sealed record UpdateTopicLessonRequest(
    string Name, string? Description, DateOnly? StartDate, DateOnly? EndDate, int DisplayOrder);

public sealed record AssignLessonStrandRequest(Guid? StrandId);
```

These mirror the route body shapes. Note: `CreateTopicLessonRequest` does
**not** carry `StrandId` — the backend `CreateTopicLesson` command has no
strand field. A lesson's strand is assigned separately via
`POST /topics/lessons/{id}/strand` (`AssignLessonStrand`). The dialog assigns
the strand as a second call after creation if the admin picked one.

## 4. Functional requirements

### Landing counts

- **FR-1** — Landing shows the **Topics** count per grade (existing,
  unchanged). Anchor navigates to
  `/students/subjects?gradeLevelId=…&periodId=…`.
- **FR-2** — Landing shows the **Strands** count per grade (new). Anchor opens
  `StrandsDialog` (FR-6).
- **FR-3** — Landing shows the **Lessons** count per grade (new). Anchor opens
  `LessonsDialog` (FR-7).
- **FR-4** — Landing shows the **Students** count per grade (existing,
  unchanged). Anchor navigates to `/students?gradeLevelId=…&periodId=…`.
- **FR-5** — When there is no current period, Strands/Lessons counts still
  render (they are date-effective on topics, not period-bound), but the
  anchors are disabled with a tooltip "No current period" (the dialogs need
  the effective topics, which still resolve; however the landing convention
### Landing anchors → dialogs

- **FR-6** — The Strands count anchor opens `StrandsDialog` passing the
  `gradeLevelId` and `CurrentPeriodId`. The dialog loads the grade's effective
  topics (`ListGradeTopicsByGradeAsync(gradeLevelId)`), then for each effective
  topic loads its strands (`ListTopicStrandsAsync(topicId)`), and renders a
  combined list grouped by topic name.
- **FR-7** — The Lessons count anchor opens `LessonsDialog` passing the
  `gradeLevelId` and `CurrentPeriodId`. The dialog loads the grade's effective
  topics, then for each effective topic loads its lessons
  (`ListTopicLessonsAsync(topicId)`), and renders a combined list grouped by
  topic name. `LessonCount` is the **full catalog** (no strand filter applied
  in the dialog's default view; a strand filter may be offered as a
  follow-up, not in this spec).

### Create/Edit pages

- **FR-8** — `/students/grade-levels/create` is a routable page
  (`GradeLevels/Create.razor`) rendering `GradeLevelFormFields` with a "Create"
  submit button. On valid submit it resolves the picked coded value, calls
  `GetOrCreateGradeLevelAsync`, applies the topic diff, and navigates back to
  the landing.
- **FR-9** — `/students/grade-levels/{id}/edit` is a routable page
  (`GradeLevels/Edit.razor`) rendering `GradeLevelFormFields` with the grade
  coded value locked (static text + override pencil), the validation fields
  editable, and the topics grid. On valid submit it calls
  `UpdateGradeLevelAsync` then applies the topic diff, and navigates back.
- **FR-10** — The landing `+ New` button navigates to
  `/students/grade-levels/create` (replacing the dialog open). The row kebab
  `Edit` action navigates to `/students/grade-levels/{id}/edit` (replacing the
  dialog open).
- **FR-11** — `GradeLevelFormFields.razor` renders: grade picker (create) or
  static grade + override pencil (edit), name, level (read-only), display
  order (read-only), age range (min/max), allowed gender, **and the topics
  multi-select grid** (checkbox list of all topics; checked = assigned).
- **FR-12** — The topics grid on create/edit shows all topics from
  `ListTopicsAsync()`. On edit, the currently-assigned topics (from
  `ListGradeTopicsByGradeAsync(id)`) are pre-checked.
- **FR-13** — On submit, the topic diff is applied: newly-checked topics →
  `AssignGradeTopicAsync`; unchecked topics →
  `RemoveTopicAssignmentAsync`. Per-topic failures are non-fatal warnings
  surfaced above the form.

### StrandsDialog

- **FR-14** — `StrandsDialog` receives `gradeLevelId` (+ optional
  `CurrentPeriodId`). It loads effective topics, then strands per topic, and
  renders one section per topic with the topic name as header and the topic's
  strands listed beneath (name, description, display order).
- **FR-15** — Each topic section in `StrandsDialog` has an "Add strand" button
  that opens an inline add-row (name, description, display order) and calls
  `CreateTopicStrandAsync(topicId, req)`.
- **FR-16** — Each strand row has edit (inline or sub-dialog) and delete
  actions. Edit calls `UpdateTopicStrandAsync(id, req)`. Delete calls
  `DeleteTopicStrandAsync(id)` with a confirmation.
- **FR-17** — `StrandsDialog` closes on explicit Cancel/Close. The landing is
  **not** automatically reloaded on dialog close (strands are a sub-resource;
  the count may drift until the next landing load — acceptable per NFR). The
  dialog does refresh its own internal list after a successful mutation.

### LessonsDialog

- **FR-18** — `LessonsDialog` receives `gradeLevelId`. It loads effective
  topics, then lessons per topic, and renders one section per topic with the
  topic name as header and the topic's lessons listed beneath (name,
  description, strand, date range, display order).
- **FR-19** — Each topic section in `LessonsDialog` has an "Add lesson" button
  that opens an inline add-row (name, description, start/end date, display
  order, optional strand picker) and calls `CreateTopicLessonAsync(topicId,
  req)`. If a strand was picked, a second call
  `AssignLessonStrandAsync(lessonId, strandId)` sets it.
- **FR-20** — Each lesson row has edit and delete actions. Edit calls
  `UpdateTopicLessonAsync(id, req)` and, if the strand changed,
  `AssignLessonStrandAsync(id, strandId)`. Delete calls
  `DeleteTopicLessonAsync(id)` with a confirmation.
- **FR-21** — `LessonsDialog` closes on explicit Cancel/Close and refreshes
  its internal list after each successful mutation (same reflow policy as
## 5. Non-functional requirements

- **NFR-1 (Performance)** — The two new count sub-queries are added inside
  the existing single projected EF query in
  `ListGradeLevelsForLandingHandler` (same `Select` projection, same cache
  entry). No extra round-trips. The `effectiveTopicIds` set is derived once
  per grade row via a correlated sub-query mirroring the `TopicCount` filter.
- **NFR-2 (Caching)** — The landing cache key and tags (`students`,
  `tenant:{id}`, `periods`) are unchanged. Strand/lesson mutations invalidate
  via the existing `students` tag if wired; otherwise the 5-minute expiration
  bounds staleness. No new cache key.
- **NFR-3 (Folder convention)** — Non-routable dialogs in
  `Components/Students/`; routable pages in
  `Components/Pages/Students/GradeLevels/`.
- **NFR-4 (No new migrations)** — `StrandCount`/`LessonCount` are computed
  projections, not persisted columns. No schema change.
- **NFR-5 (Backward-compatible API client)** — New methods are additive. The
  Admin `GradeLevelLandingDto` mirror gains two trailing positional `int`
  fields; existing consumers that don't reference them are unaffected, but
  any code constructing the record positionally must be updated (the handler
  projection and the dialog return synthesis).
- **NFR-6 (Accessibility)** — Dialog anchors are `FluentAnchor` with
  `Appearance.Lightweight`; topic sections use heading elements; add/edit
  inline rows are keyboard-navigable.
- **NFR-7 (Error handling)** — Strand/lesson API failures surface a
  `FluentMessageBar` inside the dialog; the dialog stays open. Create/Edit
  page failures surface above the form fields (existing pattern).
- **NFR-8 (Consistency)** — `StrandsDialog`/`LessonsDialog` follow the
  existing `DialogShellBase`/`IDialogService.ShowShellDialogAsync` pattern
  used by other dialogs (e.g. the deleted grade dialogs, contacts editor) for
  open/close/result wiring.

## 6. Acceptance criteria

- **AC-1** — Landing renders 6 data columns: Name, Age range, Gender,
  Topics, **Strands**, **Lessons**, Students, Period, Actions (grid template
  updated).
- **AC-2** — `StrandCount` for a grade equals the DB count of `TopicStrands`
  whose `TopicId` is in the grade's effective topic set (date-effective
  `GradeTopicAssignment`, `StartDate <= today && (EndDate == null ||
  EndDate >= today)`).
- **AC-3** — `LessonCount` for a grade equals the DB count of `TopicLessons`
  whose `TopicId` is in the same effective topic set (full catalog, no strand
  filter).
- **AC-4** — Clicking the Strands count opens `StrandsDialog` showing all
  effective topics, each with its strands; clicking Cancel closes it.
- **AC-5** — Clicking the Lessons count opens `LessonsDialog` showing all
  effective topics, each with its lessons; clicking Cancel closes it.
- **AC-6** — `+ New` navigates to `/students/grade-levels/create`; the page
  renders `GradeLevelFormFields` with the topics grid; submit creates the
  grade and applies the topic diff; success navigates to the landing.
- **AC-7** — Row kebab Edit navigates to `/students/grade-levels/{id}/edit`;
  the page pre-fills validation fields + pre-checks assigned topics; submit
  updates and applies the diff; success navigates to the landing.
- **AC-8** — In `StrandsDialog`, adding a strand under a topic section creates
  it (`POST /topics/strands`) and the section's list refreshes with the new
  strand.
- **AC-9** — In `StrandsDialog`, editing a strand updates it (`PUT
  /topics/strands/{id}`) and the list refreshes.
- **AC-10** — In `StrandsDialog`, deleting a strand removes it (`DELETE
  /topics/strands/{id}`) after confirmation and the list refreshes.
- **AC-11** — In `LessonsDialog`, adding a lesson creates it (`POST
  /topics/lessons`) and, if a strand was picked, assigns it (`POST
  /topics/lessons/{id}/strand`); the list refreshes.
- **AC-12** — In `LessonsDialog`, editing a lesson updates it (`PUT
  /topics/lessons/{id}`) and re-assigns the strand if changed; the list
  refreshes.
- **AC-13** — In `LessonsDialog`, deleting a lesson removes it (`DELETE
  /topics/lessons/{id}`) after confirmation; the list refreshes.
- **AC-14** — With no current period, Strands/Lessons counts render but their
  anchors are disabled; Topics/Students anchors already follow this.
- **AC-15** — `grep -r GradeLevelCreateDialog` and `grep -r GradeLevelEditDialog`
  return nothing in `src/` after the migration (dialogs deleted, references
## 7. Edge cases

- **EC-1 (No current period)** — Strands/Lessons counts still compute (they're
  date-effective on topics, not period-bound), but the drill-through anchors
  are disabled for landing UX consistency. The dialogs, if opened via a deep
  path, would still load effective topics.
- **EC-2 (Zero effective topics)** — `StrandCount`/`LessonCount` = 0; the
  dialogs open showing "No topics assigned to this grade" empty state with no
  topic sections.
- **EC-3 (Topic with strands but no lessons)** — StrandsDialog shows the
  topic with strands; LessonsDialog shows the topic header with an empty "No
  lessons" row and an Add button.
- **EC-4 (Concurrent edits)** — Two admins editing strands/lessons for the
  same topic may race; the dialog's post-mutation refresh shows the latest
  server state. No optimistic concurrency token on strands/lessons today
  (last-write-wins); acceptable for curriculum metadata.
- **EC-5 (API client method absence)** — Until the strand/lesson methods are
  added to `StudentsApiClient`, the dialogs cannot compile. The methods are a
  hard prerequisite (phase 2 before phase 5).
- **EC-6 (Dialog cancellation mid-edit)** — Inline add/edit rows discard
  unsaved input on Cancel/Close without a server call (client-side state only).
- **EC-7 (Lesson strand reassignment)** — Reassigning a lesson's strand via
  `AssignLessonStrand` with `StrandId = null` un-assigns it; the dialog offers
  a "No strand" option in the picker.

## 8. Implementation tasks

### Phase 1 — Backend DTO + handler

1. Add `StrandCount`, `LessonCount` to Core `GradeLevelLandingDto.cs`.
2. Extend `ListGradeLevelsForLandingHandler` projection: compute
   `effectiveTopicIds` per grade row and add `StrandCount`/`LessonCount`
   sub-counts.
3. Update the handler's `GradeLevelLandingDto` construction call to pass the
   two new values.

### Phase 2 — API client

4. Add `StrandCount`/`LessonCount` to the Admin mirror
   `GradeLevelLandingDto` in `StudentsApiClient.cs`.
5. Add request records (§3.3).
6. Add methods:
   - `ListTopicStrandsAsync(Guid topicId, CancellationToken ct)` →
     `GET /students/topics/{topicId}/strands`
   - `CreateTopicStrandAsync(CreateTopicStrandRequest req, ct)` →
     `POST /students/topics/strands` (returns `TopicStrandDto`)
   - `UpdateTopicStrandAsync(Guid id, UpdateTopicStrandRequest req, ct)` →
     `PUT /students/topics/strands/{id}`
   - `DeleteTopicStrandAsync(Guid id, ct)` →
     `DELETE /students/topics/strands/{id}`
   - `ListTopicLessonsAsync(Guid topicId, Guid? strandId, ct)` →
     `GET /students/topics/{topicId}/lessons[?strandId=…]`
   - `CreateTopicLessonAsync(CreateTopicLessonRequest req, ct)` →
     `POST /students/topics/lessons` (returns `TopicLessonDto`)
   - `UpdateTopicLessonAsync(Guid id, UpdateTopicLessonRequest req, ct)` →
     `PUT /students/topics/lessons/{id}`
   - `AssignLessonStrandAsync(Guid lessonId, Guid? strandId, ct)` →
     `POST /students/topics/lessons/{id}/strand`
   - `DeleteTopicLessonAsync(Guid id, ct)` →
### Phase 3 — Landing page

7. Add Strands + Lessons `TemplateColumn`s to `GradeLevels.razor` between
   Topics and Students.
8. Wire the Strands anchor `OnClick` to open `StrandsDialog`; the Lessons
   anchor to open `LessonsDialog` (via `IDialogService.ShowShellDialogAsync`).
9. Update `_gridSettings.GridTemplateColumns` to include the two new columns.
10. Change `OpenCreateDialogAsync` →
    `Nav.NavigateTo("/students/grade-levels/create")`.
11. Change `OpenEditDialogAsync` →
    `Nav.NavigateTo($"/students/grade-levels/{row.Id}/edit")`.
12. Remove `GradeLevelCreateDialog`/`GradeLevelEditDialog` `using`s and model
    references.

### Phase 4 — Create/Edit pages + form fields

13. Create `Components/Pages/Students/GradeLevels/Create.razor`
    (`@page "/students/grade-levels/create"`) using `GradeLevelFormFields`
    with SubmitLabel "Create". On submit: resolve coded value,
    `GetOrCreateGradeLevelAsync`, apply topic diff, navigate to landing.
14. Create `Components/Pages/Students/GradeLevels/Edit.razor`
    (`@page "/students/grade-levels/{id:guid}/edit"`) using
    `GradeLevelFormFields` with SubmitLabel "Save". On submit:
    `UpdateGradeLevelAsync`, apply topic diff, navigate to landing.
15. Extend `GradeLevelFormFields.razor` with the topics multi-select grid
    (absorb the `FluentListbox`/checkbox pattern from the deleted dialogs).
    Add `TopicIds` and `TopicIdToAssignmentId` to `GradeLevelFormModel`.
16. Delete `GradeLevelCreateDialog.razor` and `GradeLevelEditDialog.razor`.

### Phase 5 — Strand/Lesson dialogs

17. Create `Components/Students/StrandsDialog.razor`
    (`DialogShellBase`-derived or plain `IDialogService` dialog) that loads
    effective topics + strands, renders grouped sections, and supports
    add/edit/delete per §4 FR-14..17.
18. Create `Components/Students/LessonsDialog.razor` — analogous, with strand
    picker on add/edit and the two-step create+assign flow per FR-19/20.

### Phase 6 — Tests

19. Update `ListGradeLevelsForLandingHandler` unit tests: assert
    `StrandCount`/`LessonCount` for a grade with known topics/strands/lessons.
20. Add bUnit tests for `GradeLevels.razor`: Strands/Lessons columns render,
    anchors open dialogs.
21. Add bUnit tests for `Create.razor`/`Edit.razor`: form renders, topics grid
    pre-checks on edit, submit calls correct API methods.
22. Add bUnit tests for `StrandsDialog`/`LessonsDialog`: grouped list renders,
## 9. Test plan

### Handler unit tests (`ListGradeLevelsForLandingHandlerTests`)

- Grade with 2 effective topics, 3 strands on topic A + 1 on topic B →
  `StrandCount == 4`.
- Same setup, 5 lessons on A + 2 on B → `LessonCount == 7`.
- Grade with an archived topic (EndDate < today) → its strands/lessons
  excluded.
- No current period → `StrandCount`/`LessonCount` still computed (not gated
  on period).

### bUnit tests

- `GradeLevels.razor` — renders Strands/Lessons columns with correct counts
  from a mocked landing response; Strands anchor click invokes
  `ShowShellDialogAsync<StrandsDialog,…>`; Lessons anchor likewise.
- `Create.razor` — renders `GradeLevelFormFields`; picking a grade + checking
  topics + submit calls `GetOrCreateGradeLevelAsync` then
  `AssignGradeTopicAsync` per checked topic; navigates to landing on success.
- `Edit.razor` — loads grade + assignments; pre-checks assigned topics;
  submit calls `UpdateGradeLevelAsync` + diff; navigates on success.
- `StrandsDialog` — loads 2 topics with strands; renders 2 sections; Add
  under topic A calls `CreateTopicStrandAsync`; Edit calls
  `UpdateTopicStrandAsync`; Delete (after confirm) calls
  `DeleteTopicStrandAsync`; list refreshes.
- `LessonsDialog` — loads 2 topics with lessons; Add with a strand picked
  calls `CreateTopicLessonAsync` then `AssignLessonStrandAsync`; Edit with
  strand change calls `UpdateTopicLessonAsync` + `AssignLessonStrandAsync`;
  Delete calls `DeleteTopicLessonAsync`.

## 10. Open questions / follow-ups

1. **Strand filter in LessonsDialog.** The default view shows the full lesson
   catalog. A per-strand filter dropdown inside the dialog is a natural
   follow-up (the route already supports `?strandId=…`). Not in this spec.
2. **Landing reload after dialog mutations.** The dialogs refresh their own
   internal lists but do not force a landing reload. If strict count
   freshness is required, the dialog could return a "dirty" signal and the
   landing could reload on close — follow-up.
3. **Per-strand student counts on landing.** Out of scope (flagged in the
   prior spec §11.2); the strand-as-coded-value model is separate from
   `TopicStrand`.
4. **Routable deep-link to StrandsDialog/LessonsDialog.** Today they open
   only via the landing anchor. A deep-link route like
   `/students/grade-levels/{id}/strands` is possible but not required; the
   dialogs are non-routable by design (folder convention).

## 11. What this spec supersedes

- `grade-level-simplified-management.md` **§4–§5 (dialog-based create/edit)**
  — the `GradeLevelCreateDialog`/`GradeLevelEditDialog` are replaced by
  routable `Create.razor`/`Edit.razor` pages + extended
  `GradeLevelFormFields.razor`. The validation-field and topic-diff logic is
  carried over verbatim.
- The landing columns section of `grade-level-setup.md` **§7** is
  **extended** (two new columns), not superseded — the existing aggregation
  is preserved and augmented.

## 12. Rollout order (one PR per step, each shippable)

1. **Backend: DTO + handler.** Add `StrandCount`/`LessonCount` to Core +
   Admin DTOs; extend handler projection + construction; update handler tests.
2. **API client: strand/lesson methods.** Add request records + 9 methods to
   `StudentsApiClient.cs`.
3. **Landing: columns + navigation.** Add Strands/Lessons columns; switch
   `+ New`/Edit to navigate; update grid template.
4. **Create/Edit pages + form fields.** Create the two routable pages;
   extend `GradeLevelFormFields` with the topics grid; delete the two old
   dialogs.
5. **Strands/Lessons dialogs.** Create `StrandsDialog.razor`/`LessonsDialog.razor`;
   wire landing anchors.
6. **Tests.** Handler + bUnit coverage.