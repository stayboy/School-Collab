# Spec: Unified UI Gate (`Gate` / `GateBase`)

Status: Proposed · Branch: `feature/shared-ui-gate`
Related: [`tenant-gate-component.md`](./tenant-gate-component.md),
[`feature-flag-workflow.md`](../solution/feature-flag-workflow.md) (§UI Gating)

## 1. Problem

Tenant-visibility gating (`TenantGate`) and feature-flag gating share an
identical shape:

1. asynchronously resolve a boolean condition,
2. show / hide / disable the child UI based on it,
3. re-resolve when the tenant / identity changes.

Today tenant gating is a first-class component (`TenantGate`), but feature-flag
gating is done with scattered `IFeatureFlagService.IsEnabledAsync` calls in API
endpoints and a single Razor page — there is **no reactive Blazor surface** for
flags. The resolve-and-gate engine is therefore duplicated (or absent) per
concern.

Feature flags are **tenant-scoped**: `ConfigFeatureFlagService.IsEnabledAsync`
reads `ITenantProvider.GetTenantContext()` and resolves
`GET /api/features/{tenant}`. So both gates ultimately hinge on the current
tenant context — a strong signal they belong to one abstraction.

## 2. Goal

Provide one shared gate engine and two thin, semantic wrappers:

- **`GateBase`** — owns the shared logic + render (resolve conditions, `Hide` /
  `Disable` modes, `Fallback` / `DisabledContent`, reactivity, disposal).
- **`TenantGate`** — derives from `GateBase`; gates on *visible tenancy*.
  Public API unchanged; existing consumers (8 pages) need no edits.
- **`FeatureFlagGate`** — derives from `GateBase`; gates on a *runtime feature
  flag*, **reactively** (flips live when the flag changes in the Config UI).

## 3. Design

### 3.1 `IGateCondition`

```csharp
namespace SchoolCollab.Admin.Shared.Gating;

public interface IGateCondition
{
    Task<bool> EvaluateAsync(CancellationToken ct = default);
}
```

Implementations:
- `TenantSelectedCondition` — wraps `VisibleTenantService.GetScopeAsync()`;
  `true` when a real tenant is selected.
- `FeatureEnabledCondition(string key)` — wraps
  `IFeatureFlagService.IsEnabledAsync(key)`; tenant-aware automatically via the
  service.

### 3.2 `GateMode`

Generalizes `TenantGateMode`:

```csharp
public enum GateMode { Hide = 0, Disable = 1 }
```

Also `GateCombination { All = 0, Any = 1 }` for combining `Conditions`.

### 3.3 `GateBase` (shared engine)

Location: `src/SchoolCollab.Admin.Shared/Components/Gate/GateBase.razor`
(project `SchoolCollab.Admin.Shared`).

Parameters:

| Parameter | Type | Purpose |
|-----------|------|---------|
| `ChildContent` | `RenderFragment?` | Gated content. Always rendered in `Disable` mode. |
| `Fallback` | `RenderFragment?` | Shown instead of default banner when not passed (Hide mode). |
| `DisabledContent` | `RenderFragment?` | Shown when not passed in `Disable` mode (defaults to disabled `ChildContent`). |
| `Mode` | `GateMode` (`Hide` default) | `Hide` shows `ChildContent` only when passed; `Disable` always renders but disables it. |
| `Conditions` | `IReadOnlyList<IGateCondition>?` | Conditions combined by `Combination` (default `All` = AND). |
| `Combination` | `GateCombination` (`All` default \| `Any`) | How `Conditions` combine. |

Lifecycle / behavior:
- `OnInitializedAsync`: evaluate all `Conditions` → `_passed`.
- Subscribe to `AuthenticationStateProvider.AuthenticationStateChanged`
  (re-resolve + `StateHasChanged`). A flag-aware subclass additionally
  subscribes to `IFeatureFlagChangeNotifier.FeatureFlagsChanged`.
- `IDisposable`: unsubscribe from both.
- Render: if `_passed` → `ChildContent` (Disable mode wraps in a disabled
  container); else Hide → `Fallback`, Disable → `DisabledContent`.
