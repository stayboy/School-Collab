namespace SchoolCollab.Assignments.Core.Messaging;

public interface IIntegrationEventPublisher
{
    Task PublishAsync(object payload, CancellationToken ct = default);
}