using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Data.Outbox;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Integration;

/// <summary>
/// Helpers for the E3 (ar-19) NotificationLog migration + unique-index tests. The
/// in-memory EF provider does not enforce unique indexes, so these discriminating tests
/// MUST run against real Postgres (see <see cref="AssignmentsPostgres"/>). Each test
/// gets its own freshly-created <i>database</i> so the
/// <c>DedupeNotificationLogPublishRows</c> migration can be applied two-step (the dedupe
/// DELETE must run before a duplicate Publish insert can be rejected by the index).
/// </summary>
public static class AssignmentsDbFactory
{
    /// <summary>The <c>AddNotificationLog</c> migration applied <i>before</i> the dedupe
    /// migration, so the dedupe test can seed duplicates in an index-free schema.</summary>
    public const string PreDedupeMigration = "20260917071656_AddNotificationLog";

    /// <summary>The E3 migration under test (dedupe SQL + partial unique index).</summary>
    public const string DedupeMigration = "20260917112110_DedupeNotificationLogPublishRows";

    /// <summary>Creates a brand-new database on the shared container and returns a
    /// connection string to it. The <c>test</c> user is the container superuser, so
    /// <c>CREATE DATABASE</c> is permitted.</summary>
    public static async Task<string> CreateDatabaseAsync(string databaseName)
    {
        await using var admin = new NpgsqlConnection(AssignmentsPostgres.ConnectionString);
        await admin.OpenAsync();
        await using var cmd = admin.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await cmd.ExecuteNonQueryAsync();

        var builder = new NpgsqlConnectionStringBuilder(AssignmentsPostgres.ConnectionString)
        {
            Database = databaseName,
        };
        return builder.ConnectionString;
    }

    /// <summary>Creates a fresh database, applies all EF migrations, and relaxes the
    /// orphaned <c>subject_coded_value_id</c> NOT-NULL constraint. The column is
    /// deliberately retained (unmapped by the model) by migration
    /// <c>20260707091652</c> for the MigrationService backfill — production survives
    /// because that backfill path handles the debt, but the Testcontainers path runs
    /// EF migrations only, and an EF INSERT that omits the column is rejected (23502).
    /// Tests that seed <c>assignments</c> must use this helper, not raw
    /// <c>MigrateAsync</c>.</summary>
    public static async Task<string> CreateMigratedDatabaseAsync(string databaseName)
    {
        var connectionString = await CreateDatabaseAsync(databaseName);
        await using (var ctx = CreateContext(connectionString))
        {
            await ctx.Database.MigrateAsync();
        }

        await using var relax = new NpgsqlConnection(connectionString);
        await relax.OpenAsync();
        await using var cmd = relax.CreateCommand();
        cmd.CommandText =
            "ALTER TABLE assignments ALTER COLUMN subject_coded_value_id DROP NOT NULL";
        await cmd.ExecuteNonQueryAsync();
        return connectionString;
    }

    /// <summary>Builds a tenant-filtered <see cref="AssignmentsDbContext"/> for the given
    /// connection string. Outbox flags + snake-case naming match the runtime/design-time
    /// model so migrations apply cleanly. Pass <paramref name="tenantId"/> when the
    /// context must perform writes: the save-guards (FR-6) reject a strict tenant entity
    /// whose <c>TenantId</c> mismatches the current context, so write contexts are created
    /// with the tenant the rows belong to. Query-only contexts may leave it null.</summary>
    public static AssignmentsDbContext CreateContext(string connectionString, Guid? tenantId = null)
    {
        OutboxMapping.SetFlagsFor<AssignmentsDbContext>(
            OutboxConfigurationFlags.FromConfiguration(b => b
                .UsePartialIndexOnOccurredAt()));

        var tenantProvider = new TenantProvider();
        if (tenantId.HasValue)
        {
            tenantProvider.SetTenant(
                new TenantContext(tenantId.Value, "TestTenant", TenantType.School));
        }

        return new AssignmentsDbContext(
            new DbContextOptionsBuilder<AssignmentsDbContext>()
                .UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention()
                .Options,
            tenantProvider);
    }
}
