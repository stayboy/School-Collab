# Plan: follow-ups after `feat/periods-landing-grid-beautify` push

- **When:** 2026-08-30, after pushing `be345f73` as the branch's initial upstream.
- **Status of branch:** `be345f73` on `origin/feat/periods-landing-grid-beautify`; working tree clean; no PR open.
- **Owner:** parent / orchestrator. To be run before authorizing a PR to `main`.

## Context

The branch now carries three interleaved workstreams in one commit:
**drop-periodtype** refactor, **periods-landing-grid** UI beautify, and
**Settings feature-flag cleanup**. A whole-tree review after the push
(`review` pass) found no truncations and no merge conflicts, but it did surface
a small set of follow-up items. Some are committed-doc hygiene, some are
re-verification that the recorded "green" state is stale, and some are
UI-tester P2 findings that remain open.

This doc lists every follow-up item, its current (verified) state, the fix or
decision needed, and an acceptance check. It lives in `documents/solution/`
as durable technical memory (the Finding → Implementation standard): update
each item's status here as it is completed or explicitly deferred, rather than
trashing it.

---

## 1. Fix stale `documents/specs/` self-references in the drop-periodtype round docs

**Type:** committed-doc correctness (the one real miss found in review).
**Severity:** low — the four docs are ephemeral round residue, safe to
bulk-trash, but they are now committed and their internal links are broken.

**Problem:** Commit `b0e7efcf` moved the four `*-drop-periodtype.md` docs from
`documents/specs/` to `documents/rounds/` but did **not** rewrite their internal
paths. They still cite each other (and `plan-drop-periodtype.md`) at
`documents/specs/...` locations that no longer exist. Confirmed count:
**15 stale references**.

**Files affected:**
- `documents/rounds/plan-drop-periodtype.md` — "Owned documents" section.
- `documents/rounds/review-drop-periodtype.md` — header Plan ref; §P1/P2 refs.
- `documents/rounds/acceptance-drop-periodtype.md` — header Plan/Reviewer/UI-tester refs; §5/§6 "persisted at …"; §10 "Round docs remain in `documents/specs/`" (now **false** — they were moved to `documents/rounds/`).
- `documents/rounds/ui-tester-drop-periodtype.md` — header Plan + scope-handover refs.

**Fix:** replace every `documents/specs/` → `documents/rounds/` path target in
those four files (a scoped sed). The §10 sentence in the acceptance doc should
be reworded to say the round docs were moved to `documents/rounds/`.

**Acceptance:** `git grep -c "documents/specs/" -- documents/rounds/*-drop-periodtype.md` → `0`.

---

## 2. Re-verify build + test suites on the committed tree

**Type:** verification. The acceptance/review docs record green
(`dotnet build` 0 errors; Students 360/0, Admin 502/0, Settings 446/0), but
that state **predates** the landing-grid UI work that was committed on top of
it. Behavior changed since the recorded runs, so the recorded "green" is stale.

**Fix / action:** on commit `be345f73` (working tree clean):
1. `dotnet build SchoolCollab.sln -c Debug` → expect **0 errors**.
2. `dotnet test tests/SchoolCollab.Students.Tests.Unit`
3. `dotnet test tests/SchoolCollab.Admin.Tests.Unit`
4. `dotnet test tests/SchoolCollab.Settings.Tests.Unit`

**Acceptance:** all four pass with **0 failures**. This is also the required
pre-flight gate before opening a PR (§5).

---

## 3. Dev database drop/recreate (EnsureCreated operational step)

**Type:** operational. `students-db` (and `settings-db`) are `EnsureCreated` in
dev; the branch adds **four new migrations** that the current dev schema does
not carry:
- `Students`: `20260830060725_AddPeriodDivision`, `20260830060743_AddStudentTopicAssignmentSubPeriodId`, `20260830232258_DropPeriodType`
- `Settings`: `20260830062108_RemoveAcademicYearDivisionFlag`

The plan (`plan-drop-periodtype.md`) explicitly notes the worker must **not**
assume EF migrations auto-run in dev.

**Fix / action:** drop and recreate the dev `students-db` and `settings-db`
from the parent so the newest schema (`no period_type`, `division NOT NULL`,
re-filtered unique indexes; Settings `feature_flags`/`value` + no
`academic_year_division` route) applies cleanly, then re-run runtime
verification.

