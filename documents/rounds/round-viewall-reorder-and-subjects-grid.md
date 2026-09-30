Provider: pi/ollama-cloud — Tier 3 (full, UI round). Models: orchestrator ollama-cloud/glm-5.3-flash, plan-review ollama/glm-5.3:cloud, worker ollama-cloud/deepseek-v4.1-flash, diff-review ollama-cloud/kimi-k2.7-code, UI tester ollama/minimax-m3:cloud.
Round: viewall-reorder-and-subjects-grid | Base: 106536effd8e3eba44edca7a1631800fe0409b5a | Branch: feat/viewall-reorder-and-subjects-grid | Tree clean at round start | Round doc: documents/rounds/round-viewall-reorder-and-subjects-grid.md | Patch (parent writes): documents/rounds/diffs-viewall-reorder-and-subjects-grid.patch

# Round — viewall-reorder-and-subjects-grid

## Plan

### Goal

The grade-detail **Streams** and **Subjects** "View all" dialogs get **manual
reordering**, and the Subjects View-all gets promoted from a list of custom rows to a
**FluentDataGrid** with per-row icon actions. The ordering authority for BOTH lists is
the **per-grade bridge row** — `GradeStreamAssignment` and `GradeTopicAssignment` each
gain a `DisplayOrder` column; `Topic.DisplayOrder` (the per-tenant topic catalog
ordering) is untouched and only survives as a backfill tiebreaker. Reorder uses the
contact precedent (single-item order endpoint + swap semantics, move-up/move-down
buttons); **no drag-and-drop is introduced**.

### Plan corrections (parent-brief corrections found while verifying seams)

