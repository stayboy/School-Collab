using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Students.Queries.GetStudentById;

public sealed class GetStudentByIdHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<GetStudentById, StudentDto?>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<StudentDto?> HandleAsync(
        GetStudentById query,
        CancellationToken cancellationToken = default)
    {
        // Capture the tenant in the request scope: db.CurrentTenantId is lost
        // inside the HybridCache factory, so the global "Tenant" filter would
        // hide every row. Scope the query explicitly instead (see
        // ListStudentsHandler).
        var tenantId = db.CurrentTenantId;
        var cacheKey = $"student:{query.Id}:{tenantId}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            (db, query.Id, tenantId),
            static async (state, ct) =>
            {
                var (db, id, tenantId) = state;
                var student = await db.Students
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(s => s.TenantId == tenantId)
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == id, ct);

                if (student is null)
                    return null;

                return new StudentDto(
                    student.Id,
                    student.StudentNumber,
                    student.TitleCodedValueId,
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
