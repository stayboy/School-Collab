using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Students.Queries.GetStudentByStudentNumber;

public sealed class GetStudentByStudentNumberHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<GetStudentByStudentNumber, StudentDto?>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<StudentDto?> HandleAsync(
        GetStudentByStudentNumber query,
        CancellationToken cancellationToken = default)
    {
        var normalisedNumber = query.StudentNumber.Trim().ToUpperInvariant();
        var cacheKey = $"student:number:{normalisedNumber}:{db.CurrentTenantId}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            (db, normalisedNumber),
            static async (state, ct) =>
            {
                var (db, studentNumber) = state;
                var student = await db.Students
                    .IgnoreQueryFilters(["Tenant"])
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.StudentNumber == studentNumber, ct);

                if (student is null)
                    return null;

                return new StudentDto(
                    student.Id,
                    student.StudentNumber,
                    student.FirstName,
                    student.LastName,
                    student.DateOfBirth,
                    student.GenderCodedValueId,
                    student.IsDeleted,
                    student.CreatedAt,
                    student.UpdatedAt);
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}
