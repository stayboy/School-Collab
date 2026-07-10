# Spec: `TenantGate` — reusable component for tenant-visibility gating

Status: Draft · Branch: `feature/ui-visible-tenancy-guard`
Related: [`ui-visible-tenancy-guard.md`](./ui-visible-tenancy-guard.md) (FR-VT series),
[`global-tenant-filter.md`](./global-tenant-filter.md)

## 1. Problem

The `ui-visible-tenancy-guard` work gated 11 Admin pages on *visible tenancy* by
scattering `@if (_isRealTenant) { … } else { <FluentMessageBar warning> }` blocks
(and `CreateEnabled`/`SearchEnabled`/`EmptyMessage` bindings on `LandingPage`
pages) across the codebase. That works, but:

- The gating logic is **duplicated** in 11 places instead of living in one
  reusable surface.
- The "no tenant" UX (warning text, banner style) is copy-pasted and can drift.
- There is no single, declarative, `[Authorize]`-style primitive a future
  developer can reach for when adding a new strict-tenant surface.

## 2. Goal

Provide a single, reusable Blazor component — `TenantGate` — that gates its
content on visible tenancy, mirroring the ergonomics of `[Authorize]`
(`AuthorizeView`) but for **tenancy visibility** instead of authentication.

- Reuses `VisibleTenantService` (the single source of truth for the "real tenant"
  rule — see `ui-visible-tenancy-guard.md` §4.1).
- Declarative: `<TenantGate>…</TenantGate>` with an optional custom `Fallback`.
- Two modes: `Hide` (default) and `Disable`.

## 3. Component

**Location:** `src/SchoolCollab.Admin.Shared/Components/TenantGate.razor`
(project `SchoolCollab.Admin.Shared`, already on the `_Imports` path for every
Admin module).

**Parameters**

| Parameter | Type | Purpose |
|-----------|------|---------|
| `ChildContent` | `RenderFragment?` | The gated content. Always rendered in `Disable` mode. |
| `Fallback` | `RenderFragment?` | Optional content shown instead of the default banner when no real tenant (only in `Hide` mode). |
| `Mode` | `TenantGateMode` (`Hide` default \| `Disable`) | `Hide` shows `ChildContent` only with a real tenant; `Disable` always renders `ChildContent` but disables it (via a disabled `<fieldset>`) without a real tenant. |

**Behavior**

- Resolves tenancy once via `VisibleTenantService.GetScopeAsync()` in
  `OnInitializedAsync`. Re-resolves on each page load, so switching tenant via
  the dev tenant switcher (which forces a full reload) updates the gate — same
  mechanism the existing pages already rely on.
