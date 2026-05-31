using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;

namespace SchoolCollab.CodedValues.Core.Commands.RemoveCodedValueAttributeDefinition;

public sealed class RemoveCodedValueAttributeDefinitionHandler(
    ICodedValueRepository repository,
    HybridCache cache) : ICommandHandler<RemoveCodedValueAttributeDefinition>
{
    public async Task HandleAsync(RemoveCodedValueAttributeDefinition command, CancellationToken cancellationToken = default)
    {
        var codedValue = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new CodedValueNotFoundException(command.Id);

        codedValue.RemoveAttributeDefinition(command.Key);
        await repository.UpdateAsync(codedValue, cancellationToken);
        await cache.RemoveByTagAsync("coded-values", cancellationToken);
    }
}
