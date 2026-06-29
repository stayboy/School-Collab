using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Students.Queries.ListDeletedStudents;

public sealed class ListDeletedStudentsHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListDeletedStudents, StudentDto[]>
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
        var cacheKey = $"students:deleted:{db.CurrentTenantId}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            db,
            static async (state, ct) =>
            {
                var results = await state.Students
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(x => x.IsDeleted)
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
