# Round: Assignment context — strands & lessons picked in Basics (R4)
**Status:** OPEN — P1 (the four hanging Edit-mode page tests) **RESOLVED**, patch **re-frozen and re-verified**, and every Tier-3 independent layer landed (2026-10-08): reviewer `CLOSED-with-nit` (revision v1), UI tester **no P1** (v1), and a delta-only second read **confirming v2** (§ Review (v2 delta)). The frozen revision v2 applies cleanly to `c474a500`, builds 0 errors from a clean checkout, and passes the AC-13 gate **3628/3631** (the 3 = known local live-OpenRouter `400`s). OD-4/OD-5/OD-6 owner-ratified in-session 2026-10-08; the migration + its SQL is discharged (§ Pinned decisions). Every gate the round owes is discharged — the only thing left is the owner's commit instruction. See § Acceptance for the verdict, the revision each layer actually saw, and the residuals carried out.
**Tier:** 3 (full — EF migration + assignment contract change + UI)
**Provider:** pi/ollama (`ollama-cloud`)
**Models:** orchestrator `ollama-cloud/glm-5.3-flash` · plan-review `ollama-cloud/glm-5.3` · worker `ollama-cloud/deepseek-v4.1-flash` · reviewer `ollama-cloud/kimi-k2.7-code` · UI tester `ollama-cloud/minimax-m3`
**Round base:** `35380cb346e516592a9099ad2233113a26722798` (branch `main`)
**Patch:** `documents/rounds/diffs-assignment-context-strands-lessons.patch`

## Context for the orchestrator

