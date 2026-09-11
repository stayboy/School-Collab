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
/// Upserts the single guardian-signature override row for a grade. Rejects a
/// non-existent grade level to avoid orphan policy rows. The tenant query filter
/// scopes reads/writes to the caller's tenant (WS-C1 / spec §7 Q1).
/// </summary>
public sealed class UpsertGradeAssignmentPolicyHandler(
    StudentsDbContext db,
    ITenantProvider tenantProvider) : ICommandHandler<UpsertGradeAssignmentPolicy, GradeAssignmentPolicyDto>
{
    public async Task<GradeAssignmentPolicyDto> HandleAsync(
        UpsertGradeAssignmentPolicy command, CancellationToken ct = default)
    {
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
            existing.SetOverride(command.RequiresSignatureDefault);
            policy = existing;
        }
        else
        {
            policy = GradeAssignmentPolicy.Create(
                tenantId, command.GradeLevelId, command.RequiresSignatureDefault);
            db.GradeAssignmentPolicies.Add(policy);
        }

        await db.SaveChangesAsync(ct);

        return new GradeAssignmentPolicyDto(
            policy.GradeLevelId,
            policy.RequiresSignatureDefault,
            policy.UpdatedAt);
    }
}
