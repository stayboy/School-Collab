# Settings Context Merge — Implementation Spec

## 1. Goal

Merge the `Config` and `CodedValues` bounded contexts into a single `Settings` bounded context (`SchoolCollab.Settings.{Core,Contracts,Api,Admin}`) under `src/Settings/`, with a single `SettingsDbContext` and a single PostgreSQL database (`settings-db`).

## 2. Decisions (confirmed)

| Topic | Decision |
|-------|----------|
| Context name | `Settings` |
| DbContext | Single `SettingsDbContext` |
| Database | Single `settings-db` (replaces `coded-values-db` and `config-db`) |
| Migrations | Squash into one baseline migration in `Settings.Core/Data/Migrations` |
| API surface | One `Settings.Api` project exposes both legacy endpoint groups (`/api/codedvalues/*` and `/api/features/*`) |
| Aspire resources | Replace `coded-values-api` and `config-api` with `settings-api`; replace `coded-values-db` and `config-db` with `settings-db` |
| Data | Clean reset/re-seed acceptable |
| Admin host | Reference only `SchoolCollab.Settings.Admin`; single `AddSettingsModule()` |

## 3. Project mapping

| Old project | New project |
|-------------|-------------|
| `src/CodedValues/SchoolCollab.CodedValues.Core` | `src/Settings/SchoolCollab.Settings.Core` |
| `src/CodedValues/SchoolCollab.CodedValues.Contracts` | `src/Settings/SchoolCollab.Settings.Contracts` |
| `src/CodedValues/SchoolCollab.CodedValues.Api` | `src/Settings/SchoolCollab.Settings.Api` |
| `src/CodedValues/SchoolCollab.CodedValues.Admin` | `src/Settings/SchoolCollab.Settings.Admin` |
| `src/Config/SchoolCollab.Config.Core` | merged into `src/Settings/SchoolCollab.Settings.Core` |
| `src/Config/SchoolCollab.Config.Contracts` | merged into `src/Settings/SchoolCollab.Settings.Contracts` |
| `src/Config/SchoolCollab.Config.Api` | merged into `src/Settings/SchoolCollab.Settings.Api` |
| `src/Config/SchoolCollab.Config.Admin` | merged into `src/Settings/SchoolCollab.Settings.Admin` |
| `tests/SchoolCollab.CodedValues.Tests.*` | `tests/SchoolCollab.Settings.Tests.*` |
| `tests/SchoolCollab.Config.Tests.*` | merged into `tests/SchoolCollab.Settings.Tests.*` |

## 4. Namespace mapping

- `SchoolCollab.CodedValues.Core.*` → `SchoolCollab.Settings.Core.*`
- `SchoolCollab.CodedValues.Contracts.*` → `SchoolCollab.Settings.Contracts.*`
- `SchoolCollab.CodedValues.Api.*` → `SchoolCollab.Settings.Api.*`
- `SchoolCollab.CodedValues.Admin.*` → `SchoolCollab.Settings.Admin.*`
- `SchoolCollab.Config.Core.*` → `SchoolCollab.Settings.Core.*`
- `SchoolCollab.Config.Contracts.*` → `SchoolCollab.Settings.Contracts.*`
- `SchoolCollab.Config.Api.*` → `SchoolCollab.Settings.Api.*`
- `SchoolCollab.Config.Admin.*` → `SchoolCollab.Settings.Admin.*`

## 5. File collision resolution

Files with identical names in both old contexts:

| File | Resolution |
|------|-----------|
| `Extensions.cs` (Core) | Merge into one `Settings.Core/Extensions.cs` registering both CodedValues and FeatureFlag services, one `AddSettingsCore(...)` method. |
| `Program.cs` (Api) | Merge into one `Settings.Api/Program.cs` calling both endpoint mappers. |
| `ModuleServices.cs` (Admin) | Merge into one `Settings.Admin/ModuleServices.cs` with `AddSettingsModule()`. |
| `SettingsDbContext.cs` | New file combining `DbSet`s from both old contexts and applying both sets of configurations. |
| `DesignTimeSettingsDbContextFactory.cs` | New file targeting `settings-db`. |
| Project `.csproj` files | Create new ones from scratch rather than rename old ones. |
| `CQRS/ICommand.cs` etc. | Use single copies in `Settings.Core/CQRS/`. |
| `Domain/IDomainEvent.cs`, `Domain/Exceptions/DomainException.cs` | Use single copies. |
| `Domain/Events/*.cs` | Keep both sets. |

## 6. `SettingsDbContext` design

```csharp
public sealed class SettingsDbContext(DbContextOptions<SettingsDbContext> options, ITenantProvider tenantProvider)
	: ModuleDbContext(options, tenantProvider)
{
	// CodedValues sets
	public DbSet<CodedValue> CodedValues => Set<CodedValue>();
	public DbSet<TenantCodedValueOverride> TenantCodedValueOverrides => Set<TenantCodedValueOverride>();
	public DbSet<TenantCodedValueAttributeOverride> TenantCodedValueAttributeOverrides => Set<TenantCodedValueAttributeOverride>();
	public DbSet<User> Users => Set<User>();

	// Config sets
	public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
	public DbSet<TenantFeatureFlagOverride> TenantFlagOverrides => Set<TenantFeatureFlagOverride>();
	public DbSet<FlagAuditEntry> FlagAuditEntries => Set<FlagAuditEntry>();

	public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);

		// CodedValues configurations
		modelBuilder.ApplyConfiguration(new CodedValueConfiguration());
		modelBuilder.ApplyConfiguration(new TenantCodedValueOverrideConfiguration());
		modelBuilder.ApplyConfiguration(new TenantCodedValueAttributeOverrideConfiguration(() => CurrentTenantId));

		// Config configurations
		modelBuilder.ApplyConfiguration(new FeatureFlagConfiguration());
		modelBuilder.ApplyConfiguration(new TenantFeatureFlagOverrideConfiguration(() => CurrentTenantId));
		modelBuilder.ApplyConfiguration(new FlagAuditEntryConfiguration());

		modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration(OutboxMapping.FlagsFor<SettingsDbContext>()));
	}
}
```

