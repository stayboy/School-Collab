using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RemoveCodedValueAttribute;

public sealed class RemoveCodedValueAttributeHandler(
    ICodedValueRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache) : ICommandHandler<RemoveCodedValueAttribute>
{
    public async Task HandleAsync(RemoveCodedValueAttribute command, CancellationToken cancellationToken = default)
    {
        var codedValue = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new CodedValueNotFoundException(command.Id);

        codedValue.RemoveAttribute(command.Key);

        // Attribute changes are projection-relevant (see SetCodedValueAttribute).
        // Enqueue BEFORE save: atomic commit with the entity.
        var parentCode = await CodedValueEventMapper.ResolveParentCodeAsync(
            repository, codedValue.ParentId, cancellationToken);
        await publisher.EnqueueAsync(codedValue.ToUpdatedEvent(parentCode), cancellationToken);

        await repository.UpdateAsync(codedValue, cancellationToken);
        await cache.RemoveByTagAsync("coded-values", cancellationToken);
    }
}
