# Admin portal default tenant

**Status:** implemented
**Scope:** `src/SchoolCollab.Admin` (Blazor Server admin host), dev / `TestAuth` mode
**Round:** solo (`feat/recorder-takeover` working tree; isolation base `a736ac59`)

## 1. Findings

### 1.1 The problem

The admin portal opened on the **system / no-tenant** context every time. Tenant-scoped
surfaces were hidden or disabled (`TenantGate`, `VisibleTenantService`) until an operator
manually picked a tenant from the dev tenant switcher in the header. There was no way to
say "this portal should open on *this* tenant".

### 1.2 What already existed

Tenant resolution in `TestAuth`/dev mode was a three-step fallback, all of it in place:

1. `x-tenant-id` header (stamped by `TenantPropagationDelegatingHandler` from the shared
   Redis selection) — lets the API hosts agree with the shell;
2. `IDevTenantSelection` (Redis key `dev:tenant-selection`, one global value);
3. `TestAuthHandlerOptions.TenantId` — documented as *"the tenant ID to use when no tenant is
   explicitly selected via the dev tenant switcher"*, defaulting to `Guid.Empty` and
   **never configured by any host**.

So the *slot* for a default tenant existed and was inert. What was missing was (a) a place
to designate one, (b) a per-operator preference, and (c) any mechanism to make a value
applied at load actually take effect.

### 1.3 The constraint that shaped the design

A Blazor Server circuit's tenant claim is fixed when `HttpContext.User` becomes the
circuit's `AuthenticationState` at circuit start. Applying a default during
`OnInitializedAsync` therefore changes Redis but **not** the claim the running circuit is
using. Without a reconciliation step the feature would have shipped in a state where the
dropdown said "(default: Dev School)" while every tenant-scoped page still behaved as if no
tenant were selected — the worst kind of half-working.

### 1.4 Alternatives considered

| Option | Why not |
|---|---|
| Deployment config (`AdminPortal:DefaultTenantId` → `IConfiguration` → `TestAuthHandlerOptions.TenantId`) | Smallest change, and it maps onto the existing hook — but "designate" implies a deliberate, persistent, runtime-changeable choice, and this needs a redeploy to change. Rejected as the primary mechanism. |
| Browser `localStorage` | Cannot be seen by `TestAuthHandler` or any API host; per-browser, not per-operator. |
| Keycloak realm attribute | Surfaces naturally in the AuthPortal's user form, but does not exist under `TestAuth`, which is the only mode where the switcher (and the problem) exists. |
| Per-operator preference only | Does not answer "which tenant should the portal open on *by default*" for a fresh environment. |
| A `DefaultTenantId` column on the settings-local `User` row | `User` is dead infrastructure — declared and mapped in `SettingsDbContext`, never written or read anywhere. Reusing it would have dragged the whole identity-sync lifecycle into scope. |

## 2. Decisions (owner-settled)

1. **Portal:** the Blazor admin host only. The Python `AuthPortal` has no portal-wide tenant
   context — its tenant picker binds a realm user's `tenant_id` claim.
2. **Mode:** dev / `FEATURE:DisableOIDCAuth` only. Production stays claim-authoritative, so
   there is no tenancy hole.
3. **Platform default:** `Tenant.IsDefault` + migration + Settings API + admin UI —
   runtime-changeable and auditable.
4. **State model:** three states, explicit pick persists. `(system / no tenant)` is kept as a
   real, explicitly selectable state because shared-blueprint coded-value editing depends on
   it.
5. **Preference:** a narrow `admin_user_preferences` table keyed by the OIDC `sub` claim,
   self-service from the switcher.
6. **Precedence:** explicit switcher pick → operator preference → platform default →
   `Guid.Empty`.
7. **Seed:** dev designates `Dev School` (`…0002`); production seeds nothing (fail-closed).
   *(Owner-confirmed 2026-10-13 as the dev default.)*

## 3. What was built

### 3.1 Settings domain and persistence

- `Tenant.IsDefault` + `MarkAsDefault()` / `ClearDefault()`, both idempotent so a caller can
  apply them unconditionally. `Tenant.Create(name, type, isDefault = false)`.
- `TenantConfiguration`: `is_default` (required, `DEFAULT false`) and a **partial** unique
  index `ix_tenants_is_default_unique ... WHERE is_default`. PostgreSQL, not application
  code, owns the "at most one default" invariant.
- `AdminUserPreference`: global row (no query filter, listed in `GlobalEntityAllowList`)
  keyed by `SubjectId` with a unique index. `DefaultTenantId` is nullable and the two empty
  values are distinct — `null` = no preference recorded, `Guid.Empty` = an explicit
  preference for the system context.
- Migration `20261013181622_AddTenantDefaultAndAdminUserPreference`.

### 3.2 CQRS and the API

