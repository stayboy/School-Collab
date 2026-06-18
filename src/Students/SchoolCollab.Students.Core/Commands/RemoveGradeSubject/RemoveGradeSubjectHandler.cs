using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;

namespace SchoolCollab.Students.Core.Commands.RemoveGradeSubject;

public sealed class RemoveGradeSubjectHandler(
    IGradeSubjectAssignmentRepository repository,
    HybridCache cache,
    ILogger<RemoveGradeSubjectHandler> logger) : ICommandHandler<RemoveGradeSubject>
{
    public async Task HandleAsync(RemoveGradeSubject command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling RemoveGradeSubject {Id}", command.Id);

        var assignment = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new InvalidOperationException($"GradeSubjectAssignment with ID '{command.Id}' not found.");

        await repository.DeleteAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation("GradeSubjectAssignment {Id} removed", assignment.Id);
    }
}