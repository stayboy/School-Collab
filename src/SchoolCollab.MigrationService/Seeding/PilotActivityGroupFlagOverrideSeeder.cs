using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.MigrationService.Seeding;

/// <summary>
/// Seeds a <see cref="TenantFeatureFlagOverride"/> that turns
/// <c>FEATURE:EnableActivityGroups</c> ON for a single pilot tenant only
/// (Phase 6.1, NFR-11). The global default stays OFF; the override pins the
/// flag ON for the pilot tenant via the tenant-override surface.
/// </summary>
/// <remarks>
/// <para><b>Why an override, not a global flip:</b> the impl checklist's literal
/// "appsettings.PilotTenant.json" wording predates the centralized Settings
/// feature-flag model (<c>documents/configuration.md</c> §5). Under that model the
/// correct, audit-traceable, tenant-scoped mechanism is a
/// <see cref="TenantFeatureFlagOverride"/> row, not a per-tenant appsettings file.
/// The user chose the override approach.</para>
/// <para><b>Idempotent by existence:</b> a re-run finds the existing override row
/// (cross-tenant read) and skips — no duplicate row, no second audit entry,
/// mirroring the other seeders' skip behaviour.</para>
/// <para><b>Cross-tenant write:</b> the override row is a strict tenant entity owned
/// by the pilot tenant, but the migration service runs under the default
/// (<see cref="Guid.Empty"/>) context. The write is wrapped in
/// <see cref="ITenantContextAccessor.RunWithExplicitTenantAsync{T}"/> so the
/// save-guard's <c>PrepareChanges</c> stamps the correct <c>TenantId</c> and accepts
/// the write — the same sanctioned path the <c>UpsertTenantFlagOverrideHandler</c>
/// uses (global-tenant-filter.md §override).</para>
/// <para><b>No outbox event:</b> the seeder runs during migration, before the outbox
/// dispatcher is consuming; the other flag seeders in <c>Program.cs</c> deliberately
/// do not publish events either. The audit row is the traceability record; the
/// seeded override is read by <c>ConfigFeatureFlagService</c> on next resolution /
/// cache refresh.</para>
/// </remarks>
public sealed class PilotActivityGroupFlagOverrideSeeder(
    SettingsDbContext db,
    ITenantContextAccessor tenantContextAccessor,
    ILogger<PilotActivityGroupFlagOverrideSeeder> logger)
{
    /// <summary>The pilot tenant that gets the activity-groups override.</summary>
    public const string PilotTenantName = "Hydeson School";

    private const string ActorId = "system:migrator";
    private const string ActorName = "Migration Service";

    /// <summary>
    /// Seeds the pilot override if absent. Runs after the tenant seed (so the
    /// pilot tenant id is known) and after the flag seed (so the flag exists).
    /// </summary>
    /// <param name="tenantIdsByName">Tenant registry keyed by name, as returned by
    /// <see cref="TenantSeeder.SeedAsync"/>.</param>
    public async Task SeedAsync(IReadOnlyDictionary<string, Guid> tenantIdsByName, CancellationToken ct = default)
    {
        // 1. Resolve the pilot tenant id (defensive — TenantSeeder runs first).
        if (!tenantIdsByName.TryGetValue(PilotTenantName, out var pilotTenantId))
        {
            logger.LogWarning("Pilot tenant {Name} not found in seeded tenant registry; skipping activity-groups override", PilotTenantName);
            return;
        }

        // 2. Resolve the live flag row (defensive — SeedEnableActivityGroupsAsync runs first).
        var key = FeatureFlag.NormalizeKey(FeatureFlagKeys.EnableActivityGroups);
        var flag = await db.FeatureFlags
            .FirstOrDefaultAsync(f => f.Key == key && !f.IsDeleted, ct);
        if (flag is null)
        {
            logger.LogWarning("Feature flag {Key} not seeded; skipping pilot override", key);
            return;
        }

        // 3. Idempotency check — skip if the override already exists (cross-tenant read).
        var existing = await db.TenantFlagOverrides
            .IgnoreQueryFilters(["Tenant"])
            .AnyAsync(o => o.TenantId == pilotTenantId
                        && o.FeatureFlagId == flag.Id
                        && !o.IsDeleted, ct);
        if (existing)
        {
            logger.LogInformation("Pilot override for {Tenant}/{Flag} already present; skipping", PilotTenantName, key);
            return;
        }

        // 4. Create + audit under the pilot tenant (sanctioned cross-tenant write).
        await tenantContextAccessor.RunWithExplicitTenantAsync(pilotTenantId, async ct2 =>
        {
            var reason = $"Pilot rollout: enable activity groups for '{PilotTenantName}' (Phase 6.1, NFR-11). Global default remains OFF.";
            var overrideRow = TenantFeatureFlagOverride.Create(
                tenantId: pilotTenantId,
                featureFlagId: flag.Id,
                isEnabled: true,
                reason: reason,
                effectiveFrom: null,
                effectiveTo: null);
            db.TenantFlagOverrides.Add(overrideRow);

            db.FlagAuditEntries.Add(FlagAuditEntry.Create(
                tenantId: pilotTenantId,
                featureFlagId: flag.Id,
                featureFlagKey: flag.Key,
                changeKind: FlagChangeKind.OverrideCreated,
                previousIsEnabled: null,   // no prior tenant override
                newIsEnabled: true,
                reason: reason,
                actorId: ActorId,
                actorDisplayName: ActorName));

            await db.SaveChangesAsync(ct2);
            return 0;
        }, ct);

        logger.LogInformation("Seeded pilot override for {Tenant}/{Flag} (IsEnabled=true)", PilotTenantName, key);
    }
}
