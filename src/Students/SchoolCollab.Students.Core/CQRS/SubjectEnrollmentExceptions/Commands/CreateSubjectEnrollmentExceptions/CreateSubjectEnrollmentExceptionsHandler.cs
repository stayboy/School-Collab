using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.TopicAssignments;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.SubjectEnrollmentExceptions.Commands.CreateSubjectEnrollmentExceptions;

/// <summary>
/// Bulk-creates subject enrollment exceptions — one ROW PER ITEM, all in ONE transaction
/// (subject-period-exception-model.md v6 §11.3, decision 18).
///
/// <para><b>One request, many rows.</b> The bulk-ness lives in the COMMAND, not in the row shape:
/// several sequences are several exceptions, because each covers its own span (§2.4). Storing them
/// in single row would make the duplicate key <c>(owner, topic, division, span)</c> ambiguous and
/// turn the availability predicate into a containment test. The whole batch is added through one
/// <see cref="ISubjectEnrollmentExceptionRepository.AddRangeAsync"/> call, which saves once, so the
/// write is all-or-nothing: a rejection anywhere leaves the table untouched.</para>
///
/// <para><b>Which rules run once, and which run per item.</b> The owner (grade XOR activity group),
/// the topic and the division belong to the action, so they are validated once. The span SHAPE
/// (at least one bound, End ≥ Start) and the ORDINAL are per-sequence rules and run for every item,
/// in the same order as the single-create handler so error precedence cannot differ between the two
/// entry points. An undefined division is a 422, an unknown owner or topic a 404, and a duplicate
/// <c>(owner, topic, division, span)</c> a 409 — the same vocabulary the single path uses.</para>
///
/// <para><b>Duplicates.</b> Two checks, because either alone is insufficient. The <i>intra-batch</i>
/// check is needed because the database pre-check cannot see rows this transaction has not saved
/// yet — two items resolving to the same span would both pass it and then collide on the unique
/// index as a raw <c>23505</c>. The <i>database</i> check then catches a span that already exists.
/// The ordinal is deliberately NOT part of either check: it is descriptive, and two sequences that
/// cover the same span are the same row whatever positions they claim (§0 decision 15).</para>
/// </summary>
public sealed class CreateSubjectEnrollmentExceptionsHandler(
    ISubjectEnrollmentExceptionRepository repository,
    IGradeLevelRepository gradeLevelRepository,
    IActivityGroupRepository activityGroupRepository,
    ITopicRepository topicRepository,
    ITenantProvider tenantProvider,
    HybridCache cache,
    ILogger<CreateSubjectEnrollmentExceptionsHandler> logger)
    : ICommandHandler<CreateSubjectEnrollmentExceptions, Guid[]>
{
    public async Task<Guid[]> HandleAsync(
        CreateSubjectEnrollmentExceptions command,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug(
            "Handling CreateSubjectEnrollmentExceptions for grade {GradeLevelId} / group {ActivityGroupId}, topic {TopicId}, division {Division}, {Count} item(s)",
            command.GradeLevelId, command.ActivityGroupId, command.TopicId, command.Division, command.Items.Count);

        // FR-4: no strict entity may be created with an empty tenant.
        var tenantId = tenantProvider.RequireTenantContext(
            nameof(CreateSubjectEnrollmentExceptions), typeof(SubjectEnrollmentException));

        var hasGrade = command.GradeLevelId is not null;
        var hasGroup = command.ActivityGroupId is not null;
        if (hasGrade == hasGroup)
        {
            throw new TopicAssignmentPeriodException(
                "A subject enrollment exception must target exactly one owner: either a grade level or an activity group.");
        }

        // A batch with nothing in it would "succeed" while writing nothing, which reads as a
        // successful write to every caller. It is a caller error, so it is a 422 like the rest.
        if (command.Items.Count == 0)
        {
            throw new TopicAssignmentPeriodException(
                "A bulk subject enrollment exception write needs at least one item.");
        }

        // §2.4: the int-backed enum accepts any integer, so an undefined value must be rejected at
        // the boundary rather than stored verbatim — a 422. Once: the division is per action.
        TopicAssignmentPeriodValidator.ValidateExceptionDivision(command.Division);

        // Per item — the two rules that are genuinely per-sequence. Nothing cross-checks the ordinal
        // against the span: the two are ALLOWED to disagree (see the validator).
        foreach (var item in command.Items)
        {
            TopicAssignmentPeriodValidator.ValidateExceptionShape(item.StartDate, item.EndDate);
            TopicAssignmentPeriodValidator.ValidateExceptionOrdinal(command.Division, item.Ordinal);
        }

        if (hasGrade)
        {
            var gradeLevelId = command.GradeLevelId!.Value;
            if (await gradeLevelRepository.GetAsync(gradeLevelId, cancellationToken) is null)
                throw new GradeLevelNotFoundException(gradeLevelId);

            // FR-57: a grade has no window and no period reference, so any Division and any span
            // is accepted. There is deliberately no rule here.
        }
        else
        {
            // Per item: this rule is span-dependent — it checks the group's own enrollment window
            // against the dates being written, so each sequence must be checked against it.
            foreach (var item in command.Items)
            {
                await TopicAssignmentPeriodValidator.ValidateGroupExceptionDivisionAsync(
                    command.ActivityGroupId!.Value, command.Division, item.StartDate, item.EndDate,
                    activityGroupRepository, cancellationToken);
            }
        }

        // The topic must exist in this tenant: without this the unknown id reaches the FK and
        // surfaces as an unhandled DbUpdateException (500). GetAsync runs through the global
        // query filters, so it is tenant-scoped; a Guid.Empty id is null here too.
        if (await topicRepository.GetAsync(command.TopicId, cancellationToken) is null)
            throw new TopicNotFoundException(command.TopicId);

        // Intra-batch duplicates first: the database check below cannot see this transaction's own
        // un-saved rows, so without this the collision would surface as a raw 23505 from the unique
        // index instead of the 409 the caller can act on. The key mirrors the COALESCE expression
        // index — an open bound collapses onto a sentinel — so the two agree on what "same span" is.
        var spansInBatch = new HashSet<(DateOnly Start, DateOnly End)>();
        foreach (var item in command.Items)
        {
            var key = (item.StartDate ?? DateOnly.MinValue, item.EndDate ?? DateOnly.MaxValue);
            if (!spansInBatch.Add(key))
            {
                throw new DuplicateSubjectEnrollmentException(
                    command.GradeLevelId, command.ActivityGroupId, command.TopicId,
                    command.Division, item.StartDate, item.EndDate);
            }
        }

        foreach (var item in command.Items)
        {
            if (await repository.ExistsAsync(
                    command.GradeLevelId, command.ActivityGroupId, command.TopicId,
                    command.Division, item.StartDate, item.EndDate, cancellationToken))
            {
                throw new DuplicateSubjectEnrollmentException(
                    command.GradeLevelId, command.ActivityGroupId, command.TopicId,
                    command.Division, item.StartDate, item.EndDate);
            }
        }

        var exceptions = command.Items
            .Select(item => SubjectEnrollmentException.Create(
                tenantId,
                command.GradeLevelId,
                command.ActivityGroupId,
                command.TopicId,
                command.Division,
                item.StartDate,
                item.EndDate,
                command.Reason,
                ordinal: item.Ordinal))
            .ToArray();

        // The single save is what makes the batch atomic — every row or none.
        await repository.AddRangeAsync(exceptions, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation(
            "Created {Count} SubjectEnrollmentException row(s) (topic {TopicId}, division {Division})",
            exceptions.Length, command.TopicId, command.Division);

        return exceptions.Select(e => e.Id).ToArray();
    }
}
