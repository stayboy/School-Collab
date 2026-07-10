# Spec: Active Period per Tenancy (draft)

> Status: **Draft / Idea** — design proposal; FR-A1–A6 implemented, deviations synced in §10.
> Open questions in §8.
> Owner: Students + Core + Assignments contexts
> Depends on: `global-tenant-filter.md` (§5.6 Period invariant, FR-4/FR-5/FR-6/FR-16/FR-18/FR-19),
> `grade-level-setup.md` (§0.3 current period derived, §0.4 at-most-one-active invariant),
> existing `Period` / `StudentEnrollment` / `PromotionService` entities.

## 0. Central decision — do NOT put "active period" into the tenancy context

The question raised was: *"is it appropriate to add active period context to tenancy, for use
across all modules?"*

**Recommendation: No — keep `ITenantProvider`/`TenantContext` as pure identity/authorization
scope, and expose the active period as a separate, layered ambient context (`IActivePeriodProvider`)
that composes with tenancy.** The active period is then resolvable in every module without
expanding the tenancy context itself.

Rationale (all grounded in the existing code):

1. **Dependency direction.** `ITenantProvider`, `ITenantContextAccessor`, and `ModuleDbContext`
   live in `SchoolCollab.Core` and are intentionally module-agnostic (FR-16, §8.4 keep
   cross-module concerns behind abstractions). `Period` is a **Students.Core** domain entity
   (`src/Students/.../Domain/Period.cs`). Bolting the active period onto Core's tenancy context
   would force `SchoolCollab.Core → SchoolCollab.Students.Core` (a layering inversion) or would
   duplicate the Period concept into Core. The consistent move is a *new ambient-context
   abstraction defined in Core* whose *implementation* lives in Students.Core; other modules
   depend only on the Core abstraction.
2. **Semantics.** Tenancy context = *who/which school* (stable for the request, set from the
   `tenant_id` claim). Active period = *which operational window is open* (mutable, transitions
   at most once per term, and can be flipped by a user action mid-request). Mixing a mutable
   operational value into the identity context muddies `ModuleDbContext`'s save-guards
   (FR-5/FR-6 are about *which tenant*, not *which window*) and the build-time filter audit
   (FR-18).
3. **Staleness.** An `AsyncLocal` ambient value that changes rarely is tempting to cache, but a
   long-lived scope (background service, cached `HttpClient`, singleton) could hold a stale
   period. Keeping it as its own short-lived *scoped* provider — resolved per request *within*
   the current tenant scope — avoids that; the existing `HybridCache` ("students" tag, already
   invalidated on Activate/Complete) caches the lookup safely per tenant.
4. **It is already a query, not a property of tenancy.** `PeriodRepository.GetActivePeriodAsync`
   and `GetCurrentPeriodAsync` are tenant-filtered queries; `PromotionService` already runs
   per-tenant. "The active period for the current tenant" is a *derived* value. Exposing it as
   `IActivePeriodProvider.GetActivePeriodAsync()` (which reads `ITenantProvider.CurrentTenantId`
   and queries the period repo) is the natural extension.

**Net:** an active period is a *derived, tenant-scoped ambient value layered on top of tenancy* —
not part of tenancy. "Running an active period per tenancy" is achieved by composition, not by
widening `TenantContext`.

## 1. Goal

Support the grade-level wizard's requirement that **students are added to grades only when a
period is open**, with exactly **one active period per tenant** at all times, and with **opening
a new period automatically closing the previous one**, where **period closure runs promotions or
repetitions** forward into the next period. Make the active period available ambiently to any
module (Students, Assignments, …) without coupling them to Students.Core internals or to the
tenancy context.

## 2. Current state (what already exists)

