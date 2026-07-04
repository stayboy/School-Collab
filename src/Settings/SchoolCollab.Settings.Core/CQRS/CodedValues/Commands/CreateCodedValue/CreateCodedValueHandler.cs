using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Settings.Contracts.Events;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Events;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.CreateCodedValue;

public sealed class CreateCodedValueHandler(
    ICodedValueRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ILogger<CreateCodedValueHandler> logger) : ICommandHandler<CreateCodedValue, Guid>
{
    public async Task<Guid> HandleAsync(CreateCodedValue command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling CreateCodedValue {Code}", command.Code);

        var code = command.Code.Trim().ToUpperInvariant();

        if (await repository.ExistsByCodeInParentAsync(code, command.ParentId, cancellationToken))
        {
            throw new DuplicateCodeException(code, command.ParentId);
        }

        var codedValue = CodedValue.Create(
            command.Code,
            command.Name,
            command.Description,
            command.ParentId,
            command.DisplayOrder);

        await repository.AddAsync(codedValue, cancellationToken);
        await cache.RemoveByTagAsync("coded-values", cancellationToken);

        foreach (var _ in codedValue.DomainEvents.OfType<CodedValueCreatedEvent>())
        {
            await publisher.EnqueueAsync(new CodedValueCreated(
                codedValue.Id,
                codedValue.Code,
                codedValue.Name,
                codedValue.Description,
                codedValue.ParentId,
                codedValue.DisplayOrder,
                codedValue.CreatedAt), cancellationToken);
        }

        codedValue.ClearDomainEvents();

        logger.LogInformation("CodedValue {Id} persisted with code {Code}", codedValue.Id, codedValue.Code);
        return codedValue.Id;
    }
}
