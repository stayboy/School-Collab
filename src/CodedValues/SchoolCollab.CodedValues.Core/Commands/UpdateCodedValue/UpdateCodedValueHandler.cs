using MassTransit;
using SchoolCollab.CodedValues.Contracts.Events;
using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Domain.Events;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;

namespace SchoolCollab.CodedValues.Core.Commands.UpdateCodedValue;

public sealed class UpdateCodedValueHandler(
    ICodedValueRepository repository,
    IPublishEndpoint publishEndpoint) : ICommandHandler<UpdateCodedValue>
{
    public async Task HandleAsync(UpdateCodedValue command, CancellationToken cancellationToken = default)
    {
        var codedValue = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new CodedValueNotFoundException(command.Id);

        codedValue.Update(command.Name, command.Description, command.DisplayOrder);
        await repository.UpdateAsync(codedValue, cancellationToken);

        foreach (var _ in codedValue.DomainEvents.OfType<CodedValueUpdatedEvent>())
        {
            await publishEndpoint.Publish(new CodedValueUpdated(
                codedValue.Id,
                codedValue.Code,
                codedValue.Name,
                codedValue.Description,
                codedValue.UpdatedAt), cancellationToken);
        }

        codedValue.ClearDomainEvents();
    }
}