1. **Streams backfill source.** The brief says the streams backfill preserves
   "coded-value order". Two verified facts narrow that:
   - The coded values live **physically in the Settings database**
     (`GradeStreamAssignment.StreamCodedValueId` is a bare `Guid` with **no FK** —
     `src/Students/SchoolCollab.Students.Core/Domain/GradeStreamAssignment.cs`), so a
     Students EF migration **cannot join them** to backfill from `CodedValue.DisplayOrder`.
   - Today's **visible** streams order is NOT the coded-value order anyway: nothing in
     the UI sorts by `GradeStreamDto.DisplayOrder`. The list order the user sees is the
     API array order = the repository's `OrderBy(x => x.CreatedAt)`
     (`GradeStreamAssignmentRepository.ListByGradeLevelAsync`); `cv.DisplayOrder` is
     merely copied into the DTO and left unused by every consumer
     (`Detail.razor ReloadStreamsAsync`, `EnrollStudentDialog`, `StudentTransferDialog`
     consume the array as-is).
   **Correction:** the streams backfill preserves today's *visible* order by seeding
   `display_order` per grade from `row_number() over (partition by grade_level_id order
   by created_at, id)`, and `GradeStreamDto.DisplayOrder` is **re-sourced** from the
   bridge `DisplayOrder` (the coded-value display order stops being copied). Upgrade
   preserves today's visible order **up to the tiebreak** — the seeder batch-inserts its
   rows in one `AddRange` (`src/SchoolCollab.MigrationService/Seeding/GradeStreamAssignmentSeeder.cs:189`),
   so exact `created_at` ties within a grade are expected and the `id` tiebreak
   (identical to the reorder normaliser's `(CreatedAt, Id)`) is what makes the backfill
   deterministic; the owner's "preserve today's visible order" intent is met via the
   order the UI actually renders today.
2. **There are two `GradeTopicCurriculumDto` records.** A Core DTO
   (`src/Students/SchoolCollab.Students.Core/DTOs/GradeTopicCurriculumDto.cs`, served by
   `ListGradeTopicCurriculumByGradeHandler` + the `/curriculum` endpoint) and a
   **mirror record** in
   `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs:177`
   (`GradeTopicsDialog.razor` binds the Application one via an alias `@using`). Both are
   `(TopicId, Name, Code, StrandCount, LessonCount)`. Exposing the bridge assignment id
   for the subjects PUT requires touching **both definitions** plus the handler, the
   endpoint's query, and the client mirror.
3. **`GradeTopicCurriculumDto` does not carry an assignment id today** — the subjects
   reorder PUT targets the *assignment* id (owner decision 2), so the DTO gains
   `AssignmentId`. (For streams nothing is needed: `GradeStreamDto.AssignmentId`
   already exists and flows into the reorder UI for free.)
4. **Streams "View all" is the shared generic `SectionListDialog<TItem>`** opened
   inside `SectionCard` (`ViewAllAsDialog="true"`, `SectionCard.OpenViewAllDialogAsync`),
   not a dedicated dialog. The reorder callbacks must therefore be wired from the page —
   the plan switches the Streams card to the `OnViewAllClick` escape hatch
   (`SectionCard` already documents this path for custom dialogs) and extends
   `SectionListDialog<TItem>` with **optional, additive** reorder entries, so every
   existing caller stays pixel-identical.

### Plan correction flag

Plan corrections 1–4 above are the only places the parent brief did not match the
code. Everything else in the brief verified as stated.

### Current state (verified seams)

**Streams bridge** — `src/Students/SchoolCollab.Students.Core/Domain/GradeStreamAssignment.cs`:
` ITenantEntity + IHasRowVersion (xmin)`, `GradeLevelId`, `StreamCodedValueId` (no FK,
Settings DB), unique `(tenant, grade_level, stream_coded_value)` index
(`ix_grade_stream_assignments_tenant_grade_stream`). **No order column.** Factory:
`Create(gradeLevelId, streamCodedValueId)` (no order). Call sites
(AssignGradeStreamHandler, GradeStreamAssignmentSeeder, ~15 test/integration files) all
compile against the 2-arg signature — a defaulted parameter keeps every one compiling.

**Streams read path** — `ListGradeStreamsHandler`
(`src/Students/SchoolCollab.Students.Core/CQRS/GradeStreams/Queries/ListGradeStreams/`)
projects `GradeStreamDto` (`AssignmentId, StreamCodedValueId, GradeLevelId, Code, Name,
NameOverride, Description, StreamVersion, IsOverridden, IsDisabled, DisplayOrder` — the
last copied from the Settings coded value today). Listing order = repository
`OrderBy(x => x.CreatedAt)`.
Route: `GET|POST|DELETE /students/grade-levels/{id}/streams[/{assignmentId}]`
(`src/Students/SchoolCollab.Students.Api/Endpoints/GradeLevelRoutes.cs`, which maps into
the `/students` group).

**Streams consumers** — grade-detail Streams card +
`ViewAllAsDialog` → generic `SectionListDialog<GradeStreamDto>`
(`src/Students/SchoolCollab.Students.Application/Components/Students/SectionListDialog.razor`,
presentational, Content-key based); enroll/transfer pickers
(`EnrollStudentDialog.razor`, `StudentTransferDialog.razor`) consume
`StudentsApiClient.ListGradeStreamsAsync` array order as-is.

**Subjects bridge** — `GradeTopicAssignment : TopicAssignment` (TPH root
`topic_assignments`, discriminator `topic_assignment_type`; per-grade filtered unique
index `ix_topic_assignments_tenant_grade_topic_unique`). **No order column.**
Factory: `Create(gradeLevelId, topicId, startDate, endDate?, topicStrandId?, periodId?)`.
Call sites: `AssignGradeTopicHandler`, `CreateTopicForGradeHandler`,
`GetOrCreateTopicHandler`, ~20 integration/unit test files.

**Subjects read path** — `ListGradeTopicCurriculumByGradeHandler`
(`.../CQRS/TopicAssignments/Queries/ListGradeTopicCurriculumByGrade/`): joins
`GradeTopicAssignments` with `db.Topics`, **orders by `t.DisplayOrder, t.Name`** (the
per-tenant Topic catalog order), pads strand/lesson counts, cached in `HybridCache`
under tag `students` (5-min entry). Route: `GET /students/grade-levels/{id}/curriculum`
(`GradeLevelRoutes.cs`). Assign/remove flow:
`POST /students/topic-assignments/grade`, `DELETE /students/topic-assignments/{id}`
(`TopicAssignmentRoutes.cs` + `AssignGradeTopicHandler` / `RemoveTopicAssignmentHandler`
— both already evict the `students` cache tag).

**View-all surfaces** —
- Streams card: `ViewAllAsDialog="true"` → `SectionListDialog<TItem>` opened by
  `SectionCard` itself; list markup `.section-list-dialog__row`, read-only, no actions.
- Subjects card: `OnViewAllClick="OpenTopicsDialogAsync"` → `GradeTopicsDialog.razor`
  (custom rows: name+code, strands anchor, lessons, exception-count badge, kebab
  `RowActionsMenu` with Strands/Teachers/Exceptions/Remove; assign picker + Remove/
  Assign callbacks from `Detail.razor`, refresh via `OnChangedKey`). The dialog binds
  the **Application** `GradeTopicCurriculumDto` (alias at line 3).

**Reorder precedent (semantics, not verb)** —
`ContactRoutes.cs`: `POST /{id}/order` body `SetContactOrderRequest(int Order)` →
`SetContactOrder` command → `SetContactOrderHandler` (repository set-order;
`DbUpdateConcurrencyException` → `ConcurrencyException`) + `POST /reorder` bulk.
Routes catch `ContactNotFoundException` → 404, `ConcurrencyException` → 409
`Results.Conflict(new { ex.Message })`.
UI: `ContactsEditor.razor` `MoveUpAsync`/`MoveDownAsync` swap neighbors in the ordered
id list then `ReorderLiveAsync` → `Api.ReorderContactsAsync`. **No drag-and-drop exists
anywhere in the repo** (verified) — move-up/move-down buttons are the mandated pattern.

**FluentDataGrid-in-dialog precedent** —
`src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriodsListDialog.razor`:
read-only dialog pattern (not DialogShellBase; `ShowReadonlyDialogAsync` +
`Content.TryGet<T>` keys + cascading `FluentDialog` close), embeds a
`FluentDataGrid` with `TemplateColumn`s, per-row `FluentButton` icon actions, reload +
`OnChanged` parent-refresh.

**bUnit precedent** — `tests/SchoolCollab.Students.Tests.Unit/PeriodFormSequenceOptionsTests.cs`
(`: BunitContext`, loose JS interop, `Services.AddFluentUIComponents()`).

**Migration** — students migrations live in
`src/Students/SchoolCollab.Students.Core/Migrations/` (e.g.
`20260929203106_AddGradeStreamAssignments.cs`). `Topic.DisplayOrder` is per-tenant and
in the **Students** db (`topics` table) — a Students migration CAN join it for the
subjects backfill.

**Exception-count seam (subjects grid)** — `Detail.razor` already builds
`ExceptionCountsByTopicKey` (a per-topic COUNT map from `_exceptions`) and passes it to
`GradeTopicsDialog`; `GradeTopicsDialog.ExceptionCount(topicId)` renders the badge.
The grid reuses exactly this map — no new exceptions query.

### Owner-pinned decisions (NOT reopened)

1. **Ordering authority = the per-grade bridge** for BOTH lists: add `DisplayOrder`
   (int) to `GradeStreamAssignment` and to `GradeTopicAssignment` (TPH subtype —
   nullable `display_order` column on the shared `topic_assignments` table; null for
   activity-group rows is fine and never read). `Topic.DisplayOrder` untouched.
2. **Endpoint shape: single-item PUT** (owner chose it to prevent over-posting ids):
   - `PUT /students/grade-levels/{gradeLevelId}/streams/{assignmentId}/order`, body
     `{ "order": n }`.
   - `PUT /students/topic-assignments/{assignmentId}/order` (same body) for subjects —
     same swap semantics on the topic-assignment row; the grade scoping is implicit in
     the row (404 when the id's grade does not match, see semantics below).
   - **Swap/replace semantics**: the mover takes `order`; the item currently at that
     position takes the mover's old order (no full-list payload, no shift).
   - The handler MUST first normalise that grade's list into unique contiguous orders
     (sort by current `DisplayOrder`, then a stable tiebreaker — bridge tables carry
     neither code nor name, so the tiebreaker is `(CreatedAt, Id)`; for topics the
     `(Topic.DisplayOrder, Topic.Name, CreatedAt, Id)` join ordering from today's
     listing is the backfill key) because the migration backfill can leave duplicates;
     then apply the swap. Out-of-range `order` (< 0 or >= count) → 400; items not in
     that grade/tenant → 404; row-version conflict → 409 (mirror the `ContactRoutes`
     catch shape: `Results.Conflict(new { ex.Message })`).
3. **UI:** streams View-all keeps its current list markup (the generic
   `SectionListDialog` row layout) and gains per-row move-up/move-down buttons + a
   position indicator; subjects View-all adopts a `FluentDataGrid` showing subject,
   strand count, lesson count, exception count, with edit + delete icon actions plus
   reorder controls. Reuse the existing `Detail.razor` callbacks (edit / remove /
   exceptions / strands / teachers) instead of inventing new flows.
4. **Migration:** one EF migration in Students adding the two order columns + backfill
   preserving today's visible order (streams ← `created_at` listing order [see Plan
   correction 1]; subjects ← today's curricula order `topic.display_order, topic.name`).
   Keep `GradeStreamAssignment.Create(...)` / `GradeTopicAssignment.Create(...)`
   signatures source-compatible (default the new value; add `SetDisplayOrder`) so the
   seeder and every other caller needs no edit and the round's diff stays off
   `GradeStreamAssignmentSeeder.cs`.
5. **Client/CQRS layer in scope:** commands `SetGradeStreamOrder`,
   `SetGradeTopicOrder(Guid AssignmentId, int Order)` (P2-1: no `GradeLevelId` — the
   id-only subject route cannot supply a trustworthy scope; the handler derives scoping
   from the loaded, tenant-filtered row) + handlers + repository access, `StudentsApiClient` methods, the bridge assignment
   id exposed where the PUT needs it (streams already have it; subjects via Plan
   correction 2), and every listing query ordered by the bridge `DisplayOrder`.
6. **Tests for every new behaviour** (swap, normalisation with duplicate orders, 400
   out-of-range, 404 not-in-grade, 409 conflict) plus bUnit for the reorder buttons and
   the subjects grid.

### Scope (in)

**New files (12):**

| # | File |
|---|------|
| 1 | `src/Students/SchoolCollab.Students.Core/CQRS/GradeStreams/Commands/SetGradeStreamOrder/SetGradeStreamOrder.cs` — `record SetGradeStreamOrder(Guid GradeLevelId, Guid AssignmentId, int Order) : ICommand` |
| 2 | `src/Students/SchoolCollab.Students.Core/CQRS/GradeStreams/Commands/SetGradeStreamOrder/SetGradeStreamOrderHandler.cs` — invalidates the `students` cache tag (P2-4: sibling parity — `AssignGradeStreamHandler` and `RemoveGradeStreamHandler` both inject `HybridCache` and evict it) |
| 3 | `src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/Commands/SetGradeTopicOrder/SetGradeTopicOrder.cs` — `record SetGradeTopicOrder(Guid AssignmentId, int Order) : ICommand` — **P2-1: no `GradeLevelId`** (an id-only route cannot supply a trustworthy scope; the handler derives grade/tenant scoping from the loaded row) |
| 4 | `src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/Commands/SetGradeTopicOrder/SetGradeTopicOrderHandler.cs` |
| 5 | `src/Students/SchoolCollab.Students.Core/Migrations/<ts>_AddBridgeDisplayOrders.cs` (+ Designer.cs) — one migration, two columns + backfill SQL |
| 6 | `tests/SchoolCollab.Students.Tests.Unit/SetGradeStreamOrderHandlerTests.cs` |
| 7 | `tests/SchoolCollab.Students.Tests.Unit/SetGradeTopicOrderHandlerTests.cs` |
| 8 | `tests/SchoolCollab.Students.Tests.Unit/SectionListDialogReorderTests.cs` (bUnit — streams View-all reorder buttons) |
| 9 | `tests/SchoolCollab.Students.Tests.Unit/GradeTopicsDialogGridTests.cs` (bUnit — subjects FluentDataGrid) |
| 10 | `docs` step output: `documents/solution/grade-bridge-display-order.md` (Finding → Implementation standard) |
| 10a | `tests/SchoolCollab.Students.Tests.Unit/AssignGradeTopicHandlerTests.cs` — **new** file (none exists today); covers the grade-topic assign handler including the AC9 append-at-end assertion (`DisplayOrder == max + 1`) |
| 11 | `documents/rounds/diffs-viewall-reorder-and-subjects-grid.patch` — **parent-written**, listed here for completeness only |

**Modified files (22 base rows + 14a/14b/22a from P1-1 + updated rows 5–8/13/16/17/19/20 from P1-1/P1-2/P2-1):**

| # | File | Change |
|---|------|--------|
| 1 | `Domain/GradeStreamAssignment.cs` | `public int DisplayOrder { get; private set; }`, `SetDisplayOrder(int)`, `Create(..., int displayOrder = 0)` (source-compatible default) |
| 2 | `Domain/GradeTopicAssignment.cs` | same on the grade subtype (`DisplayOrder`, `SetDisplayOrder`, defaulted param) |
| 3 | `Data/Configurations/GradeStreamAssignmentConfiguration.cs` | map `display_order int not null default 0` |
| 4 | `Data/Configurations/GradeTopicAssignmentConfiguration.cs` | map nullable `display_order` (TPH shared column; only grade rows populated) |
| 5 | `Data/Repositories/GradeStreamAssignmentRepository.cs` | `ListByGradeLevelAsync` orders by `DisplayOrder, CreatedAt`; add a **tracked** `ListByGradeLevelForUpdateAsync` (swap needs concurrency-tracked rows; today's listing is `AsNoTracking`) |
| 6 | `Data/Repositories/IGradeStreamAssignmentRepository.cs` | declare it |
| 7 | `Data/Repositories/GradeTopicAssignmentRepository.cs` | `ListByGradeLevelAsync` orders by `DisplayOrder` (bridge, then existing tiebreaks); add `ListByGradeLevelForUpdateAsync` |
| 8 | `Data/Repositories/IGradeTopicAssignmentRepository.cs` | declare it |
| 9 | `DTOs/GradeStreamDto.cs` | `DisplayOrder` re-sourced from the **bridge** (doc-comment update; shape unchanged) |
| 10 | `DTOs/GradeTopicCurriculumDto.cs` | add `Guid AssignmentId` |
| 11 | `CQRS/GradeStreams/Queries/ListGradeStreams/ListGradeStreamsHandler.cs` | copy bridge `DisplayOrder` into the DTO (replaces the coded-value copy) |
| 12 | `CQRS/TopicAssignments/Queries/ListGradeTopicCurriculumByGrade/ListGradeTopicCurriculumByGradeHandler.cs` | order by bridge `a.DisplayOrder` (backfill guarantees contiguous; keep `t.DisplayOrder, t.Name` as tiebreak), select the assignment id, project into the extended DTO |
| 13 | `CQRS/GradeStreams/Commands/AssignGradeStream/AssignGradeStreamHandler.cs` | stamp append-at-end order (`await GetNextDisplayOrderAsync(...)` — rows 5/6) on the created row |
| 14 | `CQRS/TopicAssignments/Commands/AssignGradeTopic/AssignGradeTopicHandler.cs` | same for grade topic assignments (stamp on the bridge-create branch) |
| 14a | `CQRS/Topics/Commands/CreateTopicForGrade/CreateTopicForGradeHandler.cs` | **P1-1:** stamp append-at-end order via `GetNextDisplayOrderAsync` on the bridge-create branch **only** (inside the existing skip-when-active guard — the shared-topic/no-new-bridge path never stamps; behaviour otherwise unchanged) |
| 14b | `CQRS/Topics/Commands/GetOrCreateTopic/GetOrCreateTopicHandler.cs` | **P1-1:** same stamp on the bridge-create branch only (existing skip-when-active guard kept) |
| 15 | `Api/Endpoints/GradeLevelRoutes.cs` | `PUT .../streams/{assignmentId}/order` (400/404/409 catches per decision 2) |
| 16 | `Api/Endpoints/TopicAssignmentRoutes.cs` | `PUT .../{id}/order` (same shape; command carries **no** grade id — P2-1) |
| 17 | `Application/Services/StudentsApiClient.cs` | extend the **Application mirror** `GradeTopicCurriculumDto` with `AssignmentId`; add `SetGradeStreamOrderAsync(gradeLevelId, assignmentId, order)` + `SetGradeTopicOrderAsync(assignmentId, order)` (PUT `AsJsonAsync`); `AssignGradeTopicAsync` already returns `Task<Guid>` (verified, line 1696) — this is the real assignment id the optimistic append consumes (P1-2) |
| 18 | `Components/Students/SectionListDialog.razor` (+ `.razor.css`) | optional reorder entries: when `MoveUp`/`MoveDown` `Func<TItem, Task>` callbacks arrive in `Content`, render per-row ▲/▼ buttons and a position indicator (`n of N`), optimistically re-swap `_items` locally and refresh via an optional `OnChanged`; absent keys ⇒ rendered exactly as today (all existing callers unaffected) |
| 19 | `Components/Students/GradeTopicsDialog.razor` (+ `.razor.css`) | replace the foreach rows with a `FluentDataGrid` (SubPeriodsListDialog precedent): columns Subject | Strands | Lessons | Exceptions | Order (▲/▼ + position) | Actions (edit + delete **icon** buttons; keep RowActionsMenu? — see Implementation step 7). New `Edit`/`MoveUp`/`MoveDown`/`OrderIndex` keys; existing `Topics/UnassignedTopics/OpenStrands/Remove/Assign/OnChanged/OpenEnrollmentExceptions/ExceptionCountsByTopic` keys preserved. **P1-2:** `AssignKey` widened to `Func<Guid, Task<Guid>>`; `ConfirmAssignAsync` appends the new grid row with the **real** assignment id returned by the callback (today it appends `new GradeTopicCurriculumDto(topic.Id, topic.Name, topic.Code, 0, 0)` — line 194 — which would carry an empty id and 404 on its first ▲/▼) |
| 20 | `Components/Pages/Students/GradeLevels/Detail.razor` | switch Streams card `ViewAllAsDialog="true"` → `OnViewAllClick="OpenStreamsViewAllAsync"` opening `SectionListDialog<GradeStreamDto>` with the reorder callbacks + `OnChanged=ReloadStreamsAsync`; pass the new `Edit`/`MoveUp`/`MoveDown` keys into `OpenTopicsDialogAsync` (wiring to `OpenTopicEditAsync` and `Api.SetGradeTopicOrderAsync`); **P1-2:** `AssignKey` becomes `Func<Guid, Task<Guid>>` with `AssignTopicAsync` returning the assignment id from `Api.AssignGradeTopicAsync`, and the `CurriculumRow` fallback constructor (line ~359) passes a defaulted `Guid.Empty` `AssignmentId` |
| 21 | `tests/SchoolCollab.Students.Tests.Unit/ListGradeStreamsHandlerTests.cs` | new case(s): listing ordered by bridge `DisplayOrder`; DTO `DisplayOrder` no longer the coded-value's |
| 22 | `tests/SchoolCollab.Students.Tests.Unit/ListGradeTopicCurriculumByGradeHandlerTests.cs` | new case(s): ordering by bridge `DisplayOrder`; `AssignmentId` populated |
| 22a | `tests/SchoolCollab.Students.Tests.Unit/AssignGradeStreamHandlerTests.cs` (existing) + `CreateSubjectForGradeHandlerTests.cs` + `GetOrCreateSubjectHandlerTests.cs` | new case(s): after create, the appended bridge row carries `DisplayOrder == max + 1` (AC9 assertions land here — P1-1 covers all three subject sites) |
| — | `StudentsDbContextModelSnapshot.cs` | regenerated by `dotnet ef migrations add` |

### Scope (out)

- No drag-and-drop, no bulk-reorder endpoint, no `POST /reorder` verb for these lists.
- `Topic.DisplayOrder` and the Topics/Subjects landing pages (the subject **catalog**
  ordering) are untouched — the bridge order reorders per-**grade** lists only.
- `GradeStreamAssignmentSeeder.cs` untouched (factory default keeps it compiling).
- Activity-group topic assignments get no order surface (column exists, null; no UI).
- No changes to the Settings context or coded values.
- No landing-page ordering changes; the grade-level landing counts/sort stay as-is.
- No new feature flags; endpoints join the existing `/students` group wiring as-is.
- No `ContactRoutes`/`ContactsEditor` changes (semantics only are copied).

### Implementation (ordered)

Ordered: domain → migration/backfill → CQRS → endpoints → client → UI → tests → docs.
Build (`dotnet build SchoolCollab.slnx`) after every code step per AGENTS.md.

1. **Domain.** `GradeStreamAssignment`: `DisplayOrder` (int, get-private),
   `SetDisplayOrder(int)`, `Create(Guid, Guid, int displayOrder = 0)`. Same on
   `GradeTopicAssignment` (subtype field + `SetDisplayOrder`; `Create` gains a last
   optional `int displayOrder = 0`). Private-set properties + `UpdatedAt` stamp, in
   line with existing auditable-property patterns (read
   `.github/copilot/rules/dotnet-best-practices.md` first).
2. **Configuration.** Map the properties (streams: non-null `default 0`; topics:
   nullable column on the TPH-shared table). Update the `ListByGradeLevelAsync` order
   clauses in both repositories (5+7) and add the tracked for-update loads plus the
   `GetNextDisplayOrderAsync` append helpers; declare all three on the two interfaces.
3. **Migration + backfill.** One migration `<ts>_AddBridgeDisplayOrders` in
   `SchoolCollab.Students.Core/Migrations`:
   - `grade_stream_assignments`: `ADD COLUMN display_order integer NOT NULL DEFAULT 0`,
     backfill `UPDATE ... SET display_order = rn-1` from
     `row_number() OVER (PARTITION BY grade_level_id ORDER BY created_at, id)` —
     today's visible order (Plan correction 1). **P2-2:** `id` is mandatory in the
     `ORDER BY`: the seeder batch-inserts, so `created_at` ties are expected and the
     tiebreak is what makes the backfill deterministic (it matches the normaliser's
     `(CreatedAt, Id)`).
   - `topic_assignments`: `ADD COLUMN display_order integer NULL` (TPH shared),
     backfill for `topic_assignment_type = 'grade'` replicating today's listing order —
     per grade over its rows ordered by `topics.display_order, topics.name,
     start_date, id` → contiguous 0..n (SQL join `topics` on `topic_id`).
   - Verify seeder untouched and its tests still pass (factory default 0 + normalise-on-
     swap and `created_at` tiebreak keep seeded DBs stable).
4. **Commands + handlers.** `SetGradeStreamOrder` /
   `SetGradeTopicOrder(Guid AssignmentId, int Order)` + handlers:
   1. Load the grade's rows **tracked** (`ListByGradeLevelForUpdateAsync`); **P2-3:**
      the topics loader returns the **effective-on-today** set (`StartDate <= today &&
      (EndDate == null || EndDate >= today)`) — the same set `/curriculum` lists to the
      grid and the same set the 400 bound is computed from; the streams twin returns the
      grade's full bridge set (streams carry no effective window). For streams the mover
      must exist and carry `GradeLevelId == command.GradeLevelId` (the id-only streams
      route has both ids); for topics the command carries NO grade id (P2-1) — the mover
      must simply exist within the tenant filter, else the 404 path (`GradeStream/GradeTopic
      exceptions` — mirror `RemoveGradeStream`'s `InvalidOperationException` → 404 or a
      per-handler exception type; simplest parity: throw the not-found exception the
      route catches → 404).
   2. Validate `0 <= order < count` else throw `ArgumentOutOfRangeException` (→ 400).
   3. Normalise: sort by `(DisplayOrder, CreatedAt, Id)`, stamp `i = 0..n-1` (idempotent
      — makes post-backfill duplicates impossible to trip the swap).
   4. Swap: `mover.SetDisplayOrder(order)` and the row currently holding `order`
      (pre-normalisation index lookup) → `SetDisplayOrder(moverOldOrder)`; if the
      mover IS the target row and `order` equals its own — no-op, still 204.
   5. `SaveChanges` inside try/catch `DbUpdateConcurrencyException` →
      `ConcurrencyException` (mirror `SetContactOrderHandler`); invalidate the `students`
      cache tag. **P2-4 (seam corrected):** the earlier draft's "the streams handler has
      no cache" is wrong — both `AssignGradeStreamHandler` and `RemoveGradeStreamHandler`
      inject `HybridCache` and evict the `students` tag (verified), so the new
      `SetGradeStreamOrderHandler` evicts the same tag (sibling parity), as does
      `SetGradeTopicOrderHandler`.
   6. **P1-1 — append-at-end on every bridge-create site (centralised):** rather than
      stamping `max+1` inline at each create site, add `GetNextDisplayOrderAsync(
      gradeLevelId)` to both bridge repositories (modified-files rows 5–8) and stamp
      `DisplayOrder = await GetNextDisplayOrderAsync(...)` **only on the
      bridge-create branch** of all four create sites: `AssignGradeStreamHandler`,
      `AssignGradeTopicHandler`, `CreateTopicForGradeHandler`,
      `GetOrCreateTopicHandler`. The two topic-side create handlers stamp strictly inside
      their existing skip-when-active guards, so the shared-topic/no-new-bridge path
      never stamps (their otherwise-unchanged behaviour is preserved). Factory default
      stays `0` (seeder + all other `Create` callers compile unchanged); only these four
      handlers stamp the append value.
5. **Endpoints.** `GradeLevelRoutes`: `PUT /grade-levels/{gradeLevelId}/streams/{assignmentId}/order`
   with `internal record SetGradeStreamOrderRequest(int Order)`; catch
   `ArgumentOutOfRangeException` → 400, not-found → 404, `ConcurrencyException` → 409
   (`Results.Conflict(new { ex.Message })` — exact ContactRoutes catch shape).
   `TopicAssignmentRoutes`: `PUT /topic-assignments/{assignmentId}/order`, command
   `SetGradeTopicOrder(Guid AssignmentId, int Order)` — **P2-1: no `GradeLevelId`** (an
   id-only route cannot supply a trustworthy scope); the handler derives grade scoping
   from the loaded, tenant-filtered row itself, and an unknown or other-tenant
   assignment id takes the 404 path (AC5). Both return `Results.NoContent()`.
6. **Listing queries.** `ListGradeStreamsHandler`: keep catalogue resolution; copy
   `assignment.DisplayOrder` into the DTO (rows already ordered by the repository
   update in step 2 — assert the handler returns in array order). `ListGradeTopicCurriculumByGradeHandler`:
   order by `a.DisplayOrder, t.DisplayOrder, t.Name` (bridge first, existing topic
   order as tiebreak for any late-normalised duplicates), select `a.Id AS AssignmentId`,
   extend the projection + cache key unchanged (order affects the cached payload only;
   reordering evicts via the existing `"students"` tag).
7. **UI — streams.** `SectionListDialog<TItem>` gains optional keys
   (`MoveUpFn`, `MoveDownFn`, `OnChangedFn`, plus a `MoveError` string). When the
   move-up callback for an item is present, render per-row ▲/▼ `FluentButton` icon
   actions + `n / N` position indicator; the button click performs the API call via
   the callback, then swaps the two neighbours in `_items` and re-renders (on false →
   error message bar; restore order). Move-at-ends buttons disabled. `Detail.razor`
   passes `Func<GradeStreamDto, Task>` wrapping `Api.SetGradeStreamOrderAsync(Id,
   s.AssignmentId, position)` where `position` is the target index = index-of-neighbour,
   and `OnChanged = ReloadStreamsAsync`. The card keeps its data flow; only the
   View-all open path changes (`ViewAllAsDialog` → `OnViewAllClick`).
8. **UI — subjects.** `GradeTopicsDialog.razor`: the `.topic-dialog__list` foreach
   becomes a `FluentDataGrid<TGridItem="GradeTopicCurriculumDto"`-style template grid
   (items = `Topics`, per SubPeriodsListDialog): columns **Subject** (name + code),
   **Strands** (anchor via `OpenStrandsAsync`), **Lessons**, **Exceptions** (existing
   count map; 0 renders nothing), **Order** (n / N + ▲/▼ buttons hidden at ends),
   **Actions** — edit + delete icon buttons wired to the NEW `Edit` key (→ page's
   `OpenTopicEditAsync`) and the existing `Remove` key; the existing
   `RowActionsMenu` flows (Strands/Teachers/Exceptions/Remove) are preserved as-is
   — the grid ADDS icon shortcuts, it does not replace the kebab unless the worker
   finds the column too crowded (then keep the kebab and skip the icons — do not
   silently drop either affordance without flagging in the Worker Report). Assign
   picker row and footer stay unchanged. Reorder: `MoveUp`/`MoveDown` callbacks →
   `Api.SetGradeTopicOrderAsync(Id, dto.AssignmentId, targetIndex)` then locally swap
   the grid's backing array (and keep `OnChanged` for the page/card refresh).
   `Detail.razor`'s `OpenTopicsDialogAsync` passes the two new callbacks (+
   `OpenEditKey → OpenTopicEditAsync`) — no new API surface on the page. **P1-2** —
   the optimistic assign append must learn the real assignment id: the `Assign` key is
   widened to `Func<Guid, Task<Guid>>` (`StudentsApiClient.AssignGradeTopicAsync`
   verified: `Task<Guid>`, line 1696); `Detail.razor`'s `AssignTopicAsync` returns the
   assignment id from that call, `ConfirmAssignAsync` appends the grid row with it
   (replacing the empty-id `new GradeTopicCurriculumDto(topic.Id, ..., 0, 0)` at line
   194), and `Detail.razor`'s `CurriculumRow` fallback constructor passes a defaulted
   `Guid.Empty` `AssignmentId`. Without this, the appended row's first ▲/▼ PUTs an
   empty guid → 404.
9. **Client.** `StudentsApiClient`: mirror-DTO update (Plan correction 2) +
   `SetGradeStreamOrderAsync(Guid gradeLevelId, Guid assignmentId, int order)` and
   `SetGradeTopicOrderAsync(Guid assignmentId, int order)` —
   `PutAsJsonAsync` with `{ order }`, `EnsureSuccessStatusCode` (topic PUT is id-only —
   no grade scope is sent).
10. **Tests** (each named target listed under Acceptance criteria). Handler tests use
    the existing `StudentsTestScope`/`InMemory*` scaffolding shapes; bUnit follows
    `PeriodFormSequenceOptionsTests` (loose JS, Fluent components). The AC9
    append-at-end assertions land in `AssignGradeStreamHandlerTests` (existing,
    modified), `AssignGradeTopicHandlerTests` (new file — none exists today), and the
    bridge-create branches exercised by `CreateSubjectForGradeHandlerTests` and
    `GetOrCreateSubjectHandlerTests`.
11. **Docs.** `documents/solution/grade-bridge-display-order.md`: the finding
    (why the bridge is the authority; the cross-DB constraint that forced the
    `created_at` streams backfill — Plan correction 1) + implementation steps +
    verification results, per the AGENTS.md Finding → Implementation standard.

### Acceptance criteria

Each criterion names the test that proves it; **D** = discriminating (fails against
pre-change code), **N** = non-discriminating regression guard.

| # | Criterion | Test | D/N |
|---|-----------|------|-----|
| 1 | Stream swap: PUT order n moves the stream to position n and the displaced stream takes the mover's old order — no other row's order changes | `SetGradeStreamOrderHandlerTests.Swap_MovesMoverToTargetPosition_DisplacedTakesMoverOldOrder` | **D** |
| 2 | Subject swap: identical semantics on `GradeTopicAssignment` | `SetGradeTopicOrderHandlerTests.Swap_MovesAssignmentToTargetPosition_DisplacedTakesMoverOldOrder` | **D** |
| 3 | Duplicate orders are normalised BEFORE the swap (backfill artefact safety): a grade whose rows all carry order 0 still swaps deterministically and ends contiguous 0..n-1 | duplicate-orders case in both `SetGrade*OrderHandlerTests` | **D** |
| 4 | Out-of-range `order` (< 0 or ≥ count) → handler throws, endpoint returns **400** | `SetGradeStreamOrderHandlerTests` / `SetGradeTopicOrderHandlerTests` out-of-range cases + endpoint mapping | **D** |
| 5 | Item not in that grade/tenant → **404** — streams: wrong grade id; subjects: **P2-1 rewrite — unknown or other-tenant assignment id → 404** (the command carries no `GradeLevelId`; the handler fails the load on the tenant-filtered row) | not-in-grade/other-tenant cases in both handler tests (mirrors `AssignGradeStreamHandlerTests` tenant-case shape) | **D** (no pre-change endpoint exists) |
| 6 | Row-version conflict → **409** with the ContactRoutes catch shape | conflict case in both handler tests (`DbUpdateConcurrencyException` → `ConcurrencyException`) | **D** |
| 7 | Streams listing ordered by **bridge** `DisplayOrder` and the DTO's `DisplayOrder` is the bridge value (not the coded value's) | `ListGradeStreamsHandlerTests` new cases | **D** |
| 8 | Subjects listing (curriculum) ordered by bridge `DisplayOrder`; `AssignmentId` populated in every row | `ListGradeTopicCurriculumByGradeHandlerTests` new cases | **D** |
| 9 | New bridge rows append at the END of their grade's list with bridge `DisplayOrder == max + 1` (append flow stamps via `GetNextDisplayOrderAsync`) — covers **all** `GradeTopicAssignment.Create` sites (P1-1) and the streams assign | assertions in `AssignGradeStreamHandlerTests` (existing, modified), `AssignGradeTopicHandlerTests` (new), `CreateSubjectForGradeHandlerTests` + `GetOrCreateSubjectHandlerTests` (bridge-create branches) | **D** (asserts the bridge value — a bare "new row appears last" would pass pre-change via `CreatedAt` ordering, so the criterion asserts the branch value itself) |
| 10 | Factory signatures stay source-compatible (seeder + all `Create` callers compile unchanged); `GradeStreamAssignmentSeederTests` still green | `tests/SchoolCollab.MigrationService.Tests.Unit/GradeStreamAssignmentSeederTests.cs` (existing, run unchanged) | N |
| 11 | Streams View-all shows move-up/move-down per row + position indicator; buttons invoke the move callback and swap the rendered order | `SectionListDialogReorderTests` (bUnit) | **D** |
| 12 | SectionListDialog WITHOUT reorder keys renders exactly as today (no reorder column) — existing callers unaffected | `SectionListDialogReorderTests` legacy-shape case | N |
| 13 | Subjects View-all renders a FluentDataGrid with Subject / Strands / Lessons / Exceptions columns; edit + delete icon actions invoke the page's existing `OpenTopicEditAsync`/`RemoveTopicAsync` callbacks; kebab actions intact | `GradeTopicsDialogGridTests` (bUnit) | **D** (pre-change: custom rows, no grid, no edit affordance in dialog) |
| 14 | Subjects grid shows exception counts from the existing `ExceptionCountsByTopic` map (count > 0 renders; 0 renders nothing) | `GradeTopicsDialogGridTests` | N (behaviour existed; new presenter) |
| 15 | Subjects grid reorder buttons invoke the `MoveUp`/`MoveDown` callbacks with the row's assignment id | `GradeTopicsDialogGridTests` | **D** |
| 16 | Endpoints are reachable with correct status codes through the route group (204 reorder set; 400/404/409 mapped) | extension method route registration assertion if the Unit API-test project has an equivalent (`GradeStreamEndpointAuthTests` shape) or covered via handler+route-mapping review | D (status mapping is new) |
| 17 | `dotnet build SchoolCollab.slnx` 0 errors; `dotnet test` 0 failures (incl. `SchoolCollab.Students.Tests.Integration` which seeds bridges — order defaults must not break landing/curriculum reads) | CI Build & Test | N |

