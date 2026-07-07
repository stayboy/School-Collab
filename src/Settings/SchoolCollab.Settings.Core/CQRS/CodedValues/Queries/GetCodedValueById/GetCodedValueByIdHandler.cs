using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.GetCodedValueById;

public sealed class GetCodedValueByIdHandler(
    SettingsDbContext db,
    HybridCache cache) : IQueryHandler<GetCodedValueById, CodedValueDto?>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<CodedValueDto?> HandleAsync(
        GetCodedValueById query,
        CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(
            $"coded-value:{query.Id}",
            (db, query.Id),
            static async (state, ct) =>
            {
                var (db, id) = state;
                var tenantId = db.CurrentTenantId;

                var cv = await db.CodedValues
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == id, ct);

                if (cv is null)
                    return null;

                var overrideVal = await db.TenantCodedValueOverrides
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.GlobalCodedValueId == id && x.TenantId == tenantId, ct);

                string? parentCode = cv.ParentId.HasValue
                    ? await db.CodedValues.AsNoTracking()
                        .Where(x => x.Id == cv.ParentId.Value)
                        .Select(x => x.Code)
                        .SingleOrDefaultAsync(ct)
                    : null;

                return new CodedValueDto(
                    cv.Id,
                    cv.Code,
                    overrideVal?.OverriddenName ?? cv.Name,
                    overrideVal?.OverriddenDescription ?? cv.Description,
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
                    cv.DeletedAt,
                    overrideVal is not null);
            },
            CacheOptions,
            tags: ["coded-values"],
            cancellationToken: cancellationToken);
    }
}