| Concern | Today | File |
| --- | --- | --- |
| Period lifecycle | `Draft → Active → Completed → Archived` (`PeriodStatus`) | `Domain/PeriodStatus.cs` |
| At-most-one-active | Invariant enforced: `ActivatePeriodHandler` **throws** if another period is already `Active` | `CQRS/Periods/Commands/ActivatePeriod/ActivatePeriodHandler.cs` |
| Active lookup | `GetActivePeriodAsync(excludeId)` and `GetCurrentPeriodAsync()` (date-derived) | `Data/Repositories/PeriodRepository.cs` |
| Next-period link | `Period.NextPeriodId` already exists | `Domain/Period.cs` |
| Promotion | `PromotionService` auto-completes expired active periods and promotes active enrollments into `NextPeriodId` (same `GradeLevelId`), per tenant | `SchoolCollab.Students.Worker/Services/PromotionService.cs` |
| Enroll student | `EnrollStudentHandler` enrolls into any `PeriodId` — **no open-period guard** | `CQRS/Enrollments/Commands/EnrollStudent/EnrollStudentHandler.cs` |
| Wizard gate | Placeholder only: *"No period covers today. Create a period… before [enrolling] students."* (date-derived "current", not status-derived "active") | `SchoolCollab.Students.Admin/.../GradeLevels/GradeLevelWizard.razor` |
| Tenancy | Ambient `ITenantProvider`; save-guards + filter audit in `ModuleDbContext` | `SchoolCollab.Core/Tenancy/*`, `SchoolCollab.Core/Data/ModuleDbContext.cs` |

Two distinct "current" notions already coexist and must not be conflated:
- **Active (status-derived)** — the operational open window. Used for *enrollment gating*.
- **Current (date-derived)** — the period whose `[StartDate,EndDate]` contains today. Used for
  *display* (landing-page counts, `grade-level-setup.md` §0.3). Usually they coincide.

## 3. Requirements

- **FR-A1 (open-new-closes-old).** Activating a `Draft` period MUST first close the tenant's
  currently-`Active` period (auto-close), so there is never more than one `Active` period. The
  existing "throw if another active exists" guard becomes "close the other active, then activate."
- **FR-A2 (at-most-one-active).** The per-tenant "at most one `Active` period" invariant (§5.6)
  is preserved; it is now maintained by auto-close (FR-A1) rather than by rejecting the activate.
- **FR-A3 (enrollment requires open period).** `EnrollStudent` (and the wizard's add-students-to-
  grades action) MUST require an `Active` period for the current tenant. If none exists, the
  command throws `PeriodNotOpenException` and the UI disables the action. The enrolled `PeriodId`
  MUST be the active period (the wizard should not let the user pick a `Draft`/`Completed` period).
- **FR-A4 (closure ⇒ promotion or repetition).** Closing the active period (FR-A1 / auto-complete)
  MUST trigger, per active enrollment, either a **promotion** (advance to the next grade level in
  `NextPeriodId`) or a **repetition** (stay at the same grade level in `NextPeriodId`). This
  extends the existing `PromotionService` carry-forward (which today copies the *same*
  `GradeLevelId` — effectively always a repetition).
- **FR-A5 (cross-module ambient active period).** The active period MUST be resolvable in any
  module via a Core abstraction `IActivePeriodProvider` (not via the tenancy context). It returns
  a Core-level `ActivePeriod` projection (not the `Period` entity), and is automatically per-tenant
  because the underlying repository query is tenant-filtered — including inside
  `ITenantContextAccessor.RunWithExplicitTenantAsync` for workers.
- **FR-A6 (wizard entry gate — Idea C).** The GradeLevel wizard MUST NOT render its steps unless an
  `Active` period exists for the tenant. Before rendering steps, the wizard resolves the tenant's
  active period (client-side via `ListPeriodsAsync`, filtering on `Status == "Active"` — see
  §4.7); if none exists it renders an **"Open a term"** entry gate instead of the `FluentWizard`
  steps. The entry gate MUST let the user create-and-activate a new period or open an existing
  `Draft` period, and MUST re-render the wizard steps only after an active period exists. This is
  the primary "open period" surface for the wizard; the server-side `EnrollStudent` guard (FR-A3)
  remains as defense-in-depth.

## 4. Design

### 4.1 Boundary: tenancy vs. period context