### Parent-run verification (commands)

```bash
git rev-parse HEAD                      # must be 106536effd8e3eba44edca7a1631800fe0409b5a (round base) + worker commit(s)
git status --short                      # clean before accept
dotnet build SchoolCollab.slnx          # 0 errors
dotnet test                             # 0 failures (full suite; MTP)
# targeted, when iterating:
dotnet test tests/SchoolCollab.Students.Tests.Unit --filter "FullyQualifiedName~SetGradeStreamOrder|FullyQualifiedName~SetGradeTopicOrder"
dotnet test tests/SchoolCollab.Students.Tests.Unit --filter "FullyQualifiedName~SectionListDialogReorder|FullyQualifiedName~GradeTopicsDialogGrid"
dotnet test tests/SchoolCollab.MigrationService.Tests.Unit --filter "FullyQualifiedName~GradeStreamAssignmentSeederTests"
```

### UI-tester scope handover (bug-hunt surfaces)

App: grade-level detail page (`/students/grade-levels/{id}`).

1. **Streams card** — preview list order; add/edit/remove still work after the card's
   View-all path changed (`ViewAllAsDialog` → `OnViewAllClick`): the View-all link must
   still open the dialog with the full list.
2. **View all streams dialog** — reorder ▲/▼ at first/last rows (buttons disabled or
   hidden at ends — whichever the worker ships, both must not 400); position indicator
   correctness; after a move: dialog list AND underlying card both refreshed
   (`OnChanged`); error surface when the PUT fails (e.g. simulated 409).
