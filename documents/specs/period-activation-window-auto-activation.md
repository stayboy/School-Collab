# Spec: Period Activation Window & Auto-Activation

> **Status:** FR-W1–W7 **Implemented** (round `period-activation-window` r1);
> FR-AA1–AA8 **Planned** (spec-only — the auto-activation sweep is NOT implemented).
> **Date:** 2026-09-01
> **Owner contexts:** `SchoolCollab.Students.Core` (Period entity, CQRS handlers),
> `SchoolCollab.Students.Api` (routes), `SchoolCollab.Students.Worker` (future sweep),
> `SchoolCollab.AppHost` (parameter fanout).
> **Depends on:** `period-activation-guard-atomic-create.md` (FR-G1–G6, FR-C1–C6),
> `period-hierarchy-terms-semesters.md` (FR-H1–H12), `active-period-per-tenancy.md` (FR-A1–A6).
> **Decisions locked (from planning, 2026-09-01):**
> 1. The activation-window guard is a **hard, always-on invariant** — no feature flag.
> 2. Window = `[StartDate − tol, EndDate + tol]`, tol in days, same value both sides, boundaries inclusive.
> 3. Global default tol = **10 days**, supplied as an AppHost parameter
>    (`period-activation-tolerance-days`), following the `use-local-coded-value-projection`
>    precedent (AppHost `Parameters:` → `WithEnvironment("Students__…")` → `Students:…` config key).
> 4. A per-period override (`Period.ActivationToleranceDays`, nullable) takes precedence over
>    the global default; null = inherit.
> 5. Auto-activation due date = the period's own `StartDate` (no separate user field).

---

## 1. Goal

Two related deliverables:

1. **Activation-window guard (implemented):** a period whose `[StartDate, EndDate]` is far
   away from today cannot be activated. This prevents activating a period that starts months
   in the future or ended long ago — a common misconfiguration that leaves an "active" period
   that does not cover today.
2. **Auto-activation (planned, spec-only):** a Draft period auto-activates on its own
   `StartDate` (the "next rollover" due date), reusing the same `ActivatePeriod` pipeline so
   every guard applies. Manual activation stays allowed anywhere inside the window if
   auto-activation has not fired yet.

## 2. Context (what exists today)

| Concern | Today | File |
| --- | --- | --- |
| Activation | `ActivatePeriodHandler` guards hierarchy/sub-period shape (FR-G1) and auto-closes the prior active year/sibling (FR-A1/FR-H4/H5) | `CQRS/Periods/Commands/ActivatePeriod/ActivatePeriodHandler.cs` |
| Date sanity | **None** — a Draft period can be activated no matter how far `today` is from its `StartDate`/`EndDate` | `ActivatePeriodHandler.cs` |
| Rollover precedent | `ActivityGroupRolloverService` sweeps due items on an interval | `SchoolCollab.Students.Worker/ActivityGroupRolloverService.cs` |
| Config precedent | `Students:UseLocalCodedValueProjection` read via `IConfiguration.GetValue`; AppHost `Parameters:` → `WithEnvironment("Students__…")` | `Students.Core/Services/FlagRoutedCodedValuesApiClient.cs`, `AppHost/Program.cs` |

The gap: nothing stops an admin from activating a period whose window does not contain today
(e.g. a year starting in 8 months, or one that ended 6 months ago). The window guard closes
that gap; the auto-activation sweep (planned) makes the normal "open the next period on its
start date" flow automatic.

## 3. Functional Requirements

> RFC 2119 keywords. IDs: `FR-W-N` (window guard, implemented), `FR-AA-N` (auto-activation, planned).

### 3.1 Activation-window guard (implemented)

- **FR-W1** — Activating a period MUST be rejected with `PeriodActivationWindowException`
  when `today < StartDate − tol` or `today > EndDate + tol`, where tol is the effective
  tolerance (FR-W2). Boundaries are **inclusive**: activation is allowed exactly at
  `StartDate − tol` and `EndDate + tol`. The API maps the exception to **422** (FR-W6).
- **FR-W2** — Effective tolerance = `period.ActivationToleranceDays` (per-period override)
  or the global default. The global default is read from config
  `Students:PeriodActivationToleranceDays` (default **10**), supplied by the AppHost
  parameter `period-activation-tolerance-days` fanned out as
  `Students__PeriodActivationToleranceDays` to `students-api` and `students-worker`.
  A negative config value is clamped to 0.
- **FR-W3** — Per-period override: `Period.ActivationToleranceDays` is a nullable `int`;
  null = inherit the global default. It is settable at create (year and each sub-period)
  and update (an explicit null **clears** the override). A negative value is rejected
  (`ArgumentException` → 400). It surfaces in `PeriodDto`.
- **FR-W4** — The guard MUST be evaluated **before any state mutation** in
  `ActivatePeriodHandler`: no prior-year close, no sibling close, no `Activate()` may run
  when the guard fails (all-or-nothing, mirroring FR-G2).
- **FR-W5** — The guard applies to year activation, sub-period activation, and the FR-H4a
  cascade: the cascade only auto-activates sub-periods inside their own window; if zero
  eligible sub-periods are in window, the cascade is skipped (gap state stays valid, FR-H4)
  and logged.
- **FR-W6** — The API activate endpoint (`POST /periods/{id}/activate`) MUST map
  `PeriodActivationWindowException` to **422** with the exception message.
- **FR-W7** — The parameter MUST be documented in `documents/configuration.md` (§2 + §11)
  in the same PR (configuration-documentation rule).

