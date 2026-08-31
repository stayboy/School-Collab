# Plan — UI Sprint 6, Round 2: remaining bUnit tranche (T2/T3/T4/T5) + 6.3 cross-cutting polish

**Status:** Planned (orchestrator-authored; implementation delegated to a worker)
**Sprint:** 6 — Verification & Cross-Cutting Polish (`ui-implementation-backlog.md` §Sprint 6)
**Inputs:**
- `plan-ui-sprint6.md` — Round 1 plan + §9 acceptance (Round 1 CLOSED)
- `review-ui-sprint6.md` — Round 1 review, incl. the **P2 harness-fragility finding** (§ Findings)
- `activity-group-enrollment.md` — AC-45 (FR-56), AC-46 (FR-55/NFR-6)
- `period-hierarchy-terms-semesters.md` — FR-H6/H7 (division setting + switch rejection)
- `ui-implementation-backlog.md` §Sprint 6 — 6.1 unchecked bUnit items + 6.3

**Scope discipline:** test-first round. Round 1 shipped the four product fixes;
Round 2 locks their behavior with the bUnit tranche that was deferred for
harness fragility, adds ONE small product micro-fix exposed by the T2 flow
(§5.1), and closes 6.3 (mostly verification + one small division-setting UI
surface). Nothing here is feature-sized.

---

## 1. Goal

1. Land the remaining Sprint 6 bUnit tests: **T3** (AC-45 period filtering),
   **T4** (AC-46 null-`PeriodId` back-compat), **T2** (group-path duplicate
   guard), **T5** (SubPeriods page states) — in that priority order (§2).
2. Close **6.3**: verify the sub-period lists' loading/empty/error states
   (already implemented in Round 1's Item 6 work — no code change, test + docs
   only) and add the missing **academic-year division setting UI** (client
   methods + a card on `ConfigFlagDetail`) with the FR-H7 framework-switch
   rejection messaging, locked by bUnit.
3. Fold the Round 1 harness lesson into every dialog test: **drive the dialog's
   `EditForm.OnValidSubmit` directly — never click the FluentButton** (§4).

Non-goals: 6.2 Playwright smoke, AC-35..43 span-aware dialog validation,
rollover/next-window bUnit, `PeriodType`+parent-selector bUnit, re-deferred
Items 4/5, backend `AssignActivityGroupTopic` duplicate guard.

---

## 2. Scope decision

| # | Item | Verdict | Rationale |
|---|------|---------|-----------|
| T3 | bUnit AC-45 span-mismatch period filtering | **MUST** | Pure render assertion (no submit) — the most robust dialog test; `FilterPeriodsForGroup` is synchronous. |
| T4 | bUnit AC-46 null-`PeriodId` back-compat | **MUST** | Single POST-body assertion via the §4 driving fix; highest-value AC coverage gap from Round 1. |
| T2 | bUnit group-path duplicate guard | **SHOULD** | Needs group-select + coded-value driving + fire-and-forget load wait; doable with the §4 approach. |
| 5.1 | Micro-fix: load group topics when the dialog opens pre-seeded | **IN SCOPE** | One line; the *primary production flow* (`Subjects.razor` opens the dialog with `ActivityGroupId` set) currently never loads `_groupExistingTopics`, so the Item-3 guard is dead in that flow. Completes Round 1's Item-3 contract; not scope widening. |
| 6.3a | Loading/empty/error states for sub-period lists | **VERIFY ONLY** | Already implemented: `SubPeriods.razor` + `Periods.razor` both have `_loading` → `FluentProgressRing`, `_error` → `FluentMessageBar` (+ Back), `EmptyMessage` on `LandingPage`, `ErrorBoundary`. Round 2 adds the state tests (T5a/b) and checks the backlog box. |
| 6.3b | Academic-year division setting UI | **IN SCOPE (small)** | Backend GET/PUT exist (`ConfigAcademicYearDivisionRoutes.cs`); **no client method, no UI**. Generic string-flag override path bypasses the FR-H7 sub-period-count guard. Small additive card + 2 client methods. |
| T5c | bUnit SubPeriods row-action click-through (Activate/Complete confirm + POST) | **OPTIONAL** | Confirm-dialog + POST + reload flow is the most fragile piece; render-level action assertions are cheap (T5c), the click-through is documented as follow-up if flaky (§9). |
| T7 | bUnit division setting (6.1 item "AcademicYearDivision setting UI and framework-switch rejection messaging") | **MUST (with 6.3b)** | Closes 6.3b and the 6.1 division item with one test file. |