3. **Order persistence** — reorder, close dialog, reopen: order kept; page reload:
   order kept; another browser/tenant: isolated.
4. **Enroll / transfer pickers** — stream picker order follows the new bridge order
   (`EnrollStudentDialog`, `StudentTransferDialog` — they consumed array order before;
   must still make sense, and the new explicit order must be what they show).
5. **View all subjects dialog** — grid correctness (columns, counts), kebab + new edit
   and delete icon actions (delete confirm + dialog list + card refresh), assign picker
   still prepends/refreshes, exceptions badge per row, strands anchor opens strands dialog.
6. **Subjects reorder** — ▲/▼ per row; swap persistence (reopen), card preview order
   after close; a move on a topic that a concurrent user removed → 404 surfaces as an
   error message, not a crash.
7. **Non-regression** — other `SectionListDialog` users (any other card using
   `ViewAllAsDialog`: search repo; currently only the grade-detail Streams card) and
   other `SectionCard` view-alls (teachers, students) unaffected.

### Owner-requested UI revision (post-implementation, parent-recorded)

**Owner instruction:** "increase dialog size for subjects 'view all' sectioncard to accommodate grid length. Preferably, I prefer icon buttons only for setting display order. keep these buttons start of row in first column."

Applied (rework run `18966abb`, owner-driven; not a reviewer finding):

