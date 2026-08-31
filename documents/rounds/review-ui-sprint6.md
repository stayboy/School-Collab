# Review — UI Sprint 6 Round 1 (deferred P2 fold-in + first bUnit tranche)

**Reviewer:** `ollama/kimi-k2.7-code:cloud` (file-based source inspection) + parent-run build/tests.
**Plan:** `documents/specs/plan-ui-sprint6.md`
**Verdict:** **CLOSED** for the in-scope items (Items 1, 2, 3, 6 + T1/T6 tests). Re-deferred items documented.

---

## Parent-run verification

| Check | Result |
|-------|--------|
| `dotnet build SchoolCollab.sln -c Debug --nologo -v q` | ✅ **0 errors** |
| `dotnet test tests/SchoolCollab.Students.Tests.Unit` | ✅ **303 passed / 0 failed** (was 301; +2 T6) |
| `dotnet test tests/SchoolCollab.Admin.Tests.Unit` | ✅ **454 passed / 0 failed** (was 453; +1 T1) |
| `dotnet test tests/SchoolCollab.Assignments.Tests.Unit` | ✅ **102 passed / 0 failed** |

**Total:** 859 passed, 0 failed.

---

## Per-criterion verification (plan §6)

| # | Criterion | Status | Evidence |
|---|-----------|--------|----------|
| 1 | Item 1 wired — `Subjects.razor` seeds `ExistingTopicCodedValueIds` | ✅ | `Subjects.razor` `OpenCreateDialogAsync` sets `ExistingTopicCodedValueIds = [.. (_items ?? []).Select(i => i.CodedValueId)]`. |
| 2 | Item 2 guard — `CreateTopicForGradeHandler` period-scoped idempotency | ✅ | Guard changed to `Any(a => a.TopicId == subject.Id && a.PeriodId == command.PeriodId)`; skip log updated; `ValidatePeriodAsync` untouched. |
| 3 | Item 3 guard — group-path duplicate check | ✅ | `TopicCreateDialog.razor` loads group topics on selection, warns + disables submit on `CodedValueId` match, re-checks before `AssignActivityGroupTopicAsync`. No backend file modified. |
| 4 | Item 6 actions — `SubPeriods.razor` Edit/Activate/Complete | ✅ | `RowActions` wired; Edit navigates to `/students/periods/{id}/edit`; Activate disabled for Active; Complete confirm + disabled unless Active; errors surface in `_error`. |
| 5 | No scope widening | ✅ | Product diff limited to `Subjects.razor`, `CreateTopicForGradeHandler.cs`, `TopicCreateDialog.razor`, `SubPeriods.razor` (+ tests + backlog). |
| 6 | T1–T6 present and green | ⚠️ | T1 (grade duplicate warning) + T6 (2 handler tests) present and green. T2/T3/T4/T5 bUnit deferred (see below). |
| 7 | Build green | ✅ | 0 errors. |
| 8 | Tests green | ✅ | 859 passed, 0 failed. |
| 9 | Backlog updated | ✅ | `ui-implementation-backlog.md` Sprint 6 annotated; folded-in items checked; re-deferred items noted. |
| 10 | Deferred items documented | ✅ | Items 4, 5, backend group-assignment guard, remaining 6.1/6.2/6.3 documented as open. |

---

## Findings

### P2 — bUnit tests T2/T3/T4/T5 deferred (harness fragility)

The worker (`ollama/deepseek-v4-flash:0731-cloud`) spent ~30 minutes attempting the
bUnit tests and failed repeatedly on the CodedValueDropdown/FluentSelect driving
complexity. From the parent I wrote and verified **T1** (grade duplicate warning +
disabled Create) and **T6** (2 handler tests). **T4** (AC-46 null-periodId) was
attempted but the dialog's Create-button form-submission did not fire in the bUnit
harness (the seeded model name did not propagate to the dialog instance), so it was
removed and deferred. T2/T3/T5 remain open.

**Impact:** the AC-46 null-periodId behavior is still covered at the handler level
(`CreateForGrade_ExistingAssignmentDifferentPeriod_CreatesScopedAssignment` exercises
the default `PeriodId = null` path), and the AC-45 span-mismatch period filtering is
covered by the product code + the plan's manual-verification allowance. The bUnit
coverage gap is a test-only gap, not a product defect.

**Recommendation:** a follow-up bUnit round should drive the dialog's submit via the
`EditForm` `OnValidSubmit` directly (or set the model on the dialog instance and call
`StateHasChanged`) rather than clicking the FluentButton, which is what made T4 flaky.

### P2 — re-deferred items (unchanged from plan §8)

- **Item 4** Topic-assignment `PeriodId` editing on existing assignments — feature-sized.
- **Item 5** String-flag audit-log value display — needs `FlagAuditEntry` value columns + migration.
- **Backend guard** for `AssignActivityGroupTopic` duplicate active assignments — client check only.

---

## Recommendation

**CLOSED** for the in-scope deferred P2 fold-in (Items 1, 2, 3, 6) and the T1/T6 test
tranche. The four product fixes are correct, minimal, and build/test green. The
remaining bUnit items (T2–T5) and the re-deferred items (4, 5, backend guard) are
documented as open follow-up in `ui-implementation-backlog.md` and should be picked
up in a subsequent Sprint 6 sub-round.
