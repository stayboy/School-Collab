using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;

namespace SchoolCollab.CodedValues.Core.Commands.DeleteCodedValue;

public sealed class DeleteCodedValueHandler(
    ICodedValueRepository repository,
    HybridCache cache) : ICommandHandler<DeleteCodedValue>
{
    public async Task HandleAsync(DeleteCodedValue command, CancellationToken cancellationToken = default)
    {
        var codedValue = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new CodedValueNotFoundException(command.Id);

        var childCount = await repository.CountChildrenAsync(command.Id, cancellationToken);
        if (childCount > 0)
        {
            throw new CodedValueHasChildrenException(command.Id, childCount);
        }

        var referencingCodes = await repository.GetReferencingSourceCodesAsync(command.Id, cancellationToken);
        if (referencingCodes.Count > 0)
        {
            throw new CodedValueReferencedException(command.Id, referencingCodes.ToArray());
        }

        codedValue.Delete();
        await repository.UpdateAsync(codedValue, cancellationToken);
        await cache.RemoveByTagAsync("coded-values", cancellationToken);
    }
}