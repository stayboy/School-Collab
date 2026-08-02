using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Queries.GetActivityGroupById;

public sealed class GetActivityGroupByIdHandler(StudentsDbContext db)
    : IQueryHandler<GetActivityGroupById, ActivityGroupDto?>
{
    public async Task<ActivityGroupDto?> HandleAsync(
        GetActivityGroupById query, CancellationToken cancellationToken = default)
    {
        return await db.ActivityGroups
            .AsNoTracking()
            .Where(g => g.Id == query.Id)
            .Select(g => new ActivityGroupDto(
                g.Id, g.Name, g.Description, g.Category, g.PeriodId,
                g.Capacity, g.Status.ToString(),
                db.ActivityGroupMemberships
                    .Count(m => m.ActivityGroupId == g.Id && m.Status == MembershipStatus.Active),
                g.CreatedAt, g.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
