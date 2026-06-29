using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.CodedValues.Core.DTOs;

namespace SchoolCollab.CodedValues.Core.CQRS.CodedValues.Queries.ListRootCodedValues;

public sealed class ListRootCodedValuesHandler(
    CodedValuesDbContext db,
    HybridCache cache) : IQueryHandler<ListRootCodedValues, CodedValueDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<CodedValueDto[]> HandleAsync(
        ListRootCodedValues query,
        CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(
            "coded-values:root",
            db,
            static async (db, ct) =>
            {
                var results = await db.CodedValues
                    .AsNoTracking()
                    .Where(x => x.ParentId == null)
                    .OrderBy(x => x.DisplayOrder)
                    .ThenBy(x => x.Name)
                    .ToArrayAsync(ct);

                var rootIds = results.Select(r => r.Id).ToArray();

                var childCounts = rootIds.Length == 0
                    ? new Dictionary<Guid, int>()
                    : await db.CodedValues
                        .AsNoTracking()
                        .Where(x => x.ParentId != null && rootIds.Contains(x.ParentId.Value))
                        .GroupBy(x => x.ParentId!.Value)
                        .Select(g => new { ParentId = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.ParentId, x => x.Count, ct);

                return results.Select(cv => new CodedValueDto(
                    cv.Id,
                    cv.Code,
                    cv.Name,
                    cv.Description,
                    cv.ParentId,
                    (string?)null,
                    cv.IsDisabled,
                    cv.DisplayOrder,
                    cv.CreatedAt,
                    cv.UpdatedAt,
                    cv.Attributes.Select(a => new CodedValueAttributeDto(a.Key, a.Value)).ToArray(),
                    cv.AttributeDefinitions.Select(d => new CodedValueAttributeDefinitionDto(d.Key, d.DisplayName, d.DataType, d.SourceCode, d.IsRequired, d.AllowMultiple, d.MinLength, d.MaxLength, d.RegexPattern)).ToArray(),
                    childCounts.GetValueOrDefault(cv.Id, 0),
                    cv.IsDeleted,
                    cv.DeletedAt)).ToArray();
            },
            CacheOptions,
            tags: ["coded-values"],
            cancellationToken: cancellationToken);
    }
}
