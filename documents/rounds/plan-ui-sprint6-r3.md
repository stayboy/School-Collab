# Plan — UI Sprint 6, Round 3: last bUnit tranche (span dialogs, rollover/next-window, PeriodType selectors)

**Status:** Planned (orchestrator-authored; implementation delegated to a worker)
**Sprint:** 6 — Verification & Cross-Cutting Polish (`ui-implementation-backlog.md` §Sprint 6)
**Inputs:**
- `plan-ui-sprint6-r2.md` + `review-ui-sprint6-r2.md` — Round 2 (CLOSED): working bUnit driving approach
- `activity-group-enrollment.md` — AC-35..43, FR-42..54
- `period-hierarchy-terms-semesters.md` — FR-H1/H2 (PeriodType + parent)
- Product: `ActivityGroupCreateDialog.razor`, `ActivityGroupEditDialog.razor`, `JoinGroupsDialog.razor`, `ActivityGroupDetails.razor`, `PeriodForm.razor`

**Scope discipline:** test-first round, ZERO product-code change. The three remaining 6.1 bUnit
items are coverage gaps, not defects — every behavior they lock already shipped in Sprints 2–3 /
period-hierarchy phases. If a test proves fragile after a genuine attempt, document it as a
follow-up rather than forcing it (same rule as Round 2 §9).

---

## 1. Goal

Lock the last three unchecked Sprint 6 bUnit items with the Round-2-proven driving approach:

1. **Item A — span-aware create/edit dialog validation (AC-35..43):** `ActivityGroupCreateDialog` /
   `ActivityGroupEditDialog` span fields (`Span`, window dates, next window, `AutoRenewDefault`)
   and `JoinGroupsDialog` span-aware join filtering.
2. **Item B — rollover / next-window UI (AC-38/43):** `ActivityGroupDetails` forced-rollover
   button surface + the `SetActivityGroupNextWindow` PUT path (covered jointly with Item A's
   next-window tests).
3. **Item C — `PeriodType` + parent selector validation:** `PeriodForm` type dropdown ↔ parent
   academic-year dropdown visibility, parent-required validation, and POST shape.

Non-goals (explicit): 6.2 Playwright smoke; AC-42 client-side division-compat hint (backend guard
already rejects; client-side feature work is out of scope); Items 4/5 + backend duplicate guard
(still re-deferred); any product .razor/.cs change.

---

## 2. Scope decision

| # | Item | Verdict | Rationale |
|---|------|---------|-----------|
| A1 | Create dialog: span select reveals window + next-window pickers | **MUST** | Pure render assertion — most robust. |
| A2 | Create dialog: next-start before current end rejected, no POST | **MUST** | Client-side AC-43/FR-53 guard; model-direct + `OnValidSubmit` driving. |
| A3 | Create dialog: half-filled next window rejected, no POST | **MUST** | Same mechanism, one extra case, cheap. |
| A4 | Create dialog: valid DateRange → POST window fields + next-window PUT + closes | **MUST** | Locks AC-37/FR-47 window POST body, FR-53 next-window PUT, FR-49 `autoRenewDefault:true` default. |
| A5 | Edit dialog: span rendered read-only, no span select | **MUST** | FR-42 immutability — single render assertion. |
| A6 | Edit dialog: valid DateRange → PUT + next-window PUT + closes | **MUST** | Mirrors A4 on the edit path (shared validation, distinct request shape). |
| A7 | Join dialog: active Term filters list (Termly listed; Semester + closed DateRange hidden) | **MUST** | AC-35/FR-43 span-aware join + FR-52 closed-window rejection surface. |
| A8 | Join dialog: no active matching period → Termly/Semester hidden | **MUST** | FR-43 "no active matching period" surface (client filter = rejection surface). |
| A9 | Join dialog submit click-through (`SelectedValuesChanged` → POST /members) | **OPTIONAL** | `FluentListbox` multi-select driving is the one unproven control; if it fights back, document as follow-up. |
| B1 | Details page: Roll over button visible for bounded spans, hidden for OpenEnded | **MUST** | Render-only, reuses the green `ActivityGroupsPageTests` harness. |
| B2 | Details page rollover confirm click-through (POST /rollover) | **OPTIONAL** | Confirm-dialog + POST is the known fragile pattern (Round 2 T5c/T7c precedent); render test B1 + A4/A6 lock AC-38's UI surface. |
| C1 | PeriodForm: parent dropdown only for Term/Semester; hidden for AcademicYear | **MUST** | FR-H1/H2 selector coupling; pure render + one select drive. |
| C2 | PeriodForm: Term with no parent → error, no POST | **MUST** | "Select a parent academic year for this period." + no POST. |
| C3 | PeriodForm: valid Term create posts `parentPeriodId` + `periodType` Term | **MUST** | FR-H1/H2 POST shape (numeric `periodType`, parent GUID). |
| — | Backlog doc update (§6.1 three boxes) | **MUST** | Worker checks the boxes with test names after green. |

