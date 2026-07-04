using Microsoft.EntityFrameworkCore;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.FeatureFlags.Queries;

public sealed class ListFeatureFlagsHandler(SettingsDbContext db)
    : IQueryHandler<ListFeatureFlags, FeatureFlagDto[]>
{
    public async Task<FeatureFlagDto[]> HandleAsync(ListFeatureFlags query, CancellationToken ct = default)
    {
        var normalized = string.IsNullOrWhiteSpace(query.Search) ? null : FeatureFlag.NormalizeKey(query.Search);

        var flags = await db.FeatureFlags
            .AsNoTracking()
            .Where(f => !f.IsDeleted)
            .Where(f => query.IncludeArchived || !f.IsArchived)
            .Where(f => normalized == null || f.Key == normalized || EF.Functions.ILike(f.Name, $"%{query.Search}%"))
            .OrderBy(f => f.Key)
            .ToArrayAsync(ct);

        var flagIds = flags.Select(f => f.Id).ToArray();
        var overrideCounts = flagIds.Length == 0
            ? new Dictionary<Guid, int>()
            : await db.TenantFlagOverrides
                .AsNoTracking()
                .Where(o => flagIds.Contains(o.FeatureFlagId) && !o.IsDeleted)
                .GroupBy(o => o.FeatureFlagId)
                .Select(g => new { FlagId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.FlagId, x => x.Count, ct);

        return flags.Select(f => ToDto(f, overrideCounts.GetValueOrDefault(f.Id, 0))).ToArray();
    }

    private static FeatureFlagDto ToDto(FeatureFlag f, int overrideCount) => new(
        f.Id, f.Key, f.Name, f.Description, (FlagKindDto)f.Kind, f.IsEnabled, f.IsArchived, f.IsDeleted,
        overrideCount, f.CreatedAt, f.UpdatedAt);
}

public sealed class GetFeatureFlagHandler(SettingsDbContext db)
    : IQueryHandler<GetFeatureFlag, FeatureFlagDto?>
{
    public async Task<FeatureFlagDto?> HandleAsync(GetFeatureFlag query, CancellationToken ct = default)
    {
        var normalized = FeatureFlag.NormalizeKey(query.Key);
        var flag = await db.FeatureFlags.AsNoTracking()
            .SingleOrDefaultAsync(f => f.Key == normalized && !f.IsDeleted, ct);
        if (flag is null) return null;

        var overrideCount = await db.TenantFlagOverrides.AsNoTracking()
            .CountAsync(o => o.FeatureFlagId == flag.Id && !o.IsDeleted, ct);

        return new FeatureFlagDto(flag.Id, flag.Key, flag.Name, flag.Description,
            (FlagKindDto)flag.Kind, flag.IsEnabled, flag.IsArchived, flag.IsDeleted,
            overrideCount, flag.CreatedAt, flag.UpdatedAt);
    }
}

public sealed class ListAuditEntriesHandler(SettingsDbContext db)
    : IQueryHandler<ListAuditEntries, FlagAuditEntryDto[]>
{
    public async Task<FlagAuditEntryDto[]> HandleAsync(ListAuditEntries query, CancellationToken ct = default)
    {
        var normalizedKey = string.IsNullOrWhiteSpace(query.Key) ? null : FeatureFlag.NormalizeKey(query.Key);

        var entries = await db.FlagAuditEntries.AsNoTracking()
            .Where(e => normalizedKey == null || e.FeatureFlagKey == normalizedKey)
            .Where(e => query.TenantId == null || e.TenantId == query.TenantId)
            .Where(e => query.From == null || e.OccurredAt >= query.From)
            .Where(e => query.To == null || e.OccurredAt <= query.To)
            .OrderByDescending(e => e.OccurredAt)
            .Skip(query.Skip).Take(query.Take)
            .ToArrayAsync(ct);

        return entries.Select(e => new FlagAuditEntryDto(
            e.Id, e.TenantId, e.FeatureFlagId, e.FeatureFlagKey, e.ChangeKind.ToString(),
            e.PreviousIsEnabled, e.NewIsEnabled, e.Reason, e.ActorId, e.ActorDisplayName, e.OccurredAt)).ToArray();
    }
}

public sealed class ResolveFlagsForTenantHandler(SettingsDbContext db)
    : IQueryHandler<ResolveFlagsForTenant, ResolvedFlagDto[]>
{
    public async Task<ResolvedFlagDto[]> HandleAsync(ResolveFlagsForTenant query, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var flags = await db.FeatureFlags.AsNoTracking()
            .Where(f => !f.IsDeleted && !f.IsArchived)
            .ToArrayAsync(ct);

        var flagIds = flags.Select(f => f.Id).ToArray();
        var overrides = flagIds.Length == 0 || query.TenantId is null
            ? Array.Empty<TenantFeatureFlagOverride>()
            // The resolver is explicitly cross-tenant: the caller passes the tenant
            // to resolve for, so the ambient tenant filter (set from the caller's own
            // auth, which is absent for the anonymous consumer endpoint) must NOT
            // apply. Filter by query.TenantId only.
            : await db.TenantFlagOverrides.AsNoTracking()
                .IgnoreQueryFilters(["Tenant"])
                .Where(o => o.TenantId == query.TenantId && !o.IsDeleted && flagIds.Contains(o.FeatureFlagId))
                .ToArrayAsync(ct);

        var overrideByFlagId = overrides
            .Where(o => o.IsInEffectAt(now))
            .GroupBy(o => o.FeatureFlagId)
            .ToDictionary(g => g.Key, g => g.First());

        return flags.Select(f =>
        {
            if (overrideByFlagId.TryGetValue(f.Id, out var ov) && ov.IsEnabled is { } pinned)
            {
                return new ResolvedFlagDto(f.Key, pinned, "TenantOverride", now);
            }

            return new ResolvedFlagDto(f.Key, f.IsEnabled, "GlobalDefault", now);
        }).ToArray();
    }
}