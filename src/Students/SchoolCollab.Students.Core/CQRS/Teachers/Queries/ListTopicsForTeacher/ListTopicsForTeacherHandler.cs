using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTopicsForTeacher;

public sealed class ListTopicsForTeacherHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListTopicsForTeacher, TopicDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<TopicDto[]> HandleAsync(ListTopicsForTeacher query, CancellationToken cancellationToken = default)
    {
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"teachers:{query.TeacherId}:topics:{tenantId}",
            (db, query, tenantId),
            static async (state, ct) =>
            {
                var (db, query, tenantId) = state;
                var results = await (from ts in db.TeacherSubjects.IgnoreQueryFilters(["Tenant"])
                                     join s in db.Topics.IgnoreQueryFilters(["Tenant"]) on ts.TopicId equals s.Id
                                     where ts.TenantId == tenantId && ts.TeacherId == query.TeacherId && s.TenantId == tenantId
                                     orderby s.DisplayOrder, s.Name
                                     select new TopicDto(
                                         s.Id, s.CodedValueId, s.Code, s.Name,
                                         s.Description, s.DisplayOrder,
                                         s.CreatedAt, s.UpdatedAt))
                    .ToArrayAsync(ct);
                return results;
            },
            CacheOptions,
            tags: ["teachers"],
            cancellationToken: cancellationToken);
    }
}
