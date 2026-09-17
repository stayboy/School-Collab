using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetNotificationFailures;

/// <summary>
/// Failed notification rows for an assignment (WS-E2 / ar-16) — the read side the
/// ar-17 Admin failure-surface renders. Failed rows only: <c>Skipped</c> is an expected
/// policy outcome and <c>Sent</c> needs no attention. Tenant-scoped by the context's
/// global query filter, so another tenant's rows are invisible.
/// </summary>
public sealed record GetNotificationFailures(Guid AssignmentId) : IQuery<NotificationFailureDto[]>;

/// <summary>Handles <see cref="GetNotificationFailures"/>.</summary>
public sealed class GetNotificationFailuresHandler(
    AssignmentsDbContext db,
    ILogger<GetNotificationFailuresHandler> logger)
    : IQueryHandler<GetNotificationFailures, NotificationFailureDto[]>
{
    /// <inheritdoc />
    public async Task<NotificationFailureDto[]> HandleAsync(
        GetNotificationFailures query,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling GetNotificationFailures for assignment {AssignmentId}", query.AssignmentId);

        return await db.NotificationLogs
            .AsNoTracking()
            .Where(x => x.AssignmentId == query.AssignmentId
                && x.DeliveryStatus == NotificationDeliveryStatus.Failed)
            .OrderBy(x => x.RecipientId)
            .ThenBy(x => x.Attempt)
            .Select(x => new NotificationFailureDto(
                x.RecipientId,
                x.ContactId,
                (ContactChannelDto)(int)x.Channel,
                (NotificationKindDto)(int)x.Kind,
                x.Attempt,
                x.FailureReason,
                x.NextRetryAt))
            .ToArrayAsync(cancellationToken);
    }
}
