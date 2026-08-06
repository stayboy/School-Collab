using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTeachers;

public sealed class ListTeachersHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListTeachers, TeacherDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<TeacherDto[]> HandleAsync(ListTeachers query, CancellationToken cancellationToken = default)
    {
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"teachers:list:{tenantId}",
            (db, tenantId),
            static async (state, ct) =>
            {
                var (db, tenantId) = state;
                var results = await db.Teachers
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(t => t.TenantId == tenantId)
                    .OrderBy(t => t.LastName).ThenBy(t => t.FirstName)
                    .ToArrayAsync(ct);

                var quals = await db.TeacherQualifications
                    .Where(q => q.TenantId == tenantId)
                    .GroupBy(q => q.TeacherId)
                    .Select(g => new { TeacherId = g.Key, CodedValueIds = g.Select(x => x.CodedValueId).ToArray() })
                    .ToDictionaryAsync(x => x.TeacherId, x => x.CodedValueIds, ct);

                return results.Select(t => new TeacherDto(
                    t.Id, t.TitleCodedValueId, t.FirstName, t.LastName, t.DisplayName,
                    t.GenderCodedValueId, t.DateOfBirth, t.LevelOfEducationCodedValueId,
                    quals.TryGetValue(t.Id, out var q) ? q : [],
                    t.IsDeleted, t.CreatedAt, t.UpdatedAt)).ToArray();
            },
            CacheOptions,
            tags: ["teachers"],
            cancellationToken: cancellationToken);
    }
}
