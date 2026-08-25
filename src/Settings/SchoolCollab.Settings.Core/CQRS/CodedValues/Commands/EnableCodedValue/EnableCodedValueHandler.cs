using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Settings.Contracts.Events;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain.Events;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.EnableCodedValue;

public sealed class EnableCodedValueHandler(
    ICodedValueRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache) : ICommandHandler<EnableCodedValue>
{
    public async Task HandleAsync(EnableCodedValue command, CancellationToken cancellationToken = default)
    {
        var codedValue = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new CodedValueNotFoundException(command.Id);

        codedValue.Enable();
        foreach (var _ in codedValue.DomainEvents.OfType<CodedValueEnabledEvent>())
        {
            await publisher.EnqueueAsync(
                new CodedValueEnabled(codedValue.Id, codedValue.Code, codedValue.UpdatedAt),
                cancellationToken);
        }

        await repository.UpdateAsync(codedValue, cancellationToken);
        await cache.RemoveByTagAsync("coded-values", cancellationToken);

        codedValue.ClearDomainEvents();
    }
}
