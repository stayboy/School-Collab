using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.SetCodedValueAttribute;

public sealed class SetCodedValueAttributeHandler(
    ICodedValueRepository repository,
    HybridCache cache) : ICommandHandler<SetCodedValueAttribute>
{
    public async Task HandleAsync(SetCodedValueAttribute command, CancellationToken cancellationToken = default)
    {
        var codedValue = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new CodedValueNotFoundException(command.Id);

        codedValue.SetAttribute(command.Key, command.Value);
        await repository.UpdateAsync(codedValue, cancellationToken);
        await cache.RemoveByTagAsync("coded-values", cancellationToken);
    }
}
