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
        // must not bypass the question rules on the way in (FR-252). The format decides whether the
        // drafted questions must define response kinds (Q1(ii)).
        QuestionOptionDtoValidator.ValidateQuestions(
            drafted, (GradingFormatDto)(int)assignment.GradingFormat);

        // Confirm REPLACES all existing questions — mirror the update handler's
        // full-replacement question-sync block (snapshot -> remove -> re-add,
        // DisplayOrder re-indexed 0..n by list position, EC-7).
        var existingIds = assignment.Questions.Select(q => q.Id).ToList();
        foreach (var qid in existingIds)
        {
            assignment.RemoveQuestion(qid);
        }

        // QR-5 (§5.6): the drafted questions' instruction rows, re-stamped with the ids this confirm
        // mints — the rows removed just above are gone for good, so anything omitted here is lost.
        // The assignment's OWN rows are untouched by a questions confirm, so these are appended.
        var questionInstructionItems = new List<(Guid? QuestionId, Domain.InstructionKind Kind, string? Text,
            string? Url, string? FileName, string? ContentType, long FileSize, string? StoragePath,
            string? Title)>();

        for (var i = 0; i < drafted.Count; i++)
        {
            var q = drafted[i];
            // R3 (D4/P1-2): the draft blob is where GenerationId is FIRST stamped by the UI, and this
            // re-mint is where it would otherwise be lost — the drafted rows are replaced wholesale.
            var question = assignment.AddQuestion(
                q.QuestionText,
                (Domain.QuestionType)q.QuestionType,
                i,
                q.ModelAnswer,
                q.GenerationId,
                // D16/QR-2: the drafted kinds ride the re-mint for the same reason.
                q.ResponseKinds?.Select(kind => (Domain.ResponseKind)(int)kind).ToList());
            if (q.Options is { Count: > 0 })
            {
                foreach (var opt in q.Options)
                {
                    question.AddOption(opt.OptionText, opt.IsCorrect);
                }
            }

            // QR-5: the drafted question's instruction rows, stamped with the freshly minted id.
            questionInstructionItems.AddRange((q.Instructions ?? []).Select(item => (
                QuestionId: (Guid?)question.Id,
                Kind: (Domain.InstructionKind)(int)item.Kind,
                Text: item.Text,
                Url: item.Url,
                FileName: item.FileName,
                ContentType: item.ContentType,
                FileSize: item.FileSize,
                StoragePath: item.StoragePath,
                Title: item.Title)));
        }

        if (questionInstructionItems.Count > 0)
        {
            assignment.AddInstructionItems(questionInstructionItems);
        }

        assignment.ConfirmQuestionsDraft();

        await repository.UpdateAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);
        assignment.ClearDomainEvents();

        logger.LogInformation("Confirmed questions draft for assignment {AssignmentId}", command.AssignmentId);
    }
}