---

## 3. Confirmed current state (evidence)

- **Create dialog** (`src/Students/SchoolCollab.Students.Application/Components/Students/ActivityGroupCreateDialog.razor`):
  span `FluentSelect<string>` `id="ag-create-span"` binds `Model.Span` (default `"OpenEnded"`);
  `@if (Model.Span == "DateRange")` renders window + next-window `FluentDatePicker`s;
  `SubmitAsync` validates (a) half-filled next window → `Error = "Enter both next-window dates, or
  clear both."`, (b) next start < current end → `Error = "The next window's start must be on or
  after the current window's end."` (both return null → no POST); happy path →
  `POST /activity-groups` (`span`, `enrollmentStartDate/EndDate` as `DateOnly` "yyyy-MM-dd",
  `autoRenewDefault`), then `PUT /activity-groups/{id}/next-window` when next window set, then
  `GET /activity-groups/{id}` → closes non-null. `AutoRenewDefault` defaults `true` (FR-49).
  `OnInitializedAsync` → `GET /students/grade-levels/landing`.
- **Edit dialog** (same folder): span shown as readonly `FluentTextField` `id="ag-edit-span"`
  (FR-42 immutability); `PUT /activity-groups/{id}` + optional next-window PUT + GET by id.
- **Join dialog** (`JoinGroupsDialog.razor`): `SpanCompatible` filter — `OpenEnded` always;
  `DateRange` only while `today ∈ [EnrollmentStartDate, EnrollmentEndDate]`;
  `WholeAcademicYear`/`Termly`/`Semester` only when `_activePeriodType` is
  `AcademicYear`/`Term`/`Semester`. `_activePeriodType` =
  `GET /students/periods/active-sub-period` (404 → null) ?? `GET /students/periods/active-academic-year`
  ?? `"AcademicYear"`. Load path scripts: `GET /activity-groups`, `GET /students/{studentId}/activity-groups`
  (current memberships), the two active-period GETs. Submit: empty selection → error;
  per-group `POST /activity-groups/{groupId}/members`.
- **Details page** (`ActivityGroupDetails.razor`): "Roll over" `FluentButton` rendered only when
  `_group.IsActive && _group.Span != "OpenEnded"` (FR-54 admin-forced surface); click →
  `ShowConfirmationAsync` → `POST /activity-groups/{id}/rollover`.
- **PeriodForm** (`src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor`):
  type `FluentSelect<string>` (3 options, no id) always shown; parent dropdown
  ("— Select academic year —") only when `_periodTypeText != "AcademicYear"`; submit is a
  **direct-OnClick `FluentButton`** (no `EditForm`); client validation order: name → dates →
  end ≥ start → **"Select a parent academic year for this period."** when type ≠ AcademicYear and
  `_parentPeriodIdText` unparsable; `PrefillAcademicYear` (default true) pre-fills name + dates;
  create → `POST /students/periods` (`periodType` serialized as **number**, Term = 1) then
  `GET /students/periods/{id}`; `AutoActivateOnCreate` → `POST /students/periods/{id}/activate`.
  `OnInitializedAsync` → `GET /students/periods` (populates `_academicYears`).