```
ITenantProvider (Core)            IActivePeriodProvider (Core, NEW)
  └─ TenantContext                  └─ resolves CurrentTenantId from ITenantProvider
       (who/which school)                └─ queries IPeriodRepository (tenant-filtered)
                                            └─ returns active / current ActivePeriod for tenant
Students.Core implements IActivePeriodProvider (reads tenant-filtered Period repo)
Assignments / any module depends on IActivePeriodProvider (Core only)
```

`TenantContext` is unchanged. `IActivePeriodProvider` is a peer ambient service, not a field on
`TenantContext`.

### 4.2 `IActivePeriodProvider` (abstraction in `SchoolCollab.Core`)

> **Implemented deviation (synced with code):** the abstraction returns a Core-level
> `ActivePeriod` projection (see below), **not** the `Students.Core` `Period` entity. This keeps
> `SchoolCollab.Core` module-agnostic (no `Core → Students.Core` dependency). The implementation
> resolves via the tenant-filtered period repository (the tenant is applied by the repo's global
> query filter) and currently does a **direct query** — per-tenant `HybridCache` is a follow-up
> (see §4.6). Files: `SchoolCollab.Core/Tenancy/IActivePeriodProvider.cs`,
> `SchoolCollab.Students.Core/Tenancy/ActivePeriodProvider.cs`.

```csharp
namespace SchoolCollab.Core.Tenancy;

/// <summary>Core-level projection of the tenant's active period. Defined in Core so other
/// modules can resolve it without depending on Students.Core.</summary>
public sealed record ActivePeriod(
    Guid Id,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status);

public interface IActivePeriodProvider
{
    /// <summary>The single Active period for the current tenant, or null.</summary>
    Task<ActivePeriod?> GetActivePeriodAsync(CancellationToken ct = default);

    /// <summary>The date-derived "current" period for the current tenant, or null
    /// (display use; see grade-level-setup.md §0.3).</summary>
    Task<ActivePeriod?> GetCurrentPeriodAsync(CancellationToken ct = default);
}
```

