using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.GetGradeLevelById;

public sealed class GetGradeLevelByIdHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<GetGradeLevelById, GradeLevelDto?>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<GradeLevelDto?> HandleAsync(
        GetGradeLevelById query,
        CancellationToken cancellationToken = default)
    {
        // Capture the tenant in the request scope: db.CurrentTenantId is lost
        // inside the HybridCache factory (see ListGradeLevelsHandler).
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"grade-level:{query.Id}",
            (db, query.Id, tenantId),
            static async (state, ct) =>
            {
                var (db, id, tenantId) = state;
                var gradeLevel = await db.GradeLevels
                    .IgnoreQueryFilters(["Tenant"])
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == id, ct);

                if (gradeLevel is null)
                    return null;

                var studentCount = await db.StudentEnrollments
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(se => se.GradeLevelId == id && se.Status == EnrollmentStatus.Active)
                    .Join(db.Students.IgnoreQueryFilters(["Tenant"]).AsNoTracking()
                            .Where(s => s.TenantId == tenantId),
                        se => se.StudentId,
                        s => s.Id,
                        (se, _) => se)
                    .CountAsync(ct);

                return new GradeLevelDto(
                    gradeLevel.Id,
                    gradeLevel.CodedValueId,
                    gradeLevel.Level,
                    gradeLevel.Name,
                    gradeLevel.DisplayOrder,
                    0,
                    studentCount,
                    gradeLevel.CreatedAt,
                    gradeLevel.UpdatedAt,
                    gradeLevel.MinAge,
                    gradeLevel.MaxAge,
                    gradeLevel.AllowedGenderCodedValueId,
                    gradeLevel.IsBlockedFromEnrollment);
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}