# Round — enrollment exceptions move from the page into a dialog

**Provider: pi/clinepass — Tier 2 (light), owner override on a UI round** (models: worker `clinepass/cline-pass/deepseek-v4.1-flash`; reviewer `clinepass/cline-free/mimo-v2.6-flash`, owner-named). Base `main` @ `e9df9240`; tree clean at round start; branch `feat/enrollment-exception-dialog`.

> **Owner override recorded (Tier 2 on a UI round).** The tier table reserves *any* UI round for Tier 3. The owner chose the light tier explicitly ("Lean to tier 2 (light) for go"), recorded as a deviation per the ar-16 precedent. **Consequence: no UI-tester pass** — the delivered UI is not adversarially bug-hunted this round.

---

## 1. Goal

Retire the dedicated `/students/enrollment-exceptions` page. Enrollment exceptions are viewed, added and removed in a new **`EnrollmentExceptionsDialog`**, opened from the **"Enrollment exceptions" kebab action** on the three subject-row surfaces. The dialog is scoped to one owner + one subject; the reader chooses only part / position / span / reason.

## 2. Settled decisions (owner, 2026-09-29 — do not re-open)

| # | Decision |
|---|---|
| D1 | **Retire the page.** Delete `EnrollmentExceptions.razor` + `EnrollmentExceptions.razor.css` and the `@page` route. **No redirect** — the URL 404s (precedent: spec §8 Q5 removed the dead `?periodId=` filter rather than leave a deprecated no-op). |
| D2 | **One dialog, two regions** — the subject's exception list on top, the add form below, both always visible. No mode swap, no `SideDrawer`. |
| D3 | **`DialogShellBase<TModel,TResult>` shape** per `dialog-ui` §1–§2: the add form is the shell's `<EditForm OnValidSubmit="HandleSubmitAsync">`; `SubmitAsync` performs the bulk create and returns **`null` on success** (the dialog stays open — it is a management surface, not a single-shot form) after refreshing the list; `Error` is set only on failure. Footer: `SubmitText="Add exception"`, Cancel label "Close". |
| D4 | **Add-form logic unchanged (v6/v7).** Fill-from division dropdown; Position multi-select 1st–4th + any higher declared position, writing **one row per ticked sequence** through `POST /students/enrollment-exceptions/bulk`; a position the tenant has no period for renders **disabled**; the "Not offered" range belongs to **`Any date` alone**. |
| D5 | **Scope is locked.** Owner + subject come from the call site and render **read-only** in the dialog title. **No owner toggle, no subject picker** — the page's Grade-Level + Subject filter row is exactly the chrome the dialog exists to delete. |
| D6 | **Activity groups reachable.** The dialog accepts a group owner (`OwnerType = ActivityGroup`); the Topics landing already resolves `ActivityGroupId` per row. Group support keeps the existing `FEATURE:EnableActivityGroups` gate **at the call site** (the landing's owner toggle). **No new flag.** |
| D7 | **Kebab rewiring at the call sites only.** All three change `RowAction.Navigate("Enrollment exceptions", url, …)` → `RowAction.Callback("Enrollment exceptions", () => …, FluentIcons.Calendar)`. **`SectionCard.razor` and `SectionCard.razor.css` are NOT touched** — it already takes `Actions` as a parameter. The grade "View all subjects" dialog opens the exceptions dialog **directly from its own kebab**, following its existing precedent (it already opens the Strands/Teachers dialogs from `RowAction.Callback`); `GradeTopicsDialog.ExceptionPageUrlKey` (`Func<Guid,string>`) becomes an `OpenEnrollmentExceptionsKey` (`Func<Guid,Task>`). |
| D8 | **Errors are already handled.** The dialog renders the server's own sentence verbatim — the client's `ServerMessage` extracts `{"message":…}` from a 409/422, which `POST`/`bulk` already return. No new error plumbing. The raw grade-assignment 23505 → typed-409 fix is a **separate** work item, **not this round**. |
| D9 | **Spec v8.** Fold these decisions into `documents/specs/subject-period-exception-model.md` as **v8** — rewrite §5.1/§5.2 to the dialog surface — committed with the implementation. This reverses the 2026-09-29 "do not commit the dialog spec" ruling on the owner's explicit confirmation. |

## 3. Expected files

**New**
- `src/Students/SchoolCollab.Students.Application/Components/Students/EnrollmentExceptionsDialog.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Students/EnrollmentExceptionsDialog.razor.css`

**Modified**
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Students/GradeTopicsDialog.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Subjects/Subjects.razor`
- `documents/specs/subject-period-exception-model.md`
- `tests/SchoolCollab.Admin.Tests.Unit/GradeLevelDetailPageTests.cs`
- `tests/SchoolCollab.Admin.Tests.Unit/SubjectsLandingPageTests.cs`
- `tests/SchoolCollab.Admin.Tests.Unit/TopicDialogsBunitTests.cs` — only if the GradeTopicsDialog kebab is exercised there (check first)

**Renamed + re-targeted**
- `tests/SchoolCollab.Admin.Tests.Unit/EnrollmentExceptionsPageTests.cs` → `EnrollmentExceptionsDialogTests.cs`

**Deleted**
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/EnrollmentExceptions.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/EnrollmentExceptions.razor.css`

Nothing else — **as amended after the review:**
- `src/SchoolCollab.Admin.Shared/Components/Dialogs/DialogShellFooter.razor` — absorbed by the rework so D3's "Close" label becomes deliverable (see §Review, P1);
- `src/Students/SchoolCollab.Students.Application/Components/Students/TopicEditDialog.razor` + `.razor.css` — comment-only repoint of a reference to the D1-deleted page; the reviewer confirmed this is an in-scope consequence of D1, not a scope violation.

`SectionCard.razor(.css)`, `EnrollmentExceptionLabels.cs`, and every API route / endpoint / client method stay untouched.

## 4. Implementation steps

Order is binding; each step must leave the tree compiling.

1. **`EnrollmentExceptionsDialog.razor`** — build it by moving the page's list + add section + the `@code` logic that belongs to them (`SpanIsDerived`, `PositionIsDerivable`, `BuildItems`, `CanAdd`, `DivisionOptions`, the resolved-subject handling, the part/position/span labels, `RemoveAsync`, `AddAsync`, `_addError`/`_loadError` handling). Inputs, via the `DialogShellBase` model: owner type, owner id, owner name, topic id, topic name.
   - **Drop** from the page: the owner toggle + its `_activityGroupsEnabled` gate, the Subject filter row, the "All subjects" option and its states, the `?gradeLevelId=` / `?activityGroupId=` / `?topicId=` query-string preselection (`_loadedQueryKey`), the page `<h1>`/`PageTitle`, and the owner-prompt / no-subject-in-scope empty branches that only existed for "All subjects".
   - The list omits the subject column (the dialog is already one subject).
   - The title renders the scope read-only, e.g. `Enrollment exceptions — Mathematics` (owner name as description).
   - Loads via `Api.ListSubjectEnrollmentExceptionsAsync(<owner>, topicId: <topic>, ct)`; adds via `Api.CreateSubjectEnrollmentExceptionsAsync`; removes via `Api.RemoveSubjectEnrollmentExceptionAsync`.
2. **`EnrollmentExceptionsDialog.razor.css`** — carry forward only the rules the dialog still uses from the page's CSS (`.exceptions-section`, `.section-head`, `.section-title`, `.exceptions-list`, `.exception-row`, `.exception-topic`, `.exception-span`, `.exception-reason`, `.exception-remove`, and the add-form block classes). **Merge, never blanket-overwrite** (`dialog-ui` §3): grep the new markup for every class you keep, and drop rules whose class no longer appears.
3. **`Detail.razor`** — delete `ExceptionPageUrl` (≈ line 323); add `OpenEnrollmentExceptionsAsync(GradeTopicCurriculumDto topic)` opening the dialog with `OwnerType = GradeLevel`, `OwnerId = Id`, `OwnerName = _grade!.Name`, the topic id/name, then `await ReloadExceptionsAsync()` on close; change `BuildTopicRowActions` (≈ line 881) `Navigate` → `Callback`; replace the `GradeTopicsDialog.ExceptionPageUrlKey` parameter (≈ line 748) with the new `OpenEnrollmentExceptionsKey`.
4. **`GradeTopicsDialog.razor`** — replace the `ExceptionPageUrlKey` const + `ExceptionPageUrl` property (≈ lines 80, 129) with `OpenEnrollmentExceptionsKey` and `Func<Guid,Task> OpenEnrollmentExceptions`; change `BuildRowActions` (≈ line 146) `Navigate` → `Callback`; update the file-header comment (it currently says the kebab navigates to a page).
5. **`Subjects.razor`** — replace `ExceptionPageUrl` (≈ lines 459–470) with `OpenEnrollmentExceptionsAsync(SubjectDto row)` keeping the same grade-**or**-group owner resolution and the "no resolvable owner ⇒ no action" gate, then `await LoadSubjectsAsync(...)` so the column badge refreshes; change the kebab (≈ line 520) `Navigate` → `Callback`.
6. **Re-target the tests** — rename `EnrollmentExceptionsPageTests.cs` → `EnrollmentExceptionsDialogTests.cs` and re-point it at the dialog. **Keep and adapt** every test whose subject survives: list rendering (part + span + open bounds), the surviving empty states, add/`bulk` write + re-read, the server-message surface on a 409, `ChosenPart_*`, the disabled-position / range rules, `Positions_*`, `TermShapedAndPlainWindowRows_*`, `OpenEnds_*`, `PickersCheck_*`, `FailedAdd_*`, accessibility names. **Delete** only the tests whose subject no longer exists — `OwnerToggle_*`, `AllSubjects_*`, `QueryString_*`, `LandingWithNoQuery_*`, `ThereIsExactlyOneSubjectControl_*`, `FilteredEmpty_*`, `NoTopicInTheQuery_*`, `PageOffersNoSaveControl` — and **list every deletion with its reason in the worker report**. Never delete a test to make a build pass; if a kept test fails, fix the code or report the deviation.
7. **Re-decide the two guard tests, intent-preserving** — `GradeLevelDetailPageTests.cs:1058-1066` and `SubjectsLandingPageTests.cs:560,589-592` currently assert `RowAction.Navigate("Enrollment exceptions"` + the `/students/enrollment-exceptions?…` URL. They must now assert `RowAction.Callback("Enrollment exceptions"`, **not** `Navigate`, plus the dialog call site. Their intent — *"no inline editor on the card; the affordance opens the editor"* — is preserved, **not relaxed** (the same move the v3 round recorded).
8. **Spec v8** — rewrite §5.1/§5.2 of `subject-period-exception-model.md`: the dialog replaces the page; the three entry points open it; scope is owner + subject, locked; record the retired page and its states as retired (following §5.3's precedent), and state explicitly that §11.7's v7 rules carry into the dialog unchanged.
9. **Build + tests** — `dotnet build SchoolCollab.slnx` (0 errors), then `dotnet test tests/SchoolCollab.Admin.Tests.Unit`.

**Cut-line protocol.** If you approach the 30-minute cap: finish the current step to a compiling state, write your progress (steps done / missing / in-flight compile state) to `documents/rounds/.eed-worker-report.md` (scratch, untracked — **never** this round doc), and stop cleanly. A remainder worker resumes from that file plus this plan.

## 5. Acceptance criteria

| # | Criterion | Discriminating check |
|---|---|---|
| A1 | `EnrollmentExceptions.razor(.css)` gone; no `@page "/students/enrollment-exceptions"` route remains | file list + `grep -rn '@page "/students/enrollment-exceptions"'` → empty |
| A2 | All three surfaces call `RowAction.Callback("Enrollment exceptions"` and no `RowAction.Navigate` for it | the two re-decided guard tests |
| A3 | `SectionCard.razor(.css)` unchanged | `git diff --name-only` excludes them |
| A4 | The dialog renders the subject's existing exceptions (part badge + span + reason + remove) | re-targeted list tests |
| A5 | Adding N ticked positions writes N rows through `/bulk` in ONE call and refreshes the list | re-targeted `Add_CarriesTheChosenPartAndPosition_ToTheServer`, `Add_WritesTheExceptionImmediately_AndRereadsTheList` |
| A6 | An unbacked position is disabled; the range renders only on `Any date` | re-targeted `PositionsWithoutAPeriod_*`, `ChosenPart_*` |
| A7 | A server 409 surfaces its own sentence verbatim | re-targeted `DuplicateRejectedByTheServer_SurfacesTheServerMessage` |
| A8 | Both hosts reload their count badges after the dialog closes | `ReloadExceptionsAsync` in `Detail.razor`; `LoadSubjectsAsync` in `Subjects.razor` |
| A9 | Spec v8 records the dialog surface | spec diff |
| A10 | `dotnet build SchoolCollab.slnx` 0 errors; Admin unit suite 0 failures | worker + parent run |

## 6. Constraints

- Repo-scoped searches only (`src/`, `tests/`, `documents/`) — **never** `find /` or a filesystem-wide scan.
- No new NuGet package; CPM — never a `Version` on a `PackageReference`.
- No API / endpoint / migration / MassTransit contract change. No new feature flag.
- Honour `dialog-ui` (§1 shell, §2 `border-top` separator, §3 merge-not-overwrite on `.razor.css`, §4 no nested `<EditForm>`), the repo CSS-isolation rule, and `dotnet-best-practices`.
- Read the code you touch; on ambiguity open only the specs this plan cites.
- **Never edit this round doc.** Your self-report goes to `documents/rounds/.eed-worker-report.md`.
- Test-output rule: one `dotnet test tests/SchoolCollab.Admin.Tests.Unit 2>&1 | grep -E "^\s*failed|total:|failed:" | head -40` per read, at most 2 attempts; if truncated, redirect to a file once and read the tail.

## 7. Worker task spec

Implement exactly §4, in order. Return the WORKER REPORT block only:

```
WORKER REPORT
Changed files: <path list>
Build: <"0 errors" | "n errors" + one-line detail each>
Tests: <project: n passed, m failed | "not run" + why>
Deleted tests: <test name — reason>   (one per line; "none" if empty)
Deviations from plan: <none | one line each>
```

---

## Worker Report

> **Reconstructed by the parent from the live tree.** The worker run was aborted by a harness
> interruption after step 8 (spec v8 complete), so it never wrote its own report and no
> `documents/rounds/.eed-worker-report.md` scratch file exists. Every line below is read off the
> frozen patch, not self-reported by the worker. Step 9 (build + tests) was **not** completed by the
> worker — the parent ran the authoritative pass in its place.

| Field | Value |
|---|---|
| New files | `Components/Students/EnrollmentExceptionsDialog.razor` (767), `EnrollmentExceptionsDialog.razor.css` (143), `tests/.../EnrollmentExceptionsDialogTests.cs` (1004) |
| Modified | `Detail.razor` (+61), `Subjects.razor` (+68), `GradeTopicsDialog.razor` (26), `TopicEditDialog.razor` (4), `TopicEditDialog.razor.css` (4), `GradeLevelDetailPageTests.cs` (21), `SubjectsLandingPageTests.cs` (33), `documents/specs/subject-period-exception-model.md` (167) |
| Deleted | `Pages/Students/EnrollmentExceptions.razor` (1306), `EnrollmentExceptions.razor.css` (212), `tests/.../EnrollmentExceptionsPageTests.cs` (renamed to the dialog test file) |
| Build (parent) | `dotnet build SchoolCollab.slnx` — **0 errors**, 17 warnings (all pre-existing; down from 64 because the retired page's warnings went with it) |
| Tests (parent) | `Admin.Tests.Unit` **581 passed / 0 failed**; `ArchitectureTests.Unit` **72 passed / 0 failed** |
| Deleted tests | **15**, all covering retired chrome — `AllSubjects_*` (2), `OwnerToggle_*` (2), `ChangingOwner_ResetsTheAddSection`, `PageOffersNoSaveControl`, `LandingWithNoQuery_*`, `QueryString_*` (5), `FilteredEmpty_*`, `NoTopicInTheQuery_*`, `ThereIsExactlyOneSubjectControl_*`. 33 → 18 tests; the 18 kept cover the list, the surviving empty states, the bulk write + re-read, the 409 sentence, the Fill-from/position rules, the disabled-unbacked-position rule, the Any-date-only range, the picker check and the accessibility names. |
| Deviations from plan | (1) **`TopicEditDialog.razor` + `.razor.css` were touched** — 2 comment lines each, repointing a now-false reference to the deleted page; they are **not** in §3's expected-files list (flagged to the reviewer as a scope question). (2) **No worker self-report** (harness abort). |

---

## Rework — Tier-2 iteration 1 (the one permitted)

The reviewer's P1 was a **plan-feasibility miss**: D3 pinned the footer's Cancel label to "Close",
but `DialogShellFooter` had **no** `CancelText` parameter — the label was hard-coded to `Cancel`. The
light tier runs no plan-review pass, so nothing caught the unachievable clause before dispatch; it
surfaced in the diff review, which is one gate later than it should have been.

| Item | Fix |
|---|---|
| **P1** | `DialogShellFooter.razor` gained `[Parameter] public string CancelText { get; set; } = "Cancel";` (documented beside `SubmitText`) and now renders `@CancelText`; **the default preserves every existing shell dialog**. `EnrollmentExceptionsDialog.razor:217` passes `CancelText="Close"`. |
| **P2 ×2** | `Subjects.razor` ≈88 and ≈368 — the two comments that still claimed the kebab *navigates* were reworded to *opens the `EnrollmentExceptionsDialog`*. |
| **P2 ×3 (deliberately not fixed)** | `EnrollmentExceptionRoutes.cs:20`, `StudentsApiClient.cs:1698`, `EnrollmentExceptionLabels.cs:7` still say "management page". Frozen by §3 → **report-only**, recorded under §Residual. |

**Rework worker report** (`clinepass/cline-pass/deepseek-v4.1-flash`): changed files = the three above; `dotnet build SchoolCollab.slnx` **0 errors** (forced `-t:Rebuild` also 0 errors / 100 warnings, all pre-existing); `Admin.Tests.Unit` **581 passed / 0 failed** (run twice, once after the forced rebuild); deviations: **none**.

---

## Review

Static reviewer `clinepass/cline-free/mimo-v2.6-flash` — read-only; never builds, never tests, never writes files.

### Diff review #1 — verdict **P1**

```
REVIEW
Verdict: PASS
P1: none
P2: src/Students/SchoolCollab.Students.Api/Endpoints/EnrollmentExceptionRoutes.cs:20 — "mirroring the owner toggle on the management page" still false post-D1; confirmed unchanged (absent from the re-frozen patch's 15 `diff --git` entries and from the working delta) — deliberate deferral per plan §3, report-only follow-up
P2: src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs:1698 — "the management page's owner toggle" still false; confirmed unchanged, same deliberate deferral
P2: src/Students/SchoolCollab.Students.Application/Components/Students/EnrollmentExceptionLabels.cs:7 — "the management page's own list badge" still false; confirmed unchanged, same deliberate deferral
Best-practices: no overwrites (rework = 3 files: one optional parameter + label swap + attribute at call site + 2 comment lines); skills honored (dialog-ui shared footer, parameter XML-doc'd beside `SubmitText`/`SavingText` in the same style); readable
```

**Evidence for the re-check.**

- **(a) P1 closed.** `DialogShellFooter.razor:81` — `[Parameter] public string CancelText { get; set; } = "Cancel";`; `:38` — renders `@CancelText` (was the literal `Cancel`); `:17-20` — documented in the `Parameters:` block directly after `SubmitText`. `EnrollmentExceptionsDialog.razor:217` passes `CancelText="Close"` → the delivered label is "Close". Default preserved: every other `DialogShellFooter` call site (~20 repo-wide) passes no `CancelText` and so still renders exactly "Cancel". (`StudentEditDialog.razor:76`'s `CancelText="Close"` binds `DialogDrawer`, its own parameter — pre-existing, unrelated.)
- **(b) No regression.** Optional + defaulted ⇒ no call site broken. Working delta = exactly the same 15 files, `+2185/−3321`, file list identical to the re-frozen patch; the rework artifacts appear only in the three named files.
- **(c) Comments now true** — `Subjects.razor:87-89` and `:366-368` verified against `BuildRowActions`'s `RowAction.Callback("Enrollment exceptions", () => OpenEnrollmentExceptionsAsync(row), …)`.
- **(d) Deferrals intact** — the three §3-frozen files are absent from both the patch and the working delta.

---

## Acceptance

**Verdict: CLOSED.** Tier 2 — the parent adjudicates the REVIEW and transcribes the verdict (the worker's self-reported numbers are superseded by the parent's authoritative pass, which is the only source of truth).

| # | Criterion | Result |
|---|---|---|
| A1 | Page + CSS deleted; no route | ✅ the patch deletes `EnrollmentExceptions.razor` (1306) and `.razor.css` (212); `grep -rn '@page "/students/enrollment-exceptions"' src/` → empty |
| A2 | All three surfaces call `Callback`; no `Navigate` | ✅ tracked source: `RowAction.Callback("Enrollment exceptions"` ×3 — `Detail.razor:920`, `Subjects.razor:560`, `GradeTopicsDialog.razor:148`; `Navigate` ×0 |
| A3 | `SectionCard.razor(.css)` unchanged | ✅ absent from the 15-file change set |
| A4 | The dialog renders the subject's exceptions (part + span + reason + remove) | ✅ the 18 re-targeted tests pass |
| A5 | N ticked positions → N rows through `/bulk`, in one call, list refreshed | ✅ re-targeted `Add_CarriesTheChosenPartAndPosition_ToTheServer`, `Add_WritesTheExceptionImmediately_AndRereadsTheList` |
| A6 | An unbacked position is disabled; the range renders only on `Any date` | ✅ re-targeted `PositionsWithoutAPeriod_*`, `ChosenPart_*` |
| A7 | A server 409 surfaces its own sentence verbatim | ✅ re-targeted `DuplicateRejectedByTheServer_SurfacesTheServerMessage` |
| A8 | Both hosts reload their count badges after close | ✅ `ReloadExceptionsAsync` (Detail.razor) and `LoadSubjectsAsync` (Subjects.razor), verified by the reviewer |
| A9 | Spec v8 records the dialog surface | ✅ `subject-period-exception-model.md` §5 is now "Surfaces — one management dialog, thin entry points", D1–D9 recorded, the retired page in §5.3 |
| A10 | `dotnet build SchoolCollab.slnx` 0 errors; unit suites 0 failures | ✅ **0 errors** (17 warnings, all pre-existing); `Admin.Tests.Unit` **581 / 0**; `ArchitectureTests.Unit` **72 / 0** |

**Review path:** diff review #1 → **P1** (the footer's Cancel label) → the one permitted Tier-2 rework iteration → re-check → **PASS**, no P1.

---

## Residual

1. **Three stale doc comments** still say "the management page" — `EnrollmentExceptionRoutes.cs:20`, `StudentsApiClient.cs:1698`, `EnrollmentExceptionLabels.cs:7`. Deliberately **not** fixed, to keep the diff inside §3's scope; the reviewer confirmed all three are untouched. → follow-up.
2. **No UI-tester pass.** The owner override ran this UI round at Tier 2, so rendered geometry, focus behaviour, the dialog's open/close against a real browser, and the footer's state after a successful add are **not** verified. Every UI claim above is static (build + bUnit DOM assertions).
3. **Orphaned generated Razor files** under `obj/…/RazorSourceGenerator/` still contain the pre-round `RowAction.Navigate` + `ExceptionPageUrl` text. They are gitignored, absent from the change set, and demonstrably not compiled (the build is green with `ExceptionPageUrl` deleted from source). **A repo-wide `grep` must exclude `/obj/`** or it will read as an A2 failure — it did, once, during this acceptance.
4. **Process note — the P1 was a plan defect, not a code defect.** D3 pinned a label the shared component could not render. At Tier 3, step 2b's plan-review pass exists precisely to catch that before dispatch; with no plan gate, the light tier pays for it one gate later, in the diff review. Recorded as the concrete cost of the owner's Tier-2 override.
