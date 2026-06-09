using Microsoft.Extensions.Logging;
using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;

namespace SchoolCollab.CodedValues.Core.Commands.CreateCodedValue;

public sealed class BulkCreateCodedValuesHandler(
    ICodedValueRepository repository,
    ILogger<BulkCreateCodedValuesHandler> logger) : ICommandHandler<BulkCreateCodedValues>
{
    public async Task HandleAsync(BulkCreateCodedValues command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling BulkCreateCodedValues for parent {ParentId} with {Count} children",
            command.ParentId, command.Children.Count);

        var parent = await repository.GetAsync(command.ParentId, cancellationToken);
        if (parent is null)
            throw new CodedValueNotFoundException(command.ParentId);

        // Validate all codes — no duplicates within the batch and no conflicts with existing codes
        foreach (var child in command.Children)
        {
            var code = child.Code.Trim().ToUpperInvariant();
            if (await repository.ExistsByCodeAsync(code, cancellationToken))
                throw new DuplicateCodeException(code);
        }

        var entities = command.Children.Select(child =>
            CodedValue.Create(child.Code, child.Name, child.Description, command.ParentId, child.DisplayOrder))
            .ToList();

        await repository.AddRangeAsync(entities, cancellationToken);

        logger.LogInformation("Bulk created {Count} coded values under parent {ParentId}",
            entities.Count, command.ParentId);
    }
}