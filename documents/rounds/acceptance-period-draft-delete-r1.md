# Acceptance — Draft Period Delete (round r1)

> **Round:** r1 · **Status:** CLOSED (acceptance pass complete — see "Acceptance verdict (r1)" below)
> **Spec:** [documents/specs/period-draft-delete.md](../specs/period-draft-delete.md) (FR-D1..D12, NFR-D1..D3, AC-D1..D10)
> **Plan:** [documents/rounds/plan-period-draft-delete-r1.md](plan-period-draft-delete-r1.md)
> **Provider:** pi (orchestrator glm-5.3-flash / worker deepseek-v4-flash / reviewer kimi-k2.7-code / tester minimax-m3 — first attempt stalled, revived via resume) → this closes the round.

---

## Traceability (filled by worker, verified by reviewer)

| Spec item | Implementation (file) | Test evidence |
| --- | --- | --- |
| FR-D1 | — | — |
| FR-D2 | — | — |
| FR-D3 | — | — |
| FR-D4 | — | — |
| FR-D5 | — | — |
| FR-D6 | — | — |
| FR-D7 | — | — |
| FR-D8 | — | — |
| FR-D9 | — | — |
| FR-D10 | — | — |
| FR-D11 | — | — |
| FR-D12 | — | — |
| NFR-D1 | — | — |
| NFR-D2 | — | — |
| NFR-D3 | — | — |

## AC coverage

- **AC-D1..D10:** to be checked off during review (map each to §10 of the plan doc).

## Out-of-scope guardrail check (reviewer)

- [ ] `SubPeriodsListDialog.razor` (+ `.css`) untouched
- [ ] No soft delete / recycle bin
- [ ] No bulk delete
- [ ] No feature flag
- [ ] No integration/outbox event, no migration

## Verification results (worker)

- `dotnet build SchoolCollab.sln — <pending>`
- `dotnet test tests/SchoolCollab.Students.Tests.Unit — <pending>`
- `dotnet test tests/SchoolCollab.Admin.Tests.Unit — <pending>`
- `dotnet test tests/SchoolCollab.Students.Tests.Integration — <pending>` (Docker/Testcontainers)

## Review findings (reviewer)

<empty until accept pass>

## UI-tester findings (tester)

<empty until accept pass>

## Verdict (orchestrator, accept pass only)

**CLOSED** — see "Acceptance verdict (r1)" below.

---

## Acceptance verdict (r1)

**CLOSED** — no remaining P1 findings; the round is accepted at code level.

- **Reviewer outcome:** PASS-WITH-NOTES — 0 P1, 2 P2. Full FR/NFR/AC traceability in [review-period-draft-delete-r1.md](review-period-draft-delete-r1.md) covers all 25 spec IDs (FR-D1..D12, NFR-D1..D3, AC-D1..D10) as *covered*, and all 8 out-of-scope guardrails pass.
- **P2 disposition — fixed by parent post-review, re-verified in source by this accept pass:**
  - **P2-1** — stale "labeled button" comment at `Periods.razor` ~311-313: comment now correctly describes the kebab menu rendering (visible at lines 312-313).
  - **P2-2** — `SubJson` helper in `PeriodSubPeriodsSectionDeleteTests.cs` (~82-83) missing `"division":"Terms"`: now present, matching the real `PeriodDto` shape.
- **Parent re-verification after the fixes** (full rebuild + test rerun): `dotnet build` → 0 errors; `Students.Tests.Unit` → 398 passed / 0 failed; `Admin.Tests.Unit` → 514 passed / 0 failed.
- **NOT-RUN caveat — `Students.Tests.Integration`:** the integration project was **NOT RUN** due to pre-existing compile errors (CS7036 `Division` constructor arity in `PeriodWizardOpenTermGateTests.cs`, `EnrollWithStreamEndpointTests.cs`, `StudentsApiClientEndToEndEnrollmentTests.cs`) predating this round (PeriodType-drop fallout); git-status verified that no worker/new files from this round are affected. **What it blocks:** AC-D2 (Draft-year cascade of 2 Draft subs), AC-D5 (cross-tenant id → 404), and AC-D7 (`DELETE` → 204) remain **endpoint-level unverified against real Postgres (Docker/Testcontainers)** — their handler/unit-level coverage passed; the Docker-verified pass is pending until the pre-existing integration breakage is fixed independently of this round.
- **Accepted deviation:** 2-action Draft rows render a kebab menu (shared `RowActionsMenu` behavior; supervisor-approved; 3 pre-existing grid tests minimally updated) — FR-D9 / NFR-D3 / AC-D8 / AC-D9 still satisfied per reviewer traceability.
- **Provider traceability:** pi (orchestrator glm-5.3-flash / worker deepseek-v4-flash / reviewer kimi-k2.7-code / tester minimax-m3 — completed; first attempt stalled, revived via resume).

