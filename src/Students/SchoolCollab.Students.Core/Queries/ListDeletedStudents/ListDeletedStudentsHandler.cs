using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Queries.ListDeletedStudents;

public sealed class ListDeletedStudentsHandler(
    StudentsDbContext db,
    HybridCache cache,
    ITenantProvider tenantProvider) : IQueryHandler<ListDeletedStudents, StudentDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<StudentDto[]> HandleAsync(
        ListDeletedStudents query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;
        var cacheKey = $"students:deleted:{tenantId}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            (db, tenantId),
            static async (state, ct) =>
            {
                var (db, tid) = state;
                var results = await db.Students
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(x => x.IsDeleted && x.TenantId == tid)
                    .OrderBy(x => x.LastName)
                    .ThenBy(x => x.FirstName)
                    .ToArrayAsync(ct);

                return results.Select(s => new StudentDto(
                    s.Id,
                    s.StudentNumber,
                    s.FirstName,
                    s.LastName,
                    s.DateOfBirth,
                    s.GenderCodedValueId,
                    s.ContactEmail,
                    s.ContactPhone,
                    s.IsDeleted,
                    s.CreatedAt,
                    s.UpdatedAt)).ToArray();
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}
