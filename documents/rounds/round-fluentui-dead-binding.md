# Round — fluentui-dead-binding

Provider: pi/ollama-cloud · **Tier 2 (Light; UI-only — SKILL.md "UI-only work is NEVER Tier 3")** · Option A
Models: worker `ollama-cloud/deepseek-v4.1-flash` · reviewer `ollama-cloud/kimi-k2.7-code`
Round base: `f4c60d96f2cb83eacb8ace4e09180df33d4023d1`
Isolation base (dangling commit; GC-able — rebuild with the three commands):
`SNAP = 0d57b812aec0ffcc87a931c502a50f985f52c430`

```bash
git add -A && TREE=$(git write-tree) && SNAP=$(git commit-tree "$TREE" -p HEAD -m "round-fluentui-dead-binding isolation base") && git reset -q
```

Pre-existing uncommitted delta excluded from this round's patch: **36 modified tracked + 14 untracked** (round `assignment-authoring-r1` + the 2026-10-01 spec/doc updates, including `documents/solution/fluentui-dead-selectedvalues-binding.md`).
Patch freeze — SNAP tree → worktree. The `git add -N` is **required**: without it the 14 pre-existing *untracked* files (present in SNAP, absent from the index) diff as **deletions**, because `git diff <tree>` resolves the worktree side through the index. This matches `isolate-round-diff-over-uncommitted-prior-round` step 8.

```bash
git add -N . && git diff 0d57b812aec0ffcc87a931c502a50f985f52c430 -- . ':(exclude)documents' > documents/rounds/diffs-fluentui-dead-binding.patch
git reset -q
```
Expected: **9 files, 0 phantom deletions, 0 staged files** after the reset. *(Corrected 2026-10-01 after the worker proved the original command emitted 11 phantom `deleted file mode` entries / 4,627 phantom deleted lines.)*

## Plan

### Goal
Close the repo-wide dead multi-select binding in the Students admin: five sites bind `@bind-SelectedValues`, a parameter that does not exist on FluentUI **4.14.2** list components, so each picker is inert (a click never reaches the bound field; the bound field never renders as selected) and the rendered element carries a stray `selectedvalues="System.Collections.Generic.HashSet\`1[System.Guid]"` attribute.

### Authority
- `documents/solution/fluentui-dead-selectedvalues-binding.md` — finding, evidence, fix pattern, detection command.
- `.github/skills/dropdown-ui/SKILL.md` — multi-select routes to a **raw `FluentSelect`** (not the single-select wrapper).
- In-repo working precedent: `Students.Application/Components/Students/TeacherEditDialog.razor:145-148`.

### Sites (all `FluentListbox` + `@bind-SelectedValues`)

| # | File | Bound field |
|---|---|---|
| 1 | `Students.Application/Components/Students/GradeLevelCreateDialog.razor:66` | `Model.TopicIds` (`IReadOnlyList<Guid>`) |
| 2 | `Students.Application/Components/Students/GradeLevelEditDialog.razor:74` | `Model.TopicIds` (`IReadOnlyList<Guid>`) |
| 3 | `Students.Application/Components/Pages/Students/GradeLevels/Create.razor:39` | `_selectedTopicIds` |
| 4 | `Students.Application/Components/Pages/Students/GradeLevels/Edit.razor:52` | `_selectedTopicIds` |
| 5 | `Students.Application/Components/Students/JoinGroupsDialog.razor:36` | `_selectedIds` → `Model.SelectedGroupIds` (`HashSet<Guid>`) |

### Fix pattern (per site)
Keep the domain field as the **single source of truth** and bind the supported pair:

```razor
<FluentSelect TOption="TopicDto" Multiple Items="@_allTopics"
              OptionText="@(t => t.Name)" OptionValue="@(t => t.Id.ToString())"
              SelectedOptions="@SelectedTopicOptions"
              SelectedOptionsChanged="@OnSelectedTopicsChanged" />
```

- `SelectedTopicOptions` is a **computed projection** over the ids field (never a second stored field).
- `OnSelectedTopicsChanged` writes the ids back to the existing field.
- Do **not** use `@bind-SelectedOptions` — it reintroduces a mirrored field that drifts.
- Preserve the existing label/count markup (`Topics (N)`), empty and disabled states, and `Style`/height.
- `JoinGroupsDialog`: the submitted value must remain `Model.SelectedGroupIds`.

