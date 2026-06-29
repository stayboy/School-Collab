# Centralized Feature Flags — Findings & Implementation

> ⚠️ **Superseded.** This document describes the original design that routed
> feature flags through a separate `SchoolCollab.Config` service via
> `AddRemoteFeatureFlags`. That HTTP overlay was removed in the
> `feature/centralize-outbox-ai-config-in-apphost` branch because the Config
> service was a placeholder (it just proxied a local JSON file) and the cost
> of a synchronous HTTP call at every API/Admin startup was not justified by
> a single dev-only flag. Feature flags now use the same AppHost
> `Parameters:` pattern as outbox exchanges and AI config; see
> [`../configuration.md` §2](../configuration.md#2-aspire-apphost--shared-infrastructure)
> for the current design. The original notes are retained below for context.

## Findings

The repository intended `SchoolCollab.Config` to be the single source of truth for feature flags. It exposes `IFeatureFlagService`, a `FeatureFlagService` implementation that reads the `FeatureFlags` configuration section, and a `GET /api/features` endpoint that returns all configured flags.

However, each consuming service (Admin, CodedValues.Api, Assignments.Api, Students.Api) currently registers `IFeatureFlagService` locally inside `AddAuthAndTenancy`, reading from its own `IConfiguration`. This caused configuration drift and forced us to scatter `appsettings.Development.json` entries such as `FeatureFlags:FEATURE:DisableOIDCAuth: true` into every API project. That contradicts the original design and makes it easy for local settings to disagree with one another.

The preferred fix is a custom `ConfigurationProvider` that pulls the `FeatureFlags` section from `SchoolCollab.Config` at startup. Because it plugs into `IConfiguration`, the existing `FeatureFlagService` and `AddAuthAndTenancy` code continue to work unchanged, but the values now originate from one central service.

## Implementation

### 1. Add `SchoolCollab.Config` to the Aspire AppHost

`SchoolCollab.Config` is added as a project resource in `src/AppHost/SchoolCollab.AppHost/Program.cs`. All APIs and the Admin host reference it so Aspire injects the service-discovery URL (`https+http://config`) and enforces startup order.

```csharp
var config = builder.AddProject<Projects.SchoolCollab_Config>("config")
    .WithReference(redis);
```

Each consumer receives:

```csharp
.WithReference(config)
.WaitFor(config)
```

### 2. Add `ConfigFeatureFlagConfigurationProvider` and extension method

Two new files are added under `src/SchoolCollab.Core/Features`:

- `ConfigFeatureFlagConfigurationProvider.cs` — loads the `FeatureFlags` section from `GET /api/features` once during configuration construction and surfaces each flag as `FeatureFlags:{Key}`.
- `FeatureFlagConfigurationExtensions.cs` — adds the provider to an `IConfigurationBuilder` with a short, retried HTTP call and a graceful fallback.

The provider uses `Microsoft.Extensions.Http.Resilience` for a standard resilience pipeline. If the Config API is unreachable, it silently no-ops so that local `appsettings*.json` or environment variables remain effective. This preserves the ability to run an API directly with `dotnet run` when the Config service is not running.

Usage in each consumer `Program.cs`:

```csharp
builder.Configuration.AddRemoteFeatureFlags("https+http://config");
```

### 3. Make the Config API endpoint anonymous

`GET /api/features` is mapped with `.AllowAnonymous()` because it is consumed during service startup before authentication middleware is configured. The endpoint returns only application-level feature toggles, not tenant data.

### 4. Remove scattered feature-flag configuration

The temporary `FeatureFlags:FEATURE:DisableOIDCAuth` entries are removed from:

- `src/CodedValues/SchoolCollab.CodedValues.Api/appsettings.Development.json`
- `src/Assignments/SchoolCollab.Assignments.Api/appsettings.Development.json`
- `src/Students/SchoolCollab.Students.Api/appsettings.Development.json`
- `src/SchoolCollab.Admin/appsettings.Development.json`

The single source of truth becomes `src/SchoolCollab.Config/appsettings.Development.json`.

### 5. Add centralized feature-flag values to `SchoolCollab.Config`

`src/SchoolCollab.Config/appsettings.Development.json` is created with:

```json
{
  "FeatureFlags": {
    "FEATURE:DisableOIDCAuth": "true"
  }
}
```

### 6. Tests

- `tests/SchoolCollab.Core.Tests.Unit/Features/ConfigFeatureFlagConfigurationProviderTests.cs` — unit tests for the provider using an in-memory `HttpMessageHandler`.
  - Loads flags from the remote endpoint.
  - Falls back to an empty set when the remote endpoint fails.
  - Survives malformed JSON without crashing.

### 7. Documentation updates

- `src/SchoolCollab.Config/README.md` updated to describe the new `ConfigFeatureFlagConfigurationProvider` and centralized model.
- `documents/solution/disable-oidc-auth-admin-implementation.md` updated to remove the scattered-appsettings workaround and reference the centralized configuration.

## Verification

- `dotnet build --configuration Release`: 0 errors, 0 warnings.
- `dotnet test --filter "FullyQualifiedName~Tests.Unit" --ignore-exit-code 8`: 271 passed, 0 failed.

## Result

Feature flags are now centralized in `SchoolCollab.Config`. Consuming services still use `IFeatureFlagService` exactly as before, but the flag values are fetched from the Config API at startup rather than duplicated in each project's local settings.