This round implements spec §6A (`documents/specs/assignment-authoring-content-questions-modern-ui.md`)
— CP-1…CP-11 — on top of the R1–R3 feature already in base `35380cb` (#319). Settled and
**NOT re-opened**: D22–D28 (spec §0), G29–G35 (§13.2), G40–G42 (§13.3), the withdrawn
D8/VM read-only View chrome (G48). Two owner gates apply and are named in the plan:
the **EF migration** and the **contract/wire shape** reach the owner explicitly — they are
never assumed approved, and this round may **never** run unattended (D28).

Relevant spec facts already settled by audit (restated here because the worker reads only
this plan):

- **S11:** `QuestionGenerationRequest.ContextStrands` (`IReadOnlyList<string>?`) already
  exists in `src/AI/SchoolCollab.AI.Abstractions/AssignmentQuestionGenerationTypes.cs`,
  reaches the model, and is **not** stripped by a tenant lock. It is passed `null` at both
  call sites today (`QuestionGenerationSection.razor:367`, `QuestionsDraftSection.razor:398`).
  There is **no** lessons field. **No AI-host change in this round (G40).**
- **S12:** both pick sources are already client-side on the `StudentsApiClient` the page
  injects: `ListTopicStrandsAsync(topicId, parentStrandId?)` and
  `ListTopicLessonsAsync(topicId, strandId?)`. `TopicStrandDto` has
  `IsLesson` / `ParentStrandId`; `TopicLessonDto` has `StrandId`. Lessons are **parented
  `TopicStrand` rows** (strand-lesson unification sl/2, PR #143+); nothing in
  `src/Assignments` references strands or lessons today.
- **R3's composer is already R4-ready:** ` Helpers/QuestionPromptComposer.cs` already
  defines the `Strands: {names}.` / `Lessons: {names}.` optional lines (CP-9/PB-4) and
  parses them (lines 37–40, 243–253); `QuestionPromptNarrativeInputs` already carries
  `StrandNames` / `LessonNames` ( defaulted null). `ComposeSummary` does **not** yet render
  them — CP-7 extends it.
- **Page anatomy (base `35380cb`):** `AssignmentAuthoring.razor` Basics compartment starts
  at line 118 (`#authoring-basics`); the Subject `FormRow` is lines 204–213
  (`authoring-basics-subject`, bound with `@bind-SelectedOption="_selectedSubject"` — no
  explicit callback exists yet, so CP-4 adds one); subject state at lines 601–613, 989,
  1032–1075; `_model.CaptureSaveSnapshot(...)` call at lines 1262–1279;
  `LoadPickersAsync(AssignmentSummaryDto?)` at `AssignmentAuthoring.razor:1597`; children read
  (`GetAuthoringChildrenAsync` → `LoadPersistedSelectionAsync`) runs **only in Edit mode**
  (lines 1496–1500, 1690–1700).
- **Contract anchors (base `35380cb`):** `CreateAssignmentRequest` and
  `UpdateAssignmentRequest` end with `Targets` (ContractTypes.cs:190/232);
  `AssignmentAuthoringChildrenDto` ends with `Targets` (ContractTypes.cs:474–479) and is
  the `/authoring` children read the Edit surface loads. `AssignmentSummaryDto` ends with
  `TargetGradeIds` (line 191).
- **Update-handler semantics anchor:** `UpdateAssignmentCommandHandler.cs` — questions
  (the questions branch begins at `UpdateAssignmentCommandHandler.cs:100`, not ~146), attachments,
  modules and resources all branch on
  `command.X is not null` ("null we preserve … a non-null but empty collection clears").
  **CP-10 pins the picks to exactly this precedent.** (`Targets` is NOT the precedent —
  `Assignment.SetTargets` throws on an empty list.)
- **Fingerprint anchor (CP-5 checklist):** `AssignmentEditFormModel.CaptureSaveSnapshot`
  (AssignmentEditFormModel.cs:778…) mixes every payload field; the guard test is
  `SaveSnapshot_MatchesTheSerializedPayload_OnEveryDirtyRelevantField`
  (AssignmentAuthoringBunitTests.cs:1773).
- **Duplicate anchor:** `DuplicateAssignmentCommandHandler.cs` — children clone blocks at
  lines ~85–133 (`source.Targets.Count > 0 → clone.SetTargets(...)`).

---

## Plan

> **Standalone-worker contract:** everything needed to implement lives in this section.
> The worker is NOT expected to have read the specs; load-bearing spec clauses are restated.
> Read the actual files before editing — line references were taken at round start (base
> `35380cb`, clean tree). The worker MUST read `.github/copilot/rules/dotnet-best-practices.md`
> before any C# change and follow the repo's CPM rules (never a `Version=` on a
> `PackageReference`; solution is `SchoolCollab.slnx`).

### P1. Goal

Give an assignment author **strands & lessons context, picked in Basics compartment 1**
(spec §6A CP-1…CP-11): two optional multi-value pickers beneath the Subject row; the picks
are a **persisted assignment property** (create/update/duplicate round-trip); generation
wiring per D24 — **picked strands ride `QuestionGenerationRequest.ContextStrands`
(structured, lock-proof); picked lesson names ride the composed narrative**
(`Strands:`/`Lessons:` lines), and are therefore dropped for a locked org (D19/G40) while
strands survive. The composed prompt stays the durable record of what steered a generation
(CP-9/D27).

### P2. Scope

**IN:**

- The Basics `Strands & lessons` row: feature-local `ContextPicksSection.razor` +
  `.razor.css` (CP-1), two multi-VALUE pickers (CP-2), the CP-3 empty/absent states, the
  CP-4 subject-change clear-and-reload, CP-7 summary naming, CP-11 dangling handling.
- Persistence: entity columns + `SetContextPicks`, EF migration, create/update/duplicate
  contract + handler wiring, the authoring-children read DTO fields (see Open decision Q2).
- Generation wiring: `ContextStrands` = picked strand display names matched 1:1 to the
  picked ids (CP-6); lesson names into the narrative inputs of **both** surfaces (CP-8);
  `ComposeSummary` renders the picks (CP-7).
- Fingerprint: the picks join `CaptureSaveSnapshot` (CP-5 checklist) and the guard test is
  updated.

**OUT (explicitly do NOT touch):**

- **Anything AI-host** — `src/AI/**`, the AI host's request shape, `PromptOverride` budget,
  the lock's `PromptOverride` stripping (S7). `ContextStrands` already exists and is not
  stripped (S11/G40). No `ContextLessons` field is added to any AI contract.
- Students context source of picks: `src/Students/**` stays untouched — the pickers are
  read-only consumers of the existing `ListTopicStrands/ListTopicLessons` APIs (S12).
- Compartments 2–6 beyond what the wiring strictly needs: no compartment restructuring
  (D2), no `SectionCard` change (D6), no View-mode chrome (D8 withdrawn, G48).
- `AssignmentTargets`, the `/signature-default` route, approval/policy resolution, any
  list/ward/sweep read — the picks are authoring-scoped and feed no policy, scoping or
  projection (unlike the drop-primary-grade round).
- Feature flags — none needed for the picks (nothing here is flag-gated; the flag-gated
  activity-group machinery is untouched).
- Historical migration files and `AssignmentsDbContextModelSnapshot.cs` **older** entries —
  immutable (ef-migrations.md rule 1/3); only the new migration + the regenerated snapshot
  change.

### P3. Expected files

**New files:**

| # | Path | What |
|---|---|---|
| N1 | `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/ContextPicksSection.razor` | The Basics row's pickers (CP-1) — feature-local, no compartment title (X-1: the Basics `h3` is still rendered exactly once, by the page) |
| N2 | `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/ContextPicksSection.razor.css` | Scoped styles, no colour literals, no inline `style=` (X-8) |
| N3 | `src/Assignments/SchoolCollab.Assignments.Core/Migrations/<timestamp>_AddAssignmentContextPicks.cs` + `.Designer.cs` | The migration pair (P5) |
| N4 | `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentContextPicksTests.cs` | Entity + handler semantics tests (P6) |
| N5 | `tests/SchoolCollab.Assignments.Tests.Integration/AssignmentContextPicksMigrationTests.cs` | Postgres round-trip test (P6) — mirror `DropAssignmentGradeLevelColumnMigrationTests` |
| N6 | `tests/SchoolCollab.Assignments.Tests.Unit/ContextPicksSectionBunitTests.cs` | bUnit suite for N1 (P6) |

**Modified files:**

| # | Path | Change |
|---|---|---|
| M1 | `src/Assignments/SchoolCollab.Assignments.Core/Domain/Assignment.cs` | `ContextStrandIds` / `ContextLessonIds` properties + `SetContextPicks` (P4.2) |
| M2 | `src/Assignments/SchoolCollab.Assignments.Core/Data/Configurations/AssignmentConfiguration.cs` | Map the two `uuid[]` columns (P4.2/P5) |
| M3 | `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/CreateAssignmentCommandHandler.cs` | Create wiring (P4.4) |
| M4 | `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/UpdateAssignmentCommand/UpdateAssignmentCommandHandler.cs` | CP-10 branch (P4.4) |
| M5 | `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/DuplicateAssignmentCommand/DuplicateAssignmentCommandHandler.cs` | Clone the picks (P4.4) |
| M6 | `src/Assignments/SchoolCollab.Assignments.Contracts/ContractTypes.cs` | Request/DTO fields (P4.3) |
| M7 | `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Queries/GetAssignmentAuthoringChildren/GetAssignmentAuthoringChildrenQueryHandler.cs` | Map the picks onto the children DTO (P4.5; per Open decision Q2) |
| M8 | `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentEditFormModel.cs` | Pick state + projections + snapshot mix (P4.6) |
| M9 | `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentAuthoring.razor` | The row, load, subject-change hook, section param wiring (P4.7) |
| M10 | `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionGenerationSection.razor` | `ContextStrands` + narrative names (P4.8) |
| M11 | `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionsDraftSection.razor` | Same as M10 (CP-8) |
| M12 | `src/Assignments/SchoolCollab.Assignments.Application/Helpers/QuestionPromptComposer.cs` | `ComposeSummary` renders the picks (CP-7) |
| M13 | `src/Assignments/SchoolCollab.Assignments.Core/Migrations/AssignmentsDbContextModelSnapshot.cs` | Regenerated by `dotnet ef` (P5) |
| M14 | `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentAuthoringBunitTests.cs` | Fingerprint guard test update + row presence (P6) |
| M15 | `tests/SchoolCollab.Assignments.Tests.Unit/CreateAssignmentCommandHandler*Tests.cs` / `UpdateAssignment` variants / `DuplicateAssignmentCommandHandlerTests.cs` / `AssignmentFormModelMappingsTests.cs` | New pick fields on request/construction call sites — extend, do not weaken (P6) |

No changes in `src/Students/**`, `src/AI/**`, `src/SchoolCollab.Portals/**`, list/ward
repositories, publish/schedule/sweep handlers.

### P4. Design

**P4.1 UI — the Basics row (CP-1/CP-2).** In compartment 1, directly beneath the Subject
`FormRow` (after line 213), render a new feature-local component
`<ContextPicksSection>` wrapped in the page's existing `FormRow Label="Strands & lessons"`:

- **Strands picker** — options from `ListTopicStrandsAsync(TopicId)` filtered to
  `IsLesson == false` / `ParentStrandId == null` (an unfiltered fetch would surface lesson
  rows and duplicate the lessons list). **Lessons picker** — options from
  `ListTopicLessonsAsync(TopicId, strandId?)` fetched **per picked strand** (G31's stated
  cost) and unioned; when no strands are picked, all lessons of the subject show. Both
  optional (D25). Multi-VALUE selection semantics per CP-2 are fixed, and the **control vehicle is
  DECIDED**: a raw `FluentSelect` with `Multiple` (the repo's own precedent — `GradeLevelCreateDialog.razor:66`,
  `JoinGroupsDialog.razor:35`, `TeacherEditDialog.razor:145`; per `dropdown-ui/SKILL.md`), with the picked
  values rendered as **dismissible `Chip`s** (`Admin.Shared/Components/Chip.razor`, already consumed at
  `TargetsAndAudienceDialog.razor:47`). *(Parent-decided 2026-10-07: the plan's "Open decision Q3"
  reference was a numbering slip — no such owner fork exists, and this is a lookupable repo fact.)*
- **CP-3 states:** no subject picked → both pickers **disabled with reason** (the
  disabled-with-reason pattern of the subject picker); subject set but the strand list
  resolves empty → muted note "This subject has no strands yet."; strand set but lesson set
  empty → the lessons row renders its own muted none-note; nothing picked →
  "No strand selected — the AI uses the whole subject."
- **`(removed)` (resilient) picks (CP-11):** a pick whose id is not resolved by the
  successfully-loaded strand/lesson lists renders as a `Chip Label="…"` with the
  `(removed)` marker (subtitle or label suffix), is excluded from the narrative, the
  summary and `ContextStrands`, and is filtered out of the save payload (P4.6). The
  filtering is a **payload filter** — the form model keeps the id until the next successful
  save persists the filtered set.

**P4.2 Entity (`Assignment.cs` + `AssignmentConfiguration.cs`).**

- Two new properties on `Assignment`:
  ```csharp
  /// <summary>R4 (CP-5/D23): the picked strand ids — an opaque id set into the Students
  /// context's TopicStrand rows (root strands). No cross-context FK is possible, so
  /// integrity is the client's resolve-or-`(removed)` contract (CP-11).</summary>
  public IReadOnlyList<Guid> ContextStrandIds { get; private set; } = [];
  /// <summary>R4 (CP-5/D23): the picked lesson ids — TopicStrand rows with a parent
  /// (the unified lesson model). Same opaque-id posture as <see cref="ContextStrandIds"/>.</summary>
  public IReadOnlyList<Guid> ContextLessonIds { get; private set; } = [];
  ```
- New method `SetContextPicks(IReadOnlyList<Guid>? strandIds, IReadOnlyList<Guid>? lessonIds)`:
  each argument is independent — `null` preserves that pick kind's current value;
  a non-null list **replaces** it, normalized by
  `Distinct()` in first-occurrence order (a duplicated id never lands twice). Empty is not
  forbidden (unlike `SetTargets`' TGT-13 gate) — an empty replace is exactly CP-10's "clear".
  Bumps `UpdatedAt`.
- **Configuration (`AssignmentConfiguration.cs`):** map both as primitive collections with
  `HasColumnType("uuid[]")` — Npgsql first-class arrays, no owned entity, no converter.
  Add an `HasIndex`? **No** — the picks feed no query path (they ride the single-row
  children read); adding an index would be speculative scaffolding.
- **Schema pin (P1, owner 2026-10-07):** `Nullable` is enabled on this project
  (`Assignments.Core.csproj:4`) and both properties are **non-nullable**, so EF marks them required and
  the tool-generated migration emits **`NOT NULL`** columns. That is the decided schema — it amends
  OD-1's "nullable" for the **column** side only; the **request** fields stay nullable for CP-10's
  null=preserve. Do **not** hand-edit the migration to nullable. The `= []` initializer runs at
  construction only — it does **not** intercept materialization, so a NULL column would **not** read as
  empty (and could fail at first use). "No picks" has exactly one representation: the
  empty array.

**P4.3 Contract shape (`ContractTypes.cs`).** Two optional params appended at the END of
each record — every existing positional call site keeps compiling (the
`TargetGradeIds`-appended precedent of the drop-primary-grade round):

- `CreateAssignmentRequest`: `IReadOnlyList<Guid>? ContextStrandIds = null` and
  `IReadOnlyList<Guid>? ContextLessonIds = null` — **create semantics: non-null list = the
  picks; `null`/empty list = no picks** (a create has no prior state to preserve).
- `UpdateAssignmentRequest`: the same two params — **CP-10 update semantics ride the wire:
  `null` = preserve the persisted set; empty list = clear all picks; non-empty = replace.**
  Each kind is gated independently. Without this, "remove every pick" is inexpressible.
- `AssignmentAuthoringChildrenDto` (per Open decision Q2): append
  `IReadOnlyList<Guid>? ContextStrandIds = null` + `IReadOnlyList<Guid>? ContextLessonIds = null` —
  the picks round-trip onto the Edit surface's children read. `null` on a pre-deploy
  read is the fail-closed "unknown, preserve" state (P4.6).

**P4.4 Handler wiring.**

- **Create`AssignmentCommandHandler`:** after the `SetTargets` block (~lines 121–125):
  `assignment.SetContextPicks(command.ContextStrandIds, command.ContextLessonIds);`
  (a create has no persisted state, so both lists pass straight through).
- **Update`AssignmentCommandHandler`:** after the `SetTargets` block (cited as ~line 122 — the questions
  branch is at :100, so **re-derive both anchors at implementation time**), before
  the questions block:
  ```csharp
  // R4 (CP-10): the picks follow the questions/attachments precedent — null preserves the
  // persisted set, a non-null (even empty) list is a full replacement.
  if (command.ContextStrandIds is not null || command.ContextLessonIds is not null)
      assignment.SetContextPicks(command.ContextStrandIds, command.ContextLessonIds);
  ```
  (SetContextPicks preserves the kind whose argument is `null`.)
- **Duplicate`AssignmentCommandHandler`:** unconditional in the clone block —
  `clone.SetContextPicks(source.ContextStrandIds, source.ContextLessonIds);` (a copy
  carries its context, CP-5 "round-trip through create/update/duplicate").

**P4.5 Authoring-children read.** `GetAssignmentAuthoringChildrenQueryHandler` maps the two
id collections onto `AssignmentAuthoringChildrenDto` alongside `Targets` (the handler
already reads the aggregate with its children; the picks live on the aggregate, not on a
child table). The client's `GetAuthoringChildrenAsync` deserialization is automatic —
`AssignmentsApiClient` itself needs no edit.

**P4.6 Form model (`AssignmentEditFormModel.cs`) + fingerprint.**

- New state on the model: `public List<Guid> ContextStrandIds { get; set; } = []`,
  `public List<Guid> ContextLessonIds { get; set; } = []`, and
  `public bool ContextPicksLoaded { get; set; }` — the fail-closed marker set by the page when
  the children read delivered the persisted picks (mirroring `_childrenLoaded`'s posture).
- `LoadFrom`-path: the page populates the lists + marker from the children DTO; Create
  leaves both empty + marker set with empty lists (a create genuinely starts with none);
  **a children read that fails/cancels leaves `ContextPicksLoaded == false`** — the
  projections then emit `null` for both fields (preserve), exactly the questions'
  fail-closed behaviour: a failed children load must never wipe the persisted picks.
- Projections: `ToCreateRequest` emits the current lists (empty becomes `null` — the
  create wire's "no picks"); `ToUpdateRequest` — via its `ToCreateRequest` base, as
  `Targets` does today — emits the **CP-11-filtered** lists when
  `ContextPicksLoaded`, else `null`s. The filter: each id not resolved by that call's
  available strand/lesson list maps is dropped. The projections take the resolution maps
  the same way `targets` is passed in (page-supplied arguments, the `ToCreateRequest`
  argument convention).
- **Fingerprint (CP-5 checklist):** `CaptureSaveSnapshot` (the completeness rule — every
  payload field is mixed whether or not the editor owns it) mixes the same
  projection-emitted shape: the **filtered** lists plus the `ContextPicksLoaded` flag, and
  per the ContentModules precedent, `null`/empty sentinel values. Mixing the filtered set
  (not the raw ids) is required by the completeness rule — a dangling id that the payload
  filters but the snapshot mixed would make the guard test fail / a save read as dirty.
  Consequence (CP-11's letter): a save that drops dangling ids from the payload keeps the
  form's read state stable — the next baseline captures the post-save filtered state —
  and merely loading a stale id never dirties the form.
- Guard test: `SaveSnapshot_MatchesTheSerializedPayload_OnEveryDirtyRelevantField`
  (AssignmentAuthoringBunitTests.cs:1773) is **updated** to cover the picks — a pick
  change flips the snapshot; deleting the last pick does NOT leave the pair
  snapshot-clean-vs-payload-replace divergent (the empty=clear semantics must make a
  pick-removal dirty).

**P4.7 Page wiring (`AssignmentAuthoring.razor`).**

- The row renders `<FormRow Label="Strands & lessons" ...><ContextPicksSection …/></FormRow>`
  between the Subject row (line 213) and the Status row (line 218).
- New page state: `_topicStrands` / `_topicStrandOptions`, `_topicLessonOptions`,
  `_strandsLoaded`/`_lessonsLoaded` markers (per-fetch isolation, every picker source on
  this page fails open — the `/activity-groups` 404 posture), name maps built from the
  fetched Dtos.
- **Load path:** in `LoadPersistedSelectionAsync` (Edit) and `LoadPickersAsync`
  (subject resolution at load), fetch the strand/lesson lists for the resolved `TopicId`
  and populate the pick-state + name maps; Create fetches nothing until a subject is
  picked (pickers disabled-with-reason until then, CP-3).
- **CP-4 hook:** give the Subject `FluentSelect` an explicit `SelectedOptionChanged`
  handler (`@bind-SelectedOption` becomes the bind + `SelectedOptionChanged` handler pair —
  the pattern the bUnit tests already use): on change, reload the strand/lesson lists for
  the new topic and **clear the form model's picks** (a strand belongs to a subject), then
  mark the form dirty (the fingerprint mix makes this automatic once `Title`-style state
  moved — still assert it explicitly per AC).
- **CP-11 save-path hook:** the page passes the resolution maps (from the loaded lists)
  into `ToCreateRequest`/`ToUpdateRequest` — as project-arguments, not stored state.
- **Section wiring (P4.8):** the page passes the resolved pick NAMES into both sections as
  new `[Parameter] IReadOnlyList<string> ContextStrandNames` / `ContextLessonNames`
  collections (resolved-name lists, `(removed)` entries already excluded), and leaves the
  lock decisions to the sections (they own `PromptLocked`).

**P4.8 Generation wiring (CP-6/CP-7/CP-8/D24/G40).**

- `QuestionGenerationSection.razor` + `QuestionsDraftSection.razor`:
  - new `[Parameter] IReadOnlyList<string> ContextStrandNames = []` and
    `IReadOnlyList<string> ContextLessonNames = []`;
  - the request's `ContextStrands: null` (current lines 367 / 398) becomes
    `ContextStrands: ContextStrandNames.Count > 0 ? ContextStrandNames : null` — the
    picked strand **display names**, matched 1:1 to the picked ids (CP-6). Survives a
    tenant lock (S11/D24).
  - `NarrativeInputs()` (lines ~152 / ~207) feeds `StrandNames: ContextStrandNames` and
    `LessonNames: PromptLocked ? [] : ContextLessonNames` — the D19/G40 half: a locked org
    receives the strands (structured field) but its narrative loses the lesson names,
    because the server nulls `PromptOverride` (S7). Document this asymmetry in the
    parameter's doc comment (D30's recorded cost).
  - **CP-8 (D20):** both surfaces use the SAME composed prompt seam, so the draft panel
    needs no additional wiring beyond its own `NarrativeInputs()` — its context rides the
    same parameters.
- `QuestionPromptComposer.ComposeSummary`: when the inputs carry `StrandNames`/`LessonNames`,
  append ` · Strands: A, B` / ` · Lessons: X, Y` (CP-7's exact example shape) to the inline
  summary. Lock filtering stays at the section (P4.8) so the composer stays deterministic:
  it renders what it is handed — never re-checks the lock.
- **The composed prompt must keep parsing (CP-9/PB-6):** the shipped parser
  (`QuestionPromptComposer.TryParse`, lines 236–253) already accepts the two optional
  lines in strict order after `Grounded on:`, each rendered independently. No parser edit
  expected; the worker verifies with the composer tests (P6) and does not touch the
  regexes unless a gap is proven.

### P4.9 CP-10/CP-11 interplay — the one subtle case (worker guidance)

The payload filter (P4.6) and the null=preserve gate combine as: an Edit save with the
children loaded but the pick lists **untouched** replays the loaded (filtered) set as a
non-empty replace — id-identical to the persisted set (no state change) — so
filtering-out a dangling id is the only difference a payload-only save manifests, which is
CP-11's letter ("excluded … filtered out of the save payload on the next save"). Do not
short-circuit "unchanged ⇒ null" — that would keep a dangling id alive forever and is not
what CP-11 prescribes.

### P5. EF migration plan

- Model side FIRST (M1/M2), then generate (the design-time factory
  `src/Assignments/SchoolCollab.Assignments.Core/Data/DesignTimeAssignmentsDbContextFactory.cs`
  supplies the Npgsql provider — no startup project or connection flag needed,
  ef-migrations.md):
  ```
  dotnet ef migrations add AddAssignmentContextPicks --project src/Assignments/SchoolCollab.Assignments.Core
  ```
  Migrations live in **the Core project's `Migrations` folder**
  (`src/Assignments/SchoolCollab.Assignments.Core/Migrations` — note: root `Migrations/`,
  NOT `Data/Migrations/`, which is the CodedValues context's path in the rule's table).
- **Naming** per ef-migrations.md: `AddAssignmentContextPicks` (a
  `{Verb}{Entity}{Property}` shape).
- **Additive-only by construction:** `Up()` = two `AddColumn`s, both
  `"uuid[]"`-typed, **`NOT NULL`** with a `default` of the **empty array** (`'{}'`) — required by the
  non-nullable CLR property (P4.2's schema pin), and correct for an additive migration because that
  default backfills every existing row in one step. Do **not** store a `null` default: the initializer
  does not intercept materialization, so a NULL column would throw `SqlNullValueException`.
  **No data backfill** —
  every existing assignment simply has no picks (true for all of them: nothing in the base
  tree authors picks). **No new table, no FK** (per Open decision Q1's recommendation).
  `Down()` = the inverse two `DropColumn`s with a header comment that the reversal is
  by-design lossy of nothing beyond the picks themselves (which did not exist before the
  migration).
- **Snapshot:** regenerated by the same command; commit alongside (rule 7). The
  `.Designer.cs` is never hand-edited (rule 2).
- **Guard:** after wiring, `dotnet ef migrations has-pending-model-changes` must report no
  changes — `MigrationGuardTests` (`tests/SchoolCollab.Assignments.Tests.Unit/MigrationGuardTests.cs`
  + the ArchitectureTests copy) enforces the same thing in CI.
- **Owner gate (AGENTS.md / D28):** the migration reaches the owner explicitly before any
  commit. This plan does NOT assume the migration is approved — the worker implements it,
  the reviewer verifies it, and the owner accepts it as the round's explicit decision point.
- The integration round-trip test (N5) must run against a fresh migrated database and
  assert, via `pg_catalog`/`information_schema`, that the `assignments` table now carries
  the two array columns are `NOT NULL` defaulting to the empty array, and that pre-migration rows
  survive with empty picks.

### P6. Test plan — discriminating criteria (why each fails on base `35380cb`), plus labelled consistency gates

Existing suites touch: `SchoolCollab.Assignments.Tests.Unit` (entity/handler/bUnit/
composer/migration-guard), `SchoolCollab.Assignments.Tests.Integration` (Postgres),
`SchoolCollab.Admin.Tests.Unit` (only if the shared `Chip` modes are exercised — the
picks use the existing dismissible mode, so NO `Chip` change is expected this round),
`SchoolCollab.ArchitectureTests.Unit` (migration guard).

| # | Criterion (new/updated test) | Why it FAILS against the pre-fix code |
|---|---|---|
| AC-1 | `dotnet build SchoolCollab.slnx` → 0 errors (MSBuild-lock fallback per P7) | The new surface is self-consistent only after every seam (entity → config → contract → handlers → model → page → sections) moved together. **Consistency gate** — it also passes on base, so it is not a red-green discriminator |
| AC-2 | **Migration round-trip** (N5, Postgres, fresh migrate): `assignments` has `NOT NULL` `uuid[]` `context_strand_ids` + `context_lesson_ids` defaulting to the empty array; rows carry empty picks; no other table/column appears in the migration's ops | The old schema has no such columns |
| AC-3 | **Entity semantics** (N4): `SetContextPicks` — per-kind null preserves, empty clears, non-empty replaces, `Distinct()` normalizes (a dup id lands once), `UpdatedAt` bumps | The method does not exist; the properties are absent |
| AC-4 | **Create round-trip** (N4): `CreateAssignmentCommandHandler` with picks → `assignment.ContextStrandIds/ContextLessonIds` == the payload (ids in payload order, deduped); with null/empty → none | On base, the request has no pick fields and the handler never sets them |
| AC-5 | **Update semantics** (N4): (a) null+null after a picks-bearing save → unchanged (preserve); (b) empty list → cleared (a non-empty set is removed — the ONLY expressible "remove every pick" path); (c) `[a,b]` over persisted `[a]` → replaced (b added); each kind gated independently | On base there is no branch at all; a naive single-`is not null` branch (failing the independence) also fails (c)/(a) |
| AC-6 | **Duplicate** (N4): a picks-bearing source clones with BOTH lists identical | On base the clone carries no picks (the fields do not exist) |
| AC-7 | **Children read** (N4 or the query-level suite): `GetAssignmentAuthoringChildren` maps the persisted picks onto the DTO; on a pick-less assignment the lists are empty (never `null`) | On base the DTO has no fields |
| AC-8 | **Fingerprint** (update of `SaveSnapshot_MatchesTheSerializedPayload_OnEveryDirtyRelevantField` + focused cases): changing a pick dirties the form; clearing picks dirties; a payload-filtered dangling id never dirties (its snapshot mixes the FILTERED set) | On base the picks are absent from BOTH the payload and the snapshot, so the completeness rule is trivially satisfied — the updated test only passes once the pair moved together (CP-5's checklist — a partial implementation is what fails here) |
| AC-9 | **bUnit — the row renders and reacts** (N6 + M14 additions): `FormRow Label="Strands & lessons"` beneath Subject in Create and Edit; no second compartment `h3` (X-1); no subject → both pickers disabled with reason; subject with no strands → "This subject has no strands yet."; picking a strand adds a chip; subject change → picks cleared AND lists reloaded | On base the row does not exist (absence is the discriminator) and the subject selector has no `SelectedOptionChanged` handler |
| AC-10 | **bUnit — generation threading** (extend `QuestionGenerationSectionBunitTests` + `QuestionsDraftSectionBunitTests`): Generate's request carries `ContextStrands` == the picked strand display NAMES (1:1, no `(removed)` names); PromptLocked → `ContextStrands` STILL populated, narrative `Lessons:` line absent (D19/G40's asymmetry); unlocked → both present; CP-7 summary shows ` · Strands: A · Lessons: X`; CP-8 — the draft surface's dialog/compose uses the same names | On base both call sites pass `ContextStrands: null` (lines 367/398) and the sections have no name parameters |
| AC-11 | **Composer/narrative unit** (extend the existing composer tests): `Compose` emits `Strands:` then `Lessons:` independently in order (after the resource line); `ComposeSummary` renders the picks only when non-empty; `TryParse` accepts all four strand/lesson ± resource combinations and rejects anything else | `Compose` already emits the lines in R3 base — THIS test is a regression PIN, not a red-green; the discriminating part is `ComposeSummary` (base renders no picks). Keep AC-11's framing: verify the shipped parser needs no change, `ComposeSummary` is the new green |
| AC-12 | **CP-11 end-to-end** (N4/bUnit): a persisted pick absent from a successfully-loaded list renders the `(removed)` marker, is excluded from `ContextStrands`/narrative/summary, is filtered from the save payload, and the save's snapshot stays stable | No picks exist on base — the case cannot even arise; the updated pair (payload ∥ snapshot) is what fails on a partial implementation |
| AC-13 | **Full runs:** `dotnet test` (no-arg) 0 failures across the solution (same lock fallback as AC-1; the parent adjudicates unrelated pre-existing failures against the base run if any appear); `MigrationGuardTests` green | The updated suites pin every seam above. **Consistency gate** — it also passes on base |
| AC-14 | **`dotnet ef migrations has-pending-model-changes` → none** after P5 | A model change without the migration fails the guard. **Consistency gate** — it reports "none" on base too |

### P7. Verification constraints + exact commands (for the worker)

- **Build first, every change:** `dotnet build SchoolCollab.slnx` (repo root). **Never kill
  the dev stack**; if MSB3021/MSB3027 lock errors appear (the AppHost/API/worker hold the
  DLLs), re-run with
  `-p:BaseOutputPath=C:/Users/skwar/source/repos/School-Collab/artifacts/build-context-picks`
  and RECORD in the Worker Report which shape was green. 0 CS errors is the bar.
- **Migration sequence (exact):**
  ```
  dotnet build SchoolCollab.slnx
  dotnet ef migrations add AddAssignmentContextPicks --project src/Assignments/SchoolCollab.Assignments.Core
  dotnet ef migrations has-pending-model-changes --project src/Assignments/SchoolCollab.Assignments.Core   # must report none
  ```
- **Tests (MTP — per-project `dotnet test <proj>`, then the no-arg full run):**
  ```
  dotnet test tests/SchoolCollab.Assignments.Tests.Unit
  dotnet test tests/SchoolCollab.Admin.Tests.Unit
  dotnet test tests/SchoolCollab.Assignments.Api.Tests.Unit
  dotnet test tests/SchoolCollab.Assignments.Tests.Integration   # needs the Postgres harness per the existing factory
  dotnet test tests/SchoolCollab.ArchitectureTests.Unit
  dotnet test                                                    # full, no-arg — the AC-13 gate
  ```
  Iterate per-project first; the no-arg run is the final gate (a pre-existing/unrelated
  failure is reported to the round, not silently swallowed — the parent adjudicates it
  against the base run, as documented in `round-drop-primary-grade.md` residual 2).
- The round produces exactly ONE `documents/rounds/diffs-assignment-context-strands-lessons.patch`
  (the FULL feature diff from base `35380cb`; the tree is clean at round start).

### P8. Risks

1. **Npgsql `uuid[]` property mapping (the riskiest EF edit — P4.2):** `Guid[]`-typed
   `IReadOnlyList<Guid>` properties map as native primitive collections on EF/Npgsql 9/10,
   but the exact property shape the snapshot accepts (a get-only property with a private
   set assigned a NEW list vs an in-place-mutated one) drives structural-change detection
   and `HasPendingModelChanges()`. If the simple shape hits a mapping/diff edge the worker
   cannot resolve cleanly, the fallback is a `List<Guid>` (mutable list, same column type)
   — NOT a converter, NOT a value comparer (Npgsql handles array equality natively).
   Record which shape was used in the Worker Report.
2. **Children-read fail-closed nuance (P4.6):** a failed/cancelled children read must
   project `null` (preserve) — the same posture as questions. A partial implementation that
   emits an empty list on the failed path would WIPE a picks-bearing assignment on the
   author's next scalar-only save. AC-5(a) + AC-8 pin it; the reviewer should check the
   failed-loading branch explicitly.
3. **`ComposeSummary` churn (AC-11):** the summary string is asserted in bUnit suites; a
   change to its format can break unrelated assertions — extend, never reword existing parts.
4. **`(removed)` vs fetch-failure (P4.1 vs P4.6):** `(removed)` demands that the
   strand/lesson lists LOADED (authoritatively empty-of-that-id); a FAILED list fetch is
   NOT authoritative — the pick stays in the payload and its name renders as the id-less
   muted state, not as `(removed)`. Filtering on a failed load would silently clear valid
   picks on the next save (CP-11's contract applied on the wrong premise). AC-12's test
   must cover ONLY the successfully-loaded case.
5. **Fingerprint completeness (AC-8) is the round's highest-risk test:** the guard test
   enumerates "every dirty-relevant field" mechanically — a pick field missing there
   produces a payload that never reads dirty. This is CP-5's checklist item and the plan
   treats it as a FIRST-CLASS AC, not a side note.
6. **`ContextStrands` naming semantics (CP-6):** the field carries NAMES, not ids, matched
   1:1 to the picked ids. If a strand's name changes in Students between pick and
   generate, the stale name is what rides to the model — acceptable per CP-6's letter
   (the ids are the persistence side); do NOT re-resolve names at generate time beyond the
   already-loaded lists.
7. **Round-fence discipline:** no AI-host change (G40) means NO new field on
   `QuestionGenerationRequest` and NO change to `AssignmentQuestionGenerationService` —
   `ContextStrands` reaching the model is the ONLY generation-surface change, and the
   lesson names exist solely inside `PromptOverride` text (stripped under a lock, S7).

## Pinned decisions (owner, 2026-10-07 — attended grill)

> **Provenance:** these were declared as `Open decisions` by this round's orchestrator run and answered
> by the owner in an attended grill round, recorded by the parent. The orchestrator's original
> proposals follow below, unedited, as the rationale of record — the owner did **not** auto-accept
> them (the author's own recommendations are never self-accepted).

| # | Decision | Owner answer |
|---|---|---|
| **OD-1** | CP-5 storage shape (G41-deferred) | **`uuid[]` columns** — `ContextStrandIds` / `ContextLessonIds`, two nullable arrays on `assignments`; no join table, no new entity |
| **OD-2** | Which read DTO carries the picks | **`AssignmentAuthoringChildrenDto`** (the `/authoring` children read) — **not** `AssignmentSummaryDto`; CP-5's spec wording corrected to match |
| **OD-3** | View-mode display of the picks | **None this round** — Create/Edit only; View-mode picks recorded as a follow-up alongside the withdrawn D8/VM chrome |

> **Still owner-gated, unchanged:** the EF migration itself and the create/update/duplicate wire shape
> reach the owner explicitly before anything is written (`AGENTS.md` hard exclusions).

## Superseded proposals (the orchestrator's original rationale, unedited)

> The proposals below are retained for provenance; the table above is the decision of record.

**1. CP-5 storage shape (the G41-deferred decision): PostgreSQL `uuid[]` columns vs a join
table + entity.**
**Recommended: `uuid[]` columns (`ContextStrandIds` / `ContextLessonIds`) on `assignments`.**
Reasons: (a) the picks are an opaque id set with **no per-row metadata** — no
`DisplayOrder`, no per-row policy, no row-versioned edits (CP-2/D25 make order moot);
(b) a join table's only real advantage — referential integrity — is **impossible here**:
the strands/lessons live in the *Students* context's database/DbContext, and a
cross-context FK is prohibited by the bounded-context rule; both shapes therefore carry
the same dangling-id posture anyway (CP-11 exists regardless); (c) EF/Npgsql maps
`Guid[]` natively, so an additive migration (two nullable columns, no table, no entity,
no tenant stamping, no child full-replacement machinery) is dramatically smaller than a
new `AssignmentContextPick` entity + configuration + set — the join-table shape buys real
machinery for zero integrity. Cost accepted: no server-side per-id validation (the
client-side CP-11 flow is the only defence), and `Contains`-style id membership is not
queryable in SQL (nothing needs it — P4.2).

**2. Which read DTO carries the picks back: `AssignmentSummaryDto` (CP-5's literal first
mention) vs the authoring-children DTO.**
**Recommended: append the fields to `AssignmentAuthoringChildrenDto` (the `/authoring`
children read), NOT to `AssignmentSummaryDto`.** Reasons: the picks are authoring-scoped —
their single consumer is the Edit surface's children read (`LoadPersistedSelectionAsync`,
which runs only in Edit mode); riding the summary would force every list/ward/sweep
projection to either map two correlated array subqueries (Npgsql translation risk in
sweep queries, as the last round documented for `TargetGradeIds`) for **no consumer**, or
carry a `null`-mapped field. CP-5's letter says "AssignmentSummaryDto/read DTO" — the
slash's second reading is the fit. Cost: the detail/View page cannot display picks (the
summary read does not carry them) — which is moot this round, because View shows no picks
at all (see #3).

**3. View mode display of the picks.**
**Recommended: View mode shows NOTHING new (no pickers, no names row) — Create/Edit only,
matching what CP-1's parenthetical calls "blocked by the D8 deferral, so read-only picks
follow that decision" and what G48's non-goal implies for View chrome generally.** View
mode on this page keeps today's one-line-hint behaviour unchanged; no View-mode invariant
is touched this round. Cost: an owner viewing a picks-bearing assignment sees no context
picks — deferring the View display alongside the rest of the withdrawn D8/VM chrome.

---

### Resumed work — 2026-10-08 (parent in the worker seat, OD-4b)

1. **Fixed the parked blocker.** The two pick-source stubs in `ContextPicksSectionBunitTests.cs`
   (`:267-281`) were commented out; uncommenting them (their fields, drivers and URL templates already
   existed and match `StudentsApiClient` exactly) turned the suite from *no output, no tests* to
   **11 tests reported / 0 failed**.
2. **Closed M14.** `AssignmentAuthoringBunitTests.cs` carried no pick assertions, and its guard is a
   hand-written per-field list — so the new payload fields were silently uncovered. Added four
   `AssertSnapshotTracksPayload` cases (not-loaded→loaded-empty = preserve vs clear; not-loaded is
   payload-equal; strand pick; lesson pick). The suite is **79/0**.
3. **P1 — four Edit-mode page tests hang.** Bisected with per-test caps (method: filter one test at a
   time): the 11 section tests and `CreateMode_RendersTheRowBeneathSubject` pass and the host exits
   cleanly (`rc=0`); **`EditMode_RendersTheRowBeneathSubject`, `EditWithPicks_LoadsThemAsChips`,
   `EditWithDanglingPick_…` and `SubjectChange_…` hang with `total: 0`** (the first test never
   completes) — both under `dotnet test` and by running the built DLL directly. Untouched sibling
   suites (`ResourcesSectionBunitTests` 13/0, and `AssignmentAuthoringBunitTests` which also renders
   the page in Edit mode) exit `rc=0`, so the round's own suite is the different one.
   **Hypotheses already EXCLUDED — do not re-hunt:** (a) *missing stubs* — an unmatched MockHttp request
   throws, and the page's pick-source loader catches it (`LoadContextPickSourcesAsync`), so the symptom
   would be a 5 s `WaitForAssertion` failure, not a hang with zero completed tests; (b) *sync-over-async*
   — the whole R4 delta contains no `.Result` / `.Wait()` / `GetAwaiter().GetResult()`; (c)
   *`/questions-draft` 204 vs `[]`* — the real endpoint returns `Results.NoContent()` and
   `AssignmentsApiClient.GetQuestionsDraftAsync` handles 204/404 explicitly, so the R4 harness is
   faithful and the older harness's `[]` is the fiction; (d) *a child render loop* —
   `ContextPicksSection.razor` has no lifecycle hooks at all. **Remaining lead:** the page awaits
   something that never returns on the Edit path only — `LoadAsync` awaits in order `GetByIdAsync` →
   `LoadPickersAsync` → `LoadPersistedSelectionAsync` (which R4 extends with the pick readers) →
   `ResolveEffectivePolicyAsync` → `LoadAiPromptPolicyAsync`, and Create mode (which skips the Edit
   branch) passes. Needs one instrumented run (capture the page's swallowed `ILogger` error or record
   MockHttp's unmatched requests), not more blind bisecting.

**P1 update — 2026-10-08 (after the instrumentation run).**

- The instrumented run **excluded the two HTTP theories outright**: a file-writing `ILogger<Authoring>`
  and a MockHttp `Fallback` that records unrouted requests both produced **nothing** — no logged
  exception and no unrouted request — while the test still hung to the cap. So the Edit path is not
  dying on an HTTP miss and not surfacing an exception through the page's logger.
- A fifth lead was then tested and **excluded**: the only Edit-reachable `await` invisible to that
  instrumentation is the beforeunload JS interop (`OnAfterRenderAsync` → `SyncBeforeUnloadGuardAsync`
  → `JS.InvokeAsync<IJSObjectReference>("import", BeforeUnloadModulePath)`), and the R4 harness never
  called `JSInterop.SetupModule(Authoring.BeforeUnloadModulePath)` the way the passing
  `AssignmentAuthoringBunitTests` harness does. **Added that setup** (kept — it is what the passing
  harness does) and re-ran the five page tests: `CreateMode_…` passes, the four Edit tests **still
  hang** (`total: 1 / succeeded: 1`, killed at the cap). The JS module is not the cause.
- **Instrumentation is removed** (verified 0 hits) and the tree still builds **0 errors**.
- Remaining hole in the evidence, i.e. the next step if this is picked up again: the diagnostic
  captured only `ILogger<Authoring>`. The harness registers `Mock.Of<ILogger<AssignmentsApiClient>>()`
  and `Mock.Of<ILogger<StudentsApiClient>>()`, so a **client-side catch-and-log-and-return-null** would
  be invisible — capture *every* logger category (an open-generic file logger) before concluding that
  no exception occurs.

---

## Outcome — PARKED (2026-10-07)

**Why parked.** Two worker passes failed to complete step 3; per the skill's time-box rule
(probe with `grill-me`, then park) the round stops here rather than spending a third pass.

| Pass | Model | Failure |
|---|---|---|
| 1 (worker) | `ollama-cloud/deepseek-v4.1-flash` | Stalled — 11m49s in one `bash`, 152 turns / 309k tokens, fighting test tooling (wrote its own `Diagnostic_Probe` test + python helpers instead of fixing the harness). Interrupted by the parent. |
| 2 (escalation) | `ollama-cloud/kimi-k2.7-code` | Timed out at the 30-minute cap having produced **no output** beyond its opening line ("I'll start by reading the plan and reconciling the current state."); contributed no file changes. |

**What already exists on disk** (branch `feat/assignment-context-strands-lessons`, base `35380cb3`, 30 R4 paths; build reached **0 errors / 27 warnings** during pass 2):
- New UI: `ContextPicksSection.razor` + `.razor.css`
- Migration: `20261007195749_AddAssignmentContextPicks.cs` + `.Designer.cs` (+ snapshot)
- New tests: `AssignmentContextPicksTests.cs`, `ContextPicksSectionBunitTests.cs`, `AssignmentContextPicksMigrationTests.cs`
- Wiring: `Assignment.cs`, `AssignmentConfiguration.cs`, Create/Update/Duplicate handlers + commands,
  `GetAssignmentAuthoringChildrenQueryHandler`, `ContractTypes.cs`, `AssignmentRoutes.cs`,
  `AssignmentEditFormModel.cs`, `AssignmentAuthoring.razor`, `QuestionGenerationSection.razor`,
  `QuestionsDraftSection.razor`, `QuestionPromptComposer.cs`

**What remains.**
1. **bUnit harness fix (the known blocker).** `ContextPicksSectionBunitTests` fails because the page never
   leaves its loading state: the assertions see `Total render count across all components: 0` and markup that
   is only a `fluent-progress-ring`. The harness does not stub the Students strands/lessons GET routes the
   pickers call. Stub them (strands for the subject, lessons per picked strand) and await the load before
   asserting. **Pitfall:** pass `HttpMethod.Get` explicitly on every `When` matcher — a method-agnostic
   `When(url)` shadows later method-specific matchers. This is a **test-harness defect, not a product defect**.
2. **Remove pass 1's scaffolding:** the author-invented `Diagnostic_Probe` test and any python helper scripts.
3. **Scrutinise `tests/SchoolCollab.ArchitectureTests.Unit/AssignmentGradeSeamArchitectureTests.cs`** — it is
   outside the plan's expected files. If the pass-1 change *weakens* a guard to fit the new code, revert it and
   satisfy the guard in product code instead.
4. **Verification tail (untouched):** freeze `diffs-assignment-context-strands-lessons.patch`, then parent
   `dotnet build SchoolCollab.slnx` + `dotnet test` (affected projects **plus** `SchoolCollab.ArchitectureTests.Unit`)
   ∥ static reviewer; orchestrator-accept; UI tester (trigger fires — `.razor` changed).
5. **Show the migration + its SQL to the owner** before anything is committed.

**Resume.** Use the `resume-interrupted-orchestrator-round` skill. Kill stray `dotnet`/`testhost` processes
first (MSB3021/MSB3027), then reconcile `git status` against the plan's P3 before writing anything —
never assume partial work exists. Consider a **fresh session** for the resume; this one was compacted twice
and its context is the scarcer resource.

**Known environment hazards hit while parked** (do not re-hunt): the `ollama-cloud` lane served one child
that stalled and one that timed out; `cline/*` is dead on billing (`insufficient_credits`, −$0.07);
`ollama-cloud/mistral-large-3:675b` resolves but dies with `Provider finish_reason: error`.

**Side-artifact — separated, not part of R4.** `.pi/skills/orchestrator-worker-reviewer/SKILL.md` was edited
during this round (the time-box/park rule, alongside `documents/rounds/README.md`'s new `PARKED` status). It is
**not** part of R4: it is committed on its own branch `chore/skill-timebox-park` @ `81f7305e` (branched from
`c474a500`), and both files were restored in this tree so R4's diff is R4-only.

---

## Resume note — 2026-10-08 (parent, on-disk reconciliation)

**Resumed by the parent** (not a worker pass). Tree intact: branch `feat/assignment-context-strands-lessons`,
base `35380cb3`, 21 modified + 8 untracked. Local `main` has since moved to `4d3080db` (#320 + #321,
docs/CI only); the re-level is **deliberately held** for the next round, so this tree is still
`c474a500` + the R4 delta, not the current `main` tip.

**Authoritative verification re-run by the parent** (MTP, `--no-build`, each suite filtered):

| Suite | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** (as pass 2 left it) |
| `AssignmentContextPicksTests` (new, N4) | **22 passed / 0 failed** |
| `QuestionPromptComposerTests` (M12) | **28 passed / 0 failed** |
| `AssignmentAuthoringBunitTests` (M14, untouched) | **79 passed / 0 failed** |
| `ContextPicksSectionBunitTests` (new, N6) | **HANGS** — `rc=124`, no output past the host banner |

**The blocker is a HANG, not the failing assertion this doc recorded above.** A whole-project
`dotnet test` on `SchoolCollab.Assignments.Tests.Unit` and a single-suite filtered run both stop
producing output indefinitely; `--list-tests` enumerates 1000 tests fine, so discovery is healthy and
the hang is at **execution**. Root cause located in the harness: the two pick-source GET stubs are
**commented out** at `ContextPicksSectionBunitTests.cs:268` and `:273`
(`/students/topics/*/strands`, `/students/topics/*/lessons*`), so the page's load never resolves and
the `WaitForAssertion` never returns — an unmatched request in this harness does not fail fast, it
spins. **This is the same defect § "What remains" item 1 describes; the doc's "render count: 0"
symptom is that hang seen from inside the renderer.**

**Reconciled against the plan's P3 (the other three "what remains" items):**

1. **Pass-1 scaffolding — already gone.** No `Diagnostic_Probe` anywhere in `*.cs`/`*.razor`; no stray
   `*.py` outside the two Python portals. Item 2 is closed.
2. **`AssignmentGradeSeamArchitectureTests.cs` — a guard REPAIR, not a weakening (item 3 resolved).**
   The edit replaces `designers.Last(p => !p.Contains(DropMigrationName))` with
   `designers[designers.IndexOf(dropDesigner!) - 1]` — i.e. the designer *immediately before* the
   drop in migration order. The old spelling silently assumed the drop was the newest migration; R4
   adds a migration after it, so `Last(!drop)` would have selected the **new** designer and asserted
   `grade_level_id` against the wrong immutable file. The new spelling asserts exactly what the
   failure message already claimed. **No revert; the guard is strictly stronger.** Flagged for the
   static reviewer because the file is outside P3.
3. **M14 (`AssignmentAuthoringBunitTests.cs`) is untouched but green** — its 79 tests pass, and the
   fingerprint mix *does* carry the picks (`AssignmentEditFormModel.cs`: `hash.Add(ContextPicksLoaded)`
   plus the ids, and the wire flag folded into the hash). So the CP-5 checklist item appears satisfied
   **generically** rather than by amending that file; the reviewer must confirm the guard test really
   is reflection/generic over the payload before this is accepted as closed.

**Two further notes for the reviewer, both outside P3 and both vindicated on inspection:**
`AssignmentRoutes.cs` (+8/−2) threads `req.ContextStrandIds` / `ContextLessonIds` into the create and
update commands — required plumbing the plan's file list simply omitted; and the two command records
(`CreateAssignmentCommand.cs`, `UpdateAssignmentCommand.cs`) carry the same new fields.

**Owner gate — one PINNED decision was changed in implementation (see the grill below):** OD-1 pinned
"two **nullable** arrays", the migration and EF mapping ship `uuid[]` **NOT NULL with an empty-array
default**. The rationale is recorded in `Assignment.cs` / `AssignmentConfiguration.cs` (one
representation of "no picks"; a one-shot additive `ADD COLUMN … DEFAULT` backfills existing rows), and
CP-10's null-preserve / empty-clear distinction is carried by the **DTO**, not by column nullability —
but the deviation is the owner's to ratify, not the worker's.

---

## Open decisions (resume, 2026-10-08) — ANSWERED 2026-10-08 (§ Pinned decisions, 2026-10-08)

> Declared by the parent on resume, per the orchestrator skill's `Open decisions` convention: a fork the
> specs and skills do not determine is **declared**, with the author's recommendation and its cost, and
> **never self-accepted**. Answers fold into a `## Pinned decisions` table below once given.

| # | Fork | Options | Recommendation (cost if taken) |
|---|---|---|---|
| **OD-4** | **Execution mode for the residual delta** — the round is Tier 3 by its migration + contract gates, but both worker passes died on the same residual scope and the delta is now test-only and fully localized (uncomment 15 lines of an already-written harness block at `ContextPicksSectionBunitTests.cs:267-281`, plus M14's missing guard assertions in `AssignmentAuthoringBunitTests.cs`) | (a) fresh worker pass on a strong lane; (b) **parent takes the worker seat for the residual test-only delta, Tier 3's independent layers unchanged** (static reviewer on the frozen patch + deterministic UI tester); (c) Solo — parent does everything, no reviewer/tester | **(b)** — cost: deviates from "the worker implements"; benefit: 2 worker passes have already produced zero output on exactly this scope, and (c) is unsafe (migration/contract gates + UI trigger still demand the independent layers) |
| **OD-5** | **Storage shape deviates from pinned OD-1** — OD-1 said two **nullable** arrays; the shipped migration/EF mapping are `uuid[]` **NOT NULL with an empty-array default** (`Assignment.cs`, `AssignmentConfiguration.cs`) | (a) ratify the deviation (amend OD-1); (b) revert to literal nullable columns | **(a)** — cost: OD-1's text is amended; benefit: "no picks" then has exactly one representation (the empty array), which is what CP-10's clear/preserve rule needs, and the one-shot `ADD COLUMN … DEFAULT` still backfills every existing row additively. (b) reintroduces NULL-vs-`{}` ambiguity for no gain |
| **OD-6** | **Wire-shape gate** (`AGENTS.md`: contract/wire shape always reaches the owner) — `CreateAssignmentRequest` / `UpdateAssignmentRequest` gain two `IReadOnlyList<Guid>?` fields (null = preserve, empty = clear, non-empty = replace, per CP-10), and `AssignmentAuthoringChildrenDto` gains two `IReadOnlyList<Guid>` fields (OD-2's pinned DTO) | (a) ratify as built; (b) change the field set/semantics | **(a)** — cost: none; the semantics are CP-10's (already owner-settled) and the DTO is OD-2's (already owner-pinned); this is a confirm-after-implementation gate, not a new fork |

**Resolved by the parent — NOT put to the owner** (grill-me: facts are never the user's question):

- **Patch base.** The skill fixes the patch to the round doc's **round base**, and `git diff` needs the
  tree's actual base. The tree was fast-forwarded to `c474a500` during the park (one commit: #320,
  docs + CI only), so the doc's `Round base: 35380cb3` is **stale for this tree**; the freeze will use
  `c474a500` and this note records the move rather than asking.
- **UI tester.** Deterministic, not optional: the changed-file list contains `.razor` and `.razor.css`, so
  the round is a UI round and gets a tester pass. Not asked.
- **M14.** `AssignmentAuthoringBunitTests.cs` carries no pick assertions and its guard is a hand-written
  per-field list — a genuine residual **gap**, i.e. work inside OD-4's scope, not a fork.
- **`AssignmentGradeSeamArchitectureTests.cs`.** Repairs a guard that would otherwise assert the wrong
  migration file; no revert needed, reviewer sign-off only.
- **Re-level to current `main` (`4d3080db`).** Already settled by the owner: **hold**, re-level at the
  next round.

---

## Pinned decisions (owner, 2026-10-08 — attended)

Raised by the parent on resume in grill format (§ Open decisions) and **accepted by the owner in-session
2026-10-08**. This is an owner pin, never an unattended one (D28) — and it is the row that resolves the
mode contradiction this doc carried before 2026-10-08.

| # | Fork | Pinned answer | Consequence |
|---|---|---|---|
| **OD-4** | Execution mode for the residual delta | **(b)** parent holds the worker seat for the residual; Tier 3's independent layers unchanged (static reviewer on the frozen patch + deterministic UI tester). Not (c) Solo, not (a) a fresh worker pass. | The residual edits' provenance is recorded in § Worker Report — recovered pass, so a reader can tell them from a worker pass. |
| **OD-5** | Storage shape deviates from pinned OD-1 (nullable arrays) | **(a)** ratify the deviation: `uuid[]` **NOT NULL DEFAULT '{}'** (`Assignment.cs`, `AssignmentConfiguration.cs`). **OD-1's text is amended by this row.** | "No picks" has exactly one representation (the empty array); CP-10's null-preserve / empty-clear distinction stays carried by the DTO, not by column nullability. |
| **OD-6** | Wire-shape gate (AGENTS.md: contract/wire shape reaches the owner) | **(a)** ratify as built: `CreateAssignmentRequest` / `UpdateAssignmentRequest` gain two `IReadOnlyList<Guid>?` (null = preserve, empty = clear, non-empty = replace); `AssignmentAuthoringChildrenDto` gains two `IReadOnlyList<Guid>`. | Confirm-after-implementation gate, not a new fork: the semantics are CP-10's and the DTO is OD-2's, both already owner-settled. |

**Owner gate — the migration and its SQL (discharged 2026-10-08).** Shown in full, per the parked-list item.
Generated on the frozen revision with `dotnet ef migrations script 20261006225404_DropAssignmentGradeLevelColumn
20261007195749_AddAssignmentContextPicks`, i.e. exactly this round's DDL:

```sql
START TRANSACTION;
ALTER TABLE assignments ADD context_lesson_ids uuid[] NOT NULL DEFAULT ARRAY[]::uuid[];
ALTER TABLE assignments ADD context_strand_ids uuid[] NOT NULL DEFAULT ARRAY[]::uuid[];
INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20261007195749_AddAssignmentContextPicks', '10.0.8');
COMMIT;
```

And `dotnet ef migrations has-pending-model-changes` reports **"No changes have been made to the model since
the last migration"** — the snapshot matches the model independently of the reviewer's read (sign-off d).
Two additive columns, both backfilled by the `DEFAULT`, no rewrite of existing rows, `Down` drops them.
This is the OD-5 shape the owner ratified.

---

## Worker Report — recovered pass (2026-10-08)

The residual work in this round was produced by three different hands. Recorded here because the round
doc is the only place that provenance lives — a reader must be able to tell a clean pass from a recovered
one, and the acceptance must name the patch revision the reviews actually ran against.

| # | Who | What it did |
|---|---|---|
| 1 | Worker passes 1–2 (2026-10-07) | Product code, migration and tests landed — see § Outcome — PARKED. Neither pass completed. |
| 2 | **Killed child run `9f5a49d3`** — agent `worker`, `ollama-cloud/kimi-k2.7-code:high`, fresh context, started 09:21, last activity 09:34 of a 30-minute budget, 66 turns / 71 tool calls, PID 29392. **Killed by session end — not a timeout, not a transport error — and it never emitted a WORKER REPORT.** | Diagnosed and fixed P1's real cause in product code, then left the tree instrumented and its last edit unverified (below). |
| 3 | Parent in the worker seat (OD-4(b)), 2026-10-08 | Stripped the killed child's instrumentation, closed the last defect, re-froze the patch, ran the authoritative matrix. |

**What the killed child did — recovered from its run dir, not from a report:**

- **It found P1's real cause, and it was a product defect.** The cause this doc recorded ("the harness does
  not stub the pick-source GETs") was a contributor, not the root: `LoadSubjectsForGradeTargetsAsync`
  **replaced** `_subjectOptions` with the grade union, dropping the persisted topic option. A select whose
  `SelectedOption` is absent from `Items` re-raises `SelectedOptionChanged` on every render, so the page
  never settles and bUnit's `WaitForAssertion` spins forever — the "hang with zero completed tests". Its
  fix re-appends the un-loaded options
  (`Concat(_subjectOptions.Where(o => !loadedIds.Contains(o.Value)))`) and documents the mechanism in the
  method's XML doc. **Kept.**
- **Its evidence:** the in-log MTP summaries walk `Zero tests ran` → `total: 15 / failed: 1 / succeeded: 14`,
  i.e. the four previously-hanging Edit-mode tests began executing. Its last edit — replacing
  `subjectPicker.Items!.First(…)` with a constructed `PickerOption` — re-opened the loop and was never
  re-run; that was the single residual failure it was reasoning about when it was killed.
- **What it left behind (removed by the parent):** ~34 lines of `R4DIAG` debug scaffolding in
  `AssignmentAuthoring.razor` — static `DiagParamSet` / `DiagAfterRender` counters, a `DiagLog` helper
  appending to `%TEMP%\r4diag.txt`, and six call sites in `OnParametersSetAsync` / `OnAfterRenderAsync`,
  under the comment `// R4DIAG — render-loop probe, remove before freeze`. Verified gone (0 occurrences).

**Parent's residual delta (OD-4(b)), on top of the killed child's work:**

1. `AssignmentAuthoring.razor` — `OnSubjectChangedAsync` now keeps the chosen option in `_subjectOptions`
   (3 lines + rationale comment): the same defect class as the child's fix, on the one path where a caller
   can still hand the handler an option the picker never offered. This keeps the test's stronger
   out-of-`Items` assertion rather than weakening the harness to dodge the loop.
2. `ContextPicksSectionBunitTests.cs` — the killed child's un-re-run `SubjectChange_…` edit is kept as
   written; it is the assertion that exposed the loop.

**Authoritative verification (parent, 2026-10-08, after both edits above):**

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| `SchoolCollab.Assignments.Tests.Unit` | **995 passed / 0 failed** (was hanging indefinitely) |
| `SchoolCollab.Admin.Tests.Unit` | 632 / 0 |
| `SchoolCollab.Assignments.Api.Tests.Unit` | 160 / 0 |
| `SchoolCollab.ArchitectureTests.Unit` | 102 / 0 |
| `SchoolCollab.Assignments.Tests.Integration` | 22 / 0 |
| **AC-13 gate — no-arg `dotnet test`** | **3628 passed / 3 failed** |

The 3 gate failures are the live OpenRouter tests (`CodedValueAIServiceLiveTests.ChatAsync_WithOpenRouter_*`):
locally they resolve `Parameters:openrouter-api-key` from **user secrets** and call the provider for real,
which returns `400 Bad Request` for the pinned model. `.github/workflows/ci.yml` documents exactly this —
its 2026-10-07 correction notes that CI's `OpenRouter__ApiKey` feeds the *AI host* and is not read by these
tests, so in CI they self-skip as `Inconclusive` (hence that job's `--ignore-exit-code 8`). R4 changes no
AI-host code (G40). **Adjudicated as environmental, not swallowed.**

**Flake adjudication — why earlier gate runs disagreed.** Two earlier no-arg runs on this same tree reported
4 then **6** failures, with *different* tests each time — including two inside R4-modified files
(`AssignmentAuthoringBunitTests`, `QuestionsDraftSectionBunitTests`) — which is exactly why they were not
waved away as "not ours". Cause: 15 leaked MSBuild `nodeReuse` servers, leaked by `timeout … dotnet test`
(which signals only its coreutils wrapper), competing with Docker/WSL/Defender on a 16-core box
(`dotnet build-server shutdown` → same gate, 3 known local failures, **zero** bUnit failures).
Non-determinism was established before any "not ours" claim, per `fix-flaky-bunit-fluentui-after-cascade`.

**Patch.** Re-frozen 2026-10-08 from base `c474a500`: **29 files (7 new)**, `documents/rounds` excluded,
verified content-identical to the working tree (29 identical / 0 diverged). The reviewer's pass runs against
**this** revision, not the 09:19 freeze.

---

## Review

**Provenance:** static reviewer, fresh context, read-only, `ollama-cloud/kimi-k2.7-code`, child run
`38f142e8-d0d2-4d19-9c6a-d226759869db` (workflow `bdf9071b`), 2026-10-08. Reviewed the patch revision
re-frozen from the accumulated tree (the one whose re-freeze is described above). Transcribed verbatim.

> **Verdict:** CLOSED-with-nit — revision `documents/rounds/diffs-assignment-context-strands-lessons.patch`
> (29 files, 7 new, base `c474a500`).

**Requirement coverage (CP-1..CP-11):**
- CP-1: `AssignmentAuthoring.razor:218` renders the new `FormRow` + `ContextPicksSection`.
- CP-2: `ContextPicksSection.razor:37-83` uses raw `FluentSelect Multiple="true"` for strands and lessons.
- CP-3: `ContextPicksSection.razor:120-160` disables-with-reason, empty-source notes, nothing-picked note.
- CP-4: `AssignmentAuthoring.razor:1855` `OnSubjectChangedAsync` clears picks and reloads strand/lesson sources.
- CP-5: `Assignment.cs:135` properties + `SetContextPicks`, `AssignmentConfiguration.cs:93`, migration, `AssignmentEditFormModel.cs:865-920` fingerprint mix, `AssignmentAuthoringBunitTests.cs:1792-1815`.
- CP-6: `QuestionGenerationSection.razor:382` sends `ContextStrandNames` as `ContextStrands`.
- CP-7: `QuestionPromptComposer.cs:124-140` appends `· Strands:` / `· Lessons:` to the summary.
- CP-8: `QuestionsDraftSection.razor:410-416` receives the same names and uses the same narrative seam.
- CP-9: `QuestionPromptComposerTests.cs:160-200` proves `TryParse` round-trips all strand/lesson/resource combos.
- CP-10: `UpdateAssignmentCommandHandler.cs:91-100` per-kind null=preserve / empty=clear / non-empty=replace.
- CP-11: `ContextPicksSection.razor:265-280` `(removed)` chip label; `AssignmentEditFormModel.cs:390-410` payload filter.

**Defects:**
- **P2** | `ContextPicksSection.razor.css:32,40` | CSS uses fallback colour literal `#707070`; plan N2/X-8 says "no colour literals".
- **P2** | `AssignmentAuthoring.razor:2115-2122` | XML doc says the loaded union is "ADDED to the persisted topic option", but the code concatenates the persisted option after the union.
- **P2** | `AssignmentAuthoringBunitTests.cs:1797-1800` | Two of the four new `AssertSnapshotTracksPayload` cases are not fingerprint-discriminating for the pick ids.

**Named sign-offs:**
- **a.** `LoadSubjectsForGradeTargetsAsync` Concat is correct: union ordered by DisplayOrder, then any pre-existing option not in `loadedIds`; no duplicates. The `_subjectOptionsUnion` `ReferenceEquals` cache rebuilds because `_subjectOptions` is reassigned. XML doc claim accurate in effect.
- **b.** `OnSubjectChangedAsync` appends the chosen option to `_subjectOptions` when absent, closing the render loop. `_selectedSubject` then lives in Items.
- **c.** `AssignmentGradeSeamArchitectureTests.cs` change is a repair: it selects the designer immediately before the drop migration, avoiding the wrong-file assertion when later migrations exist. **Not a weakening.**
- **d.** Migration is additive only (`Up`: two `AddColumn`; `Down`: two `DropColumn`). Snapshot matches model (`uuid[]`, `IsRequired`, empty-array default). No historical migration edited. OD-5 ratified.
- **e.** Wire shape is consistent: create collapses empty→null; update preserves null, clears on empty, replaces on non-empty, per-kind independent; duplicate copies source lists; children read returns non-null lists.
- **f.** The four added snapshot cases only **partially** close the CP-5 fingerprint gap: the "context strand picks" and "context lesson picks" cases are payload-driven and would fail if pick ids were dropped; the first two would not. The claim "4 cases close the gap" is unsupported for all four.
- **g.** All three new test files (`AssignmentContextPicksTests.cs`, `AssignmentContextPicksMigrationTests.cs`, `ContextPicksSectionBunitTests.cs`) reference R4 surface and **would fail on base `c474a500`** — discriminating.

**P3 omissions / untested requirements:** P3 omitted `AssignmentRoutes.cs`, `CreateAssignmentCommand.cs`, `UpdateAssignmentCommand.cs`, and the spec file. No plan requirement lacks a test.

### Review (v2 delta)

**Provenance:** second, delta-only reviewer pass, fresh context, read-only, `ollama-cloud/kimi-k2.7-code`,
child run `688eea1b-19d9-4dbc-b0ff-9d82757c67b1`, 2026-10-08. Briefed to review only the three v2 edits **and
to challenge the parent's dismissals** — deliberately, so the adjudication was not self-reviewed. Transcribed.

> 1. The three edits discharge the v1 nits and are non-behavioural as claimed — CSS header/fallbacks/`::deep`
>    block (visual-only) and the `LoadSubjectsForGradeTargetsAsync` XML `<para>` (documentation-only).
> 2. **The delta is exactly those three edits.** The v2 patch's `ContextPicksSection.razor.css` hunk contains
>    only the reworded header, the two aligned fallbacks and the new `::deep` block; the
>    `AssignmentAuthoring.razor` hunk contains only the `<para>` rewording.
> 3. **The `::deep` guard works and is in the right place.** `Chip.razor` renders `.chip-label` inside
>    `<FluentBadge>` as slotted light-DOM content, so Blazor's `::deep` combinator reaches it from the
>    consumer's scoped CSS (`Chip.razor.css` already uses `.chip ::deep .chip-badge` for the same mechanism).
>    Constraining it here cannot regress other call sites: the selector is `.context-picks__chips ::deep .chip`,
>    so it affects only chips inside this component's chip container.
> 4. **Weakest dismissal: the colour-literal nit — the reason holds.** A repo-wide grep returns 150+ matches for
>    `var(--token, #fallback)` in `*.razor.css` (including `Chip.razor.css:45` and
>    `TargetsAndAudienceDialog.razor.css`), so token fallbacks are the established convention, and v2 aligns the
>    fallback to `#6b6b6b` with the other `--neutral-foreground-hint` usages. For M14, the code confirms the
>    correction: only the two pick cases discriminate a pick id; the first two discriminate the
>    `ContextPicksLoaded` preserve-vs-clear states.
> 5. **No new defect introduced by the delta.** The `::deep` rules are narrowly scoped, the colour change is
>    visual-only, and the XML doc change is non-behavioural.

---

## UI Tester

**Provenance:** UI tester, fresh context, read-only (bash probes only, no writes), `ollama-cloud/minimax-m3`,
child run `b8a11616-3262-4e36-9d06-febe3ba91bb3` (workflow `bdf9071b`), 2026-10-08. Transcribed verbatim
(its own two preamble lines dropped).

**Scope —** `ContextPicksSection` + the wiring on `AssignmentAuthoring.razor`,
`QuestionGenerationSection.razor`, `QuestionsDraftSection.razor`. Suite 15/0 pass (incl. the 4 Edit-mode +
subject-change tests the killed child's fix unblocked).

**Findings:**
- **P2 · `AssignmentAuthoring.razor:2068` + `ContextPicksSection.razor:259` (RemoveLesson) — dismiss-lesson chip
  never re-renders the page.** `Changed="OnModelChanged"` is the empty no-op; a lesson-chip × invokes
  `Changed` and nothing else, so the page's `IsDirty`/`_dirtyBaseline` recomputation only happens on the next
  incidental render, and the unsaved-changes `beforeunload` listener + nav guard can miss the dismissal.
  Strand dismissal is fine (it also raises `StrandPicksChanged`).
- **P2 · `AssignmentAuthoring.razor:224` — `<label for="authoring-basics-strands">` points at the strands
  picker; the lessons picker carries `aria-label="Lessons"`, so the row's visible label "Strands & lessons"
  does not describe the second control.**
- **P2 · `ContextPicksSection.razor.css` — chip overflow with long strand/lesson names.** `.context-picks__chips`
  wraps, but the inner `Chip` has no `max-width` / `overflow-wrap` / `min-width: 0`.
- **P3 · `ContextPicksSection.razor:39-40, 66-67` — `PickSourceUnavailableText` is rendered only as a `Title`
  on a `Disabled` `FluentSelect`; the web component may strip it, leaving no visible cue.**
- **P3 · `AssignmentAuthoring.razor:1799-1814` — lesson list order on initial Edit load:** verified wired (ONE
  fetch per picked strand, G31). *Declared not a defect.*
- **P3 · `ContextPicksSection.razor:163` — strand picker option order (alphabetical) differs from pick order
  (chips).** *Declared not a defect.*

**Verified correct (no defect):** CP-3 disabled-with-reason states; CP-4 subject change clears both picks and
re-reads both sources; CP-11 `(removed)` marker cannot leak into the save payload (asserted against the PUT
body) nor into `ContextStrands`/narrative (`ResolvedPickNames` filters via the name maps); the two empty-state
strings are distinct; `.razor.css` is scoped (no `::deep` needed) and uses design tokens; Create mode with no
subject renders both pickers disabled with reason; no user-facing "topic" string.

**Not verifiable without a running app:** focus order between the two adjacent multi-value pickers; dark/HC
contrast; `FluentSelect`'s `Title` on a `Disabled` control in FluentUI 4.14.2; live `beforeunload` behaviour
right after a lesson-chip dismiss; chip overflow at extreme widths in a real browser; whether the GUID-as-string
`SelectedOptions` renders legibly in the `Multiple` dropdown.

---

## Adjudication (parent, 2026-10-08)

Both passes returned **no P1**, so nothing here blocks; each finding is dispositioned rather than waved
through. Four were acted on (revision **v2**), five were dismissed **with the reason recorded**, one was
deferred as a spec change.

| Finding | Disposition | Evidence |
|---|---|---|
| Reviewer P2 — CSS colour literal violated plan N2/X-8 | **Dismissed as a false positive; fallback aligned** to `#6b6b6b` for consistency | `var(--token, #fallback)` is the repo convention — **130 occurrences** across `*.razor.css`, including the shared `Chip.razor.css:45` and `QuestionsDraftSection.razor.css:25`. The plan's "no colour literals" reads as *no bare/hard-coded colour*, not *no token fallback*; the file's own header stated the convention. |
| Reviewer P2 — XML doc wording inversion (`LoadSubjectsForGradeTargetsAsync`) | **Fixed** (reworded to "the loaded union is ordered and set first, and the persisted topic option … is then KEPT after it") | The signature is the killed child's addition; the doc contradicted the `Concat` order. |
| Reviewer P2 / sign-off **f** — the M14 claim | **Documentation corrected**: 2 of the 4 added cases are pick-id-discriminating; the other 2 pin the loaded/empty distinction. **The CP-5 guard is still genuinely payload-driven** (`AssignmentEditFormModel.cs` fingerprint mix). | Reviewer sign-off f + CP-5 pointer `AssignmentEditFormModel.cs:865-920`. |
| UI P2 — lesson-chip dismissal leaves the dirty state stale | **Dismissed** | `IsDirty` is a **live computed property** (`AssignmentAuthoring.razor:1344-1345`), never a cached flag, and the section **awaits** `Changed.InvokeAsync()` — an `EventCallback` whose receiver is the page, so the page re-renders and `OnAfterRenderAsync` → `SyncBeforeUnloadGuardAsync` reads the fresh value. No stale-state path exists; the tester itself listed the browser behaviour as unverifiable. |
| UI P2 — the lessons control's accessible name is only "Lessons" | **Dismissed** | `aria-label` **is** the accessible name, so the controls announce "Strands" and "Lessons" — distinct and unambiguous, not the row's visible label. Both names are contained in the visible "Strands & lessons", satisfying WCAG 2.5.3 (label in name). The suggested "two FormRows" would split one conceptual row. |
| UI P2 — chip overflow with a long name | **Fixed** — scoped `::deep .chip { max-width: 100%; min-width: 0 }` + `::deep .chip-label { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap }` | The shared `Chip` is `inline-flex` with no `max-width` and `.chip-label` has no `min-width`, so a long name cannot shrink. Constraining it in this component's scoped CSS leaves the shared component (and the composer's toggle chips) untouched. |
| UI P3 — unavailable-source text only as a `Title` on a disabled select | **Deferred, not fixed** | An inline "unavailable" note would contradict plan P8-4, which decides no note while the source is unloaded. Recorded as a next-round follow-up (a UX gap, but a spec change, not this round's to make). |
| UI P3 — strand option order vs pick order | **Dismissed** | Options sort alphabetically by name (`AssignmentAuthoring.razor:1749`); chips follow pick order. The tester declared it not a defect; both orders are deliberate. |
| UI P3 — lesson load order on initial Edit load | **No action** | Tester confirmed it is correctly one fetch per picked strand (G31). |

**Revision note.** Reviewer verdict and UI-tester findings both ran against revision **v1**. **v2** is v1 plus the
three review-driven edits above (two CSS hunks + one XML doc comment), all non-behavioural; the frozen patch
was regenerated from the same tree. No independent pass has yet seen v2 — see § Verification.

---

## Verification

| Check | Command shape | Result |
|---|---|---|
| Patch applies to base | `git apply --check` in a clean worktree at `c474a500` | **OK** — no conflicts, 29 paths |
| Build (clean checkout, default layout) | `dotnet build SchoolCollab.slnx` | **0 errors** on the frozen revision |
| Affected unit suites | `dotnet test tests/<proj> --no-build` | Assignments **995/0**, Admin **632/0**, Assignments.Api **160/0**, Architecture **102/0** |
| Integration | `dotnet test tests/SchoolCollab.Assignments.Tests.Integration` | **22/0** |
| **AC-13 gate (frozen revision)** | no-arg `dotnet test`, isolated checkout | **3628 / 3631** — the 3 are the known local live-OpenRouter `400`s (see § Worker Report) |
| Patch fidelity | applied-hunk comparison against the tree | **29/29 identical, 0 diverged** (7 new-file entries) |

The no-arg run also reports `error: 2` at the project level — the two **Playwright** projects
(`Students.Tests.Playwright`, `Settings.Tests.Playwright`) reporting *Zero tests ran / exit code 8* because a
bare `dotnet test` has no Playwright harness for them. Pre-existing and unrelated: CI deliberately never runs
them from the no-arg gate (it filters integration and gives the portals their own jobs).

**Two environment findings that change how this must be verified — both recorded because they invalidate the
obvious shortcuts:**

1. **`-p:BaseOutputPath` is NOT an equivalent build shape, despite plan P7 offering it as the MSB3021
   workaround.** Relocating the test binaries breaks every guard that locates the repo relative to the test
   output directory: at the alternate path `SchoolCollab.ArchitectureTests.Unit` went **8 failed / 102** with
   exactly the source-scanning guards red (`No_MediatR_In_Source`, `No_SemanticKernel_In_Source`,
   `No_ConsoleWriteLine_Outside_ProgramCs`, `DialogShellFooter_NeverReceivesALiteralParameterValue`, …) and
   `SchoolCollab.Admin.Tests.Unit` went **174 failed / 632**; the same suites at the **default** path are
   **102/0** and **632/0**. Use the default path — and when the lock blocks it, stop the stack or verify on an
   isolated checkout, never on a relocated output tree. *(Plan P7's "RECORD which shape was green" — answered:
   only the default shape is green.)*
2. **The dev stack holds the Assignments binaries.** `SchoolCollab.AppHost.exe` (PID 33380, with its DCP
   controllers) plus Visual Studio holding `SchoolCollab.slnx` (PID 14188) lock
   `SchoolCollab.Assignments.Core.dll` (via the Worker) and the Admin host's copy of
   `SchoolCollab.Assignments.Application.dll`. AGENTS.md forbids killing the dev stack, so the default-path
   rebuild of the frozen revision is **pending an owner decision**.

**The v2 verification gap is closed.** The frozen revision was verified on an isolated checkout rather than
the working tree, because the working tree's default-path rebuild is blocked by the running dev stack
(finding 2 below) and the alternate output path is invalid (finding 1). Procedure, reusable:

```
git worktree add --detach C:/Users/skwar/source/repos/School-Collab-r4verify c474a500
git -C <worktree> apply --check <abs>/documents/rounds/diffs-assignment-context-strands-lessons.patch
git -C <worktree> apply         <abs>/documents/rounds/diffs-assignment-context-strands-lessons.patch
dotnet build SchoolCollab.slnx          # in the worktree
dotnet test                             # in the worktree — the AC-13 gate
```

The worktree has its own `bin`/`obj`, so the running AppHost cannot lock it, and it keeps the standard
`tests/<proj>/bin/<cfg>/<tfm>` layout, so the repo-root-walking guards still work. It also proves the frozen
patch applies to base — something no pass had demonstrated, since the reviewer only read the diff.

**What is still true and must not be papered over:** both independent passes ran against revision **v1**.
Nobody independent has read **v2**. The three v2 edits are a CSS token alignment, a `::deep` overflow guard,
and an XML doc comment, and the full gate is green on them — but "reviewed" and "verified" are different
claims, and the acceptance below names the revision each one applies to.

**Environment footnotes (so they are not re-hunted):**

- Non-deterministic bUnit failures in `Assignments.Tests.Unit` recur under load (4 → 6 failures on one tree,
  different tests each time, including inside R4-modified files) while the same project standalone is 995/0 in
  ~13 s. Root cause: leaked MSBuild `nodeReuse` servers (`timeout … dotnet test` signals only its coreutils
  wrapper) plus Docker/WSL/Defender contention on a 16-core box. `dotnet build-server shutdown` clears it.
  A third sighting occurred while a failed mid-build was running concurrently with the suite. The isolated
  checkout run above — no locks, no leaked servers, no concurrent build — had **zero** bUnit failures.
- The `## Verification` section exists here in support of the accepted round-doc lint
  (`RoundDocDecisionsLintTests`, its own branch) — the rule that would have caught this round's earlier
  "Authoritative verification re-run" over-claim.

---

## Acceptance

**Verdict: ACCEPTED for merge — pending the owner's explicit commit instruction.** Nothing has been committed,
pushed or merged; per AGENTS.md the round's code stays in the working tree until the owner says otherwise.

**Delivered** — spec §6A CP-1…CP-11: two optional multi-value strands/lessons pickers in the Basics
compartment beneath Subject (CP-1/CP-2), disabled-with-reason and empty-state copy (CP-3), the CP-4
clear-and-reload subject hook, the picks as a persisted assignment property across create/update/duplicate with
the CP-10 null=preserve / empty=clear / non-empty=replace contract (CP-5/CP-10), generation wiring where
strands ride `QuestionGenerationRequest.ContextStrands` and lessons ride the composed narrative (CP-6/CP-7/CP-8,
CP-9), and the CP-11 `(removed)` dangling-pick handling. Plus one EF migration (two additive `uuid[]` columns)
and a repaired `AssignmentGradeSeamArchitectureTests` guard.

**Which revision each independent layer actually saw** — the distinction this round got wrong on its first
attempt, so it is stated explicitly:

| Layer | Run | Scope seen | Verdict |
|---|---|---|---|
| Static reviewer | `38f142e8` | **full v1** patch | CLOSED-with-nit — 7/7 named sign-offs granted; all new test files discriminating |
| UI tester | `b8a11616` | **full v1** patch + suite | **No P1** (3×P2, 3×P3) |
| Delta reviewer | `688eea1b` | **v2 delta only** (3 hunks) | Discharges v1's nits; delta non-behavioural; no new defect; dismissal reasons upheld on an independent grep |
| Parent (worker seat, OD-4(b)) | — | v2 | Implemented the residual, adjudicated all 9 findings, re-froze, ran the matrix |

No layer has read the **full** v2 patch; v2 is v1 plus 3 non-behavioural hunks, and the full gate is green on
it. That gap is recorded here rather than smoothed over — it is exactly the kind of unstated claim this round
was criticised for.

**Residuals carried out of the round, explicitly:**

1. **Deferred (a spec decision, not a defect):** the pick-source-unavailable text is rendered only as a `Title`
   on a disabled `FluentSelect`, so a user whose fetch failed may see no cue (UI tester P3). An inline note
   would contradict plan P8-4's decision, so it is a next-round change.
2. **The v2 patch was never read in full by an independent layer** (see the table).
3. **Held by owner decision:** re-level to current `main` (`4d3080db`) — take it at the next round.
4. **Separate branch, not part of R4:** the accepted round-doc lint (`chore/round-doc-verification-lint`,
   uncommitted) and the verification-stamp enforcement deferred to its own round.

**Not done, and not to be assumed:** commit, push, PR, merge (each needs its own explicit instruction); and the
main tree's own default-path `dotnet build` / no-arg `dotnet test` pre-flight, which stays red until the running
dev stack (`SchoolCollab.AppHost.exe` PID 33380 + Visual Studio) is stopped — the AC-13 gate above was
satisfied on an isolated checkout instead (see § Verification).