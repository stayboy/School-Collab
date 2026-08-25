using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Settings.Contracts.Events;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.DeleteCodedValue;

public sealed class DeleteCodedValueHandler(
    ICodedValueRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache) : ICommandHandler<DeleteCodedValue>
{
    public async Task HandleAsync(DeleteCodedValue command, CancellationToken cancellationToken = default)
    {
        var codedValue = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new CodedValueNotFoundException(command.Id);

        var childCount = await repository.CountChildrenAsync(command.Id, cancellationToken);
        if (childCount > 0)
        {
            throw new CodedValueHasChildrenException(command.Id, childCount);
        }

        var referencingCodes = await repository.GetReferencingSourceCodesAsync(command.Id, cancellationToken);
        if (referencingCodes.Count > 0)
        {
            throw new CodedValueReferencedException(command.Id, referencingCodes.ToArray());
        }

        codedValue.Delete();

        // Soft-deletes must reach the projection, otherwise consumers would keep
        // validating against a deleted value (adr-cross-module-calls.md Phase 0).
        await publisher.EnqueueAsync(
            new CodedValueDeleted(
                codedValue.Id,
                codedValue.Code,
                codedValue.DeletedAt ?? DateTimeOffset.UtcNow),
            cancellationToken);

        await repository.UpdateAsync(codedValue, cancellationToken);
        await cache.RemoveByTagAsync("coded-values", cancellationToken);
    }
}