- Hide + no `Fallback` → default `FluentMessageBar`
  ("No tenant selected — select a tenant using the dev tenant switcher to use
  this feature.") — kept from `TenantGate` for backward-compatible UX.

> **Implementation note (Blazor inheritance):** `GateBase` owns the render.
> `TenantGate` / `FeatureFlagGate` must reuse that render and not silently drop
> it. Safest is for `GateBase` to be the rendering component and the wrappers to
> set `Conditions` — either composition (`<GateBase Conditions="@(_c)">…`) or a
> C# base class that puts the render in `BuildRenderTree` with thin `.cs`
> subclasses. The exact approach is finalized during implementation; either way
> the wrappers contain **no gating logic of their own**.

### 3.4 `TenantGate : GateBase`

Injects `VisibleTenantService`; supplies a single `TenantSelectedCondition`.
Keeps `Mode` / `Fallback` / `ChildContent` API. No behavior change for the 8
existing consumers (TG-FR-1..5 unchanged).

```razor
<TenantGate>
    <GradeLevelWizard />
</TenantGate>
```

### 3.5 `FeatureFlagGate : GateBase` (reactive)

Parameter **`Key`** (string, e.g. `"FEATURE:EnableCodedValuesAiChat"`).
Injects `IFeatureFlagService` (the cached, tenant-aware
`ConfigFeatureFlagService` in the Admin host). Supplies
`FeatureEnabledCondition(Key)`.

**Reactive:** subscribes to `IFeatureFlagChangeNotifier.FeatureFlagsChanged`
(see §3.6) so the gate flips **live** when the flag is toggled in the Config UI
— no page reload. On change it re-evaluates and `StateHasChanged`.

Default `Fallback` (Hide, no custom): "This feature is not enabled for your
tenant."

```razor
<FeatureFlagGate Key="FEATURE:EnableCodedValuesAiChat">
    <AiChatPanel />
</FeatureFlagGate>
```

### 3.6 `IFeatureFlagChangeNotifier` (new)

A lightweight in-process signal so Blazor gates react to runtime flag changes
without polling.

```csharp
public interface IFeatureFlagChangeNotifier
{
    event Action? FeatureFlagsChanged; // null payload = any flag may have changed
}

public sealed class FeatureFlagChangeNotifier : IFeatureFlagChangeNotifier
{
    public event Action? FeatureFlagsChanged;
    public void Raise() => FeatureFlagsChanged?.Invoke();
}
```

Raised by the Settings client (`AddConfigFeatureFlagClient`) when it observes a
`FeatureFlagChanged` event on the `config` RabbitMQ exchange — the same event
that already invalidates the HybridCache. Registered as a singleton; both
`IFeatureFlagService` and `IFeatureFlagChangeNotifier` come from
`AddConfigFeatureFlagClient`.

Rejected alternative: interval polling — wasteful; the notifier reuses existing
event plumbing and matches the push-invalidation plan
(`feature-flag-workflow.md` §"How a runtime flag resolves").

## 4. Placement / dependencies

- Engine + both gates live in `SchoolCollab.Admin.Shared` (cross-module).
  `Settings.Admin` already references `Admin.Shared`, so `FeatureFlagGate` is
  usable there (CodedValues AI-chat toggle).
- `IFeatureFlagService` is registered in all hosts (config-only via
  `AddAuthAndTenancy`; cached tenant-aware `ConfigFeatureFlagService` via
  `AddConfigFeatureFlagClient` in the Admin host). `IFeatureFlagChangeNotifier`
  is registered by `AddConfigFeatureFlagClient`.

## 5. Migration

- `Settings.Admin/.../CodedValues/Index.razor` (currently
  `_aiChatEnabled = await FeatureFlags.IsEnabledAsync("FEATURE:EnableCodedValuesAiChat")`
  driving conditional markup) → wrap the AI-chat UI in
  `<FeatureFlagGate Key="FEATURE:EnableCodedValuesAiChat">…</FeatureFlagGate>`.

## 6. Requirements (UG-FR)

| ID | Requirement |
|----|-------------|
| **UG-FR-1** | `GateBase` MUST own all gating logic (resolve, modes, fallback, reactivity, dispose); wrappers contain no gating logic. |
| **UG-FR-2** | `GateBase` MUST support `Hide` (default) and `Disable` modes and an AND/Any `Combination` of `Conditions`. |
| **UG-FR-3** | `TenantGate` MUST keep its current public API and behavior (TG-FR-1..5 unchanged); existing 8 consumers need no edits. |
| **UG-FR-4** | `FeatureFlagGate` MUST gate on `Key` via `IFeatureFlagService` and be tenant-aware (service resolves per tenant). |
| **UG-FR-5** | `FeatureFlagGate` MUST react live to flag changes via `IFeatureFlagChangeNotifier` (re-evaluate + re-render, no reload). |
| **UG-FR-6** | Any module using a gate MUST register its dependencies (`VisibleTenantService` for `TenantGate`; `IFeatureFlagService` + `IFeatureFlagChangeNotifier` for `FeatureFlagGate`). |
| **UG-FR-7** | Pages using a gate MUST still resolve tenancy up front to skip tenant-scoped API calls (render gating ≠ API gating). |

## 7. Acceptance criteria (AC)

1. `<TenantGate>` with a real tenant → `ChildContent` shows, no warning; same as today.
2. `<FeatureFlagGate Key="FEATURE:EnableCodedValuesAiChat">` with flag on → child shows; flag off → `Fallback`.
3. Flip the flag in the Config UI while the page is open → `FeatureFlagGate` updates **without** reload.
4. `<GateBase Mode="Disable">` with unmet condition → `ChildContent` shown but disabled.
5. `dotnet build` = 0 errors; `dotnet test` (Admin unit) = 0 failures; `TenantGateTests` still pass.

## 8. Testing

- `TenantGateTests` (existing) unchanged — still pass.
- New `FeatureFlagGateTests` (bUnit): stub `IFeatureFlagService` +
  `IFeatureFlagChangeNotifier`:
  - flag on → child shown; flag off → `Fallback` (Hide) / disabled wrapper (Disable).
  - raising `FeatureFlagsChanged` flips the gate with no explicit re-render call.
- New `GateBaseTests`: AND vs Any combination.

## 9. Branch / commit / PR (repo convention)

- Branch `feature/shared-ui-gate`.
- Commits (Conventional Commits):
  - `feat(gate): add shared GateBase engine and IGateCondition`
  - `refactor(tenancy): derive TenantGate from GateBase`
  - `feat(flags): add reactive FeatureFlagGate + IFeatureFlagChangeNotifier`
  - `docs(spec): document unified UI gate; update related specs`
  - `test(gate): add FeatureFlagGate / GateBase bUnit tests`
  - `refactor(settings): use FeatureFlagGate in CodedValues AI chat`
- PR → squash-merge to `main` only after `Build & Test` green
  (`.github/merge-policy.md`).

## 10. Relationship to existing specs

- `TenantGate` is refactored onto `GateBase` (no consumer change) — see
  updated [`tenant-gate-component.md`](./tenant-gate-component.md).
- Feature-flag UI gating is documented in
  [`feature-flag-workflow.md`](../solution/feature-flag-workflow.md) §UI Gating.
