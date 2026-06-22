# SchoolCollab.Config

This project serves as the single source of truth for feature flags and application-wide configurations across the SchoolCollab ecosystem. It provides a centralized API to query feature flags, ensuring consistency between the Admin UI (Blazor) and all backend API services.

## Purpose

The `SchoolCollab.Config` project eliminates circular dependencies and configuration drift by:
1. Providing a dedicated service (`IFeatureFlagService`) for checking flags.
2. Exposing a unified endpoint (`GET /api/features`) so the frontend can react to flags without hardcoding logic.
3. Centralizing the definition of critical flags, such as authentication bypasses for development.

## Feature Flags

Feature flags are managed centrally by `SchoolCollab.Config` and consumed via `IFeatureFlagService`. They allow functionality to be toggled without redeploying the application.

### Configuring flags

Flags are read from the `FeatureFlags` configuration section. Set them in `appsettings.json`:

```json
{
  "FeatureFlags": {
    "FEATURE:DisableOIDCAuth": "true"
  }
}
```

Or via environment variable:

```bash
FeatureFlags__FEATURE:DisableOIDCAuth=true
```

### Introduced flags

| Flag | Purpose | Default | Consumers |
| :--- | :--- | :--- | :--- |
| `FEATURE:DisableOIDCAuth` | Disables Keycloak OIDC authentication and switches to `TestAuthHandler`, allowing local development and testing without a live identity provider. | `false` | `SchoolCollab.Admin`, `SchoolCollab.Assignments.Api`, `SchoolCollab.CodedValues.Api`, `SchoolCollab.Students.Api` |

#### `FEATURE:DisableOIDCAuth`

When enabled:
- `AddAuthAndTenancy` registers the `TestAuth` scheme instead of cookies + OpenID Connect.
- API endpoint groups do not call `RequireAuthorization()`.
- The Admin Blazor app skips `UseAuthentication()` / `UseAuthorization()` and does not require authorization on Razor components.

When disabled (default):
- Standard Keycloak OIDC authentication is used.
- All API endpoints and the Admin UI require authentication.

### Using a flag in code

Inject `IFeatureFlagService` and call `IsEnabled`:

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

### Conditional authorization pattern

Follow this pattern when guarding endpoint groups or middleware:

```csharp
var featureFlags = app.Services.GetRequiredService<IFeatureFlagService>();
var group = app.MapGroup("/api/my-feature");

if (!featureFlags.IsEnabled("FEATURE:DisableOIDCAuth"))
{
    group.RequireAuthorization();
}
```

## API Reference

### `GET /api/features`
Returns a list of all active feature flags and their current states. Used by the frontend to toggle UI elements.

**Response:**
```json
{
  "FEATURE:DisableOIDCAuth": false
}
```

## Testing
Ensure any new flags are verified in the corresponding unit tests using `FeatureFlagServiceTests.cs`. When adding integration tests for API endpoints, verify both the "Flag Enabled" and "Flag Disabled" states to ensure authorization is correctly applied or bypassed.
