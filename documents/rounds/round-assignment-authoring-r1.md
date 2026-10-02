# Round — assignment-authoring-r1

Provider: pi/ollama-cloud · Tier 2 (Light) · **owner override** for migration + UI file gates (design fully settled in the spec; SKILL.md § "no defined tier between 2 and 3") · Option A
Models: worker `ollama-cloud/deepseek-v4.1-flash` · reviewer `ollama-cloud/kimi-k2.7-code`
Base: `f4c60d96f2cb83eacb8ace4e09180df33d4023d1`
Pre-existing tree changes (NOT part of this round): `documents/specs/assignment-request-feature-spec.md` (M), `documents/specs/assignment-authoring-compartments.md` (untracked)
Round executor: scheduled unattended workflow (`schedule.create`, 2026-10-01 18:00 UTC). Parent authoritative build/test + acceptance are transcribed when the owner returns.

## Plan

### Goal
Implement **R1** of `documents/specs/assignment-authoring-compartments.md`: replace the create wizard and the divergent edit form with **one compartmentalized single-page authoring component** (Create / Edit / View modes), add the `Instructions` field end-to-end, and bring Edit to parity with Create for the question / resource / module editors. Targeting stays on today's single-select `TargetAudienceType` (the `AssignmentTarget` model is R2).

### Spec authority
`documents/specs/assignment-authoring-compartments.md` §3–§6, §9, §11, §12, §14 (R1 row). This plan is the dispatch authority; open the spec only to resolve ambiguity.

### In scope
1. `Instructions` field end-to-end: domain → EF config → migration → contracts → commands/queries/projections → form model → ward view.
2. One shared `AssignmentAuthoring` component serving Create / Edit / View.
3. Six compartments (Basics · Audience & Targets · Delivery & Publishing · Submission & Sign-off · Content & Resources · Questions & AI) as stacked section cards, plus sticky jump-nav and sticky action bar with status-driven primary action + kebab overflow + publish/schedule confirm dialogs.
4. Edit parity: reuse the existing question editor, AI generation, resources and scoring sections in Edit mode (Draft/Scheduled only).
5. Policy-derived fields rendered read-only with "Inherited from Grade/Tenant policy" badges (UX-13).
6. Conditional fields disabled-with-reason, never hidden (UX-17/18/19).

### Out of scope (later rounds — do NOT implement)
- `AssignmentTarget` multi-constraint targeting, `AllStudents` short-circuit, stream/individual pickers, recipient preview (R2).
- Attachment-grounded AI generation and its extraction path (R3). The kebab may host a "Generate questions" action wired to the **existing URL-grounded** generator only.
- Per-assignment policy overrides (D4), autosave (UX-7), dropping `TargetAudienceType`/`GradeLevelId` (TGT-15).

