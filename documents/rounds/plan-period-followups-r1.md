# Plan: round `period-followups-r1`

- **Kind:** orchestrator plan (four-agent `orchestrator-worker-reviewer` round).
- **Round slug:** `period-followups-r1`.
- **Owner:** orchestrator (this document). The acceptance doc
  `acceptance-period-followups-r1.md` is written only by the orchestrator.
- **Upstream source:** `documents/solution/periods-branch-post-push-followups.md`
  — this round implements §1 (stale doc paths) and §4 (P2 triage with the
  P2-5/P2-6 fix) from its *Suggested execution order* (items 1 and 3).
- **Out of round (parent/user-owned, do not plan or run here):**
  - §2 build/test re-verification on the clean commit — produced by the
    **reviewer child and the parent**; the worker only runs the scoped
    build/test commands listed below for its own change.
  - §3 dev-DB drop/recreate, §5 PR pre-flight — parent-owned, user-gated.
  - The parent owns the status ticks in
    `documents/solution/periods-branch-post-push-followups.md` — **nobody in
    this round edits that file**.

## Context

Commit `b0e7efcf` moved the four `*-drop-periodtype.md` round docs from
`documents/specs/` to `documents/rounds/` without rewriting their internal
path references. **15 stale `documents/specs/` references remain** (verified
with `git grep -c`: acceptance 6, plan 3, review 4, ui-tester 2), and
`acceptance-drop-periodtype.md` §10 still states the round docs *remain in*
`documents/specs/` — now false.

Separately, `documents/rounds/ui-tester-drop-periodtype.md` reported P2-1…P2-8.
Per the follow-up plan: P2-1/P2-2 are verified fixed upstream; P2-3, P2-4,
P2-7, P2-8 are **deferred** (disposition table below); P2-5 (create-from-`?parent=`
against a None-division year lands in a stuck form) and P2-6 (`PrefillAcademicYear`
silently skipped via the `_error` gate, compounding the dead-end) are **fixed
this round**, with a bUnit test.

## Scope

1. **§1** — fix the 15 stale `documents/specs/` self-references in the four
   `documents/rounds/*-drop-periodtype.md` docs + reword the §10 sentence.
2. **§4** — implement the P2-5 UX fix in `PeriodForm.razor` (None-division
   `?parent=` renders an inline cannot-host message with a working Cancel /
   back-to-periods affordance instead of the dead-end form), fold in P2-6
   (make the prefill skip an explicit consequence of the blocked state, not a
   side effect of `_error`), and add a bUnit test in
   `tests/SchoolCollab.Students.Tests.Unit`.
3. Record the P2 per-finding dispositions (table below) — P2-3/4/7/8 are
   **not** fixed in this round.

---

## §1 — Stale `documents/specs/` self-references (doc-only)

**Files (all under `documents/rounds/`):**

| File | Stale refs | What to change |
|---|---|---|
| `plan-drop-periodtype.md` | 3 | "Owned documents" section: `documents/specs/…` → `documents/rounds/…` |
| `review-drop-periodtype.md` | 4 | Header Plan ref; §P1/P2 refs: path targets only |
| `acceptance-drop-periodtype.md` | 6 | Header Plan/Reviewer/UI-tester refs; §5/§6 "persisted at …" refs; **plus §10 sentence**: “Round docs remain in `documents/specs/`.” → reword to “Round docs were moved to `documents/rounds/`.” (the historical note about the reverted move may stay; only the false *remain* claim is corrected) |
| `ui-tester-drop-periodtype.md` | 2 | Header Plan + scope-handover refs |

**Implementation:** scoped replacement of path targets only:
`sed -i 's#documents/specs/#documents/rounds/#g'` on the four files, then the
§10 rewording as a manual edit (do not blanket-replace prose beyond path
targets + that one sentence). Nothing outside these four files changes.

**Acceptance check (per item = per file):**
- Per-file: `git grep -n "documents/specs/" -- <file>` → no output.
- Round gate: `git grep -c "documents/specs/" -- "documents/rounds/*-drop-periodtype.md"` → **0** (currently 6/3/4/2 = 15).
- §10 sentence in `acceptance-drop-periodtype.md` no longer claims the docs
  remain in `documents/specs/`.

**Worker note:** round docs live **only** in `documents/rounds/` — never write
into `documents/specs/`, and never touch
`documents/solution/periods-branch-post-push-followups.md`.

---

## §4 — P2-5/P2-6 fix (code + test)

### Affected code

