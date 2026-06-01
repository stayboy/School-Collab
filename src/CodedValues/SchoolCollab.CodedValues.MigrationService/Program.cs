using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.CodedValues.MigrationService.Seeding;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Must be first — initialises Serilog and OTLP telemetry pipeline
builder.AddServiceDefaults();

// Register only the DbContext; full AddCodedValuesCore also wires MassTransit
// and CQRS handlers which are not needed in this short-lived process.
var connectionString = builder.Configuration.GetConnectionString("coded-values-db")
    ?? throw new InvalidOperationException("Connection string 'coded-values-db' is not configured.");

builder.Services.AddDbContext<CodedValuesDbContext>(opts =>
    opts.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

builder.Services.AddScoped<CodedValueSeeder>();

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

try
{
    logger.LogInformation("Migration service starting");

    using (var scope = host.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<CodedValuesDbContext>();

        logger.LogInformation("Applying EF Core migrations");
        await db.Database.MigrateAsync();
        logger.LogInformation("EF Core migrations applied successfully");

        var seeder = scope.ServiceProvider.GetRequiredService<CodedValueSeeder>();
        await seeder.SeedAsync();
    }

    logger.LogInformation("Migration service completed successfully");
    return 0;
}
catch (Exception ex)
{
    logger.LogError(ex, "Migration service failed");
    return 1;
}
finally
{
    // Ensure all buffered log entries reach the OTLP sink before the process exits
    await Log.CloseAndFlushAsync();
}
