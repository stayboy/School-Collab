namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.QuestionsDraft;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.CQRS;

/// <summary>
/// WS-B2 — handler for <see cref="GetQuestionsDraftQuery"/>. Returns the staged
/// draft as <c>IReadOnlyList&lt;NewQuestionDto&gt;</c>, null when none is staged.
/// A corrupt blob throws the typed <see cref="InvalidQuestionsDraftException"/>
/// so the draft surface never renders a half-parseable state.
/// </summary>
public sealed class GetQuestionsDraftQueryHandler(
    IAssignmentRepository repository,
    ILogger<GetQuestionsDraftQueryHandler> logger) : IQueryHandler<GetQuestionsDraftQuery, IReadOnlyList<NewQuestionDto>?>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<NewQuestionDto>?> HandleAsync(
        GetQuestionsDraftQuery query,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Reading questions draft for assignment {AssignmentId}", query.AssignmentId);

        var assignment = await repository.GetAsync(query.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(query.AssignmentId);

        if (assignment.QuestionsDraftJson is null)
            return null;

        try
        {
            var parsed = JsonSerializer.Deserialize<List<NewQuestionDto>>(assignment.QuestionsDraftJson, JsonOptions)
                ?? throw new InvalidQuestionsDraftException("The staged questions draft is empty.");
            return parsed;
        }
        catch (JsonException)
        {
            throw new InvalidQuestionsDraftException("The staged questions draft is corrupt.");
        }
    }
}