---

## 3. Confirmed current state (evidence)

- **T3/T4 surface** — `TopicCreateDialog.razor`:
  - `_filteredPeriods` (private computed) → `FilterPeriodsForGroup()` maps
    `Span` → required `PeriodType` (`Termly`→`Term`, `Semester`→`Semester`,
    `WholeAcademicYear`→`AcademicYear`, else `null` → empty) and filters
    `_periods`; empty result renders the info `FluentMessageBar` with
    `_periodHint` ("… carries no period …") instead of the
    `id="topic-create-period"` FluentSelect.
  - `OnInitializedAsync` sets `_activityGroupIdText` from
    `Model.ActivityGroupId` but **never calls `LoadGroupExistingTopicsAsync`** —
    the §5.1 gap.
  - Grade-path `SubmitAsync` posts `CreateTopicForGradeRequest` to
    `POST /students/topics/for-grade` (`StudentsApiClient.cs:1040`) with
    `periodId = Guid.TryParse(_periodIdText, …) ? pid : null` — empty select ⇒
    `"periodId":null` in the camelCase JSON body.
- **T2 surface** — `TopicCreateDialog.razor` `LoadGroupExistingTopicsAsync` +
  `OnCodedValuePicked` group check + `SubmitAsync` stale re-check (sets
  `Error` = "This subject is already assigned to this activity group." and
  returns null BEFORE `CreateTopicAsync`/`AssignActivityGroupTopicAsync`).
  Client routes: `GET /students/subjects/by-group/{id}`
  (`StudentsApiClient.cs:1084`), `POST /students/topics`,
  `POST /students/topic-assignments/activity-group` (`:1422`).
- **T5 surface** — `SubPeriods.razor` (`@page
  "/students/periods/{AcademicYearId:guid}/sub-periods"`): loading ring, error
  bar + Back, `EmptyMessage="No sub-periods for this academic year yet."`,
  `RowActions` (Edit / Activate disabled-for-Active / Complete
  disabled-unless-Active + confirm). `VisibleTenantService` needed (test
  pattern: `ActivityGroupsPageTests.cs` `FakeAuth` + `StubFlagService`).
- **6.3b surface** — `ConfigAcademicYearDivisionRoutes.cs`: GET/PUT
  `/api/config/flags/academic_year_division` (GET →
  `{"value":…,"source":…}` or 404; PUT `{"value":…,"reason":…}` → 204 | 400 |
  422 `{"message":"Cannot change … N sub-period(s) still exist …"}`).
  `ConfigFlagsApiClient` (`SchoolCollab.Admin.Shared/Services/`) has **no**
  division methods; `ConfigFlagDetail.razor` renders the generic string-flag UI
  only. `SchoolCollab.Admin.Tests.Unit` references `Settings.Application` ✓.

---

## 4. The bUnit driving approach that WORKS (fold-in of the Round 1 finding)

