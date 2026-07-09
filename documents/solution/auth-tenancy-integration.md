# Authentication & Tenancy Integration

## 1. Findings (Research)

### Authentication Strategy
To support MFA, SSO, and enterprise-grade security without building a custom identity system, we adopt **OpenID Connect (OIDC)** using **Keycloak** as the Identity Provider (IdP).

- **Keycloak's Role**: Handles user credentials, MFA challenges, and session management.
- **Application's Role**: Validates the JWT, extracts tenant claims, and manages the `ITenantProvider` context.

### Tenancy Mapping
Tenancy is handled via **Custom Claims**. 
- The IdP includes a `tenant_id` and `tenant_name` in the ID token.
- The application maps these claims to the `ITenantProvider` via an `IClaimsTransformation` service, ensuring all subsequent requests in the pipeline are scoped to the correct tenant.

### Super Admin & Seeding Strategy
To prevent system lock-out, the system requires a "Bootstrap" process:
- **System Tenant**: A default 'System' tenant is created to host administrative functions.
- **Super Admin**: A hardcoded super-admin profile is seeded during startup if not present, ensuring an entry point for initial configuration.
- **Extensibility**: The seeding logic uses a pattern that can be extended to students, teachers, and teams by adding new seed profiles to the `DbInitializer`.

---

## 2. Implementation Steps

### Step 1: OIDC Configuration ✅
- Add `Microsoft.AspNetCore.Authentication.OpenIdConnect` and `Microsoft.AspNetCore.Authentication.Cookies` (done via `Directory.Packages.props`).
- Implement a shared extension in `SchoolCollab.Core.Auth.AuthTenancyExtensions` that calls `AddAuthentication` / `AddCookie` / `AddOpenIdConnect` and is reused by all web entrypoints (CodedValues.Api, Assignments.Api, Students.Api, Admin).
- Configure `Authority`, `ClientId`, and `ClientSecret` in configuration under `Auth:Keycloak:*`.
- **In the `"Testing"` environment**, `AddAuthAndTenancy` automatically registers `TestAuthHandler` instead of OIDC, so integration tests authenticate without Keycloak.

### Step 2: Tenancy Bridge ✅
- Implement `TenantClaimsTransformation : IClaimsTransformation` in `SchoolCollab.Core.Auth`.
- Extract `tenant_id`, `tenant_name`, and optional `tenant_type` from the `ClaimsPrincipal` and inject them into `ITenantProvider` via `TenantProvider.SetTenant`.
- Register `TenantProvider` as a singleton and `IClaimsTransformation` as scoped in `AddAuthAndTenancy` so all APIs get consistent behavior.

### Step 3: ITenantProvider Enhancement ✅
- Update `ITenantProvider` to expose a `TenantContext` (Id, Name, Type) instead of just a `Guid` (implemented in `SchoolCollab.Core.Tenancy`).
- Use `AsyncLocal<TenantContext>` in `TenantProvider` so tenant context flows with the current async request.

### Step 4: Entity Tenant Integration ✅
- Define `ITenantEntity` interface with `Guid TenantId { get; set; }` in `SchoolCollab.Core.Tenancy`.
- Provide `BaseTenantEntity` and `BaseTenantEntityWithAudit` abstract classes for entities that want the full base.
- Make `Assignment` and `Student` implement `ITenantEntity` with explicit interface mapping:
  ```csharp
  Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
  public Guid TenantId { get; private set; }
  ```
- Create `TenantEntityExtensions` in `SchoolCollab.Core.Tenancy` with:
  - `.WithTenant(ITenantProvider)` — stamps TenantId on any `ITenantEntity` via reflection on the interface property.
  - `.WithTenant(Guid tenantId)` — stamps an explicit tenant ID.
  - `.EnsureTenantAccess(ITenantProvider)` — throws `TenantAccessException` if entity belongs to a different tenant.
- EF migrations added: `AddTenantToAssignments`, `AddTenantToStudents` (uuid column, default `Guid.Empty`).