1. `Detail.razor` `OpenTopicsDialogAsync` now opens the Subjects View-all at **`DialogSize.ExtraLarge`** (960px) with an explicit `height: "max(72vh, 480px)"` — the documented use of that parameter, since the dialog is a fixed-height box and content height alone does not grow it.
2. The ordering control is **icon-only** in both View-all surfaces: the `n of N` position text is gone (subjects grid, streams list).
3. The control **leads the row**: the subjects grid's `Order` column is now the first column, and the streams list renders its buttons before the name. The streams dialog's own `DialogSize` is unchanged (only the Subjects card asked for a larger shell).

**Follow-up fix (parent, same rework):** the round's `GradeTopicCurriculumDto.AssignmentId` addition broke one test call site the plan's P1-2 correction did not name — `tests/SchoolCollab.Admin.Tests.Unit/GradeDialogsBunitTests.cs:82`'s `Topic(...)` helper (5-arg constructor). It was invisible until now because the locked build never reached that project. The helper now passes a deterministic `BridgeAssignmentId`, which also keeps the move buttons away from an empty guid.

**Evidence after the revision:** `dotnet test tests/SchoolCollab.Students.Tests.Unit` → **609 / 0 failed** (608 + the new column-order pin). The locked build remains an environment limit, not a code result.

