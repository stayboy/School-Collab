using MassTransit;
using SchoolCollab.CodedValues.Contracts.Events;
using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.CodedValues.Core.Domain.Events;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;

namespace SchoolCollab.CodedValues.Core.Commands.CreateCodedValue;

public sealed class CreateCodedValueHandler(
    ICodedValueRepository repository,
    IPublishEndpoint publishEndpoint) : ICommandHandler<CreateCodedValue>
{
    public async Task HandleAsync(CreateCodedValue command, CancellationToken cancellationToken = default)
    {
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
    }
}