**Acceptance:** clean startup; `periods` table has no `period_type` column;
`division` is `NOT NULL`; the Settings flag-removal migration applies.
---

## 4. Triage the still-open UI-tester P2 findings

**Type:** decision. The UI-tester report (`documents/rounds/ui-tester-drop-periodtype.md`)
lists P2-1…P2-8. **P2-1 and P2-2 are verified as fixed** in the committed tree
(see evidence below). The remainder are open. The round's acceptance marked the
rest as "defensive/UX nits that do not block closure" — this item turns that
into an explicit per-finding decision.

### Verified fixed (no action)
- **P2-1** — `TopicCreateDialog.razor` `FilterPeriodsForGroup` parent-scopes to
  `_activeYearId.Value` (lines 316-317, 347). ✅
- **P2-2** — `SubPeriods.razor` `Activate` disabled unless `row.Status == "Draft"` (line 139). ✅

### Open — recommend per-finding disposition

| ID | Finding | Verified current state | Proposed disposition |
|----|---------|------------------------|----------------------|
| P2-3 | `GetKindLabel` renders "Term" for any non-`Semesters` division, incl. unexpected null / top-level-year data error | Present in all 4 files (`Periods.razor`, `SubPeriods.razor`, `SubPeriodsListDialog.razor`, `SubPeriodsSection.razor`) | **Defensive, low priority** — optionally assert `ParentPeriodId is not null && Division is not null` and render "—"/warning. Defer to backlog unless cheap. |
| P2-4 | `_typeText` set but unused when division is known (`SubPeriodsSection.razor` `StartEdit`) | Cosmetic | **Defer** (dead assignment). |
| P2-5 | Create-from-`?parent=` against a **None-division** year lands in a stuck form (no inline "this year cannot host sub-periods" affordance) | `PeriodForm.razor:217` sets `_error`; line 106 bottom-bar; user only has Cancel | **Highest-visible UX gap** — prioritize implementing the inline message + Cancel-to-periods affordance (this is the user-facing dead-end). |
| P2-6 | `PrefillAcademicYear` silently skipped when sub-period-`?parent=` errored (compounds P2-5) | `PeriodForm.razor:235` gates on `IsNullOrEmpty(_error)` | **Fold into P2-5 fix.** |
| P2-7 | Inactive (Completed/Archived) rows: state-mirroring drift between dialog and grid | `SubPeriodsListDialog.razor` Activate-vs-Complete switch; grid omits actions for non-Draft/Active | **Defer** (consistency nit); apply P2-2's guard if touched. |
| P2-8 | `JoinGroupsDialog.razor` kind fallback: any non-`Semesters` sub-period → "Term"; null division silently classified Term | `JoinGroupsDialog.razor:122` | **Defensive, low priority** — unreachable in normal flow (Period invariant forbids `None` sub-periods). Defer. |

**Acceptance:** per-finding tick in the disposition table; P2-5/P2-6 either
fixed with a test or explicitly deferred with user consent.

---

## 5. PR pre-flight (only after §2 green and with explicit user instruction)

**Type:** process gate. Per `AGENTS.md` merge policy.

**Action (each requires explicit user authorization — do not auto-run):**
1. Pre-flight code-review pass over branch changes vs `main`.
2. Confirm tests from §2 pass on the branch.
3. Open PR targeting `main` only after the user asks.
4. Do **not** merge or push to `main` without separate instruction.

**Acceptance:** `gh pr create` made only after §2 green + user instruction the
commit is ready.

---

## Out of scope
- Any new feature work not listed above (the three stream behaviours themselves
  are accepted/closed).
- Splitting `be345f73` into per-stream commits (note: the earlier session
  considered splitting, but the whole tree was committed as one unit on user
  instruction; a re-split is only worth doing if history cleanliness is
  required).

## Suggested execution order
1. §1 doc-path fix (small, low-risk, unblocks accurate docs).
2. §2 rebuild + tests on the clean commit (fast, authoritative).
3. §4 P2-5/P2-6 UX fix (and its test) + P2 triage decisions.
4. §3 dev DB drop/recreate once code is finalised.
5. §5 PR pre-flight and open PR on user instruction.