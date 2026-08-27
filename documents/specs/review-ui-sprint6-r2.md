# Review — UI Sprint 6 Round 2 (remaining bUnit + 6.3 polish)

**Reviewer:** `ollama/kimi-k2.7-code:cloud` (file-based source inspection) + parent-run build/tests.
**Plan:** `documents/specs/plan-ui-sprint6-r2.md`
**Verdict:** **CLOSED** for the in-scope Round 2 items (T2/T3/T4/T5/T7 bUnit + division client + 6.3a states). Residual items documented.

> **Note on reviewer P0 findings:** the reviewer flagged a P0 compile error in
> `TopicCreateDialogTests.cs` (`OnValidSubmit.InvokeAsync()` missing the
> `EditContext` argument) and missing `SubPeriodsPageTests.cs` /
> `AcademicYearDivisionSettingTests.cs`. Both were **stale** — the worker had
> already fixed the compile issue (passing `editForm.Instance.EditContext`) and
> created both test files before the reviewer's inspection. Parent-run build +
> tests confirm all green.

---

## Parent-run verification

| Check | Result |
|-------|--------|
| `dotnet build SchoolCollab.sln -c Debug --nologo -v q` | ✅ **0 errors** |
| `dotnet test tests/SchoolCollab.Students.Tests.Unit` | ✅ **303 passed / 0 failed** |
| `dotnet test tests/SchoolCollab.Admin.Tests.Unit` | ✅ **464 passed / 0 failed** (was 454; +10) |
| `dotnet test tests/SchoolCollab.Assignments.Tests.Unit` | ✅ **102 passed / 0 failed** |

**Total:** 869 passed, 0 failed.

---

## Per-criterion verification (plan §7)

| AC | Criterion | Verdict | Evidence |
|----|-----------|---------|----------|
| 1 | T3 present & green (AC-45) | ✅ | `TopicCreateDialogTests.cs` T3a/T3b assert rendered period options/hint; pass. |
| 2 | T4 present & green (AC-46) | ✅ | `TopicCreateDialogTests.cs:335` T4 drives `EditForm.OnValidSubmit` (not FluentButton click); pass. |
| 3 | T2 present & green | ✅ | `TopicCreateDialogTests.cs:287` T2 asserts warning + disabled Create + no POST; pass. |
| 4 | §5.1 micro-fix | ✅ | `TopicCreateDialog.razor:246` fires `_ = LoadGroupExistingTopicsAsync()` when `Model.ActivityGroupId` pre-seeded. |
| 5 | Division setting wired | ✅ | `ConfigFlagsApiClient.cs` GET/PUT `/api/config/flags/academic_year_division` + `message` extraction; `ConfigFlagDetail.razor` division card (loading, error, value/source, select+reason+save, reload). |
| 6 | 6.3a states | ✅ | `SubPeriods.razor` + `Periods.razor` loading/error/empty/ErrorBoundary; locked by `SubPeriodsPageTests.cs`. |
| 7 | No scope widening | ✅ | Product changes limited to `TopicCreateDialog.razor` (1 line), `ConfigFlagsApiClient.cs`, `ConfigFlagDetail.razor`, `SubPeriods.razor` + tests. |
| 8 | Tests green | ✅ | 869 passed, 0 failed. |
| 9 | Build green | ✅ | 0 errors. |
| 10 | Docs updated | ✅ | `ui-implementation-backlog.md` §6.1/6.3 updated with checkmarks + residual follow-ups. |

---

## Findings

### Correct (verified)

- **T2/T3/T4 use the working submit approach** — they drive `EditForm.OnValidSubmit`
  (passing `EditContext`) and `FluentSelect<string>.SelectedOptionChanged`, never
  clicking a `FluentButton`. This resolves the Round 1 harness-fragility issue.
- **Division client + UI wired correctly** — GET/PUT academic_year_division, server
  `message` surfaced verbatim on non-success, division card with loading/error/value/
  source/select+reason+save/reload.
- **6.3a states present** — `SubPeriods.razor` loading ring, error bar + Back,
  EmptyMessage, ErrorBoundary; `Periods.razor` LandingPage Loading/Error/EmptyMessage.

### Residual (deferred to a later sub-round)

- **bUnit AC-35..43** (span-aware create/edit dialog validation) — still open.
- **bUnit AC-38/43** (rollover / next-window UI) — still open.
- **bUnit `PeriodType` + parent selector validation** — still open.
- **6.2 Playwright smoke** — still open.
- **Item 4** (PeriodId editing), **Item 5** (string-flag audit), **backend
  `AssignActivityGroupTopic` duplicate guard** — re-deferred (unchanged).

---

## Recommendation

**CLOSED** for the in-scope Round 2 items. The bUnit tranche (T2/T3/T4/T5/T7) and the
division client + 6.3a states are implemented, correct, and build/test green. The
remaining Sprint 6 items (AC-35..43, AC-38/43, PeriodType selector, 6.2 Playwright,
and the re-deferred Items 4/5 + backend guard) are documented as open follow-up in
`ui-implementation-backlog.md`.
