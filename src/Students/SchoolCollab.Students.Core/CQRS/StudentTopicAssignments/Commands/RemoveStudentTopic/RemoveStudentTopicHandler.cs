using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;

namespace SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Commands.RemoveStudentTopic;

public sealed class RemoveStudentTopicHandler(
    IStudentTopicAssignmentRepository repository,
    HybridCache cache,
    ILogger<RemoveStudentTopicHandler> logger) : ICommandHandler<RemoveStudentTopic>
{
    public async Task HandleAsync(RemoveStudentTopic command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling RemoveStudentTopic {Id}", command.Id);

        var assignment = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new InvalidOperationException($"StudentTopicAssignment with ID '{command.Id}' not found.");

        await repository.DeleteAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation("StudentTopicAssignment {Id} removed", assignment.Id);
    }
}