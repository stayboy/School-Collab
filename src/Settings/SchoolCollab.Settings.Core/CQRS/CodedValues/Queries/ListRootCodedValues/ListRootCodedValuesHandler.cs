using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.ListRootCodedValues;

public sealed class ListRootCodedValuesHandler(
    IDbContextFactory<SettingsDbContext> dbContextFactory,
    HybridCache cache,
    ITenantProvider tenantProvider) : IQueryHandler<ListRootCodedValues, CodedValueDto[]>
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
        // Short-lived context inside the cache factory — see
        // GetCodedValuesByParentHandler for the ObjectDisposedException rationale.
        return await cache.GetOrCreateAsync(
            $"tenant:{tenantProvider.GetTenantContext().TenantId}:coded-values:root",
            dbContextFactory,
            static async (dbContextFactory, ct) =>
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(ct);
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
                    cv.DeletedAt,
                    false,
                    cv.Name)).ToArray(); // No override applied here; DefaultName == Name
            },
            CacheOptions,
            tags: ["coded-values"],
            cancellationToken: cancellationToken);
    }
}