**Owner revision 3 — dialog height belongs to the CONTENT ROOT, not the dialog box.** Owner:
*"If you want height of dialog, set height using height of contents area … to force dialog
action buttons also positions to the bottoms, instead of display mid-section."* The
`height: "max(72vh, 480px)"` argument added in revision 1 was the wrong lever: FluentUI 4.14.2
pins the box (`::part(control)` = `calc(var(--dialog-height) - 2*padding)`, `--dialog-height`
defaulting to 480px) while the body (`.fluent-dialog-body`) stays `height: auto` — so the box
grew and the Close row was left **stranded mid-dialog** with dead space beneath it. Applied
the repo's own shipped answer to the identical problem
(`docs/plans/2026-08-18-dialog-min-height-and-guardian-contact-compact.md` §4.1, the fix
`StudentEditDialog` took):

1. `OpenTopicsDialogAsync` **drops the `height:` argument** — `DialogSize.ExtraLarge` still
   carries the 960px width.
2. `GradeTopicsDialog.razor.css` makes the content root the height authority: `.topic-dialog`
   gets `height: max(72vh, 480px)` + `overflow: hidden`; the scrolling region is a new
   `.topic-dialog__scroll` **wrapper** (`flex: 1 1 auto; min-height: 0; overflow-y: auto`) that
   the grid sits inside, the grid itself keeping its **natural height** (`width: 100%` only);
   `.dialog-footer` gains `margin-top: auto; flex: none` so the actions row lands on the
   dialog's bottom.
3. The rule is now recorded in the **dialog skill §7** (*"Dialog height — the content area is
   the height authority, not the box"*, including the grid rule "a grid inside the scroll
   region keeps its natural height") plus two matching checklist items, in **both** copies:
   `~/.pi/agent/projects-memory/School-Collab/skills/dialog-ui/SKILL.md` and the in-repo
   canonical `.github/skills/dialog-ui/SKILL.md` (the two had already drifted in their
   file-path references, so the section was inserted into each).

**Owner follow-up on revision 3 — the grid rows must not stretch.** Owner: *"This fix
stretched the grid rows height. grid height is not to be affected in expanding the content
area. the grid should be scrollable in content area, if height exceeds that of content area.
Preferably only body section of grid should be scrollable, keeping the grid headers fixed and
not in scroll."* Correct — step 2 above (as first written) gave the **grid**
`flex: 1 1 auto`, so the grid host grew to fill the capped root and FluentUI stretched its
rows to match. Final shape:

- the wrapper owns the scroll; the grid is `width: 100%` only — never a flex grow, never its
  own `overflow`/`max-height` (a bounded or overflowing grid becomes the sticky context
  itself and scrolls its header out of view — the constraint recorded in
  `LandingPage.razor.css`);
- the grid now uses `GenerateHeader="GenerateHeaderOption.Sticky"`, which marks its header
  row `row-type="sticky-header"` so FluentUI's own CSS
  (`position: sticky; top: 0; background-color: var(--neutral-fill-stealth-rest; z-index: 2`)
  pins it to the wrapper — **only the body scrolls**;
- three tests pin the shape: the CSS pins (the wrapper scrolls; the grid is `width: 100%`)
  and a markup pin (`tr[row-type='sticky-header']` present, `.fluent-data-grid` nested inside
  `.topic-dialog__scroll`).
4. The two source-pin tests in `GradeTopicsDialogGridTests` were re-pointed: the open call
   must NOT pass `height:`, and the CSS must carry the root height + the `min-height: 0`
   scroll region + the `margin-top: auto` footer.

**Streams View-all left as-is:** it was not part of the size request and its content root
still caps the list with `max-height: 55vh`; the same treatment is a one-file follow-up if
the two View-alls should look identical.

**Evidence (final):** `Students.Tests.Unit` **611 / 0** (the one height source-pin became two,
plus the sticky-header markup pin), `Admin.Tests.Unit` **599 / 0**, `ArchitectureTests.Unit`
**72 / 0**, `dotnet build SchoolCollab.slnx` → **0 errors**.

**Owner revision 4 — the rule becomes a shared component.** Owner: *"deploy fix to height to
sectioncard component to apply to subjects 'view all' too."* Decision round (grill-me, one
frontier): **Q1 = extract a shared read-only shell** (not SectionCard-owned dialog opening, not
a global CSS class); **Q2 = include the SectionCard-owned Streams dialog** in the same pass;
**Q3 = content-fill** instead of the fixed height, with the floor **reduced to 220px**.

