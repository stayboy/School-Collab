# Feature Flag Workflow

This document outlines the strategy for adopting feature flags across the School Collab solution to decouple deployment from release and allow for rapid environment-specific configuration.

## Overview
Feature flags are managed by the `IFeatureFlagService` in `SchoolCollab.Core`. They allow the system to toggle functionality without requiring code changes or redeployments.

## Implementation Details

### 1. Configuration Management
Feature flags are managed by a standalone service: **`SchoolCollab.Config`**. 

- **The Config Project**: Acts as the "Source of Truth" for flags.
- **Feature API**: Exposes `GET /api/features`. All frontend and backend services can query this endpoint or use the shared `IFeatureFlagService` library if they share the same configuration source.

### 2. Backend Consumption
Backend services (e.g., `CodedValues.Api`, `Students.Api`) consume flags via `IConfiguration` or by injecting `IFeatureFlagService`.

**Example: Conditional Authorization**
```csharp
var group = app.MapGroup("/endpoint");
if (!config["FeatureFlags:FEATURE:DisableOIDCAuth"] == "true") 
{
    group.RequireAuthorization();
}
```

### 3. Frontend Consumption
The Blazor UI queries the `SchoolCollab.Config` API to determine which UI elements to render or whether to bypass authentication redirections.

### 4. Environment Overrides
Overrides can be performed via environment variables:
- `FeatureFlags__FEATURE:DisableOIDCAuth=true`

## Common Flags
| Flag | Description | Default |
| :--- | :--- | :--- |
| `FEATURE:DisableOIDCAuth` | Bypasses OIDC/Keycloak and uses `TestAuthHandler`. | `false` |
