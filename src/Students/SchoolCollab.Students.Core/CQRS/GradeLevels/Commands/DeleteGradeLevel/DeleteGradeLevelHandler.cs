using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.DeleteGradeLevel;

public sealed class DeleteGradeLevelHandler(
    StudentsDbContext db,
    HybridCache cache,
    ILogger<DeleteGradeLevelHandler> logger) : ICommandHandler<DeleteGradeLevel>
{
    public async Task HandleAsync(DeleteGradeLevel command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling DeleteGradeLevel {Id}", command.Id);

        var gradeLevel = await db.GradeLevels.FindAsync([command.Id], cancellationToken)
            ?? throw new GradeLevelNotFoundException(command.Id);

        // Check for referential integrity - cannot delete if students are enrolled
        // in this grade level or subjects are assigned to it.
        var hasEnrollments = await db.StudentEnrollments
            .AnyAsync(se => se.GradeLevelId == command.Id, cancellationToken);

        var hasTopicAssignments = await db.GradeTopicAssignments
            .AnyAsync(gsa => gsa.GradeLevelId == command.Id, cancellationToken);

        if (hasEnrollments || hasTopicAssignments)
        {
            var references = new List<string>();
            if (hasEnrollments) references.Add("StudentEnrollments");
            if (hasTopicAssignments) references.Add("GradeTopicAssignments");
            throw new GradeLevelReferencedException(command.Id, references.ToArray());
        }

        gradeLevel.Delete();

        db.GradeLevels.Remove(gradeLevel);
        await db.SaveChangesAsync(cancellationToken);

        await cache.RemoveByTagAsync("students", cancellationToken);
        gradeLevel.ClearDomainEvents();

        logger.LogInformation("GradeLevel {Id} deleted", gradeLevel.Id);
    }
}