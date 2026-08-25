using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RecoverCodedValue;

public sealed class RecoverCodedValueHandler(
    ICodedValueRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache) : ICommandHandler<RecoverCodedValue>
{
    public async Task HandleAsync(RecoverCodedValue command, CancellationToken cancellationToken = default)
    {
        var codedValue = await repository.GetIncludingDeletedAsync(command.Id, cancellationToken)
            ?? throw new CodedValueNotFoundException(command.Id);

        if (!codedValue.IsDeleted)
        {
            return;
        }

        if (await repository.ExistsByCodeInParentAsync(codedValue.Code, codedValue.ParentId, cancellationToken))
        {
            throw new DuplicateCodeException(codedValue.Code, codedValue.ParentId);
        }

        codedValue.Recover();

        // Recovery must reach the projection — consumers treat CodedValueUpdated as
        // "upsert this row as live", which re-materializes the recovered value
        // (adr-cross-module-calls.md Phase 0).
        var parentCode = await CodedValueEventMapper.ResolveParentCodeAsync(
            repository, codedValue.ParentId, cancellationToken);
        await publisher.EnqueueAsync(codedValue.ToUpdatedEvent(parentCode), cancellationToken);

        await repository.UpdateAsync(codedValue, cancellationToken);
        await cache.RemoveByTagAsync("coded-values", cancellationToken);
    }
}