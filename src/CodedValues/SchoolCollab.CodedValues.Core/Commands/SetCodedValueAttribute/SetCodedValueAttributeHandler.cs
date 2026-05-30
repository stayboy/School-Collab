using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;

namespace SchoolCollab.CodedValues.Core.Commands.SetCodedValueAttribute;

public sealed class SetCodedValueAttributeHandler(ICodedValueRepository repository)
    : ICommandHandler<SetCodedValueAttribute>
{
    public async Task HandleAsync(SetCodedValueAttribute command, CancellationToken cancellationToken = default)
    {
        var codedValue = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new CodedValueNotFoundException(command.Id);

        codedValue.SetAttribute(command.Key, command.Value, command.DataType, command.SourceCode);
        await repository.UpdateAsync(codedValue, cancellationToken);
    }
}