- **Implementation (Students.Core, scoped):** inject `IPeriodRepository`; call the existing
  tenant-filtered `GetActivePeriodAsync()` / `GetCurrentPeriodAsync()` (the tenant is applied by
  the repo's global query filter, so the provider need not read `ITenantProvider` directly). Direct
  query today; per-tenant `HybridCache` (tag `"students"`, already invalidated by the
  `Activate`/`Complete` handlers) can layer on later — see §4.6.
- **Registration:** `services.AddScoped<IActivePeriodProvider, ActivePeriodProvider>()` in the
  Students module's `Extensions.cs`.
- **Other modules** inject `IActivePeriodProvider` (from Core) — no reference to Students.Core.

### 4.3 Auto-close on activate (FR-A1 / FR-A2)

Change `ActivatePeriodHandler` so that, for the tenant, it:
1. Loads the current active period via `repository.GetActivePeriodAsync(excludeId: command.Id)`.
2. If present, calls `priorPeriod.Complete()` (the closure that triggers promotion/repetition via
   `PeriodCompleted` → `PromotionService`). Keep the existing `DbUpdateConcurrencyException →
   ConcurrencyException` wrap; both writes go through one `SaveChangesAsync` where possible, or
   accept the existing two-step update.
3. Then `period.Activate()` as today.

> Note: mapping "close" to `Complete()` reuses the existing promotion flow (FR-A4). If a softer
> "close without completing" is later needed, add `Period.Close()` → a new `Closed` status; for
> this draft, `Complete()` is sufficient and matches "during period closure, promotions or
> repetitions are made."

### 4.4 Enrollment guard (FR-A3)

In `EnrollStudentHandler` (server-side authority):
1. Resolve the active period via `IActivePeriodProvider.GetActivePeriodAsync()`.
2. If null → throw a new `PeriodNotOpenException` (add to `Domain/Exceptions`, alongside
   `PeriodNotFoundException`/`PeriodOverlapException`).
3. If `command.PeriodId != activePeriod.Id` → throw `PeriodNotOpenException` (guard against
   enrolling into a non-active period through the API).

UI (`GradeLevelWizard.razor`): the entry gate (§4.7 / FR-A6) is the primary control — the
wizard does not render steps until an active period exists, so the user opens/creates one first.
The add-students-to-grades action inside Step 2 remains guarded server-side (this handler) as
defense-in-depth, mirroring FR-19's default-tenant disable pattern. The wizard resolves the active
period via `ListPeriodsAsync` (not injected `IActivePeriodProvider` — Blazor-circuit caveat, §4.7)
and passes the resolved active `PeriodId` (not a user selection).

### 4.5 Promotion vs. repetition (FR-A4)

Extend the carry-forward in `PromotionService.PromoteStudentsAsync` to decide the target grade
level per enrollment. Proposed approach (pluggable, default rule):
- Introduce `IPromotionRule.Evaluate(StudentEnrollment, fromPeriod, toPeriod) → Guid nextGradeLevelId`
  (implementation in Students.Core). Default rule: target the tenant's `GradeLevel` whose
  `Level == currentLevel + 1`; if none exists, repeat at the same `GradeLevelId`.
- Capture the outcome on the *new* next-period enrollment (e.g. a `PromotionOutcome`
  `{ Promoted, Repeated }` on `StudentEnrollment`, or a closure record), so reporting can show
  who was promoted vs. held back.
- Keep `NextPeriodId` as the linkage; keep the duplicate-enrollment guard; keep the
  grade-subject / student-subject assignment copy logic (already present).

This reuses today's promotion plumbing and only changes *which `GradeLevelId`* the next-period
enrollment receives.

### 4.6 Caching & invalidation

- **Status (synced with code):** the provider currently resolves the active period with a **direct
  query** (no `HybridCache` yet). The `Activate`/`Complete` handlers already call
  `cache.RemoveByTagAsync("students")`, so when the per-tenant cache is added (tag `"students"`,
  key `active-period:{tenantId}`) the open/close transitions will invalidate it automatically — no
  new cache-key management required beyond wiring `HybridCache.GetOrCreateAsync` in
  `ActivePeriodProvider`.

### 4.7 Wizard entry gate (FR-A6 — Idea C)

**Placement:** `GradeLevelWizard.razor` renders the `FluentWizard` steps **only after** an active
period is confirmed for the tenant. If none, it renders a standalone **"Open a term"** panel in
place of the steps (a prerequisite gate, not a wizard step). This treats period opening as a
*tenant-level prerequisite* — there is exactly one active period per tenant, independent of any
single grade — which matches "before students can be added, periods must be opened."

**Resolution (Blazor-circuit caveat — important).** The component MUST resolve the active period
**via the API**, not by injecting `IActivePeriodProvider`. `ITenantProvider` is `AsyncLocal` and does
**not** flow into a Blazor Server circuit (FR-19); the wizard already reads the tenant from
`AuthenticationStateProvider` for this same reason. So the gate reuses the existing client-side
load: `Api.ListPeriodsAsync()` then `periods.FirstOrDefault(p => p.Status == "Active")`.
(`IActivePeriodProvider` stays the mechanism for *server handlers* and *other modules* — see §4.2.)

**Reconcile date-derived vs active.** The wizard's current `_currentPeriod` is derived from the
*date* rule (`StartDate <= today && EndDate >= today`). The gate MUST switch to the **status-derived
active** period so a `Draft` period covering today cannot let the user reach enrollment and then
fail the server guard (FR-A3). Keep the date-derived value only as a display fallback for labels.

**Entry-gate contents (when no active period):**
- A compact create-and-activate form: `Name`, `StartDate`, `EndDate`.
- A **"Create & Open"** action chaining `Api.CreatePeriodAsync(req)` → `Api.ActivatePeriodAsync(id)`
  (enrollment needs `Active`, not `Draft`).
- Optionally, a list of any existing `Draft` periods with an **"Open"** button (`ActivatePeriodAsync`)
  — reuses the same activation path without forcing a create.
- On success, re-resolve the active period and render the wizard steps.

**Rollover confirmation.** Opening a period auto-closes the prior active one (FR-A1) and triggers
promotion/repetition. For a brand-new tenant (no periods) this is safe; for an existing tenant it is
a *term rollover*. If an active period already exists when the user opens a new one, show a confirm
("This will close `<old period>` and promote its students") before activating.

**No period picker.** There is at most one active period per tenant; the wizard uses that active
period for enrollment and never offers a period selector. Choosing/opening a non-active period is a
tenant period-management concern, out of scope for the grade wizard.

## 5. Data model notes

**No new tables are required for the core flow.** Reuse:
- `Period` (status + `NextPeriodId`) — already tenant-scoped, already has the lifecycle.
- `StudentEnrollment` (`StudentId, PeriodId, GradeLevelId, Status`) — already tenant-scoped.
- Only *optional* additive: a `PromotionOutcome` column on `StudentEnrollment` (or a separate
  closure/promotion record) to distinguish promoted vs. repeated for reporting. This is the only
  migration the promotion-vs-repetition part needs.

Tenant purity is automatic: `IActivePeriodProvider` reads `CurrentTenantId` from `ITenantProvider`,
so it is per-tenant under both per-request claims and `RunWithExplicitTenantAsync` (workers). No
tenant id is threaded by callers.

## 6. Worker / `PromotionService` integration

`PromotionService` already enumerates tenants (`ITenantDirectory`) and runs per-tenant under
`RunWithExplicitTenantAsync`. With FR-A1, closing the active period is triggered synchronously by
`ActivatePeriodHandler`; the nightly `PromotionService` continues to handle *date-expired* auto-
completion and the actual promotion/repetition writes. No change to the tenancy enumeration; the
active-period resolution inside the worker uses the same `IActivePeriodProvider` (resolved within
the per-tenant scope).

## 7. UI (wizard)

- **Primary "open period" surface = the entry gate (§4.7 / FR-A6).** The wizard does not render its
  steps until an active period exists; the standalone "Open a term" panel is shown instead. This is
  where the user opens/creates the tenant's term before any grade/student work.
- **Steps then run against the active period.** Once the gate is satisfied, enrollment (Step 2)
  passes the resolved active `PeriodId` (today it passes `_currentPeriod.Id` — switch that to the
  active-period id per §4.7). The wizard never presents a period picker.
- **Component resolves via API, not `IActivePeriodProvider`.** The Blazor Server circuit cannot rely
  on the `AsyncLocal` `ITenantProvider` (FR-19), so the gate resolves the active period through
  `ListPeriodsAsync` + active filter. `IActivePeriodProvider` remains for server handlers and other
  modules.
- **Defense-in-depth:** the add-students-to-grades action is also guarded server-side by the
  `EnrollStudent` handler (FR-A3); the entry gate is the friendly front-end, not the enforcement.
- This mirrors FR-19: strict-entity actions are gated by context (here: an open period rather than a
  real tenant).

## 8. Open questions

- **Q1.** Should "close old active" map to `Complete()` (reuses promotion) or to a new softer
  `Closed` status? Draft assumes `Complete()`.
- **Q2.** Promotion rule source of truth: explicit per-student decision (teacher sets
  promoted/repeated before closure) vs. automatic `Level + 1` rule vs. both (automatic default,
  manual override)? Draft assumes automatic default + optional override.
- **Q3.** Should repetition be modeled as "new enrollment in next period at same grade" (carry-
  forward, current behavior) or as a status on the *existing* enrollment? Draft assumes new
  next-period enrollment (consistent with promotion).
- **Q4.** Where should `IActivePeriodProvider` live physically — `SchoolCollab.Core.Tenancy` or a
  new `SchoolCollab.Core.Periods` namespace? Draft assumes `Tenancy` for parity with
  `ITenantProvider`.

**Decided: wizard integration = Idea C (prerequisite entry gate before steps render).** Chosen over
an inline Step-2 affordance (Idea A) and a dedicated Term wizard step (Idea B) because period opening
is a tenant-level prerequisite (one active per tenant), not a per-grade concern, and the gate keeps
the existing 2-step wizard content intact. See §4.7 / FR-A6.
- **Q5 (residual).** Should the entry gate be an inline panel on the wizard itself, or a modal
  launched from the Grade-Levels landing page (so the user opens a term before even entering the
  wizard)? Draft assumes the inline panel on the wizard; the landing-page modal is an alternative
  entry point.

## 9. Implementation steps (stub)

1. Add `PeriodNotOpenException` to `Students.Core/Domain/Exceptions`.
2. Add `IActivePeriodProvider` (+ `ActivePeriod` record) to `SchoolCollab.Core`; implement
   `ActivePeriodProvider` in Students.Core (returns the Core `ActivePeriod` projection, direct
   query — `HybridCache` is a follow-up per §4.6); register scoped in `Extensions.cs`.
3. Update `ActivatePeriodHandler` for auto-close (FR-A1/A2).
4. Add enrollment guard to `EnrollStudentHandler` (FR-A3).
5. Extend `PromotionService` with `IPromotionRule` (promotion vs. repetition) (FR-A4); optional
   `PromotionOutcome` migration.
6. Wire wizard entry gate (Idea C, §4.7 / FR-A6): render "Open a term" panel instead of steps
   when no active period; resolve via `ListPeriodsAsync` + active filter (not `IActivePeriodProvider`);
   chain `CreatePeriodAsync` → `ActivatePeriodAsync`; add rollover confirm; re-render steps on success.
   Switch enrollment to use the resolved active `PeriodId`.
7. Unit tests: auto-close invariant, enrollment-without-active throws, promotion-vs-repetition
   outcomes, `IActivePeriodProvider` per-tenant resolution (mirror `StudentsStrictTenancyTests`),
   and wizard entry-gate shows panel when no active period / renders steps once one exists.

## 10. Implementation status (synced with code)

Implemented in this pass (builds green; Students period + promotion + strict-tenancy unit tests pass):

- **FR-A1 / FR-A2** — `ActivatePeriodHandler` now **completes** any other currently-active period
  for the tenant before activating the new one (was: throw). Test
  `Activate_WhenAnotherIsActive_ClosesPriorAndActivatesNew` updated accordingly.
- **FR-A3** — `EnrollStudentHandler` resolves the active period via `IActivePeriodProvider` and
  throws `PeriodNotOpenException` (new, in `Domain/Exceptions`) when none exists or when the
  targeted `PeriodId` is not the active one.
- **FR-A4** — added `IPromotionRule` / `DefaultPromotionRule` (advance one `GradeLevel.Level`,
  else repeat) and wired it into `PromotionService.PromoteStudentsAsync` so the next-period
  enrollment targets the resolved grade level.
- **FR-A5** — `IActivePeriodProvider` (Core) + `ActivePeriod` projection + `ActivePeriodProvider`
  (Students.Core), registered scoped. **Returns the Core `ActivePeriod` DTO, not `Period?`**
  (deviation from the original draft — keeps `Core` module-agnostic).
- **FR-A6** — `GradeLevelWizard.razor` renders an **"Open a term"** entry gate (create-and-activate
  form + list of existing `Draft` periods with "Open") instead of the steps when no active period
  exists; resolves the active period client-side via `ListPeriodsAsync` (Blazor-circuit caveat),
  then re-renders the steps. Enrollment now targets the active `PeriodId`.

### Deviations from the original draft (intentional)

1. **`IActivePeriodProvider` returns `ActivePeriod` (Core record), not `Period?`.** Required to
   avoid a `SchoolCollab.Core → SchoolCollab.Students.Core` dependency (the layering rule in §0).
2. **No `HybridCache` in the provider yet.** Direct query is the source of truth; the per-tenant
   cache (tag `"students"`) is a follow-up — see §4.6.

### Follow-ups (not yet done)

- `PromotionOutcome` column / closure record on `StudentEnrollment` for reporting
  promoted-vs-repeated (§4.5, §5 — optional).
- Rollover *confirmation* dialog — the entry gate only appears when no active period exists, so
  opening there cannot close a prior active one; the confirm matters for a future period-management
  screen (the auto-close handler already enforces it server-side).
- Per-tenant `HybridCache` wiring in `ActivePeriodProvider` (§4.6).
