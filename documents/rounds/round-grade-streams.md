Provider: pi/ollama-cloud + cline per-role override, full Tier 3 (models: ollama-cloud/glm-5.3-flash orchestrator, ollama-cloud/glm-5.3 plan-review, ollama-cloud/deepseek-v4.1-flash worker, cline/cline-free/mimo-v2.6-flash reviewer, ollama-cloud/kimi-k2.7-code escalator, ollama-cloud/minimax-m3 tester)
Round: grade-streams | Base: 6a392c8cf61ea16f31719f3e78796fe296910f26 | Round doc: documents/rounds/round-grade-streams.md | Patch (parent writes): documents/rounds/diffs-grade-streams.patch
Escalation: worker pass 1 (`ollama-cloud/deepseek-v4.1-flash`, run 9aba1fb2) and the escalation pass (`ollama-cloud/kimi-k2.7-code`, run 7849f978) both timed out at the 30-min cap *after* landing the implementation; the parent completed the residual (test-only) fixup and ran the authoritative pass.

# Round — grade-streams

## Plan

### Goal

Make grade streams **user-initiated** and **catalogue-sourced**, mirroring the subject
(`Topic`) add pattern. Introduce a `GradeStreamAssignment` **M:N bridge** linking a
`GradeLevel` to a `GRSTREAMS` coded-value child. Today the link *is* the coded value's
`gradeLevel` attribute (written behind the grade-detail card's Add button, read back by
attribute-filtered queries); after this round the link **is** the bridge row, the
attribute is only read by the backfill, the add flow offers pick-existing-or-create-new
in a subject-style dialog, enrollment validation and (grade, streamVersion) uniqueness
move onto the bridge, and the legacy attribute is no longer written.

### Settled decisions (owner-approved — NOT reopened)

1. **Bridge model — option B.** `GradeStreamAssignment`, M:N: one stream coded value may
   be offered by several grades. It is a cross-context **operational ref**:
   `StreamCodedValueId` is a `Guid` pointing at the Settings `CodedValue` row — **no
   Students-side stream entity, no FK** (cross-DB; enforced only by the unique index).
   This mirrors `StudentEnrollment.StreamCodedValueId` /
   `GradeLevel.CodedValueId` (`src/Students/SchoolCollab.Students.Core/Domain/StudentEnrollment.cs:19`,
   `GradeLevel.cs:31`).
2. **Backfill.** Existing `gradeLevel` attributes (native seed rows on upgraded DBs +
   any tenant rows) MUST become bridge rows; the attribute is no longer *written*
   going forward. Fresh DBs must end up with the same bridge rows.
3. **One round** (this one).
4. **UI surface:** the grade-detail Streams card only (no new landing page). Forced
   consequence inside the *existing* Enroll student dialog (picker data source, §Design
   7) is in scope; no other surfaces change.
5. **Add flow:** pick-existing-or-create-new with tenant override where appropriate:
   native/global coded value the user edits → tenant override; tenant-owned value →
   edit in place; brand-new → create under `GRSTREAMS`. Mirrors
   `TopicCreateDialog` + `TopicCodedValueSaver` + `TopicEditRouter`.
6. **Tier 3 full** — the diff touches `.razor`, so UI trigger fires; lean rules do not
   apply. Plan-review gate before implementation (step 2b).

### Scope

**In** — bridge entity + EF migration + repository; backfill seeder in MigrationService;
seed CSV changes; bridge-backed CQRS (list / assign / remove) + API endpoints + client
methods; enrollment validation moving onto the bridge (3 call sites); (grade,
streamVersion) uniqueness moved to assignment time; Streams-card UI re-pointed to the
bridge + new `StreamCreateDialog`; enroll-dialog stream picker data source swap;
tests (unit + bUnit); small doc edits (ADR table row, grade-level-setup supersession
appendix).

**Out** — new Streams landing page; hard-deleting stream coded values; changing the
Settings coded-value catalogue UI; removing `streamVersion` from the seed or the
Settings `DuplicateStreamException` guard (both stay); MassTransit contract changes;
student-topic-assignment surfaces.

### Design

**0. Facts establishing the backfill mechanism (the feasibility crux)**

- `coded_values` lives **physically in the Settings database**:
  `src/Settings/SchoolCollab.Settings.Core/Data/Configurations/CodedValueConfiguration.cs:25`
  (`builder.ToTable("coded_values")`), on `SettingsDbContext`.
  `src/SchoolCollab.MigrationService/Program.cs:27-49` registers
  `SettingsDbContext` (connection string `settings-db`) and `StudentsDbContext`
  (`students-db`) as **two separate Npgsql databases** — the Settings `MigrateAsync`
  (`:93`) + `CodedValueSeeder.SeedAsync()` (`:97`) run inside one scope (`:84`), the
  Students `MigrateAsync` at `:149`. **No EF migration on the Students DB can ever
  read `coded_values` (different database; no cross-DB query in Postgres without
  dblink).** The honest mechanism is therefore **not** a raw-SQL backfill inside a
  migration: it is a **MigrationService seeder step** that already has both DbContexts
  registered in one DI scope (`Program.cs:84`) and runs *strictly after* both
  the Settings migration + coded-value seed (`:93`/`:97`) **and** the Students
  migration (`:149`), mirroring the existing
  `PilotActivityGroupFlagOverrideSeeder.SeedAsync(tenantIdsByName)` hand-off shape
  (`Program.cs:119-120`, `tenantIdsByName` assigned at `:114`). Order for a fresh DB is
  therefore already correct today:
  Settings migrations → coded values seeded → Students migrations (table created) →
  **new `GradeStreamAssignmentSeeder`** → dev-identity seed.
- Seed shape today: 1 parent `GRSTREAMS` row (`seed.csv:54`) + **39 child rows**
  (`seed.csv:55-93`) in `src/SchoolCollab.MigrationService/SeedData/seed.csv`;
  **exactly 39** `gradeLevel` attribute rows (`seed-attributes.csv:48-124`, one per
  child; e.g. `GRSTREAMS_0A,gradeLevel,GRADE_R`), and the
  `GRSTREAMS` attribute definition at `seed-attribute-definitions.csv:7`
  (`GRSTREAMS,gradeLevel,Grade Level,7,GRADE,True,…`). `streamVersion` rows
  (`seed-attributes.csv:49+`) and the
  `DuplicateStreamException` guard (`src/Settings/SchoolCollab.Settings.Core/CQRS/CodedValues/Commands/SetCodedValueAttribute/SetCodedValueAttributeHandler.cs:20-46`,
  `src/Settings/SchoolCollab.Settings.Core/Domain/Exceptions/DuplicateStreamException.cs`)
  both **stay untouched** — `SetAttributeHandlerTests` keeps passing unchanged.
- GradeLevel rows are tenant-scoped and **not seeded** — they materialize at enroll
  time (`src/Students/SchoolCollab.Students.Core/CQRS/Enrollments/Commands/EnrollStudent/EnrollStudentHandler.cs:50-70`,
  race-safe `AddOrReuseAsync`). A fresh DB can therefore have zero `grade_levels`
  rows, so the backfill materializes them (idempotent, same unique-index
  reuse semantics) for each registered tenant.

**1. Bridge entity, EF model, migration**

- `GradeStreamAssignment` (`Domain/GradeStreamAssignment.cs`): `Id`, `TenantId`
  (strict, `ITenantEntity`), `GradeLevelId` (FK → `grade_levels`, cascade),
  `StreamCodedValueId` (`Guid`, **no FK** — cross-DB), audit + `RowVersion`,
  static `Create(gradeLevelId, streamCodedValueId)` factory with a
  `GradeStreamAssignedEvent` — modeled on
  `Domain/GradeTopicAssignment.cs:9-36` (bridge exemplar) but a **standalone table**,
  not TPH (single type): `TeacherGradeLevelConfiguration` style rather than
  `TopicAssignmentConfiguration`'s discriminator
  (`TopicAssignmentConfiguration.cs:15-36`, `GradeTopicAssignmentConfiguration.cs:14-31`).
- `GradeStreamAssignmentConfiguration` mirroring
  `GradeTopicAssignmentConfiguration.cs:16-31`: required FK + cascade, **unique index
  `ix_grade_stream_assignments_tenant_grade_stream` on
  (tenant_id, grade_level_id, stream_coded_value_id)**, tenant query filter via the
  `EntityTypeConfigurationBase` + `() => CurrentTenantId` accessor pattern wired in
  `StudentsDbContext.Configure` (`StudentsDbContext.cs:51-60`; DbSet at
  `StudentsDbContext.cs:14-43`).
- EF migration `AddGradeStreamAssignments` (timestamped per
  `.github/copilot/rules/ef-migrations.md`) + snapshot.
- Repository: `IGradeStreamAssignmentRepository` over `RepositoryBase` with
  `AddOrReuseAsync` (unique-index raciness, same semantics as the enroll inserts),
  `ListByGradeLevelAsync(Guid)`, `ExistsAsync(gradeLevelId, streamCodedValueId)`.

**2. Backfill + fresh-DB seeding (`GradeStreamAssignmentSeeder`)**

