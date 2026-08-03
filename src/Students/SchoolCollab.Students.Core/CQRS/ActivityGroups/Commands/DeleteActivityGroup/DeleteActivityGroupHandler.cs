using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.DeleteActivityGroup;

public sealed class DeleteActivityGroupHandler(
    IActivityGroupRepository repository,
    IActivityGroupAssignmentQuery assignmentQuery,
    HybridCache cache,
    ILogger<DeleteActivityGroupHandler> logger) : ICommandHandler<DeleteActivityGroup>
{
    public async Task HandleAsync(DeleteActivityGroup command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling DeleteActivityGroup {Id}", command.Id);

        var group = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new ActivityGroupNotFoundException(command.Id);

        // FR-6 / EC-1: referential guard — cannot delete a group with any
        // membership row (any status) or any assignment referencing it.
        var references = new List<string>();

        if (await repository.HasAnyMembershipAsync(command.Id, cancellationToken))
            references.Add("ActivityGroupMemberships");

        // Cross-context check: call the Assignments API. Fail-closed — if the
        // API is unreachable, reject the delete (spec §3.1 FR-6).
        try
        {
            var assignmentRefs = await assignmentQuery.GetReferencingAssignmentsAsync(command.Id, cancellationToken);
            if (assignmentRefs.Length > 0)
                references.Add("Assignments");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check assignment references for group {Id}; failing closed", command.Id);
            references.Add("Assignments (unreachable)");
        }

        if (references.Count > 0)
            throw new ActivityGroupReferencedException(command.Id, references.ToArray());

        group.Delete();

        await repository.DeleteAsync(group, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        group.ClearDomainEvents();

        logger.LogInformation("ActivityGroup {Id} deleted", group.Id);
    }
}
