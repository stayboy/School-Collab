# Round: Targets & audience builder — typed pickers + save wiring + orphan cleanup

**Status:** CLOSED — delivered in the working tree (uncommitted; owner has not authorized a commit).
**Provider:** pi (models: `ollama-cloud/deepseek-v4.1-flash` worker, `ollama-cloud/kimi-k2.7-code` reviewer)
**Tier:** 2 (Light round — parent plan/accept, 1 worker, 1 static diff reviewer)
**Round base:** `870b4b0f314f239de400663b8c65a4cc10f8faca` (branch `main`)
**Tree dirty at round start:** YES — the prior session's uncommitted UI work on this same feature. Prior dirty files:
`AssignmentAuthoring.razor` (M), `AssignmentAuthoring.razor.css` (M), `TargetsAndAudienceDialog.razor` (??),
`TargetsAndAudienceDialogModel.cs` (??), `TargetsAndAudienceEntry.cs` (??), `nul` (??).
Because that prior delta is the *same* feature and this round rewrites part of it, the round patch is the **full feature diff from the round base** (prior UI work + this round), not an isolated delta.
**Patch:** `documents/rounds/diffs-targets-audience-typed.patch`

## Plan

### Goal

Finish the "Targets & audience" builder on `AssignmentAuthoring`:

1. The **Add** dialog collects a **typed** value per category (a real picker), not free text.
2. The authored entries become the assignment's **persisted `AssignmentTarget` rows at save**.
3. The now-orphaned **old audience editor** state/handlers are removed.

### Owner-pinned decisions (from the grill — do not re-open)

- Q1 = **typed pickers** per category (entry stores a `Guid` `RefId`).
- Q2 = **drop `Subjects`** — the closed set is the five `TargetKindDto` kinds.
- Q3 = **delete** the orphaned old audience code in this change.
- Q4 = the card keeps the **`SectionCard` native header Add** button; entries stay in the body.
- Q5 = **delete** `nul`.
- No new table / migration; no contract change; no change to the target resolver or publish path.

### Facts the implementer must respect

- `TargetKindDto` (`src/Assignments/SchoolCollab.Assignments.Contracts/ContractTypes.cs:67`) is exactly
  `AllStudents, GradeLevel, Stream, Student, ActivityGroup` — **no `Subject`**.
- `AssignmentEditFormModel` (same folder) holds `_targets` as `AssignmentTargetSpec(TargetKindDto Kind, Guid? RefId)`
  and exposes only: `Targets`, `ToTargetDtos()`, `LoadTargets(IReadOnlyList<AssignmentTargetDto>?)`,
  `SetTargetsOfKind(TargetKindDto, IReadOnlyList<Guid>)`, `SetEveryoneTarget(bool)`, `RemoveTargetAt(int)`,
  `HasAllStudents`, `CountOf`, `TargetsChanged`, `TargetsLoaded`. **Do not add public API to it.**
  - `SetTargetsOfKind(kind, refIds)` replaces every target of that kind; `SetTargetsOfKind(kind, [])` clears that kind.
  - `AllStudents` rows carry `RefId = null`, so they are set via `SetEveryoneTarget(true)` — never via
    `SetTargetsOfKind` (which would store `Guid.Empty`).
  - `SetEveryoneTarget(true)` clears the whole set and writes the single `AllStudents` row, so call it **last**
    (or instead of the per-kind calls), never before them.
- `PickerOption` is a nested record of the **page** (`AssignmentAuthoring.razor:663`:
  `public sealed record PickerOption(string Value, string Label)`). The dialog cannot share it.
- Page option sources already exist: `_gradeLevelOptions`, `_streamOptions` (grade-scoped, loaded by
  `LoadStreamTargetsAsync`), `_activityGroupOptions`. Students are **server-searched**, not preloaded.
- The page's load path is `LoadAsync` (`AssignmentAuthoring.razor:1293`); in Edit mode it calls
  `LoadPersistedSelectionAsync`, then `SyncTargetPickerState()` + `SyncGroupsFromTargets()` and sets `_everyone`.
- The save path is `SaveAsync` (`AssignmentAuthoring.razor:2193`); it sends
  `_model.ToCreateRequest(..., _model.ToTargetDtos())` / `_model.ToUpdateRequest(..., _model.TargetsChanged ? _model.ToTargetDtos() : null)`.

### In scope (expected files)

- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/TargetsAndAudienceEntry.cs`
- `.../TargetsAndAudienceDialogModel.cs`
- `.../TargetsAndAudienceDialog.razor`
- `.../AssignmentAuthoring.razor`
- `.../AssignmentAuthoring.razor.css`
- delete `nul` (repo root)

### Out of scope

- No EF migration, schema, or `*.Contracts` change.
- No new public API on `AssignmentEditFormModel`.
- No change to `IAssignmentTargetResolver` / the publish path.
- No change to unrelated pages/components.

### Design

1. **`TargetsAndAudienceEntry.cs`**
   - `TargetsAndAudienceCategory` — drop `Subjects`; final five: `GradeLevels, Streams, Students, ActivityGroups, Everyone`.
   - Add `public sealed record TargetsAndAudienceOption(string Value, string Label);` (dialog-facing; `Value` is the `Guid` string).
   - `TargetsAndAudienceEntry` → `public sealed record TargetsAndAudienceEntry(TargetsAndAudienceCategory Category, Guid? RefId, string Label);`.
2. **`TargetsAndAudienceDialogModel.cs`**
   - `TargetsAndAudienceCategory Category` (default `GradeLevels`).
   - `TargetsAndAudienceOption? SelectedOption`.
   - `IReadOnlyList<TargetsAndAudienceOption> GradeLevelOptions` / `StreamOptions` / `ActivityGroupOptions` (`init`, default `[]`).
   - `Func<string, CancellationToken, Task<IReadOnlyList<TargetsAndAudienceOption>>>? StudentSearch` (`init`).
3. **`TargetsAndAudienceDialog.razor`** (`@inherits DialogShellBase<TargetsAndAudienceDialogModel, TargetsAndAudienceEntry>`)
   - Category `FluentSelect` (the five labels).
   - Per selected category, render **only** the matching control:
     - `GradeLevels` → `FluentSelect` over `Model.GradeLevelOptions`.
     - `Streams` → `FluentSelect` over `Model.StreamOptions`.
     - `ActivityGroups` → `FluentSelect` over `Model.ActivityGroupOptions`.
     - `Students` → `FluentAutocomplete` whose `OnOptionsSearch` invokes `Model.StudentSearch` (min 2 chars; empty when no delegate).
     - `Everyone` → no value control; a short hint.
   - `SubmitAsync`: `Everyone` → `new TargetsAndAudienceEntry(Everyone, null, "Everyone")`; otherwise a selected option is
     required (set `Error` when missing) → `new TargetsAndAudienceEntry(category, Guid.Parse(option.Value), option.Label)`.
   - Follow `.github/skills/dialog-ui/SKILL.md` (EditForm + `DialogShellFooter` + `SubmitAsync`).
4. **`AssignmentAuthoring.razor`**
   - Build the dialog model in `OpenTargetsAndAudienceDialogAsync` from the page's options, mapping each
     `PickerOption` → `TargetsAndAudienceOption`; pass a student-search delegate.
   - Refactor the body of `OnStudentSearchAsync` into
     `private async Task<IReadOnlyList<TargetsAndAudienceOption>> SearchStudentsAsync(string query, CancellationToken ct)`;
     keep `OnStudentSearchAsync` (if still needed) as a thin adapter; point the dialog's `StudentSearch` at the refactored method.
   - Keep the existing "Everyone is mutually exclusive with any other category" append rule.
   - **Populate on load:** replace the old `_model`→picker mirroring so `_targetsAndAudience` is rebuilt from
     `_model.Targets` after `LoadPersistedSelectionAsync`: one entry per target row, label resolved from the option
     lists (`Everyone` → "Everyone"; otherwise the matching option's label, falling back to the `RefId` string).
   - **Sync on save:** before `SaveAsync` builds the request, rebuild `_model`'s targets from `_targetsAndAudience`
     using only the existing model API — `SetTargetsOfKind` for GradeLevel/Stream/Student/ActivityGroup (empty list
     clears), and `SetEveryoneTarget(true)` when an `Everyone` entry is present (call it so it wins). Then
     `ToTargetDtos()` carries them.
   - **Delete the orphaned members** — only those unreferenced after the change: `_everyone`, `_selectedGroupIds`,
     `_selectedStreamTargetOptions`, `_selectedStudentOptions`, the six handlers
     (`OnEveryoneChangedAsync`, `OnSelectedGradeTargetsChangedAsync`, `OnSelectedStreamTargetsChangedAsync`,
     `OnSelectedStudentTargetsChangedAsync`, `OnSelectedGroupsChangedAsync`, `OnStudentSearchAsync` if fully replaced),
     the line projections (`SubjectLines`, `GradeLevelLines`, `StreamLines`, `StudentLines`, `GroupLines`, `EveryoneLine`),
     the target-option projections (`SelectedGradeTargetOptions`, `SelectedStreamTargetOptions`,
     `SelectedStudentTargetOptions`, `SelectedGroupOptions`), `TargetChips`, `AudienceEditorDisabledReason`,
     `ConstraintPickersDisabledReason`, `GroupPickerReason`, `GroupPickerDisabled`, `GroupPickerAriaDescribedBy`,
     `DroppedGroupLinksNote`, and helpers that only those used.
     **Do NOT delete anything the load path, the save path, or the Basics section still uses** (e.g. the primary-grade
     `_selectedGradeLevel`, `_gradeLevelOptions`, `_activityGroupOptions`, `_streamOptions`, `GradePickerDisabled`,
     `SubjectPicker*`). Let `dotnet build` prove the boundary; restore any member a compile error shows is still used.
   - Item rendering: keep the label-over-bold-value `ItemTemplate` (category label + `entry.Label`) and the remove action.
5. **`AssignmentAuthoring.razor.css`** — keep the existing `.targets-audience-item*` styles.

### Acceptance criteria

- AC-1 `dotnet build SchoolCollab.slnx` → **0 errors**.
- AC-2 `TargetKindDto` still has exactly five kinds and no `Subject`; nothing changed under `*.Contracts`.
- AC-3 The dialog renders only the control for the chosen category (no all-fields-at-once), and returns a typed
  `TargetsAndAudienceEntry` (`RefId` set for the four entity categories, `null` for `Everyone`).
- AC-4 `SaveAsync` writes the authored entries as `AssignmentTarget` rows: the four categories via `SetTargetsOfKind`,
  `Everyone` via `SetEveryoneTarget(true)`; `ToTargetDtos()` reflects exactly the builder's entries.
- AC-5 Edit load repopulates `_targetsAndAudience` from `_model.Targets` (opening edit on a targeted assignment shows
  the same targets), and a no-op save does not change the target set.
- AC-6 The six old handlers and the old audience projections are gone with no remaining references; `nul` is deleted.
- AC-7 No migration/schema/contract file is touched.

## Worker Report

### Pass 1 — implement (worker `deepseek-v4.1-flash`)

Changed files: `TargetsAndAudienceEntry.cs`, `TargetsAndAudienceDialogModel.cs`, `TargetsAndAudienceDialog.razor`,
`AssignmentAuthoring.razor` (+ `AssignmentAuthoring.razor.css` kept, `nul` deleted).
Build: 0 errors. Tests: `SchoolCollab.Assignments.Tests.Unit` 884 total, **33 failed** — all driving the removed
`authoring-audience-*` markup (the plan omitted the two bUnit files).
Deviations: kept `AudienceEditorDisabledReason` and `_selectedGroupIds` (still compiled against); kept
`OnTargetsChangedAsync` (re-pointed at the builder); preview machinery left as dead code.

### Pass 2 — rework (worker `deepseek-v4.1-flash`)

Fixed all 4 reviewer P1s + 5 P2s and the 33 tests. Build: 0 errors; `SchoolCollab.Assignments.Tests.Unit`
**876 passed / 0 failed**.

### Pass 3 — focused fix (worker `deepseek-v4.1-flash`)

Fixed the re-verification P1s: `SyncTargetsFromBuilder` now clears a stale loaded `AllStudents` row when the
builder holds no `Everyone` entry (P1-A1); `RemoveTargetEntryAsync` removes by instance identity, not record
value equality (P1-A2); restored `Edit_ReassertingTheSameGroups_DoesNotRewriteTheLinks` in the new-builder idiom
(P1-B); removed an orphan XML doc comment and re-indented the layout wrapper (P2s). Build: 0 errors;
`SchoolCollab.Assignments.Tests.Unit` **877 passed / 0 failed**. Deviations: none.

## Review

Static reviewer (`kimi-k2.7-code`) on the pass-1 patch: **P1** — (1) `SectionCard` ignores `ItemActions` when
`ItemTemplate` is set, so the entry remove button never rendered; (2) the jump-nav anchor still named
`#authoring-audience`; (3) `AudienceEditorDisabledReason` no longer enforced the UX-21 fail-closed posture;
(4) the TGT-16 recipient preview was dropped. Plus P2s (unused `@using`, indentation, duplicate heading, dead
fields/CSS). All four adjudicated REAL against source and fixed in pass 2.

