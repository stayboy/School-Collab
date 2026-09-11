using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.CQRS.AssignmentPolicies.Queries.GetTenantAssignmentPolicy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.AssignmentPolicies.Queries.GetTenantAssignmentPolicy;

/// <summary>
/// Loads the current tenant's assignment-policy row via the tenant query filter,
/// returning the DTO or <see langword="null"/> when the tenant has not configured
/// one yet (WS-C1 / spec §7 Q1).
/// </summary>
public sealed class GetTenantAssignmentPolicyHandler(SettingsDbContext db)
    : IQueryHandler<GetTenantAssignmentPolicy, TenantAssignmentPolicyDto?>
{
    public async Task<TenantAssignmentPolicyDto?> HandleAsync(
        GetTenantAssignmentPolicy query, CancellationToken ct = default)
    {
        var policy = await db.TenantAssignmentPolicies
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);

        if (policy is null)
        {
            return null;
        }

        return new TenantAssignmentPolicyDto(policy.RequiresSignatureDefault);
    }
}
