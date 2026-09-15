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
/// WS-B2 — handler for <see cref="ConfirmQuestionsDraftCommand"/>. Atomically
/// materializes the staged blob into the aggregate's owned <c>Questions</c>
/// collection (full-replacement, mirroring the
/// <c>UpdateAssignmentCommandHandler</c> question-sync block: snapshot ids,
/// remove each, re-add the drafted set re-indexed 0..n), then clears the blob via
/// <c>Assignment.ConfirmQuestionsDraft</c>. A missing or unparseable blob throws
/// the typed <see cref="InvalidQuestionsDraftException"/> (defensive).
/// </summary>
public sealed class ConfirmQuestionsDraftCommandHandler(
    IAssignmentRepository repository,
    HybridCache cache,
    ILogger<ConfirmQuestionsDraftCommandHandler> logger) : ICommandHandler<ConfirmQuestionsDraftCommand>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(ConfirmQuestionsDraftCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Confirming questions draft for assignment {AssignmentId}", command.AssignmentId);

        var assignment = await repository.GetAsync(command.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.AssignmentId);

        if (assignment.QuestionsDraftJson is null)
            throw new InvalidQuestionsDraftException("No staged questions draft to confirm.");

        List<NewQuestionDto> drafted;
        try
        {
            drafted = JsonSerializer.Deserialize<List<NewQuestionDto>>(assignment.QuestionsDraftJson, JsonOptions)
                ?? throw new InvalidQuestionsDraftException("The staged questions draft is empty.");
        }
        catch (JsonException)
        {
            throw new InvalidQuestionsDraftException("The staged questions draft is corrupt.");
        }

        // Defensive validate: staging validates first, but a hand-written blob
        // must not bypass the question rules on the way in (FR-252).
        QuestionOptionDtoValidator.ValidateQuestions(drafted);

        // Confirm REPLACES all existing questions — mirror the update handler's
        // full-replacement question-sync block (snapshot -> remove -> re-add,
        // DisplayOrder re-indexed 0..n by list position, EC-7).
        var existingIds = assignment.Questions.Select(q => q.Id).ToList();
        foreach (var qid in existingIds)
        {
            assignment.RemoveQuestion(qid);
        }

        for (var i = 0; i < drafted.Count; i++)
        {
            var q = drafted[i];
            var question = assignment.AddQuestion(q.QuestionText, (Domain.QuestionType)q.QuestionType, i, q.ModelAnswer);
            if (q.Options is { Count: > 0 })
            {
                foreach (var opt in q.Options)
                {
                    question.AddOption(opt.OptionText, opt.IsCorrect);
                }
            }
        }

        assignment.ConfirmQuestionsDraft();

        await repository.UpdateAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);
        assignment.ClearDomainEvents();

        logger.LogInformation("Confirmed questions draft for assignment {AssignmentId}", command.AssignmentId);
    }
}