Split re-verification on the pass-2 patch (two parallel static reviewers, `kimi-k2.7-code`):
- `src` slice: **P1** — (A1) `SyncTargetsFromBuilder` never cleared a stale `AllStudents` row; (A2)
  `RemoveTargetEntryAsync` removed by record value equality. Both adjudicated REAL and fixed in pass 3.
- `tests` slice: **P1** — the deleted no-op group-link rewrite guard test was not replaced. Fixed in pass 3.

## Acceptance

Verdict: **CLOSED** (residuals below accepted as P2).

| Criterion | Result |
|---|---|
| AC-1 `dotnet build SchoolCollab.slnx` | **0 errors** (parent-run; 31 pre-existing warnings) |
| AC-2 `TargetKindDto` unchanged (5 kinds, no `Subject`); no `*.Contracts`/migration touched | **pass** |
| AC-3 dialog renders only the chosen category's control; returns a typed entry | **pass** (source-verified; see residual 2) |
| AC-4 `SaveAsync` writes the entries as `AssignmentTarget` rows | **pass** — `SyncTargetsFromBuilder` via `SetTargetsOfKind`; `Everyone` via `SetEveryoneTarget(true)`; stale `AllStudents` cleared |
| AC-5 edit load repopulates the builder; a no-op save preserves the set | **pass** — pass-3 `Edit_ReassertingTheSameGroups_DoesNotRewriteTheLinks` + builder load tests |
| AC-6 old handlers/projections gone; `nul` deleted | **pass** |
| AC-7 no migration/schema/contract file touched | **pass** |

