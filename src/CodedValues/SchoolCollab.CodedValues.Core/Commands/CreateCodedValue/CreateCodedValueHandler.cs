using MassTransit;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.CodedValues.Contracts.Events;
using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.CodedValues.Core.Domain.Events;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;

namespace SchoolCollab.CodedValues.Core.Commands.CreateCodedValue;

public sealed class CreateCodedValueHandler(
    ICodedValueRepository repository,
    IPublishEndpoint publishEndpoint,
    HybridCache cache,
    ILogger<CreateCodedValueHandler> logger) : ICommandHandler<CreateCodedValue, Guid>
{
    public async Task<Guid> HandleAsync(CreateCodedValue command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling CreateCodedValue {Code}", command.Code);

        var code = command.Code.Trim().ToUpperInvariant();

        if (await repository.ExistsByCodeAsync(code, cancellationToken))
        {
            throw new DuplicateCodeException(code);
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
            await publishEndpoint.Publish(new CodedValueCreated(
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
