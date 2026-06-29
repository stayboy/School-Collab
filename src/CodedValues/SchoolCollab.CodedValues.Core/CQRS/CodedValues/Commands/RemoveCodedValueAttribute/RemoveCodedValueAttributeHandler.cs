using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;

namespace SchoolCollab.CodedValues.Core.CQRS.CodedValues.Commands.RemoveCodedValueAttribute;

public sealed class RemoveCodedValueAttributeHandler(
    ICodedValueRepository repository,
    HybridCache cache) : ICommandHandler<RemoveCodedValueAttribute>
{
    public async Task HandleAsync(RemoveCodedValueAttribute command, CancellationToken cancellationToken = default)
    {
        var codedValue = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new CodedValueNotFoundException(command.Id);

        codedValue.RemoveAttribute(command.Key);
        await repository.UpdateAsync(codedValue, cancellationToken);
        await cache.RemoveByTagAsync("coded-values", cancellationToken);
    }
}