## 7. Migration strategy

1. Delete all old migrations under `src/Settings/SchoolCollab.Settings.Core/Data/Migrations` after copying.
2. Delete the old `Migrations/` folders in the original `CodedValues.Core` and `Config.Core`.
3. Add a new baseline migration:
   ```bash
   dotnet ef migrations add InitialCreate --project src/Settings/SchoolCollab.Settings.Core --startup-project src/Settings/SchoolCollab.Settings.Api
   ```
4. Seed via the existing `CodedValueSeeder` adapted to `SettingsDbContext` and a new `FeatureFlagSeeder` for `FEATURE:EnableCodedValuesAiChat`.

## 8. API endpoint registration

In `Settings.Api/Program.cs`:

```csharp
var featureFlags = app.Services.GetRequiredService<IFeatureFlagService>();
app.MapCodedValueEndpoints(featureFlags);
app.MapConfigEndpoints(featureFlags);
```

Both legacy endpoint groups live in the same host. Namespace for endpoint classes:
- `SchoolCollab.Settings.Api.Endpoints.CodedValue*` (from CodedValues.Api)
- `SchoolCollab.Settings.Api.Endpoints.Config*` (from Config.Api)

## 9. Admin host updates

`src/SchoolCollab.Admin/SchoolCollab.Admin.csproj`:
- Remove references to `SchoolCollab.CodedValues.Admin` and `SchoolCollab.Config.Admin`.
- Remove references to `SchoolCollab.CodedValues.Core` and `SchoolCollab.Config.Core` if present.
- Add reference to `SchoolCollab.Settings.Admin`.

`src/SchoolCollab.Admin/Program.cs`:
- Replace `AddCodedValuesModule()` and `AddConfigModule()` with `AddSettingsModule()`.
- Update `AddAdditionalAssemblies` to `typeof(SchoolCollab.Settings.Admin.Components._Imports).Assembly`.

## 10. AppHost updates

`src/AppHost/SchoolCollab.AppHost/Program.cs`:
- Replace `coded-values-db` and `config-db` with a single `settings-db`.
- Replace `coded-values-api` and `config-api` with a single `settings-api` project reference.
- Remove `configOutboxExchange` and `codedValuesOutboxExchange`; add `settingsOutboxExchange`.
- Update `admin` project `.WithReference` calls.
- Update `migrator` `.WithReference` calls.

## 11. MigrationService updates

`src/SchoolCollab.MigrationService/SchoolCollab.MigrationService.csproj`:
- Replace references to `CodedValues.Core` and `Config.Core` with `Settings.Core`.

`src/SchoolCollab.MigrationService/Program.cs`:
- Replace `CodedValuesDbContext` and `ConfigDbContext` with `SettingsDbContext`.
- Single connection string `settings-db`.
- Single `OutboxMapping.SetFlagsFor<SettingsDbContext>()`.
- Move/adapt `CodedValueSeeder` into `SchoolCollab.MigrationService.Seeding`.

## 12. Consumers

Update references in:
- `src/SchoolCollab.Admin.Shared/SchoolCollab.Admin.Shared.csproj`
- `src/SchoolCollab.AI/SchoolCollab.AI.csproj`
- `src/SchoolCollab.Admin/SchoolCollab.Admin.csproj`
- `tests/SchoolCollab.ArchitectureTests.Unit/SchoolCollab.ArchitectureTests.Unit.csproj`
- Any other project referencing `CodedValues.*` or `Config.*`.

## 13. Solution file

Add new Settings projects and remove old CodedValues/Config projects from `SchoolCollab.sln`.

## 14. Tests

- Merge unit tests into `tests/SchoolCollab.Settings.Tests.Unit`.
- Merge integration tests into `tests/SchoolCollab.Settings.Tests.Integration`.
- Merge Playwright tests into `tests/SchoolCollab.Settings.Tests.Playwright`.
- Update namespaces and `DbContext` usage to `SettingsDbContext`.
- Update WebApplicationFactory target to `SchoolCollab.Settings.Api`.

## 15. Documentation

- Update `documents/configuration.md` topology and resource tables.
- Update `documents/solution/central-config-service-plan.md` if referenced.
- Add implementation notes to `documents/solution/settings-context-merge.md` after execution.

## 16. Validation

1. `dotnet build SchoolCollab.sln` — zero errors.
2. `dotnet ef migrations add InitialCreate ...` succeeds.
3. `dotnet test` — zero failures.
4. Aspire AppHost launches and `migrator` seeds `settings-db` successfully.

## 17. Execution phases

1. Create new Settings project files and folder structure.
2. Move non-colliding CodedValues source into Settings and update namespaces.
3. Move non-colliding Config source into Settings and update namespaces.
4. Merge colliding files (`Extensions.cs`, `Program.cs`, `ModuleServices.cs`, `SettingsDbContext.cs`, `DesignTimeSettingsDbContextFactory.cs`).
5. Delete old migrations and add squashed baseline.
6. Update all cross-project references (Admin host, AppHost, MigrationService, AI, Admin.Shared, ArchitectureTests).
7. Update solution file.
8. Move and merge tests.
9. Update documentation.
10. Build, add migration, test.
