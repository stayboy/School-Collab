using Microsoft.EntityFrameworkCore;
using SchoolCollab.Settings.Core.CQRS.FeatureFlags.Commands;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.FeatureFlags.Queries;

public sealed class ListTenantOverridesHandler(SettingsDbContext db)
    : IQueryHandler<ListTenantOverrides, TenantFlagOverrideDto[]>
{
    public async Task<TenantFlagOverrideDto[]> HandleAsync(ListTenantOverrides query, CancellationToken ct = default)
    {
        var key = FeatureFlag.NormalizeKey(query.Key);
        var flag = await db.FeatureFlags.AsNoTracking()
            .SingleOrDefaultAsync(f => f.Key == key && !f.IsDeleted, ct)
            ?? throw new KeyNotFoundException($"Feature flag '{key}' not found.");

        var overrides = await db.TenantFlagOverrides.AsNoTracking()
            .IgnoreQueryFilters(["Tenant"])
            .Where(o => o.FeatureFlagId == flag.Id && !o.IsDeleted)
            .OrderByDescending(o => o.UpdatedAt)
            .ToArrayAsync(ct);

        return overrides.Select(o => new TenantFlagOverrideDto(
            o.Id, o.TenantId, o.FeatureFlagId, o.IsEnabled, o.Reason,
            o.EffectiveFrom, o.EffectiveTo, o.CreatedAt, o.UpdatedAt)).ToArray();
    }
}