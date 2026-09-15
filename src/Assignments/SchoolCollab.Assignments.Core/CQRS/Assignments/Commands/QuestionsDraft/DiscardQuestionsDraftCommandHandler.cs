namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.QuestionsDraft;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.CQRS;

/// <summary>
/// WS-B2 — handler for <see cref="DiscardQuestionsDraftCommand"/>. Clears the
/// staged blob via <c>Assignment.DiscardQuestionsDraft</c> (Draft-only; idempotent
/// when no draft is staged).
/// </summary>
public sealed class DiscardQuestionsDraftCommandHandler(
    IAssignmentRepository repository,
    HybridCache cache,
    ILogger<DiscardQuestionsDraftCommandHandler> logger) : ICommandHandler<DiscardQuestionsDraftCommand>
{
    public async Task HandleAsync(DiscardQuestionsDraftCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Discarding questions draft for assignment {AssignmentId}", command.AssignmentId);

        var assignment = await repository.GetAsync(command.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.AssignmentId);

        assignment.DiscardQuestionsDraft();

        await repository.UpdateAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);
        assignment.ClearDomainEvents();

        logger.LogInformation("Discarded questions draft for assignment {AssignmentId}", command.AssignmentId);
    }
}
