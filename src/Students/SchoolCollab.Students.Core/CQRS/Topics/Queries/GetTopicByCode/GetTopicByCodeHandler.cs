using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Topics.Queries.GetTopicByCode;

public sealed class GetTopicByCodeHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<GetTopicByCode, TopicDto?>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<TopicDto?> HandleAsync(
        GetTopicByCode query,
        CancellationToken cancellationToken = default)
    {
        var normalisedCode = query.Code.Trim().ToUpperInvariant();
        var cacheKey = $"subject:code:{db.CurrentTenantId}:{normalisedCode}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            (db, normalisedCode),
            static async (state, ct) =>
            {
                var (db, code) = state;
                var subject = await db.Topics
                    .IgnoreQueryFilters(["Tenant"])
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Code == code, ct);

                if (subject is null)
                    return null;

                return new TopicDto(
                    subject.Id,
                    subject.CodedValueId,
                    subject.Code,
                    subject.Name,
                    subject.Description,
                    subject.DisplayOrder,
                    subject.CreatedAt,
                    subject.UpdatedAt);
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}