namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.QuestionsDraft;

using System.Text.Json;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.CQRS;

/// <summary>
/// WS-B2 — handler for <see cref="StageQuestionsDraftCommand"/>. Validates the
/// inbound questions (validate-first, the update handler's precedent — an
/// invalid draft never enters the blob, FR-252), serializes them with the Api's
/// Web-default JSON options, and calls <c>Assignment.StageQuestionsDraft</c>.
/// </summary>
public sealed class StageQuestionsDraftCommandHandler(
    IAssignmentRepository repository,
    HybridCache cache,
    ILogger<StageQuestionsDraftCommandHandler> logger) : ICommandHandler<StageQuestionsDraftCommand>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(StageQuestionsDraftCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Staging questions draft for assignment {AssignmentId}", command.AssignmentId);

        var assignment = await repository.GetAsync(command.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.AssignmentId);

        // Validate-first: an invalid draft must never enter the blob (FR-252).
        QuestionOptionDtoValidator.ValidateQuestions(command.Questions);

        var json = JsonSerializer.Serialize(command.Questions.ToList(), JsonOptions);
        assignment.StageQuestionsDraft(json);

        await repository.UpdateAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);
        assignment.ClearDomainEvents();

        logger.LogInformation("Staged questions draft for assignment {AssignmentId}", command.AssignmentId);
    }
}
