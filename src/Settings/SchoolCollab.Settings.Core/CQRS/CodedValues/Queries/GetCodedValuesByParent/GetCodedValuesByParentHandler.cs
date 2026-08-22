using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Settings.Core.Caching;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Settings.Core.Services;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.GetCodedValuesByParent;

public sealed class GetCodedValuesByParentHandler(
    IDbContextFactory<SettingsDbContext> dbContextFactory,
    HybridCache cache,
    ITenantProvider tenantProvider) : IQueryHandler<GetCodedValuesByParent, CodedValueDto[]>
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
            (dbContextFactory, tenantId, query),
            static async (state, ct) =>
            {
                var (dbContextFactory, tenantId, query) = state;

                // Create a SHORT-LIVED context inside the cache factory instead of
                // capturing the request-scoped one. HybridCache coalesces concurrent
                // callers onto one factory execution and may run it after the first
                // caller's DI scope was disposed (e.g. the stream picker cancelling
                // an in-flight request), which surfaced as
                // ObjectDisposedException on SettingsDbContext. A context created
                // from the singleton IDbContextFactory is owned by this factory body
                // and disposed here — safe at any point in any scope. Tenant
                // filtering is unaffected: TenantProvider is a singleton backed by
                // AsyncLocal/IHttpContextAccessor, so every context — scoped or
                // factory-created — resolves the same current tenant.
                await using var db = await dbContextFactory.CreateDbContextAsync(ct);
                var repository = new CodedValueRepository(db);
                var resolver = new CodedValueResolver(repository);

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

                    if (parentId == null) return [];

                    q = q.Where(x => x.ParentId == parentId);
                }
                else
                {
                    q = q.Where(x => x.ParentId == null);
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
