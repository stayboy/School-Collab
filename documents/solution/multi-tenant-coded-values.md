# Multi-tenant Coded Values

## Overview
To support tenant-specific overrides (disabling values or changing names/codes), a Resolution-based API is used.

## Data Architecture: The Override Pattern
1. **GlobalCodedValues (Blueprint)**:
   - `ValueId`, `Category`, `DefaultCode`, `DefaultDisplayName`, `IsActive`.
2. **TenantCodedValueOverrides (Attribute Layer)**:
   - `OverrideId`, `TenantId`, `ValueId`, `CustomCode`, `CustomDisplayName`, `IsEnabled`.

## Resolution Logic
The Resolution Engine performs the following for a given tenant:
1. Identify `TenantId` from context.
2. Join `GlobalCodedValues` with `TenantCodedValueOverrides`.
3. Resolve: `CustomValue ?? DefaultValue`.
4. Filter: Discard if `Override.IsEnabled == false`.

## Performance Strategy
- **Tenanted Caching**: Use cache keys like `tenant:{tenantId}:{category}`.
- **Invalidation**: Purge specific tenant cache on update; others remain unaffected.