### 3.2 Auto-activation (planned — NOT implemented this round)

- **FR-AA1** — A Draft period's auto-activation due date = its own `StartDate` (no separate
  user field).
- **FR-AA2** — A `Students.Worker` `BackgroundService` sweep (mirroring
  `ActivityGroupRolloverService`; interval `PeriodAutoActivation:IntervalMinutes`, default
  1440) activates due Draft periods by dispatching `ICommandHandler<ActivatePeriod>` — all
  guards (FR-W window, FR-G sub-period, FR-A1/FR-H4/FR-H5 hierarchy) apply.
- **FR-AA3** — Sweep candidates: Draft periods with `StartDate <= today` **and** inside the
  activation window (FR-W1). Overdue-beyond-window drafts are skipped with a warning log,
  never force-activated.
- **FR-AA4** — Per tenant, process due candidates ascending by `StartDate` so the newest due
  period ends Active (each activation closes the prior per FR-A1).
- **FR-AA5** — Per-candidate try/catch; one failure logs and never aborts the sweep.
- **FR-AA6** — Idempotent: re-sweeps no-op on already-Active periods (`Activate()` early-returns).
- **FR-AA7** — Manual activation inside the window remains available before auto-activation
  fires; once Active, further activations no-op.
- **FR-AA8** — Per-tenancy via explicit tenant scope (like `PromotionService`).

## 4. Design

### 4.1 Window guard

`Period.IsWithinActivationWindow(DateOnly today, int defaultToleranceDays)` computes
`tol = ActivationToleranceDays ?? defaultToleranceDays` and returns
`today >= StartDate.AddDays(-tol) && today <= EndDate.AddDays(tol)`.

`ActivatePeriodHandler` reads `today = DateOnly.FromDateTime(DateTime.UtcNow)` and the global
default once, then:
1. **Window guard** (FR-W4) — before the FR-G1 sub-period guard and any mutation.
2. **FR-G1 sub-period guard** (unchanged).
3. **Hierarchy auto-close** (unchanged).
4. **FR-H4a cascade** — filters candidates through `IsWithinActivationWindow`; skips + logs
   when no eligible sub-period is in window (FR-W5).

### 4.2 Config plumbing

```
AppHost appsettings.json Parameters:period-activation-tolerance-days = "10"
  → AppHost Program.cs: builder.AddParameter("period-activation-tolerance-days")
  → WithEnvironment("Students__PeriodActivationToleranceDays", param) on students-api + students-worker
  → ActivatePeriodHandler: configuration.GetValue("Students:PeriodActivationToleranceDays", 10)
```

### 4.3 Auto-activation sweep (future)

A `PeriodAutoActivationService : BackgroundService` in `Students.Worker` sweeps on an
interval, resolving a per-tenant scope and dispatching `ActivatePeriod` for each due Draft
period. Requires a new repository query `GetDraftPeriodsDueForActivationAsync` (Draft,
`StartDate <= today`, in-window) and per-tenant iteration (FR-AA8).

## 5. Non-functional requirements

- **NFR-W1** — No feature flag: the window guard is a hard, always-on invariant.
- **NFR-W2** — `dotnet build` (0 errors) and `dotnet test` (0 failures) before the branch is
  PR-ready (repo pre-flight standard).
- **NFR-AA1** — The sweep must be failure-isolated (FR-AA5) and idempotent (FR-AA6).

## 6. Acceptance criteria

| ID | Scenario | Expected |
| --- | --- | --- |
| AC-W1 | Activate a period with `today < StartDate − tol` | 422 `PeriodActivationWindowException`; period stays Draft; no prior year closed |
| AC-W2 | Activate a period with `today > EndDate + tol` | 422; period stays Draft |
| AC-W3 | Activate exactly at `StartDate − tol` / `EndDate + tol` | 204 (boundaries inclusive) |
| AC-W4 | Per-period override widens (30) / narrows (0) the window | Override wins over the global default |
| AC-W5 | Global default read from `Students:PeriodActivationToleranceDays` | Config value honored (e.g. 0 rejects a +1-day start) |
| AC-W6 | FR-H4a cascade with an out-of-window sub-period | Year Active; sub-period stays Draft (cascade skipped) |
| AC-W7 | Override settable at create + update (null clears); negative rejected | Persisted / cleared / 400 |
| AC-W8 | AppHost parameter + `configuration.md` §2/§11 | Documented in the same PR |

## 7. Implementation notes

- Guard placed at the top of `ActivatePeriodHandler.HandleAsync`, after loading the period,
  before the FR-G1 guard (FR-W4).
- New exception `PeriodActivationWindowException` in `Students.Core/Domain/Exceptions/`;
  mapped in `PeriodRoutes.cs` activate endpoint (422).
- New nullable column `activation_tolerance_days` on `periods` (migration
  `AddPeriodActivationToleranceDays`).
- `PeriodDto` gains `ActivationToleranceDays`; all construction sites updated.

## 8. Follow-ups (not in this round)

- **UI field:** expose per-period `ActivationToleranceDays` in the period create/edit UI
  (year + sub-periods) — tracked in `ui-implementation-backlog.md`.
- **Auto-activation sweep:** implement FR-AA1–AA8 (`PeriodAutoActivationService` in
  `Students.Worker` + `GetDraftPeriodsDueForActivationAsync` + sweep unit tests) — tracked in
  `backend-implementation-backlog.md`.
