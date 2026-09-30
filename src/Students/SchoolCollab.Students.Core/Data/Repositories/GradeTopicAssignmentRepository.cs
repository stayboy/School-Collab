using Microsoft.EntityFrameworkCore;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class GradeTopicAssignmentRepository(StudentsDbContext db)
    : TopicAssignmentRepository(db), IGradeTopicAssignmentRepository
{
    public async Task<TopicAssignmentDto[]> ListByGradeLevelAsync(Guid gradeLevelId, DateOnly effectiveDate, CancellationToken cancellationToken = default) =>
        await Db.GradeTopicAssignments
            .AsNoTracking()
            .Where(x => x.GradeLevelId == gradeLevelId
                && x.StartDate <= effectiveDate
                && (x.EndDate == null || x.EndDate >= effectiveDate))
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.TopicId)
            .Select(x => new TopicAssignmentDto(
                x.Id, "grade", x.GradeLevelId, null, x.TopicId,
                x.StartDate, x.EndDate,
                x.TopicStrandId,
                x.PeriodId,
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<GradeTopicAssignment[]> ListByGradeLevelForUpdateAsync(
        Guid gradeLevelId, DateOnly effectiveDate, CancellationToken cancellationToken = default) =>
        await Db.GradeTopicAssignments
            .Where(x => x.GradeLevelId == gradeLevelId
                && x.StartDate <= effectiveDate
                && (x.EndDate == null || x.EndDate >= effectiveDate))
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.TopicId)
            .ToArrayAsync(cancellationToken);

    public async Task<int> GetNextDisplayOrderAsync(
        Guid gradeLevelId, CancellationToken cancellationToken = default)
    {
        // The nullable cast only makes MaxAsync total on a grade with no rows;
        // grade rows themselves always carry an order.
        var highest = await Db.GradeTopicAssignments
            .Where(x => x.GradeLevelId == gradeLevelId)
            .Select(x => (int?)x.DisplayOrder)
            .MaxAsync(cancellationToken);
        return (highest ?? -1) + 1;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        Db.SaveChangesAsync(cancellationToken);
}
