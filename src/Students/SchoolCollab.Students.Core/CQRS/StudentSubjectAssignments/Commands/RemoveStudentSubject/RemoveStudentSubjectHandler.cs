using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;

namespace SchoolCollab.Students.Core.CQRS.StudentSubjectAssignments.Commands.RemoveStudentSubject;

public sealed class RemoveStudentSubjectHandler(
    IStudentSubjectAssignmentRepository repository,
    HybridCache cache,
    ILogger<RemoveStudentSubjectHandler> logger) : ICommandHandler<RemoveStudentSubject>
{
    public async Task HandleAsync(RemoveStudentSubject command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling RemoveStudentSubject {Id}", command.Id);

        var assignment = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new InvalidOperationException($"StudentSubjectAssignment with ID '{command.Id}' not found.");

        await repository.DeleteAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation("StudentSubjectAssignment {Id} removed", assignment.Id);
    }
}