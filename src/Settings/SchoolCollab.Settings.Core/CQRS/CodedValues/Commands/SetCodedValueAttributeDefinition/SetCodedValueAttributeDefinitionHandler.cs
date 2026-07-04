using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.SetCodedValueAttributeDefinition;

public sealed class SetCodedValueAttributeDefinitionHandler(
    ICodedValueRepository repository,
    HybridCache cache) : ICommandHandler<SetCodedValueAttributeDefinition>
{
    public async Task HandleAsync(SetCodedValueAttributeDefinition command, CancellationToken cancellationToken = default)
    {
        var codedValue = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new CodedValueNotFoundException(command.Id);

        codedValue.SetAttributeDefinition(
            command.Key, command.DataType, command.SourceCode, command.IsRequired,
            command.AllowMultiple, command.DisplayName, command.MinLength, command.MaxLength, command.RegexPattern);
        await repository.UpdateAsync(codedValue, cancellationToken);
        await cache.RemoveByTagAsync("coded-values", cancellationToken);
    }
}
