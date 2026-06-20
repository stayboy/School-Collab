# Auth & Tenancy Pattern for New Modules

This document defines the standard pattern for adding authentication and tenant context
support to any new HTTP-facing module (API, worker with HTTP endpoints, admin shell)
in the SchoolCollab solution.

It builds on the core primitives defined in `SchoolCollab.Core`:

- `SchoolCollab.Core.Auth.AuthTenancyExtensions`
- `SchoolCollab.Core.Auth.TenantClaimsTransformation`
- `SchoolCollab.Core.Tenancy.ITenantProvider` / `TenantProvider` / `TenantContext`
- `SchoolCollab.Core.Tenancy.ITenantEntity` / `BaseTenantEntity` / `BaseTenantEntityWithAudit`
- `SchoolCollab.Core.Tenancy.TenantEntityExtensions` (`.WithTenant()`, `.EnsureTenantAccess()`)

## 1. When to Apply This Pattern

Apply this pattern to **every new module that exposes HTTP endpoints** under `src/`:

- New bounded-context APIs (e.g., `Attendance.Api`, `Timetables.Api`).
- New admin or UI hosts that front-end other services (e.g., a reporting dashboard).
- Any worker that starts exposing HTTP management endpoints.

Do **not** apply it to pure background workers with no external HTTP surface unless
those workers need to act on behalf of a tenant for incoming messages (in which case,
consider defining a message-level tenant contract instead).

## 2. Required Dependencies

In the new project's `.csproj`, reference:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\SchoolCollab.Core\SchoolCollab.Core.csproj" />
  <ProjectReference Include="..\..\ServiceDefaults\SchoolCollab.ServiceDefaults\SchoolCollab.ServiceDefaults.csproj" />
</ItemGroup>
```

Packages for `Microsoft.AspNetCore.Authentication.Cookies` and
`Microsoft.AspNetCore.Authentication.OpenIdConnect` are already version-managed via
`Directory.Packages.props`; you do **not** need to add direct `PackageReference`
entries in new web projects.

## 3. Program.cs Template for New APIs

Every new minimal API should follow this baseline structure:

```csharp
using SchoolCollab.Core.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
// Add other infra here (RabbitMQ, Redis, etc.)

// Module-specific services
builder.Services.Add<YourModule>Core(builder.Configuration);
builder.Services.AddOpenApi();

// Shared auth + tenancy (OIDC via Keycloak; auto-switches to TestAuth in "Testing" env)
builder.Services.AddAuthAndTenancy(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.UseSerilogRequestLogging();

// Group all business endpoints under a base path and require auth
var group = app.MapGroup("/<base-path>").RequireAuthorization();

// Map endpoints within this group
// group.MapGet("/", ...);
// group.MapPost("/", ...);
// etc.

app.Run();

public partial class Program { }
```

**Key rules:**

1. **Always** call `AddAuthAndTenancy` for any HTTP-facing module.
2. **Always** put `UseAuthentication` before `UseAuthorization`.
3. Use a **route group with `RequireAuthorization()`** rather than mapping
   unsecured endpoints and decorating some with `[Authorize]` at random.
4. In the `"Testing"` environment, `AddAuthAndTenancy` automatically registers
   `TestAuthHandler` instead of OIDC — no test-specific auth bypass needed.

## 4. Tenant Context Usage in Core Logic

Within module core projects (e.g., `SchoolCollab.Assignments.Core`,
`SchoolCollab.Students.Core`), any handler that needs tenant isolation should:

### 4.1 Making Entities Tenant-Aware

Domain entities that belong to a tenant must implement `ITenantEntity`:

```csharp
using SchoolCollab.Core.Tenancy;

public sealed class Assignment : ITenantEntity
{
    // Explicit interface mapping so the reflection helper in TenantEntityExtensions
    // can set TenantId, while keeping the domain setter private.
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }

    // ... other properties ...
}
```

Alternatively, entities that share the common audit shape (Id, TenantId, CreatedAt,
UpdatedAt, IsDeleted, DeletedAt) can derive from `BaseTenantEntityWithAudit`:

```csharp
public sealed class TenantCodedValueOverride : BaseTenantEntityWithAudit
{
    // TenantId, CreatedAt, UpdatedAt, IsDeleted, DeletedAt come from the base class
    // ...
}
```

### 4.2 Stamping Tenant on New Entities (Command Handlers)

Use the `WithTenant` extension method from `TenantEntityExtensions`:

```csharp
using SchoolCollab.Core.Tenancy;

