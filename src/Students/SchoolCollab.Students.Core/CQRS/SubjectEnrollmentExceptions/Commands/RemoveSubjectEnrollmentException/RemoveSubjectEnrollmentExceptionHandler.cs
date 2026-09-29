using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;

namespace SchoolCollab.Students.Core.CQRS.SubjectEnrollmentExceptions.Commands.RemoveSubjectEnrollmentException;

/// <summary>
/// Soft-deletes a subject enrollment exception (subject-period-exception-model.md v3
/// §2.2): an exception is immutable history, so removing it deletes the row (through
/// the soft-delete filter, which also keeps remove-then-re-add legal) and keeps the
/// audit trail. Idempotent — an unknown or already-removed id is a silent no-op (the
/// endpoint answers 204 either way).
/// </summary>
public sealed class RemoveSubjectEnrollmentExceptionHandler(
    ISubjectEnrollmentExceptionRepository repository,
    HybridCache cache,
    ILogger<RemoveSubjectEnrollmentExceptionHandler> logger) : ICommandHandler<RemoveSubjectEnrollmentException>
{
    public async Task HandleAsync(RemoveSubjectEnrollmentException command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling RemoveSubjectEnrollmentException {Id}", command.Id);

        var exception = await repository.GetAsync(command.Id, cancellationToken);
        if (exception is null)
        {
            logger.LogInformation("SubjectEnrollmentException {Id} not found — remove is idempotent", command.Id);
            return;
        }

        await repository.SoftDeleteAsync(exception, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation("SubjectEnrollmentException {Id} removed", exception.Id);
    }
}
