using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentAudience;

/// <summary>
/// The published-to read (spec §4.2, Q130/Q145/Q146): one row per student of the assignment's
/// resolved cohort, with the persisted recipients as its contacts and the notification log as the
/// delivery state. <c>Reached = false</c> rows are the point of the read — they are what the
/// per-recipient table could never show. Tenant-scoped by the context's global query filter, so a
/// foreign assignment reads as absent (<see cref="AssignmentNotFoundException"/> → 404).
/// </summary>
public sealed record GetAssignmentAudience(Guid AssignmentId) : IQuery<AssignmentAudienceDto>;

/// <summary>Handles <see cref="GetAssignmentAudience"/>.</summary>
/// <remarks>
/// <para><b>Delivery authority (Q145).</b> The notification log decides the state, the attempt and
/// the failure reason — it is the record the renderer writes. <see cref="AssignmentRecipient.OpenedAt"/>
/// supplies <i>opened</i> only: the two are read side by side and the log never overwrites the
/// recipient's own opened stamp, nor the reverse. A recipient with no publish log row answers
/// <see cref="AudienceDeliveryStateDto.Unknown"/>, never a fabricated <c>Sent</c>.</para>
/// <para><b>Degradation is split, never collapsed.</b> A directory (name) failure leaves the rows
/// intact with an empty name and delivery untouched; a log failure leaves the roster and the
/// contacts intact with <see cref="AudienceDeliveryStateDto.Unknown"/>. Neither empties the panel.</para>
/// </remarks>
public sealed class GetAssignmentAudienceHandler(
    AssignmentsDbContext db,
    IAssignmentTargetResolver targetResolver,
    IContactResolver contactResolver,
    ILogger<GetAssignmentAudienceHandler> logger)
    : IQueryHandler<GetAssignmentAudience, AssignmentAudienceDto>
{
    /// <inheritdoc />
    public async Task<AssignmentAudienceDto> HandleAsync(
        GetAssignmentAudience query,
        CancellationToken cancellationToken = default)
    {
        var assignment = await db.Assignments
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == query.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(query.AssignmentId);

        // The persisted recipients are the contact rows publish wrote — the audience read never
        // re-resolves contacts, so a row it shows is a row that was really queued.
        var recipients = await db.AssignmentRecipients
            .AsNoTracking()
            .Where(r => r.AssignmentId == query.AssignmentId)
            .ToListAsync(cancellationToken);

        var recipientsByStudent = recipients
            .Where(r => r.WardStudentId.HasValue)
            .ToLookup(r => r.WardStudentId!.Value);

        var (cohort, cohortDegraded) = await ResolveCohortAsync(assignment, recipientsByStudent, cancellationToken);
        var (names, namesDegraded) = await ResolveStudentNamesAsync(assignment, cohort, cancellationToken);
        var degraded = cohortDegraded || namesDegraded;

        // A log read failure degrades DELIVERY only: the rows keep their roster and contacts.
        IReadOnlyDictionary<Guid, NotificationLog>? publishLogs;
        try
        {
            publishLogs = await ReadPublishLogsAsync(query.AssignmentId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Notification log read failed for assignment {AssignmentId}; reporting every delivery state as Unknown",
                query.AssignmentId);
            degraded = true;
            publishLogs = null;
        }

        var ordered = cohort
            .OrderBy(studentId => names.TryGetValue(studentId, out var name) ? name : string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(studentId => studentId)
            .ToArray();

        var rows = ordered
            .Take(AudienceLimits.AudienceCap)
            .Select(studentId =>
            {
                var contacts = recipientsByStudent[studentId]
                    .OrderBy(recipient => (int)recipient.Channel)
                    .ThenBy(recipient => (int)recipient.OwnerType)
                    .Select(recipient => ProjectContact(recipient, publishLogs))
                    .ToList();

                return new AudienceStudentDto(
                    studentId,
                    names.TryGetValue(studentId, out var name) ? name : string.Empty,
                    contacts.Exists(contact => contact.SubscriptionActive),
                    contacts);
            })
            .ToList();

        return new AssignmentAudienceDto(rows, ordered.Length > AudienceLimits.AudienceCap, degraded);
    }

    /// <summary>
    /// The student cohort, from the same resolver publish uses. A resolver failure degrades to the
    /// persisted recipients' wards — the cohort that really was published — rather than an empty
    /// list, because those rows are exactly what the caller came to read.
    /// </summary>
    private async Task<(Guid[] Cohort, bool Degraded)> ResolveCohortAsync(
        Assignment assignment,
        ILookup<Guid, AssignmentRecipient> recipientsByStudent,
        CancellationToken cancellationToken)
    {
        var targets = assignment.Targets;
        if (targets.Count == 0)
        {
            return ([], false);
        }

        try
        {
            var resolved = await targetResolver.ResolveStudentIdsAsync(
                targets.Select(target => new TargetConstraint(target.Kind, target.RefId)).ToArray(),
                targets.Any(target => target.Kind == TargetKind.AllStudents),
                cancellationToken);
            return (resolved, false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Target resolution failed for assignment {AssignmentId}; falling back to the persisted recipient cohort",
                assignment.Id);
            return (recipientsByStudent.Select(group => group.Key).ToArray(), true);
        }
    }

    /// <summary>
    /// Student display names from the ONE batched subscriber read the publish path already uses
    /// (spec §4.2: no per-student directory hop). A failure degrades the NAMES only: the rows keep
    /// their cohort and their delivery state.
    /// </summary>
    /// <remarks>
    /// <para><b>Degraded means a TOTAL name loss, not a missing one.</b> The names come from the batched
    /// <c>contacts/subscribed</c> read, so a student with no subscribed guardian contact produces no row
    /// there and therefore no name — an ordinary tenant, not evidence of a failure. Only a cohort that
    /// resolves <i>no</i> non-blank name at all degrades the read (the signature the production
    /// <c>StudentsContactResolver</c> leaves when it swallows a per-owner <see cref="HttpRequestException"/>:
    /// a SUCCESSFUL read carrying nothing). One resolved name anywhere is enough to call the directory
    /// healthy. An empty cohort is not degraded; a read that throws still is.</para>
    /// <para><b>Known limit.</b> A partial outage is indistinguishable from students who have no contact,
    /// so it is not reported — the names need their own agreed source, never a second hop from here.</para>
    /// </remarks>
    private async Task<(IReadOnlyDictionary<Guid, string> Names, bool Degraded)> ResolveStudentNamesAsync(
        Assignment assignment,
        Guid[] cohort,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        if (cohort.Length == 0)
        {
            return (names, false);
        }

        try
        {
            var subscribers = await contactResolver.ResolveSubscribersAsync(
                new ResolveSubscribersRequest(
                    db.CurrentTenantId,
                    SubscriptionScope.AllAssignments,
                    AssignmentPolicyScope.GradeTargetIds(assignment),
                    cohort,
                    assignment.TopicId),
                cancellationToken);

            foreach (var group in subscribers.Where(subscriber => subscriber.StudentId.HasValue).GroupBy(s => s.StudentId!.Value))
            {
                names[group.Key] = group
                    .Select(subscriber => subscriber.StudentName)
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? string.Empty;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Student directory read failed for assignment {AssignmentId}; the audience rows keep an empty name",
                assignment.Id);
            return (names, true);
        }

        // Total loss only: a cohort without a single resolved name. Partial absence is the normal shape
        // of a tenant whose students simply have no subscribed contact.
        var namesDegraded = cohort.All(studentId =>
            !names.TryGetValue(studentId, out var resolvedName) || string.IsNullOrWhiteSpace(resolvedName));

        return (names, namesDegraded);
    }

    /// <summary>
    /// The publish notifications of this assignment, keyed by recipient. Publish rows only: a
    /// reminder or overdue row describes a different sendout and must not overwrite the state of
    /// the publish one. Where a recipient was queued more than once, the latest attempt wins.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, NotificationLog>> ReadPublishLogsAsync(
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        var rows = await db.NotificationLogs
            .AsNoTracking()
            .Where(log => log.AssignmentId == assignmentId && log.Kind == NotificationKind.Publish)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(log => log.RecipientId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(log => log.Attempt).ThenByDescending(log => log.CreatedAt).First());
    }

    /// <summary>One persisted recipient projected onto the wire shape, with the log's delivery
    /// state layered on top (Q145) and the recipient's own <c>OpenedAt</c> left untouched.</summary>
    private static AudienceContactDto ProjectContact(
        AssignmentRecipient recipient,
        IReadOnlyDictionary<Guid, NotificationLog>? publishLogs)
    {
        NotificationLog? log = null;
        publishLogs?.TryGetValue(recipient.Id, out log);

        return new AudienceContactDto(
            (ContactOwnerTypeDto)recipient.OwnerType,
            (ContactChannelDto)recipient.Channel,
            recipient.Role is { } role ? (GuardianRoleDto)role : null,
            recipient.SubscriptionActive,
            recipient.NotifyOnBroadcast,
            ToDeliveryState(log),
            log?.SentAt,
            recipient.OpenedAt,
            log?.FailureReason,
            log?.NextRetryAt is not null,
            log?.TemplateId);
    }

    /// <summary>The log's state on the wire. No row — or no readable log at all — is
    /// <see cref="AudienceDeliveryStateDto.Unknown"/>, never <c>Sent</c>.</summary>
    private static AudienceDeliveryStateDto ToDeliveryState(NotificationLog? log) => log?.DeliveryStatus switch
    {
        NotificationDeliveryStatus.Queued => AudienceDeliveryStateDto.Queued,
        NotificationDeliveryStatus.Sent => AudienceDeliveryStateDto.Sent,
        NotificationDeliveryStatus.Failed => AudienceDeliveryStateDto.Failed,
        NotificationDeliveryStatus.Skipped => AudienceDeliveryStateDto.Skipped,
        _ => AudienceDeliveryStateDto.Unknown,
    };
}
