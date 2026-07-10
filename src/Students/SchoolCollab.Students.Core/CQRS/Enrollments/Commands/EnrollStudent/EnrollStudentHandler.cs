using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Enrollments.Commands.EnrollStudent;

public sealed class EnrollStudentHandler(
    IStudentEnrollmentRepository repository,
    IActivePeriodProvider activePeriodProvider,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ILogger<EnrollStudentHandler> logger) : ICommandHandler<EnrollStudent, Guid>
{
    public async Task<Guid> HandleAsync(EnrollStudent command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling EnrollStudent {StudentId} in period {PeriodId}", command.StudentId, command.PeriodId);

        // FR-A3: enrollment requires an Active (open) period for the current tenant.
        var active = await activePeriodProvider.GetActivePeriodAsync(cancellationToken);
        if (active is null)
        {
            throw new PeriodNotOpenException(
                "Cannot enrol students: no active period is open for this tenant. Open a period before enrolling.");
        }
        if (command.PeriodId != active.Id)
        {
            throw new PeriodNotOpenException(
                $"Enrollment targets period '{command.PeriodId}' but the active period is '{active.Id}'. " +
                "Enrollments must target the tenant's active period.");
        }

        var enrollment = StudentEnrollment.Create(
            command.StudentId,
            command.PeriodId,
            command.GradeLevelId,
            command.EnrolledOn);

        await repository.AddAsync(enrollment, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        foreach (var evt in enrollment.DomainEvents.OfType<StudentEnrolledEvent>())
        {
            await publisher.EnqueueAsync(new StudentEnrolled(
                evt.StudentId,
                evt.PeriodId,
                evt.GradeLevelId,
                enrollment.EnrolledOn,
                DateTimeOffset.UtcNow), cancellationToken);
        }

        enrollment.ClearDomainEvents();

        logger.LogInformation("Student {StudentId} enrolled in period {PeriodId}", enrollment.StudentId, enrollment.PeriodId);
        return enrollment.Id;
    }
}
