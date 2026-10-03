# Round — authoring-residuals-mopup

Provider: pi/ollama-cloud · **Tier 2 (Light — worker + static diff-only reviewer)** · UI-only behavioural round
Models: parent (plan owner) · worker `ollama-cloud/deepseek-v4.1-flash` · reviewer `ollama-cloud/kimi-k2.7-code`
Round base: **`fcfdc217`** — *corrected during the round.* The branch was first created from `main`
(`66c2676a`), but this plan depends on the **unmerged** `stack/5` tip: the `IsDirty`/`CaptureSaveSnapshot` code
that item 5 optimises and the `§17 Deferred / known gaps` table that item 6 edits **do not exist on `main`**
(and the plan's line numbers were stack-5 numbers). The worker caught this before making any edit, reported it
with evidence, and stopped; the parent verified the evidence independently (`git merge-base --is-ancestor
21476126 main` → not an ancestor; `§17` and `IsDirty` greps → 0 on `main`) and re-mounted the branch with
`git checkout -B stack/6-authoring-residuals-mopup fcfdc217`. No commits were lost — the branch had none — and
the untracked round doc survived. Stack order: `main → stack/5 (#292, held for a later merge) → stack/6`.
Branch: `stack/6-authoring-residuals-mopup`

## Plan

### Goal

Close every residual recorded in `documents/specs/assignment-authoring-compartments.md` **§17 Deferred / known
gaps** that is a UI-side item in the authoring surface, fix the two residuals the previous round disclosed only
in its ephemeral round doc, and leave the §17 table an accurate, self-maintaining record.

### Settled decisions (owner-accepted, 2026-10-02)

- **D1 — the table is the authority.** §17 lists what is open; each item below names the entry it closes and the
  table is edited in the same change (per its own instruction: "strike an entry, naming the round that closed it").
- **D2 — archived-group relink (R1) is closed on the UX side, not the API side.** R2 already settled the save
  path (off-group rejection applies to **newly-added** ids only, so an unchanged persisted archived id re-saves).
  What remains is that `LoadLinkedGroupIdsAsync` silently drops ids the picker cannot represent
  (`AssignmentAuthoring.razor:1273-1279`) and the author is told nothing. **No route/contract change.**
- **D3 — P2-c (transaction rollback proof) is NOT in this round.** It is test-only in
  `tests/SchoolCollab.Assignments.Tests.Integration` (Testcontainers/PostgreSQL) — a different workstream with a
  Docker dependency; it runs as the immediately-following bounded pass. It stays in the §17 table.
- **D4 — the read-only View aria entry needs no action** (verified benign: the idref does not dangle on a control
  that had something to say). The table entry is left as-is with its reasoning; do not "fix" it.
- **D5 — §17 is extended** with the two residuals that so far exist only in
  `documents/rounds/round-authoring-ux7-residuals.md`: the `IsDirty`-per-render serialization cost and the
  UX-7/F17 defence-in-depth gap (the latter stays open — record it; do not implement it).

### Items

| # | Closes | Change |
|---|---|---|
| 1 | §17 "`ReloadAsync` re-reads the summary only" (R1) | `ReloadAsync` (`:1667-1687`) re-reads only `Api.GetByIdAsync` + `_model.LoadFrom`. Reuse the initial load path (`LoadChildrenAsync` + `LoadLinkedGroupIdsAsync`, as `LoadAsync` does at `:1077-1078`) so a lifecycle action (publish / unpublish / close / archive) reflects the persisted children and target rows immediately. **Keep the existing cancellation/error posture** — a failed re-read must not corrupt the form (do not clear state before the re-read succeeds). |
| 2 | §17 "F4 — a restored student target renders a fallback chip" (R2) | Restoring the constraints Everyone replaced puts the student **ids** back but never re-resolves their labels, so a restored student chip shows `Student <id8>`. Re-resolve labels for the restored ids and retain the option objects in `_selectedStudentOptions`, so the chip shows the real name. |
| 3 | §17 "The students picker cannot name its reason" (F15) | `FluentAutocomplete` owns its inner `<fluent-text-field>`'s aria surface on FluentUI 4.14.2 and **drops** the page's `aria-describedby` (`:247-262`). Make the disabled students picker expose its reason like its four siblings — wrap the control so the attribute lands on an element that keeps it. Update or replace the change-detector assertion `AssignmentAuthoringBunitTests.AssertEveryDisabledConstraintControlNamesARenderedReason` so it now passes for **all five** controls; it must still fail if a future FluentUI forwards the attribute (no assertion that would break on upgrade). |
| 4 | §17 "Archived-group relink is refused with 422" (R1) — UX half | When `LoadLinkedGroupIdsAsync` drops a persisted id it cannot represent, tell the author: render a visible note naming the dropped link(s) rather than silently discarding them. Reuse the existing `authoring-reason` styling/markup idiom. Do **not** change the route, the replace-set semantics, or what is persisted. |
| 5 | §17 "`IsDirty` serializes the full save payload on every render" (from `authoring-ux7-residuals`) | `OnAfterRenderAsync` → `SyncBeforeUnloadGuardAsync` → `IsDirty` → `CaptureSaveSnapshot` → `JsonSerializer.Serialize` of the whole update request, every render. Reduce that cost (cache the snapshot and invalidate it on the change handlers, or compare incrementally) **without changing dirty semantics** — D3 of the previous round still holds: a changed-then-reverted value is not dirty. |
| 6 | §17 hygiene | Edit the table: strike the entries closed by this round **naming it**, and add the two missing residuals per D5. Keep the table's stated contract intact. |

### Expected files (bound the diff)

| File | Change |
|---|---|
| `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentAuthoring.razor` | items 1–5 (items 1, 2, 4, 5 are code-behind; item 3 is the picker markup) |
| `.../AssignmentAuthoring.razor.js` | only if item 3 genuinely needs an interop hook (prefer a pure-markup wrapper) |
| `.../AssignmentEditFormModel.cs` | only if items 2/5 sit more naturally there (the dirty baseline already lives here) |
| `documents/specs/assignment-authoring-compartments.md` | item 6 |
| `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentAuthoringBunitTests.cs` | the tests below |

**Explicitly out of scope:** no route/contract/endpoint change, no EF migration, no new feature flag, no
`Directory.Packages.props` edit, no FluentUI upgrade, no P2-c rollback test (D3), no read-only-aria "fix" (D4).
If an item cannot be done without one of these, **stop and report** (mid-round escalation rule).

### Acceptance criteria (each a DISCRIMINATING test — it must fail against `66c2676a`)

1. **(1)** After a lifecycle action, the surface reflects the **re-read** children/targets — assert on state that
   only the fresh read can produce (not merely that `_item` refreshed). A failing re-read leaves the form intact.
2. **(2)** Restore a student constraint (Everyone ON → OFF with the student picker stubbed to answer a search)
   and assert the chip renders the **resolved label**, not `Student <id8>`.
3. **(3)** With the constraint gate engaged, **all five** disabled controls (grades, streams, students, groups,
   Everyone) each name a reason that actually renders; the students assertion must be specific to the students
   control (not a page-wide text match).
4. **(4)** When a persisted link is dropped as unrepresentable, the page renders a note naming it; when nothing
   is dropped, the note is absent.
5. **(5)** Dirty semantics are **unchanged** after the optimisation: the previous round's
   `ChangedThenRevertedValue_IsNotDirty_SoNoGuardRemains` still passes, and a new assertion proves the snapshot
   is not recomputed when nothing changed (or that dirty still flips correctly on an edit).
6. **(6)** §17 strikes the closed entries naming this round and contains the two added ones.

### Reviewer acceptance criteria (what the static diff review must adjudicate)

- **Item 1 must not create a data-loss path.** Re-reading children/targets after a lifecycle action happens on a
  form that may be **dirty**; if the re-read overwrites unsaved edits, the round has traded one bug for a worse
  one. State explicitly whether the re-read is safe on a dirty form and why.
- **Item 1 must not double-load or leak** (`_loadCts` cancellation, the `OperationCanceledException` posture).
- **Item 3's wrapper must not break the picker's existing aria contract** (combobox role, `aria-expanded`,
  `aria-controls`, `aria-label` owned by `FluentAutocomplete`) or its keyboard behaviour.
- **Item 5 must not change dirty semantics** — the baseline comparison is what makes UX-7 correct; a perf fix
  that reintroduces false prompts is a regression, not an optimisation.
- Item 4 must not claim data was lost when it was merely not re-writable.
- Conventions: multi-select stays `SelectedOptions`/`SelectedOptionsChanged` (never `@bind-SelectedValues`); no
  inline `<style>`; isolated `.razor.css`; no unrelated overwrites.

## Worker Report

**Run `72b65886` (`deepseek-v4.1-flash`), on the corrected base `fcfdc217`.** The base retarget was
supervisor-approved and is recorded in the header: the branch was first mounted on `main` although the plan
depends on the unmerged `stack/5` tip; re-mounted losslessly with `git checkout -B` (no commits lost, untracked
round doc survived). Post-retarget base check confirmed `IsDirty`/`CaptureSaveSnapshot`/`SyncBeforeUnloadGuardAsync`
present, `§17` at `:428`, and the ux7 round doc present.

Changed files: `AssignmentAuthoring.razor` (items 1–4), `AssignmentEditFormModel.cs` (item 5),
`AssignmentAuthoringBunitTests.cs` (7 tests + the updated detector), and the spec (item 6). Patch 1,184 lines ·
4 diffs · 0 deletions.

**Item notes**
1. **`ReloadAsync` re-reads the persisted set.** The initial Edit load is extracted into one
   `LoadPersistedSelectionAsync` (children → links → picker mirrors → FR-58 group-subject union), called from
   both `LoadAsync` and `ReloadAsync`; the non-Edit branch keeps `LoadTargets([])`. **Dirty safety:** the re-read
   is gated on an `unsavedEdits` flag sampled *before* the summary refresh, because two callers
   (Scheduled→Unpublish, Draft→Schedule) leave the author on a still-editable surface without having saved.
   Cancellation/error posture unchanged; nothing is cleared before a read succeeds.
2. **F4 restored student chip.** `OnEveryoneChangedAsync` OFF now re-resolves each restored student id via
   `StudentsApi.GetStudentByIdAsync` and keeps the option in `_selectedStudentOptions`; one label spelling
   (`StudentLabel`) shared with the search path. Fail-open per id.
3. **Students picker names its reason — and the plan's premise was WRONG.** The declared `aria-describedby` is
   **not dropped**: `FluentAutocomplete` routes it to its own root `<div class="full-width
   fluent-autocomplete-multiselect">`, while the `Id` (and the combobox role / `aria-expanded` / `aria-controls`
   / label association) is on the inner `<fluent-text-field>` — so the idref reaches nothing for ATs. Fix: a
   labelled `role="group"` row wrapper (`#authoring-audience-students-group`, `aria-label="Students"`); the
   control's own declaration stays so a forwarding FluentUI version keeps working; the detector resolves the
   reason from the control **or** its row group and never asserts the limitation (upgrade-safe).
4. **Dropped archived link is visible.** `LoadLinkedGroupIdsAsync` records unrepresentable ids; the compartment
   renders `#authoring-groups-dropped-links` naming the group(s) from the *unfiltered* read, falling back to the
   id. Wording states what is true (the link stays readable while the selection is unchanged) and never claims
   loss. No route/contract/persistence change.
5. **`IsDirty` per-render cost.** `CaptureSaveSnapshot` is now a 128-bit fingerprint (two FNV-1a lanes, fixed
   32-hex) mixed from the form's values instead of `JsonSerializer.Serialize(ToUpdateRequest(...))`; the
   predicate and call sites are unchanged, so dirty semantics are byte-for-byte the same. **Deviation: the
   criterion is met in substance, not literally** — page-level caching cannot be sound here because the child
   editors write the shared model **without re-rendering the page**, so a stale cache is dangerous in *both*
   directions (a stale "clean" silently drops the navigation prompt = data loss; a stale "dirty" resurrects the
   phantom prompt the previous round fixed). The plan's own "or compare incrementally" alternative was taken.
6. **§17 hygiene.** Struck, naming this round: `ReloadAsync` summary-only, the F4 fallback chip, the
   students-picker reason, the UX half of the archived-group relink, and the ux7 round's `IsDirty` cost. Added:
   **UX-7/F17 defence-in-depth** (open, not implemented) per D5; the archived-group row is narrowed to its
   **route half** so the Architecture doc-guard's `archived` fact survives. P2-c and the read-only-View entry
   are untouched (D3/D4).

**Tests — 7 new, 6 verified to fail pre-fix** (production reverted, tests intact; pre-fix run total 15 /
**failed 6**): `Unpublish_IntoAnEditableDraft_ReadsThePersistedChildrenAndTargets` (item 1),
`StudentsPicker_NamesItsReasonFromItsOwnRow_WhenTheConstraintGateDisablesIt` (3),
`ConstraintReason_SitsWithThePickerRows_AndEveryDisabledControlNamesARenderedReason` (updated detector, 3),
`EveryoneOff_RestoresAResolvedStudentChip_NotTheIdFallback` (2),
`ArchivedLinkedGroup_IsNamedOnThePage_AndTheNoteIsAbsentWhenNothingIsDropped` (4),
`SaveSnapshot_IsAFixedWidthFingerprint_NotTheMaterializedPayload` (5). Declared **non-regression anchors** (pass
pre-fix by construction, and declared rather than dressed up as discriminating):
`SaveSnapshot_MatchesTheSerializedPayload_OnEveryDirtyRelevantField` (equivalence guard for the new
implementation) and `Unpublish_FromScheduled_WithUnsavedRows_KeepsTheAuthorsEdits` (removing the unsaved-edits
gate fails it). `ChangedThenRevertedValue_IsNotDirty_SoNoGuardRemains` (ux7) still passes → criterion 5's
dirty-semantics half holds.

Parent-verified on the restored tree: build **0 errors** · Assignments.Tests.Unit **820/0** (813 → +7) ·
ArchitectureTests.Unit **75/0**.

**Residual risks disclosed:** item 5 hand-mirrors the payload projection (a future `ToCreateRequest` field must
be added to the walk *and* the matrix — documented at both sites); item 3's benefit rests on ATs announcing a
*named group* (the description cannot be moved onto the focusable field declaratively on 4.14.2); item 1's
freshness is now conditional on a clean form (confirming a questions draft on a *dirty* form still does not pull
the newly created questions onto that instance — unchanged from pre-fix); and child-only edits do not re-render
the page, so the browser-unload mirror can lag them (pre-existing; recorded next to §17's new defence-in-depth
row).

### Pass R2 (bounded P2 rework) — `1e48d9a3` (`deepseek-v4.1-flash`)

Fixes the review's two P2 findings; no other change.

- **P2-1 — `ContentModules` is now structurally covered.** The model gained a `ContentModules` carrier
  (`AssignmentEditFormModel.cs:48`) — additive, **no production writer**, documented with the file's existing
  `ArchiveGraceDays` "carried, not authored" precedent. `ToCreateRequest` normalizes null/empty → wire `null` and
  passes it between `Attachments` and `Resources` (`:425`, `:457`); `CaptureSaveSnapshot` mixes it at that same
  wire position (`:847-853`) — a null-safe sentinel, then each module's fields. The walk's doc gained an explicit
  **completeness rule** (every field `ToUpdateRequest` hands to the wire, in projection order) plus the failure
  mode it prevents. The equivalence matrix gained a `—— ContentModules` block (added / removed / url /
  threshold each move both digest and payload; empty→null moves neither) and an explicit assertion that the
  digest changes when a module is added. **Discrimination proven**: with the mix deleted, the target assertion
  failed (`Expected (afterSnapshot != beforeSnapshot) to be True … but found False`); the mix was then restored
  byte-for-byte and re-verified green.
- **P2-2 — the partial-refresh posture is now explicit.** `LoadPersistedSelectionAsync` is wrapped in its own
  try/catch (`AssignmentAuthoring.razor:2124-2127`) that logs the partial failure distinctly, with the accepted
  posture documented (refreshed scalars, **stale children/targets, nothing cleared**) and the ordering kept —
  the status refresh is the primary thing the author needs. **No test added, deliberately**: the reachable throw
  paths are the post-load page mirrors (`SyncTargetPickerState` / `SyncGroupsFromTargets`), transport failures
  are already swallowed inside the loaders, so no mock can reach the new catch and any assertion would pass
  pre-fix — a test that asserts nothing is worse than a documented posture.

Parent-verified: build 0 errors · Assignments **820/0** · Architecture **75/0** · Students **623/0**.

## Review

**Diff review `a823512e` (`kimi-k2.7-code`) — Verdict: P2-only — the round can be accepted.** The data-loss
path the reviewer acceptance criteria targeted is closed.

### Confirmed correct
- **Item 1 data-loss gate** (`AssignmentAuthoring.razor:2087-2119`): `unsavedEdits = IsDirty` is sampled
  **before** `_model.LoadFrom(_item)` mutates the scalar fields, so the dirty signal is not erased by the summary
  refresh; the gate applies to the persisted-set re-read only, which is skipped when dirty — so unsaved
  child/target edits cannot be overwritten.
- **Item 3 wrapper + detector** (`:246-275`): the labelled `role="group"` wrapper carries `aria-describedby`
  and `aria-label`; the inner `FluentAutocomplete` keeps its full combobox contract (`Id`, `aria-expanded`,
  `aria-controls`, label association). The detector (`AssignmentAuthoringBunitTests.cs:2219-2225`) accepts either
  the control **or** the group as the carrier, so it will not punish a future FluentUI upgrade.
- **Item 5 fingerprint semantics** (`AssignmentEditFormModel.cs:756-895`, `:898-990`): the FNV-1a two-lane
  128-bit digest is a sound change-detector — length-prefixed and type-fixed — and matches the payload
  projection for every field the form can actually mutate; the equivalence matrix exercises both directions.
- **Item 2** (`:1805-1843`): re-resolves each restored id via `GetStudentByIdAsync`, fail-open, sharing the
  `StudentLabel` helper with the search path.
- **Item 4** (`:569-584`, `:1623-1660`): names the archived group from the *full* read and states the link is
  kept while the selection is unchanged — it never claims data loss.
- **Item 6 §17 table**: accurate — closed entries struck and named, P2-c and the read-only-View entry untouched
  (D3/D4), the archived row narrowed to its route half, and the new UX-7/F17 defence-in-depth row left **open**.
- **Conventions**: `SelectedOptions`/`SelectedOptionsChanged`, no inline `<style>`, no
  route/contract/migration/CPM change.

### P2-1 — `ContentModules` is carried by `ToUpdateRequest` but omitted from the fingerprint walk
`AssignmentEditFormModel.cs:756-895`. `ToUpdateRequest` carries `ContentModules` (`:499`), but
`CaptureSaveSnapshot` never mixes it. The field is always null today (no editor owns it), so nothing is
user-visible **yet** — but the failure mode is exactly what this round exists to prevent: a form field added
later without a matching `hash.Add` makes a **real edit read as "clean"**, so the navigation prompt silently does
not fire and unsaved work is lost. **Parent-verified** before acting: the walk contains no `ContentModules` mix,
and the only occurrence in the file is `:499`. Required: mix it at the wire position and add a matrix case so the
walk covers every `ToUpdateRequest` field structurally, not just those with editors today.

### P2-2 — a failed lifecycle re-read leaves new scalars beside old children
`AssignmentAuthoring.razor:2087-2119`: `_model.LoadFrom(_item)` runs **before** the `unsavedEdits` gate and before
`LoadPersistedSelectionAsync`, so a throw there leaves the refreshed status beside the prior child/target set.
The review judged this **not a regression** — pre-fix refreshed scalars and never re-read children at all.
Required: make the partial-refresh posture explicit rather than silent.

### Test adjudication
The six claimed discriminating tests **would** fail against `fcfdc217`. The two declared non-regression anchors
(`SaveSnapshot_MatchesTheSerializedPayload_OnEveryDirtyRelevantField`,
`Unpublish_FromScheduled_WithUnsavedRows_KeepsTheAuthorsEdits`) are correctly labelled — both pass pre-fix by
construction and guard the new implementation.

### Deviations adjudicated
- **Item 3** — the worker's correction of the plan's premise is **correct**: `FluentAutocomplete` does not drop
  the attribute; it lands on the component's root `<div>` while the accessible focusable element is the inner
  `<fluent-text-field>`. The `role="group"` wrapper is the right fix and is upgrade-safe.
- **Item 5** — the worker's argument for a fingerprint over page-level caching is **correct**: child editors
  mutate the shared model without re-rendering the page, so a stale cache would be unsafe in **both**
  directions. The fingerprint satisfies the plan's "compare incrementally" alternative.

## Acceptance

**Round CLOSED — accepted.** Tier 2; the ≤1 rework iteration was spent on the review's two P2 findings.

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| Assignments.Tests.Unit | **820 / 0** |
| ArchitectureTests.Unit | 75 / 0 |
| Students.Tests.Unit | 623 / 0 |
| Patch `diffs-authoring-residuals-mopup.patch` | 1,285 lines · 4 diffs · **0** `deleted file mode` · 0 new |
| Branch / index | `stack/6-authoring-residuals-mopup` @ base `fcfdc217` · clean |

**Delivered (D1):** §17's UI-side residuals — (1) `ReloadAsync` re-reads the persisted set, gated on an
`unsavedEdits` flag sampled **before** the summary refresh; (2) the restored student chip re-resolves its label;
(3) the students picker names its reason through an upgrade-safe `role="group"` wrapper; (4) the dropped
archived link is named on the page; (5) the per-render save snapshot is a 128-bit fingerprint with dirty
semantics unchanged; (6) §17 struck the closed rows **naming this round**, narrowed the archived row to its
route half, and added the open UX-7/F17 defence-in-depth row. Plus the review's two P2s: the `ContentModules`
structural-completeness fix and the explicit partial-refresh posture.

**Gate history:** worker `72b65886` (including the supervisor-approved base retarget from `66c2676a` to
`fcfdc217`) → diff review `a823512e` (**P2-only**) → bounded rework `1e48d9a3` (P2-1 + P2-2).

**Decisions recorded at acceptance:**
- The new public `ContentModules` member is **accepted** as the carrier that makes the structural assertion
  expressible. It is additive, has **no production writer** (no wire-behaviour change — the equivalence matrix is
  green), and follows the file's existing "carried, not authored" precedent. Recorded because a test-only public
  member should be a visible decision, not a quiet one.
- **No test was written for P2-2, deliberately**, and that is accepted: the reachable throw paths are post-load
  page mirrors, so no mock can reach the new catch and any assertion would pass pre-fix.

**Still open in §17 (not this round):** the P2-c transaction-rollback proof (D3) · the archived-group **route**
half · the read-only-View entry (D4, verified benign) · the UX-7/F17 defence-in-depth row (recorded, open).

**Merge: NOT authorized.** PR #292 (`stack/5`) is open and green, held for a later stack merge; this layer is
uncommitted.