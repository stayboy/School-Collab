using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;

namespace SchoolCollab.Students.Core.CQRS.GradeSubjectAssignments.Commands.RemoveGradeSubject;

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

        // Grade↔topic assignments span multiple years, so we block/archive by
        // ending the effective period rather than hard-deleting the row. This
        // keeps the audit trail and any historical references intact.
        await repository.EndAsync(assignment, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation("GradeSubjectAssignment {Id} ended (blocked/archived)", assignment.Id);
    }
}