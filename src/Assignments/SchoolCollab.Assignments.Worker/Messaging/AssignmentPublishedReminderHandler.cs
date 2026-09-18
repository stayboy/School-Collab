using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts.Events;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Assignments.Worker.Messaging;

/// <summary>
/// E3 (ar-19) — consumed the unchanged <c>AssignmentPublishedIntegrationEvent</c> to
/// kick one reminder/ completion sweep pass immediately after a publish, so the first
/// reminder evaluation does not wait a full sweep interval. Redelivery / duplicate
/// deliveries are harmless: the sweep is idempotent by construction (a recipient is
/// only queued once the current cycle has settled).
/// </summary>
public sealed class AssignmentPublishedReminderHandler(
    Services.ReminderSweepService reminderSweepService,
    ILogger<AssignmentPublishedReminderHandler> logger)
    : IIntegrationEventHandler<AssignmentPublishedIntegrationEvent>
{
    /// <inheritdoc />
    public async Task HandleAsync(
        AssignmentPublishedIntegrationEvent @event,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Published event {AssignmentId} received — kicking one reminder sweep pass",
            @event.AssignmentId);

        try
        {
            await reminderSweepService.SweepAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A publishing hiccup must not crash the subscriber; the next scheduled sweep
            // pass is sufficient.
            logger.LogError(ex, "Published-event-triggered reminder sweep failed for {AssignmentId}",
                @event.AssignmentId);
        }
    }
}
