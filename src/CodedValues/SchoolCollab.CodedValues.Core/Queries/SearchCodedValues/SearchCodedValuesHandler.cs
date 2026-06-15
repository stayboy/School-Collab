using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.CodedValues.Core.Caching;
using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.CodedValues.Core.DTOs;

namespace SchoolCollab.CodedValues.Core.Queries.SearchCodedValues;

public sealed class SearchCodedValuesHandler(
    CodedValuesDbContext db,
    HybridCache cache) : IQueryHandler<SearchCodedValues, CodedValueDto[]>
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
        var cacheKey = $"coded-values:search:{CacheKeyHelper.Hash(rawKey)}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            (db, pattern, query),
            static async (state, ct) =>
            {
                var (db, pattern, query) = state;

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

                return results.Select(ToDto).ToArray();
            },
            CacheOptions,
            tags: ["coded-values"],
            cancellationToken: cancellationToken);
    }

    private static CodedValueDto ToDto(Domain.CodedValue cv) => new(
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
        0,
        cv.IsDeleted,
        cv.DeletedAt);
}