| File | Role |
|---|---|
| `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor` | The fix. Error bar ~line 106; None-division `_error` ~line 217 (inside the `?parent=` branch of `OnInitializedAsync`); `PrefillAcademicYear` gate `string.IsNullOrEmpty(_error)` ~line 235. |
| `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Create.razor` | `?parent=` host page; passes `CancelRoute="/students/periods"`. **No change expected** — verify only. |
| `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Edit.razor` | Edit host. **No change expected** — verify only. |
| `…/Periods/SubPeriods.razor` (~line 36) and `…/Periods/SubPeriodsListDialog.razor` (~line 242) | Entry points navigating to `/students/periods/create?parent=…`. **No change** — the fix lives in the form so both entry points are covered. |

### Design (keep it minimal — no new components)

1. **Explicit blocked state.** In the `?parent=` branch of `OnInitializedAsync`,
   when the resolved parent year's division is `"None"` (covers the
   parent-not-found fallback, which currently also resolves to `"None"`), set a
   dedicated field `_parentBlocked = true` (message text may stay a const).
   Keep the existing detection condition — this round changes *presentation*,
   not detection. `_error` returns to being the pure save/validate error
   surface; it no longer carries the blocked case (set `_error` there only if
   a code path still needs the bottom-bar variant — prefer not).
2. **Render a blocked panel instead of the form.** When
   `!PeriodId.HasValue && _parentBlocked`, render **instead of** the editable
   form fields (Division select, parent select, name + Suggest/Backfill, Dates,
   tip, submit row):
   - a `FluentMessageBar` (Intent `Warning`) with the cannot-host message,
     e.g. “This academic year's division is 'None' — it cannot host
     sub-periods. Change the year's division or create an academic year
     without sub-periods.”;
   - a `FluentButton` “Back to periods” wired to `CancelAsync` (which already
     honours `OnCancel` then `CancelRoute`), so the affordance works on every
     host. Do **not** hardcode a route in the component.
3. **P2-6 fold-in.** Change the prefill gate from
   `PrefillAcademicYear && string.IsNullOrWhiteSpace(_name) && string.IsNullOrEmpty(_error)`
   to an explicit `… && !_parentBlocked` (plus the existing name/dates
   conditions). Prefill being skipped in the blocked state becomes an
   intentional, documented consequence of the blocked state rather than an
   accidental side effect of `_error` — add a one-line comment saying so.
4. **Do not touch:** edit-mode rendering, wizard paths (`ShowHeader`,
   `AutoActivateOnCreate`), `SubmitAsync` validation, the four `GetKindLabel`
   call sites (P2-3), `SubPeriodsSection.StartEdit` (P2-4), `SubPeriodsListDialog`
   row actions (P2-7), `JoinGroupsDialog` kind fallback (P2-8).

### Style/rules obligations for the worker

