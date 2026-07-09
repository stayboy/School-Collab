using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.MigrationService.Seeding;

/// <summary>
/// Seeds the real <see cref="Tenant"/> registry with the sample tenants used to
/// exercise tenancy overrides end-to-end ('Hydeson School' and 'Little Legends').
/// </summary>
/// <remarks>
/// <para><b>Idempotent by natural key</b> (<see cref="Tenant.Name"/>): a re-run finds
/// existing rows by name and skips them, mirroring the <see cref="CodedValueSeeder"/>
/// pattern which is idempotent by <c>Code</c>. The unique index
/// <c>ix_tenants_name_unique</c> backs this invariant at the database level.</para>
/// <para>Tenant ids follow the project convention <c>Id = Guid.NewGuid()</c> via
/// <see cref="Tenant.Create"/>: no hardcoded Guids are ever seeded. The captured ids
/// are returned so later seed passes (sample coded-value overrides, gradelevels) can
/// reference them without a second lookup.</para>
/// <para><see cref="Tenant"/> carries no domain events, so — unlike
/// <see cref="CodedValueSeeder"/> — there is nothing to clear after each insert.</para>
/// </remarks>
public sealed class TenantSeeder(
    SettingsDbContext db,
    ITenantContextAccessor tenantContextAccessor,
    ILogger<TenantSeeder> logger)
{
    /// <summary>
    /// The well-known System tenant id — a backfill sink for unattributable strict
    /// rows (global-tenant-filter.md §9.3 / Q-1). Seeded idempotently by the
    /// <c>AddSystemTenant</c> Settings migration. No end-users authenticate as System.
    /// </summary>
    public static readonly Guid SystemTenantId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");
    /// <summary>
    /// The sample tenants seeded in development/test environments to exercise
    /// tenancy overrides. School type matches the most common tenant shape.
    /// </summary>
    private static readonly (string Name, TenantType Type)[] SampleTenants =
    [
        ("Hydeson School", TenantType.School),
        ("Little Legends", TenantType.School),
    ];

    /// <summary>
    /// Sample grade-name overrides per tenant, keyed by coded-value <c>Code</c>.
    /// Demonstrates the Global-blueprint → Tenant-override → Resolver pattern:
    /// the global GRADE coded value supplies the default name; the tenant override
    /// supplies the tenant-resolved display name. See spec §5.5 / §6.2.
    /// </summary>
    private static readonly (string TenantName, string CodedValueCode, string OverriddenName)[] SampleGradeOverrides =
    [
        // Hydeson School renames "Grade 1/2/3" → "Standard 1/2/3".
        ("Hydeson School", "GRADE_1", "Standard 1"),
        ("Hydeson School", "GRADE_2", "Standard 2"),
        ("Hydeson School", "GRADE_3", "Standard 3"),
        // Little Legends renames "Grade R" → "Reception", "Grade 1/2" → "Year 1/2".
        ("Little Legends", "GRADE_R", "Reception"),
        ("Little Legends", "GRADE_1", "Year 1"),
        ("Little Legends", "GRADE_2", "Year 2"),
    ];

    /// <summary>
    /// Seeds the sample tenants if absent. Returns the full tenant registry
    /// (newly seeded + pre-existing) keyed by name so callers can wire related
    /// seed data in the same pass.
    /// </summary>
    public async Task<Dictionary<string, Guid>> SeedAsync(CancellationToken ct = default)
    {
        // Load existing tenants once to avoid N+1 lookups and to short-circuit inserts.
        var existing = await db.Tenants
            .ToDictionaryAsync(t => t.Name, t => t.Id, ct);

        var inserted = 0;

        foreach (var (name, type) in SampleTenants)
        {
            if (existing.ContainsKey(name))
            {
                logger.LogDebug("Tenant {Name} already exists; skipping", name);
                continue;
            }

            var tenant = Tenant.Create(name, type);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(ct);

            existing[name] = tenant.Id;
            inserted++;
            logger.LogDebug("Seeded tenant {Name} ({Id})", name, tenant.Id);
        }

        if (inserted > 0)
            logger.LogInformation("Seeded {Count} tenants successfully", inserted);
        else
            logger.LogInformation("All sample tenants already exist. Nothing to insert");

        await SeedSampleGradeOverridesAsync(existing, ct);

        return existing;
    }

    /// <summary>
    /// Seeds the sample <see cref="TenantCodedValueOverride"/> rows that rename the
    /// global GRADE coded values for each sample tenant. Idempotent by the
    /// (TenantId, GlobalCodedValueId) unique index: existing overrides are skipped.
    /// Silently skips any override whose coded value is not present in the database
    /// (e.g. if the coded-value seed file is trimmed) so the seeder never hard-fails
    /// on optional demo data.
    /// </summary>
    /// <remarks>
    /// FR-10: the override rows target real tenants but the seeder runs under the
    /// default (Guid.Empty) context, so the writes are wrapped in a suppressed
    /// save-guard (sanctioned bypass for seed services) and the existence check
    /// bypasses the "Tenant" filter (cross-tenant read).
    /// </remarks>
    private async Task SeedSampleGradeOverridesAsync(
        Dictionary<string, Guid> tenantIdsByName,
        CancellationToken ct)
    {
        var neededCodes = SampleGradeOverrides
            .Select(o => o.CodedValueCode)
            .Distinct()
            .ToList();

        var codeToCodedValueId = await db.CodedValues
            .Where(c => neededCodes.Contains(c.Code))
            .ToDictionaryAsync(c => c.Code, c => c.Id, ct);

        if (codeToCodedValueId.Count == 0)
        {
            logger.LogDebug("No GRADE coded values found; skipping sample grade overrides");
            return;
        }

        // Load existing overrides for the sample tenants + needed coded values once,
        // so a re-run skips already-seeded overrides (unique index backstop).
        var sampleTenantIds = SampleTenants
            .Select(t => tenantIdsByName.GetValueOrDefault(t.Name))
            .Where(id => id != Guid.Empty)
            .ToList();

        var neededCodedValueIds = codeToCodedValueId.Values.ToList();

        var existingOverrideKeys = await db.TenantCodedValueOverrides
            .IgnoreQueryFilters(["Tenant"])
            .Where(o => sampleTenantIds.Contains(o.TenantId)
                && neededCodedValueIds.Contains(o.GlobalCodedValueId))
            .Select(o => new { o.TenantId, o.GlobalCodedValueId })
            .ToListAsync(ct);

        var existingSet = existingOverrideKeys
            .Select(o => (o.TenantId, o.GlobalCodedValueId))
            .ToHashSet();

        var inserted = 0;

        // FR-10: suppress the strict save-guard for the whole override-seed pass —
        // the rows belong to real tenants but the seeder runs under the default context.
        using (tenantContextAccessor.SuppressTenantGuard())
        {
        foreach (var (tenantName, code, overriddenName) in SampleGradeOverrides)
        {
            if (!tenantIdsByName.TryGetValue(tenantName, out var tenantId))
                continue;

            if (!codeToCodedValueId.TryGetValue(code, out var codedValueId))
            {
                logger.LogDebug("Coded value {Code} not found; skipping override for {Tenant}",
                    code, tenantName);
                continue;
            }

            if (existingSet.Contains((tenantId, codedValueId)))
            {
                logger.LogDebug("Override {Tenant}/{Code} already exists; skipping",
                    tenantName, code);
                continue;
            }

            var overrideRow = TenantCodedValueOverride.Create(
                tenantId, codedValueId, overriddenName, description: null);

            db.TenantCodedValueOverrides.Add(overrideRow);
            await db.SaveChangesAsync(ct);

            existingSet.Add((tenantId, codedValueId));
            inserted++;
            logger.LogDebug("Seeded override {Tenant}/{Code} → {Name}",
                tenantName, code, overriddenName);
        }

        if (inserted > 0)
            logger.LogInformation("Seeded {Count} sample grade overrides successfully", inserted);
        else
            logger.LogInformation("All sample grade overrides already exist. Nothing to insert");
        } // end SuppressTenantGuard
    }
}