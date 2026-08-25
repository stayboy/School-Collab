using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Settings.Contracts.Events;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain.Events;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.DisableCodedValue;

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
        foreach (var _ in codedValue.DomainEvents.OfType<CodedValueDisabledEvent>())
        {
            await publisher.EnqueueAsync(
                new CodedValueDisabled(codedValue.Id, codedValue.Code, codedValue.UpdatedAt),
                cancellationToken);
        }

        await repository.UpdateAsync(codedValue, cancellationToken);
        await cache.RemoveByTagAsync("coded-values", cancellationToken);

        codedValue.ClearDomainEvents();
    }
}
