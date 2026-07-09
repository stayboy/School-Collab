# Tenancy Override Pattern Skill

This skill provides guidance on implementing multi-tenancy for reference data or entities that require a "Global Blueprint $\rightarrow$ Tenant Override" architecture.

## When to use this skill
Use this skill when you need to add tenancy to a module that requires a **Blueprint $\rightarrow$ Override** architecture. This is typically used for **Reference Data** (e.g., Coded Values, Category Lists, System Settings).

### ⚠️ Important: Override vs. Direct Tenancy
Not all entities should follow this pattern. You must distinguish between **Reference Data** and **Operational Data**:

| Data Type | Example | Tenancy Pattern | Logic |
| :--- | :--- | :--- | :--- |
| **Reference Data** | Coded Values, Grade Levels (codes) | **Hybrid Override Pattern** (see below) | Shared blueprint (NULL tenant) + Tenant-owned rows; per-tenant name overlay via override. |
| **Operational Data** | Students, Assignments, Grades | **Direct Tenancy** | Entity belongs to one `TenantId`. No global version exists. |

**Rule of Thumb:**
- If the data is a "system-wide option" that tenants can customize $\rightarrow$ **Use this Override Skill**.
- If the data is "created by the tenant" (e.g., a student record) $\rightarrow$ **Use Direct Tenancy** (Inherit from `BaseTenantEntity` and filter by `TenantId` in all queries).

**Permission-Based Overrides**:
For operational data (Students/Assignments), do not use the blueprint pattern. Any specific overrides or specialized access must be managed via a dedicated **Permissions/ACL system**, not via the reference data override mechanism.

## Hybrid Override Model (CodedValue / reference data)

`documents/specs/global-tenant-filter.md` §3.2–§3.3 narrows the blueprint/override
model to a **hybrid** tenancy contract for reference entities like `CodedValue`.
Reference rows are either:

- **Shared blueprint** — `tenant_id IS NULL`. CSV-seeded by the migration
  service under `SuppressTenantGuard()`. Visible to all tenants via the
  hybrid filter `TenantId == CurrentTenantId OR TenantId IS NULL`. Tenants
  customise the displayed `Name` (and any overlaid attributes) per-tenant via
  `TenantCodedValueOverride` / `TenantCodedValueAttributeOverride`.
- **Tenant-owned** — `tenant_id = <real>`. Created by a tenant-facing path
  (e.g. the Grade-Level wizard's "create new" under a real tenant) for codes
  the shared blueprint does not provide. Isolated to that tenant; no override
  row targets it — the tenant edits the row's own `Name` / `IsDisabled`.

`Guid.Empty` is **never** a valid `CodedValue.TenantId`. The two
`CreateCodedValueHandler` paths reflect this:

- `CurrentTenantId != Guid.Empty` $\rightarrow$ stamps `TenantId = current`
  (tenant-owned row). The duplicate-code guard (FR-6) rejects creation if a
  shared row with the same `(parent, code)` already exists — the tenant is
  directed to **override the shared row's name** rather than create a
  duplicate.
- `CurrentTenantId == Guid.Empty` (default/dev only; prod guarantees a real
  claim via the API pipeline) $\rightarrow$ writes `TenantId = NULL` under
  `SuppressTenantGuard()` — the dev/admin vocabulary-edit affordance.

The resolver is unchanged: `overrideValue?.OverriddenName ?? cv.Name`
transparently returns the owned row's own name (no override row exists) and
the shared row's overridden name. The override pattern is **retained**, not
deprecated.

## Implementation Workflow

### 1. Entity Definition
- **Global Entity**: The base entity remains as the "blueprint" (e.g., `CodedValue`).
- **Override Entity**: Create a `Tenant[EntityName]Override` entity inheriting from `BaseTenantEntity`.
  - It must contain a reference to the global entity (`GlobalEntityId`).
  - It should only contain the fields that are allowed to be overridden (e.g., `Code`, `Name`, `IsActive`).
  - Fields should be nullable to allow "Partial Overrides" (if null, fall back to global).

### 2. Data Access Layer (Repository)
- Implement `GetOverrideAsync(Guid tenantId, Guid entityId)` to fetch the specific override.
- Implement `UpsertOverrideAsync` to manage the override lifecycle.

### 3. Resolution Logic (The Resolver)
Create a `[EntityName]Resolver` service that implements the following logic:
```csharp
public async Task<<EntityEntityDto> ResolveAsync(GlobalEntity global, Guid tenantId)
{
    var overrideVal = await _repo.GetOverrideAsync(tenantId, global.Id);
    
    return new EntityDto(
        Id: global.Id,
        // Merge logic: Tenant Override ?? Global Default
        Name: overrideVal?.Name ?? global.Name,
        Code: overrideVal?.Code ?? global.Code,
        IsDisabled: overrideVal?.IsDisabled ?? global.IsDisabled
    );
}
```

### 4. Caching Strategy
- **Tenant-Aware Keys**: Always include the `tenantId` in the cache key to prevent data leakage.
  - Pattern: `tenant:{tenantId}:[entity-category]:{id}`
- **Invalidation**: Purge only the specific tenant's cache entry when an override is updated.

### 5. Verification Checklist
- [ ] Does the `TenantOverride` entity inherit from `BaseTenantEntity`?
- [ ] Is the resolver using the `TenantOverride ?? Global` merge pattern?
- [ ] Are cache keys prefixed with `tenant:{id}`?
- [ ] Did you run `dotnet build` and create unit tests for the resolver?
- [ ] If the reference entity is `CodedValue` (or any other `IHybridTenantEntity`): does the `Create` handler stamp `TenantId = current` (real tenant) or `TenantId = null` (default tenant, under `SuppressTenantGuard`)? Does the duplicate-code guard reject creation when a shared row with the same `(parent, code)` already exists, directing the tenant to override the shared row's name?
