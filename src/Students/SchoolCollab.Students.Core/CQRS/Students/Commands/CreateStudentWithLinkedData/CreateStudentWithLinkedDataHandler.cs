using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.EntityCodes;
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

namespace SchoolCollab.Students.Core.CQRS.Students.Commands.CreateStudentWithLinkedData;

/// <summary>
/// Atomically creates a student with its guardians, optional contacts, and (when an
/// enrollment target is supplied) its enrollment as a single unit of work. Everything
/// succeeds or fails together — no orphaned student, no partial guardian set, no
/// "student exists but not on the grade card" halfway state.
/// </summary>
public sealed class CreateStudentWithLinkedDataHandler(
    IUnitOfWork<StudentsDbContext> uow,
    IEntityCodeGenerator entityCodeGenerator,
    StudentsDbContext db,
    HybridCache cache,
    ITenantProvider tenantProvider,
    IActivePeriodProvider activePeriodProvider,
    IGradeLevelRepository gradeLevelRepository,
    ICodedValuesApiClient codedValuesApi,
    IFeatureFlagService featureFlagService,
    ICompositeEnrollmentSpecification enrollmentSpecification,
    IIntegrationEventPublisher publisher,
    ILogger<CreateStudentWithLinkedDataHandler> logger)
    : ICommandHandler<CreateStudentWithLinkedData, Guid>
{
    public async Task<Guid> HandleAsync(
        CreateStudentWithLinkedData command,
        CancellationToken cancellationToken = default)
    {
        // FR-4: no strict entity may be created with an empty tenant.
        tenantProvider.RequireTenantContext(nameof(CreateStudentWithLinkedData), typeof(Student));

        // Pre-validate reference ids + enrollment preconditions BEFORE tracking anything,
        // so a bad grade / guardian id or a closed period fails fast with a domain
        // exception (mapped to 4xx) rather than surfacing mid-transaction.
        await ValidateReferencesAsync(command, cancellationToken);
        Guid? enrollmentPeriodId = null;
        if (command.EnrollmentGradeLevelId is not null)
            enrollmentPeriodId = await ValidateEnrollmentTargetAsync(command, cancellationToken);

        // Capture integration events raised on the new entities so they can be enqueued
        // AFTER the commit. The outbox publisher persists each enqueue in its own
        // DbContext/transaction — it does NOT ride the UoW tx — so enqueuing inside the
        // action would leave a committed phantom event if the data tx later rolled back.
        var studentCreatedEvents = new List<StudentCreated>();
        var studentEnrolledEvents = new List<StudentEnrolled>();

        var studentId = await uow.ExecuteAsync(async (ctx, ct) =>
        {
            // Spec §4.5: auto-generate the student number before constructing the entity.
            var studentNumber = await entityCodeGenerator.GenerateAsync("STUDENT_CODE", ct);
            if (await ctx.Students.AnyAsync(s => s.StudentNumber == studentNumber, ct))
                throw new DuplicateStudentNumberException(studentNumber);

            var student = Student.Create(
                    studentNumber,
                    command.FirstName,
                    command.LastName,
                    command.DateOfBirth,
                    command.GenderCodedValueId,
                    command.TitleCodedValueId)
                .WithTenant(tenantProvider);
            ctx.Students.Add(student);

            // Guardians: an existing id is linked directly; otherwise a new guardian is
            // created (with its initial name-history snapshot) and then linked — all
            // inside the same transaction.
            foreach (var draft in command.Guardians ?? [])
            {
                var guardianId = draft.ExistingGuardianId is { } existingId
                    ? existingId
                    : AddNewGuardian(ctx, draft);

                ctx.StudentGuardians.Add(
                    StudentGuardian.Create(
                            student.Id,
                            guardianId,
                            draft.Role,
                            draft.RelationshipCodedValueId,
                            draft.IsEmergencyContact,
                            draft.ActingGuardianId)
                        .WithTenant(tenantProvider));
            }

            // Contacts (reserved shape — wired when the UI collects them).
            foreach (var c in command.Contacts ?? [])
            {
                ctx.Contacts.Add(
                    Contact.Create(ContactOwnerType.Student, student.Id, c.Channel, c.Value,
                            c.Label, c.CountryCode, c.DisplayOrder)
                        .WithTenant(tenantProvider));
            }

            // Enrollment (optional): only when an enrollment target is supplied.
            StudentEnrollment? enrollment = null;
            if (command.EnrollmentGradeLevelId is { } gradeId)
            {
                // Enrollment validation spec (feature-flagged, default off) — runs against
                // the freshly created student so the grade's age/gender gates apply. For a
                // brand-new student there are no prior active enrollments.
                if (await featureFlagService.IsEnabledAsync(
                        FeatureFlagKeys.EnableEnrollmentValidation, ct))
                {
                    var grade = await gradeLevelRepository.GetAsync(gradeId, ct)
                        ?? throw new GradeLevelNotFoundException(gradeId);
                    var enrollmentDate = command.EnrolledOn ?? DateOnly.FromDateTime(DateTime.UtcNow);
                    var context = new EnrollmentContext(
                        student, grade, enrollmentDate, Array.Empty<StudentEnrollment>());
                    if (!enrollmentSpecification.IsSatisfiedBy(context))
                        throw ResolveException(enrollmentSpecification, context);
                }

                enrollment = StudentEnrollment.Create(
                        student.Id,
                        enrollmentPeriodId!.Value,
                        gradeId,
                        command.EnrolledOn,
                        command.StreamCodedValueId)
                    .WithTenant(tenantProvider);
                ctx.StudentEnrollments.Add(enrollment);
            }

            // Capture integration events from the entities before they leave scope.
            studentCreatedEvents.AddRange(
                student.DomainEvents.OfType<StudentCreatedEvent>().Select(evt =>
                    new StudentCreated(student.Id, student.StudentNumber, student.FirstName,
                        student.LastName, student.CreatedAt)));
            if (enrollment is not null)
            {
                studentEnrolledEvents.AddRange(
                    enrollment.DomainEvents.OfType<StudentEnrolledEvent>().Select(evt =>
                        new StudentEnrolled(evt.StudentId, evt.PeriodId, evt.GradeLevelId,
                            evt.StreamCodedValueId, enrollment.EnrolledOn, DateTimeOffset.UtcNow)));
            }

            // Single commit — the unit of work commits only if this returns without throwing.
            await ctx.SaveChangesAsync(ct);

            logger.LogInformation(
                "Student {Id} created with number {StudentNumber} for tenant {TenantId} with {GuardianCount} guardian(s) and {Enrollment} enrollment",
                student.Id, student.StudentNumber, student.TenantId,
                command.Guardians?.Length ?? 0, enrollment is null ? 0 : 1);

            return student.Id;
        }, cancellationToken);

        // After the commit: invalidate cache + enqueue outbox events. Cache invalidation
        // is non-transactional; the enqueue MUST stay after the UoW returns so a data
        // rollback can never leave a committed phantom outbox event.
        await cache.RemoveByTagAsync("students", cancellationToken);
        await cache.RemoveByTagAsync("guardians", cancellationToken);
        await cache.RemoveByTagAsync("contacts", cancellationToken);

        foreach (var e in studentCreatedEvents)
            await publisher.EnqueueAsync(e, cancellationToken);
        foreach (var e in studentEnrolledEvents)
            await publisher.EnqueueAsync(e, cancellationToken);

        return studentId;
    }

    /// <summary>
    /// Pre-validates reference ids against the database (enrollment grade, existing
    /// guardians) and rejects within-batch duplicate guardian links. Runs BEFORE the
    /// unit of work so failures surface as fast 4xx with nothing written.
    /// </summary>
    private async Task ValidateReferencesAsync(
        CreateStudentWithLinkedData command,
        CancellationToken cancellationToken)
    {
        if (command.EnrollmentGradeLevelId is { } gradeId)
        {
            var gradeExists = await db.GradeLevels.AnyAsync(g => g.Id == gradeId, cancellationToken);
            if (!gradeExists)
                throw new GradeLevelNotFoundException(gradeId);
        }

        var existingGuardianIds = (command.Guardians ?? [])
            .Where(g => g.ExistingGuardianId is not null)
            .Select(g => g.ExistingGuardianId!.Value)
            .Distinct()
            .ToArray();
        if (existingGuardianIds.Length > 0)
        {
            var found = await db.Guardians
                .Where(g => existingGuardianIds.Contains(g.Id))
                .Select(g => g.Id)
                .ToArrayAsync(cancellationToken);
            var missing = existingGuardianIds.Except(found).ToArray();
            if (missing.Length > 0)
                throw new GuardianNotFoundException(missing[0]);
        }

        var duplicate = (command.Guardians ?? [])
            .Where(g => g.ExistingGuardianId is not null)
            .GroupBy(g => g.ExistingGuardianId!.Value)
            .FirstOrDefault(grp => grp.Count() > 1);
        if (duplicate is not null)
            throw new GuardianLinkAlreadyExistsException(Guid.Empty, duplicate.Key);
    }

    /// <summary>
    /// Validates the enrollment target's preconditions (active period, stream) that do
    /// not need the freshly created student. Returns the resolved period id.
    /// </summary>
    private async Task<Guid> ValidateEnrollmentTargetAsync(
        CreateStudentWithLinkedData command,
        CancellationToken cancellationToken)
    {
        // FR-A3: enrollment requires an Active (open) period for the current tenant.
        var active = await activePeriodProvider.GetActivePeriodAsync(cancellationToken);
        if (active is null)
        {
            throw new PeriodNotOpenException(
                "Cannot enrol students: no active period is open for this tenant. Open a period before enrolling.");
        }
        var periodId = command.EnrollmentPeriodId ?? active.Id;
        if (command.EnrollmentPeriodId is { } pid && pid != active.Id)
        {
            throw new PeriodNotOpenException(
                $"Enrollment targets period '{pid}' but the active period is '{active.Id}'. " +
                "Enrollments must target the tenant's active period.");
        }

        // FR-9: stream validation.
        if (command.StreamCodedValueId is { } streamId)
        {
            await ValidateStreamAsync(command.EnrollmentGradeLevelId!.Value, streamId, cancellationToken);
        }

        return periodId;
    }

    private Guid AddNewGuardian(StudentsDbContext ctx, GuardianDraft draft)
    {
        var guardian = Guardian.Create(
                draft.TitleCodedValueId,
                draft.FirstName!,
                draft.LastName!,
                displayName: null,
                address: null,
                communityId: null,
                draft.DateOfBirth,
                draft.GenderCodedValueId)
            .WithTenant(tenantProvider);

        // The initial name-history snapshot is a separate aggregate — add it explicitly
        // (the repository's PersistNameHistoryAsync is bypassed here).
        guardian.AddInitialNameHistory();
        ctx.Guardians.Add(guardian);
        foreach (var h in guardian.NameHistory)
            ctx.GuardianNameHistories.Add(h);

        return guardian.Id;
    }

    private async Task ValidateStreamAsync(
        Guid gradeLevelId,
        Guid streamCodedValueId,
        CancellationToken cancellationToken)
    {
        // Resolve the grade's CodedValueId via the repository.
        var gradeLevel = await gradeLevelRepository.GetAsync(gradeLevelId, cancellationToken)
            ?? throw new GradeLevelNotFoundException(gradeLevelId);
        var gradeCodedValueId = gradeLevel.CodedValueId;

        // Fetch the stream coded value from the Settings API.
        var stream = await codedValuesApi.GetByIdAsync(streamCodedValueId, cancellationToken)
            ?? throw new StreamGradeMismatchException(streamCodedValueId, gradeLevelId);

        var gradeLevelAttr = stream.Attributes
            .FirstOrDefault(a => a.Key == "gradeLevel");
        if (gradeLevelAttr is null)
        {
            throw new StreamGradeMismatchException(streamCodedValueId, gradeLevelId);
        }

        if (!Guid.TryParse(gradeLevelAttr.Value, out var streamGradeCodedValueId)
            || streamGradeCodedValueId != gradeCodedValueId)
        {
            throw new StreamGradeMismatchException(streamCodedValueId, gradeLevelId);
        }
    }

    /// <summary>
    /// Maps the composite gateway's first failing leaf rule to its typed domain
    /// exception (mirrors <c>EnrollStudentHandler</c>).
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
}