- `Hide` + no `Fallback` → renders a default `FluentMessageBar`
  ("No tenant selected — select a tenant using the dev tenant switcher to use
  this feature.").
- Does **not** touch the server-side `ITenantProvider` (AsyncLocal; not reliably
  available in a Blazor Server circuit). Pure UI/claim read via
  `VisibleTenantService`.

**Usage**

```razor
@* Hide + default banner *@
<TenantGate>
    <GradeLevelWizard />
</TenantGate>

@* Hide + custom fallback *@
<TenantGate>
    <ChildContent><StudentCreateForm /></ChildContent>
    <Fallback><FluentMessageBar>You have no tenant assigned. Student records are tenant-scoped — pick a tenant to continue.</FluentMessageBar></Fallback>
</TenantGate>

@* Disable children instead of hiding *@
<TenantGate Mode="TenantGateMode.Disable">
    <FluentButton OnClick="CreateAsync">Create</FluentButton>
</TenantGate>
```

## 4. Relationship to API-call gating (important)

`TenantGate` is a **render** gate only. It does **not** prevent the page's
`*ApiClient` calls. The parent spec's hard requirement — *skip all
tenant-scoped API calls when `!IsRealTenant`* (VT-FR-2 / VT-FR-7) — remains the
**page's** responsibility:

- Each page still resolves tenancy up front in `OnInitializedAsync`
  (`_isRealTenant = (await VisibleTenant.GetScopeAsync()).IsRealTenant;`) and
  returns early before any API call when `!_isRealTenant`.
- `TenantGate` then handles only the *visibility* of the already-safe content.

The double resolve (page + `TenantGate`) is cheap — both are auth-state reads,
no network. This keeps the two concerns cleanly separated and keeps the
zero-API-call guarantee verifiable in tests (see `GradeLevelsTenancyTests`).

## 5. Which surfaces use `TenantGate`

**Form / create / detail / edit / wizard pages (8)** — these had a single
`@if (_isRealTenant) { … } else { warning }` render block, which maps 1:1 onto
`<TenantGate Fallback="warning">…</TenantGate>`:

- `Students/Create.razor`, `Students/Edit.razor`, `Students/Detail.razor`
- `Students/GradeLevels/Edit.razor`, `Students/GradeLevels/GradeLevelWizard.razor`
- `Students/Periods.razor`
- `Students/Subjects/Create.razor`, `Students/Subjects/Edit.razor`

**`LandingPage`-based list pages (3: `Index`, `GradeLevels`, `Subjects`)** — these
gate via the `LandingPage` component's declarative parameters
(`CreateEnabled`, `SearchEnabled`, `EmptyMessage`), not an `@if` block. That is
the idiomatic fit for `LandingPage` and was the explicit design in
`ui-visible-tenancy-guard.md` §4.2 (no edits to `LandingPage.razor`; show the
grid with an empty-state message, not a bare banner). Wrapping them in
`TenantGate` would hide the grid chrome and change the UX the parent spec chose,
so they **keep the binding gate**. They remain covered by VT-FR-2/3 and the
existing `GradeLevelsTenancyTests`.

> If uniformity is later preferred, the list pages can be wrapped in
> `<TenantGate Fallback="@(_emptyMessageFragment)">` — but that hides the grid
> when no tenant and is a deliberate UX change, not a bug fix.

## 6. Requirements (TG-FR)

| ID | Requirement |
|----|-------------|
| **TG-FR-1** | `TenantGate` MUST resolve visible tenancy via `VisibleTenantService` and MUST NOT touch the server-side `ITenantProvider`. |
| **TG-FR-2** | `TenantGate` MUST support `Hide` (default) and `Disable` modes. |
| **TG-FR-3** | In `Hide` mode with no `Fallback`, `TenantGate` MUST render a default warning `FluentMessageBar`. |
| **TG-FR-4** | Any module that uses `<TenantGate>` MUST have `VisibleTenantService` registered (the Students Admin module already does; other modules add it when they adopt the component). |
| **TG-FR-5** | Pages that use `<TenantGate>` MUST still resolve tenancy up front to skip tenant-scoped API calls — render gating and API-call gating are separate concerns. |

## 7. Acceptance criteria (AC)

1. Render `<TenantGate>` with a real tenant → `ChildContent` shows, no warning.
2. Render `<TenantGate>` with no real tenant and no `Fallback` → default
   "No tenant selected" banner shows, `ChildContent` hidden.
3. Render `<TenantGate>` with no real tenant and a custom `Fallback` → `Fallback`
   shows, `ChildContent` hidden, default banner absent.
4. Render `<TenantGate Mode="Disable">` with no real tenant → `ChildContent`
   shows but wrapped in a disabled `<fieldset>`.
5. Each refactored page still skips **all** tenant-scoped API calls when
   `!IsRealTenant` (no regression to VT-FR-7), verified by the existing
   `GradeLevelsTenancyTests` counting handler.
6. `dotnet build` = 0 errors; `dotnet test` (Admin unit) = 0 failures.

## 8. Testing

`tests/SchoolCollab.Admin.Tests.Unit/TenantGateTests.cs` (bUnit) covers AC 1–4 by
registering a `MutableAuthenticationStateProvider` + `VisibleTenantService` and
rendering `<TenantGate>` with real/default tenants. The existing
`GradeLevelsTenancyTests` and `GradeLevelWizardTenancyTests` continue to prove the
zero-API-call guarantee and the wizard's live re-resolution.

## 9. Open considerations (not adopted)

- **Attribute decoration `[RequireTenant]`.** Blazor does not natively read
  arbitrary attributes; `[Authorize]` only works because `AuthorizeRouteView`
  reflects on the page type. Mimicking it for tenancy would require a custom
  `RouteView` (the repo currently uses a plain `<RouteView>`, not
  `AuthorizeRouteView`) and would gate **pages only** — failing to cover wizards
  and button groups. `TenantGate` covers all three with no router changes, so
  attribute decoration is deferred.
- **Live `AuthenticationStateChanged` re-resolution inside `TenantGate`.** Skipped
  on purpose: the dev tenant switcher forces a full reload, and the existing pages
  already re-resolve per load. Only `GradeLevelWizard` needs live updates (it
  keeps its own `AuthenticationStateChanged` subscription per
  `ui-visible-tenancy-guard.md` §4.5).

## 10. Migration notes

The 8 form/wizard pages were migrated by replacing their top-level
`@if (_isRealTenant) { <real content> } else { <warning> }` with
`<TenantGate Fallback="<warning>"><ChildContent><real content></ChildContent></TenantGate>`,
preserving the exact prior warning text (so UX and any page-level assertions are
unchanged). The page's `_isRealTenant` field and its `OnInitializedAsync` API-gating
`return` are retained (TG-FR-5).

## 11. Unified gate (`Gate` / `GateBase`)

`TenantGate` is the tenant-visibility specialization of the shared gate engine
described in [`ui-gate-component.md`](./ui-gate-component.md). The gating logic
(resolve, `Hide`/`Disable` modes, `Fallback`/`DisabledContent`, reactivity,
disposal) is being lifted into a common `GateBase`, and `TenantGate` will derive
from it — supplying a single `TenantSelectedCondition`. **This is a refactor
only:** the public API (`Mode`, `Fallback`, `ChildContent`) and the behavior
covered by TG-FR-1..5 are unchanged, so the 8 existing consumers need no edits.
A new `FeatureFlagGate : GateBase` provides the same first-class, reactive
surface for runtime feature flags.
