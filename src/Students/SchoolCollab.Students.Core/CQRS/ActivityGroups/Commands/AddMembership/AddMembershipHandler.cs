using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.AddMembership;

public sealed class AddMembershipHandler(
    IActivityGroupRepository groupRepository,
    IActivityGroupMembershipRepository membershipRepository,
    IStudentRepository studentRepository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<AddMembershipHandler> logger) : ICommandHandler<AddMembership, Guid>
{
    public async Task<Guid> HandleAsync(AddMembership command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling AddMembership student={StudentId} group={GroupId}",
            command.StudentId, command.ActivityGroupId);

        // FR-12: reject membership for an archived group.
        var group = await groupRepository.GetAsync(command.ActivityGroupId, cancellationToken)
            ?? throw new ActivityGroupNotFoundException(command.ActivityGroupId);

        if (group.Status == ActivityGroupStatus.Archived)
            throw new ArchivedGroupException(command.ActivityGroupId);

        // FR-11: reject membership for a student that is soft-deleted, belongs
        // to a different tenant, or does not exist. The global tenant filter
        // and soft-delete filter on GetAsync handle all three cases — a null
        // result means the student is not visible in this tenant context.
        var student = await studentRepository.GetAsync(command.StudentId, cancellationToken)
            ?? throw new StudentNotFoundException(command.StudentId);

        // FR-10: duplicate-active prevention — at most one active membership
        // per (tenant, student, group).
        var existing = await membershipRepository.GetActiveAsync(
            command.StudentId, command.ActivityGroupId, cancellationToken);

        if (existing is not null)
            throw new DuplicateActiveMembershipException(command.StudentId, command.ActivityGroupId);

        // FR-13: capacity enforcement — if Capacity is set and active count
        // has reached it, reject. Null Capacity = unlimited (AC-10).
        if (group.Capacity.HasValue)
        {
            var activeCount = await groupRepository.CountActiveMembersAsync(
                command.ActivityGroupId, cancellationToken);

            if (activeCount >= group.Capacity.Value)
                throw new GroupAtCapacityException(
                    command.ActivityGroupId, group.Capacity.Value, activeCount);
        }

        // FR-15: strict tenant entity — inherit the current tenant context.
        var membership = ActivityGroupMembership.Create(
            command.ActivityGroupId, command.StudentId, command.JoinedOn)
            .WithTenant(tenantProvider);

        await membershipRepository.AddAsync(membership, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        membership.ClearDomainEvents();

        logger.LogInformation("Membership {Id} created: student={StudentId} group={GroupId}",
            membership.Id, command.StudentId, command.ActivityGroupId);

        return membership.Id;
    }
}