### Acceptance criteria (each must fail against the pre-fix code)
1. `grep -rn 'bind-SelectedValues' src/` returns **no** hits.
2. Each site: a selection reaches the bound ids field (assert through the component's changed-handler and/or the model at submit).
3. Each site renders **no** stray `selectedvalues=` attribute and no `fluent-listbox` element.
4. A pre-populated selection renders as **selected** (the Edit/loaded paths).
5. Grade-level create/edit submits the selected topic ids; `JoinGroupsDialog` submits the selected group ids.
6. `dotnet build SchoolCollab.slnx` = 0 errors; affected tests + `SchoolCollab.ArchitectureTests.Unit` = 0 failures.

### Build / test
- `dotnet build SchoolCollab.slnx`
- `dotnet test tests/SchoolCollab.Admin.Tests.Unit` (hosts these components' bUnit suites: `GradeLevelCreateDialogTests`, `GradeLevelEditDialogTests`, `JoinGroupsDialogTests`)
- `dotnet test tests/SchoolCollab.Students.Tests.Unit`
- `dotnet test tests/SchoolCollab.ArchitectureTests.Unit`
- Exactly one pipeline per project, e.g. `dotnet test tests/<Project> 2>&1 | grep -E "^\s*failed |total:|failed:" | head -40`.

### Guards
- **UI-only** — no contract, schema, handler or API change. If one becomes necessary, stop and escalate (the round is mis-tiered).
- bUnit: drive callbacks via `.Instance.*.InvokeAsync(...)` wrapped in `cut.InvokeAsync(...)`; never raw `TriggerEvent(...)`.
- Repo-scoped searches only; never `find /`.
- No commit, push, PR, or edits to this round doc.
- Freeze the patch with the isolation command in the header (the `documents` pathspec keeps the round doc and patch out of the diff).

## Worker Report

Round `fluentui-dead-binding`, Tier 2 (Light, UI-only). Worker `ollama-cloud/deepseek-v4.1-flash`.

- All **five** sites migrated from `FluentListbox` + `@bind-SelectedValues` to a raw multi-select `FluentSelect` with the supported `SelectedOptions` / `SelectedOptionsChanged` pair; each keeps its existing ids field as the **single source of truth** with a **computed** projection; label/count markup (`Topics (N)`), empty/disabled states, `Style` and `Title` preserved. `JoinGroupsDialog` now binds `Model.SelectedGroupIds`; the orphaned `_selectedIds` field and its usages were removed. **UI-only** — no contract, schema, handler or API change.
- Changed: 5 components + 4 test suites (+7 tests) + the frozen patch.
- Worker-reported: build 0 errors; Admin 620/0, Students 619/0, Architecture 73/0; the 4 touched suites 17/0; **pre-fix probe** (the 5 components restored from SNAP) → **7 failed / 10 passed** = exactly the 7 new tests; `grep 'bind-SelectedValues' src/` → prose hits only; no live `FluentListbox`.
- **Patch freeze (Option A — `git add -N` is required; see the header correction):** 9 files, 667 insertions / 52 deletions, **0 `deleted file mode`**, 0 `new file mode`, 0 staged entries after `git reset -q`; a verification re-diff was byte-identical (`cmp`).
- Worker deviations: only the freeze-command correction (owner-ruled Option A). No other scope change.
- Worker residuals: (1) join-groups search-vs-selection; (2) the multi-select `FluentSelect` may render a different box height than the old listbox; (3) join-groups emptiness is enforced by the explicit `Count == 0` guard, not `[Required]` (unchanged); (4) stale gitignored build outputs still contain pre-fix Razor output (not deliverables); (5) one ~110-char comment line per site left unwrapped (patch already frozen); (6) a pre-existing fixture quirk in the two grade-level dialog suites — `RegisterFor` registers method-keyed wildcard URLs that cannot match parameterised paths; the new tests register their own routes rather than changing shared fixtures.

## Review

Reviewer `ollama-cloud/kimi-k2.7-code` (static; never builds). Verdict: **OK with notes — round CLOSED**.

- **Correct** (with file:line): all five sites migrated — `GradeLevelCreateDialog.razor:65/76/170/174`, `GradeLevelEditDialog.razor:72/83/212/216`, `GradeLevels/Create.razor:37/48/98/102`, `GradeLevels/Edit.razor:50/61/118/122`, `JoinGroupsDialog.razor:45/84/88`; `JoinGroupsDialog.razor:183-211` reads and returns `model.SelectedGroupIds` and `grep _selectedIds src/` is empty; no live `@bind-SelectedValues` or `<FluentListbox` in `src/`; no `@bind-SelectedOptions`; tests discriminating and callback-driven via `cut.InvokeAsync(() => picker.Instance.SelectedOptionsChanged.InvokeAsync(...))` (`GradeLevelCreateDialogTests.cs:384`, `GradeLevelEditDialogTests.cs:412`, `GradeLevelCreateEditPageTests.cs:221,272`, `JoinGroupsDialogTests.cs:241`); scope stayed UI-only.
- **P2-1 (pre-existing test hygiene):** `GradeLevelCreateDialogTests.cs:232` is named `Create_Dialog_Submit_BrandNewGrade_AssignsAllPickedSubjects` but submits-then-cancels and asserts a null cancel result; the assignment assertions moved to the new regression test, so the name no longer describes the behaviour.
- **P2-2 (residual risk — runtime-only):** `JoinGroupsDialog.razor:45` binds `Items="@_filteredGroups"` (search-filtered) while `SelectedGroupOptions` at `:84` projects over the full `_availableGroups`. The reviewer **cannot statically prove** whether `FluentSelect` preserves a selection whose option is filtered out of `Items`, or whether the next `SelectedOptionsChanged` round trip drops it; if it drops, searching after selecting silently unselects groups. Flagged as needing a runtime check.
- Best-practices/architecture: raw `FluentSelect` per `dropdown-ui`; one ids field as truth; computed projection; consistent with `TeacherEditDialog.razor:136`; changes confined to the Students Application components and their unit tests.

## Acceptance — **CLOSED, with 2 recorded P2 residuals**

Parent-authoritative pass (tree frozen, no writers):

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** (17 warnings, pre-existing) |
| `dotnet test tests/SchoolCollab.Admin.Tests.Unit` | **620 / 0** |
| `dotnet test tests/SchoolCollab.Students.Tests.Unit` | **619 / 0** |
| `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` | **73 / 0** |
| live `@bind-SelectedValues` in `src/` | **0** (3 hits, all prose comments) |
| patch | 9 files, **0 phantom deletions**, 0 staged, byte-identical re-diff |

- Independent **pre-fix probe**: 7 of the 17 touched-suite tests fail with the five components restored from SNAP (`7 failed / 10 passed`) → the new tests are genuinely discriminating.
- **0 open P1s.**
- **P2-2 — CLOSED (owner chose option (a), 2026-10-01).** Deterministic hidden-selection merge in `JoinGroupsDialog.razor:100-102`: `renderedIds` from `_filteredGroups` → `hidden = Model.SelectedGroupIds.Where(id => !renderedIds.Contains(id))` → `Model.SelectedGroupIds = hidden.Concat(selected ids)`. Correct under **either** `FluentSelect` behaviour and idempotent when it already preserves the passed selection. Regression test `JoinDialog_SearchFiltersOutSelectedGroup_SelectionOfHiddenGroupSurvives`, proven discriminating by a **negative control** (reverting the production method to wholesale replace makes that test fail). Intended consequence: a group hidden by the active search can only be deselected after clearing the search. Parent-verified after the fix: build 0 errors; Admin **621/0**, Students 619/0, Architecture 73/0; patch re-frozen at 9 diffs / 0 phantom deletions.
- **P2-1** (test naming) — cosmetic and pre-existing; recorded for a hygiene sweep.
- **UI-tester pass not dispatched:** the round is UI-only, which `SKILL.md` rules can never promote to Tier 3, and the tester role belongs to Tier 3. The single runtime question is instead put to the owner above.
