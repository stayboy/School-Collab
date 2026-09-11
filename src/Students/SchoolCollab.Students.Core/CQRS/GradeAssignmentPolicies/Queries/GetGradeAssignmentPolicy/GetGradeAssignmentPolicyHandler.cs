using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.CQRS.GradeAssignmentPolicies.Queries.GetGradeAssignmentPolicy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeAssignmentPolicies.Queries.GetGradeAssignmentPolicy;

/// <summary>
/// Loads the grade's override row via the tenant query filter, returning the DTO
/// or <see langword="null"/> when the grade inherits the tenant default (WS-C1 /
/// spec §7 Q1).
/// </summary>
public sealed class GetGradeAssignmentPolicyHandler(StudentsDbContext db)
    : IQueryHandler<GetGradeAssignmentPolicy, GradeAssignmentPolicyDto?>
{
    public async Task<GradeAssignmentPolicyDto?> HandleAsync(
        GetGradeAssignmentPolicy query, CancellationToken ct = default)
    {
        var policy = await db.GradeAssignmentPolicies
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.GradeLevelId == query.GradeLevelId, ct);

        if (policy is null)
        {
            return null;
        }

        return new GradeAssignmentPolicyDto(
            policy.GradeLevelId,
            policy.RequiresSignatureDefault,
            policy.UpdatedAt);
    }
}
