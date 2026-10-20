using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.GradeAssignmentPolicies.Commands.UpsertGradeAssignmentPolicy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeAssignmentPolicies.Commands.UpsertGradeAssignmentPolicy;

/// <summary>
/// Upserts the single assignment-policy override row for a grade. Rejects a
/// non-existent grade level to avoid orphan policy rows. The two contact caps are
/// validated here (owner decision Q7): a non-positive cap is rejected rather than
/// stored, because the recipient filter can only treat it as "uncapped" and a silent
/// no-op cap is worse than a rejected write. Null stays "inherit". The tenant query
/// filter scopes reads/writes to the caller's tenant (WS-C1 / spec §7 Q1).
/// </summary>
public sealed class UpsertGradeAssignmentPolicyHandler(
    StudentsDbContext db,
    ITenantProvider tenantProvider) : ICommandHandler<UpsertGradeAssignmentPolicy, GradeAssignmentPolicyDto>
{
    public async Task<GradeAssignmentPolicyDto> HandleAsync(
        UpsertGradeAssignmentPolicy command, CancellationToken ct = default)
    {
        GuardContactCaps(command.MaxPrimaryContacts, command.MaxCopyContacts);
        GuardApprovalWindow(command.ApprovalWindowDays);

        // Reject overrides for grades that don't exist in this tenant.
        var gradeExists = await db.GradeLevels.AnyAsync(x => x.Id == command.GradeLevelId, ct);
        if (!gradeExists)
        {
            throw new GradeLevelNotFoundException(command.GradeLevelId);
        }

        var tenantId = tenantProvider.GetTenantContext().TenantId;

        var existing = await db.GradeAssignmentPolicies
            .SingleOrDefaultAsync(x => x.GradeLevelId == command.GradeLevelId, ct);

        GradeAssignmentPolicy policy;
        if (existing is not null)
        {
            existing.SetOverride(
                command.SignatureRequirement,
                command.RequiresApprovalBeforePublish,
                command.MaxPrimaryContacts,
                command.MaxCopyContacts,
                command.MandatoryReview,
                command.ArchiveGraceDays,
                command.ApprovalWindowDays);
            policy = existing;
        }
        else
        {
            policy = GradeAssignmentPolicy.Create(
                tenantId,
                command.GradeLevelId,
                command.SignatureRequirement,
                command.RequiresApprovalBeforePublish,
                command.MaxPrimaryContacts,
                command.MaxCopyContacts,
                command.MandatoryReview,
                command.ArchiveGraceDays,
                command.ApprovalWindowDays);
            db.GradeAssignmentPolicies.Add(policy);
        }

        await db.SaveChangesAsync(ct);

        return new GradeAssignmentPolicyDto(
            policy.GradeLevelId,
            policy.SignatureRequirement,
            policy.RequiresApprovalBeforePublish,
            policy.MaxPrimaryContacts,
            policy.MaxCopyContacts,
            policy.MandatoryReview,
            policy.ArchiveGraceDays,
            policy.UpdatedAt,
            policy.ApprovalWindowDays);
    }

    /// <summary>
    /// Q7 range rule for the approval window (round assignment-approval-workflow / spec §3.3): a
    /// non-positive window is rejected rather than stored — a stored zero would mean "expires
    /// immediately" to a naive consumer, and every pending assignment would be returned on the next
    /// sweep tick.
    /// </summary>
    private static void GuardApprovalWindow(int? approvalWindowDays)
    {
        if (approvalWindowDays is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(UpsertGradeAssignmentPolicy.ApprovalWindowDays), approvalWindowDays,
                "The approval window must be a positive number of days (null = inherit).");
        }
    }

    /// <summary>
    /// Owner decision Q7: a contact cap must be positive when set. The Students API's
    /// assignment-policy PUT maps this to 400, mirroring the notification-policy PUT.
    /// </summary>
    private static void GuardContactCaps(int? maxPrimaryContacts, int? maxCopyContacts)
    {
        if (maxPrimaryContacts is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(UpsertGradeAssignmentPolicy.MaxPrimaryContacts), maxPrimaryContacts,
                "The maximum primary contacts must be a positive number (null = inherit).");
        }

        if (maxCopyContacts is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(UpsertGradeAssignmentPolicy.MaxCopyContacts), maxCopyContacts,
                "The maximum copy contacts must be a positive number (null = inherit).");
        }
    }
}
