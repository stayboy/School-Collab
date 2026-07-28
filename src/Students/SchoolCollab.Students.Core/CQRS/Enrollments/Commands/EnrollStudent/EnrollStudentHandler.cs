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
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Core.CQRS.Enrollments.Commands.EnrollStudent;

public sealed class EnrollStudentHandler(
    IStudentEnrollmentRepository repository,
    IActivePeriodProvider activePeriodProvider,
    IGradeLevelRepository gradeLevelRepository,
    ICodedValuesApiClient codedValuesApi,
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

        // FR-9: strand validation. If a GradeStrandCodedValueId is provided, the strand
        // must be a child of GRSTRNDS and its gradeLevel attribute must reference a
        // CodedValue that matches the enrollment's GradeLevel.
        if (command.GradeStrandCodedValueId is { } strandId)
        {
            await ValidateStrandAsync(command.GradeLevelId, strandId, cancellationToken);
        }

        var enrollment = StudentEnrollment.Create(
            command.StudentId,
            command.PeriodId,
            command.GradeLevelId,
            command.EnrolledOn,
            command.GradeStrandCodedValueId);

        await repository.AddAsync(enrollment, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        foreach (var evt in enrollment.DomainEvents.OfType<StudentEnrolledEvent>())
        {
            await publisher.EnqueueAsync(new StudentEnrolled(
                evt.StudentId,
                evt.PeriodId,
                evt.GradeLevelId,
                evt.GradeStrandCodedValueId,
                enrollment.EnrolledOn,
                DateTimeOffset.UtcNow), cancellationToken);
        }

        enrollment.ClearDomainEvents();

        logger.LogInformation("Student {StudentId} enrolled in period {PeriodId}", enrollment.StudentId, enrollment.PeriodId);
        return enrollment.Id;
    }

    private async Task ValidateStrandAsync(Guid gradeLevelId, Guid strandCodedValueId, CancellationToken cancellationToken)
    {
        // Resolve the grade's CodedValueId via the repository.
        var gradeLevel = await gradeLevelRepository.GetAsync(gradeLevelId, cancellationToken)
            ?? throw new GradeLevelNotFoundException(gradeLevelId);
        var gradeCodedValueId = gradeLevel.CodedValueId;

        // Fetch the strand coded value from the Settings API.
        var strand = await codedValuesApi.GetByIdAsync(strandCodedValueId, cancellationToken)
            ?? throw new GradeStrandGradeMismatchException(strandCodedValueId, gradeLevelId);

        // The strand's gradeLevel attribute must reference a CodedValue whose Id
        // matches the enrollment's grade's CodedValueId.
        var gradeLevelAttr = strand.Attributes
            .FirstOrDefault(a => a.Key == "gradeLevel");
        if (gradeLevelAttr is null)
        {
            throw new GradeStrandGradeMismatchException(strandCodedValueId, gradeLevelId);
        }

        // The attribute value is the coded value's GUID (because DataType=CodedValue).
        // We compare as Guid.
        if (!Guid.TryParse(gradeLevelAttr.Value, out var strandGradeCodedValueId)
            || strandGradeCodedValueId != gradeCodedValueId)
        {
            throw new GradeStrandGradeMismatchException(strandCodedValueId, gradeLevelId);
        }
    }
}