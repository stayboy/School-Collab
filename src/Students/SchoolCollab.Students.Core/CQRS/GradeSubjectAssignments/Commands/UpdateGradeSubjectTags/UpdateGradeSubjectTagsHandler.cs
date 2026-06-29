using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeSubjectAssignments.Commands.UpdateGradeSubjectTags;

public sealed class UpdateGradeSubjectTagsHandler(StudentsDbContext db) : ICommandHandler<UpdateGradeSubjectTags, GradeSubjectAssignmentDto>
{
    public async Task<GradeSubjectAssignmentDto> HandleAsync(UpdateGradeSubjectTags command, CancellationToken ct = default)
    {
        var assignment = await db.GradeSubjectAssignments.FindAsync(new object[] { command.AssignmentId }, ct);
        if (assignment == null) throw new KeyNotFoundException($"GradeSubjectAssignment {command.AssignmentId} not found.");

        assignment.UpdateTags(command.SubjectStrandId, command.SubjectLessonId);
        await db.SaveChangesAsync(ct);

        return new GradeSubjectAssignmentDto(
            assignment.Id,
            assignment.GradeLevelId,
            assignment.SubjectId,
            assignment.PeriodId,
            assignment.SubjectStrandId,
            assignment.SubjectLessonId,
            assignment.CreatedAt,
            assignment.UpdatedAt);
    }
}