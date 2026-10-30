using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.CQRS;

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
/// <para><b>Names come from the cohort read, not from the contacts.</b> The target resolver returns
/// id <i>and</i> display name in one round-trip, so a student with no subscribed guardian contact
/// still carries their name — those are exactly the rows a teacher has to chase. A failed resolver
/// read degrades to the persisted recipient cohort with empty names and
/// <c>Degraded = true</c>, leaving every row and every delivery state intact; a log failure
/// degrades delivery only, never the roster.</para>
/// </remarks>
public sealed class GetAssignmentAudienceHandler(
    AssignmentsDbContext db,
    IAssignmentTargetResolver targetResolver,
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

        var (cohort, degraded) = await ResolveCohortAsync(assignment, recipientsByStudent, cancellationToken);

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
            .OrderBy(student => student.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(student => student.Id)
            .ToArray();

        var rows = ordered
            .Take(AudienceLimits.AudienceCap)
            .Select(student =>
            {
                var contacts = recipientsByStudent[student.Id]
                    .OrderBy(recipient => (int)recipient.Channel)
                    .ThenBy(recipient => (int)recipient.OwnerType)
                    .Select(recipient => ProjectContact(recipient, publishLogs))
                    .ToList();

                return new AudienceStudentDto(
                    student.Id,
                    student.FullName,
                    contacts.Exists(contact => contact.SubscriptionActive),
                    contacts);
            })
            .ToList();

        return new AssignmentAudienceDto(rows, ordered.Length > AudienceLimits.AudienceCap, degraded);
    }

    /// <summary>
    /// The student cohort and its display names, from the same resolver publish uses. A resolver
    /// failure degrades to the persisted recipients' wards — the cohort that really was published,
    /// without names — rather than an empty list, because those rows are exactly what the caller
    /// came to read.
    /// </summary>
    private async Task<(TargetStudent[] Cohort, bool Degraded)> ResolveCohortAsync(
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
            var resolved = await targetResolver.ResolveStudentsAsync(
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
                "Target resolution failed for assignment {AssignmentId}; falling back to the persisted recipient cohort, without names",
                assignment.Id);
            return (recipientsByStudent
                .Select(group => new TargetStudent(group.Key, string.Empty))
                .ToArray(), true);
        }
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