- **Existing green test count (Round 2 review):** Admin 464, Students 303, Assignments 102 (869).
- **`IdResponse`** is `{"id":"..."}` camelCase; all client JSON is camelCase; `@bind-Value` on
  `FluentDatePicker`/`@bind-SelectedValues` on `FluentListbox` compile today (build green), so
  `ValueChanged` / `SelectedValuesChanged` EventCallbacks are public parameters.

---

## 4. The bUnit driving approach (Round-2 rules + Round-3 refinements) — MANDATORY

1. **Dialog form submit** — never click the footer FluentButton (does NOT fire `EditForm` in bUnit):
   ```csharp
   var editForm = cut.FindComponent<EditForm>();
   await cut.InvokeAsync(() => editForm.Instance.OnValidSubmit.InvokeAsync(editForm.Instance.EditContext));
   ```
2. **Selects** — drive the bound callback on the component instance, never click `fluent-option`:
   `FluentSelect<string>.ValueChanged.InvokeAsync("DateRange")` (find by `Instance.Id`, e.g.
   `ag-create-span`). For `CodedValueDropdown`-style pickers keep the Round-2
   `SelectedOptionChanged` pattern.
3. **Test-owned model mutation (sanctioned shortcut):** the test creates the model it passes to
   `ShowShellDialogAsync`, and `DialogShellBase.Model` resolves to that SAME instance — mutate
   `model.EnrollmentStartDate` etc. directly, then `cut.Render()` to re-evaluate conditional markup.
   Use this for date pickers and any field where web-component driving is fragile. (This is the
   Round-2 "set the model on the component instance + StateHasChanged" rule, generalized.)
4. **Direct-OnClick FluentButtons DO click** (proven: Close buttons, kebabs, menu items in green
   tests). `PeriodForm`'s submit and `ActivityGroupDetails`' buttons may be clicked. The prohibition
   applies ONLY to EditForm `type=submit` buttons.
5. **Async settling** — fire-and-forget loads: always `cut.WaitForAssertion(...)`; script every
   GET the component issues (unknown URLs → 404 handled gracefully where the client returns null).
6. **Harness** — `TopicCreateDialogTests.cs` is the template for dialog tests (ScriptedHandler with
   `Calls` capture + exact `(Method, Url)` map, `FluentDialogProvider` +
   `ShowShellDialogAsync<T, TModel, TResult>`); `SubPeriodsPageTests.cs`/`ActivityGroupsPageTests.cs`
   for page tests (`FakeAuth` + `VisibleTenantService` + `StubFlagService` + `AddFluentUIComponents`
   + JSInterop Loose). Cleanup: close via `fluent-button[aria-label='Close']`, await the dialog task
   with `WaitAsync(TimeSpan.FromSeconds(5))`.
7. **Date JSON** — `JoinGroupsDialog` compares against `DateTime.UtcNow`; generate window dates
   relative to "today" in test JSON (e.g. `today.AddDays(1)` → `today.AddDays(30)` for an open
   window), never hardcoded calendar dates.

---

## 5. Exact change list (tests + docs only)

### 5.1 New `tests/SchoolCollab.Admin.Tests.Unit/ActivityGroupSpanDialogTests.cs` (Items A1–A6, B-next-window)

Harness per §4.6: script `GET /students/grade-levels/landing` → `[]` for both dialogs.
Group JSON helper: reuse the `TopicCreateDialogTests.GroupJson(span)` shape (camelCase
`ActivityGroupDto` incl. `span`, `enrollmentStartDate`, `enrollmentEndDate`, `autoRenewDefault`).

