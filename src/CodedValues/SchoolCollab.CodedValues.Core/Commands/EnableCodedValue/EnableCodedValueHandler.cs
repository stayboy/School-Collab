using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.CodedValues.Contracts.Events;
using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Domain.Events;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;
using SchoolCollab.CodedValues.Core.Messaging;

namespace SchoolCollab.CodedValues.Core.Commands.EnableCodedValue;

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
        await repository.UpdateAsync(codedValue, cancellationToken);
        await cache.RemoveByTagAsync("coded-values", cancellationToken);

        foreach (var _ in codedValue.DomainEvents.OfType<CodedValueEnabledEvent>())
        {
            await publisher.EnqueueAsync(
                new CodedValueEnabled(codedValue.Id, codedValue.Code, codedValue.UpdatedAt),
                cancellationToken);
        }

        codedValue.ClearDomainEvents();
    }
}
