using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.CodedValues.Core.Caching;
using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.CodedValues.Core.DTOs;
using SchoolCollab.CodedValues.Core.Services;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.CodedValues.Core.Queries.GetCodedValuesByParent;

public sealed class GetCodedValuesByParentHandler(
    CodedValuesDbContext db,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ICodedValueResolver resolver) : IQueryHandler<GetCodedValuesByParent, CodedValueDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<CodedValueDto[]> HandleAsync(
        GetCodedValuesByParent query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;
        var filterStr = query.AttributeFilters is { Count: > 0 }
            ? string.Join("|", query.AttributeFilters
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={kv.Value}"))
            : string.Empty;

        var rawKey = $"{tenantId}:{query.ParentId}:{query.ParentCode?.Trim().ToUpperInvariant() ?? string.Empty}:{query.IncludeDisabled}:{filterStr}";
        var cacheKey = $"tenant:{tenantId}:coded-values:by-parent:{CacheKeyHelper.Hash(rawKey)}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            (db, resolver, tenantId, query),
            static async (state, ct) =>
            {
                var (db, resolver, tenantId, query) = state;

                IQueryable<Domain.CodedValue> q = db.CodedValues.AsNoTracking();

                if (!query.IncludeDisabled)
                {
                    q = q.Where(x => !x.IsDisabled);
                }

                if (query.ParentId.HasValue)
                {
                    q = q.Where(x => x.ParentId == query.ParentId);
                }
                else if (!string.IsNullOrWhiteSpace(query.ParentCode))
                {
                    var parentCode = query.ParentCode.Trim().ToUpperInvariant();
                    var parentId = await db.CodedValues
                        .AsNoTracking()
                        .Where(x => x.Code == parentCode)
                        .Select(x => (Guid?)x.Id)
                        .SingleOrDefaultAsync(ct);

                    q = q.Where(x => x.ParentId == parentId);
                }

                if (query.AttributeFilters is { Count: > 0 })
                {
                    foreach (var (key, value) in query.AttributeFilters)
                    {
                        var k = key;
                        var v = value;
                        q = q.Where(x => x.Attributes.Any(a => a.Key == k && a.Value == v));
                    }
                }

                var results = await q
                    .Include(x => x.Attributes)
                    .Include(x => x.AttributeDefinitions)
                    .OrderBy(x => x.DisplayOrder)
                    .ThenBy(x => x.Name)
                    .ToArrayAsync(ct);

                var resolved = new List<CodedValueDto>();
                foreach (var cv in results)
                {
                    resolved.Add(await resolver.ResolveAsync(cv, tenantId, ct));
                }

                return resolved.ToArray();
            },
            CacheOptions,
            tags: ["coded-values", $"tenant:{tenantId}"],
            cancellationToken: cancellationToken);
    }
}
