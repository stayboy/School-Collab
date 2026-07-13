using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListSubjectsForTeacher;

public sealed class ListSubjectsForTeacherHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListSubjectsForTeacher, SubjectDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<SubjectDto[]> HandleAsync(ListSubjectsForTeacher query, CancellationToken cancellationToken = default)
    {
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"teachers:{query.TeacherId}:subjects:{tenantId}",
            (db, query, tenantId),
            static async (state, ct) =>
            {
                var (db, query, tenantId) = state;
                var results = await (from ts in db.TeacherSubjects.IgnoreQueryFilters(["Tenant"])
                                     join s in db.Subjects.IgnoreQueryFilters(["Tenant"]) on ts.SubjectId equals s.Id
                                     where ts.TenantId == tenantId && ts.TeacherId == query.TeacherId && s.TenantId == tenantId
                                     orderby s.DisplayOrder, s.Name
                                     select new SubjectDto(s.Id, s.CodedValueId, s.Code, s.Name, s.DisplayOrder, s.CreatedAt, s.UpdatedAt))
                    .ToArrayAsync(ct);
                return results;
            },
            CacheOptions,
            tags: ["teachers"],
            cancellationToken: cancellationToken);
    }
}
