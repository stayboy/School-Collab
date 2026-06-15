using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.CodedValues.Core.DTOs;

namespace SchoolCollab.CodedValues.Core.Queries.GetCodedValueByCode;

public sealed class GetCodedValueByCodeHandler(
    CodedValuesDbContext db,
    HybridCache cache) : IQueryHandler<GetCodedValueByCode, CodedValueDto?>
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
        var normalisedCode = query.Code.Trim().ToUpperInvariant();
        var cacheKey = query.ParentId.HasValue
            ? $"coded-value:code:{normalisedCode}:parent:{query.ParentId.Value}"
            : $"coded-value:code:{normalisedCode}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            (db, normalisedCode, query.ParentId),
            static async (state, ct) =>
            {
                var (db, code, parentId) = state;
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

                string? parentCode = cv.ParentId.HasValue
                    ? await db.CodedValues.AsNoTracking()
                        .Where(x => x.Id == cv.ParentId.Value)
                        .Select(x => x.Code)
                        .SingleOrDefaultAsync(ct)
                    : null;

                return new CodedValueDto(
                    cv.Id,
                    cv.Code,
                    cv.Name,
                    cv.Description,
                    cv.ParentId,
                    parentCode,
                    cv.IsDisabled,
                    cv.DisplayOrder,
                    cv.CreatedAt,
                    cv.UpdatedAt,
                    cv.Attributes.Select(a => new CodedValueAttributeDto(a.Key, a.Value)).ToArray(),
                    cv.AttributeDefinitions.Select(d => new CodedValueAttributeDefinitionDto(d.Key, d.DisplayName, d.DataType, d.SourceCode, d.IsRequired, d.AllowMultiple, d.MinLength, d.MaxLength, d.RegexPattern)).ToArray(),
                    0,
                    cv.IsDeleted,
                    cv.DeletedAt);
            },
            CacheOptions,
            tags: ["coded-values"],
            cancellationToken: cancellationToken);
    }
}