- Read and honour: `.github/copilot/rules/dotnet-best-practices.md` (mandatory
  for any `.razor` code-behind change), `.github/copilot/rules/blazor-components.md`
  incl. its **Blazor CSS isolation** section, `.github/copilot/rules/testing.md`,
  and `.github/skills/dialog-ui/SKILL.md` for the message/affordance layout
  (message + action, no dead-end). For form-input behaviour, apply the
  Microsoft catalog skill `dotnet/skills → dotnet-blazor → collect-user-input`
  (https://raw.githubusercontent.com/dotnet/skills/main/plugins/dotnet-blazor/skills/collect-user-input/SKILL.md)
  — not present locally, per AGENTS.md skill-discovery rule.
- Keep styling consistent with existing PeriodForm markup (FluentMessageBar +
  inline FluentButton; add scoped CSS only if genuinely needed).

### Test (bUnit, in `tests/SchoolCollab.Students.Tests.Unit`)

**Important:** that test project currently references only
`SchoolCollab.Students.Core` and has **no bUnit**. Required csproj additions
(both CPM-compliant — `bunit` 2.7.2 and the `AngleSharp` 1.5.2 pin already
exist in `Directory.Packages.props`):

```xml
<PackageReference Include="bunit" />
```
```xml
<ProjectReference Include="..\..\src\Students\SchoolCollab.Students.Application\SchoolCollab.Students.Application.csproj" />
```

New file `tests/SchoolCollab.Students.Tests.Unit/PeriodFormBlockedParentTests.cs`
follows the `TopicCreateDialogTests` pattern (in `Admin.Tests.Unit`):
`[TestClass]` + `BunitContext`, `JSInterop.Mode = JSRuntimeMode.Loose`,
`Services.AddFluentUIComponents()`, a scripted `HttpMessageHandler` mapping
`GET /students/periods` (confirm the exact path in
`StudentsApiClient.ListPeriodsAsync`, StudentsApiClient.cs ~line 1251) to a
serialized `PeriodDto[]`, and `StudentsApiClient` + `CodedValuesApiClient`
registered against that handler with `NullLogger<>` (mirror the ctor pattern
used by `TopicCreateDialogTests.Register`). Serialize a real `PeriodDto` with
the same JSON options `StudentsApiClient` uses — do not hand-write JSON casing.

Minimum test cases:

1. **Blocked state (P2-5):** list contains one top-level year with
   `Division = "None"` → render `PeriodForm` with
   `InitialParentPeriodId = <yearId>`, `CancelRoute = "/students/periods"`.
   Assert: markup contains the cannot-host message (Substring), the Division
   `FluentSelect` is **not** rendered, and a “Back” button is rendered.
2. **Working affordance:** click the “Back to periods” button → assert
   `Services.GetRequiredService<NavigationManager>().Uri` ends with
   `/students/periods`.
3. **P2-6:** in the blocked render, assert the name input value is **empty**
   (no suggestion applied, prefill skip is the documented blocked-state
   behaviour, no stale prefill behind the panel).
4. **Positive control:** same setup but `Division = "Terms"` → normal form
   renders, division select pre-locked to Terms, no blocked message.

**Acceptance check (per item):**
- `dotnet test tests/SchoolCollab.Students.Tests.Unit` passes with the new
  `PeriodFormBlockedParentTests` included (all cases above green).
- Manual-path reasoning: `?parent=` against None-division year → inline
  warning + working back affordance, form fields replaced; P2-6 skip is
  explicit (`_parentBlocked`), not `_error`-coupled.

---

## P2 disposition table (recorded; §4 of the follow-up plan)

| ID | Finding (short) | Disposition | Rationale / evidence |
|---|---|---|---|
| P2-1 | Group filter not restricted to active-year sub-periods | ✅ **Verified fixed upstream** (no action) | `TopicCreateDialog.razor` `FilterPeriodsForGroup` parent-scopes to `_activeYearId.Value` (~lines 316-317, 347). |
| P2-2 | Activate shown for Completed/Archived rows | ✅ **Verified fixed upstream** (no action) | `SubPeriods.razor` `Activate` disabled unless `row.Status == "Draft"` (~line 139). |
| P2-3 | `GetKindLabel` renders “Term” for any non-`Semesters` division incl. null | ⏸ **Deferred** | Defensive, low priority; label is informational, backend constraints prevent the null path in normal flow. Backlog item. |
| P2-4 | `_typeText` set but unused when division known | ⏸ **Deferred** | Cosmetic dead assignment in `SubPeriodsSection.razor` `StartEdit`. |
| P2-5 | Create-from-`?parent=` on None-division year = stuck form | ✅ **Fix this round** | Highest-visible UX gap; §4 blocked-panel implementation above, with bUnit test. |
| P2-6 | `PrefillAcademicYear` silently skipped via `_error` gate | ✅ **Fix this round** (folded into P2-5) | Gate moves to explicit `!_parentBlocked`; skip becomes intentional + documented; covered by test case 3. |
| P2-7 | Inactive-row state-mirroring drift dialog vs grid | ⏸ **Deferred** | Consistency nit; if ever touched, apply P2-2's Draft guard. |
| P2-8 | `JoinGroupsDialog` kind fallback null-division → “Term” | ⏸ **Deferred** | Defensive, unreachable in normal flow (Period invariant forbids `None` sub-periods). |

**Acceptance check (table item):** this table is present in the plan and echoed
in the acceptance doc with per-finding status; no code exists for P2-3/4/7/8 in
the round's diff.

---

## Worker build/test verification (scoped, in-session)

Run after every code change (repo `AGENTS.md` build-verification rule):

```
dotnet build SchoolCollab.sln -c Debug --nologo -v q
dotnet test tests/SchoolCollab.Students.Tests.Unit
```

- `dotnet build` → **0 errors** before reporting done.
- Only the Students unit test project is required in-round (the change touches
  Students only); the reviewer/parent later re-run the full matrix (follow-up
  plan §2).
- MSB3021/MSB3027 file locks: stop the offending `dotnet run` process, do not
  blind-retry; surface the lock.

## Worker & reviewer ownership limits (enforced)

- Worker and reviewer must **never** edit
  `documents/rounds/plan-period-followups-r1.md` or
  `documents/rounds/acceptance-period-followups-r1.md` — orchestrator-only.
- Round docs go **only** in `documents/rounds/`, never `documents/specs/`.
- Do **not** edit `documents/solution/periods-branch-post-push-followups.md`
  (parent owns its ticks).
- No commit, no push, no PR — local working-tree changes only.
- Do not implement P2-3, P2-4, P2-7, or P2-8.