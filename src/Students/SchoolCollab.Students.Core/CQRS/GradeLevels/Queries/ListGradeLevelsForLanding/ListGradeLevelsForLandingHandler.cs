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
/// EndDate &gt;= today</c>) - there is no period parameter, so the UI can't get out
/// of sync. <see cref="GradeLevelLandingDto.StudentCount"/> is tenant-scoped via
/// the <c>Student</c> global query filter (which uses <see cref="ITenantProvider"/>);
/// <see cref="GradeLevelLandingDto.TopicCount"/> is global. See spec 5.3.
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
            (db, currentPeriodId, currentPeriod?.Name, tenantId, today),
            static async (state, ct) =>
            {
                var (db, currentPeriodId, currentPeriodName, tenantId, today) = state;
                var hasPeriod = currentPeriodId is not null;

                var rows = await db.GradeLevels
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(gl => gl.TenantId == tenantId)
                    .AsNoTracking()
                    .OrderBy(x => x.Level)
                    .Select(gl => new
                    {
                        gl.Id,
                        gl.CodedValueId,
                        gl.Name,
                        gl.MinAge,
                        gl.MaxAge,
                        gl.AllowedGenderCodedValueId,
                        gl.CreatedAt,
                        gl.UpdatedAt,
                        // TopicCount is date-effective, not period-bound: a grade's
                        // topic spans multiple years unless blocked/archived (an
                        // EndDate). It is not gated on a current period existing.
                        TopicCount = db.GradeSubjectAssignments
                            .IgnoreQueryFilters(new[] { "Tenant" })
                            .Count(ga =>
                                ga.GradeLevelId == gl.Id
                                && ga.TenantId == tenantId
                                && ga.StartDate <= today
                                && (ga.EndDate == null || ga.EndDate >= today)),
                        StudentCount = hasPeriod
                            ? db.StudentEnrollments
                                .IgnoreQueryFilters(new[] { "Tenant" })
                                .Count(se =>
                                    se.GradeLevelId == gl.Id
                                    && se.PeriodId == currentPeriodId!.Value
                                    && se.Status == EnrollmentStatus.Active
                                    && db.Students
                                        .IgnoreQueryFilters(new[] { "Tenant" })
                                        .Any(s => s.Id == se.StudentId && s.TenantId == tenantId))
                            : 0
                    })
                    .ToArrayAsync(ct);

                return rows
                    .Select(gl => new GradeLevelLandingDto(
                        gl.Id,
                        gl.CodedValueId,
                        gl.Name,
                        gl.TopicCount,
                        gl.StudentCount,
                        currentPeriodId,
                        currentPeriodName,
                        gl.CreatedAt,
                        gl.UpdatedAt,
                        gl.MinAge,
                        gl.MaxAge,
                        gl.AllowedGenderCodedValueId))
                    .ToArray();
            },
            CacheOptions,
            tags: ["students", $"tenant:{tenantId}", "periods"],
            cancellationToken: cancellationToken);
    }
}
