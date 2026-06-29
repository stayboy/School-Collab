## Findings

- `SchoolCollab.Admin` currently enables OIDC auth unconditionally in `Program.cs`:
  - `app.UseAuthentication()` and `app.UseAuthorization()` are always called.
  - Razor components always use `.RequireAuthorization()`.
- `AddAuthAndTenancy` switches to `TestAuthHandler` when `FEATURE:DisableOIDCAuth` is enabled. With this change, the flag is now the **sole** determiner for disabling auth; it is no longer coupled to a specific environment name such as `Testing`.
- Per `.github/copilot-instructions.md`, authorization requirements on endpoint groups should be conditional based on `IFeatureFlagService`.
- Per `.github/copilot/rules/testing.md`, feature flags that guard auth must be tested for both states: enabled and disabled.
- The original implementation set the flag via each host's local
  `appsettings.Development.json` (Admin + the three APIs). That has since
  been superseded: the flag is now owned by the AppHost
  `Parameters:feature-flag-disable-oidc-auth` row and fanned out via
  `WithEnvironment("FeatureFlags__FEATURE__DisableOIDCAuth", param)` —
  see [`../configuration.md` §5](../configuration.md#5-featureflags--apphost-parameters)
  and [`feature-flag-workflow.md`](./feature-flag-workflow.md). The body
  below is retained for the original implementation record.

## Implementation Steps

1. **Update `src/SchoolCollab.Admin/Program.cs`**:
   - Resolve `IFeatureFlagService` after building the app.
   - Read `FEATURE:DisableOIDCAuth` into a local boolean.
   - Conditionally call `app.UseAuthentication()` / `app.UseAuthorization()` only when the flag is disabled.
   - Conditionally call `.RequireAuthorization()` on the Razor Components mapping only when the flag is disabled.
   - Keep `app.UseAntiforgery()` in both modes.

2. **Add `src/SchoolCollab.Admin/appsettings.Development.json`**:
   - Set `FeatureFlags:FEATURE:DisableOIDCAuth` to `true` so the Admin host disables OIDC when running under the `Development` environment.

3. **Add `FEATURE:DisableOIDCAuth` to API `appsettings.Development.json` files**:
   - `src/CodedValues/SchoolCollab.CodedValues.Api/appsettings.Development.json`
   - `src/Assignments/SchoolCollab.Assignments.Api/appsettings.Development.json`
   - `src/Students/SchoolCollab.Students.Api/appsettings.Development.json`
   - The Admin Blazor UI calls these APIs via `HttpClient` without attaching OIDC tokens. Each API reads feature flags from its own local `IConfiguration`, so the flag must be present in each API project for the endpoints to bypass `[Authorize]` requirements in Development.

4. **Add unit tests in `tests/SchoolCollab.Admin.Tests.Unit/ProgramAuthFeatureFlagTests.cs`**:
   - Verify that when `FEATURE:DisableOIDCAuth` is `true`, `AddAuthAndTenancy` registers the `TestAuth` scheme.
   - Verify that when `FEATURE:DisableOIDCAuth` is `false`, `AddAuthAndTenancy` registers the `Cookies` and `OpenIdConnect` schemes.

5. **Verification**:
   - `dotnet build --configuration Release`: 0 errors, 0 warnings.
   - `dotnet test --filter "FullyQualifiedName~Tests.Unit" --ignore-exit-code 8`: 271 passed, 0 failed.

## Outcome

`SchoolCollab.Admin` now honors the `FEATURE:DisableOIDCAuth` feature flag end-to-end. When the flag is enabled in the Admin host's configuration (e.g., `appsettings.Development.json`), OIDC middleware and authorization requirements are bypassed, allowing local development without a live Keycloak instance. When disabled, the previous OIDC + Razor authorization behavior remains unchanged.
