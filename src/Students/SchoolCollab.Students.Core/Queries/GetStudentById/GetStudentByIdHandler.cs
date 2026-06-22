using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Queries.GetStudentById;

public sealed class GetStudentByIdHandler(
    StudentsDbContext db,
    HybridCache cache,
    ITenantProvider tenantProvider) : IQueryHandler<GetStudentById, StudentDto?>
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
        var tenantId = tenantProvider.GetTenantContext().TenantId;
        var cacheKey = $"student:{query.Id}:{tenantId}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            (db, query.Id, tenantId),
            static async (state, ct) =>
            {
                var (db, id, tid) = state;
                var student = await db.Students
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tid, ct);

                if (student is null)
                    return null;

                return new StudentDto(
                    student.Id,
                    student.StudentNumber,
                    student.FirstName,
                    student.LastName,
                    student.DateOfBirth,
                    student.GenderCodedValueId,
                    student.ContactEmail,
                    student.ContactPhone,
                    student.IsDeleted,
                    student.CreatedAt,
                    student.UpdatedAt);
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}