- **New shared component** `src/SchoolCollab.Admin.Shared/Components/Dialogs/ReadOnlyDialogShell.razor(.css)`
  (the read-only sibling of the existing `DialogShellFooter`). Root = the height authority
  (`display: flex; flex-direction: column; gap: .75rem; min-height: 220px; max-height: 72vh;
  overflow: hidden` — content-fill); ONE `.readonly-dialog__scroll`
  (`flex: 1 1 auto; min-height: 0; overflow-y: auto`); bottom-pinned `.readonly-dialog__footer`
  (`margin-top: auto; flex: none; border-top` separator). Slots: `Class` (the caller's own root
  class, appended to `readonly-dialog`, so its scoped CSS and tests keep working), `Toolbar`
  (optional fixed rows above the region), `ChildContent`, `Footer`.
- **`GradeTopicsDialog`** and **`SectionListDialog`** now compose it and declare **no**
  height/scroll/footer CSS of their own. The streams list's own `max-height: 55vh` self-cap is
  gone (the shell scrolls it), and `.dialog-footer` is retired from both files — its other
  users (`SubPeriodsListDialog`, `TeacherRoleDialog`, `TopicStrandsDialog`, `Detail.razor.css`)
  keep theirs untouched (verified by grep before removing).
- Razor rejects unnamed content beside named slots (`RZ9996`), so each dialog's body is an
  explicit `<ChildContent>`: the fixed picker / error bar is the `<Toolbar>`, the grid or list
  keeps its natural height inside the shell's region, and the Close row is the `<Footer>`.
- Tests: new `tests/SchoolCollab.Admin.Tests.Unit/ReadOnlyDialogShellTests.cs` (3 — slot
  rendering with the caller's class, no phantom toolbar row, and the shell's own CSS rules),
  the subjects pins re-pointed at the shell
  (`GradeTopicsDialog_ComposesTheSharedShell_AndDeclaresNoLayoutOfItsOwn` plus the
  `.readonly-dialog__scroll` markup pin), and a composition pin added to
  `SectionListDialogReorderTests`. The rule now names the shell as its implementation in §7 of
  the dialog skill, in **both** copies.

**Evidence (revision 4):** `Students.Tests.Unit` **612 / 0**, `Admin.Tests.Unit` **602 / 0**
(599 + the 3 shell tests), `Students.Api.Tests.Unit` **4 / 0**, `ArchitectureTests.Unit`
**72 / 0**, `dotnet build SchoolCollab.slnx` → **0 errors**.

## Worker Report

**Implementation run `f2dd2a5f`** (`ollama-cloud/deepseek-v4.1-flash`) — 45 files (+856/−81): bridge `DisplayOrder` on `GradeStreamAssignment` + `GradeTopicAssignment`, migration `20260930104306_AddBridgeDisplayOrders` with the two order-preserving backfills, `SetGradeStreamOrder`/`SetGradeTopicOrder` (+handlers: tracked load → 404 → 400 → normalise `(DisplayOrder, CreatedAt, Id)` → swap → 409 → evict `students`), the two single-item PUT endpoints, client methods, `GetNextDisplayOrderAsync` on both bridge repositories wired into all four create sites, the streams View-all reorder entries, and the Subjects `FluentDataGrid`. Disclosed deviations (8, all forced-and-minimal): repository `SaveChangesAsync` seam, `protected set` on `TopicAssignment.UpdatedAt` (TPH subtype), the in-memory test double, `StreamDtoFactory` optional order, the `SetOrderRequest` body record, the empty-id early return in `ConfirmAssignAsync`, `InvalidOperationException`-for-404 (parity with `RemoveGradeStreamHandler`), `ChevronUp/Down` (no `ArrowUp/Down` in `FluentIcons`).

**Environment limit at the time:** the owner's VS-launched AppHost held the output binaries, so `dotnet build SchoolCollab.slnx` completed with **0 compiler errors** but 30 MSB3021/MSB3027 copy failures, and `Students.Api.Tests.Unit` / `Admin.Tests.Unit` could not run. In-run evidence was `Students.Tests.Unit` **608/0**. No process was killed; the locks were surfaced to the owner.

**Owner revision 1 — rework `18966abb`:** dialog `ExtraLarge` + explicit height, icon-only ordering control, control leading the row in both surfaces (see the section above); `Students.Tests.Unit` **609/0**.

