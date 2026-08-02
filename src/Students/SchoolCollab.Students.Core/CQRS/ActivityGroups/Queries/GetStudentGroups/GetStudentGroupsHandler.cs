using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Queries.GetStudentGroups;

public sealed class GetStudentGroupsHandler(StudentsDbContext db)
    : IQueryHandler<GetStudentGroups, ActivityGroupDto[]>
{
    public async Task<ActivityGroupDto[]> HandleAsync(
        GetStudentGroups query, CancellationToken cancellationToken = default)
    {
        // Return only groups where the student has an active membership.
        return await db.ActivityGroups
            .AsNoTracking()
            .Where(g => db.ActivityGroupMemberships
                .Any(m => m.ActivityGroupId == g.Id
                    && m.StudentId == query.StudentId
                    && m.Status == MembershipStatus.Active))
            .OrderBy(g => g.Name)
            .Select(g => new ActivityGroupDto(
                g.Id, g.Name, g.Description, g.Category, g.PeriodId,
                g.Capacity, g.Status.ToString(),
                db.ActivityGroupMemberships
                    .Count(m => m.ActivityGroupId == g.Id && m.Status == MembershipStatus.Active),
                g.CreatedAt, g.UpdatedAt))
            .ToArrayAsync(cancellationToken);
    }
}
