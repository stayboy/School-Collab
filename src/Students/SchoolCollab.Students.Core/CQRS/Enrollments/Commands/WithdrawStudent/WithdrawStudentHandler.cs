using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Students.Core.CQRS.Enrollments.Commands.WithdrawStudent;

public sealed class WithdrawStudentHandler(
    IStudentEnrollmentRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ILogger<WithdrawStudentHandler> logger) : ICommandHandler<WithdrawStudent>
{
    public async Task HandleAsync(WithdrawStudent command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling WithdrawStudent {EnrollmentId}", command.EnrollmentId);

        var enrollment = await repository.GetAsync(command.EnrollmentId, cancellationToken)
            ?? throw new InvalidOperationException($"Enrollment with ID '{command.EnrollmentId}' not found.");

        enrollment.Withdraw(command.ExitDate, command.Reason);

        foreach (var evt in enrollment.DomainEvents.OfType<StudentWithdrawnEvent>())
        {
            await publisher.EnqueueAsync(new StudentWithdrawn(
                evt.StudentId,
                evt.PeriodId,
                DateTimeOffset.UtcNow), cancellationToken);
        }

        try
        {
            await repository.UpdateAsync(enrollment, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("StudentEnrollment", enrollment.Id);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);


        enrollment.ClearDomainEvents();

        logger.LogInformation("Student {StudentId} withdrawn from period {PeriodId}", enrollment.StudentId, enrollment.PeriodId);
    }
}