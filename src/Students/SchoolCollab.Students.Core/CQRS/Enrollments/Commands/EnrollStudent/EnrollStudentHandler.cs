using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.Domain.Specifications;
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Core.CQRS.Enrollments.Commands.EnrollStudent;

public sealed class EnrollStudentHandler(
    IStudentEnrollmentRepository repository,
    IActivePeriodProvider activePeriodProvider,
    IGradeLevelRepository gradeLevelRepository,
    ICodedValuesApiClient codedValuesApi,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ILogger<EnrollStudentHandler> logger,
    IFeatureFlagService featureFlagService,
    IStudentRepository studentRepository,
    ICompositeEnrollmentSpecification enrollmentSpecification) : ICommandHandler<EnrollStudent, Guid>
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

        // §6 Enrollment validation guard clauses (age, gender, single-active).
        // Feature-flagged (FEATURE:EnableEnrollmentValidation, default off) for gradual
        // rollout. Existing active enrollments are grandfathered: validation runs only
        // for *new* enrollments and only while the flag is on.
        if (await featureFlagService.IsEnabledAsync(
                FeatureFlagKeys.EnableEnrollmentValidation, cancellationToken))
        {
            await ValidateEnrollmentAsync(command, cancellationToken);
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

    /// <summary>
    /// Runs the enrollment validation specifications (plan §6). Each failing rule
    /// throws its typed domain exception with an actionable, UI-renderable message.
    /// </summary>
    private async Task ValidateEnrollmentAsync(EnrollStudent command, CancellationToken cancellationToken)
    {
        var student = await studentRepository.GetAsync(command.StudentId, cancellationToken)
            ?? throw new StudentNotFoundException(command.StudentId);

        var gradeLevel = await gradeLevelRepository.GetAsync(command.GradeLevelId, cancellationToken)
            ?? throw new GradeLevelNotFoundException(command.GradeLevelId);

        // Cross-period: any active enrollment for this student blocks a new one.
        var existing = await repository.GetActiveEnrollmentsByStudentAsync(command.StudentId, cancellationToken);

        var enrollmentDate = command.EnrolledOn ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var context = new EnrollmentContext(student, gradeLevel, enrollmentDate, existing);

        // Evaluate the composite gateway (age → gender → single-active, in DI
        // registration order). On failure, map the failing leaf rule to its typed
        // domain exception so the UI renders an actionable, specific message.
        // Exception construction stays in the handler; specs stay side-effect-free
        // apart from IEnrollmentSpecification.FailureMessage.
        if (!enrollmentSpecification.IsSatisfiedBy(context))
        {
            throw ResolveException(enrollmentSpecification, context);
        }
    }

    /// <summary>
    /// Maps the composite gateway's first failing leaf rule to its typed domain
    /// exception. Keeps exception construction in the handler (specs do not build
    /// exceptions) while preserving a single, swappable validation dependency.
    /// </summary>
    private static Exception ResolveException(
        ICompositeEnrollmentSpecification enrollmentSpecification, EnrollmentContext context)
    {
        return enrollmentSpecification.FailingSpecification switch
        {
            AgeRangeSpecification => new StudentAgeViolationException(
                context.Student.Id,
                context.GradeLevel.Id,
                AgeRangeSpecification.ComputeAge(context.Student.DateOfBirth!.Value, context.EnrollmentDate),
                context.GradeLevel.MinAge,
                context.GradeLevel.MaxAge,
                context.Student.DateOfBirth!.Value,
                context.EnrollmentDate),
            GenderRestrictionSpecification => new StudentGenderViolationException(
                context.Student.Id,
                context.GradeLevel.Id,
                context.GradeLevel.AllowedGenderCodedValueId,
                context.Student.GenderCodedValueId),
            SingleActiveEnrollmentSpecification => new MultipleActiveEnrollmentsException(
                context.Student.Id,
                context.GradeLevel.Id,
                context.ExistingActiveEnrollments.Select(e => e.Id).ToArray()),
            _ => new InvalidOperationException(
                $"Unhandled enrollment specification failure: {enrollmentSpecification.FailureMessage}")
        };
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