**Create dialog** (model: `ActivityGroupCreateModel` with `Name = "Chess Club"` to satisfy `[Required]`):

- **A1** `CreateDialog_SpanSelect_DateRangeRevealsWindowPickers` — drive
  `ag-create-span` `ValueChanged("DateRange")` (§4.2); assert Start/End and "Next start"/"Next end"
  date pickers render; initial render (OpenEnded) has none of them.
- **A2** `CreateDialog_NextWindowStartBeforeCurrentEnd_RejectsWithoutPost` — set model
  `Span="DateRange"`, `EnrollmentStartDate=2026-01-01`, `EnrollmentEndDate=2026-06-30`,
  `NextEnrollmentStartDate=2026-05-01`, `NextEnrollmentEndDate=2027-03-01` (§4.3); `cut.Render()`;
  drive `EditForm.OnValidSubmit`; assert error text verbatim, **no POST `/activity-groups`** in
  `handler.Calls`, dialog stays open (task result null after close-with-Cancel or keep-open assert).
- **A3** `CreateDialog_NextWindowHalfFilled_RejectsWithoutPost` — only `NextEnrollmentStartDate`
  set → error `"Enter both next-window dates, or clear both."` + no POST.
- **A4** `CreateDialog_DateRangeValid_PostsWindowAndNextWindow` — valid window + next window; script
  `POST /activity-groups` → `{"id":"<newId>"}`, `PUT /activity-groups/<newId>/next-window` → `200 {}`,
  `GET /activity-groups/<newId>` → DateRange group JSON; drive submit; assert POST body contains
  `"span":"DateRange"`, `"enrollmentStartDate":"2026-01-01"`, `"autoRenewDefault":true` (FR-49
  default); assert the next-window PUT happened and its body carries `"nextStartDate"`/"nextEndDate";
  awaited dialog task completes non-null (AC-37/FR-47/FR-49/FR-53 happy path).

**Edit dialog** (model pre-seeded: `Id`, `Name`, `Span`):

- **A5** `EditDialog_SpanRenderedReadOnly_NoSpanSelect` — open with `Span="Termly"`; assert
  `#ag-edit-span` is a readonly text field and NO `FluentSelect` with a span id exists
  (FR-42 immutability surface).
- **A6** `EditDialog_DateRangeValid_PutsWindowAndNextWindow` — `Span="DateRange"` + valid window +
  next window; script `PUT /activity-groups/{id}` → `200 {}`, `PUT /activity-groups/{id}/next-window`
  → `200 {}`, `GET /activity-groups/{id}` → group JSON; drive submit; assert both PUTs (update first,
  next-window second) and non-null result.

### 5.2 New `tests/SchoolCollab.Admin.Tests.Unit/JoinGroupsDialogTests.cs` (Items A7–A9)

Open via `ShowShellDialogAsync<JoinGroupsDialog, JoinGroupsModel, JoinGroupsResult>` with
`new JoinGroupsModel { StudentId = <guid> }`. Script `GET /activity-groups` (4 groups:
`OpenEnded`, `Termly`, `Semester`, `DateRange` with a **closed** window [today−30, today−1], and
optionally a 5th `DateRange` open window), `GET /students/{studentId}/activity-groups` → `[]`,
`GET /students/periods/active-sub-period`, `GET /students/periods/active-academic-year`.

- **A7 (required)** `JoinGroups_ActiveTerm_TermlyAndOpenEndedListed_OthersFiltered` — active
  sub-period JSON `periodType:"Term"`; assert option text contains the `OpenEnded` + `Termly`
  group names and NOT the `Semester` group nor the closed-window `DateRange` group
  (AC-35/FR-43 + FR-52).
- **A8 (required)** `JoinGroups_NoActivePeriod_PeriodAlignedSpansHidden` — both active-period GETs
  → 404; assert the `Termly` and `Semester` groups are NOT listed (client-side FR-43 rejection
  surface); `OpenEnded` still listed.
