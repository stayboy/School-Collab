using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Core.Data.Outbox;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Core.Features;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.MigrationService.Seeding;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Must be first — initialises Serilog and OTLP telemetry pipeline
builder.AddServiceDefaults();

// Core tenancy provider is required by all module DbContexts. Register it before
// any DbContext so the contexts can resolve ITenantProvider even though this
// worker does not use authentication/claims transformation.
builder.Services.AddTenancy();

// Register Settings DbContext (unified replacement for CodedValuesDbContext and
// ConfigDbContext — see documents/solution/settings-context-merge-spec.md §6).
var settingsConnectionString = builder.Configuration.GetConnectionString("settings-db")
    ?? throw new InvalidOperationException("Connection string 'settings-db' is not configured.");

builder.Services.AddDbContext<SettingsDbContext>(opts =>
    opts.UseNpgsql(settingsConnectionString).UseSnakeCaseNamingConvention());

// Register Assignments DbContext
var assignmentsConnectionString = builder.Configuration.GetConnectionString("assignments-db")
    ?? throw new InvalidOperationException("Connection string 'assignments-db' is not configured.");

builder.Services.AddDbContext<AssignmentsDbContext>(opts =>
    opts.UseNpgsql(assignmentsConnectionString).UseSnakeCaseNamingConvention());

// Register Students DbContext
var studentsConnectionString = builder.Configuration.GetConnectionString("students-db")
    ?? throw new InvalidOperationException("Connection string 'students-db' is not configured.");

builder.Services.AddDbContext<StudentsDbContext>(opts =>
    opts.UseNpgsql(studentsConnectionString).UseSnakeCaseNamingConvention());

// Seed the per-context outbox flags exactly as the runtime AddOutbox<TContext>
// and design-time factories do. Without this, MigrateAsync fails with
// "The model for context 'X' has pending changes" because the runtime model
// built from AddDbContext reads default flags while the model snapshot was
// generated with the per-module outbox flags applied.
// Keep these SetFlagsFor calls in lock-step with:
//   * src/Settings/SchoolCollab.Settings.Core/Extensions.cs (runtime)
//   * src/Settings/SchoolCollab.Settings.Core/Data/DesignTimeSettingsDbContextFactory.cs (design-time)
OutboxMapping.SetFlagsFor<SettingsDbContext>(OutboxConfigurationFlags.FromConfiguration(b => b
    .SetTypeMaxLength(500)
    .UseJsonbPayload()
    .UseAttemptsDefaultZero()
    .UsePartialIndexOnOccurredAt()));
OutboxMapping.SetFlagsFor<AssignmentsDbContext>(OutboxConfigurationFlags.FromConfiguration(b => b
    .UsePartialIndexOnOccurredAt()));
// Students uses OutboxConfigurationFlags.Default — no SetFlagsFor call needed.

// Register CodedValueSeeder
builder.Services.AddScoped<CodedValueSeeder>();
builder.Services.AddScoped<TenantSeeder>();
builder.Services.AddScoped<AssignmentBackfillService>();

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

var exitCode = 0;

try
{
    logger.LogInformation("Unified migration service starting");

    using (var scope = host.Services.CreateScope())
    {
        // ── Settings migrations + seeding (CodedValues data + FeatureFlag
        //    FEATURE:EnableCodedValuesAiChat) ──
        try
        {
            var settingsDb = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();

            logger.LogInformation("Applying EF Core migrations for Settings");
            await settingsDb.Database.MigrateAsync();
            logger.LogInformation("Settings EF Core migrations applied successfully");

            var seeder = scope.ServiceProvider.GetRequiredService<CodedValueSeeder>();
            await seeder.SeedAsync();
            await SeedEnableCodedValuesAiChatAsync(settingsDb, logger);
            await SeedEnableGradeLevelSetupOnEnrollDialogAsync(settingsDb, logger);

            // Seed the real Tenant registry ('Hydeson School' + 'Little Legends') so
            // tenancy overrides have a target tenant. Idempotent by Name. See
            // documents/specs/grade-level-setup.md §5.5 / PR 1.
            var tenantSeeder = scope.ServiceProvider.GetRequiredService<TenantSeeder>();
            await tenantSeeder.SeedAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Settings migration or seeding failed");
            exitCode = 1;
        }

        // ── Assignments migrations ──
        try
        {
            var assignmentsDb = scope.ServiceProvider.GetRequiredService<AssignmentsDbContext>();

            logger.LogInformation("Applying EF Core migrations for Assignments");
            await assignmentsDb.Database.MigrateAsync();
            logger.LogInformation("Assignments EF Core migrations applied successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Assignments migration failed");
            exitCode = 1;
        }

        // ── Students migrations ──
        try
        {
            var studentsDb = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();

            logger.LogInformation("Applying EF Core migrations for Students");
            await studentsDb.Database.MigrateAsync();
            logger.LogInformation("Students EF Core migrations applied successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Students migration failed");
            exitCode = 1;
        }

        // ── Assignment backfill (PR 4: SubjectId/GradeLevelId from coded-value IDs) ──
        // Must run AFTER Assignments and Students migrations both succeed.
        // See documents/specs/grade-level-setup-progress.md §PR 4.
        if (exitCode == 0)
        {
            try
            {
                var backfillService = scope.ServiceProvider.GetRequiredService<AssignmentBackfillService>();
                await backfillService.BackfillAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Assignment backfill failed");
                exitCode = 1;
            }
        }
    }

    if (exitCode == 0)
        logger.LogInformation("Unified migration service completed successfully");
    else
        logger.LogWarning("Unified migration service completed with errors");
}
catch (Exception ex)
{
    logger.LogError(ex, "Unified migration service failed unexpectedly");
    exitCode = 1;
}
finally
{
    // Ensure all buffered log entries reach the OTLP sink before the process exits
    await Log.CloseAndFlushAsync();
}

