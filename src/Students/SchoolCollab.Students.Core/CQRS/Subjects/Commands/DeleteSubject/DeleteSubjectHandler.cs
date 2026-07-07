using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.DeleteSubject;

public sealed class DeleteSubjectHandler(
    StudentsDbContext db,
    HybridCache cache,
    ILogger<DeleteSubjectHandler> logger) : ICommandHandler<DeleteSubject>
{
    public async Task HandleAsync(DeleteSubject command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling DeleteSubject {Id}", command.Id);

        var subject = await db.Subjects.FindAsync([command.Id], cancellationToken)
            ?? throw new SubjectNotFoundException(command.Id);

        // Check for referential integrity - cannot delete if grade-subject assignments
        // or student-subject assignments reference this subject.
        var hasGradeAssignments = await db.GradeSubjectAssignments
            .AnyAsync(gsa => gsa.SubjectId == command.Id, cancellationToken);

        var hasStudentAssignments = await db.StudentSubjectAssignments
            .AnyAsync(ssa => ssa.SubjectId == command.Id, cancellationToken);

        if (hasGradeAssignments || hasStudentAssignments)
        {
            var references = new List<string>();
            if (hasGradeAssignments) references.Add("GradeSubjectAssignments");
            if (hasStudentAssignments) references.Add("StudentSubjectAssignments");
            throw new SubjectReferencedException(command.Id, references.ToArray());
        }

        subject.Delete();

        db.Subjects.Remove(subject);
        await db.SaveChangesAsync(cancellationToken);

        await cache.RemoveByTagAsync("students", cancellationToken);
        subject.ClearDomainEvents();

        logger.LogInformation("Subject {Id} deleted", subject.Id);
    }
}