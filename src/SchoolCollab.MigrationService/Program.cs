using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.Core.Data.Outbox;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
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

// Register CodedValues DbContext
var codedValuesConnectionString = builder.Configuration.GetConnectionString("coded-values-db")
    ?? throw new InvalidOperationException("Connection string 'coded-values-db' is not configured.");

builder.Services.AddDbContext<CodedValuesDbContext>(opts =>
    opts.UseNpgsql(codedValuesConnectionString).UseSnakeCaseNamingConvention());

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
// Keep these three SetFlagsFor calls in lock-step with:
//   * src/<Domain>/SchoolCollab.<Domain>.Core/Extensions.cs (runtime)
//   * src/<Domain>/SchoolCollab.<Domain>.Core/Data/DesignTime*DbContextFactory.cs (design-time)
OutboxMapping.SetFlagsFor<CodedValuesDbContext>(OutboxConfigurationFlags.FromConfiguration(b => b
    .SetTypeMaxLength(500)
    .UseJsonbPayload()
    .UseAttemptsDefaultZero()
    .UsePartialIndexOnOccurredAt()));
OutboxMapping.SetFlagsFor<AssignmentsDbContext>(OutboxConfigurationFlags.FromConfiguration(b => b
    .UsePartialIndexOnOccurredAt()));
// Students uses OutboxConfigurationFlags.Default — no SetFlagsFor call needed.

// Register CodedValueSeeder
builder.Services.AddScoped<CodedValueSeeder>();

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

var exitCode = 0;

try
{
    logger.LogInformation("Unified migration service starting");

    using (var scope = host.Services.CreateScope())
    {
        // ── CodedValues migrations + seeding ──
        try
        {
            var codedValuesDb = scope.ServiceProvider.GetRequiredService<CodedValuesDbContext>();

            logger.LogInformation("Applying EF Core migrations for CodedValues");
            await codedValuesDb.Database.MigrateAsync();
            logger.LogInformation("CodedValues EF Core migrations applied successfully");

            var seeder = scope.ServiceProvider.GetRequiredService<CodedValueSeeder>();
            await seeder.SeedAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CodedValues migration or seeding failed");
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