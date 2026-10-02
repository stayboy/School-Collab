using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.LinkAssignmentGroups;

/// <summary>
/// Replaces the activity-group link set of an assignment (spec §7.3
/// <c>PUT /api/assignments/{assignmentId}/groups</c>). Replace-set semantics: the posted set
/// is written fresh (FR-17).
///
/// <para><b>R2 (D-8.2) — thin adapter over the aggregate.</b> This handler no longer writes the
/// link table on its own: it maps the posted group ids onto the assignment's <b>full</b>
/// replacement target set (every non-group target preserved, the group rows swapped for the
/// posted set) and hands that to <see cref="Assignment.SetTargets"/>, so validation (TGT-13,
/// TGT-2, D-2, the D-8.1 archived-group rule), <c>DisplayOrder</c> re-indexing and the derived
/// compat column all come from the aggregate instead of a second, divergent write path. The
/// legacy link table — read by <c>GET /assignments/{id}/groups</c> and the FR-6 delete guard —
/// is then synchronized to the same validated set.</para>
/// </summary>
public sealed class LinkAssignmentGroupsHandler(
    AssignmentsDbContext db,
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
        // does not exist or belongs to a different tenant) are omitted by
        // the port and rejected here.
        var groups = await groupLookup.GetByIdsAsync(requestedIds, cancellationToken);
        var foundIds = groups.Select(g => g.Id).ToHashSet();
        var missing = requestedIds.Where(id => !foundIds.Contains(id)).ToArray();
        if (missing.Length > 0)
            throw new ArgumentException(
                $"Activity group(s) not found or not in the current tenant: {string.Join(", ", missing)}");

        var tenantId = tenantProvider.GetTenantContext().TenantId;

        // D-8.2: preserve every non-group target, then append the posted groups — the whole set
        // goes through SetTargets, which is now the only place validation and derivation happen.
        var replacement = assignment.Targets
            .Where(t => t.Kind != TargetKind.ActivityGroup)
            .OrderBy(t => t.DisplayOrder)
            .Select(t => (Kind: t.Kind, RefId: t.RefId))
            .ToList();
        replacement.AddRange(requestedIds.Select(id => (Kind: TargetKind.ActivityGroup, RefId: (Guid?)id)));

        // FR-22 (D-8.1): the newly-added archived ids are rejected inside the aggregate; the
        // already-persisted ones are not re-validated, so a re-save keeps a historical link.
        var inactiveGroupIds = groups.Where(g => !g.IsActive).Select(g => g.Id).ToArray();
        assignment.SetTargets(replacement, tenantId, inactiveGroupIds);

        // DetectChanges before SaveChanges: the target rows live behind a field-backed
        // collection (PropertyAccessMode.Field), which the InMemory provider in particular does
        // not pick up on its own (the UpdateAssignmentCommandHandler precedent).
        // BEST-PRACTICE FIX (R2-7): the target-row replacement and the legacy link-table
        // replacement must succeed or fail together. They share the same DbContext, so one
        // explicit transaction wraps both SaveChanges calls.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        assignmentRepository.DetectChanges();
        await assignmentRepository.UpdateAsync(assignment, cancellationToken);
        await linkRepository.ReplaceForAssignmentAsync(command.AssignmentId, tenantId, requestedIds, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        await cache.RemoveByTagAsync("assignments", cancellationToken);

        logger.LogInformation(
            "Assignment {AssignmentId} linked to {Count} activity group(s)",
            command.AssignmentId, requestedIds.Length);
    }
}
