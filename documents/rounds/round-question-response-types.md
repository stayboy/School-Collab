# Round — question response definitions, instructions, teacher response loop (Feature A)

**Mode:** light (Tier 2) — parent plans, one worker run, a static diff reviewer. **Stage:** in
progress on `feat/question-response-types` (branched off `fix/assignment-form-alignment-polish`, so
it inherits D14/D15 locally). Durable spec: `documents/specs/question-response-types.md` (§7
settled) — read it first; this round doc is the execution plan, not the design record.

**Progress (2026-10-09):**

| Step | State |
|---|---|
| A1 Contracts | **done** — `QuestionResponseKindDto`, `InstructionKindDto`, `NewInstructionDto`, `InstructionReadDto`; `NewQuestionDto` + `AssignmentQuestionReadDto` gained `ResponseKinds`/`Instructions`; `InstructionItems` added to `CreateAssignmentRequest`, `UpdateAssignmentRequest`, `AssignmentSummaryDto`, `AssignmentAuthoringChildrenDto`. Contracts builds 0 errors |
| A3 Rules | **done** — `QuestionResponseKindRules` + `QuestionResponseKindValidationException` (every response kind is media ⇒ Teacher Marked only; the D15 file's shape). Contracts builds 0 errors |
| A2 Domain | **done** — enums, `AssignmentInstruction`, aggregate collection/accessor/setter, `AssignmentQuestion.ResponseKinds` (+ persisted as an array property), `AddQuestion(responseKinds:)` + `RemoveQuestion` purging its instruction rows, the ≥1-kind rule in `QuestionOptionDtoValidator`, and `InstructionDtoValidator` for instruction payloads (Text needs text · Url an absolute http(s) URL · media the staged file's name/type/path). Core builds 0 errors |
| A4 Persistence + migration | **generated, presented for the gate** — `assignment_questions.response_kinds` (`integer[]`, NOT NULL, default `{}` — the one additive `ADD COLUMN`, safe on populated tables) and the new `assignment_instructions` table (nullable `question_id`, `kind` default 0, media columns, `display_order`, PK, FK→assignments cascade, indexes on `assignment_id` + `question_id`). File: `Migrations/20261009052346_AddAssignmentInstructionsAndResponseKinds.cs`. **Not applied to any database** — it lands as a file for review; the migration test follows in A7 |
| A5 Handlers | **done** — `InstructionItems` on both commands + routes; both handlers validate instructions and the media rule and map kinds/instructions onto the re-mint; the children read round-trips them; the two data-loss paths (draft **confirm**, **duplicate**) now carry kinds + instruction rows; `QuestionResponseKindValidationException` mapped to **400** on both routes. Core + Application + Api build 0 errors |
| A6 UI | **in progress** — the model half is done and green: `QuestionEditorRow` carries `ResponseKinds` + `Instructions`, `AssignmentEditFormModel` carries the assignment's own `InstructionItems` behind the fail-closed `InstructionItemsLoaded` marker (the `ContextPicksLoaded` posture), both projections + `LoadChildren` round-trip them, and `QuestionsPassSubmitGate` mirrors the scoped kinds rule so a legacy Teacher-Marked question is a one-line prompt instead of a server 400. New `InstructionEditorRow` (one shape, two owners, `ToDto()`). Controls pending: the kind picker, the instruction list component, the compartment block |
| A7 Tests | **in progress** — suite **1029 total · 1029 passed · 0 failed**; new `QuestionResponseKindRulesTests` (7) + the Q1(ii) boundary tests in the create-handler suite (2); the Teacher-Marked draft fixtures now carry kinds. Still to add: the migration test (populated-DB apply, FK cascade, indexes, enum-array round-trip, `RemoveQuestion` purge) and a route-level 400 for the kinds mapping |
| A8 Docs | pending |

**Design deviations from the spec's sketch (recorded, both build-verified):**

- **Instruction ownership sits with the `Assignment` aggregate**, not on `AssignmentQuestion` — one
  table (`instruction_items`), one owned child collection, questions addressed by `QuestionId`. The
  read handlers group by that id, so the wire shape the spec sketched (instructions nested per
  question) is unchanged.
- **The child carries no `TenantId`** (§5.7's sketch listed one): no other assignment child does — the
  tenant is the assignment's, and the table is tenant-scoped through that FK.
- The setter is **tuple-based** (`SetTargets` precedent) so the aggregate never takes a wire DTO.
- **The instruction rows carry no audit columns** (§5.7's sketch listed them): no assignment child
  does — questions, options and attachments are all plain children, and the tenant is the
  assignment's (as recorded above, no child carries its own `TenantId` either).

## 1. Owner decisions this plan executes (2026-10-09)

Authoring-side only · answers are `{Video, Audio, Document, Image}` · response kinds per question,
≥1, mandatory · instructions per question (many, ordered) and on the assignment itself, **one shape**
(`QuestionId` null ⇒ assignment) · five instruction kinds (text · audio · video · url · image) ·
media kinds **forbidden** on Auto Scored / Instant Feedback questions · AI generation unchanged ·
media caps as domain constants · the existing `Instructions` text column stays.

## 2. Owner gates inside this round (hard stops, not defaults)

| Gate | What lands | Why it needs you |
|---|---|---|
| **EF migration** | new `instruction_items` table + the response-kind storage change | migrations are owner-gated in this repo; a migration test ships with it |
| **Contract/wire shape** | `NewQuestionDto`, `AssignmentQuestionReadDto`, the assignment instruction DTOs and the new enum on the wire | wire changes are owner-gated; they also fix what the AI/student surfaces read later |

## 3. Plan-level decision still open (needs one answer)

**Base branch.** D14 + D15 sit committed but **unpushed** on `fix/assignment-form-alignment-polish`,
and Feature A depends on D15 (media kinds vs auto-grading). Options: **(a) recommended — branch
Feature A off the current branch** (it inherits D15 and we deal with stacking when you push);
(b) push + PR the fix branch first, then branch Feature A from `main` (cleanest history, needs your
push instruction); (c) branch from `main` and rebase later (risk of a silent dependency gap).

## 4. Work breakdown

| # | Step | Files |
|---|---|---|
| A1 | **Contracts** — `QuestionResponseKindDto` enum; `NewQuestionDto` + `AssignmentQuestionReadDto` gain `ResponseKinds` + `Instructions`; new `NewInstructionDto` / `InstructionReadDto` (as built — the sketch's `NewQuestionInstructionDto` / `QuestionInstructionDto` names were superseded when the shape became one-model-two-owners); assignment-level `Instructions` items on `CreateAssignmentRequest`, `UpdateAssignmentRequest`, `AssignmentSummaryDto`, `AssignmentAuthoringChildrenDto` | `Contracts/ContractTypes.cs` |
| A2 | **Domain** — `InstructionItem` (shared, `QuestionId` null ⇒ assignment; Kind + Text/Url/media columns + `DisplayOrder`); `ResponseKind` enum; `AssignmentQuestion.ResponseKinds` (+ at least one) and `AssignmentQuestion.Instructions`; `Assignment.Instructions` collection; validation in the aggregate/submit path | `Assignments.Core/Domain/…` |
| A3 | **Rules** — media-kind rule beside D15's: a question carrying a media kind is valid only on Teacher Marked (the `AssignmentTypeGradingRules` precedent, same file family) + its typed exception → 400 | `Contracts/…Rules.cs`, route catches |
| A4 | **Persistence** — `instruction_items` config (tenant + assignment + nullable question FK + indexes) and the response-kind storage (proposed: an `integer[]` column on questions, the `uuid[]`/`ContextStrandIds` precedent — a join table is the alternative) + **migration** | `Assignments.Core/Data/Configurations`, `Migrations/` |
| A5 | **Handlers** — map the new fields through create/update (questions ride the assignment commands as a full replacement) incl. the instruction collections | `Create/UpdateAssignmentCommandHandler` |
| A6 | **Form model + UI** — `QuestionEditorRow`/`AssignmentEditFormModel` gain the fields; `QuestionEditDialog` gains the response-kind picker (chips) + the instruction list (add-per-kind, the D4 `ContextPicksSection` add-button + chip pattern); the authoring page's Instructions compartment gains the same instruction block | `Application/Components/Pages/Assignments/…` |
| A7 | **Tests** — domain validation (≥1 kind, kind payload rules, the media/auto rule); migration test (the `AssignmentContextPicksMigrationTests` precedent); handler/route 400; form-model mapping; bUnit for the picker + instruction add/remove | `tests/SchoolCollab.Assignments.Tests.*` |
| A8 | **Docs** — round doc acceptance + `documents/solution/` notes if any shape needs durable explanation (the D15 doc pattern); configuration docs untouched | `documents/…` |

## 5. Acceptance criteria

1. A question cannot be saved without ≥1 response kind; a media kind on an Auto Scored / Instant
   Feedback assignment is rejected **400** with the rule's message.
2. Instructions round-trip for both owners: text · audio · video · url · image, ordered, and the
   assignment's existing text keeps rendering first.
3. The migration applies on a clean database and on one carrying existing assignments (no backfill
   rows), proven by the migration test.
4. `dotnet build SchoolCollab.slnx` 0 errors; Assignments unit suites green; the existing
   D14/D15 assertions unaffected.
5. UI: the question editor and the Instructions compartment each render the new controls with the
   repo's disabled-with-reason + (i)-hint conventions (no new inline narrative paragraphs).

## 6. Not in this round

The student media-submission surface, the teacher review/response queue (Q1 phase 2), AI generation
changes (Q6), the WYSIWYG editor (Feature B, after this), and folding the assignment text column into
an instruction row.

## 7. Progress log

### A5 — Handlers (done, uncommitted)

| Surface | What landed |
|---|---|
| `CreateAssignmentCommand` / `UpdateAssignmentCommand` | new trailing optional `IReadOnlyList<NewInstructionDto>? InstructionItems` |
| `CreateAssignmentCommandHandler` | `InstructionDtoValidator.ValidateAll(command.InstructionItems, "This assignment")`; `QuestionResponseKindRules.EnsurePermitted(grading format, all question kinds)` beside the D15 rule; `AddQuestion(..., responseKinds:)`; one `SetInstructionItems` carrying the assignment's rows (`QuestionId` null) + each question's rows stamped with the freshly minted id |
| `UpdateAssignmentCommandHandler` | same two validations; kinds + per-question instruction rows on the re-mint; **assignment rows preserved when `InstructionItems` is null** (append via `Assignment.AddInstructionItems`) and replaced when supplied — an unrelated edit can no longer drop them |
| `Assignment` (domain) | `AddInstructionItems` (append-only, per-owner `DisplayOrder`) alongside `SetInstructionItems` |
| `GetAssignmentAuthoringChildrenQueryHandler` | emits `ResponseKinds` + `Instructions` per question and the assignment's own `InstructionItems`, so Edit round-trips instead of blanking |
| `AssignmentRoutes` | both routes pass `req.InstructionItems` |

**Persistence correction (A4 follow-up).** The hand-written `HasConversion<int[]>` on
`AssignmentQuestion.ResponseKinds` **broke the EF model**: it composed with EF's own element
conversion and the model build threw *"Cannot compose converter from `IReadOnlyList<ResponseKind>`
to `int[]` with converter from `IEnumerable<int>` to `string`…"* — which surfaced as ~15 unrelated
domain tests failing at context construction. EF Core's primitive-collection support maps the
`IReadOnlyList<T>` natively and applies the enum element conversion itself; the config now keeps only
`HasColumnType("integer[]")` + `HasDefaultValue(Array.Empty<ResponseKind>())`. **Column shape and the
generated migration are unchanged** — no new migration needed.

**Evidence.** `Assignments.Core` and `Assignments.Application` build with 0 errors. Assignments unit
suite: **1020 total, 1010 passed, 10 failed** — every failure is the new ≥1-kind rule firing on a
fixture that predates it (`Stages_ValidatedQuestions_IntoBlob`, `ClearsBlobOnConfirm`,
`ReplacesAllExistingQuestions`, `Throws_WhenNotDraft`, `HandleAsync_*` ×5), all A7 fixture work.

**Tooling note (cost the round ~40 min).** Running the suite with `/p:BuildProjectReferences=false`
(copied from the F5 Worker-lock workaround) leaves a **stale `SchoolCollab.Assignments.Application.dll`
in the test bin**; it then calls the pre-A1 contract ctors and 42 tests fail with
`MissingMethodException` / bUnit waits while nothing is actually broken. Rebuild the referenced
projects (at minimum Contracts + Core + Application) before trusting a skipped-reference test run.

### A6/A7 — two product decisions surfaced by the rule

1. **AI-generated questions carry no kinds.** The generation staging path builds `NewQuestionDto`
   with an empty set, so the staged draft cannot be saved until the author picks kinds — or the
   generator must stamp a default (every kind is media, so a default is a claim about what the
   student submits).
2. **Legacy questions read back with `[]`.** The column default is the empty array, so an assignment
   authored before this feature fails the ≥1-kind rule on its next unrelated save unless the editor
   supplies a kind (or the rule tolerates rows that never had one).


## 8. Round-2 grill (owner, 2026-10-09) — the mandatory-kind rule's scope

A5's own artifact exposed the conflict: the ≥1-kind rule (§5.1) and the media rule (§5.3) cannot both
hold on Auto Scored / Instant Feedback, and those two formats are the *only* home of AI generation
(`QuestionGenerationGate`, FR-220) — so the generation flow was unreachable, not merely inconvenient.

Asked and answered, **all as recommended** (spec §7.3 records the settlements; §5.1 and §5.3 amended):

| Q | Settled |
|---|---|
| Q1(ii) | A kind is mandatory **exactly where it is permitted** (Teacher Marked); on Auto Scored / Instant Feedback the set is empty by design and nothing is required |
| Q2 | Validation order **stays kinds-first**; fixtures carry kinds deliberately so a rule test cannot pass for the wrong reason |

**What changed as a result (all uncommitted, builds green)**

| File | Change |
|---|---|
| `Contracts/QuestionResponseKindRules.cs` | new `RequiresResponseKinds(format)` — the mandatory rule and `IsPermitted` are one invariant, test-guarded |
| `Core/…/QuestionOptionDtoValidator.cs` | `ValidateQuestions(questions, gradingFormat)`: the ≥1 rule now binds only where kinds are permitted |
| the 4 validator call sites | create · update · stage · confirm all pass the assignment's grading format (each already had it) |
| `Core/…/ConfirmQuestionsDraftCommandHandler.cs` | **data loss fixed** — the re-mint carries `ResponseKinds` + the drafted instruction rows (they were dropped on confirm) |
| `Core/…/DuplicateAssignmentCommandHandler.cs` | **data loss fixed** — the clone carries `ResponseKinds` and every instruction row (assignment- and question-owned) |
| `Api/Endpoints/AssignmentRoutes.cs` | `QuestionResponseKindValidationException` mapped to **400** on both routes (it was unmapped, i.e. 500) |
| tests | new `QuestionResponseKindRulesTests` (7) + the Q1(ii) boundary pair in `CreateAssignmentCommandHandlerQuestionsTests`; the Teacher-Marked draft fixtures gained kinds |

**Evidence:** Assignments unit suite **1029 total · 1029 passed · 0 failed** (was 1020/10 failing before
the settlement). Core, Application, Api build 0 errors.

**Still open (not folded silently into this round):** the legacy Teacher-Marked rows (empty kinds —
editor prompt vs backfill); the AI contract widening (Q6); a route-level 400 test for the kinds
mapping; A6 UI; the migration test.

## 9. Round-3 grill (owner, 2026-10-09) — the UI/upload frontier

Settled **all as recommended**; Round-2's two deferred items came onto the frontier once Q1(ii) was
fixed, and the UI work surfaced two more.

| Q | Settled |
|---|---|
| Q1 | **One shared inline instruction-editing component** for both owners (question + assignment) — the only shape legal inside the question dialog, where the repo rule prescribes inline presentational components with no nested dialog/`<EditForm>` (`dialog-ui` §4) |
| Q2 | **No backfill of legacy kinds.** The editor surfaces "N questions need a response definition" and blocks the save; a backfill would rewrite what *already-published* students are asked to submit, irreversibly (a backfilled kind is indistinguishable from an authored one) |
| Q3 | **Instruction media reuse the existing staging path verbatim** — same `/attachments/stage` endpoint, same `IFileStore`/sweeper/caps. No server change: the returned `StoragePath` rides `NewInstructionDto` exactly as attachments ride theirs |
| Q4 | **One route-level 400 test covering both typed mappings** (kinds + D15), closing a gap that was invisible because neither mapping had a route test |

**Storage answer (Q3, "where is storage source stored?"):** `IFileStore` (`Assignments.Core/Services/IFileStore.cs`) with the
local-FS implementation `LocalFileStore`; the root is `AssignmentFileStoreOptions.RootPath`
(section `Assignments:FileStore`, default `assignment-files`, relative paths resolved against
`AppContext.BaseDirectory`), fanned out by the AppHost parameter `assignment-file-store-root`
(`Program.cs:149`) as `Assignments__FileStore__RootPath` (`Program.cs:346`), committed default
`"assignment-files"` (`appsettings.json:20`). Files land at
`tenants/{tenantId:N}/staging/{guid:N}/{safeName}` and that **relative key is what the entity stores**
as `StoragePath`; the hosted `StagedFileSweepService` reclaims abandoned staged files and refuses to
start without the root. Azure Blob stays the documented later port behind the same opaque-path
contract — instruction media therefore inherit all of it by construction.

**Landed in this increment (build + tests green):** the model half above; `LoadedQuestion` fixture
gains a `kinds` switch; a new bUnit test asserts the legacy prompt
(`Edit_LegacyQuestionWithoutAResponseKind_IsSurfacedByTheGateNotTheServer`). Suite **1030 total ·
1030 passed · 0 failed**; Application builds 0 errors.

## 10. Spec-conformance review (2026-10-09) — unattended, per owner instruction

**Mode: unattended (auto-accept recommendations)** — the owner instructed: *"where possible, use
grill-me to resolve contentions unattended"*. No plan-author recommendation was auto-accepted, and
nothing in this review touched a migration file, the contract shape (the one field mapped below was
already gate-passed onto the wire), security, auth, tenant isolation, or a public route — so nothing
reached the human gates. Owner-ratified pins stay owner-ratified and are marked as such.

### Acceptance-criteria audit (round §5 vs as-built)

| # | Criterion | State |
|---|---|---|
| 1 | ≥1 kind where permitted; a media kind on Auto/Instant rejected **400** with the rule's message | **met server-side** — rule + both route mappings + `QuestionResponseKindRulesTests` (incl. the `RequiresResponseKinds ⟺ IsPermitted` invariant). Route-level 400 test still pending |
| 2 | Instructions round-trip for both owners, all five kinds, ordered, assignment text first | **met in domain + handlers + form model** (create / update / draft-confirm / duplicate all carry them; `DisplayOrder` re-indexed per owner on every write; `InstructionsFor` orders by it). Handler-level round-trip **tests pending**; UI controls pending |
| 3 | Migration applies on clean + populated DB, proven by the migration test | migration generated + owner gate-passed; **the migration test is the largest outstanding gap** |
| 4 | `dotnet build SchoolCollab.slnx` 0 errors; Assignments unit suites green; D14/D15 assertions unaffected | **met** — 0 errors, suite **1030/1030**, D15 suite untouched |
| 5 | UI renders the new controls with disabled-with-reason + (i)-hint conventions | **pending (A6)** |

### Contentions resolved unattended (grill; recommendations auto-accepted)

| # | Contention | Resolution |
|---|---|---|
| U1 | §7.1 Q7 says "media caps as **domain constants** for v1", but round-3 Q3 settled "reuse the attachment staging policy verbatim" — the constants were never written and the two records contradict | **Documentation reconciliation**: Q3 (later, more specific, owner-ratified) supersedes Q7's *mechanism*; the spec's Q7 row now carries the supersession note. Per-kind caps remain the named follow-up. No code change — the reuse is the shipped shape |
| U2 | `AssignmentSummaryDto.InstructionItems` was gate-passed onto the wire but **never mapped** — a dead field on the detail read | **Mapped** in `GetAssignmentByIdQueryHandler` (the assignment's own blocks, ordered). The **list** read deliberately stays unmapped: mapping it would bloat every row of a list view for no consumer. Filling an already-approved field is not a wire-shape change |
| U3 | §5.7's sketch listed audit columns (and a per-row `TenantId`) on the instruction table; the entity carries neither | **Recorded as a deliberate deviation**: no assignment child carries audit columns or its own tenant (questions, options, attachments are all plain children). "Fixing" it would churn a gate-passed migration for zero behavioural gain |

Stale text corrected in passing: the round doc's A1 row still named the sketch's
`NewQuestionInstructionDto`/`QuestionInstructionDto` (as built: `NewInstructionDto`/`InstructionReadDto`),
and spec §5.7 still said `instruction_items` (as built: `assignment_instructions`).

### Not resolved unattended — remaining work

- **A6 UI controls**: the kind picker (disabled-with-reason via `TeacherMarkedOnlyHint`), the shared
  inline instruction component, the Instructions compartment block, the "N questions need a
  response definition" banner.
- **A7 gaps**: the migration test (populated-DB apply, FK cascade, indexes, enum-array round-trip,
  `RemoveQuestion` purge); the route-level 400 test (Q4, settled); handler-level instruction
  round-trip tests (create with both owners' items · update null-preserves/list-replaces · duplicate ·
  draft confirm).
- **A8**: optionally a `documents/solution/` note in the D15 doc's style for the response-kind rules.

**Evidence after this review's one code change (U2):** Core builds 0 errors; Assignments unit suite
**1030 total · 1030 passed · 0 failed**.

## 11. Intent review (2026-10-09) — unattended, per owner instruction ("resume")

The §10 audit checked conformance; this pass checks **intent** — the spec's §1 owner requirements and
§4 workflow against what an author can actually do end-to-end. Mode: unattended (auto-accept
recommendations); no plan-author recommendation auto-accepted; nothing touched a migration, the
contract shape, auth, tenant isolation, or a public route.

| Owner requirement (§1) | Intent-level state |
|---|---|
| 1 · "Question responses must be defined." | **Authoring model + validation done; not yet authorable end-to-end** — the picker (A6) is the missing half. Without it the requirement is unfulfillable from the UI |
| 2 · "any of or a mix of — video, audio, doc, image, at setup" | set semantics ✓ (multi-kind, distinct, `RequiresResponseKinds ⟺ IsPermitted`); the *picker's* multi-select is A6 |
| 3 · "teacher review + response required where teacher marked" | phase 2 by Q1's settled scope — the review/queue state machine is explicitly out of this round |
| 4 · "refine AI generation or leave generated questions for further edit" | satisfied by construction after Q1(ii): generation is offered only on Auto/Instant, where kinds are forbidden, so its kind-less drafts are legal as staged; if the author later switches such an assignment to Teacher Marked, the Q2 gate prompts for definitions — that *is* the "further edit" half |
| 5 · per-question instructions (text/audio/video/url/image) | domain + handlers + wire + form model ✓; the instruction editor control and the media upload wiring (staging reuse) are A6 |
| 6 · assignment-level instructions | same — the compartment block is A6 |

### Contentions resolved unattended this pass

| # | Contention | Resolution |
|---|---|---|
| U4 | Q2's settlement named a banner **with jump links**; the gate already names the first offending question. Jump links per question are the expensive half | **Minimal banner for v1**: a count ("N questions need a response definition") above the save action, no jump links — the gate error already names the first offender and the dialog opens per question. The banner's *substance* (surface + block, no backfill) is the owner-settled part and is intact; jump links flagged as an optional A6 polish |
| U5 | Who owns the staging upload inside the shared inline instruction component — inject the API client, or stay presentational? | **Callback parameter** (`StageMediaAsync` delegate) supplied by the host page/dialog. Keeps the component presentational per `dialog-ui` §4 and bUnit-testable without HTTP; the dialog/page owns the client exactly as the attachments flow does today |

### Defect found by the new tests, fixed and re-verified

`Update_SuppliedInstructionItems_ReplaceTheAssignmentRows_LeaveTheQuestionsAlone` caught a real data-loss
bug: with **questions untouched** and instructions supplied, the handler's full-collection
`SetInstructionItems` silently deleted every **question-owned** row. Fix: new aggregate method
`Assignment.SetAssignmentInstructionItems` (per-owner replace — removes/re-adds only the `QuestionId`
null rows, question rows and their ids untouched), used by that one branch. The other branches were
already correct (full replace when the questions themselves are re-minted; append when only questions
changed; nothing when neither was supplied).

### Tests landed this pass (A7 progress)

- **`InstructionDtoValidatorTests`** (10 cases) — the kind→payload pairing had **zero** coverage.
- **Create handler** (2): both owners' rows persisted, per-owner re-index, question rows stamped with
  the minted id; an invalid block rejects before any child is added.
- **Update handler** (3): null-preserves both owners; a supplied list replaces only the assignment's
  rows (the bug); questions-replaced re-mints question rows and **keeps assignment-row ids**.

**Evidence:** `dotnet build SchoolCollab.slnx` 0 errors; Assignments unit suite
**1045 total · 1045 passed · 0 failed**.

### Remaining to mark every spec task accepted

1. **A6 UI** (the only intent-blocking half): the kind picker (disabled-with-reason +
   `TeacherMarkedOnlyHint` on Auto/Instant), the shared inline instruction component (U5's callback
   shape), the Instructions compartment block, the U4 minimal banner — plus their bUnit tests.
2. **A7**: the migration test (`Tests.Integration`, real Postgres, the `AssignmentContextPicksMigrationTests`
   precedent); the route-level 400 test (Q4).
3. **A8**: optionally the `documents/solution/` note for the response-kind rules.

Everything else on the spec's settled list is implemented and tested.

## 12. Commit / push / stack PR (2026-10-09) — unattended grill, per owner instruction

Owner instruction: *"commit and push to PR (stack pr is applicable)"* plus *"'grill-me' for all open
questions. note this"*. The open questions were therefore resolved with the grill method
**unattended** and recorded here instead of asked ("all as recommended" on the *agent's* own
recommendations; no plan-author recommendation auto-accepted; nothing touched a migration, the
contract shape, auth, tenant isolation or a public route).

| Q | Settled (unattended) |
|---|---|
| Q1 | **Change set = Feature A only.** The unrelated dirty files stayed uncommitted: `AGENTS.md`, `documents/rounds/round-assignment-context-strands-lessons.md`, `documents/rounds/.session-state.2026-09-22.md`, `documents/solution/{blazor-agent-skills-adoption,maui-agent-skills-readiness}.md`, `documents/specs/mobile-hybrid-app.md` — a feature PR must contain the feature, and the commit gate exists to keep foreign work out |
| Q2 | **One commit** for the slice: contracts → domain → persistence → handlers → form model → tests → docs cannot land in a compiling order separately, and the round is one coherent feature |
| Q3 | **Two-layer stack** (`fix/assignment-form-alignment-polish` → `main`, `feat/question-response-types` → base): the layer depends on the base's D14/D15 commits, so it cannot target `main` |
| Q4 | Generated PR titles/bodies replaced with real ones — what each layer contains, its verification, and what is deliberately **not** in it |

**Outcome**

| | |
|---|---|
| Commit | `44c707a1` — `feat(assignments): question response definitions, instructions and the media/auto-scored rule` (34 files, +3542/−51) |
| Stack | **#325** — `main ← fix/assignment-form-alignment-polish ← feat/question-response-types` |
| PR | **#323** base layer → `main` · **#324** feature layer → `fix/assignment-form-alignment-polish` |
| Push | both branches, with `SCHOOLCOLLAB_ALLOW_PUSH=1` for the pre-push guard |

Two operational notes worth keeping: a PowerShell `utf8` write put a BOM on the commit subject, so the
commit was **amended before the push** (the published history is clean); and `gh stack init` rejects
`--remote` in v0.2.0 (it is local-only), while piping gh output through `Select-Object -First` breaks
the pipe (EPIPE → spurious exit 1) — `Out-String` is the reliable read.

Not done, deliberately: no merge (needs its own instruction and green checks), and this push does not
advance A6/A7 — the UI controls, the migration test and the route-level 400 test are still pending.

## 13. A6 — the response-kind picker (2026-10-09, uncommitted)

The first intent-blocking UI control, plus a defect it exposed.

| File | Change |
|---|---|
| `AssignmentQuestionEditorRows.cs` → `QuestionEditDialogTypes.cs` | `QuestionEditModel` carries the assignment's `GradingFormat` (default Teacher Marked, so non-format-aware call sites keep their meaning); `ForEdit`/`ForCreate` take it |
| `QuestionEditDialog.razor(.css)` | the **Response** picker: one `FluentCheckbox` per kind, the set rebuilt in canonical order (Video · Audio · Document · Image) so the payload order is a function of the SET; where the media rule forbids kinds the boxes render **disabled with the reason** (`QuestionResponseKindRules.TeacherMarkedOnlyHint`) — the `ScoringFieldsSection` pattern |
| `QuestionEditorSection.razor` | new `[Parameter, EditorRequired] GradingFormat`, threaded into the dialog model (the scoring section's precedent) |
| `AssignmentAuthoring.razor` | both `<QuestionEditorSection>` sites pass `GradingFormat="@SelectedGradingFormat"` |

### Defect found while writing the tests — fixed

`QuestionEditModel.CopyRow` (the dialog's working copy) copied text/type/options/correct-answer/provenance
but **not** the new `ResponseKinds`/`Instructions` → confirming *any* edit through the dialog would have
silently wiped the question's definition and every instruction block. Fixed (deep copy of both) and
locked by `ResponseKinds_AndInstructions_SurviveAnEditThroughTheDialog`.

### Tests

`ResponseKinds_TeacherMarked_TogglesBindAndThePayloadKeepsCanonicalOrder` (toggling the last then the
first kind yields the canonical order) · `ResponseKinds_AutoScored_RendersDisabledWithTheReason` (all
four disabled + the reason stated) · `ResponseKinds_AndInstructions_SurviveAnEditThroughTheDialog`.
The boxes are driven the repo's way — `FindComponents<FluentCheckbox>().Instance.ValueChanged` (a
FluentCheckbox renders `<fluent-checkbox>`, so `input[type=checkbox]` does not exist in bUnit markup).

**Evidence:** Application builds 0 errors; Assignments unit suite **1048 total · 1048 passed · 0 failed**.

### Still open in A6

The shared inline instruction component (U5's `StageMediaAsync` callback shape) + the assignment's
Instructions compartment block, and the U4 "N questions need a response definition" banner — then A7's
migration test and route-level 400 test.

## 14. A6 — the shared inline instruction editor (2026-10-09, uncommitted)

| File | Change |
|---|---|
| `InstructionEditorList.razor(.css)` (new) | the shared editor: one row per block (kind picker → the payload that kind needs → remove), an "Add instruction" button, an inline failure line. **Presentational** — no API client; media staging arrives as the `StageMediaAsync` seam (U5) |
| `AssignmentQuestionEditorRows.cs` | new `StageInstructionMediaAsync` delegate — the one seam every host implements |
| `QuestionEditDialog.razor(.css)` | the per-question instruction block, using the shared editor (inline — no nested dialog, `dialog-ui` §4) |
| `QuestionEditDialogTypes.cs`, `QuestionEditorSection.razor`, `AssignmentAuthoring.razor` | the staging seam threaded page → section → dialog model, alongside the grading format |

### Two findings it exposed

1. **The attachment allow-list carries no audio/video extensions** (`AttachmentUploadOptions.AllowedExtensions`
   = pdf/doc/docx/ppt/pptx/xls/xlsx/images/text — no `.mp3`, `.mp4`, `.mov`). Reusing the staging
   pipeline "verbatim" (round-3 Q3) therefore cannot upload the two kinds the feature exists for. The
   client pre-check is now **kind-aware** (`AttachmentUploadPolicy.ValidateSizeOnly` for audio/video, so
   those attempts are not blocked client-side) but the **server's `StagedFileValidator` still refuses
   them** — so audio/video instructions do not work end-to-end until the list is widened (or a
   purpose-scoped list is added). **This is a human-gated change** (upload validation): it needs the
   owner's call, and it touches the AppHost parameter default + `documents/configuration.md` §2/§13.
2. **A directly-invoked component method does not re-render.** The bUnit seam calls the component's
   methods directly, so the failure line never appeared until every mutating path called
   `StateHasChanged()` explicitly (a Blazor event handler would have rendered for free). Fixed
   component-wide — it is also what makes the page/dialog re-render when they call these methods.

### Tests (10 new)

`InstructionEditorListBunitTests` (9): empty state · add · remove · kind switch renders the payload that
kind needs · a staged file renders its name + clear action · a successful stage fills the row from the
host's result · the client policy refuses a bad extension **without** calling the host · **audio is not
client-blocked by the attachment allow-list** (the gap above, pinned as behaviour) · a host refusal asks
for a retry · no seam says so. Plus `Instructions_AddedInTheDialog_RoundTripThroughTheWorkingCopy` in
the dialog suite.

**Evidence:** Application builds 0 errors; Assignments unit suite **1059 total · 1059 passed · 0 failed**.

### Still open in A6

The assignment's Instructions **compartment block** (the same component on the page, beside the
existing text field) and the U4 minimal "N questions need a response definition" banner — then A7's
migration test and route-level 400 test, and the allow-list decision above.

## 15. Round-4 grill settlements (2026-10-09) — applied

Owner answered **all as recommended** on the four findings from §14 and the intent review.

| Q | Settled | What landed |
|---|---|---|
| Q1 | **Widen the one allow-list** (not a purpose-scoped second list) | `AttachmentUploadOptions.AllowedExtensions` + `AttachmentUploadPolicy.AllowedExtensions` gain `.mp3 .m4a .wav .ogg .aac .mp4 .webm .mov .m4v`; the AppHost parameter default (`appsettings.json`) and `documents/configuration.md` rows 134/1206 carry them. The component's kind-aware pre-check is gone — one list for every kind (`ValidateSizeOnly` deleted with it). **Audio/video instruction uploads now work end-to-end** |
| Q2 | **The migration test follows the real-Postgres precedent** in `Tests.Integration` | `AssignmentInstructionsResponseKindsMigrationTests` — pre-migration schema asserted ABSENT first, then the column (`integer[]`, NOT NULL, `ARRAY[]::integer[]` default), the table + every column, the nullable `question_id` owner discriminator, both indexes, the assignment FK's CASCADE, the seeded pre-migration row surviving, the enum-array round-trip, the cascade through a real row, and an additive-only script assertion. **Runs via Testcontainers — 2/2 green locally (8s)** |
| Q3 | **Codify both lessons** | `blazor-components.md` § Component parameters gains the "a directly-invoked method does not re-render — end mutating methods with `StateHasChanged()`" rule; `testing.md` gains rule 5 on driving Fluent web components (`FindComponents<T>().Instance.ValueChanged`, assert the tag/parameters, not an inner `<input>`) |
| Q4 | **A durable solution note** | `documents/solution/question-response-definitions.md` — the requirement, the two rules (and why the mandatory-kind rule is scoped to where kinds are permitted), the model, the full-replacement write paths, the upload/allow-list story, and the file map |

**Evidence:** migration test **2/2** on real Postgres; Assignments unit suite re-run green after the
allow-list change; the `AttachmentUploadPolicy` client mirror and the server `StagedFileValidator` read
the same list, so the two cannot drift without a test noticing.

### Still open (the whole remainder)

1. **A6**: the Instructions compartment block on the authoring page + the U4 banner.
2. **A7**: the route-level 400 test for the typed rule rejections (kinds + D15).
3. The round's work is **uncommitted** — PR #324 currently carries the pre-A6 layer only.

