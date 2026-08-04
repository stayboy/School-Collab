using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.SetGradeLevelEnrollmentBlocked;

public sealed class SetGradeLevelEnrollmentBlockedHandler(
    IGradeLevelRepository repository,
    HybridCache cache,
    ILogger<SetGradeLevelEnrollmentBlockedHandler> logger)
    : ICommandHandler<SetGradeLevelEnrollmentBlocked>
{
    public async Task HandleAsync(
        SetGradeLevelEnrollmentBlocked command,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling SetGradeLevelEnrollmentBlocked {Id} = {Blocked}", command.Id, command.Blocked);

        var gradeLevel = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new GradeLevelNotFoundException(command.Id);

        gradeLevel.SetEnrollmentBlocked(command.Blocked);

        try
        {
            await repository.UpdateAsync(gradeLevel, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("GradeLevel", gradeLevel.Id);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);

        gradeLevel.ClearDomainEvents();

        logger.LogInformation("GradeLevel {Id} enrollment-blocked set to {Blocked}", gradeLevel.Id, command.Blocked);
    }
}
