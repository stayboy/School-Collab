using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.CreateCodedValue;

/// <summary>
/// Bulk-creates coded values. <b>Dual-mode tenancy</b> (global-tenant-filter.md
/// §3.3 / FR-5) — same pattern as <see cref="CreateCodedValueHandler"/>:
/// <list type="bullet">
/// <item><b>Real tenant</b> (<c>CurrentTenantId != Guid.Empty</c>): stamps
///   <b>tenant-owned</b> rows (<c>TenantId = current</c>), isolated to that tenant.</item>
/// <item><b>Default/dev tenant</b> (<c>CurrentTenantId == Guid.Empty</c>): writes
///   <b>shared blueprint</b> rows (<c>TenantId = null</c>) under a suppressed guard —
///   the dev/admin vocabulary-edit affordance. In production the API pipeline
///   guarantees a real <c>tenant_id</c> claim (FR-19), so <c>Guid.Empty</c> only
///   occurs in dev/test.</item>
/// </list>
/// <para>Intra-batch duplicates are rejected before any DB work. Codes that
/// already exist in the tenant-visible scope are skipped (idempotent on retry).</para>
/// </summary>
public sealed class BulkCreateCodedValuesHandler(
    ICodedValueRepository repository,
    IIntegrationEventPublisher publisher,
    ITenantProvider tenantProvider,
    ITenantContextAccessor tenantContextAccessor,
    HybridCache cache,
    ILogger<BulkCreateCodedValuesHandler> logger) : ICommandHandler<BulkCreateCodedValues, BulkCreateResult>
{
    public async Task<BulkCreateResult> HandleAsync(BulkCreateCodedValues command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling BulkCreateCodedValues for parent {ParentId} with {Count} children",
            command.ParentId, command.Children.Count);

        // FR-5: determine the target tenant for this batch.
        var currentTenantId = tenantProvider.GetTenantContext().TenantId;
        var isDefaultTenant = currentTenantId == Guid.Empty;
        var targetTenantId = isDefaultTenant ? (Guid?)null : currentTenantId;

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
            {
                var cv = CodedValue.Create(child.Code, child.Name, child.Description, command.ParentId, child.DisplayOrder);
                // FR-5: stamp the target tenant — real tenant → owned row, default → NULL blueprint.
                cv.SetTenant(targetTenantId);
                return cv;
            }).ToList();

            // FR-5: the default/dev path writes NULL-blueprint rows under a suppressed
            // guard (the hybrid save-guard permits NULL, but this is belt-and-suspenders
            // and documents intent).
            if (isDefaultTenant)
            {
                using (tenantContextAccessor.SuppressTenantGuard())
                {
                    await repository.AddRangeAsync(entities, cancellationToken);
                }
            }
            else
            {
                await repository.AddRangeAsync(entities, cancellationToken);
            }

            await cache.RemoveByTagAsync("coded-values", cancellationToken);

            // Bulk-created values must reach the projection like any other create —
            // the startup backfill only runs once (adr-cross-module-calls.md).
            // All entities share command.ParentId, so parent.Code serves each event.
            foreach (var entity in entities)
            {
                await publisher.EnqueueAsync(entity.ToCreatedEvent(parent.Code), cancellationToken);
            }

            logger.LogInformation(
                "Bulk created {Count} coded values under parent {ParentId} (tenant={TenantKind}), skipped {SkippedCount} existing codes",
                entities.Count, command.ParentId, isDefaultTenant ? "shared-blueprint" : "tenant-owned", skippedCodes.Count);
        }
        else
        {
            logger.LogInformation("All {Count} codes already exist under parent {ParentId}, nothing to create",
                command.Children.Count, command.ParentId);
        }

        return new BulkCreateResult(toCreate.Count, skippedCodes.AsReadOnly(), command.ParentId);
    }
}