### Ordered passes — keep the build green after each pass
**Pass 1 — Instructions domain + persistence**
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/Assignment.cs`: add `public string? Instructions { get; private set; }`; add an `instructions` parameter (default `null`) to `Create(...)` and `Update(...)`; assign trimmed.
- `src/Assignments/SchoolCollab.Assignments.Core/Data/Configurations/AssignmentConfiguration.cs`: map `Instructions` with a max length (4000).
- New **additive nullable** EF migration in `src/Assignments/SchoolCollab.Assignments.Core/Migrations/`.

**Pass 2 — Instructions wire + handlers**
- `src/Assignments/SchoolCollab.Assignments.Contracts/ContractTypes.cs`: append `string? Instructions = null` as the **last** parameter of `AssignmentSummaryDto`, `CreateAssignmentRequest`, `UpdateAssignmentRequest` and `WardAssignmentViewDto` (positional records — appending avoids breaking existing positional construction).
- Thread `Instructions` through `CreateAssignmentCommand(+Handler)` and `UpdateAssignmentCommand(+Handler)`.
- Project `Instructions` in `GetAssignmentByIdQueryHandler`, `ListAssignmentsQueryHandler`, `DuplicateAssignmentCommandHandler`, and `GetWardAssignmentViewQueryHandler`.
- `AssignmentEditFormModel.cs`: add `Instructions`, wire `LoadFrom`, `ToCreateRequest`, `ToUpdateRequest`.

**Pass 3 — Shared compartmentalized component**
- New `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentAuthoring.razor` + `.razor.css`, with `Mode`/`Id` parameters and an internal mode enum.
- Six compartment cards + sticky jump-nav + sticky action bar (kebab overflow) ; reuse `PublishDialog` / `ScheduleDialog`.
- `Create.razor` and `Edit.razor` become thin hosts of the shared component; `Detail.razor`'s Overview renders View mode.
- Reuse `QuestionGenerationSection`, `QuestionEditorSection`, `ResourcesSection`, `ScoringFieldsSection`, `QuestionsDraftSection`.
- Render `Instructions` in `src/SchoolCollab.Families/Components/Pages/Ward/Assignment.razor`.

**Pass 4 — Tests**
- Unit: `Instructions` round-trips create/update and projects to both DTOs.
- bUnit: six compartments in all three modes; Edit parity; action matrix; disabled-with-reason gating; policy badges.

### Acceptance criteria (discriminating — each must fail against pre-change code)
1. `Instructions` persists on create/update and reads back on `AssignmentSummaryDto` and `WardAssignmentViewDto`.
2. Migration is additive/nullable; `NoUncommittedModelChanges` green.
3. The shared component renders all six compartments in Create, Edit and View (bUnit).
4. Edit mode surfaces the question/resource/module editors while `Draft|Scheduled`; `Published|Closed|Archived` render View (bUnit).
5. Action-bar primary action matches the §11 status matrix (bUnit).
6. Changing type/grading **disables** (does not hide) inapplicable fields with an inline reason (bUnit).
7. Policy-derived fields render read-only with an inherited badge (bUnit).
8. `dotnet build SchoolCollab.slnx` = 0 errors; affected tests + `SchoolCollab.ArchitectureTests.Unit` = 0 failures.

### Build / test commands
- `dotnet build SchoolCollab.slnx`
- One pipeline per project: `dotnet test tests/<Project> 2>&1 | grep -E "^\s*failed |total:|failed:" | head -40`
- Always include `SchoolCollab.ArchitectureTests.Unit`.

### Guards
- Repo-scoped searches only (`src/`, `tests/`, `~/.nuget/packages`); **never** `find /`.
- **No** commit, push, PR, or edits to this round doc.
- Keep the tree compiling: if the scope cannot be completed, stop at a compiling, test-passing state and report exactly what remains.
- After the final green state, write the patch once: `git diff > documents/rounds/diffs-assignment-authoring-r1.patch`.

## Worker Report

Round executed as the scheduled unattended workflow (schedule `58154095`, fire 2026-10-01 18:00Z). Worker `ollama-cloud/deepseek-v4.1-flash`.

- Implemented, in order: (1) `Instructions` end-to-end — domain property + trim, EF `HasMaxLength(4000)`, additive nullable migration `20261001180607_AddAssignmentInstructions`, ModelSnapshot updated; (2) contracts/commands/projections/form-model threading; (3) shared `AssignmentAuthoring.razor` (+ `.css`, `AssignmentAuthoringMode.cs`) with six compartments, sticky jump-nav, §11 action bar + kebab, disabled-with-reason gating, policy badges; (4) `Create`/`Edit`/`Detail` rewired to the shared component; (5) tests.
- Worker-reported: `dotnet build SchoolCollab.slnx` → 0 errors; full suite 3,108 passed / 0 failed. Patch written once to `documents/rounds/diffs-assignment-authoring-r1.patch` (41 diffs, includes the new untracked files).
- Worker-declared deviations: **D-a** public `POST /assignments/{id}/archive` + client (reverses WS-A2 decision (f)); **D-b** `ScoringFieldsSection` disabled-with-reason; **D-c** `Detail.razor` Overview hosts the shared View mode.
- Blocker history: initial `dotnet build`/`test` blocked by MSB3021/MSB3027 locks from the owner's live AppHost stack (10 processes); the worker raised a supervisor decision and did **not** terminate anything; the stack then exited on its own, locks cleared, and the build/test ran in the repo. Second blocker: the wizard-coupled `AssignmentCreateBunitTests` reflection suite — resolved as **Q2 = (a)** (retire the wizard suite; port the four behavioural assertions onto the shared component).

### Rework pass R1-5 — worker report (owner decisions A-(b) + B)

Run `304ca81f-d6e9-4470-a531-e5a3f1b10942`, agent `worker`, model `ollama-cloud/deepseek-v4.1-flash`.

- **P1 closure 1 — destructive child replacement: CLOSED.** The load half was delivered without retreating the editors: `GetAssignmentAuthoringChildrenQuery` + handler, read DTOs (`AssignmentQuestionReadDto`, `AssignmentAttachmentReadDto`, `AssignmentAuthoringChildrenDto`, reusing `ResourceDto`), route `GET /assignments/{id:guid}/authoring` (`AssignmentRoutes.cs:84`, 404/200, no added auth), client `GetAuthoringChildrenAsync`, `AssignmentEditFormModel.LoadChildren` (+ `PreservedResources` so File/Video resources survive full replacement), Edit-mode load **before** the editors render, and **fail-closed** disabled-with-reason rendering when the read fails/404s. Adding one question/URL in Edit now sends loaded + new.
- **P1 closure 2 — dead group-picker binding: CLOSED.** `@bind-SelectedValues` (nonexistent on FluentUI 4.14.2 `FluentListbox<T>`) replaced with the supported pair on a raw `FluentSelect TOption="PickerOption" Multiple` (`SelectedOptions` + `SelectedOptionsChanged`), chosen per `.github/skills/dropdown-ui/SKILL.md` (which routes multi-select to a raw `FluentSelect`) with the in-repo `TeacherEditDialog` multi-grade precedent; a computed `SelectedGroupOptions` projection keeps the ids list as the single source of truth. Load + change-gated persist + fail-closed read retained (`PUT /assignments/{id}/groups` only when the loaded set and the selection differ).
- Tests **+19** (Assignments.Tests.Unit 754 → 773): Create selection passes `ValidateForSave`; Edit renders loaded links selected (`aria-selected`); Edit persists only on change; re-asserting the same set issues no link write; no stray `selectedvalues=` attribute; a children-read failure disables both editors with a reason; add-one-question-in-Edit preserves the loaded set. Callbacks driven via `.Instance.*.InvokeAsync(...)` inside `cut.InvokeAsync(...)`.
- Worker-declared deviations: the approved picker repair; the artifact command needed an exclude pathspec (the patch would otherwise embed itself) plus `git reset` after `git add -N .`.

## Review

Reviewer `ollama-cloud/kimi-k2.7-code` (static; never builds). The first run **timed out** at the 30-minute deadline — killed mid-reasoning (`stopReason=error`, `errorMessage=terminated`) after reading the 358 KB patch in nine 800-line chunks at `thinkingLevel: high`, and produced **no verdict**. It was **resumed** from its persisted session (new run `45892235-5dba-4c2b-b69c-fd8a9a8f6a0a`) and completed.

Reviewer verdict: **CLOSED**.

- Correct (with evidence): `Instructions` threaded end-to-end; R1 scope respected (no `AssignmentTarget`, no recipient preview, no attachment extraction, no new flag, no autosave, `TargetAudienceType`/`GradeLevelId` retained); CPM untouched; migration additive nullable + matching snapshot; shared component implements six compartments, jump-nav, action bar, §11 matrix, disabled-with-reason.
- Deviations: **D-a ACCEPTED** (coherent — §11 makes Archive the Closed row's primary action; mirrors the `/close` precedent, `AssignmentRoutes.cs:548-566`, 404/400); **D-b ACCEPTED** (UX-17/D12, constant keeps markup and tests in sync); **D-c ACCEPTED** (UX-10/§5; approver actions stay on Detail per LIF-1).
- **P2-1** Edit group picker is editable but never loaded/persisted (`AssignmentAuthoring.razor:188-195`, `:671-751`, `:1058-1080`); no destructive path (the update handler does not full-replace activity groups); parity limitation, not a blocker.
- **P2-2** Edit surfaces the question/attachment/resource editors but cannot populate them (`AssignmentEditFormModel.cs:109-140`); reviewer concluded "no silent deletion occurs" because empty collections project to `null`.
- **P2-3** `teachers-ward-portal-prefab-plan.md` (+75) is outside the R1 plan's declared scope.
- Best-practices/architecture: CPM, tenancy, EF/migrations, minimal-API grouping, no MediatR, isolated `.razor.css`, `ErrorBoundary`, `CancellationTokenSource`/`IDisposable` all pass; `AssignmentAuthoring.razor` is ~1,300 lines — acceptable for R1, a future refactor candidate.

## Acceptance — **CLOSED** (the earlier BLOCKED state is superseded by rework pass R1-5)

Parent-authoritative pass (tree frozen, no writers):

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** (17 warnings, all pre-existing NU/ASPIRE) |
| `dotnet test tests/SchoolCollab.Assignments.Tests.Unit` | 754 passed / **0 failed** |
| `dotnet test tests/SchoolCollab.Assignments.Api.Tests.Unit` | 94 passed / **0 failed** |
| `dotnet test tests/SchoolCollab.Families.Tests.Unit` | 44 passed / **0 failed** |
| `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` | 73 passed / **0 failed** |
| Artifact | `diffs-assignment-authoring-r1.patch` — 41 diffs, new files included |

**P1 (parent-found; the reviewer missed it) — destructive child replacement from the Edit surface.**
`AssignmentEditFormModel.ToUpdateRequest` forwards `Questions`/`Attachments`/`ContentModules`/`Resources` from the form model, whose own doc-comment states that the update handler's full-replacement semantics treat a **non-null empty** collection as "clear". `UpdateAssignmentCommandHandler.cs:71-140` full-replaces whenever the collection is **non-null**. In Edit mode `LoadFrom` populates **only scalars**, so the child editors render against an empty model and the author adding a single question / URL / module makes the collection non-null — the handler then deletes **all** persisted questions, attachments, modules and resources and re-adds only the new one. The reviewer's P2-2 conclusion ("no silent deletion") holds only if the author touches nothing.

- **Root cause is the R1 plan's acceptance criterion #4** ("Edit mode surfaces the question editor + resources (parity)") — orchestrator defect: it required the *render* half and not the *load* half.
- **No read DTO carries children** (`AssignmentSummaryDto`/`AssignmentSummary` have none) and no authoring child-read route exists, so closing this correctly needs a contract + query addition (or an explicit retreat from Edit parity for R1).

**P1-b (surfaced during the rework; the reviewer had rated the symptom P2-1) — the group picker's two-way binding is dead, and it regresses Create.**
`AssignmentAuthoring.razor:189-197` binds `@bind-SelectedValues` on `FluentListbox<T>` — a parameter that does **not exist** on FluentUI 4.14.2 (the type exposes `SelectedOptions`/`SelectedOptionsChanged`; verified by assembly reflection, `anySelectedValuesProperty=False`). Blazor routed the unknown name into the component's catch-all `AdditionalAttributes`, so the rendered element emits a stray `selectedvalues="System.Collections.Generic.HashSet\`1[System.Guid]"` attribute and clicks never reach `_selectedGroupIds`. Consequences: the picker is decorative in **both** modes; loaded links can never render as selected; the change-gated persist can never fire; and **Create-mode By-Group can no longer pass `ValidateForSave`** ("Select at least one activity group.") — a regression from the pre-R1 wizard, which supported By-Group selection. Owner decision 2026-10-01: **(a) repair the binding** to the supported FluentUI API on the same element, honouring `.github/skills/dropdown-ui/SKILL.md` and `fluentui-component-props`, with tests proving Create selection passes `ValidateForSave`, Edit renders loaded links as selected, Edit persists only on change, and no stray attribute renders. This closes inside R1 as a **second P1**.

**Disposition**
- D-a / D-b / D-c: recorded as **accepted**. D-a supersedes **WS-A2 decision (f)** — the supersession note in `documents/specs/assignment-authoring-compartments.md` is pending this acceptance. The archive route must inherit the teacher-portal authorization treatment when T4 policies land.
- Reviewer **P2-3**: attributed to the **orchestrator**, not the worker — the T1–T7 teacher-portal spec edit is a parallel-track documentation change (see `teachers-ward-portal-prefab-plan.md`), not worker scope creep. Not a finding.
- Reviewer **P2-1** and **P2-2**: superseded by the P1 resolution (both are parity-load gaps on the same missing read path).
- **Owner decision (2026-10-01): A-(b)** — deliver the **load half** of Edit parity via a dedicated authoring child-read (not a retreat from the editors); **B** — D-a accepted, and the **WS-A2 (f) supersession** recorded in `assignment-authoring-compartments.md` (§11 **LIF-3**, plus the new §5 **UX-21** fail-closed rule and the §17 reference note).
- Rework iteration **1 of ≤1** (Tier 2) — run `304ca81f-d6e9-4470-a531-e5a3f1b10942` (agent `worker`, model `ollama-cloud/deepseek-v4.1-flash`) completed; both P1s closed and parent-re-verified below.

### Re-verification (parent, post-rework, tree frozen)

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** (17 warnings, pre-existing) |
| `dotnet test tests/SchoolCollab.Assignments.Tests.Unit` | **773 / 0** (754 → 773, +19) |
| `dotnet test tests/SchoolCollab.Assignments.Api.Tests.Unit` | **94 / 0** |
| `dotnet test tests/SchoolCollab.Families.Tests.Unit` | **44 / 0** |
| `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` | **73 / 0** |
| Artifact | `diffs-assignment-authoring-r1.patch` — **45 diffs**, new files included, **no self-embed** (the filename hits are prose inside the embedded round doc) |

- **P1-1 re-verified:** `GET /assignments/{id}/authoring` exists and is wired end-to-end (route `:84`, query + handler, `GetAuthoringChildrenAsync`, `LoadChildren`), and Edit renders the child editors fail-closed on a read failure.
- **P1-b re-verified:** no `@bind-SelectedValues` remains in the Assignments context; the picker uses `FluentSelect Multiple` + `SelectedOptions`/`SelectedOptionsChanged` with a computed `SelectedGroupOptions`; the bUnit suite pins the absence of the stray attribute in both modes.
- **Verdict: CLOSED — 0 open P1s.** Deviations D-a/D-b/D-c accepted; D-a supersedes WS-A2 (f), recorded in the spec at §11 LIF-3 and §5 UX-21.

**Residuals (P2 / follow-ups, not blockers)**

1. **Cross-cutting dead binding — needs its own round.** Five further `@bind-SelectedValues` sites on `FluentListbox` in the Students admin (`GradeLevels/Create.razor`, `GradeLevels/Edit.razor`, `GradeLevelCreateDialog.razor`, `GradeLevelEditDialog.razor`, `JoinGroupsDialog.razor`); no repo component defines `SelectedValues`, so those multi-selects are dead the same way. Durable note: `documents/solution/fluentui-dead-selectedvalues-binding.md`.
2. An assignment linked to a group **archived** after linking cannot re-write that link (the route 422s on any archived id) — R1b route/handler item.
3. `ReloadAsync` does not re-read children/links (the page navigates away) — R1b.
4. `AssignmentAuthoring.razor` is ~1,300 lines — future refactor candidate.
5. The patch embeds the round doc (`git add -N .`), so the ephemeral working doc appears in the diff — harmless, noted.
