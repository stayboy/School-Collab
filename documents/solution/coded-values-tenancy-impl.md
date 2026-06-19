# Tenancy Implementation: Coded Values Module

## 1. Architectural Goal
Enable multi-tenant support for reference data (Coded Values) where each tenant can:
- Disable specific global coded values.
- Override the `Code` and `Name` of a coded value.
- Override specific `Attributes` on child coded values.

## 2. Implementation Detail

### Base Infrastructure
- **`BaseTenantEntity`**: Introduced in `SchoolCollab.Core`. Provides a `TenantId` to all tenanted entities.
- **`ITenantProvider`**: Service used to resolve the current `TenantId` from the request context (JWT/Session).

### Data Model
We adopted the **Override Pattern** to maintain a single global blueprint while allowing tenant-specific deviations.

- **`CodedValue` (Global)**: The source of truth. Contains default `Code`, `Name`, and `IsDisabled` status.
- **`TenantCodedValueOverride`**: Stores overrides for a specific `TenantId` and `CodedValueId`. Fields mirror the base table (`Code`, `Name`, `IsDisabled`).
- **`TenantCodedValueAttributeOverride`**: Stores tenant-specific values for child attributes, mapped by `(TenantId, CodedValueId, AttributeKey)`.

### Resolution Logic (`CodedValueResolver`)
The system uses a "Resolution Engine" to merge data at runtime:
1. **Basic Properties**: `ResolvedValue = TenantOverride ?? GlobalDefault`.
2. **Attributes**: For each attribute in the global `CodedValue`, the resolver checks for a `TenantCodedValueAttributeOverride`.
3. **Metadata**: Attribute definitions (metadata on parents) remain global.

### Performance & Caching
To avoid expensive joins and repeated database hits:
- **Tenanted Cache Keys**: Cache keys are now prefixed with the tenant ID: `tenant:{tenantId}:coded-value:...`.
- **Isolation**: This ensures that Tenant A's overrides are cached separately from Tenant B's, preventing data leakage and ensuring high performance.

## 3. Verification
- **Build**: Verified via `dotnet build` (0 errors).
- **Tests**: Validated via `CodedValueResolverTests` covering global-only, full-override, and partial-override scenarios.
