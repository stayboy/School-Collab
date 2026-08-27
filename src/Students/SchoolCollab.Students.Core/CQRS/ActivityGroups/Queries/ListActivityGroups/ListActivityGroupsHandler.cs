using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Queries.ListActivityGroups;

public sealed class ListActivityGroupsHandler(StudentsDbContext db)
    : IQueryHandler<ListActivityGroups, ActivityGroupDto[]>
{
    public async Task<ActivityGroupDto[]> HandleAsync(
        ListActivityGroups query, CancellationToken cancellationToken = default)
    {
        return await db.ActivityGroups
            .AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new ActivityGroupDto(
                g.Id, g.Name, g.Description, g.Category, g.Capacity, g.IsActive,
                g.Span.ToString(), g.EnrollmentStartDate, g.EnrollmentEndDate, g.AutoRenewDefault,
                db.ActivityGroupGradeLevels
                    .Where(agg => agg.ActivityGroupId == g.Id)
                    .Select(agg => agg.GradeLevelId)
                    .ToArray(),
                db.ActivityGroupMemberships
                    .Count(m => m.ActivityGroupId == g.Id && m.Status == MembershipStatus.Active),
                g.CreatedAt, g.UpdatedAt))
            .ToArrayAsync(cancellationToken);
    }
}
