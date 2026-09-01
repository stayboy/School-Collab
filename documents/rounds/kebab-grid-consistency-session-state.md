# Session State � Kebab Menu Grid Consistency

## Completed
1. Added `ForceKebab` parameter and static `HasKebabActions` helper to `RowActionsMenu.razor`.
2. Updated `LandingPage.razor` to compute `_forceKebab` and pass it to every row's `RowActionsMenu`.
3. Updated `PeriodSubPeriodsEditor.razor` to force kebab when any sub-period is Draft.
4. Updated `TestLandingPage.razor` with `RowActionsUseMenuService` parameter wired to `LandingPage`.
5. Added `using SchoolCollab.Admin.Shared.Constants` to `LandingPageTests.cs` and `RowActionsMenuTests.cs`.
6. Added `RowActionsMenuTests.cs` (5 tests) and new tests to `LandingPageTests.cs` (2 tests) and `PeriodEditPageTests.cs`.
7. Wired `rowActionsUseMenuService = false` default in `RenderWrapper` to avoid `FluentMenuProvider` requirement in bUnit kebab path.

## Verification Results
- `dotnet build SchoolCollab.sln` � succeeded, 0 errors.
- `RowActionsMenuTests` � 5 passed.
- `LandingPageTests` � 16 passed.
- `LandingPageOnCreateTests` � 3 passed.
- `PeriodEditPageTests` � 14 passed.
- `SubPeriodsPageTests` � 3 passed.
- `EntityGridTests` � 2 passed.
- `PeriodsLandingGridTests` � 26 passed.
- `PeriodCreatePageTests` � 1 passed.
- `SchoolCollab.Students.Tests.Unit` � 394 passed.
- Full `SchoolCollab.Admin.Tests.Unit` run � **529 passed, 0 failed (43s)**. The earlier 30s timeout was a tool timeout, not a test failure; the suite passes when given enough time.
- Filtered re-run (RowActionsMenuTests + LandingPageTests + PeriodEditPageTests) � 35 passed.

## Cleanup
- Removed stray `nul` and `out_unit.txt` artifacts from the working tree.

## Files Modified
- src/SchoolCollab.Admin.Shared/Components/Landing/LandingPage.razor
- src/SchoolCollab.Admin.Shared/Components/RowActionsMenu.razor
- src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodSubPeriodsEditor.razor
- tests/SchoolCollab.Admin.Tests.Unit/LandingPageTests.cs
- tests/SchoolCollab.Admin.Tests.Unit/PeriodEditPageTests.cs
- tests/SchoolCollab.Admin.Tests.Unit/TestLandingPage.razor
- tests/SchoolCollab.Admin.Tests.Unit/RowActionsMenuTests.cs (new)

## Next Steps When Resuming
1. **DONE** � Full `SchoolCollab.Admin.Tests.Unit` suite passes (529/529, 43s). No batching needed; the earlier timeout was a tool timeout.
2. **Verified** � `SubPeriodsSection_AnyDraftRow_ForcesKebabOnEveryRow` selector `fluent-button[title='Sub-period actions']` matches the editor's `AriaLabel="Sub-period actions"` on `RowActionsMenu`. Stable unless that label changes.
3. **Left as-is** � `WaitForAssertion` in `LandingPageTests.RowActions_AnyRowHasKebab_ForcesKebabOnEveryRow` is defensive and harmless; kept for robustness.
4. Commit/push per repo policy only after user explicitly requests.
