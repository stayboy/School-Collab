using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;

namespace SchoolCollab.CodedValues.Core.Commands.RecoverCodedValue;

public sealed class RecoverCodedValueHandler(
    ICodedValueRepository repository,
    HybridCache cache) : ICommandHandler<RecoverCodedValue>
{
    public async Task HandleAsync(RecoverCodedValue command, CancellationToken cancellationToken = default)
    {
        var codedValue = await repository.GetIncludingDeletedAsync(command.Id, cancellationToken)
            ?? throw new CodedValueNotFoundException(command.Id);

        if (!codedValue.IsDeleted)
        {
            return;
        }

        if (await repository.ExistsByCodeInParentAsync(codedValue.Code, codedValue.ParentId, cancellationToken))
        {
            throw new DuplicateCodeException(codedValue.Code, codedValue.ParentId);
        }

        codedValue.Recover();
        await repository.UpdateAsync(codedValue, cancellationToken);
        await cache.RemoveByTagAsync("coded-values", cancellationToken);
    }
}