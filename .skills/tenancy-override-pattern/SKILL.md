# Tenancy Override Pattern Skill

This skill provides guidance on implementing multi-tenancy for reference data or entities that require a "Global Blueprint $\rightarrow$ Tenant Override" architecture.

## When to use this skill
Use this skill when you need to add tenancy to a module that requires a **Blueprint $\rightarrow$ Override** architecture. This is typically used for **Reference Data** (e.g., Coded Values, Category Lists, System Settings).

### ⚠️ Important: Override vs. Direct Tenancy
Not all entities should follow this pattern. You must distinguish between **Reference Data** and **Operational Data**:

| Data Type | Example | Tenancy Pattern | Logic |
| :--- | :--- | :--- | :--- |
| **Reference Data** | Coded Values, Grade Levels | **Override Pattern** | Global Blueprint $\rightarrow$ Tenant Override $\rightarrow$ Resolved Value |
| **Operational Data** | Students, Assignments, Grades | **Direct Tenancy** | Entity belongs to one `TenantId`. No global version exists. |

**Rule of Thumb:**
- If the data is a "system-wide option" that tenants can customize $\rightarrow$ **Use this Override Skill**.
- If the data is "created by the tenant" (e.g., a student record) $\rightarrow$ **Use Direct Tenancy** (Inherit from `BaseTenantEntity` and filter by `TenantId` in all queries).

**Permission-Based Overrides**:
For operational data (Students/Assignments), do not use the blueprint pattern. Any specific overrides or specialized access must be managed via a dedicated **Permissions/ACL system**, not via the reference data override mechanism.

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
