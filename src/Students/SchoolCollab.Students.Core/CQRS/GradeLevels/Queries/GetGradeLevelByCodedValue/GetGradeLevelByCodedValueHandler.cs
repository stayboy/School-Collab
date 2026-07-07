using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.GetGradeLevelByCodedValue;

public sealed class GetGradeLevelByCodedValueHandler(StudentsDbContext db)
    : IQueryHandler<GetGradeLevelByCodedValue, GradeLevelDto?>
{
    public async Task<GradeLevelDto?> HandleAsync(
        GetGradeLevelByCodedValue query,
        CancellationToken cancellationToken = default)
    {
        var gradeLevel = await db.GradeLevels
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CodedValueId == query.CodedValueId, cancellationToken);

        if (gradeLevel is null)
            return null;

        return new GradeLevelDto(
            gradeLevel.Id,
            gradeLevel.CodedValueId,
            gradeLevel.Level,
            gradeLevel.Name,
            gradeLevel.DisplayOrder,
            0,
            0,
            gradeLevel.CreatedAt,
            gradeLevel.UpdatedAt);
    }
}