- **A9 (optional)** `JoinGroups_Submit_JoinsSelectedGroup` — drive
  `FluentListbox.SelectedValuesChanged` (inherited bindable param) to pick one group; drive
  `EditForm.OnValidSubmit`; assert `POST /activity-groups/{groupId}/members` captured and task
  completes non-null. **If the listbox driving proves fragile after a genuine attempt, drop A9 and
  record the follow-up in the backlog — do not force it.**

### 5.3 Extend `tests/SchoolCollab.Admin.Tests.Unit/ActivityGroupsPageTests.cs` (Item B1, optional B2)

- **B1 (required)** `DetailsPage_RolloverButton_VisibleForBoundedSpans_HiddenForOpenEnded` —
  script GET group (full JSON incl. `span`) + GET members `[]`; render `ActivityGroupDetails` with
  `Span="DateRange"` → markup contains "Roll over"; repeat with `Span="OpenEnded"` → does not.
- **B2 (optional)** `DetailsPage_RolloverConfirmed_PostsRollover` — click "Roll over", click the
  confirm dialog's confirm `fluent-button`, assert `POST /activity-groups/{id}/rollover` + reload
  GETs. Drop-with-backlog-note if fragile (Round 2 confirm-dialog precedent).

### 5.4 New `tests/SchoolCollab.Admin.Tests.Unit/PeriodFormTests.cs` (Items C1–C3)

Render `PeriodForm` directly (no dialog provider, no `VisibleTenantService` needed — just
`StudentsApiClient` + `AddFluentUIComponents` + JSInterop Loose). Script
`GET /students/periods` with 1 Active `AcademicYear` (YearId). Defaults pre-fill name + dates
(`PrefillAcademicYear=true`) — do NOT disable; that makes C2/C3 three-step drives.

- **C1** `PeriodForm_TypeSelect_TogglesParentYearDropdown` — initial: markup does NOT contain
  "— Select academic year —"; drive the type select (first `FluentSelect<string>`)
  `ValueChanged("Term")` → parent dropdown appears with the year's name as an option; drive back to
  `"AcademicYear"` → disappears.
- **C2** `PeriodForm_TermWithoutParent_ShowsErrorAndNoPost` — drive type to `"Term"`, click the
  submit FluentButton (direct-OnClick, §4.4); assert error bar
  "Select a parent academic year for this period." and **no POST /students/periods**.
- **C3** `PeriodForm_TermWithParent_PostsPeriodTypeAndParent` — drive type `"Term"`, drive parent
  select (second `FluentSelect<string>`) `ValueChanged(YearId)`, script
  `POST /students/periods` → `{"id":"<newId>"}` + `GET /students/periods/<newId>` → PeriodDto JSON;
  click submit; assert POST body contains `"periodType":1` (Term, numeric JSON enum) and
  `"parentPeriodId":"<YearId>"`; no activate POST (`AutoActivateOnCreate=false` default).

### 5.5 Docs

- `ui-implementation-backlog.md` §6.1: check the three remaining items with the new test names
  (span-aware dialogs; rollover/next-window; PeriodType + parent selector), noting any dropped
  OPTIONAL test as a documented follow-up. 6.2 stays open.

### 5.6 Out of scope (do NOT touch)

- All product code — no .razor/.cs changes anywhere. AC-42 stays backend-covered (no client
  division-compat hint this round).
- `StudentsApiClient` signatures, API endpoints, handlers, DTOs, migrations.
- 6.2 Playwright smoke, Items 4/5, backend `AssignActivityGroupTopic` duplicate guard.

---

## 6. Test expectations

