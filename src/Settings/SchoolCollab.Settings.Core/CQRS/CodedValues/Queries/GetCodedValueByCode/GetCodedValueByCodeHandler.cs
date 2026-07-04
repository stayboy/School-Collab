using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Settings.Core.Services;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.GetCodedValueByCode;

public sealed class GetCodedValueByCodeHandler(
    SettingsDbContext db,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ICodedValueResolver resolver) : IQueryHandler<GetCodedValueByCode, CodedValueDto?>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<CodedValueDto?> HandleAsync(
        GetCodedValueByCode query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;
        var normalisedCode = query.Code.Trim().ToUpperInvariant();
        var cacheKey = query.ParentId.HasValue
            ? $"tenant:{tenantId}:coded-value:code:{normalisedCode}:parent:{query.ParentId.Value}"
            : $"tenant:{tenantId}:coded-value:code:{normalisedCode}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            (db, resolver, tenantId, normalisedCode, query.ParentId),
            static async (state, ct) =>
            {
                var (db, resolver, tenantId, code, parentId) = state;
                var cv = parentId.HasValue
                    ? await db.CodedValues
                        .AsNoTracking()
                        .Include(x => x.Attributes)
                        .Include(x => x.AttributeDefinitions)
                        .FirstOrDefaultAsync(x => x.Code == code && x.ParentId == parentId, ct)
                    : await db.CodedValues
                        .AsNoTracking()
                        .Include(x => x.Attributes)
                        .Include(x => x.AttributeDefinitions)
                        .FirstOrDefaultAsync(x => x.Code == code, ct);

                if (cv is null)
                    return null;

                return await resolver.ResolveAsync(cv, tenantId, ct);
            },
            CacheOptions,
            tags: ["coded-values", $"tenant:{tenantId}"],
            cancellationToken: cancellationToken);
    }
}