using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.ListGradeLevelsForLanding;

/// <summary>
/// Landing-page query: every grade level with counts. Deliberately <b>not</b>
/// bound to any "current period" — grade-levels setup is period-agnostic.
/// <see cref="GradeLevelLandingDto.TopicCount"/> and the strand/lesson counts are
/// date-effective on topic assignments (<c>StartDate &lt;= today &amp;&amp; (EndDate
/// == null || EndDate &gt;= today)</c>). <see cref="GradeLevelLandingDto.StudentCount"/>
/// is the number of <b>active enrollments</b> in the grade (all periods), tenant-scoped
/// via the <c>Student</c> query filter. See spec 5.3.
/// </summary>
/// <remarks>
/// <para><b>EF Core best practices applied:</b></para>
/// <list type="bullet">
/// <item><b>No N+1.</b> The handler issues a <b>constant</b> number of queries
/// regardless of how many grade levels exist. Instead of a per-row correlated
/// subquery for each count, it batch-loads the tenant's effective topic
/// assignments and the tenant's strand/lesson memberships, then aggregates
/// the counts in memory. The result set is small (a school's curriculum), so
/// client-side grouping is cheap and the SQL stays simple and index-friendly.</item>
/// <item><b>Projection only.</b> Every query uses <c>AsNoTracking()</c> and selects
/// only the columns needed (PKs, FKs, ids) — no full entity materialization.</item>
/// <item><b>Tenant scoping is explicit.</b> Where the model applies a <c>Tenant</c>
/// global query filter, the handler calls <c>IgnoreQueryFilters</c> and then applies
/// <c>TenantId == tenantId</c> itself, so all child tables agree on the same tenant
/// value captured once from <see cref="ITenantProvider"/>.</item>
/// <item><b>Aggregation is pushed to the database where it pays</b> (the student
/// count groups a potentially large enrollments set server-side), and done in memory
/// only for the small curriculum set.</item>
/// <item><b><c>AsSplitQuery()</c> is deliberately not used.</b> Split query only helps
/// when projecting collection navigations (cartesian explosion). Here every count is
/// a batch/grouped scalar, so a single query per table is correct.</item>
/// </list>
/// <para><b>Indexes that make this query fast</b> (see the Students module migrations):
/// <c>GradeTopicAssignments (TenantId, StartDate, EndDate)</c>,
/// <c>TopicStrands (TenantId, TopicId)</c>, <c>TopicLessons (TenantId, TopicId)</c>,
/// <c>StudentEnrollments (PeriodId, Status)</c>, <c>Students (Id, TenantId)</c>, and
/// <c>GradeLevels (TenantId)</c>.</para>
/// </remarks>
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
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cacheKey = $"tenant:{tenantId}:grade-levels:landing";

        return await cache.GetOrCreateAsync(
            cacheKey,
            (db, tenantId, today),
            static async (state, ct) =>
            {
                var (db, tenantId, today) = state;

                // 1. Effective topic assignments for this tenant — the single source
                //    of truth for "what a grade teaches today". One query.
                var assignments = await db.GradeTopicAssignments
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(ga =>
                        ga.TenantId == tenantId
                        && ga.StartDate <= today
                        && (ga.EndDate == null || ga.EndDate >= today))
                    .AsNoTracking()
                    .Select(ga => new { ga.GradeLevelId, ga.TopicId })
                    .ToArrayAsync(ct);

                // 2. Strand/lesson memberships for this tenant (two queries). We
                //    IgnoreQueryFilters + filter by TenantId explicitly (like the
                //    assignments query above) because the handler's tenant comes
                //    from ITenantProvider, which is not guaranteed to agree with the
                //    DbContext's query-filter tenant. Loading the (small) tenant
                //    curriculum and grouping client-side avoids a parameterized
                //    IN-list, keeping the SQL simple and portable.
                var strandTopicIds = await db.TopicStrands
                    .IgnoreQueryFilters(new[] { "Tenant" })
                    .Where(ts => ts.TenantId == tenantId && ts.ParentStrandId == null)
                    .AsNoTracking()
                    .Select(ts => ts.TopicId)
                    .ToArrayAsync(ct);
                var lessonTopicIds = await db.TopicStrands
                    .IgnoreQueryFilters(new[] { "Tenant" })
                    .Where(tl => tl.TenantId == tenantId && tl.ParentStrandId != null)
                    .AsNoTracking()
                    .Select(tl => tl.TopicId)
                    .ToArrayAsync(ct);

                var strandCountsByTopic = strandTopicIds.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
                var lessonCountsByTopic = lessonTopicIds.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());

                // Per-grade counts: each effective assignment contributes its topic's
                // strand/lesson counts (matches a SelectMany over the assignments).
                var perGrade = assignments
                    .GroupBy(a => a.GradeLevelId)
                    .ToDictionary(
                        g => g.Key,
                        g => (
                            Topics: g.Count(),
                            Strands: g.Sum(a => strandCountsByTopic.GetValueOrDefault(a.TopicId)),
                            Lessons: g.Sum(a => lessonCountsByTopic.GetValueOrDefault(a.TopicId))));

                // 3. Active enrollments per grade for the tenant's students. NOT
                //    period-bound (grade-levels setup is period-agnostic), so every
                //    Active enrollment counts regardless of which period it belongs to.
                //    Pushed to the DB (GROUP BY) because enrollments are the one table
                //    that can grow. One query.
                var studentCounts = await db.StudentEnrollments
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(se => se.Status == EnrollmentStatus.Active)
                    .Join(db.Students.IgnoreQueryFilters(["Tenant"]).AsNoTracking()
                            .Where(s => s.TenantId == tenantId),
                        se => se.StudentId,
                        s => s.Id,
                        (se, _) => se)
                    .GroupBy(se => se.GradeLevelId)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

                // 4. Grade levels + assemble DTOs. One query.
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
                        gl.IsBlockedFromEnrollment,
                        gl.CreatedAt,
                        gl.UpdatedAt
                    })
                    .ToArrayAsync(ct);

                return rows
                    .Select(gl =>
                    {
                        var counts = perGrade.GetValueOrDefault(gl.Id);
                        return new GradeLevelLandingDto(
                            gl.Id,
                            gl.CodedValueId,
                            gl.Name,
                            counts.Topics,
                            counts.Strands,
                            counts.Lessons,
                            studentCounts.GetValueOrDefault(gl.Id),
                            gl.CreatedAt,
                            gl.UpdatedAt,
                            gl.MinAge,
                            gl.MaxAge,
                            gl.AllowedGenderCodedValueId,
                            gl.IsBlockedFromEnrollment);
                    })
                    .ToArray();
            },
            CacheOptions,
            tags: ["students", $"tenant:{tenantId}"],
            cancellationToken: cancellationToken);
    }
}
