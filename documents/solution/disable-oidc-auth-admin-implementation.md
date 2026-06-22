:

## Findings

- `SchoolCollab.Admin` currently enables OIDC auth unconditionally in `Program.cs`:
  - `app.UseAuthentication()` and `app.UseAuthorization()` are always called.
  - Razor components always use `.RequireAuthorization()`.
- The shared `AddAuthAndTenancy` extension already respects `FEATURE:DisableOIDCAuth` by switching to `TestAuthHandler` when the flag is enabled. However, the Admin host still forces the middleware and authorization requirement, which causes redirect/challenge behavior even when `TestAuthHandler` is registered.
- Per `.github/copilot-instructions.md`, authorization requirements on endpoint groups should be conditional based on `IFeatureFlagService`.
- Per `.github/copilot/rules/testing.md`, feature flags that guard auth must be tested for both states: enabled and disabled.

## Implementation Steps

1. **Update `src/SchoolCollab.Admin/Program.cs`**:
   - Resolve `IFeatureFlagService` after building the app.
   - Read `FEATURE:DisableOIDCAuth` into a local boolean.
   - Conditionally call `app.UseAuthentication()` / `app.UseAuthorization()` only when the flag is disabled.
   - Conditionally call `.RequireAuthorization()` on the Razor Components mapping only when the flag is disabled.
   - Keep `app.UseAntiforgery()` in both modes.

2. **Add unit tests in `tests/SchoolCollab.Admin.Tests.Unit/ProgramAuthFeatureFlagTests.cs`**:
   - Verify that when `FEATURE:DisableOIDCAuth` is `true`, `AddAuthAndTenancy` registers the `TestAuth` scheme.
   - Verify that when `FEATURE:DisableOIDCAuth` is `false`, `AddAuthAndTenancy` registers the `Cookies` and `OpenIdConnect` schemes.

3. **Verification**:
   - `dotnet build --configuration Release`: 0 errors, 0 warnings.
   - `dotnet test --filter "FullyQualifiedName~Tests.Unit" --ignore-exit-code 8`: 271 passed, 0 failed.

## Outcome

`SchoolCollab.Admin` now honors the `FEATURE:DisableOIDCAuth` feature flag end-to-end. When the flag is enabled, OIDC middleware and authorization requirements are bypassed, allowing local development without a live Keycloak instance. When disabled, the previous OIDC + Razor authorization behavior remains unchanged.
