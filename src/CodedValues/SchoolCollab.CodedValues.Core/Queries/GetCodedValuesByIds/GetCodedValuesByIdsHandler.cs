using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.CodedValues.Core.Caching;
using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.CodedValues.Core.DTOs;

namespace SchoolCollab.CodedValues.Core.Queries.GetCodedValuesByIds;

public sealed class GetCodedValuesByIdsHandler(
    CodedValuesDbContext db,
    HybridCache cache) : IQueryHandler<GetCodedValuesByIds, CodedValueDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<CodedValueDto[]> HandleAsync(
        GetCodedValuesByIds query,
        CancellationToken cancellationToken = default)
    {
        if (query.Ids.Length == 0)
        {
            return [];
        }

        var sortedIds = string.Join(",", query.Ids.OrderBy(id => id));
        var cacheKey = $"coded-values:by-ids:{CacheKeyHelper.Hash(sortedIds)}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            (db, query.Ids),
            static async (state, ct) =>
            {
                var (db, ids) = state;
                var results = await db.CodedValues
                    .AsNoTracking()
                    .Where(x => ids.Contains(x.Id))
                    .OrderBy(x => x.DisplayOrder)
                    .ThenBy(x => x.Name)
                    .ToArrayAsync(ct);

                return results.Select(cv => new CodedValueDto(
                    cv.Id,
                    cv.Code,
                    cv.Name,
                    cv.Description,
                    cv.ParentId,
                    cv.IsDisabled,
                    cv.DisplayOrder,
                    cv.CreatedAt,
                    cv.UpdatedAt,
                    cv.Attributes.Select(a => new CodedValueAttributeDto(a.Key, a.Value)).ToArray(),
                    cv.AttributeDefinitions.Select(d => new CodedValueAttributeDefinitionDto(d.Key, d.DisplayName, d.DataType, d.SourceCode, d.IsRequired, d.AllowMultiple, d.MinLength, d.MaxLength, d.RegexPattern)).ToArray())).ToArray();
            },
            CacheOptions,
            tags: ["coded-values"],
            cancellationToken: cancellationToken);
    }
}