### Step 5: Endpoint Protection ✅
- For each API, require authentication on business endpoints:
  - CodedValues: group all `/coded-values` routes under `app.MapGroup("/coded-values").RequireAuthorization()`.
  - Students: group all student/grade/subject/period/enrollment routes under `app.MapGroup("/students").RequireAuthorization()`.
  - Assignments: wire auth middleware and `.RequireAuthorization()` on the route group.
- The Admin Blazor host calls `AddAuthAndTenancy` and marks the root `App` component as `.RequireAuthorization()`, ensuring only authenticated users can access the unified UI.

### Step 6: Tenant Isolation in Repositories & Handlers ✅
- **Repository `GetAsync(id)`** methods filter by `TenantId` to prevent cross-tenant access via direct ID.
- **Repository `ListAsync`** methods filter by `TenantId` to scope listings to the current tenant.
- **Command handlers** stamp `TenantId` via `.WithTenant(tenantProvider)` on newly created entities.
- **Query handlers** use tenant-aware cache keys: `assignment:{id}:{tenantId}`, `students:list:{tenantId}`, etc.
- **Delete handlers** use the repository (tenant-scoped `GetAsync`) instead of raw `DbContext.FindAsync`.

### Step 7: Seeding System
- Centralise seeding in `SchoolCollab.MigrationService` instead of per-API `DbInitializer`.
- Seed a `System` tenant (well-known GUID) and a `SuperAdmin` user in the CodedValues database (and later, a dedicated identity/tenants store) from the migration service.
- Ensure seeding is idempotent (checks for existence before inserting).
- `DbInitializer.Initialize()` in CodedValues.Api has been made a no-op; seeding will move to MigrationService.

---

## 3. Key Files

| Component | Location |
|-----------|----------|
| `AddAuthAndTenancy` extension | `src/SchoolCollab.Core/Auth/AuthTenancyExtensions.cs` |
| `TenantClaimsTransformation` | `src/SchoolCollab.Core/Auth/TenantClaimsTransformation.cs` |
| `TestAuthHandler` (testing) | `src/SchoolCollab.Core/Auth/TestAuthHandler.cs` |
| `ITenantProvider` / `TenantProvider` | `src/SchoolCollab.Core/Tenancy/` |
| `ITenantEntity` / `BaseTenantEntity` | `src/SchoolCollab.Core/Tenancy/BaseTenantEntity.cs` |
| `TenantEntityExtensions` | `src/SchoolCollab.Core/Tenancy/TenantEntityExtensions.cs` |
| `TenantAccessException` | `src/SchoolCollab.Core/Tenancy/TenantAccessException.cs` |
| `TenantContext` | `src/SchoolCollab.Core/Tenancy/TenantContext.cs` |

---

## 4. Future Work

- Configure Keycloak claim mappings (`options.ClaimActions`) in `AddOpenIdConnect` once Keycloak setup is finalized.
- Add integration tests: anonymous → 401; different tenant_ids → isolated data.
- ~~Extend tenant scoping to remaining Students entities (GradeLevel, Subject, Period, etc.).~~ **Delivered** by `documents/specs/global-tenant-filter.md` (Step 3): `GradeLevel`, `Subject`, `Period`, `StudentEnrollment`, `GradeSubjectAssignment`, `StudentSubjectAssignment`, `SubjectStrand`, `SubjectLesson` are now strict-tenant entities; `CodedValue` is the hybrid reference; the per-tenant "at most one current period" invariant and `(tenant_id, coded_value_id)` / `(tenant_id, student_number)` composite indexes are in place.
- Remove legacy `DbInitializer` marker class from CodedValues.Api entirely.
- Implement seeding in MigrationService (System tenant, SuperAdmin user).
- ~~Consider EF Core global query filters for automatic tenant scoping.~~ **Delivered** by `documents/specs/global-tenant-filter.md` (Steps 1–5): named `"Tenant"` filter on every `ITenantEntity` / `IHybridTenantEntity`; `ModuleDbContext` save-guard refuses empty/mismatched `TenantId`; `ITenantContextAccessor` is the only sanctioned bypass; `OnModelCreating` audit throws `TenantFilterMissingException` for any non-allow-listed entity without the filter.
