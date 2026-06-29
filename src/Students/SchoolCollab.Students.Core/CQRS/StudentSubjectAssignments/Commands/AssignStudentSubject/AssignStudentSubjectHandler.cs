using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.StudentSubjectAssignments.Commands.AssignStudentSubject;

public sealed class AssignStudentSubjectHandler(
    IStudentSubjectAssignmentRepository repository,
    HybridCache cache,
    ILogger<AssignStudentSubjectHandler> logger) : ICommandHandler<AssignStudentSubject, Guid>
{
    public async Task<Guid> HandleAsync(AssignStudentSubject command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling AssignStudentSubject for student {StudentId} subject {SubjectId}", command.StudentId, command.SubjectId);

        var assignment = StudentSubjectAssignment.Create(
            command.StudentId,
            command.SubjectId,
            command.PeriodId,
            command.IsOverride,
            command.SourceType);

        await repository.AddAsync(assignment, cancellationToken);
        assignment.ClearDomainEvents();
        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation("StudentSubjectAssignment {Id} created", assignment.Id);
        return assignment.Id;
    }
}