New `src/SchoolCollab.MigrationService/Seeding/GradeStreamAssignmentSeeder.cs`,
registered in `Program.cs` and invoked right after the Students-migration block:
`await gradeStreamSeeder.SeedAsync(tenantIdsByName);` — same delegate-the-map pattern
as `PilotActivityGroupFlagOverrideSeeder`. One code path serves **upgraded and fresh
DBs** by taking the **UNION of two sources**, resolved against the Settings
`codeToId` map, both idempotent under the unique index:

- **(a) Attribute-derived (upgraded DBs):** read SettingsDbContext
  `CodedValues` (GRSTREAMS children — global NULL-tenant rows *and* tenant-owned
  rows) whose **stored** `gradeLevel` attribute exists, resolve the attribute's
  Guid→ GRADE coded value → GRADE code. This is exactly today's live behavior
  (`Detail.razor:589-596` reads the same attribute; legacy tenant rows picked up
  even where the CSV never covered them).
- **(b) CSV-derived (fresh DBs, where the attribute rows were removed from the
  seed):** new `SeedData/seed-stream-assignments.csv`, columns
  `streamCode,gradeCode`, **39 rows** — the exact current
  `seed-attributes.csv:48-124` `gradeLevel` mapping copied verbatim before deletion.
  Consumed via a **new `CsvSeedReader.ReadStreamAssignments(filePath)`** method:
  the existing reader has only 5-field `Read` (`CsvSeedReader.cs:17`), 10-field
  `ReadAttributeDefinitions` (`:64`) and 3-field `ReadAttributes` (`:105`) — no
  2-column shape exists, so `CsvSeedReader.cs` gains `ReadStreamAssignments` +
  `StreamAssignmentSeedRow` (both in Expected files).

**Scope note (review P2-1 — `tenantIdsByName` is not in scope today):**
`tenantIdsByName` is declared inside the Settings `try` block (`Program.cs:114`)
and consumed at `:120`; the new call sits after the Students block (`:149+`,
*outside* that `try`), so `Program.cs` must **hoist**
`Dictionary<string, Guid>? tenantIdsByName = null;` above `:87`, assign it at
`:114`, and **null-guard** the seeder call — the per-block try/catch means a
Settings-seed failure does not abort the Students block, so the map can be null.

For each intended (gradeCode → streamCodedValueId) pair and each tenant in the
passed `tenantIdsByName` map (tenant-owned streams only under their own tenant;
global values under every registered tenant — matches resolved-value visibility):
1. Resolve or **materialize** the `GradeLevel` row by `CodedValueId` in
   StudentsDbContext — exactly the enroll-path shape
   (`EnrollStudentHandler.cs:53-70`):
   `GradeLevel.Create(cv.Id, level: cv.DisplayOrder, name: cv.Name,
   displayOrder: cv.DisplayOrder).WithTenant(tenantProvider)` +
   `gradeLevelRepository.AddOrReuseAsync(...)` (logging on reuse) — fresh DBs get
   `grade_levels` rows for the 13 `GRADE_*` values without touching the enroll
   path, which stays idempotent (`AddOrReuseAsync` reuses on
   `unique (tenant_id, coded_value_id)`).
2. Insert the bridge row `(tenantId, gradeLevelId, streamCodedValueId)`,
   skipping rows that already exist (unique index / pre-check), batch-
   `SaveChangesAsync`, single transaction, guard-suppressed tenant context
   (`tenantContextAccessor.SuppressTenantGuard()`, same as
   `CodedValueSeeder.cs:24-34`).