public sealed class CreateAssignmentHandler(
    IAssignmentRepository repository,
    ITenantProvider tenantProvider,
    ...) : ICommandHandler<CreateAssignmentCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateAssignmentCommand command, CancellationToken ct)
    {
        var assignment = Assignment.Create(...)
            .WithTenant(tenantProvider);  // Sets TenantId via ITenantEntity

        await repository.AddAsync(assignment, ct);
        return assignment.Id;
    }
}
```

The `.WithTenant(tenantProvider)` extension uses reflection on `ITenantEntity.TenantId`
once, centrally. This is acceptable because the target is an interface property — the
contract is compiler-validated. No per-aggregate `WithTenant` method is needed.

### 4.3 Filtering Queries by Tenant

All read operations must filter by the current tenant:

```csharp
public async Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? status, CancellationToken ct)
{
    var tenantId = _tenantProvider.GetTenantContext().TenantId;
    var query = _db.Assignments.AsNoTracking().Where(a => a.TenantId == tenantId);
    // ...
}
```

Repository `GetAsync(id)` methods must also filter by tenant to prevent cross-tenant
access via direct ID:

```csharp
public async Task<Assignment?> GetAsync(Guid id, CancellationToken ct) =>
    await _db.Assignments.SingleOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId, ct);
```

### 4.4 Tenant-Aware Cache Keys

All cache keys for tenanted data must include the tenant ID:

```csharp
var tenantId = tenantProvider.GetTenantContext().TenantId;
var cacheKey = $"assignment:{id}:{tenantId}";
var cacheKey = $"assignments:list:{tenantId}:{status}";
```

### 4.5 Cross-Tenant Access Guard

For command handlers that load an entity and then mutate it, the repository's
tenant-filtered `GetAsync` already prevents loading another tenant's data. If you
need an explicit check (e.g., loading via a non-repository path), use
`.EnsureTenantAccess()`:

```csharp
var entity = await db.Entities.FindAsync([id], ct) ?? throw new NotFoundException(id);
entity.EnsureTenantAccess(tenantProvider); // throws TenantAccessException on mismatch
```

## 5. Claim Contract with Keycloak

All modules implicitly rely on Keycloak issuing the following claims in the ID token:

- `tenant_id` (string, GUID): unique tenant identifier.
- `tenant_name` (string): human-readable tenant name.
- `tenant_type` (string, optional): one of `School`, `Organization`, or `Team`.

The shared `TenantClaimsTransformation` in `SchoolCollab.Core.Auth`:

- Parses `tenant_id` into a `Guid`.
- Falls back to `TenantType.School` if `tenant_type` is missing or invalid.
- Uses `"Unknown"` as a default `TenantName` if `tenant_name` is not present.
- Leaves the existing `TenantContext` unchanged if `tenant_id` is missing or invalid.

**TODO for future work:** document and configure Keycloak's client/mapper setup so
these claims are always present for authenticated users, and add explicit
`ClaimActions` mappings in `AddOpenIdConnect` once the data shape is final.

## 6. Testing

### 6.1 Integration Tests

`AddAuthAndTenancy` detects the `"Testing"` environment and automatically registers
`TestAuthHandler` instead of OIDC. This handler authenticates every request as a
known test user with a default tenant ID (`00000000-0000-0000-0000-000000000001`).

To configure a custom test tenant:

```csharp
// In your WebApplicationFactory.ConfigureWebHost:
builder.ConfigureServices(services =>
{
    services.Configure<TestAuthHandlerOptions>(options =>
        options.TenantId = Guid.Parse("..."));
});
```

### 6.2 Unit Tests

When testing handlers that depend on `ITenantProvider`, mock it:

```csharp
var tenantProvider = new Mock<ITenantProvider>();
tenantProvider.Setup(x => x.GetTenantContext())
    .Returns(new TenantContext(Guid.Parse("..."), "Test", TenantType.School));
```

## 7. Example: Applying the Pattern to a New Module

When adding a new module, e.g., `Attendance.Api`:

1. Create project under `src/Attendance/SchoolCollab.Attendance.Api`.
2. Reference `SchoolCollab.Core` and `SchoolCollab.ServiceDefaults` in the `.csproj`.
3. In `Program.cs`:
   - Call `builder.AddServiceDefaults()`.
   - Register module core services (e.g., `AddAttendanceCore`).
   - Call `builder.Services.AddAuthAndTenancy(builder.Configuration);`.
   - Use `UseAuthentication` / `UseAuthorization` mid-pipeline.
   - Map endpoints under `app.MapGroup("/attendance").RequireAuthorization()`.
4. In `SchoolCollab.Attendance.Core`:
   - Make domain entities implement `ITenantEntity` (or derive from `BaseTenantEntityWithAudit`).
   - In create handlers, use `.WithTenant(tenantProvider)` to stamp the tenant.
   - In query/mutation handlers, filter by `tenantId` from `ITenantProvider`.
   - Include `tenantId` in all cache keys for tenanted data.
   - Add tenant filtering to repository `GetAsync` / `ListAsync` methods.

Following this pattern ensures that all modules:

- Share a consistent OIDC configuration and Keycloak integration.
- Interpret tenant claims uniformly.
- Enforce authentication on business endpoints by default.
- Isolate tenant data at the repository level (no cross-tenant leaks).
- Are ready for future cross-cutting policies (e.g., per-tenant rate limiting,
  per-tenant logging) built on top of `TenantContext`.
