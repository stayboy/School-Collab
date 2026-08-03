using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.LinkAssignmentGroups;

public sealed class LinkAssignmentGroupsHandler(
    IAssignmentRepository assignmentRepository,
    IAssignmentActivityGroupRepository linkRepository,
    IActivityGroupLookup groupLookup,
    ITenantProvider tenantProvider,
    HybridCache cache,
    ILogger<LinkAssignmentGroupsHandler> logger) : ICommandHandler<LinkAssignmentGroups>
{
    public async Task HandleAsync(LinkAssignmentGroups command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling LinkAssignmentGroups {AssignmentId}", command.AssignmentId);

        var assignment = await assignmentRepository.GetAsync(command.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.AssignmentId);

        var requestedIds = command.ActivityGroupIds.Distinct().ToArray();

        // FR-21/EC-11: resolve groups in the caller's tenant. Missing ids (group
        // does not exist or belongs to a different tenant) are omitted by the
        // port and rejected here.
        var groups = await groupLookup.GetByIdsAsync(requestedIds, cancellationToken);
        var foundIds = groups.Select(g => g.Id).ToHashSet();
        var missing = requestedIds.Where(id => !foundIds.Contains(id)).ToArray();
        if (missing.Length > 0)
            throw new ArgumentException(
                $"Activity group(s) not found or not in the current tenant: {string.Join(", ", missing)}");

        // FR-22: reject archived groups.
        var archived = groups.Where(g => g.Status == "Archived").ToArray();
        if (archived.Length > 0)
            throw new ArgumentException(
                $"Cannot link archived activity group(s): {string.Join(", ", archived.Select(a => a.Id))}");

        var tenantId = tenantProvider.GetTenantContext().TenantId;
        await linkRepository.ReplaceForAssignmentAsync(command.AssignmentId, tenantId, groups.Select(g => g.Id).ToArray(), cancellationToken);
        await cache.RemoveByTagAsync("assignments", cancellationToken);

        logger.LogInformation(
            "Assignment {AssignmentId} linked to {Count} activity group(s)",
            command.AssignmentId, groups.Length);
    }
}