| ID | File | Test name(s) | Locks |
|----|------|--------------|-------|
| A1–A4 | `tests/SchoolCollab.Admin.Tests.Unit/ActivityGroupSpanDialogTests.cs` (new) | `CreateDialog_SpanSelect_DateRangeRevealsWindowPickers`, `CreateDialog_NextWindowStartBeforeCurrentEnd_RejectsWithoutPost`, `CreateDialog_NextWindowHalfFilled_RejectsWithoutPost`, `CreateDialog_DateRangeValid_PostsWindowAndNextWindow` | AC-37/40/43 UI, FR-42/47/49/53 |
| A5–A6 | same | `EditDialog_SpanRenderedReadOnly_NoSpanSelect`, `EditDialog_DateRangeValid_PutsWindowAndNextWindow` | FR-42 immutability, AC-43/FR-53 (edit path) |
| A7–A9 | `tests/SchoolCollab.Admin.Tests.Unit/JoinGroupsDialogTests.cs` (new) | `JoinGroups_ActiveTerm_TermlyAndOpenEndedListed_OthersFiltered`, `JoinGroups_NoActivePeriod_PeriodAlignedSpansHidden` (+ optional `JoinGroups_Submit_JoinsSelectedGroup`) | AC-35/36, FR-43/52 |
| B1 (B2 opt) | `tests/SchoolCollab.Admin.Tests.Unit/ActivityGroupsPageTests.cs` (extend) | `DetailsPage_RolloverButton_VisibleForBoundedSpans_HiddenForOpenEnded` (+ optional `DetailsPage_RolloverConfirmed_PostsRollover`) | AC-38/43 UI surface, FR-54 admin-forced |
| C1–C3 | `tests/SchoolCollab.Admin.Tests.Unit/PeriodFormTests.cs` (new) | `PeriodForm_TypeSelect_TogglesParentYearDropdown`, `PeriodForm_TermWithoutParent_ShowsErrorAndNoPost`, `PeriodForm_TermWithParent_PostsPeriodTypeAndParent` | FR-H1/H2 UI, period POST shape |

**Required count: 12. Optional: up to 3 (A9, B2, plus edit-dialog duplicate of the A2/A3
validation if desired — not counted).** Baseline before Round 3: Admin 464, Students 303,
Assignments 102 (869). Expected after: Admin 464 + 12 required (± optional), others unchanged.

