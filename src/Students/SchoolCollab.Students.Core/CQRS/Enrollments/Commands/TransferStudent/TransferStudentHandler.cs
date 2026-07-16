using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Services;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Students.Core.CQRS.Enrollments.Commands.TransferStudent;

public sealed class TransferStudentHandler(
    IStudentEnrollmentRepository repository,
    StudentsDbContext db,
    IActorAccessor actorAccessor,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ILogger<TransferStudentHandler> logger) : ICommandHandler<TransferStudent>
{
    public async Task HandleAsync(TransferStudent command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling TransferStudent {EnrollmentId}", command.EnrollmentId);

        var enrollment = await repository.GetAsync(command.EnrollmentId, cancellationToken)
            ?? throw new InvalidOperationException($"Enrollment with ID '{command.EnrollmentId}' not found.");

        var fromGradeLevelId = enrollment.GradeLevelId;
        enrollment.Transfer(command.NewGradeLevelId, command.TransferDate, command.Reason);

        // Audit the transfer in the same transaction as the enrollment update
        // (the repository's SaveChangesAsync flushes both tracked changes).
        new StudentTransferAuditor(actorAccessor).Record(
            db,
            enrollment.TenantId,
            enrollment.StudentId,
            fromGradeLevelId,
            command.NewGradeLevelId,
            enrollment.PeriodId,
            command.Reason);

        try
        {
            await repository.UpdateAsync(enrollment, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("StudentEnrollment", enrollment.Id);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);

        foreach (var evt in enrollment.DomainEvents.OfType<StudentTransferredEvent>())
        {
            await publisher.EnqueueAsync(new StudentTransferred(
                enrollment.StudentId,
                enrollment.PeriodId,
                fromGradeLevelId,
                evt.NewGradeLevelId,
                DateTimeOffset.UtcNow), cancellationToken);
        }

        enrollment.ClearDomainEvents();

        logger.LogInformation("Student {StudentId} transferred from grade {FromGrade} to {ToGrade}", enrollment.StudentId, fromGradeLevelId, command.NewGradeLevelId);
    }
}