Parent-run tests: `SchoolCollab.Assignments.Tests.Unit` **877 passed / 0 failed**;
`SchoolCollab.ArchitectureTests.Unit` **96 passed / 0 failed**. Patch: `diffs-targets-audience-typed.patch`
(3715 lines, 0 phantom deletions).

### Deviations / residual P2s (no open P1)

1. **Tier-2 reviewer-loop bound exceeded by one.** One rework + the split re-verification, then one focused fix
   pass. The reviewer bound is ≤1; the extra pass was taken because the re-verification findings were small,
   independently-adjudicated functional defects (a stale `AllStudents` row, value-equality removal, deleted
   coverage). Recorded as an explicit deviation.
2. **The Add dialog's `SubmitAsync` has no direct bUnit coverage** — bUnit renders the page without a FluentUI
   dialog provider, so the real dialog cannot be driven in-tree; the tests assert the page's mocked
   `ShowShellDialogAsync` seam instead. The dialog's per-category control rendering is verified by source review
   only. Residual P2.
3. **Pre-existing test-harness quirk** — a kebab-invoked `ValidateForSave` error renders on the next pass;
   `Edit_TargetsLoadFails…` re-sets parameters before reading `#authoring-error`. Untouched (pre-existing).
4. The prior session's UI delta (single `SectionCard` + dialog) remains uncommitted on `main`; the round patch
   is the full feature diff from `870b4b0`. Owner authorization is still required to commit/push/PR.

## Post-acceptance rework (owner-requested UI revision)

Owner asked for five bounded UI changes after the round closed; applied as one worker run, then statically re-verified.

1. **SectionCard header owns the title** — the compartment `<h3>Targets & audience</h3>` was removed; the `SectionCard` now carries `Title="Targets & audience"`, so exactly one title renders in the card header (with `Count` + native Add).
2. **No double frame** — `.authoring-compartment--right` resets `padding`/`background-color`/`border`/`border-radius`, so the card's own `FluentCard` frame is the single visible border. The shared `SectionCard` was not modified. (Deviation: the base `.authoring-compartment` class also declared those four properties on the same element, so the modifier had to reset them explicitly rather than only drop its own.)
3. **Primary grade moved** into `#authoring-targets` (above the card), with its `authoring-grade-reason` and the no-grades warning; ids `authoring-basics-grade`/`authoring-grade-reason` preserved; FR-58/signature side effects unchanged.
4. **Multi-select dialog** — `TargetsAndAudienceDialogResult(IReadOnlyList<TargetsAndAudienceEntry> Entries)`; every entity category uses `Multiple="true"` with `SelectedOptions`/`SelectedOptionsChanged` (FluentUI 4.14.2 — not `@bind-SelectedValues`); Everyone still emits one value-less entry; `AssignmentAuthoring.razor` appends all returned entries and keeps the Everyone-exclusivity rule.
5. **Dialog size** — now opened with `DialogSize.Large` (720px), not `Small` (420px), so the multi-select rows fit. (Deviation: `Large`, not the requested `Medium` minimum — the dialog's `full-width` field floors at 400px, and 560px beside the 180px label column left the multi-select rows overflowing.)

Verification (parent): canonical `dotnet build SchoolCollab.slnx` is **blocked by MSB3027/MSB3021** — the running dev stack (`SchoolCollab.Assignments.Worker` 43508, `SchoolCollab.Auth` 28364, `SchoolCollab.AI.Server` 47800, Visual Studio 43656) holds the output DLLs. Compilation was therefore proven with the output redirected out of the locked dirs: **Build succeeded, 0 errors**. Tests: `SchoolCollab.Assignments.Tests.Unit` **879 passed / 0 failed**; `SchoolCollab.ArchitectureTests.Unit` **96 passed / 0 failed**. Static reviewer (`kimi-k2.7-code`): **PASS**, no P1/P2.

Residual (P2): the card/compartment title reads "Targets & audience" (lower-case article) to stay consistent with the in-page jump-nav label, not the owner's literal "Targets & Audience"; change both together if the capital form is wanted.