return exitCode;

// ── Settings seed ──
// Seeds the first real runtime feature flag — FEATURE:EnableCodedValuesAiChat —
// if it does not already exist. Default true preserves the current always-on AI-chat
// behaviour on the CodedValues landing page. Records an audit row with a system
// actor so the seed is traceable. NOTE: FEATURE:DisableOIDCAuth is intentionally
// NOT seeded here — it is a startup auth-mode switch read from IConfiguration, not a
// runtime feature flag (see documents/solution/central-config-service-plan.md §2).
static async Task SeedEnableCodedValuesAiChatAsync(SettingsDbContext db, Microsoft.Extensions.Logging.ILogger logger)
{
    const string actorId = "system:migrator";
    const string actorName = "Migration Service";

    // FeatureFlag.Create normalizes the key via FeatureFlag.NormalizeKey (upper-cases
    // the area after 'FEATURE:'), so we must compare against the canonical form here.
    // A plain `f.Key == "FEATURE:EnableCodedValuesAiChat"` is a case-sensitive
    // Postgres text comparison and would MISS an existing row stored as
    // "FEATURE:ENABLECODEDVALUESAICHAT", causing SaveChangesAsync to violate the
    // partial unique index ix_feature_flags_key_unique on a re-run.
    var key = FeatureFlag.NormalizeKey(FeatureFlagKeys.EnableCodedValuesAiChat);

    var exists = await db.FeatureFlags.AnyAsync(f => f.Key == key);
    if (exists)
    {
        logger.LogInformation("Seed flag {Key} already present; skipping", key);
        return;
    }

    var flag = FeatureFlag.Create(key, "Enable AI chat on Coded Values landing page", null, isEnabled: true);
    db.FeatureFlags.Add(flag);
    db.FlagAuditEntries.Add(FlagAuditEntry.Create(
        tenantId: null,
        featureFlagId: flag.Id,
        featureFlagKey: flag.Key,
        changeKind: FlagChangeKind.Created,
        previousIsEnabled: null,
        newIsEnabled: flag.IsEnabled,
        reason: "Initial seed by migration service",
        actorId: actorId,
        actorDisplayName: actorName));

    await db.SaveChangesAsync();
    logger.LogInformation("Seeded feature flag {Key} (IsEnabled={IsEnabled})", key, flag.IsEnabled);
}

// ── Settings seed: FEATURE:EnableGradeLevelSetupOnEnrollDialog ──
// Seeds the runtime feature flag that gates the "+" inline-grade-create
// button on the EnrollStudentDialog. Default false (opt-in) so the
// global side-effect (creating a new GRADE coded value + GradeLevel row,
// shared across all tenants) requires an explicit ConfigFlags toggle
// before it becomes available. Idempotent — re-runs no-op on a
// pre-existing row. Records an audit row with a system actor so the
// seed is traceable. The flag is consumed in
// <EnrollStudentDialog> via <FeatureFlagGate Key="FEATURE:EnableGradeLevelSetupOnEnrollDialog">.
// See documents/specs/grade-level-setup.md §3.
static async Task SeedEnableGradeLevelSetupOnEnrollDialogAsync(SettingsDbContext db, Microsoft.Extensions.Logging.ILogger logger)
{
    const string actorId = "system:migrator";
    const string actorName = "Migration Service";

    // Same canonicalization caveat as the AI-chat seed — compare against the
    // normalized form so a re-run does not violate ix_feature_flags_key_unique.
    var key = FeatureFlag.NormalizeKey(FeatureFlagKeys.EnableGradeLevelSetupOnEnrollDialog);

    var exists = await db.FeatureFlags.AnyAsync(f => f.Key == key);
    if (exists)
    {
        logger.LogInformation("Seed flag {Key} already present; skipping", key);
        return;
    }

    var flag = FeatureFlag.Create(
        key,
        "Enable inline grade-level setup (\"+\" button) on the Enroll Student dialog",
        null,
        isEnabled: false);
    db.FeatureFlags.Add(flag);
    db.FlagAuditEntries.Add(FlagAuditEntry.Create(
        tenantId: null,
        featureFlagId: flag.Id,
        featureFlagKey: flag.Key,
        changeKind: FlagChangeKind.Created,
        previousIsEnabled: null,
        newIsEnabled: flag.IsEnabled,
        reason: "Initial seed by migration service",
        actorId: actorId,
        actorDisplayName: actorName));

    await db.SaveChangesAsync();
    logger.LogInformation("Seeded feature flag {Key} (IsEnabled={IsEnabled})", key, flag.IsEnabled);
}