Round 1's T4 failed because **clicking the `FluentButton` submit did not fire
the `EditForm`** (FluentButton is a web component; in bUnit's DOM its click
does not raise the form-submit event Blazor's `EditForm` intercepts). Round 2
rules (mandatory):

1. **Form submit** — find the `EditForm` and invoke its valid-submit callback:
   ```csharp
   var editForm = cut.FindComponent<EditForm>();
   await cut.InvokeAsync(() => editForm.Instance.OnValidSubmit.InvokeAsync());
   ```
   This invokes `DialogShellBase.HandleSubmitAsync` (→ `SubmitAsync`).
   Validation is bypassed, so set the model fields valid beforehand (e.g.
   `Model.Name = "Math"`). Assert side effects via `ScriptedHandler.Calls` and
   the awaited `ShowShellDialogAsync` task (`result.Should().NotBeNull()` on a
   200-mapped POST response; the dialog closes itself).
2. **Selects** — never click `fluent-option`; invoke the bound callback on the
   component instance:
   - Owner / group selects (`TOption="string"`):
     `cut.FindComponents<FluentSelect<string>>().First(s => s.Instance.Id == "topic-create-group")`
     → `await cut.InvokeAsync(() => s.Instance.ValueChanged.InvokeAsync(groupId.ToString()))`
     (`@bind-Value:after` runs `OnActivityGroupChangedAsync`).
   - `CodedValueDropdown`: keep the T1 pattern —
     `dropdown.FindComponent<FluentSelect<CodedValueDto>>().Instance.SelectedOptionChanged.InvokeAsync(picked)`.
3. **Async settling** — `LoadGroupExistingTopicsAsync` is fire-and-forget
   (`_ =`); always assert through `cut.WaitForAssertion(...)` (and script the
   by-group GET even when its payload is irrelevant, e.g. `[]` or 404).
4. **Harness** — reuse the `TopicCreateDialogTests` `ScriptedHandler`
   (exact `(Method, Url)` map + captured `Calls` incl. bodies). Page tests
   (T5/T7) reuse the `ActivityGroupsPageTests` registration: `FakeAuth`
   (claims `tenant_id`/`tenant_name`) + `VisibleTenantService` +
   `StubFlagService` enabled + `AddFluentUIComponents` + JSInterop Loose.
5. **Cleanup** — close dialogs via `fluent-button[aria-label='Close']` and
   await the dialog task with `WaitAsync(TimeSpan.FromSeconds(5))` (existing
   pattern).

---

## 5. Exact change list

### 5.1 Micro-fix — `TopicCreateDialog.razor` (1 line, product)
In `OnInitializedAsync`, after the `Model.ActivityGroupId` seeding block, fire
the same load the user-path selection uses:

```csharp
if (Model.ActivityGroupId.HasValue)
    _ = LoadGroupExistingTopicsAsync();
```

(No reset/flag work needed — `LoadGroupExistingTopicsAsync` already handles the
invalid/failed cases.) This makes the Item-3 guard effective when the dialog
opens from `Subjects.razor` with the group pre-selected — today
`_groupExistingTopics` stays `[]` there and the duplicate warning can never
fire.

### 5.2 6.3b — Academic-year division setting (2 files, product)

**`src/SchoolCollab.Admin.Shared/Services/ConfigFlagsApiClient.cs`**
1. Mirror DTO (Admin.Shared stays Core-free, same comment pattern as the other
   mirrors): `public record AcademicYearDivisionSettingDto(string Value, string Source);`
2. `GetAcademicYearDivisionAsync(CancellationToken ct = default)` →
   `GET /api/config/flags/academic_year_division`; 200 → deserialize the DTO;
   404 → `null` (caller renders "None (default)"); else `EnsureSuccessStatusCode`.
3. `SetAcademicYearDivisionAsync(string value, string reason, CancellationToken ct = default)`
   → `PUT` with `new { value, reason }` (web defaults). On non-success,
   read the body and surface the server `message` property (JSON — camelCase;
   fall back to the status code) as the exception text so the 422
   FR-H7 rejection text reaches the UI verbatim.

**`src/Settings/SchoolCollab.Settings.Application/Components/Pages/ConfigFlagDetail.razor`**
When the loaded flag is the division flag (`_flag.Key` equals
`FeatureFlagKeys.AcademicYearDivision` case-insensitively — the constant lives
in `SchoolCollab.Core/Features/FeatureFlagKeys.cs`, referenced by
Settings.Application), render a new `FluentCard` **"Academic-year division"**
above the "Default state" card:

- **Loading state:** `FluentProgressRing` while `_divisionLoading`.
- **Loaded:** current effective value + Source
  (`FluentBadge` — "TenantOverride" / "GlobalDefault"; GET 404 → "None"
  + "(default)" hint), a `FluentSelect TOption="string"` with options
  None/Terms/Semesters, a reason `FluentTextField` (required), Save
  `FluentButton` (`Disabled="@_busy || reason empty"`).
- **Error state:** `_divisionError` → `FluentMessageBar
  Intent="MessageIntent.Error"` (this is the FR-H7 framework-switch rejection
  messaging — the server's "Cannot change … sub-period(s) still exist …"
  text must appear verbatim).
- **Success:** re-`GET` the division (and the flag) so the card reflects the
  new value; clear the reason.
- The existing generic string-flag override UI stays untouched (its bypass of
  the FR-H7 guard is pre-existing behavior — noted as residual §9).

### 5.3 Tests (project `tests/SchoolCollab.Admin.Tests.Unit`)

**`TopicCreateDialogTests.cs`** (extend the existing file/harness):

- **T3a** `CreateDialog_TermlyGroup_PeriodOptionsFilteredToTerms` —
  script `GET /activity-groups` with one group `"span":"Termly"` and
  `GET /students/periods` with a mix (1 `AcademicYear` active year, 2 `Term`
  children, 1 `Semester`); open with `OwnerType="ActivityGroup"`; drive the
  group select to the group id; assert the markup contains the two Term
  period names as options and does **not** contain the Semester name
  (AC-45: `Termly` group + `Semester` period must be impossible to select).
- **T3b** `CreateDialog_OpenEndedGroup_ShowsNoPeriodHint` — same setup with
  `"span":"OpenEnded"`; assert the info bar (`"carries no period"`) renders
  and **no** period `FluentSelect` (id `topic-create-period`) exists;
  assert none of the scripted period names appear as options (AC-45: null
  `PeriodId`).
- **T2** `CreateDialog_GroupOwner_DuplicateCodedValue_WarnsAndBlocksAssign` —
  script `GET /api/coded-values/by-parent?parentCode=SUBJECT` (T1 pattern),
  the group with `"span":"Termly"`, and
  `GET /students/subjects/by-group/{groupId}` returning one `SubjectDto`
  whose `CodedValueId` equals the scripted coded value. Flow: open with
  `OwnerType="ActivityGroup"` → drive group select → drive
  `SelectedOptionChanged` to the coded value → assert the warning bar
  ("This subject is already assigned to this activity group.") and Create
  `disabled` → drive `EditForm.OnValidSubmit` (§4) → assert
  `handler.Calls` contains **no POST** (guard fires before
  `CreateTopicAsync`/`AssignActivityGroupTopicAsync`; the saver short-circuits
  unchanged fields) and the error bar shows the guard message; dialog stays
  open.
- **T4** `CreateDialog_GradeOwner_NullPeriodId_PostsPeriodIdNull` —
  default model (`GradeLevelId` set), `Model.Name = "Math"` (satisfies
  `[Required]`); script `POST /students/topics/for-grade` → 200 with a
  `TopicDto` JSON; drive `EditForm.OnValidSubmit` (§4) without touching the
  period select; assert the captured body contains `"periodId":null`
  (AC-46 back-compat: default = year-spanning) and the awaited
  `ShowShellDialogAsync` task completes non-null.

**New `SubPeriodsPageTests.cs`** (harness per §4.4 / `ActivityGroupsPageTests`):

- **T5a** `SubPeriods_EmptyList_ShowsEmptyMessage` — render `SubPeriods` with
  `AcademicYearId` parameter; script `GET /students/periods/{yearId}` (the
  year) + `GET /students/periods/{yearId}/sub-periods` → `[]`; assert the
  empty message and `CreateEnabled` (real tenant via `FakeAuth`).
- **T5b** `SubPeriods_LoadError_ShowsErrorBarAndBackButton` — script the
  sub-periods GET → 500; assert the error `FluentMessageBar` shows the
  message and the Back button renders (6.3a locked).
- **T5c (OPTIONAL)** `SubPeriods_RowActions_RenderPerStatus` — script one
  `Draft` and one `Active` sub-period; assert all rows have an Edit action,
  the `Draft` row has Activate enabled and Complete disabled, the `Active`
  row the reverse. Rendering assertions only — do **not** drive the confirm
  dialog / POST flow; if T5c proves flaky, drop it and note the follow-up.

**New `AcademicYearDivisionSettingTests.cs`**:

- **T7a** `DivisionSetting_CardShowsEffectiveValueAndSource` — render
  `ConfigFlagDetail` with `Key = FeatureFlagKeys.AcademicYearDivision`;
  script `GET /api/config/flags/FEATURE%3AAcademicYearDivision`
  (`Uri.EscapeDataString(key)`), overrides `[]`, audit `[]`, flag JSON
  `"kind":"String"`, and `GET /api/config/flags/academic_year_division` →
  `{"value":"Terms","source":"TenantOverride"}`; assert the card shows the
  current value and source.
- **T7b** `DivisionSetting_SwitchRejection_ShowsServerMessage` — same setup;
  drive the division select to "Semesters", set the reason, click Save with
  `PUT` scripted → 422 `{"message":"Cannot change academic-year division from
  'Terms' to 'Semesters': 3 sub-period(s) still exist. Complete or remove them
  first."}`; assert the error bar renders that message verbatim.
- **T7c (OPTIONAL)** `DivisionSetting_SuccessfulSave_ReloadsValue` — PUT →
  204 + re-GET returning the new value; assert the card now shows it.

### 5.4 Out of scope (do NOT touch)
- `AssignActivityGroupTopicHandler` / backend guards; `TopicRoutes.cs`;
  `StudentsApiClient` signatures (only `ConfigFlagsApiClient` gains methods).
- `SubPeriods.razor` / `Periods.razor` product code (states already shipped in
  Round 1 — T5a/b are locks, not fixes).
- `Settings.Api` endpoints (division routes already exist and are correct).
- Items 4/5, 6.2 Playwright, AC-35..43 / rollover / PeriodType bUnit items.

---

## 6. Test expectations

| ID | File | Test name(s) | Locks |
|----|------|--------------|-------|
| T2 | `tests/SchoolCollab.Admin.Tests.Unit/TopicCreateDialogTests.cs` | `CreateDialog_GroupOwner_DuplicateCodedValue_WarnsAndBlocksAssign` | Item 3 (group duplicate guard) end-to-end incl. no-POST |
| T3 | same | `CreateDialog_TermlyGroup_PeriodOptionsFilteredToTerms`, `CreateDialog_OpenEndedGroup_ShowsNoPeriodHint` | AC-45 / FR-56 UI filtering |
| T4 | same | `CreateDialog_GradeOwner_NullPeriodId_PostsPeriodIdNull` | AC-46 null-`PeriodId` back-compat |
| T5 | `tests/SchoolCollab.Admin.Tests.Unit/SubPeriodsPageTests.cs` (new) | `SubPeriods_EmptyList_ShowsEmptyMessage`, `SubPeriods_LoadError_ShowsErrorBarAndBackButton` (+ optional `SubPeriods_RowActions_RenderPerStatus`) | Item 6 / 6.3a states |
| T7 | `tests/SchoolCollab.Admin.Tests.Unit/AcademicYearDivisionSettingTests.cs` (new) | `DivisionSetting_CardShowsEffectiveValueAndSource`, `DivisionSetting_SwitchRejection_ShowsServerMessage` (+ optional success-reload) | 6.3b division UI + FR-H7 rejection messaging |
| — | `tests/SchoolCollab.Students.Tests.Unit` | none new; regression-only | Item 2 locked in Round 1 |

Baseline before Round 2: Admin 454, Students 303, Assignments 102 (859 total).
Expected after: Admin 454 + 7 required (±2 optional) tests, others unchanged.

---

## 7. Acceptance criteria (the reviewer checks these)

1. **T3 present and green** — both AC-45 cases assert on the actual rendered
   period options/hint (not implementation internals); `OpenEnded` shows the
   info bar and no period select.
2. **T4 present and green** — the captured `POST /students/topics/for-grade`
   body contains `"periodId":null` with a valid name; the submit was driven
   via `EditForm.OnValidSubmit` (§4), NOT a FluentButton click; the dialog
   task completes with a non-null result.
3. **T2 present and green** — warning bar + disabled Create on duplicate
   pick; submit driven per §4 produces **no POST** in `ScriptedHandler.Calls`
   and surfaces the guard error bar.
4. **§5.1 micro-fix** — `OnInitializedAsync` fires
   `LoadGroupExistingTopicsAsync` when `Model.ActivityGroupId` is pre-seeded;
   T2 does not depend on manually re-selecting the group in the pre-seeded
   flow. No other product change in the dialog.
5. **Division setting wired** — `ConfigFlagsApiClient` gains
   `AcademicYearDivisionSettingDto` + GET/PUT methods; PUT surfaces the server
   rejection `message` on non-success; `ConfigFlagDetail` renders the division
   card ONLY for that key (other string flags unchanged), with loading ring,
   error message bar (verbatim FR-H7 message on 422), and post-save reload.
6. **6.3a verified** — `SubPeriods.razor` and `Periods.razor` confirmed to
   have loading/empty/error states (no product change); T5a/T5b lock them.
7. **No scope widening** — product diff limited to `TopicCreateDialog.razor`
   (1 line), `ConfigFlagsApiClient.cs`, `ConfigFlagDetail.razor`; plus test
   files and backlog annotations. No endpoint/handler/DTO/migration changes.
8. **Tests green** — full Admin suite passes (≥461 with 7 new); Students 303
   and Assignments 102 unchanged.
9. **Build green** — `dotnet build SchoolCollab.sln -c Debug --nologo -v q` →
   0 errors.
10. **Docs updated** — `ui-implementation-backlog.md` §6.1: AC-45 item and the
    AC-46 null-periodId entry checked with the new test names; the
    `AcademicYearDivision` setting item checked (T7 + §5.2). §6.3 checked with
    notes (sub-period states verified; division card added; row-action bUnit
    click-through follow-up noted if T5c dropped). Residual list (Items 4/5,
    backend guard, 6.2, remaining 6.1 items) stays documented as open.

---

## 8. Verification commands (tooling quirk applies)

**Known quirk:** `dotnet test --nologo` fails on this machine (`.NET 10` SDK /
Microsoft.Testing.Platform rejects the forwarded `--nologo`, exit 5, 0 tests).
Run `dotnet test <project>` WITHOUT `--nologo`.

1. `dotnet build SchoolCollab.sln -c Debug --nologo -v q` → 0 errors
2. `dotnet test tests/SchoolCollab.Admin.Tests.Unit`
3. `dotnet test tests/SchoolCollab.Students.Tests.Unit`
4. `dotnet test tests/SchoolCollab.Assignments.Tests.Unit`
(`Students.Tests.Integration` not required — needs the Aspire/AppHost harness.)

---

## 9. Residual risks / pragmatic fallbacks

- **Priority order if the dialog harness fights back:** T3 + T4 are REQUIRED
  (§4 makes them robust). T2 is SHOULD — if the coded-value/group driving
  proves fragile after a genuine attempt, record it as a documented follow-up
  in the backlog (do not force it); the §5.1 micro-fix still ships.
- **T5c/T7c (row-action click-through, division save-reload)** are explicitly
  optional; dropping them with a backlog note is acceptable. T5a/T5b/T7a/T7b
  are required unless genuinely blocked.
- **Division card placement** — `ConfigFlagDetail` was chosen because the
  client, DI registration, and page states already exist there. If the worker
  finds the key-comparison awkward, a dedicated tiny section is acceptable as
  long as acceptance criteria 5 hold.
- **The generic string-flag override path on `ConfigFlagDetail` can still set
  `FEATURE:AcademicYearDivision` per tenant, bypassing the FR-H7 sub-period
  guard** (pre-existing; the guarded endpoint is the one the new card uses).
  Out of scope here; noted for a future hardening pass (hide the raw Value
  override inputs for this key, or route them through the guarded PUT).
- **`LoadGroupExistingTopicsAsync` failure mode** stays degrade-to-empty (per
  Round 1 design); bUnit only asserts the scripted-success path.
---

## 8. Acceptance (orchestrator pass, 2026-08-27)

**Performed by:** orchestrator (`ollama/glm-5.3-flash:cloud`), acceptance pass.
(The orchestrator-accept child hit a transient 500 API error; this acceptance was
completed from the parent using the reviewer's report + parent-run build/tests.)

**Verdict: CLOSED** for the in-scope Round 2 items.

### Per-criterion verdict

| Criterion | Verdict |
|-----------|---------|
| 1. T3 (AC-45 period filtering) | ✅ PASS |
| 2. T4 (AC-46 null-periodId) | ✅ PASS |
| 3. T2 (group duplicate guard) | ✅ PASS |
| 4. §5.1 micro-fix (pre-seeded group loads existing topics) | ✅ PASS |
| 5. Division setting UI + client | ✅ PASS |
| 6. 6.3a loading/empty/error states | ✅ PASS |
| 7. No scope widening | ✅ PASS |
| 8. Tests green | ✅ PASS (Students 303, Admin 464, Assignments 102) |
| 9. Build green | ✅ PASS (0 errors) |
| 10. Docs updated | ✅ PASS |

### Residual (deferred to a later sub-round)

- bUnit AC-35..43 (span-aware dialog validation), AC-38/43 (rollover/next-window),
  `PeriodType` + parent selector validation.
- 6.2 Playwright smoke.
- Item 4 (PeriodId editing), Item 5 (string-flag audit), backend
  `AssignActivityGroupTopic` duplicate guard.

The Round 2 bUnit tranche and 6.3a polish are implemented, correct, and build/test
green. Proceed to the next Sprint 6 sub-round for the remaining items.
