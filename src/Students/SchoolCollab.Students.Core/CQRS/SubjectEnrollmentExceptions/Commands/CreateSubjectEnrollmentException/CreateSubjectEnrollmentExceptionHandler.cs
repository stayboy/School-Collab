using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.TopicAssignments;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.SubjectEnrollmentExceptions.Commands.CreateSubjectEnrollmentException;

/// <summary>
/// Creates a subject enrollment exception (subject-period-exception-model.md v3 §4.2/§6).
/// Validates the owner shape, the division enum membership, the span invariants (≥1 bound,
/// End ≥ Start), the existence of the referenced owner and topic, and — for a
/// group-owned exception — the FR-56 division matrix through
/// <see cref="TopicAssignmentPeriodValidator"/> (those rules live beside the FR-56/57
/// bridge rules they mirror). An unknown grade/group/topic is a 404 instead of the
/// unhandled <c>DbUpdateException</c> the foreign keys would raise; an undefined
/// division is a 422. A duplicate <c>(owner, topic, division, span)</c> is a
/// 409 rather than a raw <c>23505</c> from the expression index.
///
/// <para>Nothing here consults a period: a grade-owned exception is unconstrained by
/// FR-57 (§4.2), and there is no retired-period rule to apply (§2.2).</para>
/// </summary>
public sealed class CreateSubjectEnrollmentExceptionHandler(
    ISubjectEnrollmentExceptionRepository repository,
    IGradeLevelRepository gradeLevelRepository,
    IActivityGroupRepository activityGroupRepository,
    ITopicRepository topicRepository,
    ITenantProvider tenantProvider,
    HybridCache cache,
    ILogger<CreateSubjectEnrollmentExceptionHandler> logger) : ICommandHandler<CreateSubjectEnrollmentException, Guid>
{
    public async Task<Guid> HandleAsync(CreateSubjectEnrollmentException command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug(
            "Handling CreateSubjectEnrollmentException for grade {GradeLevelId} / group {ActivityGroupId}, topic {TopicId}, division {Division}",
            command.GradeLevelId, command.ActivityGroupId, command.TopicId, command.Division);

        // FR-4: no strict entity may be created with an empty tenant.
        var tenantId = tenantProvider.RequireTenantContext(nameof(CreateSubjectEnrollmentException), typeof(SubjectEnrollmentException));

        var hasGrade = command.GradeLevelId is not null;
        var hasGroup = command.ActivityGroupId is not null;
        if (hasGrade == hasGroup)
        {
            throw new TopicAssignmentPeriodException(
                "A subject enrollment exception must target exactly one owner: either a grade level or an activity group.");
        }

        // §2.2 shape invariants — a 422 for the caller, not an unhandled argument error.
        TopicAssignmentPeriodValidator.ValidateExceptionShape(command.StartDate, command.EndDate);

        // §2.4: the int-backed enum accepts any integer, so an undefined value (e.g. 42)
        // must be rejected at the boundary rather than stored verbatim — a 422.
        TopicAssignmentPeriodValidator.ValidateExceptionDivision(command.Division);

        // The ordinal's own invariants (v5 §0 decision 15): 1-based, and only on a real
        // part — a free window has no "3rd". Both are 422s at the boundary, so an invalid
        // ordinal never reaches the entity's ArgumentException. Nothing here cross-checks
        // the ordinal against the span: the two are ALLOWED to disagree (see the validator).
        TopicAssignmentPeriodValidator.ValidateExceptionOrdinal(command.Division, command.Ordinal);

        if (hasGrade)
        {
            var gradeLevelId = command.GradeLevelId!.Value;
            if (await gradeLevelRepository.GetAsync(gradeLevelId, cancellationToken) is null)
                throw new GradeLevelNotFoundException(gradeLevelId);

            // FR-57: a grade has no window and no period reference, so any Division and
            // any span is accepted. There is deliberately no rule here.
        }
        else
        {
            await TopicAssignmentPeriodValidator.ValidateGroupExceptionDivisionAsync(
                command.ActivityGroupId!.Value, command.Division, command.StartDate, command.EndDate,
                activityGroupRepository, cancellationToken);
        }

        // The topic must exist in this tenant: without this the unknown id reaches the FK and
        // surfaces as an unhandled DbUpdateException (500). GetAsync runs through the global
        // query filters, so it is tenant-scoped; a Guid.Empty id is null here too.
        if (await topicRepository.GetAsync(command.TopicId, cancellationToken) is null)
            throw new TopicNotFoundException(command.TopicId);

        if (await repository.ExistsAsync(
                command.GradeLevelId, command.ActivityGroupId, command.TopicId,
                command.Division, command.StartDate, command.EndDate, cancellationToken))
        {
            throw new DuplicateSubjectEnrollmentException(
                command.GradeLevelId, command.ActivityGroupId, command.TopicId,
                command.Division, command.StartDate, command.EndDate);
        }

        var exception = SubjectEnrollmentException.Create(
            tenantId,
            command.GradeLevelId,
            command.ActivityGroupId,
            command.TopicId,
            command.Division,
            command.StartDate,
            command.EndDate,
            command.Reason,
            ordinal: command.Ordinal);

        await repository.AddAsync(exception, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation(
            "SubjectEnrollmentException {Id} created (topic {TopicId}, division {Division})",
            exception.Id, exception.TopicId, exception.Division);

        return exception.Id;
    }
}
