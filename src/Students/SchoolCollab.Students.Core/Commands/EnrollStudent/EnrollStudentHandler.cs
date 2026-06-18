using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.Messaging;

namespace SchoolCollab.Students.Core.Commands.EnrollStudent;

public sealed class EnrollStudentHandler(
    IStudentEnrollmentRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ILogger<EnrollStudentHandler> logger) : ICommandHandler<EnrollStudent, Guid>
{
    public async Task<Guid> HandleAsync(EnrollStudent command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling EnrollStudent {StudentId} in period {PeriodId}", command.StudentId, command.PeriodId);

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