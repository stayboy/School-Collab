using MassTransit;
using SchoolCollab.CodedValues.Contracts.Events;
using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Domain.Events;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;

namespace SchoolCollab.CodedValues.Core.Commands.EnableCodedValue;

public sealed class EnableCodedValueHandler(
    ICodedValueRepository repository,
    IPublishEndpoint publishEndpoint) : ICommandHandler<EnableCodedValue>
{
    public async Task HandleAsync(EnableCodedValue command, CancellationToken cancellationToken = default)
    {
        var codedValue = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new CodedValueNotFoundException(command.Id);

        codedValue.Enable();
        await repository.UpdateAsync(codedValue, cancellationToken);

        foreach (var _ in codedValue.DomainEvents.OfType<CodedValueEnabledEvent>())
        {
            await publishEndpoint.Publish(
                new CodedValueEnabled(codedValue.Id, codedValue.Code, codedValue.UpdatedAt),
                cancellationToken);
        }

        codedValue.ClearDomainEvents();
    }
}
