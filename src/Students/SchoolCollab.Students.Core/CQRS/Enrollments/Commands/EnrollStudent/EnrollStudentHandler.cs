using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Students.Core.Data;
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
    ICompositeEnrollmentSpecification enrollmentSpecification,
    ITenantProvider tenantProvider,
    StudentsDbContext db,
    IActorAccessor actorAccessor) : ICommandHandler<EnrollStudent, Guid>
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

        // Resolve the target grade from its GRADE coded value (the dialog now
        // submits the CodedValueId; the CodedValueId → GradeLevelId join is
        // server-side). A coded value without a GradeLevel row is materialized
        // here — same idempotent semantics the dialog's inline-create flow used,
        // now atomic with the enrollment itself.
        var gradeLevel = await gradeLevelRepository.GetByCodedValueIdAsync(command.GradeCodedValueId, cancellationToken);
        if (gradeLevel is null)
        {
            var cv = await codedValuesApi.GetByIdAsync(command.GradeCodedValueId, cancellationToken)
                ?? throw new GradeLevelNotFoundException(command.GradeCodedValueId);
            var candidate = GradeLevel.Create(cv.Id, cv.DisplayOrder, cv.Name, cv.DisplayOrder)
                .WithTenant(tenantProvider);
            // Race-safe insert: a concurrent enroll of the same brand-new coded
            // value can win the unique index (tenant_id, coded_value_id); we then
            // reuse the winner's row instead of failing the command with a raw
            // DbUpdateException (500).
            gradeLevel = await gradeLevelRepository.AddOrReuseAsync(candidate, cancellationToken);
            logger.LogInformation("Materialized GradeLevel {GradeLevelId} for CodedValueId {CodedValueId} during enrollment",
                gradeLevel.Id, command.GradeCodedValueId);
        }

        // Enrollment governance: a grade blocked for enrollment rejects the enrol
        // regardless of how the picker reached it (the client no longer filters).
        if (gradeLevel.IsBlockedFromEnrollment)
        {
            throw new GradeLevelEnrollmentBlockedException(gradeLevel.Id);
        }

        // FR-9: stream validation. If a StreamCodedValueId is provided, the stream
        // must be a child of GRSTREAMS and its gradeLevel attribute must reference a
        // CodedValue that matches the enrollment's GradeLevel. Runs for BOTH the
        // insert and the in-place-update path of the upsert below.
        if (command.StreamCodedValueId is { } streamId)
        {
            await ValidateStreamAsync(gradeLevel, streamId, cancellationToken);
        }

        // ── Upsert (fix for 23505 ix_student_enrollments_tenant_student_period) ──
        // Re-submitting the enroll command for a student who already has an ACTIVE
        // enrollment in the target period is a same-period grade/stream correction:
        // update that row in place instead of inserting a second one (which the
        // unique index rejects). No-op when grade+stream already match; otherwise
        // UpdateGrade keeps the enrollment Active, writes an audit entry, and
        // publishes StudentEnrollmentUpdated.
        var existing = await repository.GetActiveEnrollmentByStudentAndPeriodAsync(
            command.StudentId, command.PeriodId, cancellationToken);
        if (existing is not null)
        {
            return await UpdateExistingAsync(existing, gradeLevel, command, cancellationToken);
        }

        // §6 Enrollment validation guard clauses (age, gender, single-active).
        // Feature-flagged (FEATURE:EnableEnrollmentValidation, default off) for gradual
        // rollout. Only runs on the genuinely-new-enrollment path — an existing active
        // enrollment was already routed to the in-place update above (the single-active
        // rule must not reject correcting an existing enrollment).
        if (await featureFlagService.IsEnabledAsync(
                FeatureFlagKeys.EnableEnrollmentValidation, cancellationToken))
        {
            await ValidateEnrollmentAsync(command, gradeLevel, cancellationToken);
        }

        var enrollment = StudentEnrollment.Create(
            command.StudentId,
            command.PeriodId,
            gradeLevel.Id,
            command.EnrolledOn,
            command.StreamCodedValueId);

        // Race-safe insert: a concurrent enroll can win the unique index
        // (tenant_id, student_id, period_id) between our lookup above and this
        // insert; we then converge on the winner's row via the update semantics
        // instead of failing the command with a raw DbUpdateException (500).
        var persisted = await repository.AddOrReuseAsync(enrollment, cancellationToken);
        if (!ReferenceEquals(persisted, enrollment))
        {
            // Our insert never committed — publishing StudentEnrolled here would
            // announce an enrollment that doesn't exist. Convergence through
            // UpdateExistingAsync publishes the accurate StudentEnrollmentUpdated.
            logger.LogInformation(
                "Lost the enrollment-insert race for student {StudentId} / period {PeriodId}; converging on the winning row",
                command.StudentId, command.PeriodId);
            return await UpdateExistingAsync(persisted, gradeLevel, command, cancellationToken);
        }

        foreach (var evt in enrollment.DomainEvents.OfType<StudentEnrolledEvent>())
        {
            await publisher.EnqueueAsync(new StudentEnrolled(
                evt.StudentId,
                evt.PeriodId,
                evt.GradeLevelId,
                evt.StreamCodedValueId,
                enrollment.EnrolledOn,
                DateTimeOffset.UtcNow), cancellationToken);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);


        enrollment.ClearDomainEvents();

        logger.LogInformation("Student {StudentId} enrolled in period {PeriodId}", enrollment.StudentId, enrollment.PeriodId);
        return enrollment.Id;
    }

    /// <summary>
    /// Converges an existing ACTIVE enrollment onto the requested grade/stream —
    /// the update half of the Enroll-dialog upsert. Same grade+stream → idempotent
    /// no-op returning the existing id. Otherwise updates in place via
    /// <see cref="Domain.StudentEnrollment.UpdateGrade"/> (enrollment stays Active),
    /// audits the grade change in the same transaction, and publishes
    /// <see cref="Contracts.Events.StudentEnrollmentUpdated"/>.
    /// </summary>
    private async Task<Guid> UpdateExistingAsync(
        Domain.StudentEnrollment existing,
        Domain.GradeLevel gradeLevel,
        EnrollStudent command,
        CancellationToken cancellationToken)
    {
        if (existing.GradeLevelId == gradeLevel.Id && existing.StreamCodedValueId == command.StreamCodedValueId)
        {
            logger.LogInformation(
                "Enroll no-op: student {StudentId} is already actively enrolled in period {PeriodId} with the requested grade/stream",
                command.StudentId, command.PeriodId);
            return existing.Id;
        }

        var previousGradeLevelId = existing.GradeLevelId;
        existing.UpdateGrade(gradeLevel.Id, command.StreamCodedValueId);

        // Audit the grade correction in the same transaction as the enrollment
        // update (the repository's SaveChangesAsync flushes both tracked changes),
        // mirroring how TransferStudentHandler records its grade change.
        new StudentTransferAuditor(actorAccessor).Record(
            db,
            existing.TenantId,
            existing.StudentId,
            previousGradeLevelId,
            gradeLevel.Id,
            existing.PeriodId,
            "Grade updated via Enroll student dialog (same-period correction)");

        foreach (var evt in existing.DomainEvents.OfType<StudentEnrollmentUpdatedEvent>())
        {
            await publisher.EnqueueAsync(new StudentEnrollmentUpdated(
                evt.StudentId,
                evt.PeriodId,
                evt.PreviousGradeLevelId,
                evt.NewGradeLevelId,
                evt.NewStreamCodedValueId,
                DateTimeOffset.UtcNow), cancellationToken);
        }

        await repository.UpdateAsync(existing, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);


        existing.ClearDomainEvents();

        logger.LogInformation(
            "Student {StudentId}'s enrollment in period {PeriodId} updated from grade {FromGradeLevelId} to {ToGradeLevelId}",
            existing.StudentId, existing.PeriodId, previousGradeLevelId, gradeLevel.Id);
        return existing.Id;
    }

    /// <summary>
    /// Runs the enrollment validation specifications (plan §6). Each failing rule
    /// throws its typed domain exception with an actionable, UI-renderable message.
    /// </summary>
    private async Task ValidateEnrollmentAsync(
        EnrollStudent command, Domain.GradeLevel gradeLevel, CancellationToken cancellationToken)
    {
        var student = await studentRepository.GetAsync(command.StudentId, cancellationToken)
            ?? throw new StudentNotFoundException(command.StudentId);

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

    private async Task ValidateStreamAsync(Domain.GradeLevel gradeLevel, Guid streamCodedValueId, CancellationToken cancellationToken)
    {
        var gradeCodedValueId = gradeLevel.CodedValueId;

        // Fetch the stream coded value from the Settings API.
        var stream = await codedValuesApi.GetByIdAsync(streamCodedValueId, cancellationToken)
            ?? throw new StreamGradeMismatchException(streamCodedValueId, gradeLevel.Id);

        // The stream's gradeLevel attribute must reference a CodedValue whose Id
        // matches the enrollment's grade's CodedValueId.
        var gradeLevelAttr = stream.Attributes
            .FirstOrDefault(a => a.Key == "gradeLevel");
        if (gradeLevelAttr is null)
        {
            throw new StreamGradeMismatchException(streamCodedValueId, gradeLevel.Id);
        }

        // The attribute value is the coded value's GUID (because DataType=CodedValue).
        // We compare as Guid.
        if (!Guid.TryParse(gradeLevelAttr.Value, out var streamGradeCodedValueId)
            || streamGradeCodedValueId != gradeCodedValueId)
        {
            throw new StreamGradeMismatchException(streamCodedValueId, gradeLevel.Id);
        }
    }
}