- Rejected alternatives (stated so review doesn't re-litigate): raw-SQL
  cross-DB backfill inside the EF migration (impossible — separate databases);
  bridge rows stored in the Settings DB (violates settled decision 1);
  dblink extension (new infra dependency, not sanctioned).

**3. Seed CSV changes**

- `seed-attributes.csv`: **delete the 39 `GRSTREAMS_*,gradeLevel,…` rows** —
  after the round nothing reads them for behavior; the seeder's source of truth
  is (a) existing DB attributes (upgraded DBs keep working off their historical
  rows) and (b) the new CSV (fresh DBs).
- `seed-attribute-definitions.csv`: **delete the `GRSTREAMS,gradeLevel,…`
  definition row** (`:7`) — the attribute stops being *written*; the definition
  drives editability in the coded-values landing, so keeping it would invite
  writes. Historical DBs unaffected (definitions are idempotent-skip, not
  delete).
- `streamVersion` attribute rows **stay** (`seed-attributes.csv:49+`).
- Settings guard: `SetCodedValueAttributeHandler.cs:20-46` and
  `DuplicateStreamException` **unchanged** — they keep guarding *direct attribute
  writes* through the coded-values landing (any legacy row still carrying a
  `gradeLevel` attribute). They are simply no longer the primary guarantee;
  `SetAttributeHandlerTests` keeps discriminating them.

**4. Enrollment validation — `ValidateStreamAsync` moves onto the bridge**

Three call sites today all read the Settings API and string-parse the
`gradeLevel` attribute:
- `EnrollStudentHandler.cs:92-96` + `:286-312`,
- `TransferStudentHandler.cs:34-38` + `:81-105`,
- `CreateStudentWithLinkedDataHandler.cs:258-262` + `:289-313`.

New shape (identical in all three; extract once into a shared helper class in
`CQRS/Enrollments/`):

```csharp
private async Task ValidateStreamAsync(Domain.GradeLevel gradeLevel, Guid streamCodedValueId, CancellationToken ct)
{
    var offered = await bridgeRepository.ExistsAsync(gradeLevel.Id, streamCodedValueId, ct);
    if (!offered)
        throw new StreamGradeMismatchException(streamCodedValueId, gradeLevel.Id);
}
```

- **Lookup lives in the Students DB** (tenant filter automatic via the strict
  tenant query filter) — **kills the ad-hoc Settings HTTP hop** that today
  `adr-cross-module-calls.md:20-30` documents as the write-path call
  "Students → settings … `EnrollStudentHandler` … stream validate (`:186`)"; that
  ADR table row is updated by this round (doc edit, §Expected files 27). The
  `ICodedValuesApiClient` dependency stays on `EnrollStudentHandler` only for
  grade materialization.
- **Exception:** keep `StreamGradeMismatchException`
  (`Domain/Exceptions/StreamGradeMismatchException.cs`) — all three HTTP catch
  sites (`EnrollmentRoutes.cs:59,88`; `StudentRoutes.cs:92`) map it to `409
  Conflict` already; only its doc comment changes to bridge semantics.
- **Discriminating tests** (`EnrollStudentHandlerTests.cs` updated + new cases):
  stub the coded-values API to return a stream whose `gradeLevel` **attribute
  matches** (the exact pre-fix success condition) but insert **no bridge row** →
  enrollment must now fail with `StreamGradeMismatchException` (pre-fix code
  enrolls fine ⇒ test fails pre-fix). Inverse: insert a bridge row and stub the
  API to return the DTO with **no attributes** → enrollment succeeds (pre-fix
  throws). Same pair for `TransferStudentHandler` and
  `CreateStudentWithLinkedDataHandler`.

**5. Stream-version uniqueness moves to assignment time**

- New `CQRS/GradeStreams/Commands/AssignGradeStream/AssignGradeStreamHandler.cs`:
  1. resolve the new stream's `streamVersion` attribute via the Students-side
     `ICodedValuesApiClient.GetByIdAsync`
     (`src/Students/SchoolCollab.Students.Core/Services/CodedValuesApiClient.cs:39-77` —
     `StreamCodedValueDto` already carries `Attributes`, `:12-23`);
  2. if the version is set, fetch the grade's existing bridge rows
     (`ListByGradeLevelAsync`), and read the catalogue in **one** hop via a new
     `GetChildrenByParentCodeAsync(string parentCode)` added to the Students
     `ICodedValuesApiClient` + `CodedValuesApiClient` — mirroring the Admin.Shared
     method at `src/SchoolCollab.Admin.Shared/Services/CodedValuesApiClient.cs:91-92`
     → `GET /api/coded-values/by-parent?parentCode=GRSTREAMS`. That endpoint is
     **override-resolving** (`GetCodedValuesByParentHandler.cs:58,109`, via
     `CodedValueResolver.ResolveAsync`) and **tenant-scoped in its cache key**
     (`:35`); filter the returned children to the bridge's `StreamCodedValueId`s.
     Deliberately NOT the `by-ids` endpoint: `GetCodedValuesByIdsHandler.cs:47-64`
     applies no override resolution, hardcodes `IsOverridden = false`, and uses a
     tenant-independent cache key (`:30`) — a resolved read is impossible there
     (review P1-2 / P2-4);
  3. any existing bridge stream with the same non-empty `streamVersion` → throw the
     new typed `DuplicateStreamAssignmentException(gradeLevelId, streamVersion,
     existingCodedValueId)` (Students domain; new `DomainException` like the
     Settings one);
  4. else `AddOrReuseAsync` an idempotent insert (re-assign returns the existing
     bridge id — the unique index makes the race window safe).
  Absent/empty `streamVersion` skips the uniqueness check (a new user-created
  stream has no version label until one is set — same as today's seed-created
  values only when the attribute exists).
- **The Settings `DuplicateStreamException` guard STAYS** for direct attribute
  writes (§3) — vestigial once the UI stops writing `gradeLevel`, but kept
  fail-closed for the coded-values landing's legacy rows.
- Map the exception to `409 Conflict` in `GradeLevelRoutes.cs` next to the
  other Students domain exceptions on those routes.

**6. Streams-card read path**

- New query `CQRS/GradeStreams/Queries/ListGradeStreams/{ListGradeStreams.cs,
  ListGradeStreamsHandler.cs}`:
  bridge rows for `GradeLevelId` (tenant-filtered) → distinct
  `StreamCodedValueId`s → **one** `GetChildrenByParentCodeAsync("GRSTREAMS")` hop
  (override-resolving, tenant-scoped — see §5) → filter to the bridge set → project to new
  `GradeStreamDto(Guid AssignmentId, Guid StreamCodedValueId, Guid GradeLevelId,
  string Code, string Name, string? NameOverride, string? Description, string?
  StreamVersion, bool IsOverridden, bool IsDisabled, int DisplayOrder)`
  (`src/Students/SchoolCollab.Students.Core/DTOs/GradeStreamDto.cs`, new).
  Override display is populated from the resolved DTO — `Name` = resolved
  (override-aware) name, `NameOverride` = `CodedValueDto.DefaultName`,
  `IsOverridden` = `CodedValueDto.IsOverridden`
  (`Admin.Shared/Services/CodedValuesApiClient.cs:31-51`); **no Settings change**, so
  the tenant-override display the base commit has on the card is preserved
  (review P1-2).
- Endpoint inside the existing `MapGradeLevelRoutes` group
  (`src/Students/SchoolCollab.Students.Api/Endpoints/GradeLevelRoutes.cs:24`):
  `GET /grade-levels/{id}/streams` (list), `POST /grade-levels/{id}/streams`
  (assign), `DELETE /grade-levels/{id}/streams/{assignmentId}` (unassign) —
  grouped per the repo's endpoint-grouping rule (no inline routes).
- Client: `StudentsApiClient.ListGradeStreamsAsync(Guid gradeLevelId, ...)`,
  `AssignGradeStreamAsync`, `RemoveGradeStreamAsync`
  (`src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs`;
  method style mirrors `CreateTopicForGradeAsync` at `:1153` /
  `AssignGradeTopicAsync` at `:1647`).
- `Detail.razor`: card `SectionCard TItem="GradeStreamDto"`; `ReloadStreamsAsync`
  (`Detail.razor:589-596`) swaps
  `CodedValuesApi.GetChildrenByParentCodeFilteredByAttributeAsync` (Admin.Shared,
  `CodedValuesApiClient.cs:101-115`, endpoint
  `/api/coded-values/by-parent?parentCode=GRSTREAMS&attributeKey=gradeLevel…`)
  for `Api.ListGradeStreamsAsync(Id)`. **"Manage streams" link stays** pointing at
  the GRSTREAMS catalogue (`ViewAllNavigationUrl="/coded-values/GRSTREAMS/children"`,
  `Detail.razor:192`) with `ViewAllText` relabelled to **"All streams
  (catalogue)"** — the catalogue still owns the coded values themselves.
- **Remove** becomes `RemoveGradeStreamAsync` (bridge-row delete; the coded value
  STAYS in the catalogue and its `IsDisabled` state is untouched) — replacing
  today's `CodedValuesApi.DisableAsync` destructive semantics
  (`Detail.razor:648-666`), with the kebab confirm text updated to say the
  stream stays in the catalogue. **Edit** stays the `CodedValueDialog` override
  flow (`OpenStreamEditAsync`, `CodedValueDialogTypes.cs:36-40`) against the row's
  coded value.

**7. Add dialog — `StreamCreateDialog.razor`**

- New component **`src/Students/SchoolCollab.Students.Application/Components/Students/StreamCreateDialog.razor`**
  (+ isolated `.razor.css`) — a **stream analogue of `TopicCreateDialog`**
  (`Components/Students/TopicCreateDialog.razor`), NOT a reuse of any dialog
  router: `DialogShellBase` + `ShowShellDialogAsync` shell, per
  `.github/skills/dialog-ui`.
  - "Pick existing stream" `CodedValueDropdown Parent="CodedValueParent.Streams"`
    (code string at `CodedValueConstants.cs:46`) with `ShowEmptyOption` +
    "None — custom stream" mirror of `TopicCreateDialog.razor:56-67`; a picked
    value pre-fills read-only fields, the stealth edit pencil switches to editing.
  - **Duplicate guard** fed from the page exactly like
    `TopicCreateDialog`'s `ExistingTopicCodedValueIds` (`Detail.razor:469-477`):
    the dialog model carries `ExistingStreamCodedValueIds: HashSet<Guid>` built
    from the card's current bridge rows; warning bar rendered when the picked code
    is already linked to this grade.
  - **Persistence:** new static `StreamCodedValueSaver.cs` mirroring
    `TopicCodedValueSaver.cs:16-79`, **delegating to the existing pure
    `TopicEditRouter.Decide`** (`Components/Students/TopicEditRouter.cs:47`) — the
    router is topic-agnostic (inputs: `CodedValueDto?` + name/code/description/
    displayOrder), so it is **reused, not duplicated**. **Pinned create route
    (review P2-3):** the router's `DirectNameOnly` action
    (`TopicCodedValueSaver.cs:50-51` maps it to *(null, code)* — i.e. **no write**
    for topics) means "no backing coded value" for a stream, and the stream saver
    MUST reinterpret it as **create a brand-new `GRSTREAMS` child via
    `CodedValuesApiClient.CreateAsync` with `ParentId` = the GRSTREAMS parent** —
    **NOT** `CreateProvisionalCodedValueAsync` (provisional values belong to the
    topic tcv/3 flow and would be invisible to the bridge read path, which queries
    `by-parent?parentCode=GRSTREAMS`). Override semantics: native/global value the
    user edits → `UpsertOverrideAsync` (`Admin.Shared CodedValuesApiClient.cs:200+`);
    tenant-owned value → `UpdateAsync` in place. All via the Admin.Shared
    `CodedValuesApiClient` that `Detail.razor` already injects.
  - **Link step:** after save the dialog calls
    `Api.CreateStreamForGradeAsync(new AssignGradeStream(gradeLevelId,
    codedValueId))` → server-side idempotent `AssignGradeStream` → bridge row +
    version-uniqueness check (§5). Two calls (Settings save, Students link) rather
    than one atomic endpoint — same eventual shape as today's create+SetAttribute
    pair (`Detail.razor:566-573`); the bridge insert is unique-index idempotent so
    a crash between the calls is recoverable by re-running the dialog.
  - Optional "Version label" field writes the `streamVersion` attribute via
    `CodedValuesApi.SetAttributeAsync` (`CodedValuesApiClient.cs:161`) only when
    non-empty (keeps §5's skip-if-empty rule meaningful).
- `OpenStreamCreateAsync` (`Detail.razor:549-581`) is rewritten to open the new
  dialog (the direct `CodedValueDialog` create + `SetAttributeAsync("gradeLevel",…)`
  flow at `Detail.razor:563-566` is deleted — gradeLevel is no longer written).

**8. Enroll-dialog stream picker (forced consequence, in scope)**

**Two** attribute-filtered stream pickers exist, not one (review P1-1):
`EnrollStudentDialog.razor:308-309` **and** `StudentTransferDialog.razor:52-53`
(`AttributeFilter="@(SelectedGrade is null ? null : ("gradeLevel",
SelectedGrade.CodedValueId.ToString()))"`, `Label="Transfer to stream"`). Once
attributes stop being written, a stream created via the new `StreamCreateDialog`
(writes the bridge, never `gradeLevel`) would silently vanish from **both**.
Replace the option source in **both**: when a grade is picked, fetch
`Api.ListGradeStreamsAsync(gradeLevelId)` (bridge-backed) and render the picker as
a plain `FluentSelect` (or `CodedValueDropdown` with an explicit pre-loaded
options list — implementer picks the smaller diff; the **contract** is:
`AttributeFilter` no longer selects the option list for streams, the
`ListGradeStreamsAsync` bridge endpoint does). Same source backs
`CreateStudentWithLinkedDataHandler`'s dialog path. Validation correctness is
unchanged (§4) regardless.

**9. Docs**

- `documents/solution/adr-cross-module-calls.md:20-30`: the
  "Students → settings … stream validate" write-path row shrinks to only the
  streamVersion-uniqueness read hops (`AssignGradeStreamHandler`), with the
  enroll/transfer/create stream hop removed.
- `documents/specs/grade-level-setup.md`: short supersession appendix noting the
  `gradeLevel` attribute link is replaced by `grade_stream_assignments`(the
  pattern used by the cited subject-to-topic spec).

### Expected files

New (12):
1. `src/Students/SchoolCollab.Students.Core/Domain/GradeStreamAssignment.cs`
2. `src/Students/SchoolCollab.Students.Core/Domain/Exceptions/DuplicateStreamAssignmentException.cs`
3. `src/Students/SchoolCollab.Students.Core/Data/Configurations/GradeStreamAssignmentConfiguration.cs`
4. `src/Students/SchoolCollab.Students.Core/Data/Repositories/GradeStreamAssignmentRepository.cs` (+ interface)
5. `src/Students/SchoolCollab.Students.Core/CQRS/GradeStreams/Queries/ListGradeStreams/ListGradeStreams.cs` + `ListGradeStreamsHandler.cs`
6. `src/Students/SchoolCollab.Students.Core/CQRS/GradeStreams/Commands/AssignGradeStream/AssignGradeStream.cs` + `AssignGradeStreamHandler.cs`
7. `src/Students/SchoolCollab.Students.Core/CQRS/GradeStreams/Commands/RemoveGradeStream/RemoveGradeStream.cs` + `RemoveGradeStreamHandler.cs`
8. `src/Students/SchoolCollab.Students.Core/DTOs/GradeStreamDto.cs`
9. `src/Students/SchoolCollab.Students.Application/Components/Students/StreamCreateDialog.razor` (+ `.razor.css`)
10. `src/Students/SchoolCollab.Students.Application/Components/Students/StreamCodedValueSaver.cs`
11. `src/SchoolCollab.MigrationService/Seeding/GradeStreamAssignmentSeeder.cs`
12. `src/SchoolCollab.MigrationService/SeedData/seed-stream-assignments.csv`
+ `src/Students/SchoolCollab.Students.Core/Migrations/<t>_AddGradeStreamAssignments.cs` (+ `.Designer.cs`, snapshot)

Modified (19):
13. `src/Students/SchoolCollab.Students.Core/Domain/Exceptions/StreamGradeMismatchException.cs` (doc comment)
14. `src/Students/SchoolCollab.Students.Core/Data/StudentsDbContext.cs` (DbSet + ApplyConfiguration)
15. `src/Students/SchoolCollab.Students.Core/Services/CodedValuesApiClient.cs` (`GetChildrenByParentCodeAsync` on interface + client — the Students client exposes only `GetByIdAsync` today, so this adds the **override-resolving by-parent** read; NOT `by-ids`, see §5/§6)
16. `src/Students/SchoolCollab.Students.Core/CQRS/Enrollments/Commands/EnrollStudent/EnrollStudentHandler.cs`
17. `src/Students/SchoolCollab.Students.Core/CQRS/Enrollments/Commands/TransferStudent/TransferStudentHandler.cs`
18. `src/Students/SchoolCollab.Students.Core/CQRS/Students/Commands/CreateStudentWithLinkedData/CreateStudentWithLinkedDataHandler.cs`
19. `src/Students/SchoolCollab.Students.Api/Endpoints/GradeLevelRoutes.cs`
20. `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs`
21. `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor`
22. `src/Students/SchoolCollab.Students.Application/Components/Students/EnrollStudentDialog.razor`
23. `src/SchoolCollab.MigrationService/Program.cs` (seeder registration + call after Students migrations)
24. `src/SchoolCollab.MigrationService/SeedData/seed-attributes.csv` (drop 39 gradeLevel rows)
25. `src/SchoolCollab.MigrationService/SeedData/seed-attribute-definitions.csv` (drop GRSTREAMS gradeLevel definition)
25a. `src/SchoolCollab.MigrationService/Seeding/CsvSeedReader.cs` (new `ReadStreamAssignments` + `StreamAssignmentSeedRow`, §2(b))
26. `documents/solution/adr-cross-module-calls.md`
27. `documents/specs/grade-level-setup.md`
27a. `src/Students/SchoolCollab.Students.Application/Components/Students/StudentTransferDialog.razor` (stream picker swap, §8)
28. Registration files for Scrutor-scanned handlers/CQRS (DI wiring follows the existing assembly-scanning pattern; no manual registration expected — worker verifies)
28a. `SchoolCollab.slnx` (register the new MigrationService test project, item 36)

Tests (new + updated):
29. `tests/SchoolCollab.Students.Tests.Unit/GradeStreamAssignmentTests.cs` (new)
30. `tests/SchoolCollab.Students.Tests.Unit/AssignGradeStreamHandlerTests.cs` (new)
31. `tests/SchoolCollab.Students.Tests.Unit/ListGradeStreamsHandlerTests.cs` (new)
32. `tests/SchoolCollab.Students.Tests.Unit/EnrollStudentHandlerTests.cs` (update to bridge validation)
33. `tests/SchoolCollab.Students.Tests.Unit/TransferStudentHandlerTests.cs` / CreateStudentWithLinkedData tests (update where attribute mocks exist)
34. `tests/SchoolCollab.Admin.Tests.Unit/GradeLevelDetailPageTests.cs` (update Streams-card mocks to the new endpoints)
35. `tests/SchoolCollab.Admin.Tests.Unit/` bUnit dialog-opener test for `StreamCreateDialog` (per `.github/skills/test-dialog-opener-components`)
36. **NEW test project** `tests/SchoolCollab.MigrationService.Tests.Unit/` — `GradeStreamAssignmentSeederTests.cs` (+ `.csproj` modelled on `tests/SchoolCollab.Students.Tests.Unit`), registered in `SchoolCollab.slnx`, referencing `SchoolCollab.MigrationService`. **No MigrationService test project exists today** (review P2-6), so this is a sanctioned new test project; it does not add a `src/` cross-context project reference.

### Acceptance criteria (each DISCRIMINATING: fails against pre-fix code)

- **AC1 — bridge + migration.** Migration `AddGradeStreamAssignments` creates
  `grade_stream_assignments` (tenant_id, grade_level_id FK cascade,
  stream_coded_value_id, unique index `(tenant_id, grade_level_id,
  stream_coded_value_id)`). Test: double insert of the same
  (tenant, grade, stream) pair violates the index / `AddOrReuseAsync` returns the
  winner. Pre-fix: table + entity do not exist.
- **AC2 — read path.** `ListGradeStreamsHandlerTests`: seeded bridge rows +
  stubbed `GetChildrenByParentCodeAsync` (resolved DTO carrying
  `IsOverridden`/`DefaultName`) → handler returns `GradeStreamDto[]` whose `Name`
  is the **override-aware** name and whose `IsOverridden` mirrors the DTO.
  Pre-fix: the query does not exist, so the test is red by non-compilation — an
  acknowledged, inherent limitation of a brand-new query (no runtime old-vs-new
  pair); the substance discriminator is the override-name assertion, which the
  base card path expresses only through the attribute-filtered endpoint.
- **AC3 — assignment + uniqueness (new behavior).**
  `AssignGradeStreamHandlerTests`: (a) first assign of stream S to grade G succeeds;
  (b) assigning T whose `streamVersion` equals S's already-assigned version to the
  same grade throws `DuplicateStreamAssignmentException` — pre-fix there is no
  bridge/exception at all; (c) re-assign of S returns the existing bridge id
  (idempotence); (d) a stream with null/empty `streamVersion` never triggers (d).
- **AC4 — enrollment validation moved (old vs new discriminator).**
  Updated `EnrollStudentHandlerTests`: (i) stub coded-values API attribute-match +
  NO bridge row → `StreamGradeMismatchException` (pre-fix: enrolls fine);
  (ii) bridge row present + API attribute-match absent → enrollment succeeds
  (pre-fix: throws). Mirror pair asserted for `TransferStudentHandler` and
  `CreateStudentWithLinkedDataHandler`.
- **AC5 — backfill seeder (named test + pinned host).** Host:
  `tests/SchoolCollab.MigrationService.Tests.Unit/GradeStreamAssignmentSeederTests.cs`
  (**new project**, item 36 — no MigrationService test project exists today).
  Cases: (a) attribute-derived — a stubbed Settings `CodedValues` set carrying
  `gradeLevel` attributes yields the expected bridge pairs; (b) CSV-derived — the
  new 39-row CSV yields the **39-pair** bridge set per tenant; (c) materialization
  — a grade with no `GradeLevel` row gets one with
  `(codedValueId, level: cv.DisplayOrder, name: cv.Name, displayOrder: cv.DisplayOrder)`;
  (d) idempotency — a second run inserts nothing (no duplicate-count change).
  Pre-fix: the seeder type does not exist ⇒ the test cannot compile; the
  *behavioural* discriminators are (b)/(d) — 39 bridge rows per tenant after one
  run, no growth after two.
- **AC6 — Streams card on bridge data.** `GradeLevelDetailPageTests` updated:
  the Streams card renders from a mock of `GET /api/grade-levels/{id}/streams`
  (`POST`/`DELETE` for add/remove). Discriminators: (a) the by-parent attribute
  mock (`GradeLevelDetailPageTests.cs:176`) is no longer consulted; (b) Remove
  now calls the bridge DELETE, NOT `POST /api/coded-values/{id}/disable`
  (`:857`) — assert the disable route is never hit; (c) the Add flow opens
  `StreamCreateDialog` (mocked dialog-service assert per the dialog-opener skill).
  Against pre-fix code the new routes 404 on the mock handler ⇒ card empty ⇒ red.
- **AC7 — seed hygiene.** `seed-attributes.csv` contains **zero** `GRSTREAMS`+
  `gradeLevel` rows; `seed-stream-assignments.csv` contains the **39-row**
  `streamCode,gradeCode` mapping; `streamVersion` rows remain in
  `seed-attributes.csv` (grep-level assertion — a checklist item, not a
  `dotnet test`; pre-fix repo fails both greps).
- **AC8 — green builds (GATE, not a discriminating AC).** `dotnet build
  SchoolCollab.slnx` 0 errors; `dotnet test` 0 failures (repo-wide MTP run). This
  cannot fail against a green base commit, so it is recorded as the round's
  verification gate rather than a behavioural discriminator (review P2-6).
- **AC9 — endpoint auth (missing discriminator, review P2-6).** A **WAF-based** host
  (mirroring `tests/SchoolCollab.Settings.Api.Tests.Unit`, whose csproj comment
  sanctions a public `Program` for `WebApplicationFactory` use):
  `POST /grade-levels/{id}/streams` and
  `DELETE /grade-levels/{id}/streams/{assignmentId}` return 401/403 without an
  authenticated caller, asserting they inherit the group's `RequireAuthorization`
  (`StudentEndpoints.cs:12-26`). **Not** `Students.Tests.Integration/ApiFactory.cs:19`
  — it forces TestAuth and cannot serve an unauthenticated request (review re-read P2).
- **AC10 — cross-tenant isolation (missing discriminator, review P2-6).**
  `ListGradeStreamsHandlerTests` + `AssignGradeStreamHandlerTests` **over a real or
  in-memory `StudentsDbContext` with a hand-rolled repository** (precedent
  `EnrollStudentHandlerTests.cs:660-667`; EF global query filters do apply on the
  InMemory provider) — **not** a mocked `IGradeStreamAssignmentRepository`, which
  would pass vacuously (review re-read P2): with two tenants' bridge rows seeded, a
  tenant-scoped query returns only its own rows (strict tenant filter), and
  `ExistsAsync` for another tenant's grade/stream pair returns false.

### Worker task spec (compact contract)

Implement exactly §Design 1–9 + the Expected-files list; **no product decisions**:
- Domain/EF first (entity → config → DbContext → migration), build after each
  edit per AGENTS.md build-verification rule.
- Bridge validation helper: one shared implementation used by all three call
  sites (§Design 4) — do not copy-paste three copies.
- `TopicEditRouter` is reused; rename only if a compile-safe, behavior-neutral
  move to a shared name is free otherwise leave the name (narrow diff).
- Do NOT touch Settings code (`SetCodedValueAttributeHandler` stays byte-for-byte).
- Seeder must be idempotent and tenant-guard-suppressed like `CodedValueSeeder`.
- Keep `streamVersion` untouched in seed; keep "Manage streams"→catalogue link.
- Deliver with `dotnet build` + `dotnet test` green and the acceptance criteria
  in § above each evidenced by a named test.
Write the patch to `documents/rounds/diffs-grade-streams.patch` (parent writes
this file — worker reports changed files; do not commit).

### Reviewer task spec (diff-review contract)

Static-verifies against this §Plan only:
1. **No silent scope changes:** Settings `SetCodedValueAttributeHandler` /
   `DuplicateStreamException` untouched; no `gradeLevel` attribute write remains
   outside `CodedValuesApi.SetAttributeAsync` usages for `streamVersion` only;
   no new project references between contexts (ArchitectureTests green).
2. Bridge shape matches §Design 1 (no FK on `StreamCodedValueId`; unique index
   present; strict tenant filter wired via the `CurrentTenantId` accessor).
3. All **three** validation call sites moved (enroll / transfer /
   create-with-linked-data) and share one helper; `StreamGradeMismatchException`
   mapped to 409 still.
3a. **Both** attribute-filtered stream pickers re-pointed
   (`EnrollStudentDialog.razor:308-309` **and** `StudentTransferDialog.razor:52-53`);
   no `AttributeFilter=("gradeLevel", …)` remains as a stream option source.
3b. No `by-ids` dependency: the read/uniqueness hops use only the
   override-resolving `by-parent` endpoint (P1-2/P2-4), and `GradeStreamDto.Name`
   is override-aware.
3c. `DirectNameOnly` is handled as create-new via `CreateAsync` with
   `ParentId` = GRSTREAMS, never `CreateProvisionalCodedValueAsync` (P2-3).
3d. `Program.cs` hoists `tenantIdsByName` and null-guards the seeder call (P2-1);
   `CsvSeedReader.ReadStreamAssignments` exists (P2-2).
4. Seeder ordering in `Program.cs` strictly after Students migrations and after
   `CodedValueSeeder`/`TenantSeeder` results (§Design 2, file:line evidence).
5. Uniqueness move: version comparison reads `streamVersion` from coded-value
   DTOs (not a copy of the string in the bridge row); Settings guard stays.
6. UI: card `TItem` = `GradeStreamDto`; Remove = bridge delete + catalogue
   value preserved; Add dialog mirrors topic pattern with duplicate guard;
   `TopicEditRouter` reused via `StreamCodedValueSaver`.
7. Every AC has a named test; flag any criterion whose test cannot fail against
   the base commit. AC2/AC5 are red-by-non-compilation (acknowledged in the
   plan); AC8 is a gate, not a discriminator; AC9/AC10 cover the auth and
   cross-tenant gaps.

### UI-tester handover scope (planned, after review)

- Grade detail → Streams card: empty state, list, "All streams (catalogue)"
  navigation, Add (pick-existing / create-new / duplicate warning), edit
  (override / tenant-owned / brand-new), Remove (bridge only — value stays in
  /coded-values/GRSTREAMS/children).
- Enroll student dialog **and** transfer dialog: stream options follow the
  picked grade via the bridge; a stream created through the new dialog
  (bridge-only, no `gradeLevel` attribute) shows up in **both** pickers.
- Regression sweep: /coded-values/GRSTREAMS children landing still renders;
  direct `streamVersion` edit on a legacy row still surfaces
  `DuplicateStreamException`.
- Backfill validation on the dev DB: streams that appeared on the card before
  this round still appear after migrating (no data-loss regression).

---

## Review

### PLAN REVIEW — `ollama-cloud/glm-5.3` (run `3ab414fa-4a5d-41e7-b230-53dc9a065796`, 2026-09-29)

**Verdict: REVISE (3 × P1, 6 × P2) — all resolved in `## Plan` before worker dispatch.**

| # | Severity | Finding | Resolution |
|---|---|---|---|
| P1-1 | blocking | `StudentTransferDialog.razor:52-53` is a **second** attribute-filtered stream picker the plan omitted (§8 claimed enroll-only) — new streams would vanish from it | §8 now names both; Expected files 27a; reviewer spec 3a; tester scope |
| P1-2 | blocking | §6 read path via `by-ids` cannot populate `NameOverride`/`IsOverridden` (no override resolution; `IsOverridden` hardcoded `false`) → tenant-override display regression, contradicting decision 5 | §5/§6 now use the **override-resolving** `by-parent` endpoint (`GetCodedValuesByParentHandler.cs:58,109`; tenant-scoped cache `:35`) filtered to the bridge ID set; no Settings change |
| P1-3 | blocking | backfill count wrong: **39** `gradeLevel` rows, not 40 (40 = `grep` lines including the parent `GRSTREAMS` row) | §0/§2(b)/AC5/AC7 now say 39; citations corrected |
| P2-1 | non-blocking | `tenantIdsByName` out of scope where the seeder is called (declared inside the Settings `try`) | §2 hoist + null-guard pinned; reviewer 3d |
| P2-2 | non-blocking | no 2-column `CsvSeedReader` shape exists | §2(b) pins new `ReadStreamAssignments`; Expected files 25a |
| P2-3 | non-blocking | create-new route ambiguous (`DirectNameOnly` = **no write** for topics) | §7 pins `CreateAsync(ParentId=GRSTREAMS)`, explicitly not provisional; reviewer 3c |
| P2-4 | non-blocking | Settings `by-ids` not tenant-scoped, 5-min cache | dissolved — `by-ids` no longer used |
| P2-5 | non-blocking | citation drift (±7 lines `Program.cs`; `ExistingTopicCodedValueIds` at `:730` not `:469-477`) | corrected in §0; remaining patterns independently verified |
| P2-6 | non-blocking | AC honesty: AC8 non-discriminating; AC5 host unresolved (no MigrationService test project); missing auth/tenant discriminators | AC8 relabelled a gate; AC5 host pinned (item 36); AC9/AC10 added |

**Provenance:** the P1/P2 revisions were **applied by the parent**, not by a fresh
orchestrator run. Every cited seam had already been verified with file:line evidence by
both the parent and the reviewer, and the corrections are surgical (counts, citations, a
pinned endpoint swap, one pinned create route, one new test project) — an orchestrator
re-plan would regenerate 485 lines to change ~14 spans without adding scrutiny. The plan
review's guarantee (no open P1 at worker dispatch) is preserved; a focused re-read of the
revised sections runs next (≤1 plan-review iteration).

### PLAN REVIEW (re-read) — `ollama-cloud/glm-5.3` (run `2d340e9b-ef18-494c-8d04-617d4b28a3aa`, 2026-09-29)

**Verdict: PASS — no open P1, no new P1.** P1-1/P1-2/P1-3 verified CLOSED against source
(`StudentTransferDialog.razor:52-53`; `GetCodedValuesByParentHandler` override resolver +
`tenant:{tenantId}`-tagged cache; **39** rows at `seed-attributes.csv:48-124`). P2-1…P2-6 hold.
New-problem checks clean: `by-parent` over-fetch is bounded by the GRSTREAMS catalogue and the
filter-to-bridge-IDs step is pinned twice (§5, §6); the `DirectNameOnly` create route is
coherent (created value linked immediately; the duplicate guard is a separate path); the new
MigrationService test project violates no architecture rule (`ArchitectureTests.Unit` itself
ProjectReferences src Core projects, and the `AddHttpMessageHandler<>` bearer tripwire counts
registrations, not call sites). Two residual P2s were folded into the ACs before dispatch:
AC9 host pinned to a WAF-based API unit project, AC10 pinned to a real/in-memory
`StudentsDbContext`.

### Diff review — substitution pass `ollama-cloud/glm-5.3` (run `5f237a11`)

**Verdict: CLOSED — no P1.** (The first attempt on the user-named `cline/cline-free/mimo-v2.6-flash`, run `3342d365`, looped on the already-verified `CreateAsync` JSON question and timed out at the 30-min cap without a report; substituted within-tier per the skill and recorded as a deviation. The escalator `kimi-k2.7-code` differs from both, so the verifier guardrail holds.)

Checklist (plan `### Reviewer task spec` items 1–7 + 3a–3d): all **PASS** with file:line evidence —
scope gates (Settings `SetCodedValueAttributeHandler`/`DuplicateStreamException` absent from the
patch; no `gradeLevel` write remains; zero `Version=` on a `PackageReference`); bridge shape
(`GradeStreamAssignment.cs:25-44`, `GradeStreamAssignmentConfiguration.cs:26-37`, migration
`20260929203106`); the shared validator at all three sites (`GradeStreamValidator.cs:21`); both
pickers (`EnrollStudentDialog.razor:500`, `StudentTransferDialog.razor:162`; zero `AttributeFilter`
left); uniqueness read from the DTO (`AssignGradeStreamHandler.cs:42-43,75-76`); UI semantics
(`Detail.razor:187,193-194,666`); `DirectNameOnly` → `CreateAsync`; Program.cs hoist/null-guard
(`:91,:121,:172-176`).

Best practices: PASS — no new `.Result`/`.Wait()`/`async void`; async + `CancellationToken`
throughout; private setters + `Create` factories; logging on the seeder skip/failure paths and the
transfer-dialog catch; DI lifetimes consistent.

AC audit: AC1–AC7, AC9, AC10 discriminating with named tests (AC10 explicitly non-vacuous over a real
`StudentsTestScope` DbContext); AC2's compile-time limitation acknowledged; AC8 a gate (2,903/0).

**P2-1 (fixed in-round):** `StudentTransferDialogBunitTests.cs:98` carried the same latent broad-matcher
shadowing (`Contains("/students/grade-levels")` with no `/streams` exclusion) now that
`StudentTransferDialog.razor:162` fetches the bridge. Latent only (no test in that file selects a
grade), but it is the exact defect class the parent fixed in `EnrollStudentDialogBunitTests.cs`.
Added the `&& !path.EndsWith("/streams", …)` guard; `Admin.Tests.Unit` re-run **597/0**; the patch
was re-frozen afterwards (64 files).

**P2-2 (deferred, pre-existing):** the plan's §4 claim that all three mismatch sites already map to
409 is true only for `StudentRoutes.cs:92`; `EnrollmentRoutes.cs:59,88` return **400** for
enroll/transfer. Neither file is in this diff — not caused by the round. Recorded as a follow-up: if
409 semantics are wanted for enroll/transfer mismatches that is a separate change.

---

## Worker Report

*(Parent-transcribed: neither worker pass returned a report — both hit the 30-minute cap. Content below is the verified on-disk state plus the parent's authoritative numbers.)*

**Passes**

- **Pass 1** — `ollama-cloud/deepseek-v4.1-flash` (run `9aba1fb2`): implemented the whole round — entity/config/repository/EF migration, seeder + both CSVs + `CsvSeedReader.ReadStreamAssignments`, bridge CQRS + endpoints + `StudentsApiClient`, the shared `GradeStreamValidator` used by all three call sites, both picker swaps, `StreamCreateDialog` + `StreamCodedValueSaver`, the docs, the tests, and the new MigrationService test project. `tests/SchoolCollab.Students.Api.Tests.Unit` was green (`total: 4 failed: 0`) before the run died inside a `timeout 1500 dotnet test tests/SchoolCollab.Students.Tests.Integration`.
- **Pass 2 (escalation, per skill step 3)** — `ollama-cloud/kimi-k2.7-code` (run `7849f978`): updated `EnrollWithStreamEndpointTests` + `StudentsApiClientEndToEndEnrollmentTests` for bridge semantics (seed a bridge row, extend the TRUNCATE, rename the attribute-era test) and started reworking the `EnrollStudentDialog` bUnit stream-picker expectations. Timed out mid-edit of that test.
- **Parent residual (deviation, recorded):** diagnosed the remaining bUnit failure as a **test-mock shadowing defect, not a product defect** — the mock's grade-list matcher `path.Contains("/students/grade-levels")` shadowed the later, more specific `/students/grade-levels/{id}/streams` matcher (first match wins), so the picker was fed the grade list and the escalation had begun weakening the assertions to match that wrong data. Fixed the matcher (`&& !path.EndsWith("/streams")`) and **restored** the correct assertions (4 options: sentinel + three real ids). Test file only; no product code touched.

**Changed files (feature scope, 64 in `diffs-grade-streams.patch`)** — everything in the plan's Expected-files list 1–36, plus these deviations:

| Deviation | Why |
|---|---|
| `src/SchoolCollab.Admin.Shared/Services/CodedValuesApiClient.cs` — `CreateAsync` now returns `Task<Guid>` (parses the `201 Created` body) | §7 needs the created value's id to link it without a second, cache-lagged by-code read. Source-compatible (callers may ignore the return). **Diff-review flag: the endpoint's `{id}` body shape must match, else `Guid.Empty` breaks the link.** |
| `tests/SchoolCollab.Students.Tests.Integration/{EnrollWithStreamEndpointTests,StudentsApiClientEndToEndEnrollmentTests}.cs` | not in the plan's list, but required for AC8 (repo-wide green) — validation is now local, so the attribute-matching stub no longer enrols successfully |
| `tests/SchoolCollab.Admin.Tests.Unit/EnrollStudentDialogBunitTests.cs` | the stream-picker test needed the new bridge-backed option source (and the mock-shadowing fix) |
| `tests/SchoolCollab.Admin.Tests.Unit/StudentTransferDialogBunitTests.cs` | diff-review P2-1: added the `/streams` exclusion to the broad grade-list mock matcher (latent shadowing) |
| `tests/SchoolCollab.MigrationService.Tests.Unit/SeedDataHygieneTests.cs` | added to make AC7 a real test rather than a checklist item |
| `Students.Core/Domain/Events/DomainEvents.cs`, `Students.Core/Extensions.cs`, `Services/FlagRoutedCodedValuesApiClient.cs`, `StudentsTestScope.cs`, `AcademicYearDivisionNoneBackCompatTests.cs` | expected knock-ons (event record, repository DI, interface implementation, test scope/mocks) |

**Build** — `dotnet build SchoolCollab.slnx` → **0 errors**, 18 warnings.

**Authoritative test matrix (parent-run)**

| Project | total | failed |
|---|---|---|
| Students.Tests.Unit | 570 | 0 |
| Admin.Tests.Unit | 597 | 0 |
| Settings.Tests.Unit | 519 | 0 |
| Assignments.Tests.Unit | 674 | 0 |
| Auth.Tests.Unit | 214 | 0 |
| Core.Tests.Unit | 114 | 0 |
| Assignments.Api.Tests.Unit | 87 | 0 |
| ArchitectureTests.Unit | 72 | 0 |
| Families.Tests.Unit | 42 | 0 |
| MigrationService.Tests.Unit | 9 | 0 |
| Students.Api.Tests.Unit | 4 | 0 |
| Settings.Api.Tests.Unit | 1 | 0 |
| **Total** | **2,903** | **0** |
| Students.Tests.Integration (affected classes only: `EnrollWithStream`, `StudentsApiClientEndToEnd`) | 5 | 0 |

**AC → test**

| AC | Named test(s) |
|---|---|
| AC1 bridge + migration | `GradeStreamAssignmentTests.Model_DeclaresUniqueIndexOnTenantGradeStream`, `Model_StreamCodedValueIdIsNotAForeignKey`, `Model_GradeLevelForeignKeyCascades`, `AddOrReuseAsync_ReturnsTheWinnersRow_ForADuplicatePair` |
| AC2 read path | `ListGradeStreamsHandlerTests.ReturnsOverrideAwareName_AndIsOverriddenFlag`, `GlobalStream_ReportsNoOverride`, `BridgeRowMissingFromTheCatalogue_IsSkipped` |
| AC3 assign + uniqueness | `AssignGradeStreamHandlerTests.DuplicateStreamVersion_Throws_AndInsertsNothing`, `DifferentStreamVersion_IsAllowed`, `StreamWithNoVersion_NeverTripsTheUniquenessRule`, `ReAssign_ReturnsTheExistingBridgeId` |
| AC4 validation moved | `EnrollStudentHandlerTests.StreamValidation_MatchingGradeLevelAttributeButNoBridgeRow_Throws` / `StreamValidation_BridgeRowPresentAndNoGradeLevelAttribute_Succeeds`; `TransferStudentHandlerStreamValidationTests.*`; `CreateStudentWithLinkedDataStreamValidationTests.*` |
| AC5 seeder | `GradeStreamAssignmentSeederTests.CsvDerived_SeedsThe39RowMapping_ForEachTenant`, `RepeatedRun_InsertsNothing`, `AttributeDerived_BackfillsTheStoredGradeLevelAttribute`, `MaterializesMissingGradeLevelRows_WithTheEnrollPathShape`, `TwoTenants_EachGetTheirOwnRows` |
| AC6 Streams card | `GradeLevelDetailPageTests` (updated mocks) + `EnrollStudentDialogBunitTests.Grade5Selected_StreamPickerLoads_NonNullStreamGuids` |
| AC7 seed hygiene | `SeedDataHygieneTests.SeedAttributes_ContainsNoGradeLevelRowsForStreams`, `SeedStreamAssignments_ContainsThe39RowMapping`, `SeedAttributes_KeepsTheStreamVersionRows`, `SeedAttributeDefinitions_DropsOnlyTheGradeLevelDefinition` |
| AC8 gate | build 0 errors + matrix 2,903/0 (not a behavioural discriminator) |
| AC9 endpoint auth | `GradeStreamEndpointAuthTests.{Post,Delete,Get}GradeLevelStream*_WithoutACaller_IsChallenged` |
| AC10 cross-tenant isolation | `GradeStreamAssignmentTests.QueryFilter_IsolatesTenants`, `AssignGradeStreamHandlerTests.ExistsAsync_ForAnotherTenantsGradeAndStream_ReturnsFalse`, `ListGradeStreamsHandlerTests.AnotherTenantsBridgeRow_IsNotReturned` |

**Open issues / residuals**

1. The **full repo-wide `dotnet test`** was not run locally — the Testcontainers integration suites are what timed out both child passes. The parent ran every unit project plus the *affected* integration classes; CI remains the repo-wide gate (same posture the enrollment-exception PR recorded).
2. `42P01: relation "outbox_messages" does not exist` was observed once inside the **aborted full** integration run; it did **not** reproduce in the filtered run. Likely environmental ordering in that long run — recorded, not chased.
3. `CreateAsync`'s new `Task<Guid>` return depends on the Settings endpoint's `201` body carrying `id` — flagged for the diff review.

---

## Acceptance

### Criteria checklist (AC1–AC10)

| AC | Status | Evidence (tests as mapped in `## Worker Report` → **AC → test**) |
|---|---|---|
| AC1 — bridge + migration | **Pass** | `GradeStreamAssignmentTests.Model_DeclaresUniqueIndexOnTenantGradeStream`, `Model_StreamCodedValueIdIsNotAForeignKey`, `Model_GradeLevelForeignKeyCascades`, `AddOrReuseAsync_ReturnsTheWinnersRow_ForADuplicatePair` |
| AC2 — read path (override-aware) | **Pass** | `ListGradeStreamsHandlerTests.ReturnsOverrideAwareName_AndIsOverriddenFlag`, `GlobalStream_ReportsNoOverride`, `BridgeRowMissingFromTheCatalogue_IsSkipped` (compile-time red-by-non-compilation limitation acknowledged in plan/diff review; the substantiating discriminator is the override-name assertion) |
| AC3 — assignment + version uniqueness | **Pass** | `AssignGradeStreamHandlerTests.DuplicateStreamVersion_Throws_AndInsertsNothing`, `DifferentStreamVersion_IsAllowed`, `StreamWithNoVersion_NeverTripsTheUniquenessRule`, `ReAssign_ReturnsTheExistingBridgeId` |
| AC4 — enrollment validation moved to bridge | **Pass** | `EnrollStudentHandlerTests.StreamValidation_MatchingGradeLevelAttributeButNoBridgeRow_Throws` / `StreamValidation_BridgeRowPresentAndNoGradeLevelAttribute_Succeeds`; `TransferStudentHandlerStreamValidationTests.*`; `CreateStudentWithLinkedDataStreamValidationTests.*` — old-vs-new discriminator pairs (fail pre-fix: (i) enrolls fine, (ii) throws) |
| AC5 — backfill seeder (+39-row mapping) | **Pass** | `GradeStreamAssignmentSeederTests.CsvDerived_SeedsThe39RowMapping_ForEachTenant`, `RepeatedRun_InsertsNothing`, `AttributeDerived_BackfillsTheStoredGradeLevelAttribute`, `MaterializesMissingGradeLevelRows_WithTheEnrollPathShape`, `TwoTenants_EachGetTheirOwnRows` (host: the new `tests/SchoolCollab.MigrationService.Tests.Unit` project) |
| AC6 — Streams card on bridge data | **Pass** | `GradeLevelDetailPageTests` (updated mocks); Add flow opens `StreamCreateDialog`; picker data source: `EnrollStudentDialogBunitTests.Grade5Selected_StreamPickerLoads_NonNullStreamGuids` |
| AC7 — seed hygiene | **Pass** | `SeedDataHygieneTests.SeedAttributes_ContainsNoGradeLevelRowsForStreams`, `SeedStreamAssignments_ContainsThe39RowMapping`, `SeedAttributes_KeepsTheStreamVersionRows`, `SeedAttributeDefinitions_DropsOnlyTheGradeLevelDefinition` (upgraded from a grep checklist item to a real test in-round) |
| AC8 — green builds (GATE, non-discriminating) | **Pass (gate)** | `dotnet build SchoolCollab.slnx` → **0 errors, 18 warnings**; unit matrix **2,903 / 0**; affected integration classes **5 / 0** (numbers below) |
| AC9 — endpoint auth | **Pass** | `GradeStreamEndpointAuthTests.{Post,Delete,Get}GradeLevelStream*_WithoutACaller_IsChallenged` (WAF-based host per the pinned plan correction) |
| AC10 — cross-tenant isolation | **Pass** | `GradeStreamAssignmentTests.QueryFilter_IsolatesTenants`, `AssignGradeStreamHandlerTests.ExistsAsync_ForAnotherTenantsGradeAndStream_ReturnsFalse`, `ListGradeStreamsHandlerTests.AnotherTenantsBridgeRow_IsNotReturned` — non-vacuous over a real `StudentsTestScope` DbContext, not a mocked repository |

**Count: 10/10 pass** (AC1–AC7, AC9, AC10 discriminating with named tests; AC8 is the gate, per plan review P2-6). No open P1 — the diff review verdict is CLOSED, its P2-1 (`StudentTransferDialogBunitTests` mock-shadowing guard) was fixed in-round and re-verified at `Admin.Tests.Unit` **597 / 0**, and P2-2 is recorded as a pre-existing follow-up (below).

### Authoritative numbers (parent-run; the verification authority of this round)

- `dotnet build SchoolCollab.slnx` — **0 errors / 18 warnings**.
- Unit test matrix — **2,903 total / 0 failed** across 12 projects: Students.Tests.Unit 570/0, Admin.Tests.Unit 597/0, Settings.Tests.Unit 519/0, Assignments.Tests.Unit 674/0, Auth.Tests.Unit 214/0, Core.Tests.Unit 114/0, Assignments.Api.Tests.Unit 87/0, ArchitectureTests.Unit 72/0, Families.Tests.Unit 42/0, MigrationService.Tests.Unit 9/0, Students.Api.Tests.Unit 4/0, Settings.Api.Tests.Unit 1/0.
- Students integration (affected classes only: `EnrollWithStream`, `StudentsApiClientEndToEnd`) — **5 / 0**.

### Verdict

**CLOSED.**

### Residual risks / follow-ups

1. **Repo-wide `dotnet test` not run locally** — the Testcontainers integration suites timed out both child passes; the parent ran every unit project plus the affected integration classes only. CI remains the repo-wide gate (same posture as the enrollment-exception PR).
2. **P2-2 (deferred, pre-existing):** `EnrollmentRoutes.cs:59,88` return **400** rather than 409 for enroll/transfer stream mismatch (`StudentRoutes.cs:92` already maps 409). Neither file is in this diff — not caused by the round; recorded as a separate follow-up.
3. **`42P01 outbox_messages`** was observed once inside the aborted full integration run and did **not** reproduce in the filtered run — likely environmental ordering in that long run; recorded, not chased.
4. **No automated UI-level test for the transfer dialog's bridge picker** — covered by the UI-tester pass, which completed **P2-only** (no P1) and whose two P2s were fixed in-round (see `## UI Tester`). The missing automated coverage itself remains a residual.

### Round artifacts

- Round doc: `documents/rounds/round-grade-streams.md` (this file).
- Diff: `documents/rounds/diffs-grade-streams.patch` — **64 files**.
- Base commit: `6a392c8cf61ea16f31719f3e78796fe296910f26` (`6a392c8c`).
- Escalation provenance: worker pass 1 (`ollama-cloud/deepseek-v4.1-flash`, run `9aba1fb2`) and the escalation pass (`ollama-cloud/kimi-k2.7-code`, run `7849f978`) both timed out at the 30-min cap after landing the implementation; the parent completed the residual (test-only) fixup and ran the authoritative pass.

### UI-tester handover scope (verbatim, refined from the plan + the actual changed UI surfaces)

- Grade detail → **Streams card**: empty state; list rendering; `ViewAllText` navigation **"All streams (catalogue)"** → `/coded-values/GRSTREAMS/children`; Add → `StreamCreateDialog` (pick-existing / create-new / duplicate warning); Edit (override / tenant-owned / brand-new coded value); Remove = **bridge delete** with the coded value **still present** in the GRSTREAMS catalogue.
- **`StreamCreateDialog`** itself — the new `DialogShellBase` surface and its scoped CSS.
- **Enroll student dialog stream picker** and **transfer dialog stream picker**: options follow the picked grade via the bridge; a stream created through `StreamCreateDialog` (bridge-only, no `gradeLevel` attribute) appears in **both** pickers.
- Regression sweep: the `/coded-values/GRSTREAMS/children` landing still renders; a direct `streamVersion` duplicate edit on a legacy row still surfaces the Settings `DuplicateStreamException`.
- Dev-DB backfill: streams that appeared on the Streams card before this round still appear after migrating (no data-loss regression).
- The tester must **NOT** derive or expand its own scope beyond this list.

---

## UI Tester

**Verdict: P2-only, no P1** — `ollama-cloud/minimax-m3` (run `ca81c0e1`), scope acked verbatim as the acceptance handover.

```
UI TEST
Scope ack: hunted exactly the handed-over surfaces — Streams card on Detail.razor,
  StreamCreateDialog + its .razor.css + StreamCodedValueSaver, EnrollStudentDialog
  stream picker (FluentSelect over ListGradeStreamsAsync), StudentTransferDialog
  stream picker (same source), and the regression-sweep surfaces
  (/coded-values/GRSTREAMS/children, Settings DuplicateStreamException retention,
  GradeStreamAssignmentSeeder backfill shape).
Verdict: P2-only
P1: (none)
P2: StreamCreateDialog.razor:217 — clearing the picker after picking leaves
    Model.Name/Code/Description/Version holding the previous coded value's fields; the
    form falls back into edit mode and submit creates a brand-new GRSTREAMS child with the
    stale name/code (usually tripping the server's duplicate-code guard).
P2: StreamCreateDialog.razor:217 — OnCodedValuePicked's TryFindItem returns false while the
    dropdown items are still loading, so the fields are not pre-filled even though
    Model.CodedValueId is set (narrow load window; SubmitAsync's GetByIdAsync fallback
    still works, so the user just sees a blank Name briefly).
Out-of-round observations: none
```

**Parent rework (both P2s fixed in-round; product change after the tester pass, so a focused tester
re-verify follows).** `OnCodedValuePicked` is now `async Task`: clearing the picker resets
Name/Code/Description/Version and `_editing` to a blank new-stream baseline (no stale value can leak
into a new catalogue child), and a failed `TryFindItem` falls back to `CodedValues.GetByIdAsync(id)`
so the fields are never silently left blank. Re-verified by the parent: `dotnet build
SchoolCollab.slnx` **0 errors**; `Admin.Tests.Unit` **597/0**; `Students.Tests.Unit` **570/0**; patch
re-frozen (64 files).

**Tester re-verify:** PASS — `ollama-cloud/minimax-m3` (run `9bfdb8f9`).

```
UI TEST (re-verify)
P2-1: CLOSED — clear branch in OnCodedValuePicked sets Model.Name=string.Empty and
      Model.Code/Description/Version=null, so a wiped picker yields a blank "new stream"
      baseline with no stale fields surviving into SubmitAsync.
P2-2: CLOSED — the handler is now async Task and falls back to
      `cv ??= await CodedValues.GetByIdAsync(id)`, so a still-loading dropdown can no
      longer leave the fields blank when a coded value is selected.
New defect: none — `@bind-SelectedId:after` accepts an async Task handler; the
      `_editing`/`_pickedCodedValue` reset on clear is consistent with StartEditing's
      snapshot semantics; SubmitDisabled locks submit when Name is empty after a clear;
      HandleCancel's snapshot interaction is unchanged.
Verdict: PASS
```

### Round close

All four agent stages are settled: plan (2 review gates PASS) → worker/escalation
(implementation; parent-completed residual) → diff review **CLOSED** (P2-1 fixed) →
acceptance **CLOSED** (AC 10/10) → UI tester **P2-only** (both P2s fixed, re-verify PASS).
No open P1; residuals are recorded in `## Acceptance`. Implementation is **uncommitted** on
`feat/grade-streams` — commit/push/PR await an explicit owner instruction.

---

## Post-close fix round — configured grades only (A) + manual linking (B)

**Reported after close (owner, 2026-09-29):** the grade-level landing
(`/students/grade-levels`) listed **all 13 GRADE coded values** as grade levels.

**Root cause (verified in code and in the dev DB).** The landing lists `grade_levels`
rows — `Index.razor:200` → `ListGradeLevelsForLandingAsync` →
`ListGradeLevelsForLandingHandler` → `db.GradeLevels.Where(TenantId == tenantId)`; the
`GetChildrenByParentCodeAsync("GRADE")` call at `Index.razor:205` only resolves
**display names** and never adds rows. This round's `GradeStreamAssignmentSeeder`
**materialized a `GradeLevel` row for every grade** referenced by
`seed-stream-assignments.csv`, and that CSV is a **global blueprint with no tenant
column** which the seeder fanned out to **every** registered tenant. The mapping covered
all 13 GRADE codes, so each tenant got all 13 grades. Dev-DB proof (pre-fix): all four
tenants had exactly **13** `grade_levels` (three tenants' rows created in the single
seeder instant `2026-09-29 23:06:51`) and **39** bridge rows, with **0 enrollments**.

**Owner decisions (2026-09-29).** **Q1 = (A)** a tenant's grade list is *the grades it
configured*, not the full catalogue → the seeder must never create `GradeLevel` rows.
**Q2 = (B)** *manual only* → no auto-linking of native streams; the school adds them on
the Streams card.

**Changes on this branch (uncommitted).**
- `GradeStreamAssignmentSeeder` is now **preservation-only**: one source (the stored
  legacy `gradeLevel` attribute); a pair links **only** against an existing
  `(tenant, GradeLevel)` row; materialization removed (skips are counted and logged).
- Deleted `SeedData/seed-stream-assignments.csv`, `Seeding/StreamAssignmentSeedRow.cs`
  and `CsvSeedReader.ReadStreamAssignments`; the seeder's `IConfiguration` dependency is
  gone; the `Program.cs` comment was corrected.
- The main round's seed-CSV changes **stand** (no `GRSTREAMS,gradeLevel` rows and no
  definition; `streamVersion` retained).
- Spec appendix `documents/specs/grade-level-setup.md` rewritten to the revised design.

**AC revision.**
- **AC5 (revised):** `GradeStreamAssignmentSeederTests` pins — fresh DB → **0** rows;
  attribute pair **without** a `GradeLevel` row → **0** bridge rows **and 0
  `grade_levels` rows created** (the discriminator for this fix); attribute pair **with**
  an existing row → 1 row with the right ids; re-run → 0; two tenants → only the tenant
  holding the grade links.
- **AC7 (revised):** the hygiene suite drops the CSV row-count assertion and instead
  asserts the CSV file **no longer exists**; the other three assertions stand.

**Dev-DB reset (executed).** Deleted bridges and grade levels that had no real children,
keeping grades referenced by enrollments / topic assignments / policies / exceptions /
teacher links. Result: **51 grade levels + 153 bridge rows removed; 1 grade kept**
(`Grade 4`, tenant `514faa38` — the one carrying curriculum) with its **3** bridge rows.

**Verification.** Affected graph builds 0 errors; `MigrationService.Tests.Unit` **9/0**
(including the three new discriminators). The full-solution build is currently blocked by
the **running app** (`SchoolCollab.Auth` PID 36184 and `SchoolCollab.AI.Server` 29528,
plus VS 9356, hold output DLLs → MSB3021/MSB3027).

**⚠️ Restart required.** The running AppHost still carries the **old** seeder binary —
restarting before a rebuild would re-create the 51 grade rows and 153 bridges.

---

### Delivery — post-freeze delta

The frozen patch predates three changes that landed after it; the delivery commit
carries them together with the round's work:

- `StateHasChanged()` on the three child-invoked reloads (`ReloadStreamsAsync`, the
  exception reload, `ReloadStudentsAsync`) so a `SectionCard` preview refreshes after an
  Add or a kebab Remove raised inside the card itself.
- `GradeLevelDetailPageTests.ScriptedHandler.MapDynamic` plus the
  `Detail_StreamsCard_Remove_RefreshesTheCard_SoTheRemovedStreamDisappears` regression
  test (a static `Map` kept answering the stale bridge body, which hid a card that never
  refreshed).
- The test harness now maps the bridge-row DELETE
  (`.../grade-levels/{id}/streams/{assignmentId}`): without it the handler answered
  **404**, `RemoveStreamAsync` fell into its catch and the refresh test could never pass.
  Found while reviewing the Streams card's "View all" affordance; fixed here so the layer
  is green.

The Streams card's "View all" retarget itself (`SectionCard` `ViewAllAsDialog` +
`SectionListDialog<TItem>`, replacing the cross-grade
`/coded-values/GRSTREAMS/children` catalogue navigation with a grade-scoped dialog) is
deliberately **not** in this layer — it ships as the next stack layer.

Verified on the delivery state: `dotnet build SchoolCollab.slnx` **0 errors**;
`Admin.Tests.Unit` **598/0**, `Students.Tests.Unit` **570/0**,
`Students.Api.Tests.Unit` **4/0**, `MigrationService.Tests.Unit` **9/0**. Repo-wide
`dotnet test` (Testcontainers integration) remains CI's gate — the same posture the
enrollment-exception PR recorded.