using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Students.Core.Data;

namespace SchoolCollab.MigrationService.Seeding;

/// <summary>
/// Dev-only identity seed (ar-20): guarantees a fixed well-known "Dev School" tenant and
/// "Dev Teacher" row that back the committed Keycloak realm file's <c>tenant_id</c> /
/// <c>teacher_id</c> protocol-mapper values. A real Keycloak dev login therefore resolves
/// to a real tenant + teacher id on first boot (no manual row creation).
/// </summary>
/// <remarks>
/// <para>Fixed well-known Guids follow the <see cref="TenantSeeder.SystemTenantId"/> (…0001)
/// precedent: …0002 = "Dev School" tenant, …0003 = "Dev Teacher". The realm file's protocol
/// mappers emit EXACTLY these two values, so the mapper value always matches a seeded row.
/// Random-guid sample tenants ('Hydeson School' etc.) are untouched.</para>
/// <para>Both rows are inserted idempotently via raw SQL <c>INSERT … ON CONFLICT DO NOTHING</c>
/// (the tenant keyed on its natural key <c>name</c>, the teacher on <c>id</c>) — the same
/// fixed-guid mechanism the <c>AddSystemTenant</c> Settings migration uses
/// (<c>migrationBuilder.Sql</c>). This sidesteps the domain factories' <c>Guid.NewGuid()</c>
/// identity (both <see cref="Tenant.Create"/> and <see cref="Teacher.Create"/> assign a fresh id
/// with a private setter) so a known id can be forced without a migration. The xmin row-version
/// column is auto-valued by PostgreSQL, so it is absent from the insert column list.</para>
/// <para><b>Dev-only gate:</b> the "Dev Teacher" password lives in the committed realm file and is
/// labelled dev-only there; no production path authenticates against these fixed ids. This seeder
/// is <b>gated on <see cref="IHostEnvironment.IsDevelopment"/></b> — the rows are inserted only in
/// a Development host environment, never in Production. This prevents dev rows from leaking into a
/// production MigrationService run.</para>
/// </remarks>
public sealed class DevIdentitySeeder(
    SettingsDbContext settingsDb,
    StudentsDbContext studentsDb,
    IHostEnvironment environment,
    ILogger<DevIdentitySeeder> logger)
{
    /// <summary>The fixed "Dev School" tenant id (…0002, after <see cref="TenantSeeder.SystemTenantId"/> …0001).</summary>
    public static readonly Guid DevSchoolTenantId =
        Guid.Parse("00000000-0000-0000-0000-000000000002");

    /// <summary>The fixed "Dev Teacher" row id (…0003) inside <see cref="DevSchoolTenantId"/>.</summary>
    public static readonly Guid DevTeacherId =
        Guid.Parse("00000000-0000-0000-0000-000000000003");

    /// <summary>
    /// Seeds the dev tenant + dev teacher idempotently. No-op when either already exists, and a
    /// no-op outside a Development host environment (<see cref="IHostEnvironment.IsDevelopment"/> —
    /// the dev-only gate).
    /// </summary>
    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!environment.IsDevelopment())
        {
            logger.LogInformation(
                "Dev identity seed skipped: host environment '{EnvironmentName}' is not Development",
                environment.EnvironmentName);
            return;
        }

        // P1: parameters are passed explicitly as an object[] so EF treats them as SQL parameters
        // and passes ct as the CancellationToken (the bare `DevSchoolTenantId, ct` form would bind
        // both into params object[], sending the token as a parameter value — a conversion failure
        // at migrator startup).
        // P2-2: `ON CONFLICT (name)` (not id) — tenants has a unique index on name
        // (ix_tenants_name_unique), so a pre-existing "Dev School" row with a different id would
        // otherwise raise a unique violation. The fixed id and the fixed name travel together, so
        // the id PK conflict is covered by the same natural-key conflict target.
        var insertedTenant = await settingsDb.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO tenants (id, name, type, created_at, updated_at)
            VALUES ({0}, 'Dev School', 'School', now(), now())
            ON CONFLICT (name) DO NOTHING
            """,
            new object[] { DevSchoolTenantId }, ct);
        if (insertedTenant > 0)
            logger.LogInformation("Seeded dev tenant 'Dev School' ({Id})", DevSchoolTenantId);
        else
            logger.LogDebug("Dev tenant 'Dev School' already exists; skipping");

        var insertedTeacher = await studentsDb.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO teachers
                (id, title_coded_value_id, first_name, last_name, date_of_birth,
                 gender_coded_value_id, display_name, staff_user_id, staff_number,
                 level_of_education_coded_value_id, tenant_id, is_deleted, deleted_at,
                 created_at, updated_at)
            VALUES
                ({0}, NULL, 'Dev', 'Teacher', NULL, NULL, 'Dev Teacher', NULL, 'DEV-0001', NULL,
                 {1}, false, NULL, now(), now())
            ON CONFLICT (id) DO NOTHING
            """,
            new object[] { DevTeacherId, DevSchoolTenantId }, ct);
        if (insertedTeacher > 0)
            logger.LogInformation("Seeded dev teacher 'Dev Teacher' ({Id})", DevTeacherId);
        else
            logger.LogDebug("Dev teacher 'Dev Teacher' already exists; skipping");
    }
}
