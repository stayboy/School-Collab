using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Settings.Contracts.Events;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain.Events;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.UpdateCodedValue;

public sealed class UpdateCodedValueHandler(
    ICodedValueRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ILogger<UpdateCodedValueHandler> logger) : ICommandHandler<UpdateCodedValue>
{
    public async Task HandleAsync(UpdateCodedValue command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling UpdateCodedValue {Id}", command.Id);

        var codedValue = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new CodedValueNotFoundException(command.Id);

        codedValue.Update(command.Name, command.Description, command.DisplayOrder);
        await repository.UpdateAsync(codedValue, cancellationToken);
        await cache.RemoveByTagAsync("coded-values", cancellationToken);

        foreach (var _ in codedValue.DomainEvents.OfType<CodedValueUpdatedEvent>())
        {
            await publisher.EnqueueAsync(new CodedValueUpdated(
                codedValue.Id,
                codedValue.Code,
                codedValue.Name,
                codedValue.Description,
                codedValue.UpdatedAt), cancellationToken);
        }

        codedValue.ClearDomainEvents();

        logger.LogInformation("CodedValue {Id} updated", codedValue.Id);
    }
}
