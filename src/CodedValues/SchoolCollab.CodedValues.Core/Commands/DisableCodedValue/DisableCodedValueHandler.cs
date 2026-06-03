using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.CodedValues.Contracts.Events;
using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Domain.Events;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;
using SchoolCollab.CodedValues.Core.Messaging;

namespace SchoolCollab.CodedValues.Core.Commands.DisableCodedValue;

public sealed class DisableCodedValueHandler(
    ICodedValueRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache) : ICommandHandler<DisableCodedValue>
{
    public async Task HandleAsync(DisableCodedValue command, CancellationToken cancellationToken = default)
    {
        var codedValue = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new CodedValueNotFoundException(command.Id);

        codedValue.Disable();
        await repository.UpdateAsync(codedValue, cancellationToken);
        await cache.RemoveByTagAsync("coded-values", cancellationToken);

        foreach (var _ in codedValue.DomainEvents.OfType<CodedValueDisabledEvent>())
        {
            await publisher.EnqueueAsync(
                new CodedValueDisabled(codedValue.Id, codedValue.Code, codedValue.UpdatedAt),
                cancellationToken);
        }

        codedValue.ClearDomainEvents();
    }
}