## UI-tester scope handover (r1)

The 4th agent (UI tester) must bug-hunt **exactly** the following closed list of affected UI surfaces. **Nothing else is in scope** — no other pages, components, or flows are affected by this round.

| # | UI surface | Rationale |
| --- | --- | --- |
| 1 | **Periods.razor landing grid (Periods page)** — kebab Delete row action on Draft rows + confirm dialog + reload/error bar | New per-row delete flow added in `BuildPeriodActions` / `OnDeleteAsync`: verify the kebab menu item (with Title tooltip), confirmation wording from `PeriodDeletePrompts`, post-delete grid reload, and 404/422 error-bar rendering. |
| 2 | **Edit.razor period edit page (+ Edit.razor.css)** — Draft-only danger-zone delete section | New danger-zone delete section with confirmation dialog and post-delete navigation to `/students/periods`; styles are isolated in `Edit.razor.css` — check layout, Draft-only visibility, and navigation after deletion. |
| 3 | **SubPeriodsSection.razor (embedded in Edit.razor)** — per-row Delete for Draft sub-periods | New Draft-only Delete button beside Edit/Cancel in the `subperiods-actions` cell + confirm + `OnChanged`-driven refresh inside the year edit page; verify it appears only on Draft rows and does not leak into the untouched `SubPeriodsListDialog`. |
| 4 | **StudentsApiClient.DeletePeriodAsync** — shared API error surfacing | All three delete surfaces call this wrapper; verify 422 guard messages and 404s surface as the standard error bar consistently across surfaces 1–3. |

Residual note: endpoint-level Docker verification (AC-D2/D5/D7) is pending per the NOT-RUN caveat above — if the UI tester cannot run the integration suite, flag it in findings rather than expanding scope.

## UI-tester disposition + final round verdict (r1)

UI-tester pass complete and dispositioned by the parent ([ui-tester-period-draft-delete-r1.md](ui-tester-period-draft-delete-r1.md)): **tester verdict P2 — 0 P1, 2 P2.**

- **P2-1 (Type-column kind visibility) — DISMISSED:** premise was a misread — the `—` is in the Sub-periods column (decorative), while the Type column renders a visible `GetKindLabel` FluentBadge for every row, the same source the delete confirmation uses; no user-visible gap exists. Adjacent latent nit recorded out-of-round: `GetKindLabel` null-division fall-through "Semester" vs `Edit.razor` "Term" default — not reachable from real data (sub-periods inherit division from the year).
- **P2-2 (`_loadError` never cleared on Edit.razor success path) — FIXED by parent:** success path of `OnInitializedAsync` now sets `_loadError = null`; re-verified from parent: `dotnet build SchoolCollab.sln -c Debug` = 0 errors; Admin.Tests.Unit 514 passed; Students.Tests.Unit 398 passed.

**Final numbers (independently re-confirmed by this acceptance addendum):** `dotnet build SchoolCollab.sln -c Debug` → **0 errors** (6 pre-existing NuGet-vulnerability warnings); `Admin.Tests.Unit` → **514 passed / 0 failed**; `Students.Tests.Unit` → **398 passed / 0 failed**; **integration NOT RUN (residual)** — pre-existing CS7036 `Division` compile errors predating this round; AC-D2/D5/D7 endpoint-level verification pending the Docker fix.

**Final round verdict (r1): CLOSED / PASS** — 0 P1 across reviewer and UI-tester passes; tester P2-1 dismissed with evidence, P2-2 fixed and re-verified (fix confirmed in source at `Edit.razor:136`).

Completion note (spec `period-draft-delete`): draft-period delete delivered — domain guard + 204/404/422 route + client wrapper + grid/Edit/SubPeriodsSection affordances; docs: plan/review/ui-tester/acceptance in `documents/rounds/`.