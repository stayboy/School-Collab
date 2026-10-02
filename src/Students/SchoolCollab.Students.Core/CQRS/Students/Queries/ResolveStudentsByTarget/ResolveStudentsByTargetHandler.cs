using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.Students.Queries.ResolveStudentsByTarget;

/// <summary>
/// Handles <see cref="ResolveStudentsByTarget"/> — the union of the five targeting legs
/// (TGT-3…TGT-9, EC-4). One round-trip per leg, then a dedupe; no ordering guarantee is
/// promised because the consumer (publish recipient resolution) is order-insensitive.
/// </summary>
public sealed class ResolveStudentsByTargetHandler(StudentsDbContext db)
    : IQueryHandler<ResolveStudentsByTarget, Guid[]>
{
    public async Task<Guid[]> HandleAsync(
        ResolveStudentsByTarget query,
        CancellationToken cancellationToken = default)
    {
        var result = new HashSet<Guid>();

        // ── allStudents leg (D-6): tenant-wide, no grade/period/group predicate. ──
        if (query.AllStudents)
        {
            var all = await db.Students
                .AsNoTracking()
                .Where(s => !s.IsDeleted)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);
            result.UnionWith(all);
        }

        // ── student leg (TGT-6): the id must exist, be non-soft-deleted and same-tenant. ──
        if (query.StudentIds.Count > 0)
        {
            var ids = query.StudentIds.Distinct().ToArray();
            var students = await db.Students
                .AsNoTracking()
                .Where(s => ids.Contains(s.Id) && !s.IsDeleted)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);
            result.UnionWith(students);
        }

        // ── Current period, derived exactly as ListStudentsByGradeHandler does. The
        //    period-scoped legs (grade / stream) contribute nothing without one; the
        //    allStudents / student / group legs are period-independent. ──
        Guid? periodId = null;
        if (query.GradeLevelIds.Count > 0 || query.StreamCodedValueIds.Count > 0)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var currentPeriod = await db.Periods
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.StartDate <= today && p.EndDate >= today, cancellationToken);
            periodId = currentPeriod?.Id;
        }

        if (periodId is Guid currentPeriodId)
        {
            // ── grade leg (TGT-4): active enrollment in the current period. ──
            if (query.GradeLevelIds.Count > 0)
            {
                var gradeIds = query.GradeLevelIds.Distinct().ToArray();
                var byGrade = await db.StudentEnrollments
                    .AsNoTracking()
                    .Where(se => gradeIds.Contains(se.GradeLevelId)
                              && se.PeriodId == currentPeriodId
                              && se.Status == EnrollmentStatus.Active)
                    .Join(db.Students, se => se.StudentId, s => s.Id, (se, s) => new { s.Id, s.IsDeleted })
                    .Where(x => !x.IsDeleted)
                    .Select(x => x.Id)
                    .ToListAsync(cancellationToken);
                result.UnionWith(byGrade);
            }

            // ── stream leg (TGT-5): grade-agnostic — the stream is the whole constraint. ──
            if (query.StreamCodedValueIds.Count > 0)
            {
                var streamIds = query.StreamCodedValueIds.Distinct().ToArray();
                var byStream = await db.StudentEnrollments
                    .AsNoTracking()
                    .Where(se => se.StreamCodedValueId != null
                              && streamIds.Contains(se.StreamCodedValueId.Value)
                              && se.PeriodId == currentPeriodId
                              && se.Status == EnrollmentStatus.Active)
                    .Join(db.Students, se => se.StudentId, s => s.Id, (se, s) => new { s.Id, s.IsDeleted })
                    .Where(x => !x.IsDeleted)
                    .Select(x => x.Id)
                    .ToListAsync(cancellationToken);
                result.UnionWith(byStream);
            }
        }

        // ── group leg (TGT-7 / EC-4): active memberships of ACTIVE groups only — archived /
        //    suspended collapse to IsActive = false. No period check: the pre-R2 publish
        //    semantics (ActivityGroupLookupHttpClient) include any Status = Active membership. ──
        if (query.ActivityGroupIds.Count > 0)
        {
            var groupIds = query.ActivityGroupIds.Distinct().ToArray();
            var byGroup = await db.ActivityGroupMemberships
                .AsNoTracking()
                .Where(m => groupIds.Contains(m.ActivityGroupId) && m.Status == MembershipStatus.Active)
                .Join(db.ActivityGroups, m => m.ActivityGroupId, g => g.Id, (m, g) => new { m.StudentId, g.IsActive })
                .Where(x => x.IsActive)
                .Join(db.Students, x => x.StudentId, s => s.Id, (x, s) => new { s.Id, s.IsDeleted })
                .Where(x => !x.IsDeleted)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
            result.UnionWith(byGroup);
        }

        return result.ToArray();
    }
}
