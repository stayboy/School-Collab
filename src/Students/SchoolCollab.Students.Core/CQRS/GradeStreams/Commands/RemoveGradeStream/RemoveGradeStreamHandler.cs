using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;

namespace SchoolCollab.Students.Core.CQRS.GradeStreams.Commands.RemoveGradeStream;

/// <summary>
/// Removes the grade↔stream bridge row. The stream coded value itself is
/// untouched: it stays in the <c>GRSTREAMS</c> catalogue and its
/// <c>isDisabled</c> state is not modified — the grade simply stops offering it.
/// </summary>
public sealed class RemoveGradeStreamHandler(
    IGradeStreamAssignmentRepository repository,
    HybridCache cache,
    ILogger<RemoveGradeStreamHandler> logger) : ICommandHandler<RemoveGradeStream>
{
    public async Task HandleAsync(RemoveGradeStream command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling RemoveGradeStream {AssignmentId}", command.AssignmentId);

        var assignment = await repository.GetAsync(command.AssignmentId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Grade stream assignment with ID '{command.AssignmentId}' was not found.");

        // The route carries the grade as well; refuse a mismatched pair instead of
        // silently deleting another grade's row.
        if (assignment.GradeLevelId != command.GradeLevelId)
        {
            throw new InvalidOperationException(
                $"Grade stream assignment '{command.AssignmentId}' does not belong to grade level '{command.GradeLevelId}'.");
        }

        await repository.DeleteAsync(assignment, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation(
            "Stream {StreamCodedValueId} no longer offered by grade {GradeLevelId}",
            assignment.StreamCodedValueId, assignment.GradeLevelId);
    }
}
