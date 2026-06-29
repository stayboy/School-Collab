using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;

namespace SchoolCollab.CodedValues.Core.CQRS.CodedValues.Commands.CreateCodedValue;

public sealed class BulkCreateCodedValuesHandler(
    ICodedValueRepository repository,
    HybridCache cache,
    ILogger<BulkCreateCodedValuesHandler> logger) : ICommandHandler<BulkCreateCodedValues, BulkCreateResult>
{
    public async Task<BulkCreateResult> HandleAsync(BulkCreateCodedValues command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling BulkCreateCodedValues for parent {ParentId} with {Count} children",
            command.ParentId, command.Children.Count);

        var parent = await repository.GetAsync(command.ParentId, cancellationToken);
        if (parent is null)
            throw new CodedValueNotFoundException(command.ParentId);

        // Check for intra-batch duplicates (these are always errors)
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in command.Children)
        {
            var code = child.Code.Trim().ToUpperInvariant();
            if (!seenCodes.Add(code))
                throw new DuplicateCodeException(code, command.ParentId);
        }

        // Determine which codes already exist under the parent — skip those
        var skippedCodes = new List<string>();
        var toCreate = new List<BulkCreateChildItem>();
        foreach (var child in command.Children)
        {
            var code = child.Code.Trim().ToUpperInvariant();
            if (await repository.ExistsByCodeInParentAsync(code, command.ParentId, cancellationToken))
            {
                skippedCodes.Add(code);
                logger.LogInformation("Skipping existing code {Code} under parent {ParentId}", code, command.ParentId);
            }
            else
            {
                toCreate.Add(child);
            }
        }

        if (toCreate.Count > 0)
        {
            var entities = toCreate.Select(child =>
                CodedValue.Create(child.Code, child.Name, child.Description, command.ParentId, child.DisplayOrder))
                .ToList();

            await repository.AddRangeAsync(entities, cancellationToken);
            await cache.RemoveByTagAsync("coded-values", cancellationToken);

            logger.LogInformation("Bulk created {Count} coded values under parent {ParentId}, skipped {SkippedCount} existing codes",
                entities.Count, command.ParentId, skippedCodes.Count);
        }
        else
        {
            logger.LogInformation("All {Count} codes already exist under parent {ParentId}, nothing to create",
                command.Children.Count, command.ParentId);
        }

        return new BulkCreateResult(toCreate.Count, skippedCodes.AsReadOnly(), command.ParentId);
    }
}