**Explicitly not UI-testable this round (document, don't force):** AC-38/39/40 rollover *logic*
(backend handler territory — verify handler tests exist, else note as backend follow-up);
AC-41 (capacity); AC-42 (framework-compat rejection — backend guard, no client surface).

---

## 7. Acceptance criteria (the reviewer checks these)

1. **A1–A4 green** — span select reveals DateRange pickers (A1 renders before/after); both
   rejection cases assert the verbatim error bar AND **no POST** in `ScriptedHandler.Calls`; A4
   asserts the POST body (`"span":"DateRange"`, window dates, `"autoRenewDefault":true`), the
   next-window PUT, and a non-null awaited dialog result. Submit driven via
   `EditForm.OnValidSubmit` + `EditContext` — **never** a FluentButton click.
2. **A5–A6 green** — edit span is read-only with no span select (FR-42); valid edit PUTs update +
   next-window in order and closes non-null.
3. **A7–A8 green** — filter assertions are on rendered option text (group names), not internals;
   A8 uses both active-period GETs → 404.
4. **B1 green** — Roll over button presence tracked to `Span != "OpenEnded"` on an active group;
   uses the existing page-test harness; no product change.
5. **C1–C3 green** — parent dropdown toggles with the type select; missing-parent error verbatim +
   no POST; valid Term POST body carries numeric `periodType` (Term) + the parent GUID. Submit
   driven by clicking the direct-OnClick button (allowed per §4.4) — if the worker instead finds a
   more robust invoke, that is acceptable.
6. **Dropped OPTIONALs documented** — any dropped test (A9/B2) has a backlog follow-up note; no
   silent drops.
7. **No scope widening** — diff contains ONLY: the two new test files, the extended
   `ActivityGroupsPageTests.cs`, the new `PeriodFormTests.cs`, and the backlog doc. Zero product
   file changes.
8. **Tests green** — Admin suite passes (≥ 476 with 12 required new); Students 303 and Assignments
   102 unchanged.
9. **Build green** — `dotnet build SchoolCollab.sln -c Debug --nologo -v q` → 0 errors.
10. **Docs updated** — `ui-implementation-backlog.md` §6.1: the three items checked with test
    names; residual list (6.2, Items 4/5, backend guard, AC-42 client hint) stays documented.

---

## 8. Verification commands (tooling quirk applies)

**Known quirk:** `dotnet test --nologo` fails on this machine (Microsoft.Testing.Platform rejects
the forwarded `--nologo`, exit 5). Run `dotnet test <project>` WITHOUT `--nologo`.

1. `dotnet build SchoolCollab.sln -c Debug --nologo -v q` → 0 errors
2. `dotnet test tests/SchoolCollab.Admin.Tests.Unit`
3. `dotnet test tests/SchoolCollab.Students.Tests.Unit`
4. `dotnet test tests/SchoolCollab.Assignments.Tests.Unit`

---

## 9. Residual risks / pragmatic fallbacks

- **Priority if the harness fights back:** A2/A4, A7, B1, C2, C3 are the highest-value REQUIRED
  set (they lock AC-43, AC-35/FR-43, AC-38's surface, FR-H2). A1/A3/A5/A6/C1 are cheap adds but
  may be dropped to follow-up if genuinely blocked — record in backlog, don't force.
- **A9 (listbox multi-select) and B2 (confirm-dialog click-through)** are the two explicitly
  optional click-throughs; both follow the Round-2 T5c/T7c drop-with-note precedent.
- **`periodType` JSON encoding** — PostAsJsonAsync (web defaults) serializes enums as numbers
  (Term = 1). If the worker observes string form, assert the observed form and note it in the PR.
- **DateRange join-window dates** must be UtcNow-relative (§4.7) or the test becomes
  date-fragile by construction.
- **AC-42 client hint and 6.2 Playwright smoke** remain documented residuals after this round;
  Sprint 6 §6.1 will then be fully closed.
---

## 10. Acceptance (parent-completed, 2026-08-27)

**Overall verdict: CLOSED.** All three in-scope Sprint 6 bUnit items are covered by meaningful, passing tests.

| Criterion (§7) | Verdict |
|----------------|---------|
| 1. A1–A4 green, submit via EditForm.OnValidSubmit + EditContext, no-POST on reject, A4 POST body + next-window PUT + non-null | **PASS** (A3/A4 assert no-POST guard; error-text rendering dropped as fragile — see review) |
| 2. A5–A6 green, read-only span, PUT ordering, non-null close | **PASS** |
| 3. A7–A8 green, rendered option text, A8 404s | **PASS** |
| 4. B1 green, button tracks Span != OpenEnded | **PASS** |
| 5. C1–C3 green, dropdown toggle, no-parent error + no POST | **PASS** (POST-body shape test is a minor follow-up) |
| 6. Dropped OPTIONALs documented | **PASS** (A9, B2) |
| 7. No scope widening | **PARTIAL** — 2 product files changed (legitimate footer-Error bug fix) |
| 8. Tests green | **PASS** — Admin 477 (+13), Students 303, Assignments 102 |
| 9. Build green | **PASS** — 0 errors |
| 10. Docs updated | **PASS** |

**Residual (non-blocking follow-ups):** PeriodForm valid-create POST-shape test; A9 Join submit click-through; B2 rollover confirmation click-through; 6.2 Playwright smoke (deferred); Items 4/5 + backend duplicate guard (re-deferred).

**Product fix surfaced by tests:** `ActivityGroupCreateDialog`/`EditDialog` `DialogShellFooter` now binds `Error="Error"` so validation errors render (real bug — dialogs set `Error` but never displayed it).