**Owner revision 2 — rework `6fd422fb`:** Actions cell reduced to the shared kebab alone; `BuildRowActions` gained a conditional **Edit** first (the standalone icon had been the only edit affordance, so the action would otherwise have been lost) → Edit, Strands, Teachers, [Enrollment exceptions], Separator, Remove; hand-rolled `ConfirmRemoveAsync` deleted (the kebab's `destructive: true` + `confirmMessage` already routes through the shared confirm — `.github/copilot/rules/section-card.md` §Kebab actions, `blazor-components.md:478`); `RemoveAsync` gained `StateHasChanged()` because the kebab click is child-handled and the grid otherwise kept the removed row (`section-card.md:32`, proved by bUnit).

**Parent fix (Solo, per the owner's new UI-tiering rule):** the round's surface changes had left **two reds** in `tests/SchoolCollab.Admin.Tests.Unit/GradeDialogsBunitTests.cs`, hidden until now by the locked build — `TopicsDialog_ListsAssignedTopics_WithCounts` asserted the old list text `"2 strands"` (the grid renders the count as the Strands cell's text) and `TopicsDialog_Assign_MovesTopicFromPicker_ToAssignedList` still injected `Func<Guid, Task>` for the widened `AssignKey`. Both fixed at the test level (harness signature widened; the assign stub now returns a non-empty assignment id, since the dialog deliberately appends nothing for an empty id).

## Review

### Plan review

**PLAN REVIEW dispatched 2026-09-30 between 10:25 and 10:34 UTC** (run id
`d62f5310-b313-425e-872c-b85fb7901746`, reviewer model `ollama/glm-5.3:cloud`) —
**verdict: P1-BLOCK** (2 × P1, 4 × P2):

- **P1-1** — append-at-end must cover every grade-topic bridge create site: only
  `AssignGradeTopicHandler` was planned, but `CreateTopicForGradeHandler` (the Subjects
  card's primary Add flow) and `GetOrCreateTopicHandler` also call
  `GradeTopicAssignment.Create(...)`; their new rows would default to order 0 and appear
  at the top.
- **P1-2** — the optimistic append must carry the real assignment id:
  `GradeTopicsDialog.ConfirmAssignAsync` builds
  `new GradeTopicCurriculumDto(topic.Id, topic.Name, topic.Code, 0, 0)` and
  `Detail.razor`'s `CurriculumRow` has a fallback constructor; neither was named in the
  plan, and with `AssignKey` as `Func<Guid, Task>` the appended row's ▲/▼ would PUT an
  empty guid → 404.
- **P2-1** — `SetGradeTopicOrder(GradeLevelId, AssignmentId, Order)` cannot be built by
  an id-only route; drop `GradeLevelId` (derive scope from the loaded, tenant-filtered
  row) and rewrite AC5's subjects half as "unknown or other-tenant assignment id → 404".
- **P2-2** — backfill determinism: the seeder batch-inserts its rows, so `created_at`
  ties are expected; add `id` to the backfill `ORDER BY` (matching the normaliser's
  `(CreatedAt, Id)` tiebreak) and drop the "bit-for-bit stable" overclaim.
- **P2-3** — pin the topics update-load scope: `ListByGradeLevelForUpdateAsync` (and its
  streams twin) loads the **effective-on-today** set — the same set the grid shows and
  the 400 bound is computed from.
- **P2-4** — seam accuracy: "the streams handler has no cache" is false —
  `AssignGradeStreamHandler` and `RemoveGradeStreamHandler` both inject `HybridCache`
  and evict the `students` tag; the new stream-order handler must evict the same tag
  (sibling parity).

The reviewer explicitly stated no re-review of the whole plan is needed after these
amendments.

### Plan revision (post-review)

This single plan-revision iteration applies all six findings; each cited seam was
re-verified against the code before amending (call-site grep + line reads):

1. **P1-1 applied — centralised.** Instead of stamping `max+1` inline at each create
   site, the plan now adds `GetNextDisplayOrderAsync(gradeLevelId)` to both bridge
   repositories (already modified-files rows 5–8, so no extra files) and stamps
   `DisplayOrder` only on the bridge-create branches of all four create sites; the two
   topic-side handlers keep their existing skip-when-active behaviour untouched
   (implementation step 4.6). `CreateTopicForGradeHandler`/`GetOrCreateTopicHandler`
   added to the modified-files table (rows 14a/14b).
2. **P1-2 applied.** `AssignKey` widened to `Func<Guid, Task<Guid>>`
   (`StudentsApiClient.AssignGradeTopicAsync` verified `Task<Guid>`, line 1696);
   `ConfirmAssignAsync` populates the appended row's `AssignmentId`; the
   `CurriculumRow` fallback passes a defaulted id; both call sites named in the
   modified-files table (rows 19/20) and implementation step 8.
3. **P2-1 applied.** `SetGradeTopicOrder` is now `(Guid AssignmentId, int Order)` —
   new-files row 3, implementation step 5, client step 9; AC5's subjects half rewritten
   to "unknown or other-tenant assignment id → 404".
4. **P2-2 applied.** Streams backfill `ORDER BY created_at, id` (Plan correction 1 and
   migration step 3); the "bit-for-bit stable" claim removed.
5. **P2-3 applied.** Implementation step 4.1 pins `ListByGradeLevelForUpdateAsync`
   (topics) to the effective-on-today set — the same set `/curriculum` lists and the
   400 bound is computed from.
6. **P2-4 applied.** The stream cache seam statement corrected (both streams handlers
   inject `HybridCache` and evict `students`); `SetGradeStreamOrderHandler` required to
   evict the same tag (new-files row 2, step 4.5).

Also: AC9 re-marked to assert the bridge value (`DisplayOrder == max + 1`) instead of
the pre-change-passing "new row appears last", and the handler-test files it lands in
(`AssignGradeStreamHandlerTests`, new `AssignGradeTopicHandlerTests`,
`CreateSubjectForGradeHandlerTests`, `GetOrCreateSubjectHandlerTests`) added to the
modified-files table (row 22a). Per the reviewer: no re-review of the plan required;
proceeding to the worker round.

### Diff review

**Static diff review (`ollama-cloud/kimi-k2.7-code`, read-only, never built or tested) — verdict: PASS, no issues found.** Its judgement: "all plan files are present and limited to the declared scope; the backfill SQL, swap/normalise handlers, tenant-scoped loads, optional reorder UI, and test coverage match the amended plan", and the `InvalidOperationException`-for-404 deviation is "consistent with existing handlers such as `RemoveGradeStreamHandler`", with the small deviations "forced-and-minimal rather than scope-widening".

**Scope note:** that pass ran against the patch as frozen *before* the two owner revisions. Those revisions are presentation-only (dialog size/height, control placement/iconography, kebab-only actions) and were verified by the parent's authoritative pass plus the bUnit suites that pin them — not by a second reviewer run, in line with the owner's UI-only tiering rule recorded in `.pi/skills/orchestrator-worker-reviewer/SKILL.md`.

## Acceptance

**Verdict: CLOSED** — parent-transcribed. *Deviation from the full-Tier-3 letter* (which would dispatch an orchestrator-accept run and a UI tester): the round is green on every gate, the two post-review owner revisions were UI-only, and the owner's new UI-tiering rule (`.pi/skills/orchestrator-worker-reviewer/SKILL.md`) makes a Solo/light close the preferred shape. The orchestrator-accept run and a UI-tester pass remain available on request.

| Criterion | Evidence |
|---|---|
| AC1–AC4 swap semantics, duplicate-order normalisation, same-order no-op | `SetGradeStreamOrderHandlerTests` / `SetGradeTopicOrderHandlerTests` (green) |
| AC5 400 out-of-range / 404 unknown or other-tenant / 409 conflict | handler tests + route catch parity with `ContactRoutes` |
| AC6–AC7 bridge-sourced listing order; `GradeStreamDto.DisplayOrder` no longer the coded value's | `ListGradeStreamsHandlerTests`, `ListGradeTopicCurriculumByGradeHandlerTests` (updated) |
| AC8 `AssignmentId` exposed on both DTO records | `GradeTopicCurriculumDto` + the client mirror; asserted in the grid tests |
| AC9 append-at-end on every create site (`DisplayOrder == max + 1`) | `AssignGradeStreamHandlerTests`, `AssignGradeTopicHandlerTests`, `CreateSubjectForGradeHandlerTests`, `GetOrCreateSubjectHandlerTests` |
| AC11 reorder UI (icon-only, leading, bounded by the row's own callbacks) | `SectionListDialogReorderTests`, `GradeTopicsDialogGridTests` |
| AC13/AC15 subjects grid + kebab-only actions with the shared confirm | `GradeTopicsDialogGridTests` (column order, first-column Order, no position text, one control per Actions cell, kebab Edit/Remove paths) |
| AC16 endpoint mapping | handler-level status coverage + route inspection; no dedicated API test file (plan explicitly allowed the review path) |
| AC17 build + affected suites | parent pass below |

**Parent-run authoritative pass (clean feature branch):** `dotnet build SchoolCollab.slnx` → **0 errors**; `tests/SchoolCollab.Students.Tests.Unit` → **612 / 0**; `tests/SchoolCollab.Students.Api.Tests.Unit` → **4 / 0**; `tests/SchoolCollab.Admin.Tests.Unit` → **602 / 0** (599 + the 3 `ReadOnlyDialogShellTests`; the 3 `DevTenantSwitcherTests` live on the parked `fix/dev-tenant-selection-resilience` branch, not here); `tests/SchoolCollab.ArchitectureTests.Unit` → **72 / 0**. Frozen patch: the feature files plus the two `dialog-ui` skill copies and the new shared shell; only `documents/rounds/` is excluded (the ephemeral round doc + the patch itself).

**Residuals (accepted):** AC16 has no dedicated API-test file (handler-level status coverage + route inspection instead, as the plan allowed); not-found uses `InvalidOperationException` for parity with the neighbouring handlers rather than a typed exception pair (repo prose prefers typed — recorded, not changed); the streams View-all dialog stays at `DialogSize.Medium` (only the Subjects card asked for a larger shell); `Topic.DisplayOrder`, the seeder, the Settings context and the landing pages are untouched by design.

## UI Tester

**Not dispatched** — pending the owner's call, and only meaningful with the AppHost relaunched against the new build. The UI surfaces this round ships are pinned by bUnit today: Order is the first column and icon-only with no position text (`GradeTopicsDialogGridTests`), the Actions cell holds exactly the kebab with the shared confirm and no standalone edit/delete buttons, the streams list renders its reorder controls before the name (`SectionListDialogReorderTests`), and `OpenTopicsDialogAsync` passes `DialogSize.ExtraLarge` with **no** `height:` argument while the content root carries the height and the **wrapper** — not the grid — owns the scroll region (grid at natural height + `GenerateHeaderOption.Sticky`); source + markup pins.