- `SetDefaultTenant`, `SetUserDefaultTenant`, `GetUserDefaultTenant`; `ListTenants` now
  projects `TenantDto.IsDefault` (additive, defaulted, so the auth portal's mediated reader
  is unaffected).
- `SetDefaultTenantHandler` clears the previous default in one save and sets the new one in a
  second, **inside one explicit transaction**. That is not incidental: EF Core does not
  order the UPDATEs within a single `SaveChanges`, so a one-batch move can transiently
  violate the partial unique index.
- Routes: `PUT /api/tenants/{id}/default`, `GET|PUT /api/tenants/me/default`. The `/me`
  routes take the subject from the bound `ClaimsPrincipal` — never the body — so no caller
  can read or write another operator's preference. `ICurrentUser` was deliberately left
  alone, which also spared seven test doubles an interface change.

### 3.3 Applying the default

- `IDevTenantSelection` gained a second slot (`dev:tenant-default`): the *applied default*.
  It is separate from the *selection* on purpose — had the portal written its resolved
  default into the selection slot, "no explicit pick" and "the operator chose this tenant"
  would have become the same state and a later change to the platform default would never
  take effect.
- `AdminDefaultTenantResolver` (admin host) resolves the chain and publishes the result.
  `NeedsReload(explicitSelection, effectiveDefault, claimTenant)` is a **pure function**
  expressing the circuit-reconciliation decision; it is a fixed point, which is what makes
  the single reload unable to loop.
- `TestAuthHandler` and `TenantPropagationDelegatingHandler` consult
  selection → applied default → configured options, testing `HasValue` and not
  `!= Guid.Empty` so an explicit system-context pick survives.
- `DevTenantSwitcher` renders the three states, offers "Default" (pin the tenant in view)
  and "Clear" (release the preference), and reconciles the circuit in
  `OnAfterRenderAsync(firstRender: true)` — after the render phase, where navigation is
  legal.

### 3.4 Surfaces

- `/tenants` (`Settings.Application` RCL) lists the registry and designates the platform
  default; linked from `/settings`.
- `DevIdentitySeeder` designates `Dev School`, guarded by
  `AND NOT EXISTS (SELECT 1 FROM tenants WHERE is_default)` so it fills the empty state and
  never overwrites an operator's choice.

## 4. Verification

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | 0 errors |
| `SchoolCollab.Settings.Tests.Unit` | 554 passed |
| `SchoolCollab.Admin.Tests.Unit` | 675 passed |
| `SchoolCollab.Core.Tests.Unit` | 133 passed |
| `SchoolCollab.ArchitectureTests.Unit` | 105 passed |
| `SchoolCollab.Assignments.Tests.Unit` | 1135 passed |
| `SchoolCollab.Students.Tests.Unit` | 624 passed |
| `SchoolCollab.Auth.Tests.Unit` | 224 passed |
| `SchoolCollab.Families.Tests.Unit` | 47 passed |
| `SchoolCollab.MigrationService.Tests.Unit` | 9 passed |
| `SchoolCollab.Settings.Tests.Integration` | **deferred by the owner (2026-10-13).** Docker is not running in this environment, so every Testcontainers class in the project fails identically at `ClassInitialize` (`DockerUnavailableException`) — including pre-existing classes untouched by this work. `TenantsApiTests` is written and compiles but has **never executed**; it must run green before this change is treated as verified. |

New coverage: `TenantDefaultDesignationTests` (14), `AdminDefaultTenantResolverTests` (12),
`DevTenantSwitcherTests` (7), `TenantsApiTests` (9, integration), plus additions to
`TestAuthHandlerDevTenantTests` (3) and `DevTenantSelectionTests` (4) and
`TenantPropagationDelegatingHandlerTests` (3).

The integration class exists specifically because the in-memory provider does **not**
enforce filtered unique indexes — the "at most one platform default" invariant is only
truly proven against real PostgreSQL. Until it runs, the migration's index definition is
reviewed-but-unexecuted: the generated SQL reads correctly (`filter: "is_default"`,
`defaultValue: false`), and `SetDefaultTenantHandler`'s clear-then-set ordering is a
response to that index, but neither has been exercised against a live server.

## 5. Limits and follow-ups

- **Dev-only.** Under OIDC the tenant is claim-authoritative; the whole mechanism is inert
  and `DevTenantSwitcher` does not render. The preference rows are written for any
  authenticated caller, so they are ready if production switching is ever enabled.
- **Effectively single-operator in dev**, because `TestAuthHandler` emits one synthetic
  subject (`test-user-id`). The per-operator model is real (unique key, `/me` routes) but only
  becomes multi-user in production.
- **Clearing a preference is required** to see the platform default again — with a preference
  recorded, the "(default …)" entry resolves to the preference. Hence the "Clear" action.
- The AuthPortal does not surface the preference; that would reopen the mediated-claim seam
  and is deliberately out of scope.
