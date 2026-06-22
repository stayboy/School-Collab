using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.ListStudents;

public sealed class ListStudentsHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListStudents, StudentDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<StudentDto[]> HandleAsync(
        ListStudents query,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"students:list:{db.CurrentTenantId}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            db,
            static async (state, ct) =>
            {
                var results = await state.Students
                    .AsNoTracking()
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
