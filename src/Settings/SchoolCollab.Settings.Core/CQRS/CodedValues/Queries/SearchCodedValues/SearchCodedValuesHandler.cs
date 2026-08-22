using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Settings.Core.Caching;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.SearchCodedValues;

public sealed class SearchCodedValuesHandler(
    IDbContextFactory<SettingsDbContext> dbContextFactory,
    HybridCache cache,
    ITenantProvider tenantProvider) : IQueryHandler<SearchCodedValues, CodedValueDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<CodedValueDto[]> HandleAsync(
        SearchCodedValues query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.SearchText))
            return [];

        var searchText = query.SearchText.Trim();

        var pattern = $"%{searchText}%";
        var parentIdStr = query.ParentId?.ToString() ?? "root";
        var includeDisabledStr = query.IncludeDisabled ? "1" : "0";
        var rawKey = $"search:{searchText}:{parentIdStr}:{includeDisabledStr}";
        var cacheKey = $"coded-values:search:{tenantProvider.GetTenantContext().TenantId}:{CacheKeyHelper.Hash(rawKey)}";

        // Short-lived context created INSIDE the cache factory: HybridCache may
        // run this body after the triggering request's DI scope was disposed
        // (coalesced callers / cancelled requests), so a captured scoped
        // SettingsDbContext surfaces as ObjectDisposedException. See
        // GetCodedValuesByParentHandler for the full rationale.
        return await cache.GetOrCreateAsync(
            cacheKey,
            (dbContextFactory, pattern, query),
            static async (state, ct) =>
            {
                var (dbContextFactory, pattern, query) = state;
                await using var db = await dbContextFactory.CreateDbContextAsync(ct);

                IQueryable<Domain.CodedValue> q = db.CodedValues.AsNoTracking();

                if (!query.IncludeDisabled)
                    q = q.Where(x => !x.IsDisabled);

                if (query.ParentId.HasValue)
                    q = q.Where(x => x.ParentId == query.ParentId);
                else
                    q = q.Where(x => x.ParentId == null);

                q = q.Where(x =>
                    EF.Functions.ILike(x.Code, pattern) ||
                    EF.Functions.ILike(x.Name, pattern) ||
                    (x.Description != null && EF.Functions.ILike(x.Description, pattern)));

                var results = await q
                    .OrderBy(x => x.DisplayOrder)
                    .ThenBy(x => x.Name)
                    .ToArrayAsync(ct);

                var resultIds = results.Select(r => r.Id).ToArray();

                var childCounts = resultIds.Length == 0
                    ? new Dictionary<Guid, int>()
                    : await db.CodedValues
                        .AsNoTracking()
                        .Where(x => x.ParentId != null && resultIds.Contains(x.ParentId.Value))
                        .GroupBy(x => x.ParentId!.Value)
                        .Select(g => new { ParentId = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.ParentId, x => x.Count, ct);

                return results.Select(cv => ToDto(cv, childCounts.GetValueOrDefault(cv.Id, 0))).ToArray();
            },
            CacheOptions,
            tags: ["coded-values"],
            cancellationToken: cancellationToken);
    }

    private static CodedValueDto ToDto(Domain.CodedValue cv, int childrenCount) => new(
        cv.Id,
        cv.Code,
        cv.Name,
        cv.Description,
        cv.ParentId,
        null,
        cv.IsDisabled,
        cv.DisplayOrder,
        cv.CreatedAt,
        cv.UpdatedAt,
        cv.Attributes.Select(a => new CodedValueAttributeDto(a.Key, a.Value)).ToArray(),
        cv.AttributeDefinitions.Select(d => new CodedValueAttributeDefinitionDto(d.Key, d.DisplayName, d.DataType, d.SourceCode, d.IsRequired, d.AllowMultiple, d.MinLength, d.MaxLength, d.RegexPattern)).ToArray(),
        childrenCount,
        cv.IsDeleted,
        cv.DeletedAt);
}