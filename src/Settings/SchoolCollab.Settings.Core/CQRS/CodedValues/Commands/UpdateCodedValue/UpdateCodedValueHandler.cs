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

        // FR-6: DisplayOrder uniqueness for GRADE children. If the parent is GRADE
        // and the DisplayOrder changed, check that no sibling already uses it.
        if (codedValue.ParentId is not null)
        {
            var parent = await repository.GetAsync(codedValue.ParentId.Value, cancellationToken);
            if (parent is not null && parent.Code == "GRADE")
            {
                var duplicateLevel = await repository.FindSiblingByDisplayOrderAsync(
                    codedValue.ParentId.Value, command.DisplayOrder, cancellationToken);
                if (duplicateLevel is not null && duplicateLevel.Id != codedValue.Id)
                {
                    throw new DuplicateGradeLevelException(command.DisplayOrder, duplicateLevel.Id);
                }
            }
        }

        codedValue.Update(command.Name, command.Description, command.DisplayOrder);

        // Enriched full-state payload via CodedValueEventMapper — single source of
        // truth for the projection contract (adr-cross-module-calls.md).
        // Enqueue BEFORE save: atomic commit with the entity.
        var parentCode = await CodedValueEventMapper.ResolveParentCodeAsync(
            repository, codedValue.ParentId, cancellationToken);

        foreach (var _ in codedValue.DomainEvents.OfType<CodedValueUpdatedEvent>())
        {
            await publisher.EnqueueAsync(codedValue.ToUpdatedEvent(parentCode), cancellationToken);
        }

        await repository.UpdateAsync(codedValue, cancellationToken);
        await cache.RemoveByTagAsync("coded-values", cancellationToken);

        codedValue.ClearDomainEvents();

        logger.LogInformation("CodedValue {Id} updated", codedValue.Id);
    }
}
