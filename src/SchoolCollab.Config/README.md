# SchoolCollab.Config

This project serves as the single source of truth for feature flags and application-wide configurations across the SchoolCollab ecosystem. It provides a centralized API to query feature flags, ensuring consistency between the Admin UI (Blazor) and all backend API services.

## Purpose

The `SchoolCollab.Config` project eliminates circular dependencies and configuration drift by:
1. Providing a dedicated service (`IFeatureFlagService`) for checking flags.
2. Exposing a unified endpoint (`GET /api/features`) so the frontend can react to flags without hardcoding logic.
3. Centralizing the definition of critical flags, such as authentication bypasses for development.

## Feature Flag Implementation

### 1. Adding a New Flag
To add a new feature flag, add it to the `appsettings.json` of the `SchoolCollab.Config` project (or the central configuration provider) under the `FeatureFlags` section:

```json
{
  "FeatureFlags": {
    "FEATURE:MyNewFeature": "true"
  }
}
```

### 2. Using the Flag in Backend Services
All backend services should inject the `IFeatureFlagService` and use the `IsEnabled` method:

```csharp
public class MyService(IFeatureFlagService featureFlags) 
{
    public void DoWork() 
    {
        if (featureFlags.IsEnabled("FEATURE:MyNewFeature")) 
        {
            // New logic
        }
    }
}
```

### 3. Conditional Authorization (The API Pattern)
To maintain consistency and allow development bypasses, follow the established pattern in `Program.cs` and endpoint grouping extensions:

```csharp
var featureFlags = app.Services.GetRequiredService<IFeatureFlagService>();
var group = app.MapGroup("/api/my-feature");

if (!featureFlags.IsEnabled("FEATURE:DisableOIDCAuth"))
{
    group.RequireAuthorization();
}
```

## Critical Flags

| Flag | Description | Default | Impact |
| :--- | :--- | :--- | :--- |
| `FEATURE:DisableOIDCAuth` | Bypasses Keycloak OIDC authentication and uses `TestAuthHandler`. | `false` | Disables auth requirements across all APIs and Admin UI. |

## API Reference

### `GET /api/features`
Returns a list of all active feature flags and their current states. Used by the frontend to toggle UI elements.

**Response:**
```json
{
  "FEATURE:DisableOIDCAuth": true,
  "FEATURE:MyNewFeature": false
}
```

## Testing
Ensure any new flags are verified in the corresponding unit tests using `FeatureFlagServiceTests.cs`. When adding integration tests for API endpoints, verify both the "Flag Enabled" and "Flag Disabled" states to ensure authorization is correctly applied or bypassed.
