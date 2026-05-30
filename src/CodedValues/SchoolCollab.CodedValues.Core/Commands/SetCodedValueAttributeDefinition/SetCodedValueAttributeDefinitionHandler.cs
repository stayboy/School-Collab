using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;

namespace SchoolCollab.CodedValues.Core.Commands.SetCodedValueAttributeDefinition;

public sealed class SetCodedValueAttributeDefinitionHandler(ICodedValueRepository repository)
    : ICommandHandler<SetCodedValueAttributeDefinition>
{
    public async Task HandleAsync(SetCodedValueAttributeDefinition command, CancellationToken cancellationToken = default)
    {
        var codedValue = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new CodedValueNotFoundException(command.Id);

        codedValue.SetAttributeDefinition(command.Key, command.DataType, command.SourceCode, command.IsRequired, command.AllowMultiple, command.DisplayName);
        await repository.UpdateAsync(codedValue, cancellationToken);
    }
}
