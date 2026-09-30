# Grade bridge display order (Streams & Subjects reorder)

**Status:** implemented (round `viewall-reorder-and-subjects-grid`,
`feat/viewall-reorder-and-subjects-grid`).
**Surfaces:** grade-level detail page → Streams card "View all streams" dialog and
Subjects card "View all subjects" dialog (`/students/grade-levels/{id}`).
**Migration:** `20260930104306_AddBridgeDisplayOrders` (Students).

---

## 1. Finding — why the *bridge* row is the ordering authority

Both grade-detail lists needed manual reordering. The two obvious data homes were
the shared catalogues (`CodedValue.DisplayOrder` for streams,
`Topic.DisplayOrder` for subjects) and the per-grade bridge rows
(`GradeStreamAssignment`, `GradeTopicAssignment`).

The catalogue order is a **cross-grade** concept: `Topic.DisplayOrder` orders the
Subjects/Topics catalogue the tenant sees on its own landing page, and the
`GRSTREAMS` coded-value order does the same for the stream catalogue. Reusing it
for one grade's list would reorder every other grade (and the landing pages), and
would make "move this subject up in Grade 4" impossible to express without
changing Grade 5.

Two facts pinned the design:

1. **Streams cannot be backfilled from the catalogue.** `GradeStreamAssignment.StreamCodedValueId`
   is a bare `Guid` with **no FK** — coded values live physically in the *Settings*
   database (`CodedValue`), while the bridge lives in the *Students* database. A
   Students EF migration cannot join the two, so "seed from `CodedValue.DisplayOrder`"
   is not implementable in the migration that adds the column.
2. **Nothing ever sorted by the catalogue order anyway.** No UI consumer sorted by
   `GradeStreamDto.DisplayOrder`; the visible stream order was simply the API array
   order, i.e. `GradeStreamAssignmentRepository.ListByGradeLevelAsync`'s
   `OrderBy(x => x.CreatedAt)`. The coded value's order was copied into the DTO and
   never used.

`Topic.DisplayOrder` *is* in the Students database (`subjects` table), so the
subjects backfill **can** join it — and it did order the curriculum read before this
round (`ORDER BY t.DisplayOrder, t.Name`), so it is what the user actually saw.

**Decision (owner-pinned):** the per-grade bridge row owns the order for both lists.
`GradeStreamAssignment` and `GradeTopicAssignment` each gain a `DisplayOrder`;
`Topic.DisplayOrder` keeps ordering the shared catalogue and only survives as the
migration's backfill key for subjects.

### Backfill keys (preserving today's visible order)

| List | Today's visible order | Backfill |
|---|---|---|
| Streams | `created_at` listing order (the coded value's order was never applied) | `row_number() over (partition by grade_level_id order by created_at, id) - 1` |
| Subjects | `topic.display_order, topic.name` (the curriculum read) | `row_number() over (partition by grade_level_id order by s.display_order, s.name, start_date, id) - 1` |

The `id` tiebreak is mandatory for streams: the migration seeder
(`GradeStreamAssignmentSeeder`) batch-inserts its rows in one `AddRange`, so
`created_at` ties inside a grade are expected. `(created_at, id)` also matches the
reorder handler's normalisation tiebreak, so a seeded grade stays in exactly the
order it rendered before the column existed.

## 2. Implementation steps

1. **Domain** — `GradeStreamAssignment.DisplayOrder` (int, private set) +
   `SetDisplayOrder(int)` + `Create(..., int displayOrder = 0)`;
   `GradeTopicAssignment` the same (the TPH subtype's property maps to a **nullable**
   `display_order` on the shared `topic_assignments` table — null for activity-group
   rows, which have no order surface). The factory defaults keep the seeder and every
   existing `Create` caller source-compatible.
2. **Migration + backfill** — one migration, two columns, the two backfills above.
3. **Repositories** — the grade listings now `OrderBy(DisplayOrder)` (then the
   existing tiebreaks), plus a **tracked** `ListByGradeLevelForUpdateAsync` (the
   swap mutates several rows in one save; the listing twin is `AsNoTracking`), a
   `GetNextDisplayOrderAsync(gradeLevelId)` append helper, and `SaveChangesAsync`.
   The topics update-load is restricted to the **effective-on-today** set — the same
   rows the curriculum read lists, so the 400 bound cannot include a row the grid
   does not show.
4. **Commands** — `SetGradeStreamOrder(GradeLevelId, AssignmentId, Order)` and
   `SetGradeTopicOrder(AssignmentId, Order)`. The subject command deliberately
   carries **no grade id**: the route is id-only, so a grade id alongside it could
   lie; the handler derives the grade (and tenant) scope from the loaded,
   tenant-filtered row. Load → 404 when the mover is not in that grade's set;
   range-check → 400; normalise (`DisplayOrder, CreatedAt, Id` → contiguous 0..n-1) →
   swap the mover with the row holding the requested position; save under the
   `DbUpdateConcurrencyException` → `ConcurrencyException` catch (409); evict the
   `students` cache tag (the cached curriculum payload carries the order).
5. **Endpoints** — `PUT /students/grade-levels/{gradeLevelId}/streams/{assignmentId}/order`
   and `PUT /students/topic-assignments/{assignmentId}/order`, body `{ "order": n }`,
   `204` on success, with the `ContactRoutes` catch shape (`400` /
   `404` / `Conflict(new { ex.Message })`).
6. **Append at the end** — every bridge-create site stamps
   `GetNextDisplayOrderAsync(...)` on the created row: `AssignGradeStreamHandler`,
   `AssignGradeTopicHandler`, `CreateTopicForGradeHandler`, `GetOrCreateTopicHandler`
   (the last two strictly inside their existing skip-when-active guard, so the
   shared-topic/no-new-bridge path never stamps).
7. **Client + UI** — `StudentsApiClient.SetGradeStreamOrderAsync` /
   `SetGradeTopicOrderAsync`; `GradeTopicCurriculumDto` gained `AssignmentId` (the id
   the subject PUT targets) in both its Core and Application definitions; the Streams
   View-all (`SectionListDialog`) gained *optional* reorder keys, and the Subjects
   View-all (`GradeTopicsDialog`) became a `FluentDataGrid` with the kebab actions
   preserved plus edit/delete icon shortcuts and an Order column.

### Affordance choice

Move-up/move-down buttons only — the contact precedent's semantics. There is no
drag-and-drop anywhere in this repository, so none was introduced.

## 3. Verification

- `SetGradeStreamOrderHandlerTests` / `SetGradeTopicOrderHandlerTests`:
  swap, duplicate-order normalisation, out-of-range (400), not-in-grade /
  other-tenant / activity-group / not-effective (404), row-version conflict (409).
- `ListGradeStreamsHandlerTests` / `ListGradeTopicCurriculumByGradeHandlerTests`:
  the listing follows the bridge order and the DTO carries the bridge value /
  assignment id.
- `AssignGradeStreamHandlerTests`, `AssignGradeTopicHandlerTests`,
  `CreateTopicForGradeHandlerTests`, `GetOrCreateTopicHandlerTests`: the append
  assertion (`DisplayOrder == max + 1`).
- `SectionListDialogReorderTests` / `GradeTopicsDialogGridTests`: the reorder
  affordances and the grid presenters (bUnit), including "no reorder keys ⇒
  unchanged rendering".
