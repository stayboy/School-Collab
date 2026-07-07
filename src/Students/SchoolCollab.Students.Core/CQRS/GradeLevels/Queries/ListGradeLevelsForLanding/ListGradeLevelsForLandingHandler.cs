using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.ListGradeLevelsForLanding;

/// <summary>
/// Landing-page query: every grade level with per-<b>current-period</b> counts.
/// The current period is derived server-side (<c>StartDate &lt;= today &amp;&amp;
/// EndDate &gt;= today</c>) — there is no period parameter, so the UI can't get out
/// of sync. <see cref="GradeLevelLandingDto.StudentCount"/> is tenant-scoped via
/// the <c>Student</c> global query filter (which uses <see cref="ITenantProvider"/>);
/// <see cref="GradeLevelLandingDto.SubjectCount"/> is global. See spec §5.3.
/// </summary>
public sealed class ListGradeLevelsForLandingHandler(
    StudentsDbContext db,
    ITenantProvider tenantProvider,
    HybridCache cache) : IQueryHandler<ListGradeLevelsForLanding, GradeLevelLandingDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<GradeLevelLandingDto[]> HandleAsync(
        ListGradeLevelsForLanding query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;

        // Derive the current period OUTSIDE the cache (cheap FirstOrDefault) so the
        // cache key can vary by period. Period create/activate invalidate the
        // `periods` tag (evicting this entry), so a newly-current period is picked
        // up on the next landing request.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var currentPeriod = await db.Periods
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.StartDate <= today && p.EndDate >= today, cancellationToken);

        var currentPeriodId = currentPeriod?.Id;
        var periodKeySegment = currentPeriodId?.ToString() ?? "none";
        var cacheKey = $"tenant:{tenantId}:grade-levels:landing:current-period:{periodKeySegment}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            (db, currentPeriodId, currentPeriod?.Name),
            static async (state, ct) =>
            {
                var (db, currentPeriodId, currentPeriodName) = state;
                var hasPeriod = currentPeriodId is not null;

                var rows = await db.GradeLevels
                    .AsNoTracking()
                    .OrderBy(x => x.Level)
                    .Select(gl => new
                    {
                        gl.Id,
                        gl.CodedValueId,
                        gl.Level,
                        gl.Name,
                        gl.DisplayOrder,
                        gl.CreatedAt,
                        gl.UpdatedAt,
                        // SubjectCount is GLOBAL (subjects are the shared curriculum
                        // blueprint): GradeSubjectAssignments for (grade, current period).
                        SubjectCount = hasPeriod
                            ? db.GradeSubjectAssignments.Count(ga =>
                                ga.GradeLevelId == gl.Id && ga.PeriodId == currentPeriodId!.Value)
                            : 0,
                        // StudentCount is TENANT-SCOPED via the Student global query
                        // filter (tenant + soft-delete), joined through enrollments
                        // for (grade, current period, Active status).
                        StudentCount = hasPeriod
                            ? db.StudentEnrollments.Count(se =>
                                se.GradeLevelId == gl.Id
                                && se.PeriodId == currentPeriodId!.Value
                                && se.Status == EnrollmentStatus.Active
                                && db.Students.Any(s => s.Id == se.StudentId))
                            : 0
                    })
                    .ToArrayAsync(ct);

                return rows
                    .Select(gl => new GradeLevelLandingDto(
                        gl.Id,
                        gl.CodedValueId,
                        gl.Level,
                        gl.Name,
                        gl.DisplayOrder,
                        gl.SubjectCount,
                        gl.StudentCount,
                        currentPeriodId,
                        currentPeriodName,
                        gl.CreatedAt,
                        gl.UpdatedAt))
                    .ToArray();
            },
            CacheOptions,
            tags: ["students", $"tenant:{tenantId}", "periods"],
            cancellationToken: cancellationToken);
    }
}